using DesktopDeck.Widgets;

public static class FocusTests
{
    private static readonly DateTime Today = new(2026, 10, 4);

    public static void SetAndRoundTrip()
    {
        var state = new FocusLogic.FocusState();
        FocusLogic.SetFocus(state, "  Ship the slides ", Today);
        state.Subtitle = "By noon";
        var back = FocusLogic.Parse(FocusLogic.Serialize(state));
        Expect.Equal("Ship the slides", back.Text, "trimmed text survives JSON");
        Expect.Equal("2026-10-04", back.Date, "date key");
        Expect.Equal("By noon", back.Subtitle, "subtitle");
    }

    public static void BadJsonIsEmpty()
    {
        Expect.Equal("", FocusLogic.Parse("{not json").Text, "invalid JSON");
        Expect.Equal("", FocusLogic.Parse(null).Text, "null");
        Expect.Equal(0, FocusLogic.Parse("{\"history\":null}").History.Count, "null history");
    }

    public static void RolloverArchivesWhenAutoClear()
    {
        var state = new FocusLogic.FocusState();
        FocusLogic.SetFocus(state, "Write report", Today.AddDays(-1));
        state.Done = true;
        Expect.True(!FocusLogic.Rollover(state, Today.AddDays(-1), true), "same day: nothing to do");
        Expect.True(!FocusLogic.Rollover(state, Today, false), "no auto clear: carries over");
        Expect.Equal("Write report", state.Text, "still there");
        Expect.True(FocusLogic.Rollover(state, Today, true), "next day with auto clear");
        Expect.Equal("", state.Text, "cleared");
        Expect.Equal(1, state.History.Count, "archived");
        Expect.True(state.History[0].Done, "done flag kept");
        Expect.Equal("2026-10-03", state.History[0].Date, "archived under its own day");
    }

    public static void NewFocusOnLaterDayArchivesOld()
    {
        var state = new FocusLogic.FocusState();
        FocusLogic.SetFocus(state, "Old", Today.AddDays(-2));
        FocusLogic.SetFocus(state, "New", Today);
        Expect.Equal("New", state.Text, "replaced");
        Expect.Equal("Old", state.History.Single().Text, "old one archived");
        FocusLogic.SetFocus(state, "Newer", Today);
        Expect.Equal(1, state.History.Count, "same-day edits are not archived");
    }

    public static void ChangingTextResetsDone()
    {
        var state = new FocusLogic.FocusState();
        FocusLogic.SetFocus(state, "A", Today);
        state.Done = true;
        FocusLogic.SetFocus(state, "A", Today);
        Expect.True(state.Done, "same text keeps done");
        FocusLogic.SetFocus(state, "B", Today);
        Expect.True(!state.Done, "new text starts not done");
    }

    public static void HistoryIsTrimmedAndOrdered()
    {
        var state = new FocusLogic.FocusState();
        for (int i = 10; i >= 1; i--) FocusLogic.SetFocus(state, "Day " + i, Today.AddDays(-i));
        FocusLogic.SetFocus(state, "Today", Today);
        Expect.Equal(FocusLogic.HistoryLength, state.History.Count, "at most seven");
        Expect.Equal("Day 1", state.History[0].Text, "newest first");
        Expect.Equal(3, FocusLogic.Recent(state, 3).Count, "recent count");
        Expect.Equal(0, FocusLogic.Recent(state, 0).Count, "none");
    }

    public static void QuoteAndStreak()
    {
        Expect.Equal("“Go”", FocusLogic.Quote("Go", "quotes"), "curly quotes");
        Expect.Equal("Go", FocusLogic.Quote("Go", "bar"), "bar style leaves text alone");
        var state = new FocusLogic.FocusState();
        state.History.Add(new FocusLogic.FocusDay { Date = "2026-10-03", Text = "x", Done = true });
        state.History.Add(new FocusLogic.FocusDay { Date = "2026-10-02", Text = "y", Done = true });
        state.History.Add(new FocusLogic.FocusDay { Date = "2026-10-01", Text = "z", Done = false });
        Expect.Equal(2, FocusLogic.DoneStreak(state, Today), "today not done yet: counts from yesterday");
        FocusLogic.SetFocus(state, "Now", Today);
        state.Done = true;
        Expect.Equal(3, FocusLogic.DoneStreak(state, Today), "today done");
    }

    public static void HistoryKeepsTheChosenDays()
    {
        static FocusLogic.FocusState Seeded()
        {
            var s = new FocusLogic.FocusState { Text = "Latest", Date = FocusLogic.Key(Today.AddDays(-2)) };
            s.History.Add(new FocusLogic.FocusDay { Date = FocusLogic.Key(Today.AddDays(-3)), Text = "a" });
            s.History.Add(new FocusLogic.FocusDay { Date = FocusLogic.Key(Today.AddDays(-8)), Text = "b" });
            s.History.Add(new FocusLogic.FocusDay { Date = FocusLogic.Key(Today.AddDays(-20)), Text = "c" });
            return s;
        }
        Expect.Equal(7, FocusLogic.HistoryLength, "the built-in default is 7 days");
        var byDefault = Seeded();
        FocusLogic.Archive(byDefault, Today);
        Expect.Equal(2, byDefault.History.Count, "the default keeps the last 7 days only");
        var same = Seeded();
        FocusLogic.Archive(same, Today, 7);
        Expect.Equal(byDefault.History.Count, same.History.Count, "an explicit 7 is the old behavior");
        var everything = Seeded();
        FocusLogic.Archive(everything, Today, 0);
        Expect.Equal(4, everything.History.Count, "0 keeps every day");
        var shortest = Seeded();
        FocusLogic.Archive(shortest, Today, 2);
        Expect.Equal(1, shortest.History.Count, "a shorter setting trims more");
        var rolled = Seeded();
        Expect.True(FocusLogic.Rollover(rolled, Today, true, 0) && rolled.History.Count == 4, "a new day keeps everything at 0");
    }
}
