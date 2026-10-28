using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DesktopDeck.Widgets;

/// <summary>
/// Stopwatch state kept in the item's options so it survives restarts: when it was started (null while paused),
/// the time gathered before that, and the lap splits. Pure logic (unit tested).
/// </summary>
public static class StopwatchLogic
{
    public const string StartedKey = "data.startedAt", AccumulatedKey = "data.accumulatedMs", LapsKey = "data.laps", NotifiedKey = "data.notified";
    /// <summary>Laps kept at most (older ones are dropped).</summary>
    public const int MaxStoredLaps = 200;

    /// <summary>A stopwatch: <see cref="Laps"/> are cumulative split times in milliseconds, oldest first.</summary>
    public sealed record State(long AccumulatedMs, DateTimeOffset? StartedAt, IReadOnlyList<long> Laps)
    {
        public bool Running => StartedAt is not null;
    }

    public static readonly State Empty = new(0, null, []);

    public static State Read(IReadOnlyDictionary<string, string> options)
    {
        long accumulated = options.TryGetValue(AccumulatedKey, out var a) && long.TryParse(a, NumberStyles.Integer, CultureInfo.InvariantCulture, out var acc) ? Math.Max(0, acc) : 0;
        DateTimeOffset? started = options.TryGetValue(StartedKey, out var s) && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms) && ms >= -62135596800000L && ms <= 253402300799999L
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null;
        var laps = options.TryGetValue(LapsKey, out var l)
            ? l.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => long.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : -1).Where(v => v >= 0).ToList()
            : [];
        return new State(accumulated, started, laps);
    }

    public static void Write(IDictionary<string, string> options, State state)
    {
        options[AccumulatedKey] = state.AccumulatedMs.ToString(CultureInfo.InvariantCulture);
        options[StartedKey] = state.StartedAt is { } s ? s.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) : "";
        options[LapsKey] = string.Join(",", state.Laps.Select(v => v.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>Total time on the stopwatch at <paramref name="now"/> (never negative, even if the clock went back).</summary>
    public static long Elapsed(State state, DateTimeOffset now)
        => state.AccumulatedMs + (state.StartedAt is { } s ? Math.Max(0, (long)(now - s).TotalMilliseconds) : 0);

    public static State Start(State state, DateTimeOffset now) => state.Running ? state : state with { StartedAt = now };
    public static State Pause(State state, DateTimeOffset now) => state.Running ? state with { AccumulatedMs = Elapsed(state, now), StartedAt = null } : state;
    public static State Reset() => Empty;

    public static State Lap(State state, DateTimeOffset now)
    {
        var laps = state.Laps.Append(Elapsed(state, now)).ToList();
        if (laps.Count > MaxStoredLaps) laps.RemoveRange(0, laps.Count - MaxStoredLaps);
        return state with { Laps = laps };
    }

    /// <summary>Each lap's own duration and its cumulative split, oldest first.</summary>
    public static IReadOnlyList<(long Lap, long Split)> LapTimes(State state)
    {
        var result = new List<(long, long)>();
        long previous = 0;
        foreach (long split in state.Laps)
        {
            result.Add((Math.Max(0, split - previous), split));
            previous = split;
        }
        return result;
    }

    /// <summary>Indices of the fastest and slowest lap (-1 when there are fewer than two laps or they're all equal).</summary>
    public static (int Best, int Worst) BestWorst(IReadOnlyList<(long Lap, long Split)> laps)
    {
        if (laps.Count < 2) return (-1, -1);
        int best = 0, worst = 0;
        for (int i = 1; i < laps.Count; i++)
        {
            if (laps[i].Lap < laps[best].Lap) best = i;
            if (laps[i].Lap > laps[worst].Lap) worst = i;
        }
        return laps[best].Lap == laps[worst].Lap ? (-1, -1) : (best, worst);
    }

    /// <summary>
    /// "05:07" / "1:05:07" plus a fraction ".3" or ".34" for <paramref name="precision"/> "tenths" / "hundredths".
    /// Digits are truncated, as on a real stopwatch.
    /// </summary>
    public static (string Main, string Fraction) Format(long ms, string precision, bool forceHours = false)
    {
        ms = Math.Max(0, ms);
        long totalSeconds = ms / 1000;
        long hours = totalSeconds / 3600;
        int minutes = (int)(totalSeconds / 60 % 60), seconds = (int)(totalSeconds % 60);
        string main = hours > 0 || forceHours
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes:00}:{seconds:00}");
        string sep = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        string fraction = precision switch
        {
            "tenths" => sep + (ms % 1000 / 100).ToString(CultureInfo.InvariantCulture),
            "hundredths" => sep + (ms % 1000 / 10).ToString("00", CultureInfo.InvariantCulture),
            _ => "",
        };
        return (main, fraction);
    }

    public static string FormatText(long ms, string precision, bool forceHours = false)
    {
        var (main, fraction) = Format(ms, precision, forceHours);
        return main + fraction;
    }
}

