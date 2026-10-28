using System;
using System.Globalization;

namespace DesktopDeck.Widgets;

/// <summary>Shared pure helpers for the Health widgets: clock times, "active hours" windows, whole-minute scheduling.</summary>
public static class HealthLogic
{
    private static readonly string[] TimeFormats = ["H:mm", "HH:mm", "H.mm", "h:mm tt", "h:mmtt", "h tt", "htt", "%H"];

    /// <summary>A time of day from "HH:mm", "6:30 pm" and similar; <paramref name="fallback"/> when it can't be read.</summary>
    public static TimeSpan ParseTime(string? text, TimeSpan fallback)
        => !string.IsNullOrWhiteSpace(text) && DateTime.TryParseExact(text.Trim(), TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed) ? parsed.TimeOfDay : fallback;

    /// <summary>"07:30" or "7:30 AM".</summary>
    public static string FormatClock(TimeSpan time, bool use24Hour, CultureInfo? culture = null)
    {
        var at = DateTime.Today.Add(time);
        return use24Hour ? at.ToString("HH:mm", CultureInfo.InvariantCulture) : at.ToString("h:mm tt", culture ?? CultureInfo.CurrentCulture);
    }

    /// <summary>The "clock" setting: "24", "12", or anything else (auto) to follow the Windows time format.</summary>
    public static bool Use24Hour(string? setting, CultureInfo? culture = null)
        => setting switch { "24" => true, "12" => false, _ => (culture ?? CultureInfo.CurrentCulture).DateTimeFormat.ShortTimePattern.Contains('H') };

    /// <summary>Rounds up to the next whole minute (a time already on the minute is unchanged).</summary>
    public static DateTime CeilMinute(DateTime time)
    {
        var floor = new DateTime(time.Year, time.Month, time.Day, time.Hour, time.Minute, 0, time.Kind);
        return time > floor ? floor.AddMinutes(1) : floor;
    }

    private static bool Weekend(DateTime day) => day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    /// <summary>
    /// Whether <paramref name="at"/> is inside the daily window from <paramref name="from"/> to <paramref name="to"/>
    /// (a window that ends before it starts runs past midnight; equal times mean all day). With
    /// <paramref name="weekdaysOnly"/> only windows that start Monday to Friday count.
    /// </summary>
    public static bool IsActive(DateTime at, TimeSpan from, TimeSpan to, bool weekdaysOnly)
    {
        var t = at.TimeOfDay;
        if (from == to) return !weekdaysOnly || !Weekend(at);
        if (from < to) return t >= from && t < to && (!weekdaysOnly || !Weekend(at));
        // Past midnight: [from, 24h) belongs to today, [0, to) to the day before.
        if (t >= from) return !weekdaysOnly || !Weekend(at);
        return t < to && (!weekdaysOnly || !Weekend(at.AddDays(-1)));
    }

    /// <summary>
    /// The first moment at or after <paramref name="after"/> that is inside the window: the same moment when it
    /// already is, otherwise the next start of the window.
    /// </summary>
    public static DateTime NextActive(DateTime after, TimeSpan from, TimeSpan to, bool weekdaysOnly)
    {
        if (IsActive(after, from, to, weekdaysOnly)) return after;
        for (int i = 0; i <= 9; i++)
        {
            var day = after.Date.AddDays(i);
            if (weekdaysOnly && Weekend(day)) continue;
            var start = day + from;
            if (start >= after) return start;
        }
        return after;
    }

    /// <summary>
    /// When the next reminder falls after <paramref name="from"/>: one interval later, on a whole minute. With
    /// active hours, a time outside the window moves to the start of the next window plus one interval.
    /// </summary>
    public static DateTime ScheduleAfter(DateTime from, TimeSpan interval, bool activeOnly, TimeSpan windowFrom, TimeSpan windowTo, bool weekdaysOnly)
    {
        if (interval < TimeSpan.FromMinutes(1)) interval = TimeSpan.FromMinutes(1);
        var candidate = CeilMinute(from + interval);
        if (!activeOnly) return candidate;
        for (int i = 0; i < 8 && !IsActive(candidate, windowFrom, windowTo, weekdaysOnly); i++)
        {
            var start = NextActive(candidate, windowFrom, windowTo, weekdaysOnly);
            candidate = CeilMinute(start + interval);
        }
        return candidate;
    }

    /// <summary>"1 h 05 m", "12 m", "0:34" (seconds when under a minute and <paramref name="seconds"/> is true; else "1 m").</summary>
    public static string FormatSpan(TimeSpan span, bool seconds = false)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        if (seconds && span < TimeSpan.FromMinutes(10)) return $"{(int)span.TotalMinutes}:{span.Seconds:00}";
        int minutes = (int)Math.Ceiling(span.TotalMinutes - 1e-9);
        if (minutes >= 60) return $"{minutes / 60} h {minutes % 60:00} m";
        return Math.Max(minutes, span > TimeSpan.Zero ? 1 : 0) + " m";
    }

    /// <summary>A moment as text that survives saving: local time without offset.</summary>
    public static string FormatStamp(DateTime time) => time.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

    public static DateTime? ParseStamp(string? text)
        => DateTime.TryParseExact(text, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at : null;

    public static string DayKey(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
