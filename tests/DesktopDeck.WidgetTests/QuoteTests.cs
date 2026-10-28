using DesktopDeck.Widgets;

public static class QuoteTests
{
    public static void CollectionHasEnoughShortAttributedQuotesInEveryCategory()
    {
        Expect.True(QuoteLogic.BuiltIn.Length >= 80, "at least 80 quotes");
        Expect.True(QuoteLogic.BuiltIn.All(q => q.Author.Length > 0 && QuoteLogic.WordCount(q.Text) <= 25), "short attributed entries");
        foreach (var category in QuoteLogic.Categories)
            Expect.True(QuoteLogic.BuiltIn.Count(q => q.Category == category) >= 10, "enough " + category + " quotes");
        Expect.True(QuoteLogic.BuiltIn.All(q => QuoteLogic.Categories.Contains(q.Category)), "known categories only");
        Expect.Equal(QuoteLogic.BuiltIn.Length, QuoteLogic.BuiltIn.Select(q => q.Text).Distinct().Count(), "no duplicate quotes");
    }

    public static void DailyAndHourlyPeriodsRespectBoundary()
    {
        var time = new DateTime(2026, 10, 4, 23, 59, 0);
        Expect.Equal(QuoteLogic.Period("daily", time) + 1, QuoteLogic.Period("daily", time.AddMinutes(1)), "midnight");
        Expect.Equal(QuoteLogic.Period("hourly", time) + 1, QuoteLogic.Period("hourly", time.AddMinutes(1)), "hour rollover");
        Expect.Equal(QuoteLogic.Period("daily", time), QuoteLogic.Period("daily", time.AddHours(-23)), "same day");
        Expect.Equal(0L, QuoteLogic.Period("click", time), "click stationary");
    }

    public static void DeterministicShuffleVisitsEveryEntryOncePerCycle()
    {
        var indices = Enumerable.Range(0, 100).Select(i => QuoteLogic.Pick(i, 100, 17)).ToArray();
        Expect.Equal(100, indices.Distinct().Count(), "full cycle");
        Expect.Equal(QuoteLogic.Pick(-23, 100), QuoteLogic.Pick(-23, 100), "negative steps are deterministic");
        Expect.Equal(-1, QuoteLogic.Pick(0, 0), "empty pool");
        Expect.True(Enumerable.Range(0, 100).Select(i => QuoteLogic.Pick(-i - 1, 100, 17)).Distinct().Count() == 100, "walking backwards also visits all");
        Expect.True(Enumerable.Range(0, 100).Any(i => QuoteLogic.Pick(i, 100, 17) != QuoteLogic.Pick(i, 100, 29)), "salt changes the order");
    }

    public static void SameDayAlwaysGivesTheSameQuote()
    {
        var pool = QuoteLogic.Pool("built-in", QuoteLogic.Categories, []);
        var morning = new DateTime(2026, 10, 4, 8, 0, 0);
        Expect.Equal(QuoteLogic.For(pool, "daily", morning, 0), QuoteLogic.For(pool, "daily", morning.AddHours(12), 0), "stable all day");
        Expect.True(QuoteLogic.For(pool, "daily", morning, 0) != QuoteLogic.For(pool, "daily", morning.AddDays(1), 0), "new day, new quote");
        Expect.True(QuoteLogic.For(pool, "daily", morning, 0) != QuoteLogic.For(pool, "daily", morning, 1), "click moves on");
        Expect.Equal(QuoteLogic.For(pool, "daily", morning, 0), QuoteLogic.For(pool, "daily", morning, 5 + -5), "back returns");
    }

    public static void ClickOffsetResetsWhenThePeriodChanges()
    {
        string stored = QuoteLogic.WriteOffset(9000, 4);
        Expect.Equal(4L, QuoteLogic.ReadOffset(stored, 9000), "same period keeps clicks");
        Expect.Equal(0L, QuoteLogic.ReadOffset(stored, 9001), "new period starts fresh");
        Expect.Equal(0L, QuoteLogic.ReadOffset("junk", 9000), "damaged value");
        Expect.Equal(0L, QuoteLogic.ReadOffset(null, 0), "missing value");
        Expect.Equal(-2L, QuoteLogic.ReadOffset(QuoteLogic.WriteOffset(5, -2), 5), "negative offsets work");
    }

    public static void UserQuotesParseWithSeveralSeparators()
    {
        var entry = QuoteLogic.ParseLine("A small beginning. — Notebook")!;
        Expect.Equal("A small beginning.", entry.Text, "text");
        Expect.Equal("Notebook", entry.Author, "author parse");
        Expect.Equal("Me", QuoteLogic.ParseLine("\"Quoted\" - Me")!.Author, "hyphen separator");
        Expect.Equal("Quoted", QuoteLogic.ParseLine("\"Quoted\" - Me")!.Text, "quote marks stripped");
        Expect.Equal("", QuoteLogic.ParseLine("No author here")!.Author, "no author");
        Expect.True(QuoteLogic.ParseLine("   ") is null, "blank line");
        Expect.Equal(1, QuoteLogic.Pool("mine", [], ["A small beginning. — Notebook", ""]).Count, "own pool skips blanks");
    }

    public static void SourceAndCategoryChoicesShapeThePool()
    {
        string[] mine = ["Mine one — Me"];
        int humor = QuoteLogic.BuiltIn.Count(q => q.Category == "humor");
        Expect.Equal(humor, QuoteLogic.Pool("built-in", ["humor"], mine).Count, "category filter");
        Expect.Equal(humor + 1, QuoteLogic.Pool("both", ["humor"], mine).Count, "both sources");
        Expect.Equal(1, QuoteLogic.Pool("mine", ["humor"], mine).Count, "only mine");
        Expect.Equal(0, QuoteLogic.Pool("built-in", [], mine).Count, "no categories");
        Expect.True(QuoteLogic.For([], "daily", DateTime.Today, 0) is null, "empty selection");
    }

    public static void QuoteMarksWrapText()
    {
        Expect.Equal("“Hello”", QuoteLogic.Wrap("Hello", "curly"), "curly");
        Expect.Equal("\"Hello\"", QuoteLogic.Wrap("Hello", "straight"), "straight");
        Expect.Equal("Hello", QuoteLogic.Wrap("Hello", "none"), "none");
        Expect.Equal("Hello", QuoteLogic.Wrap("Hello", "big"), "big marks are drawn by the widget");
    }

    public static void TextFitShrinksLongQuotesOnly()
    {
        double small = QuoteLogic.FitFontSize("Know thyself.", 24, 11, 250, 120);
        Expect.Near(24, small, 0.01, "short quote keeps its size");
        string longQuote = string.Join(" ", Enumerable.Repeat("wandering", 24));
        double shrunk = QuoteLogic.FitFontSize(longQuote, 24, 11, 250, 100);
        Expect.True(shrunk < 24 && shrunk >= 11, "long quote shrinks");
        Expect.Near(11, QuoteLogic.FitFontSize(longQuote, 24, 11, 40, 20), 0.01, "never below the minimum");
    }
}
