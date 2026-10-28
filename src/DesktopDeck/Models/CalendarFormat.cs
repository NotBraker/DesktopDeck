using System.Globalization;

namespace DesktopDeck.Models;

/// <summary>Plain-text dates and times for events (pure, so Schedule, Agenda and the dialogs word them the same way).</summary>
public static class CalendarFormat
{
    public static string Time(DateTime time, bool use24Hour) => Planner.Format(time.ToString("HH:mm", CultureInfo.InvariantCulture), use24Hour);

    public static string Day(DateOnly day) => day.ToString("ddd, MMM d", CultureInfo.CurrentCulture);

    /// <summary>"Today", "Tomorrow" or "Wed, Oct 7".</summary>
    public static string DayLabel(DateOnly day, DateOnly today) => (day.DayNumber - today.DayNumber) switch { 0 => "Today", 1 => "Tomorrow", -1 => "Yesterday", _ => Day(day) };

    /// <summary>"Mon, Oct 5 · 9:00 AM – 10:30 AM", or "Wed, Oct 7 – Fri, Oct 9 · All day".</summary>
    public static string Range(CalendarEvent e, bool use24Hour = false)
    {
        if (e.AllDay)
        {
            var last = e.LastDay;
            return (last > e.FirstDay ? Day(e.FirstDay) + " – " + Day(last) : Day(e.FirstDay)) + " · All day";
        }
        if (DateOnly.FromDateTime(e.Start) == DateOnly.FromDateTime(e.End) || e.End <= e.Start)
            return Day(e.FirstDay) + " · " + Time(e.Start, use24Hour) + (e.End > e.Start ? " – " + Time(e.End, use24Hour) : "");
        return Day(e.FirstDay) + ", " + Time(e.Start, use24Hour) + " – " + Day(DateOnly.FromDateTime(e.End)) + ", " + Time(e.End, use24Hour);
    }
}
