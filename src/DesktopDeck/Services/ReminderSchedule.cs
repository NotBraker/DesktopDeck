using DesktopDeck.Models;

namespace DesktopDeck.Services;

/// <summary>
/// When reminders ring: the repeat rules, with no clock and no UI. Everything takes the time zone explicitly so tests can use any.
/// Daily, weekly, monthly (and day or week) repeats keep their local time of day across daylight saving time; a time that does not
/// exist that day (the clocks jump over it) rings at the first moment after the gap. "Every N minutes or hours" counts real elapsed time.
/// </summary>
public static class ReminderSchedule
{
    /// <summary>The local wall-clock time of an instant in a zone (Kind Unspecified).</summary>
    public static DateTime Wall(DateTimeOffset instant, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(instant, zone).DateTime;

    /// <summary>
    /// The instant a local wall-clock time stands for. A time skipped by the clocks moves forward to the first valid minute; a time that
    /// happens twice (the clocks go back) means the first time.
    /// </summary>
    public static DateTimeOffset At(DateTime wall, TimeZoneInfo zone)
    {
        wall = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        for (int guard = 0; zone.IsInvalidTime(wall) && guard < 24 * 60; guard++) wall = wall.AddMinutes(1);
        var offset = zone.IsAmbiguousTime(wall) ? zone.GetAmbiguousTimeOffsets(wall).Max() : zone.GetUtcOffset(wall);
        return new DateTimeOffset(wall, offset);
    }

    /// <summary>An instant expressed in the zone's current offset (so its wall-clock reading is right).</summary>
    public static DateTimeOffset Normalize(DateTimeOffset instant, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(instant, zone);

    /// <summary>The span an interval repeat adds in real time (minutes and hours only); days and weeks follow the calendar.</summary>
    public static TimeSpan? Elapsed(ReminderRepeat repeat) => repeat is { Kind: RepeatKind.Interval } r
        ? r.Unit switch { IntervalUnit.Minutes => TimeSpan.FromMinutes(r.Every), IntervalUnit.Hours => TimeSpan.FromHours(r.Every), _ => null }
        : null;

    /// <summary>The ring after <paramref name="current"/> (strictly later), or null for a reminder that does not repeat.</summary>
    public static DateTimeOffset? Next(ReminderRepeat repeat, DateTimeOffset current, TimeZoneInfo zone)
    {
        var wall = Wall(current, zone);
        var time = repeat.Time ?? wall.TimeOfDay;
        switch (repeat.Kind)
        {
            case RepeatKind.Daily:
                return At(wall.Date.AddDays(1) + time, zone);
            case RepeatKind.Weekdays:
            {
                var day = wall.Date.AddDays(1);
                while (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) day = day.AddDays(1);
                return At(day + time, zone);
            }
            case RepeatKind.Weekly:
            {
                var days = repeat.Days.Count > 0 ? repeat.Days : new List<DayOfWeek> { wall.DayOfWeek };
                var day = wall.Date.AddDays(1);
                for (int i = 0; i < 7 && !days.Contains(day.DayOfWeek); i++) day = day.AddDays(1);
                return At(day + time, zone);
            }
            case RepeatKind.Monthly:
            {
                int anchor = repeat.DayOfMonth > 0 ? repeat.DayOfMonth : wall.Day;
                var first = new DateTime(wall.Year, wall.Month, 1).AddMonths(1);
                return At(new DateTime(first.Year, first.Month, Math.Min(anchor, DateTime.DaysInMonth(first.Year, first.Month))) + time, zone);
            }
            case RepeatKind.Interval:
                if (Elapsed(repeat) is { } span) return Normalize(current + span, zone);
                int days2 = repeat.Unit == IntervalUnit.Weeks ? 7 * repeat.Every : repeat.Every;
                return At(wall.Date.AddDays(days2) + time, zone);
            default:
                return null;
        }
    }

    /// <summary>The first ring strictly after <paramref name="after"/>, for a repeat that starts at <paramref name="time"/> o'clock.</summary>
    public static DateTimeOffset? First(ReminderRepeat repeat, TimeSpan time, DateTimeOffset after, TimeZoneInfo zone)
    {
        var wall = Wall(after, zone);
        switch (repeat.Kind)
        {
            case RepeatKind.Daily:
            case RepeatKind.Weekdays:
            case RepeatKind.Weekly:
                for (int i = 0; i <= 8; i++)
                {
                    var day = wall.Date.AddDays(i);
                    bool ok = repeat.Kind switch
                    {
                        RepeatKind.Weekdays => day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday),
                        RepeatKind.Weekly => (repeat.Days.Count > 0 ? repeat.Days : new List<DayOfWeek> { wall.DayOfWeek }).Contains(day.DayOfWeek),
                        _ => true,
                    };
                    if (ok && At(day + time, zone) is var candidate && candidate > after) return candidate;
                }
                return null;
            case RepeatKind.Monthly:
            {
                int anchor = repeat.DayOfMonth > 0 ? repeat.DayOfMonth : wall.Day;
                for (int i = 0; i <= 13; i++)
                {
                    var month = new DateTime(wall.Year, wall.Month, 1).AddMonths(i);
                    var candidate = At(new DateTime(month.Year, month.Month, Math.Min(anchor, DateTime.DaysInMonth(month.Year, month.Month))) + time, zone);
                    if (candidate > after) return candidate;
                }
                return null;
            }
            case RepeatKind.Interval:
            {
                if (Elapsed(repeat) is { } span) return Normalize(after + span, zone);
                var today = At(wall.Date + time, zone);
                if (today > after) return today;
                return At(wall.Date.AddDays(repeat.Unit == IntervalUnit.Weeks ? 7 * repeat.Every : repeat.Every) + time, zone);
            }
            default:
                return null;
        }
    }
}
