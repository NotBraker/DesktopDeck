using DesktopDeck.Models;
using DesktopDeck.Widgets;

/// <summary>Grouping and wording for the Agenda widget (AgendaLogic.cs).</summary>
public static class AgendaTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 10, 0, 0);
    private static AgendaOptions Options(int days = 7, int max = 50, bool google = true, bool ics = true, bool local = true, bool hideAllDay = false, bool empty = false) =>
        new(days, max, google, ics, local, hideAllDay, empty, false);

    private static CalendarEvent E(CalendarSource source, string title, DateTime start, DateTime end, bool allDay = false) =>
        new() { Source = source, Title = title, Start = start, End = end, AllDay = allDay, CalendarId = source.ToString() };

    private static List<CalendarEvent> Sample() =>
    [
        E(CalendarSource.Google, "Done already", Now.Date.AddHours(8), Now.Date.AddHours(9)),
        E(CalendarSource.Google, "Happening", Now.AddMinutes(-30), Now.AddMinutes(30)),
        E(CalendarSource.Ics, "Later", Now.AddHours(3), Now.AddHours(4)),
        E(CalendarSource.Local, "Block", Now.AddHours(5), Now.AddHours(6)),
        E(CalendarSource.Ics, "Holiday", Now.Date, Now.Date.AddDays(1), allDay: true),
        E(CalendarSource.Google, "Trip", Now.Date.AddDays(1), Now.Date.AddDays(3), allDay: true),
        E(CalendarSource.Google, "Tomorrow 9", Now.Date.AddDays(1).AddHours(9), Now.Date.AddDays(1).AddHours(10)),
        E(CalendarSource.Google, "Far away", Now.Date.AddDays(20), Now.Date.AddDays(20).AddHours(1)),
    ];

    public static void GroupsByDayAndHidesFinishedEvents()
    {
        var result = AgendaLogic.Group(Sample(), Now, Options());
        Expect.Equal(3, result.Days.Count, "today, tomorrow and the day after (the trip)");
        Expect.True(result.Days[0].Events.Select(e => e.Title).SequenceEqual(["Holiday", "Happening", "Later", "Block"]), "today: all-day first, then by time, finished events hidden");
        Expect.True(result.Days[1].Events.Select(e => e.Title).SequenceEqual(["Trip", "Tomorrow 9"]), "tomorrow: the trip (all-day) then the 9:00 event");
        Expect.True(result.Days[2].Events.Select(e => e.Title).SequenceEqual(["Trip"]), "a multi-day event shows on every day it covers");
        Expect.Equal(0, result.More, "nothing left over");
    }

    public static void SourcesCanBeSwitchedOff()
    {
        var googleOnly = AgendaLogic.Group(Sample(), Now, Options(ics: false, local: false));
        Expect.True(googleOnly.Days.SelectMany(d => d.Events).All(e => e.Source == CalendarSource.Google), "only Google events");
        var localOnly = AgendaLogic.Group(Sample(), Now, Options(google: false, ics: false));
        Expect.Equal(1, localOnly.Days.SelectMany(d => d.Events).Count(), "only the local block");
    }

    public static void HideAllDayAndLimits()
    {
        var noAllDay = AgendaLogic.Group(Sample(), Now, Options(hideAllDay: true));
        Expect.True(noAllDay.Days.SelectMany(d => d.Events).All(e => !e.AllDay), "all-day events hidden");
        var limited = AgendaLogic.Group(Sample(), Now, Options(max: 3));
        Expect.Equal(3, limited.Days.SelectMany(d => d.Events).Count(), "limited to three");
        Expect.True(limited.More > 0, "the rest is counted");
        var oneDay = AgendaLogic.Group(Sample(), Now, Options(days: 1));
        Expect.Equal(1, oneDay.Days.Count, "one day only");
    }

    public static void EmptyDaysCanBeShown()
    {
        var shown = AgendaLogic.Group([E(CalendarSource.Google, "Later", Now.AddDays(2).AddHours(1), Now.AddDays(2).AddHours(2))], Now, Options(days: 4, empty: true));
        Expect.Equal(4, shown.Days.Count, "every day listed");
        Expect.True(shown.Days[0].Events.Count == 0 && shown.Days[2].Events.Count == 1, "empty days are empty");
        Expect.Equal(1, AgendaLogic.Group([E(CalendarSource.Google, "Later", Now.AddDays(2).AddHours(1), Now.AddDays(2).AddHours(2))], Now, Options(days: 4)).Days.Count, "by default empty days are skipped");
    }

    public static void NowAndTimeText()
    {
        var happening = Sample()[1];
        Expect.True(AgendaLogic.IsNow(happening, Now) && !AgendaLogic.IsNow(Sample()[2], Now) && !AgendaLogic.IsNow(Sample()[4], Now), "now highlight: running events only, never all-day");
        var day = new DateOnly(2026, 10, 5);
        Expect.Equal("11:00 AM", AgendaLogic.TimeText(E(CalendarSource.Google, "x", Now.AddHours(1), Now.AddHours(2)), day, false, false), "start time only");
        Expect.Equal("11:00 AM – 12:00 PM", AgendaLogic.TimeText(E(CalendarSource.Google, "x", Now.AddHours(1), Now.AddHours(2)), day, false, true), "with end time");
        Expect.Equal("11:00", AgendaLogic.TimeText(E(CalendarSource.Google, "x", Now.AddHours(1), Now.AddHours(2)), day, true, false), "24-hour");
        Expect.Equal("All day", AgendaLogic.TimeText(Sample()[4], day, false, false), "all-day");
        var overnight = E(CalendarSource.Google, "x", Now.AddHours(13), Now.AddHours(17));
        Expect.Equal("until 3:00 AM", AgendaLogic.TimeText(overnight, day.AddDays(1), false, false), "an event that began yesterday");
        Expect.Equal("Today", AgendaLogic.DayLabel(day, day, true), "today label");
        Expect.Equal("Tomorrow", AgendaLogic.DayLabel(day.AddDays(1), day, true), "tomorrow label");
        Expect.True(AgendaLogic.DayLabel(day, day, false) != "Today", "always-the-date mode");
    }

    public static void SampleDataIsUsable()
    {
        var sample = AgendaLogic.Sample(Now);
        Expect.True(sample.Count >= 8 && sample.Any(e => e.AllDay) && sample.Select(e => e.Source).Distinct().Count() == 4, "the sample covers every source (Google, link, Schedule, Reminders) and an all-day event");
        Expect.True(AgendaLogic.Group(sample, Now, Options()).Days.Count >= 3, "the sample fills several days");
    }

    public static void LaneAndColorHelpersWorkForWidgets()
    {
        var lanes = CalendarLanes.Assign(new[] { (S: 600, E: 660), (S: 630, E: 700), (S: 800, E: 830) }, x => x.S, x => x.E);
        Expect.Equal(2, lanes[0].Lanes, "overlapping pair shares two lanes");
        Expect.Equal(1, lanes[2].Lanes, "a separate item gets the full width");
        Expect.True(CalendarColors.IsLight("#ffffff") && !CalendarColors.IsLight("#000000"), "contrast");
    }
}
