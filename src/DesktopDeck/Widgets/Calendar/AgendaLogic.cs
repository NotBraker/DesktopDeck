using DesktopDeck.Models;

namespace DesktopDeck.Widgets;

/// <summary>What the Agenda widget shows (read from its settings).</summary>
internal sealed record AgendaOptions(int Days, int MaxItems, bool Google, bool Ics, bool Local, bool HideAllDay, bool ShowEmptyDays, bool Use24Hour, bool Reminders = true);

/// <summary>One day of the agenda.</summary>
internal sealed record AgendaDay(DateOnly Day, List<CalendarEvent> Events);

/// <summary>The grouped agenda, and how many more events did not fit under the item limit.</summary>
internal sealed record AgendaResult(List<AgendaDay> Days, int More);

/// <summary>Grouping and wording for the Agenda widget. Pure (no WPF, no network) so tests cover it.</summary>
internal static class AgendaLogic
{
    public static bool SourceAllowed(CalendarEvent e, AgendaOptions o) => e.Source switch
    {
        CalendarSource.Google => o.Google,
        CalendarSource.Ics => o.Ics,
        CalendarSource.Reminder => o.Reminders,
        _ => o.Local,
    };

    /// <summary>True while the event is happening (all-day events are not "now").</summary>
    public static bool IsNow(CalendarEvent e, DateTime now) => !e.AllDay && e.Start <= now && now < e.End;

    /// <summary>
    /// Upcoming events grouped by day, starting today. Events that already ended today are left out; all-day events come
    /// first in a day; multi-day events appear on every day they cover. Stops after <see cref="AgendaOptions.MaxItems"/>.
    /// </summary>
    public static AgendaResult Group(IEnumerable<CalendarEvent> events, DateTime now, AgendaOptions o)
    {
        var today = DateOnly.FromDateTime(now);
        var wanted = events.Where(e => SourceAllowed(e, o) && !(o.HideAllDay && e.AllDay)).ToList();
        var days = new List<AgendaDay>();
        int shown = 0, more = 0;
        for (int i = 0; i < Math.Max(1, o.Days); i++)
        {
            var day = today.AddDays(i);
            var onDay = wanted.Where(e => e.OnDay(day) && (e.AllDay || e.End > now || e.End <= e.Start && e.Start >= now))
                .GroupBy(e => e.Key).Select(g => g.First())
                .OrderBy(e => e.AllDay ? 0 : 1).ThenBy(e => e.Start).ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
            var kept = new List<CalendarEvent>();
            foreach (var e in onDay)
            {
                if (shown >= o.MaxItems) { more++; continue; }
                kept.Add(e);
                shown++;
            }
            if (kept.Count > 0 || (o.ShowEmptyDays && onDay.Count == 0)) days.Add(new AgendaDay(day, kept));
        }
        return new AgendaResult(days, more);
    }

    /// <summary>"All day", "9:00 AM", "9:00 AM – 10:30 AM", or "until 9:00 AM" for an event that began on an earlier day.</summary>
    public static string TimeText(CalendarEvent e, DateOnly day, bool use24Hour, bool withEnd)
    {
        if (e.AllDay) return "All day";
        bool startsToday = DateOnly.FromDateTime(e.Start) == day;
        bool endsToday = DateOnly.FromDateTime(e.End) == day || e.End.TimeOfDay == TimeSpan.Zero && DateOnly.FromDateTime(e.End.AddTicks(-1)) == day;
        if (!startsToday) return endsToday && e.End > e.Start ? "until " + CalendarFormat.Time(e.End, use24Hour) : "All day";
        string start = CalendarFormat.Time(e.Start, use24Hour);
        return withEnd && endsToday && e.End > e.Start ? start + " – " + CalendarFormat.Time(e.End, use24Hour) : start;
    }

    /// <summary>"Today", "Tomorrow", then "Wed, Oct 7"; with <paramref name="relative"/> false always the date.</summary>
    public static string DayLabel(DateOnly day, DateOnly today, bool relative) => relative ? CalendarFormat.DayLabel(day, today) : CalendarFormat.Day(day);

    /// <summary>Made-up events for smoke renders and the widget's samples (no network, no real data).</summary>
    public static List<CalendarEvent> Sample(DateTime now)
    {
        var start = now.Date;
        DateTime At(int dayOffset, int hour, int minute = 0) => start.AddDays(dayOffset).AddHours(hour).AddMinutes(minute);
        CalendarEvent E(CalendarSource source, string calendar, string color, string title, DateTime from, DateTime to, string location = "", bool allDay = false) =>
            new() { Source = source, CalendarLabel = calendar, CalendarId = calendar, Color = color, Title = title, Start = from, End = to, Location = location, AllDay = allDay, Editable = source == CalendarSource.Google };
        return
        [
            E(CalendarSource.Google, "Personal", "#4285f4", "Study session: chemistry", now.AddMinutes(-20), now.AddMinutes(40), "Library, room 2"),
            E(CalendarSource.Google, "Personal", "#4285f4", "Call with Mom", now.AddHours(2), now.AddHours(2.5)),
            E(CalendarSource.Ics, "School", "#14b8a6", "Chemistry lab", now.AddHours(4), now.AddHours(5.5), "Science building"),
            E(CalendarSource.Local, "Schedule", "#6c63ff", "Soccer practice", now.AddHours(6), now.AddHours(7.5), "North field"),
            E(CalendarSource.Google, "Personal", "#4285f4", "Dentist", At(1, 9, 30), At(1, 10, 15), "Main Street Dental"),
            E(CalendarSource.Ics, "School", "#14b8a6", "Essay due", At(1, 0), At(2, 0), "", true),
            E(CalendarSource.Google, "Family", "#f59e0b", "Grandma's birthday", At(2, 0), At(3, 0), "", true),
            E(CalendarSource.Local, "Schedule", "#6c63ff", "Math class", At(2, 8, 35), At(2, 9, 25), "Room 12"),
            E(CalendarSource.Google, "Personal", "#4285f4", "Dinner with Alex", At(2, 18, 30), At(2, 20), "Luigi's"),
            E(CalendarSource.Ics, "School", "#14b8a6", "Field trip", At(3, 0), At(5, 0), "", true),
            E(CalendarSource.Reminder, "Reminders", "#ec4899", "Call Mom", now.AddHours(3), now.AddHours(3)),
            E(CalendarSource.Reminder, "Reminders", "#ec4899", "Pack the soccer bag", At(1, 7, 30), At(1, 7, 30)),
            E(CalendarSource.Google, "Family", "#f59e0b", "Piano lesson", At(4, 16), At(4, 17)),
        ];
    }
}
