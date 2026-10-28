using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace DesktopDeck.Widgets;

/// <summary>Today's focus: the statement, its done flag, the midnight rollover and the short history. Pure logic (unit tested).</summary>
public static class FocusLogic
{
    /// <summary>A past day's focus.</summary>
    public sealed class FocusDay
    {
        public string Date { get; set; } = "";
        public string Text { get; set; } = "";
        public bool Done { get; set; }
    }

    /// <summary>What is saved in Item.Options["data.focus"].</summary>
    public sealed class FocusState
    {
        public string Text { get; set; } = "";
        public string Subtitle { get; set; } = "";
        /// <summary>The day (yyyy-MM-dd) the focus was set.</summary>
        public string Date { get; set; } = "";
        public bool Done { get; set; }
        /// <summary>Newest first, the days kept by Settings → History (<see cref="HistoryLength"/> by default).</summary>
        public List<FocusDay> History { get; set; } = new();
    }

    /// <summary>The default days of past focus kept (Settings → History). 0 in a call means keep forever.</summary>
    public const int HistoryLength = 7;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string Key(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static bool TryDate(string key, out DateTime date) => DateTime.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    public static FocusState Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new FocusState();
        try
        {
            var state = JsonSerializer.Deserialize<FocusState>(json, Json) ?? new FocusState();
            state.Text ??= "";
            state.Subtitle ??= "";
            state.Date ??= "";
            state.History = (state.History ?? new()).Where(h => h is not null && !string.IsNullOrWhiteSpace(h.Text)).ToList();
            return state;
        }
        catch (JsonException) { return new FocusState(); }
    }

    public static string Serialize(FocusState state) => JsonSerializer.Serialize(state, Json);

    /// <summary>
    /// Puts the current focus into the history (one entry per day, newest first). Entries older than <paramref name="keepDays"/> days
    /// before <paramref name="today"/> are dropped; 0 keeps them all.
    /// </summary>
    public static void Archive(FocusState state, DateTime today, int keepDays = HistoryLength)
    {
        if (string.IsNullOrWhiteSpace(state.Text)) return;
        state.History.RemoveAll(h => h.Date == state.Date);
        state.History.Insert(0, new FocusDay { Date = state.Date, Text = state.Text, Done = state.Done });
        string oldest = Key(today.Date.AddDays(-keepDays));
        state.History = state.History.Where(h => keepDays <= 0 || string.CompareOrdinal(h.Date, oldest) >= 0).OrderByDescending(h => h.Date, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// A new day started: with <paramref name="autoClear"/> an old focus moves to the history and today starts empty.
    /// Without it the focus carries over. Returns true if anything changed.
    /// </summary>
    public static bool Rollover(FocusState state, DateTime today, bool autoClear, int keepDays = HistoryLength)
    {
        if (!autoClear || string.IsNullOrWhiteSpace(state.Text) || state.Date == Key(today)) return false;
        Archive(state, today, keepDays);
        state.Text = "";
        state.Subtitle = "";
        state.Done = false;
        state.Date = "";
        return true;
    }

    /// <summary>Sets today's focus. A focus from an earlier day is archived first; a new statement starts not done.</summary>
    public static void SetFocus(FocusState state, string text, DateTime today, int keepDays = HistoryLength)
    {
        text = text.Trim(); if (text.Length > 300) text = text[..300];
        string key = Key(today);
        if (!string.IsNullOrWhiteSpace(state.Text) && state.Date != key && state.Date.Length > 0)
        {
            Archive(state, today, keepDays);
            state.Subtitle = "";
        }
        if (text != state.Text) state.Done = false;
        state.Text = text;
        state.Date = key;
    }

    /// <summary>Clears today's focus without archiving it (for typos and changes of plan).</summary>
    public static void Clear(FocusState state)
    {
        state.Text = "";
        state.Subtitle = "";
        state.Done = false;
        state.Date = "";
    }

    /// <summary>Past focuses, newest first, never including the current day's.</summary>
    public static List<FocusDay> Recent(FocusState state, int count) =>
        state.History.Where(h => h.Date != state.Date || string.IsNullOrWhiteSpace(state.Text)).OrderByDescending(h => h.Date, StringComparer.Ordinal).Take(Math.Max(0, count)).ToList();

    /// <summary>The statement as shown: "quotes" wraps it in curly quotes.</summary>
    public static string Quote(string text, string style) => style == "quotes" && text.Length > 0 ? "“" + text + "”" : text;

    /// <summary>How many days in a row (ending today or yesterday) had a focus that was marked done.</summary>
    public static int DoneStreak(FocusState state, DateTime today)
    {
        var done = new HashSet<string>(state.History.Where(h => h.Done).Select(h => h.Date), StringComparer.Ordinal);
        if (state.Done && state.Date.Length > 0) done.Add(state.Date);
        var day = today.Date;
        if (!done.Contains(Key(day))) day = day.AddDays(-1);
        int streak = 0;
        while (done.Contains(Key(day))) { streak++; day = day.AddDays(-1); }
        return streak;
    }
}

