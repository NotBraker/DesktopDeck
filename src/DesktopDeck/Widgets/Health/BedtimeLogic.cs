using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DesktopDeck.Widgets;

public enum BedtimePhase { Day, WindDown, Asleep }

/// <summary>One night: when to go to bed, when to wake, and where "now" sits in it.</summary>
public sealed record BedtimeNight(DateTime WakeDate, DateTime Bed, DateTime Wake, DateTime WindDownAt, BedtimePhase Phase)
{
    /// <summary>Identifies the night in saved markers (the bedtime).</summary>
    public string Key => HealthLogic.FormatStamp(Bed);
}

/// <summary>Pure parts of the bedtime widget: nights across midnight, wind-down timing, 90-minute sleep cycles, ratings.</summary>
public static class BedtimeLogic
{
    public const double CycleMinutes = 90;
    public const int KeepDays = 35;

    // ---------- Schedule ----------

    /// <summary>Hours between going to bed and waking, across midnight when needed (equal times mean 24 h).</summary>
    public static TimeSpan SleepDuration(TimeSpan bed, TimeSpan wake)
    {
        var d = wake - bed;
        if (d <= TimeSpan.Zero) d += TimeSpan.FromHours(24);
        return d;
    }

    /// <summary>The bed and wake times for the night that ends on <paramref name="wakeDate"/>; Saturday and Sunday mornings use the weekend times when asked.</summary>
    public static (TimeSpan Bed, TimeSpan Wake) PlanFor(DateTime wakeDate, TimeSpan bed, TimeSpan wake, bool separateWeekend, TimeSpan weekendBed, TimeSpan weekendWake)
        => separateWeekend && wakeDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? (weekendBed, weekendWake) : (bed, wake);

    /// <summary>The night that ends on <paramref name="wakeDate"/>, with the phase at <paramref name="now"/>.</summary>
    public static BedtimeNight Night(DateTime wakeDate, DateTime now, TimeSpan bed, TimeSpan wake, bool separateWeekend, TimeSpan weekendBed, TimeSpan weekendWake, int windDownMinutes)
    {
        var (b, w) = PlanFor(wakeDate, bed, wake, separateWeekend, weekendBed, weekendWake);
        var wakeAt = wakeDate.Date + w;
        var bedAt = wakeAt - SleepDuration(b, w);
        var wind = bedAt.AddMinutes(-Math.Max(0, windDownMinutes));
        var phase = now >= bedAt ? BedtimePhase.Asleep : now >= wind ? BedtimePhase.WindDown : BedtimePhase.Day;
        return new BedtimeNight(wakeDate.Date, bedAt, wakeAt, wind, phase);
    }

    /// <summary>The night we are in or heading for: the first whose wake-up is still ahead.</summary>
    public static BedtimeNight Current(DateTime now, TimeSpan bed, TimeSpan wake, bool separateWeekend, TimeSpan weekendBed, TimeSpan weekendWake, int windDownMinutes)
    {
        BedtimeNight? last = null;
        for (int i = 0; i <= 2; i++)
        {
            last = Night(now.Date.AddDays(i), now, bed, wake, separateWeekend, weekendBed, weekendWake, windDownMinutes);
            if (last.Wake > now) return last;
        }
        return last!;
    }

    /// <summary>
    /// Which notices are due now, given the markers saved after earlier ones: the wind-down once per night, and
    /// bedtime once per night if it is still within <paramref name="grace"/> of bedtime. Passing a stage without
    /// notice (the deck was closed) marks it so it is never shown late.
    /// </summary>
    public static (bool WindDown, bool Bedtime, string WindMarker, string BedMarker) Due(BedtimeNight night, DateTime now, string? windMarker, string? bedMarker, TimeSpan? grace = null)
    {
        grace ??= TimeSpan.FromMinutes(15);
        bool wind = false, bed = false;
        string wm = windMarker ?? "", bm = bedMarker ?? "";
        if (night.Phase == BedtimePhase.WindDown && wm != night.Key) { wind = true; wm = night.Key; }
        if (night.Phase == BedtimePhase.Asleep)
        {
            wm = night.Key;
            if (bm != night.Key) { bed = now - night.Bed <= grace; bm = night.Key; }
        }
        return (wind, bed, wm, bm);
    }

    // ---------- Cycles ----------

    /// <summary>Whole-and-part 90-minute cycles in a night, after the time it takes to fall asleep.</summary>
    public static double Cycles(TimeSpan sleep, int fallAsleepMinutes) => Math.Max(0, (sleep.TotalMinutes - fallAsleepMinutes) / CycleMinutes);

    public static string CycleText(double cycles, CultureInfo? culture = null) => (Math.Round(cycles * 10) / 10).ToString("0.#", culture ?? CultureInfo.CurrentCulture);

    /// <summary>Bedtimes that let you wake at <paramref name="wake"/> between sleep cycles: 6, 5 and 4 cycles by default (as a time of day).</summary>
    public static List<(int Cycles, TimeSpan Bed)> BedtimesFor(TimeSpan wake, int fallAsleepMinutes, params int[] cycles)
    {
        if (cycles.Length == 0) cycles = [6, 5, 4];
        return cycles.Select(c => (c, Wrap(wake - TimeSpan.FromMinutes(c * CycleMinutes + fallAsleepMinutes)))).ToList();
    }

    /// <summary>Wake times if you fall asleep after <paramref name="fallAsleepMinutes"/> from <paramref name="now"/>: 4, 5 and 6 cycles by default.</summary>
    public static List<(int Cycles, DateTime At)> WakeTimesFrom(DateTime now, int fallAsleepMinutes, params int[] cycles)
    {
        if (cycles.Length == 0) cycles = [4, 5, 6];
        return cycles.Select(c => (c, now.AddMinutes(fallAsleepMinutes + c * CycleMinutes))).ToList();
    }

    private static TimeSpan Wrap(TimeSpan t)
    {
        double minutes = t.TotalMinutes % 1440;
        if (minutes < 0) minutes += 1440;
        return TimeSpan.FromMinutes(Math.Round(minutes));
    }

    // ---------- Ratings ("yyyy-MM-dd|1-5" per line, keyed by the wake-up date) ----------

    public static Dictionary<string, int> ParseRatings(string? text)
    {
        var ratings = new Dictionary<string, int>();
        if (string.IsNullOrEmpty(text)) return ratings;
        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split('|');
            if (parts.Length == 2 && DateTime.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n is >= 1 and <= 5) ratings[parts[0]] = n;
        }
        return ratings;
    }

    /// <summary>The ratings as text, oldest first, without days older than <paramref name="keepDays"/> before <paramref name="today"/> (0 = keep all).</summary>
    public static string FormatRatings(Dictionary<string, int> ratings, DateTime today, int keepDays = KeepDays)
    {
        string oldest = HealthLogic.DayKey(today.AddDays(-keepDays));
        var sb = new StringBuilder();
        foreach (var (day, n) in ratings.OrderBy(e => e.Key, StringComparer.Ordinal))
            if ((keepDays <= 0 || string.CompareOrdinal(day, oldest) >= 0) && n is >= 1 and <= 5) sb.Append(day).Append('|').Append(n.ToString(CultureInfo.InvariantCulture)).Append('\n');
        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>Sets a rating; choosing the same rating again clears it. Returns the new value (0 = none).</summary>
    public static int Toggle(Dictionary<string, int> ratings, DateTime wakeDate, int rating)
    {
        string key = HealthLogic.DayKey(wakeDate);
        if (rating is < 1 or > 5 || ratings.TryGetValue(key, out int current) && current == rating) { ratings.Remove(key); return 0; }
        ratings[key] = rating;
        return rating;
    }

    /// <summary>The last <paramref name="days"/> mornings ending with <paramref name="lastWake"/>, oldest first (0 = not logged).</summary>
    public static List<(DateTime Day, int Rating)> Recent(Dictionary<string, int> ratings, DateTime lastWake, int days = 7)
        => Enumerable.Range(0, days).Select(i => lastWake.Date.AddDays(i - days + 1)).Select(d => (d, ratings.GetValueOrDefault(HealthLogic.DayKey(d)))).ToList();

    public static double Average(IEnumerable<(DateTime Day, int Rating)> days)
    {
        var rated = days.Where(d => d.Rating > 0).ToList();
        return rated.Count == 0 ? 0 : rated.Average(d => d.Rating);
    }

    /// <summary>Whether to ask "how did you sleep?": today's wake-up has passed and nothing is logged for it.</summary>
    public static bool ShouldAskRating(DateTime now, DateTime todaysWake, Dictionary<string, int> ratings)
        => now >= todaysWake && !ratings.ContainsKey(HealthLogic.DayKey(now));

    public static string RatingWord(int rating) => rating switch { 1 => "Terrible", 2 => "Poor", 3 => "Okay", 4 => "Good", 5 => "Great", _ => "" };

    public static string RatingColor(int rating) => rating switch { 1 => "#ef4444", 2 => "#f97316", 3 => "#eab308", 4 => "#84cc16", 5 => "#22c55e", _ => "#5C5446" };
}
