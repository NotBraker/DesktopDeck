using System;

namespace DesktopDeck.Widgets;

/// <summary>How far through the day, week, month or year we are. Pure logic (unit tested).</summary>
public static class DayProgressLogic
{
    /// <summary>The start and end of the period containing <paramref name="now"/>.</summary>
    public static (DateTime Start, DateTime End) Period(string period, DateTime now, DayOfWeek weekStart = DayOfWeek.Monday)
    {
        var day = now.Date;
        switch (period)
        {
            case "week":
                int back = ((int)day.DayOfWeek - (int)weekStart + 7) % 7;
                var start = day.AddDays(-back);
                return (start, start.AddDays(7));
            case "month":
                var month = new DateTime(day.Year, day.Month, 1);
                return (month, month.AddMonths(1));
            case "year":
                var year = new DateTime(day.Year, 1, 1);
                return (year, year.AddYears(1));
            default:
                return (day, day.AddDays(1));
        }
    }

    /// <summary>0–1 through the period.</summary>
    public static double Fraction(string period, DateTime now, DayOfWeek weekStart = DayOfWeek.Monday)
    {
        var (start, end) = Period(period, now, weekStart);
        return Math.Clamp((now - start).TotalSeconds / (end - start).TotalSeconds, 0, 1);
    }

    /// <summary>"6 h 20 m left", "3 days left".</summary>
    public static string Remaining(string period, DateTime now, DayOfWeek weekStart = DayOfWeek.Monday)
    {
        var left = Period(period, now, weekStart).End - now;
        if (left.TotalDays >= 2) return $"{(int)left.TotalDays} days left";
        if (left.TotalHours >= 1) return $"{(int)left.TotalHours} h {left.Minutes} m left";
        return $"{Math.Max(1, (int)Math.Ceiling(left.TotalMinutes))} m left";
    }
}
