using System.Globalization;

namespace DesktopDeck.Models;

/// <summary>What a countdown shows right now: a big number, its unit, and a line about the date.</summary>
public sealed record CountdownView(string Number, string Unit, string Caption, DateTime Target, bool Past);

public static class Countdown
{
    /// <summary>The moment the countdown aims at, or null if the date is missing or invalid.</summary>
    public static DateTime? Target(string date, string time)
    {
        if (!DateOnly.TryParseExact(date?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) return null;
        var clock = Planner.Minutes(Planner.ParseTime(time ?? "") ?? "") is { } minutes ? TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minutes)) : TimeOnly.MinValue;
        return day.ToDateTime(clock);
    }

    /// <summary>
    /// Days (or hours and minutes on the last day) until the target. A yearly countdown moves to next year's date
    /// the day after it passes; on the day itself it shows "Today!".
    /// </summary>
    public static CountdownView? Describe(DateTime now, string date, string time, bool yearly)
    {
        if (Target(date, time) is not { } target) return null;
        bool hasTime = !string.IsNullOrWhiteSpace(time) && Planner.ParseTime(time) is not null;
        if (yearly)
        {
            while (target.Date < now.Date) target = SafeAddYears(target, 1);
            // A yearly event already past today stays "Today!" until midnight.
        }
        string caption = target.ToString(hasTime ? "dddd, MMMM d, yyyy · h:mm tt" : "dddd, MMMM d, yyyy", CultureInfo.CurrentCulture);
        if (target.Date == now.Date && (!hasTime || target <= now)) return new CountdownView("Today!", "", caption, target, false);
        if (target < now)
        {
            int ago = (now.Date - target.Date).Days;
            return new CountdownView(ago.ToString(CultureInfo.CurrentCulture), ago == 1 ? "day ago" : "days ago", caption, target, true);
        }
        var left = target - now;
        int days = (target.Date - now.Date).Days;
        if (hasTime && left.TotalHours < 24)
        {
            int hours = (int)left.TotalHours, minutes = Math.Max(1, (int)Math.Ceiling(left.TotalMinutes) - hours * 60);
            if (minutes == 60) { hours++; minutes = 0; }
            return hours > 0
                ? new CountdownView($"{hours}h {minutes}m", "to go", caption, target, false)
                : new CountdownView(minutes.ToString(CultureInfo.CurrentCulture), minutes == 1 ? "minute to go" : "minutes to go", caption, target, false);
        }
        return new CountdownView(days.ToString(CultureInfo.CurrentCulture), days == 1 ? "day to go" : "days to go", caption, target, false);
    }

    private static DateTime SafeAddYears(DateTime value, int years)
    {
        // Feb 29 becomes Feb 28 in years without it.
        try { return value.AddYears(years); }
        catch (ArgumentOutOfRangeException) { return value.AddDays(-1).AddYears(years); }
    }
}
