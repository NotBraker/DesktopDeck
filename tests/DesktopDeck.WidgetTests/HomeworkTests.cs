using System.Globalization;
using DesktopDeck.Widgets;

public static class HomeworkTests
{
    private static readonly DateTime Monday = new(2026, 10, 5, 9, 0, 0); // a Monday
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static HomeworkLogic.Assignment Item(string title, int? dueInDays, string time = "", string subject = "Math", int priority = 1) =>
        HomeworkLogic.Create(title, subject, dueInDays is { } d ? Monday.Date.AddDays(d) : null, time, priority);

    public static void SubjectLinesParse()
    {
        var subjects = StudySubjectsLogic.Parse(["History | #C2A14D", "Latin", "  ", "history | #fff", "Chem | nope", "Math|#9B9A5E"]);
        Expect.Equal(4, subjects.Count, "blank and duplicate lines dropped");
        Expect.Equal("#C2A14D", subjects[0].Color, "color kept");
        Expect.True(StudySubjectsLogic.IsHex(subjects[1].Color), "missing color gets a palette color");
        Expect.True(StudySubjectsLogic.IsHex(subjects[2].Color), "invalid color gets a palette color");
        Expect.Equal("#9B9A5E", subjects[3].Color, "no spaces around the bar");
        Expect.Equal("Latin", StudySubjectsLogic.Find(subjects, " latin ")?.Name, "find ignores case");
        Expect.Equal(StudySubjectsLogic.ColorOf(subjects, "Art"), StudySubjectsLogic.ColorOf(subjects, "Art"), "unknown subject color is stable");
    }

    public static void ParsesPlainDates()
    {
        Expect.Equal(Monday.Date, HomeworkLogic.ParseDate("today", Monday), "today");
        Expect.Equal(Monday.Date.AddDays(1), HomeworkLogic.ParseDate("Tomorrow", Monday), "tomorrow");
        Expect.Equal(new DateTime(2026, 10, 12), HomeworkLogic.ParseDate("2026-10-12", Monday), "iso");
        Expect.Equal(new DateTime(2026, 10, 12), HomeworkLogic.ParseDate("10/12", Monday), "month first");
        Expect.Equal(new DateTime(2026, 12, 10), HomeworkLogic.ParseDate("10/12", Monday, dayFirst: true), "day first");
        Expect.Equal(new DateTime(2026, 10, 12), HomeworkLogic.ParseDate("12/10", Monday, dayFirst: true), "day first reads 12/10 as 12 Oct");
        Expect.Equal(new DateTime(2026, 10, 9), HomeworkLogic.ParseDate("Oct 9", Monday), "month name");
        Expect.Equal(new DateTime(2026, 10, 9), HomeworkLogic.ParseDate("9 october", Monday), "day then month");
        Expect.Equal(new DateTime(2026, 10, 9), HomeworkLogic.ParseDate("October 9th", Monday), "ordinal");
        Expect.Equal(new DateTime(2027, 8, 1), HomeworkLogic.ParseDate("august 1", Monday), "august keeps its letters (and rolls to next year)");
        Expect.Equal(new DateTime(2026, 10, 8), HomeworkLogic.ParseDate("+3", Monday), "plus days");
        Expect.Equal(new DateTime(2026, 10, 19), HomeworkLogic.ParseDate("2 weeks", Monday), "weeks");
        Expect.Equal(new DateTime(2026, 10, 12), HomeworkLogic.ParseDate("next week", Monday), "next week");
        Expect.Equal(new DateTime(2026, 10, 12), HomeworkLogic.ParseDate("12", Monday), "bare day of month");
        Expect.Equal(new DateTime(2026, 11, 3), HomeworkLogic.ParseDate("3", Monday), "bare day already passed means next month");
    }

    public static void WeekdaysMeanTheNextOne()
    {
        Expect.Equal(new DateTime(2026, 10, 9), HomeworkLogic.ParseDate("fri", Monday), "friday");
        Expect.Equal(new DateTime(2026, 10, 9), HomeworkLogic.ParseDate("Friday", Monday), "friday full");
        Expect.Equal(new DateTime(2026, 10, 12), HomeworkLogic.ParseDate("monday", Monday), "same weekday is next week");
        Expect.Equal(new DateTime(2026, 10, 8), HomeworkLogic.ParseDate("thurs", Monday), "thurs");
        Expect.Equal(new DateTime(2026, 10, 11), HomeworkLogic.ParseDate("next sun", Monday), "next sunday");
    }

    public static void RejectsNonsense()
    {
        Expect.True(HomeworkLogic.ParseDate("blah", Monday) is null, "words");
        Expect.True(HomeworkLogic.ParseDate("2/30", Monday) is null, "feb 30");
        Expect.True(HomeworkLogic.ParseDate("13/45", Monday) is null, "month 13");
        Expect.True(HomeworkLogic.ParseDate("", Monday) is null, "empty");
        Expect.True(!HomeworkLogic.ParseDue("someday", Monday).Ok, "ParseDue reports failure");
    }

    public static void YearlessDatesRollForward()
    {
        Expect.Equal(new DateTime(2027, 1, 15), HomeworkLogic.ParseDate("1/15", Monday), "january is next year");
        Expect.Equal(new DateTime(2026, 9, 20), HomeworkLogic.ParseDate("9/20", Monday), "recent past stays (it is overdue)");
    }

    public static void ParsesTimes()
    {
        Expect.Equal("14:30", HomeworkLogic.ParseTime("14:30"), "24 hour");
        Expect.Equal("14:30", HomeworkLogic.ParseTime("2:30pm"), "pm");
        Expect.Equal("15:00", HomeworkLogic.ParseTime("3pm"), "bare pm");
        Expect.Equal("00:15", HomeworkLogic.ParseTime("12:15 am"), "12 am is midnight");
        Expect.Equal("12:00", HomeworkLogic.ParseTime("noon"), "noon");
        Expect.True(HomeworkLogic.ParseTime("13pm") is null, "13pm");
        Expect.True(HomeworkLogic.ParseTime("25:00") is null, "25:00");
        Expect.True(HomeworkLogic.ParseTime("5") is null, "a bare number is not a time");
    }

    public static void ParsesDateAndTimeTogether()
    {
        var a = HomeworkLogic.ParseDue("fri 3pm", Monday);
        Expect.True(a.Ok && a.Date == new DateTime(2026, 10, 9) && a.Time == "15:00", "fri 3pm");
        var b = HomeworkLogic.ParseDue("Oct 12 at 14:30", Monday);
        Expect.True(b.Ok && b.Date == new DateTime(2026, 10, 12) && b.Time == "14:30", "oct 12 at 14:30");
        var c = HomeworkLogic.ParseDue("tomorrow 9 am", Monday);
        Expect.True(c.Ok && c.Date == new DateTime(2026, 10, 6) && c.Time == "09:00", "tomorrow 9 am");
        var d = HomeworkLogic.ParseDue("noon", Monday);
        Expect.True(d.Ok && d.Date == Monday.Date && d.Time == "12:00", "a time alone means today");
        var e = HomeworkLogic.ParseDue("  ", Monday);
        Expect.True(e.Ok && e.Date is null, "empty means no due date");
    }

    public static void GroupsByDueDate()
    {
        Expect.Equal(HomeworkLogic.Group.Overdue, HomeworkLogic.GroupOf(Item("a", -1), Monday), "yesterday");
        Expect.Equal(HomeworkLogic.Group.Today, HomeworkLogic.GroupOf(Item("a", 0), Monday), "today");
        Expect.Equal(HomeworkLogic.Group.Tomorrow, HomeworkLogic.GroupOf(Item("a", 1), Monday), "tomorrow");
        Expect.Equal(HomeworkLogic.Group.Week, HomeworkLogic.GroupOf(Item("a", 2), Monday), "in two days");
        Expect.Equal(HomeworkLogic.Group.Week, HomeworkLogic.GroupOf(Item("a", 6), Monday), "in six days");
        Expect.Equal(HomeworkLogic.Group.Later, HomeworkLogic.GroupOf(Item("a", 7), Monday), "in seven days");
        Expect.Equal(HomeworkLogic.Group.NoDate, HomeworkLogic.GroupOf(Item("a", null), Monday), "no date");
        var done = Item("a", -5);
        HomeworkLogic.Complete(done, true, Monday);
        Expect.Equal(HomeworkLogic.Group.Done, HomeworkLogic.GroupOf(done, Monday), "done wins over overdue");
    }

    public static void SortsByDateThenTimeThenPriority()
    {
        var items = new List<HomeworkLogic.Assignment>
        {
            Item("later", 5), Item("nodate", null), Item("late-pm", 1, "17:00"), Item("early-am", 1, "08:00"),
            Item("untimed-high", 1, "", "Math", 2), Item("today", 0),
        };
        var sections = HomeworkLogic.Layout(items, "due", Monday, true, 0, 0);
        var order = sections.SelectMany(s => s.Items).Select(a => a.Title).ToList();
        Expect.Equal("today,early-am,late-pm,untimed-high,later,nodate", string.Join(',', order), "order");
        Expect.Equal("Today,Tomorrow,This week,No date", string.Join(',', sections.Select(s => s.Title)), "sections");
    }

    public static void DoneAreLastAndCanHide()
    {
        var finished = Item("finished", -1);
        HomeworkLogic.Complete(finished, true, Monday.AddDays(-3));
        var recent = Item("recent", 0);
        HomeworkLogic.Complete(recent, true, Monday);
        var open = Item("open", 2);
        var all = new List<HomeworkLogic.Assignment> { finished, recent, open };

        var forever = HomeworkLogic.Layout(all, "due", Monday, true, 0, 0);
        Expect.Equal("open", forever[0].Items[0].Title, "open first");
        Expect.Equal(2, forever.Last().Items.Count, "both done items shown when never hidden");
        var hidden = HomeworkLogic.Layout(all, "due", Monday, true, 2, 0);
        Expect.Equal(1, hidden.Last().Items.Count, "older done item hidden after 2 days");
        Expect.Equal("recent", hidden.Last().Items[0].Title, "recent done item stays");
        Expect.True(HomeworkLogic.Layout(all, "due", Monday, false, 0, 0).All(s => s.Key != "Done"), "done can be switched off");
    }

    public static void MaxItemsCapsOpenOnes()
    {
        var items = Enumerable.Range(0, 10).Select(i => Item("t" + i, i)).ToList();
        var sections = HomeworkLogic.Layout(items, "none", Monday, true, 0, 4);
        Expect.Equal(4, sections.Sum(s => s.Items.Count), "four shown");
        Expect.Equal("t0", sections[0].Items[0].Title, "earliest kept");
    }

    public static void GroupsBySubjectAndPriority()
    {
        var items = new List<HomeworkLogic.Assignment> { Item("m", 1, "", "Math"), Item("l", 2, "", "Latin"), Item("x", 3, "", ""), Item("h", 1, "", "History", 2) };
        var bySubject = HomeworkLogic.Layout(items, "subject", Monday, true, 0, 0, ["History", "Latin", "Math"]);
        Expect.Equal("History,Latin,Math,No subject", string.Join(',', bySubject.Select(s => s.Title)), "subject order follows the list");
        var byPriority = HomeworkLogic.Layout(items, "priority", Monday, true, 0, 0);
        Expect.Equal("High priority", byPriority[0].Title, "high first");
        Expect.Equal("h", byPriority[0].Items[0].Title, "the high item");
    }

    public static void PillsExplainUrgency()
    {
        var pill = HomeworkLogic.PillFor(Item("a", -3), Monday, 2, true, Inv);
        Expect.Equal("3d late", pill.Text, "late text");
        Expect.Equal(HomeworkLogic.PillKind.Overdue, pill.Kind, "late kind");
        Expect.Equal("Yesterday", HomeworkLogic.PillFor(Item("a", -1), Monday, 2, true, Inv).Text, "yesterday");
        Expect.Equal("Today", HomeworkLogic.PillFor(Item("a", 0), Monday, 2, true, Inv).Text, "today");
        Expect.Equal(HomeworkLogic.PillKind.Today, HomeworkLogic.PillFor(Item("a", 0), Monday, 2, true, Inv).Kind, "today kind");
        Expect.Equal("Today · 15:00", HomeworkLogic.PillFor(Item("a", 0, "15:00"), Monday, 2, true, Inv).Text, "today with time");
        Expect.Equal("Today · 3 PM", HomeworkLogic.PillFor(Item("a", 0, "15:00"), Monday, 2, false, Inv).Text, "12-hour clock");
        Expect.Equal(HomeworkLogic.PillKind.Overdue, HomeworkLogic.PillFor(Item("a", 0, "08:00"), Monday, 2, true, Inv).Kind, "due earlier today is overdue");
        Expect.Equal("Tomorrow", HomeworkLogic.PillFor(Item("a", 1), Monday, 2, true, Inv).Text, "tomorrow");
        Expect.Equal("Fri", HomeworkLogic.PillFor(Item("a", 4), Monday, 2, true, Inv).Text, "weekday");
        Expect.Equal(HomeworkLogic.PillKind.Normal, HomeworkLogic.PillFor(Item("a", 4), Monday, 2, true, Inv).Kind, "beyond the warning window");
        Expect.Equal(HomeworkLogic.PillKind.Soon, HomeworkLogic.PillFor(Item("a", 2), Monday, 2, true, Inv).Kind, "inside the warning window");
        Expect.Equal("Oct 19", HomeworkLogic.PillFor(Item("a", 14), Monday, 2, true, Inv).Text, "far date");
        Expect.Equal(HomeworkLogic.PillKind.None, HomeworkLogic.PillFor(Item("a", null), Monday, 2, true, Inv).Kind, "no date");
    }

    public static void PostponeMovesLateItemsFromToday()
    {
        var late = Item("a", -4);
        HomeworkLogic.Postpone(late, Monday, 1);
        Expect.Equal("2026-10-06", late.Due, "late item lands tomorrow");
        var upcoming = Item("b", 3);
        HomeworkLogic.Postpone(upcoming, Monday, 1);
        Expect.Equal("2026-10-09", upcoming.Due, "upcoming item moves a day");
        var none = Item("c", null);
        HomeworkLogic.Postpone(none, Monday, 2);
        Expect.Equal("2026-10-07", none.Due, "undated item gets a date");
    }

    public static void JsonRoundTripAndRepair()
    {
        var items = new List<HomeworkLogic.Assignment> { Item("Read chapter 4", 1, "09:30", "History", 2), Item("Worksheet", null) };
        HomeworkLogic.Complete(items[1], true, Monday);
        var back = HomeworkLogic.Parse(HomeworkLogic.Serialize(items));
        Expect.Equal(2, back.Count, "count");
        Expect.Equal("09:30", back[0].Time, "time kept");
        Expect.Equal(2, back[0].Priority, "priority kept");
        Expect.True(back[1].Done && back[1].DoneAt == "2026-10-05", "done kept");
        Expect.Equal(0, HomeworkLogic.Parse("not json").Count, "garbage");
        Expect.Equal(0, HomeworkLogic.Parse("").Count, "empty");
        var repaired = HomeworkLogic.Parse("[{\"title\":\"x\",\"due\":\"banana\",\"time\":\"99:99\",\"priority\":9},{\"title\":\" \"}]");
        Expect.Equal(1, repaired.Count, "blank titles dropped");
        Expect.Equal("", repaired[0].Due, "bad date cleared");
        Expect.Equal("", repaired[0].Time, "bad time cleared");
        Expect.Equal(2, repaired[0].Priority, "priority clamped");
        Expect.True(repaired[0].Id.Length > 0, "id assigned");
    }

    public static void SummaryAndQuickDates()
    {
        var items = new List<HomeworkLogic.Assignment> { Item("a", -1), Item("b", 0), Item("c", 0), Item("d", 4), Item("e", null) };
        HomeworkLogic.Complete(items[4], true, Monday);
        var (open, overdue, today, done) = HomeworkLogic.Summary(items, Monday);
        Expect.Equal((4, 1, 2, 1), (open, overdue, today, done), "summary");
        var quick = HomeworkLogic.QuickDates(Monday, Inv);
        Expect.Equal("Today", quick[0].Label, "first");
        Expect.Equal("Wednesday", quick[2].Label, "third is a weekday name");
        Expect.Equal(Monday.Date.AddDays(7), quick.Last().Date, "last is a week away");
    }

    public static void FinishedItemsExpireAfterTheChosenDays()
    {
        static List<HomeworkLogic.Assignment> Items() =>
        [
            new() { Title = "Old", Done = true, DoneAt = "2026-09-01" },
            new() { Title = "Recent", Done = true, DoneAt = "2026-10-03" },
            new() { Title = "Undated", Done = true, DoneAt = "" },
            new() { Title = "Open", Done = false },
        ];
        var today = new DateTime(2026, 10, 5, 9, 0, 0);
        var none = Items();
        Expect.Equal(0, HomeworkLogic.PruneDone(none, today, 0), "0 keeps finished items until cleared by hand");
        Expect.Equal(4, none.Count, "nothing is removed at 0");
        Expect.Equal("", none[2].DoneAt, "no done date is added at 0");
        var list = Items();
        Expect.Equal(1, HomeworkLogic.PruneDone(list, today, 30), "a 30-day limit removes the old finished item");
        Expect.Equal("Recent,Undated,Open", string.Join(",", list.Select(a => a.Title)), "the rest stays");
        Expect.Equal("2026-10-05", list[1].DoneAt, "a finished item with no done date counts as done today");
    }
}
