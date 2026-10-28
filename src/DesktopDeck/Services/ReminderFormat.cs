using System.Globalization;
using DesktopDeck.Models;

namespace DesktopDeck.Services;

/// <summary>Wording for reminders: when, how long from now, how they repeat, and text the parser reads back. Pure and culture-independent.</summary>
public static class ReminderFormat
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly string[] DayNames = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    /// <summary>"5:00 PM" or "17:00".</summary>
    public static string Time(DateTime wall, bool use24Hour) => wall.ToString(use24Hour ? "HH:mm" : "h:mm tt", Inv);

    public static string Time(TimeSpan time, bool use24Hour) => Time(DateTime.MinValue + time, use24Hour);

    /// <summary>"Today 5:00 PM", "Tomorrow 8:00 AM", "Wed 9:00 AM" (this week), "Mon, Oct 12 9:00 AM", "Oct 12, 2027 9:00 AM".</summary>
    public static string When(DateTimeOffset at, DateTimeOffset now, TimeZoneInfo zone, bool use24Hour, string joiner = " ")
    {
        var wall = ReminderSchedule.Wall(at, zone);
        var today = ReminderSchedule.Wall(now, zone).Date;
        return Day(wall.Date, today) + joiner + Time(wall, use24Hour);
    }

    /// <summary>"Today", "Tomorrow", "Wed" within a week, "Mon, Oct 12", "Oct 12, 2027".</summary>
    public static string Day(DateTime day, DateTime today)
    {
        int days = (int)(day.Date - today.Date).TotalDays;
        if (days == 0) return "Today";
        if (days == 1) return "Tomorrow";
        if (days == -1) return "Yesterday";
        if (days is > 1 and < 7) return day.ToString("ddd", Inv);
        return day.Year == today.Year ? day.ToString("ddd, MMM d", Inv) : day.ToString("MMM d, yyyy", Inv);
    }

    /// <summary>"in 20 min", "in 2 h", "in 3 days", "5 min ago", "now".</summary>
    public static string Relative(DateTimeOffset at, DateTimeOffset now)
    {
        var delta = at - now;
        var span = delta.Duration();
        if (span < TimeSpan.FromSeconds(45)) return "now";
        string text = span < TimeSpan.FromMinutes(90) ? $"{Math.Max(1, (int)Math.Round(span.TotalMinutes))} min"
            : span < TimeSpan.FromHours(36) ? $"{(int)Math.Round(span.TotalHours)} h"
            : $"{(int)Math.Round(span.TotalDays)} days";
        return delta > TimeSpan.Zero ? "in " + text : text + " ago";
    }

    public static string Ordinal(int n) => n + ((n % 100) is >= 11 and <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

    /// <summary>"Every day", "Every weekday", "Every Mon, Wed and Fri", "Every month on the 31st", "Every 2 hours".</summary>
    public static string RepeatText(ReminderRepeat r)
    {
        switch (r.Kind)
        {
            case RepeatKind.Daily: return "Every day";
            case RepeatKind.Weekdays: return "Every weekday";
            case RepeatKind.Weekly:
            {
                var days = r.Days.OrderBy(d => ((int)d + 6) % 7).ToList();
                if (days.Count == 0) return "Every week";
                if (days.Count == 2 && days.Contains(DayOfWeek.Saturday) && days.Contains(DayOfWeek.Sunday)) return "Every weekend";
                if (days.Count == 1) return "Every " + DayNames[(int)days[0]];
                var names = days.Select(d => DayNames[(int)d][..3]).ToList();
                return "Every " + string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];
            }
            case RepeatKind.Monthly: return r.DayOfMonth > 0 ? $"Every month on the {Ordinal(r.DayOfMonth)}" : "Every month";
            case RepeatKind.Interval:
            {
                string unit = r.Unit switch { IntervalUnit.Minutes => "minute", IntervalUnit.Hours => "hour", IntervalUnit.Days => "day", _ => "week" };
                return r.Every == 1 ? "Every " + unit : $"Every {r.Every} {unit}s";
            }
            default: return "";
        }
    }

    /// <summary>The repeat with its time of day, e.g. "Every Monday at 7:30 AM".</summary>
    public static string RepeatWithTime(ReminderRepeat r, bool use24Hour) =>
        RepeatText(r) + (r.Time is { } t && !(r.Kind == RepeatKind.Interval && r.Unit is IntervalUnit.Minutes or IntervalUnit.Hours) ? " at " + Time(t, use24Hour) : "");

    /// <summary>
    /// The reminder as a sentence the parser reads back to the same reminder (for editing), e.g. "Call mom tomorrow at 5:00 pm" or
    /// "Trash every Monday at 7:30 am". An every-N-hours reminder keeps its rule, not its start.
    /// </summary>
    public static string ToNatural(Reminder r, DateTimeOffset now, TimeZoneInfo zone)
    {
        string when = WhenNatural(r, now, zone);
        string note = r.Note.Length > 0 ? " note: " + r.Note : "";
        return $"{r.Text} {when}{note}".Trim();
    }

    /// <summary>Just the time part of <see cref="ToNatural"/>.</summary>
    public static string WhenNatural(Reminder r, DateTimeOffset now, TimeZoneInfo zone)
    {
        var wall = ReminderSchedule.Wall(r.Due, zone);
        string at = " at " + wall.ToString("h:mm tt", Inv).ToLowerInvariant();
        var rep = r.Repeat;
        string tod = " at " + DateTime.MinValue.Add(rep.Time ?? wall.TimeOfDay).ToString("h:mm tt", Inv).ToLowerInvariant();
        switch (rep.Kind)
        {
            case RepeatKind.Daily: return "every day" + tod;
            case RepeatKind.Weekdays: return "every weekday" + tod;
            case RepeatKind.Weekly:
                var days = (rep.Days.Count > 0 ? rep.Days : new List<DayOfWeek> { wall.DayOfWeek }).OrderBy(d => ((int)d + 6) % 7).Select(d => DayNames[(int)d].ToLowerInvariant());
                return "every " + string.Join(" and ", days) + tod;
            case RepeatKind.Monthly: return $"every month on the {Ordinal(rep.DayOfMonth > 0 ? rep.DayOfMonth : wall.Day)}{tod}";
            case RepeatKind.Interval:
                string unit = rep.Unit switch { IntervalUnit.Minutes => "minutes", IntervalUnit.Hours => "hours", IntervalUnit.Days => "days", _ => "weeks" };
                return $"every {rep.Every} {unit}" + (rep.Unit is IntervalUnit.Days or IntervalUnit.Weeks ? tod : "");
        }
        var today = ReminderSchedule.Wall(now, zone).Date;
        int offset = (int)(wall.Date - today).TotalDays;
        if (offset == 0) return "today" + at;
        if (offset == 1) return "tomorrow" + at;
        return "on " + wall.ToString("yyyy-MM-dd", Inv) + at;
    }
}
