using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace DesktopDeck.Widgets;

/// <summary>Routine checklist: the daily reset at a chosen time, active hours and the streak of fully completed days. Pure logic (unit tested).</summary>
public static class RoutineLogic
{
    /// <summary>What is saved in Item.Options["data.routine"].</summary>
    public sealed class RoutineState
    {
        /// <summary>The routine day (yyyy-MM-dd) the ticks in <see cref="Done"/> belong to.</summary>
        public string Day { get; set; } = "";
        /// <summary>Ticked steps, by their text.</summary>
        public List<string> Done { get; set; } = new();
        /// <summary>Routine days on which every step was ticked, oldest first.</summary>
        public List<string> Completed { get; set; } = new();
    }

    /// <summary>One step from the Lines option: "Stretch | 5 min" gives a title and a detail.</summary>
    public readonly record struct RoutineStep(string Title, string Detail);

    public const int HistoryDays = 400;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string Key(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static RoutineState Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new RoutineState();
        try
        {
            var state = JsonSerializer.Deserialize<RoutineState>(json, Json) ?? new RoutineState();
            state.Day ??= "";
            state.Done = (state.Done ?? new()).Where(s => !string.IsNullOrEmpty(s)).ToList();
            state.Completed = (state.Completed ?? new()).Where(s => !string.IsNullOrEmpty(s)).ToList();
            return state;
        }
        catch (JsonException) { return new RoutineState(); }
    }

    public static string Serialize(RoutineState state) => JsonSerializer.Serialize(state, Json);

    public static RoutineStep ParseStep(string line)
    {
        int bar = line.IndexOf('|');
        return bar < 0 ? new RoutineStep(line.Trim(), "") : new RoutineStep(line[..bar].Trim(), line[(bar + 1)..].Trim());
    }

    /// <summary>The steps from the Lines option, without blanks or repeated titles.</summary>
    public static List<RoutineStep> Steps(IEnumerable<string> lines)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return lines.Take(100).Select(ParseStep).Where(s => s.Title.Length > 0 && seen.Add(s.Title)).ToList();
    }

    /// <summary>"04:00", "6:30" → a time of day; anything else gives <paramref name="fallback"/>.</summary>
    public static TimeSpan ParseTime(string? text, TimeSpan fallback)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        return TimeSpan.TryParseExact(text.Trim(), ["hh\\:mm", "h\\:mm", "hh\\:mm\\:ss"], CultureInfo.InvariantCulture, out var t) && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1) ? t : fallback;
    }

    /// <summary>The routine day <paramref name="now"/> belongs to: before the reset time it is still yesterday's routine.</summary>
    public static DateTime RoutineDay(DateTime now, TimeSpan reset) => now.TimeOfDay < reset ? now.Date.AddDays(-1) : now.Date;

    /// <summary>Clears the ticks when a new routine day has started. Returns true if it did.</summary>
    public static bool Normalize(RoutineState state, DateTime now, TimeSpan reset)
    {
        string key = Key(RoutineDay(now, reset));
        if (state.Day == key) return false;
        state.Day = key;
        state.Done.Clear();
        return true;
    }

    public static bool IsDone(RoutineState state, string title) => state.Done.Contains(title, StringComparer.Ordinal);

    public static int CountDone(RoutineState state, IReadOnlyList<RoutineStep> steps) => steps.Count(s => IsDone(state, s.Title));

    public static bool IsComplete(RoutineState state, IReadOnlyList<RoutineStep> steps) => steps.Count > 0 && CountDone(state, steps) == steps.Count;

    /// <summary>Ticks or unticks a step and records whether the routine day is fully complete.</summary>
    public static void Toggle(RoutineState state, string title, IReadOnlyList<RoutineStep> steps, DateTime now, TimeSpan reset)
    {
        Normalize(state, now, reset);
        if (!state.Done.Remove(title)) state.Done.Add(title);
        Record(state, steps);
    }

    /// <summary>Ticks every step (or none).</summary>
    public static void SetAll(RoutineState state, bool done, IReadOnlyList<RoutineStep> steps, DateTime now, TimeSpan reset)
    {
        Normalize(state, now, reset);
        state.Done = done ? steps.Select(s => s.Title).ToList() : new();
        Record(state, steps);
    }

    private static void Record(RoutineState state, IReadOnlyList<RoutineStep> steps)
    {
        state.Completed.Remove(state.Day);
        if (IsComplete(state, steps)) state.Completed.Add(state.Day);
        state.Completed = state.Completed.Distinct(StringComparer.Ordinal).OrderBy(d => d, StringComparer.Ordinal).TakeLast(HistoryDays).ToList();
    }

    /// <summary>Fully completed days in a row, ending on the current routine day (or the day before if today isn't finished yet).</summary>
    public static int Streak(RoutineState state, DateTime routineDay)
    {
        var done = new HashSet<string>(state.Completed, StringComparer.Ordinal);
        var day = routineDay.Date;
        if (!done.Contains(Key(day))) day = day.AddDays(-1);
        int streak = 0;
        while (done.Contains(Key(day))) { streak++; day = day.AddDays(-1); }
        return streak;
    }

    /// <summary>The longest run of completed days on record.</summary>
    public static int Best(RoutineState state)
    {
        int best = 0, run = 0;
        DateTime? previous = null;
        foreach (var key in state.Completed.OrderBy(d => d, StringComparer.Ordinal))
        {
            if (!DateTime.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) continue;
            run = previous is { } p && (day - p).TotalDays == 1 ? run + 1 : 1;
            best = Math.Max(best, run);
            previous = day;
        }
        return best;
    }

    /// <summary>Whether <paramref name="time"/> falls in the active window [from, to); windows may cross midnight. from == to means always.</summary>
    public static bool IsActive(TimeSpan time, TimeSpan from, TimeSpan to)
    {
        if (from == to) return true;
        return from < to ? time >= from && time < to : time >= from || time < to;
    }
}

