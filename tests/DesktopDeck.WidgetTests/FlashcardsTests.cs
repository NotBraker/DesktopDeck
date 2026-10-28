using DesktopDeck.Widgets;

public static class FlashcardsTests
{
    private static readonly DateTime Today = new(2026, 10, 5);
    private static readonly int[] Intervals = [1, 2, 4, 8, 16];

    private static List<FlashcardsLogic.Entry> Latin() =>
        FlashcardsLogic.Entries(FlashcardsLogic.ParseDeck(["aqua | water", "terra | earth", "ignis | fire", "ventus | wind", "# a comment", "no answer", "| nothing", "lux | light | brightness"]), "front");

    public static void HashIsStableAcrossRuns()
    {
        // Published FNV-1a 64-bit test vectors: this must never change, or everyone's progress is lost.
        Expect.Equal("cbf29ce484222325", FlashcardsLogic.StableHash(""), "empty");
        Expect.Equal("af63dc4c8601ec8c", FlashcardsLogic.StableHash("a"), "a");
        Expect.Equal("85944171f73967e8", FlashcardsLogic.StableHash("foobar"), "foobar");
        Expect.Equal(FlashcardsLogic.KeyOf("aqua", "water"), FlashcardsLogic.KeyOf(" aqua ", " water "), "whitespace around is ignored");
        Expect.True(FlashcardsLogic.KeyOf("aqua", "water") != FlashcardsLogic.KeyOf("water", "aqua"), "directions have separate progress");
    }

    public static void DecksParse()
    {
        var cards = FlashcardsLogic.ParseDeck(["aqua | water", "# comment", "", "terra\tearth", "no answer", "| nothing", "lux | light | brightness", "line \\n break | two\\nlines"]);
        Expect.Equal(4, cards.Count, "valid cards");
        Expect.Equal("water", cards[0].Back, "back");
        Expect.Equal("earth", cards[1].Back, "tab separated");
        Expect.Equal("light | brightness", cards[2].Back, "only the first bar splits");
        Expect.Equal("line \n break", cards[3].Front, "escaped line break");
        Expect.Equal(1, FlashcardsLogic.ParseDeck(["x | y"], 1)[0].Deck, "deck number");
    }

    public static void DirectionsAndDuplicates()
    {
        var cards = FlashcardsLogic.ParseDeck(["aqua | water", "aqua | water", "terra | earth"]);
        Expect.Equal(2, FlashcardsLogic.Entries(cards, "front").Count, "duplicates dropped");
        var reversed = FlashcardsLogic.Entries(cards, "back");
        Expect.Equal("water", reversed[0].Prompt, "reverse asks the back");
        Expect.True(reversed[0].Reversed, "flagged reversed");
        Expect.Equal(4, FlashcardsLogic.Entries(cards, "both").Count, "both directions");
    }

    public static void IntervalsAreRepaired()
    {
        Expect.Equal("1,2,4,8,16", string.Join(',', FlashcardsLogic.ParseIntervals("")), "default");
        Expect.Equal("1,3,7,14,28", string.Join(',', FlashcardsLogic.ParseIntervals("1, 3, 7, 14, 28")), "custom");
        Expect.Equal("2,4,8,16,32", string.Join(',', FlashcardsLogic.ParseIntervals("2,4")), "missing ones double");
        Expect.Equal("1,2,4,8,16", string.Join(',', FlashcardsLogic.ParseIntervals("a,b")), "junk");
        Expect.Equal(5, FlashcardsLogic.ParseIntervals("1,2,3,4,5,6,7").Length, "extra ones ignored");
    }

    public static void GoodMovesUpOneBox()
    {
        var first = FlashcardsLogic.Review(null, FlashcardsLogic.Grade.Good, Today, Intervals, true, 1);
        Expect.Equal(1, first.Box, "new card Good lands in box 1");
        Expect.Equal("2026-10-06", first.Due, "box 1 is due in a day");
        var second = FlashcardsLogic.Review(first, FlashcardsLogic.Grade.Good, Today.AddDays(1), Intervals, true, 2);
        Expect.Equal(2, second.Box, "box 2");
        Expect.Equal("2026-10-08", second.Due, "box 2 interval is 2 days");
        Expect.Equal(2, second.Reps, "reps counted");
        var top = new FlashcardsLogic.Progress { Box = 5, Reps = 9, Due = "2026-10-05" };
        var kept = FlashcardsLogic.Review(top, FlashcardsLogic.Grade.Good, Today, Intervals, true, 3);
        Expect.Equal(5, kept.Box, "box 5 is the top");
        Expect.Equal("2026-10-21", kept.Due, "top interval is 16 days");
    }

    public static void EasySkipsABox()
    {
        var fresh = FlashcardsLogic.Review(null, FlashcardsLogic.Grade.Easy, Today, Intervals, true, 1);
        Expect.Equal(2, fresh.Box, "new card Easy lands in box 2");
        var from3 = FlashcardsLogic.Review(new FlashcardsLogic.Progress { Box = 3, Reps = 3 }, FlashcardsLogic.Grade.Easy, Today, Intervals, true, 2);
        Expect.Equal(5, from3.Box, "3 to 5");
        var from4 = FlashcardsLogic.Review(new FlashcardsLogic.Progress { Box = 4, Reps = 3 }, FlashcardsLogic.Grade.Easy, Today, Intervals, true, 2);
        Expect.Equal(5, from4.Box, "capped at 5");
    }

    public static void AgainDropsToBoxOne()
    {
        var learned = new FlashcardsLogic.Progress { Box = 4, Reps = 6, Lapses = 1, Due = "2026-10-05" };
        var retry = FlashcardsLogic.Review(learned, FlashcardsLogic.Grade.Again, Today, Intervals, true, 7);
        Expect.Equal(1, retry.Box, "back to box 1");
        Expect.Equal("2026-10-05", retry.Due, "retry brings it back today");
        Expect.Equal(2, retry.Lapses, "lapse counted");
        var tomorrow = FlashcardsLogic.Review(learned, FlashcardsLogic.Grade.Again, Today, Intervals, false, 7);
        Expect.Equal("2026-10-06", tomorrow.Due, "without retry it waits the first interval");
        var firstTime = FlashcardsLogic.Review(null, FlashcardsLogic.Grade.Again, Today, Intervals, true, 1);
        Expect.Equal(0, firstTime.Lapses, "failing a new card is not a lapse");
    }

    public static void PreviewMatchesReview()
    {
        Expect.Equal(0, FlashcardsLogic.PreviewDays(null, FlashcardsLogic.Grade.Again, Intervals, true), "again now");
        Expect.Equal(1, FlashcardsLogic.PreviewDays(null, FlashcardsLogic.Grade.Good, Intervals, true), "new good");
        Expect.Equal(2, FlashcardsLogic.PreviewDays(null, FlashcardsLogic.Grade.Easy, Intervals, true), "new easy");
        Expect.Equal("now", FlashcardsLogic.IntervalText(0), "now");
        Expect.Equal("3 days", FlashcardsLogic.IntervalText(3), "days");
        Expect.Equal("3 wk", FlashcardsLogic.IntervalText(21), "weeks");
        Expect.Equal("3 mo", FlashcardsLogic.IntervalText(90), "months");
    }

    public static void NewCardsRespectTheDailyLimit()
    {
        var entries = Latin();
        var progress = new Dictionary<string, FlashcardsLogic.Progress>();
        var daily = new FlashcardsLogic.Daily { Date = FlashcardsLogic.Day(Today), New = 1 };
        var plan = FlashcardsLogic.MakePlan(entries, progress, daily, Today, 3, "deck", "s");
        Expect.Equal(2, plan.Queue.Count, "limit 3 with 1 already learned today leaves 2");
        Expect.Equal(2, plan.NewLeftToday, "new left");
        Expect.Equal(5, plan.NewAvailable, "five new cards exist");
        Expect.Equal(3, plan.Extra.Count, "the rest wait as extras");
        Expect.Equal("aqua", plan.Queue[0].Prompt, "deck order is kept");
        Expect.Equal(0, plan.DueCount, "nothing is due yet");
    }

    public static void DueCardsComeBeforeNewOnesAndFailedGoLast()
    {
        var entries = Latin();
        var p = new Dictionary<string, FlashcardsLogic.Progress>
        {
            [entries[0].Key] = new() { Box = 2, Reps = 2, Due = "2026-10-03", Stamp = 4 },
            [entries[1].Key] = new() { Box = 1, Reps = 1, Due = "2026-10-05", Stamp = 9 },   // just failed
            [entries[2].Key] = new() { Box = 3, Reps = 3, Due = "2026-10-05", Stamp = 2 },
            [entries[3].Key] = new() { Box = 3, Reps = 3, Due = "2026-10-20", Stamp = 3 },   // not due
        };
        var daily = new FlashcardsLogic.Daily { Date = FlashcardsLogic.Day(Today) };
        var plan = FlashcardsLogic.MakePlan(entries, p, daily, Today, 5, "deck", "s");
        Expect.Equal("aqua,ignis,terra,lux", string.Join(',', plan.Queue.Select(e => e.Prompt)), "overdue, then due by stamp, then new");
        Expect.Equal(3, plan.DueCount, "three due");
        Expect.Equal("ventus", plan.Extra[0].Prompt, "study-ahead offers the next due card");
        Expect.Equal(1, plan.BoxCounts[1] + 0, "one card in box 1");
        Expect.Equal(2, plan.BoxCounts[3], "two cards in box 3");
        var newFirst = FlashcardsLogic.MakePlan(entries, p, daily, Today, 5, "deck", "s", reviewsFirst: false);
        Expect.Equal("lux", newFirst.Queue[0].Prompt, "new cards first when asked");
    }

    public static void ShuffleIsStableForASeedAndChangesWithIt()
    {
        var entries = Enumerable.Range(0, 30).Select(i => new FlashcardsLogic.Entry("p" + i, "a" + i, FlashcardsLogic.KeyOf("p" + i, "a" + i), 0, false)).ToList();
        var progress = new Dictionary<string, FlashcardsLogic.Progress>();
        var daily = new FlashcardsLogic.Daily { Date = FlashcardsLogic.Day(Today) };
        string Order(string seed) => string.Join(',', FlashcardsLogic.MakePlan(entries, progress, daily, Today, 50, "shuffle", seed).Queue.Select(e => e.Prompt));
        Expect.Equal(Order("a"), Order("a"), "same seed, same order");
        Expect.True(Order("a") != Order("b"), "another seed, another order");
        Expect.True(Order("a") != string.Join(',', entries.Select(e => e.Prompt)), "shuffled differs from deck order");
        Expect.Equal(30, FlashcardsLogic.MakePlan(entries, progress, daily, Today, 50, "shuffle", "a").Queue.Select(e => e.Key).Distinct().Count(), "nothing lost");
    }

    public static void ProgressRoundTripAndRepair()
    {
        var p = new Dictionary<string, FlashcardsLogic.Progress> { ["abc"] = new() { Box = 3, Due = "2026-10-09", Reps = 4, Lapses = 1, Stamp = 8 } };
        var back = FlashcardsLogic.ParseProgress(FlashcardsLogic.SerializeProgress(p));
        Expect.Equal(3, back["abc"].Box, "box");
        Expect.Equal("2026-10-09", back["abc"].Due, "due");
        Expect.Equal(0, FlashcardsLogic.ParseProgress("garbage").Count, "garbage");
        var repaired = FlashcardsLogic.ParseProgress("{\"k\":{\"b\":99,\"d\":\"soon\"}}");
        Expect.Equal(5, repaired["k"].Box, "box clamped");
        Expect.Equal("", repaired["k"].Due, "bad date cleared");
        Expect.Equal(8, FlashcardsLogic.NextStamp(new Dictionary<string, FlashcardsLogic.Progress> { ["a"] = new() { Stamp = 7 } }), "next stamp");
    }

    public static void DailyCountersResetEachDay()
    {
        var daily = FlashcardsLogic.ParseDaily("", Today);
        FlashcardsLogic.RecordReview(daily, true, FlashcardsLogic.Grade.Good);
        FlashcardsLogic.RecordReview(daily, false, FlashcardsLogic.Grade.Again);
        Expect.Equal((2, 1, 1), (daily.Reviews, daily.New, daily.Again), "counts");
        var same = FlashcardsLogic.ParseDaily(FlashcardsLogic.SerializeDaily(daily), Today);
        Expect.Equal(2, same.Reviews, "same day keeps counts");
        var next = FlashcardsLogic.ParseDaily(FlashcardsLogic.SerializeDaily(daily), Today.AddDays(1));
        Expect.Equal(0, next.Reviews, "new day starts at zero");
    }

    public static void TodayFractionTracksProgress()
    {
        var entries = Latin();
        var daily = new FlashcardsLogic.Daily { Date = FlashcardsLogic.Day(Today), Reviews = 3 };
        var plan = FlashcardsLogic.MakePlan(entries, [], daily, Today, 3, "deck", "s");
        Expect.Near(0.5, FlashcardsLogic.TodayFraction(daily, plan), 1e-9, "3 done, 3 waiting");
        var empty = FlashcardsLogic.MakePlan([], [], new FlashcardsLogic.Daily { Date = FlashcardsLogic.Day(Today) }, Today, 3, "deck", "s");
        Expect.Near(1, FlashcardsLogic.TodayFraction(new FlashcardsLogic.Daily(), empty), 1e-9, "nothing to do is complete");
    }
}
