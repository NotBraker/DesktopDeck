using DesktopDeck.Widgets;

public static class DiceTests
{
    public static void DiceCountAndFacesStayInBounds()
    {
        var random = new Random(123);
        for (int i = 0; i < 200; i++)
        {
            var dice = DiceLogic.Roll(random, 99, 6);
            Expect.Equal(6, dice.Length, "count is capped at six");
            Expect.True(dice.All(x => x is >= 1 and <= 6), "faces are 1-6");
        }
        Expect.Equal(1, DiceLogic.Roll(random, 0, 20).Length, "at least one die");
        foreach (int sides in DiceLogic.Sides)
            Expect.True(Enumerable.Range(0, 300).SelectMany(_ => DiceLogic.Roll(random, 3, sides)).All(x => x >= 1 && x <= sides), "d" + sides);
    }

    public static void EveryFaceValueCanAppear()
    {
        var random = new Random(5);
        var seen = Enumerable.Range(0, 2000).SelectMany(_ => DiceLogic.Roll(random, 1, 20)).Distinct().Count();
        Expect.Equal(20, seen, "all d20 faces appear");
    }

    public static void SidesParseFromChoicesAndFallBackToSix()
    {
        Expect.Equal(20, DiceLogic.ParseSides("d20"), "d20");
        Expect.Equal(100, DiceLogic.ParseSides("D100"), "d100");
        Expect.Equal(6, DiceLogic.ParseSides("banana"), "junk");
        Expect.Equal(6, DiceLogic.ParseSides("d1"), "too few sides");
    }

    public static void PipsMatchTheFaceAndShapesAreDefined()
    {
        for (int face = 1; face <= 6; face++) Expect.Equal(face, DiceLogic.Pips(face).Length, "pip count " + face);
        Expect.Equal(0, DiceLogic.Pips(7).Length, "no pips beyond six");
        Expect.Equal("triangle", DiceLogic.Shape(4), "d4");
        Expect.Equal("hexagon", DiceLogic.Shape(20), "d20");
        Expect.Equal(3, DiceLogic.ShapePoints("triangle").Length, "triangle corners");
        Expect.Equal(5, DiceLogic.ShapePoints("pentagon").Length, "pentagon corners");
        Expect.Equal(6, DiceLogic.ShapePoints("hexagon").Length, "hexagon corners");
        foreach (string shape in new[] { "triangle", "diamond", "kite", "pentagon", "hexagon" })
            Expect.True(DiceLogic.ShapePoints(shape).All(p => p.X is >= 0 and <= 1 && p.Y is >= 0 and <= 1), shape + " fits the unit square");
    }

    public static void InclusiveNumbersHandleExtremeAndReversedBounds()
    {
        var random = new Random(23);
        Expect.Equal(long.MaxValue, DiceLogic.Number(random, long.MaxValue, long.MaxValue), "max singleton");
        Expect.Equal(long.MinValue, DiceLogic.Number(random, long.MinValue, long.MinValue), "min singleton");
        for (int i = 0; i < 200; i++) Expect.True(DiceLogic.Number(random, 3, 1) is >= 1 and <= 3, "reversed bounds");
        var seen = Enumerable.Range(0, 300).Select(_ => DiceLogic.Number(random, -2, 2)).Distinct().Count();
        Expect.Equal(5, seen, "both ends are reachable");
        DiceLogic.Number(random, long.MinValue, long.MaxValue);
        DiceLogic.Number(random, long.MinValue, 5);
        DiceLogic.Number(random, 5, long.MaxValue);
    }

    public static void RangesParseFromFriendlyText()
    {
        Expect.Equal((1L, 100L), DiceLogic.ParseRange("1, 100")!.Value, "comma");
        Expect.Equal((1L, 100L), DiceLogic.ParseRange("1-100")!.Value, "dash");
        Expect.Equal((-5L, 5L), DiceLogic.ParseRange("-5 to 5")!.Value, "negative start");
        Expect.Equal((3L, 9L), DiceLogic.ParseRange("9 3")!.Value, "reversed");
        Expect.True(DiceLogic.ParseRange("7") is null, "one number is not a range");
        Expect.True(DiceLogic.ParseRange(null) is null, "null");
    }

    public static void PickingWithoutRepeatsCyclesThroughPool()
    {
        var random = new Random(1);
        string[] items = ["A", "B", "C"];
        var used = new List<string>();
        var picked = new HashSet<string>();
        for (int i = 0; i < 3; i++)
        {
            var result = DiceLogic.Pick(random, items, used, true);
            used = result.Used;
            picked.Add(result.Item!);
        }
        Expect.Equal(3, picked.Count, "every choice once per round");
        Expect.Equal(1, DiceLogic.Pick(random, items, used, true).Used.Count, "a new round starts");
        Expect.True(DiceLogic.Pick(random, [], [], true).Item is null, "empty list");
        Expect.Equal(0, DiceLogic.Pick(random, items, used, false).Used.Count, "repeats allowed keeps no memory");
        Expect.True(DiceLogic.Pick(random, ["X"], ["gone"], true).Item == "X", "forgotten items are ignored");
    }

    public static void YesNoAndCoinOnlyGiveValidAnswers()
    {
        var random = new Random(9);
        var answers = Enumerable.Range(0, 200).Select(_ => DiceLogic.YesNo(random, false)).Distinct().OrderBy(x => x).ToArray();
        Expect.Equal("No,Yes", string.Join(",", answers), "yes or no");
        Expect.True(Enumerable.Range(0, 300).Select(_ => DiceLogic.YesNo(random, true)).Contains("Maybe"), "maybe appears when allowed");
        Expect.True(Enumerable.Range(0, 100).Select(_ => DiceLogic.Coin(random)).Distinct().Count() == 2, "both coin sides");
    }

    public static void HistoryIsTaggedBoundedAndPerMode()
    {
        Expect.Equal("one\ntwo", DiceLogic.PushHistory("two\nthree", "one", 2), "bounded history");
        string history = "";
        history = DiceLogic.PushHistory(history, DiceLogic.Tag("coin", "Heads"), 60);
        history = DiceLogic.PushHistory(history, DiceLogic.Tag("dice", "2d6: 3 + 5 = 8"), 60);
        history = DiceLogic.PushHistory(history, DiceLogic.Tag("coin", "Tails"), 60);
        Expect.Equal("Tails,Heads", string.Join(",", DiceLogic.HistoryFor(history, "coin", 5)), "coin entries newest first");
        Expect.Equal(1, DiceLogic.HistoryFor(history, "dice", 5).Count, "dice entries are separate");
        Expect.Equal(1, DiceLogic.HistoryFor(history, "coin", 1).Count, "take limit");
        Expect.True(!DiceLogic.Tag("pick", "a\nb").Contains('\n'), "tagged entries are one line");
    }

    public static void FacesRoundTripAndDescribe()
    {
        Expect.Equal("3,5", DiceLogic.Encode(DiceLogic.Decode("3,bad,5")), "faces encoding");
        Expect.Equal("2d6: 3 + 5 = 8", DiceLogic.Describe([3, 5], 6, true), "sum description");
        Expect.Equal("d20: 17", DiceLogic.Describe([17], 20, true), "single die");
        Expect.Equal("3d4: 1, 2, 3", DiceLogic.Describe([1, 2, 3], 4, false), "no sum");
        Expect.Equal("2d6", DiceLogic.DiceLabel(2, 6), "label");
        Expect.Equal("2–12", DiceLogic.Range(2, 6), "range");
    }
}
