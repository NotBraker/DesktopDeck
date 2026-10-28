using DesktopDeck.Widgets;

public static class JournalTests
{
    private static readonly DateTime Today = new(2026, 10, 4);

    public static void MoodTogglesAndClears()
    {
        var entries = new Dictionary<string, JournalLogic.JournalDay>();
        JournalLogic.SetMood(entries, Today, 4);
        Expect.Equal(4, JournalLogic.Get(entries, Today)!.Mood, "set");
        JournalLogic.SetMood(entries, Today, 2);
        Expect.Equal(2, JournalLogic.Get(entries, Today)!.Mood, "changed");
        JournalLogic.SetMood(entries, Today, 2);
        Expect.True(JournalLogic.Get(entries, Today) is null, "same mood again clears an otherwise empty day");
        JournalLogic.SetNote(entries, Today, " walked the dog ");
        JournalLogic.SetMood(entries, Today, 5);
        JournalLogic.SetMood(entries, Today, 5);
        Expect.Equal("walked the dog", JournalLogic.Get(entries, Today)!.Note, "note survives clearing the mood");
        JournalLogic.SetNote(entries, Today, "");
        Expect.True(JournalLogic.Get(entries, Today) is null, "empty day removed");
        JournalLogic.SetMood(entries, Today, 9);
        Expect.Equal(5, JournalLogic.Get(entries, Today)!.Mood, "clamped");
    }

    public static void StreakCountsBackFromToday()
    {
        var entries = new Dictionary<string, JournalLogic.JournalDay>();
        for (int i = 1; i <= 4; i++) JournalLogic.SetMood(entries, Today.AddDays(-i), 3);
        JournalLogic.SetMood(entries, Today.AddDays(-6), 3);
        Expect.Equal(4, JournalLogic.Streak(entries, Today), "today blank: counts from yesterday");
        JournalLogic.SetNote(entries, Today, "note only still counts");
        Expect.Equal(5, JournalLogic.Streak(entries, Today), "today logged");
        Expect.Equal(0, JournalLogic.Streak(entries, Today.AddDays(3)), "broken");
    }

    public static void StripAndAverage()
    {
        var entries = new Dictionary<string, JournalLogic.JournalDay>();
        JournalLogic.SetMood(entries, Today, 5);
        JournalLogic.SetMood(entries, Today.AddDays(-2), 2);
        var days = JournalLogic.LastDays(entries, Today, 7);
        Expect.Equal(7, days.Count, "seven days");
        Expect.Equal(Today.AddDays(-6), days[0].Day, "oldest first");
        Expect.Equal(Today, days[^1].Day, "ends today");
        Expect.True(days[^2].Entry is null, "blank yesterday");
        Expect.Near(3.5, JournalLogic.Average(entries, Today, 7)!.Value, 1e-9, "average");
        Expect.True(JournalLogic.Average(entries, Today.AddDays(-30), 7) is null, "nothing logged");
    }

    public static void JsonRoundTripAndPruning()
    {
        var entries = new Dictionary<string, JournalLogic.JournalDay>();
        JournalLogic.SetMood(entries, Today, 4);
        JournalLogic.SetNote(entries, Today, "good day");
        JournalLogic.SetMood(entries, Today.AddDays(-JournalLogic.KeepDays - 5), 1);
        var back = JournalLogic.Parse(JournalLogic.Serialize(entries, Today));
        Expect.Equal(1, back.Count, "very old entry pruned");
        Expect.Equal("good day", back["2026-10-04"].Note, "note");
        Expect.Equal(0, JournalLogic.Parse("nope").Count, "bad JSON");
        Expect.Equal(1, JournalLogic.Parse("{\"2026-10-01\":{\"mood\":3},\"bad-key\":{\"mood\":2},\"2026-10-02\":{\"mood\":0}}").Count, "bad keys and empty days dropped");
    }

    public static void LabelsAndPalettes()
    {
        var labels = JournalLogic.Labels("Rough,  Meh ,,Nice");
        Expect.Equal("Rough", labels[0], "first");
        Expect.Equal("Meh", labels[1], "trimmed");
        Expect.Equal("Okay", labels[2], "blank falls back");
        Expect.Equal("Great", labels[4], "missing falls back");
        Expect.Equal(5, JournalLogic.Palette("classic")!.Length, "five colors");
        Expect.True(JournalLogic.Palette("accent") is null, "accent shades are computed by the widget");
        var custom = JournalLogic.Palette("custom", "#111111, nonsense, #abc");
        Expect.Equal("#111111", custom![0], "custom color");
        Expect.Equal("#f97316", custom[1], "invalid falls back");
        Expect.Equal("#abc", custom[2], "short hex");
    }

    public static void HistoryKeepsTheChosenDays()
    {
        var today = new DateTime(2026, 10, 5);
        var entries = new Dictionary<string, JournalLogic.JournalDay>
        {
            ["2026-10-04"] = new() { Mood = 4, Note = "yesterday" },
            ["2026-09-01"] = new() { Mood = 3, Note = "last month" },
            ["2025-01-01"] = new() { Mood = 2, Note = "last year" },
        };
        Expect.Equal(3, JournalLogic.Parse(JournalLogic.Serialize(entries, today)).Count, "the default keeps about three years");
        Expect.Equal(3, JournalLogic.Parse(JournalLogic.Serialize(entries, today, 0)).Count, "0 keeps everything");
        Expect.Equal(2, JournalLogic.Parse(JournalLogic.Serialize(entries, today, 60)).Count, "60 days drops last year");
        Expect.Equal(1, JournalLogic.Parse(JournalLogic.Serialize(entries, today, 30)).Count, "30 days keeps only yesterday");
    }
}
