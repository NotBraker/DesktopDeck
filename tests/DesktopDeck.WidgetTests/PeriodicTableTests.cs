using DesktopDeck.Widgets;

public static class PeriodicTableTests
{
    private static PeriodicTableLogic.Element El(string symbol) => PeriodicTableLogic.Find(symbol)!;

    public static void AllElementsPresentAndUnique()
    {
        var all = PeriodicTableLogic.All;
        Expect.Equal(118, all.Count, "118 elements");
        for (int i = 0; i < 118; i++) Expect.Equal(i + 1, all[i].Number, "numbers run 1..118 in order");
        Expect.Equal(118, all.Select(e => e.Symbol).Distinct(StringComparer.Ordinal).Count(), "symbols are unique");
        Expect.Equal(118, all.Select(e => e.Name).Distinct(StringComparer.Ordinal).Count(), "names are unique");
        Expect.True(all.All(e => e.Symbol.Length is >= 1 and <= 2 && char.IsUpper(e.Symbol[0]) && e.Symbol.Skip(1).All(char.IsLower)), "symbols look like symbols");
        Expect.True(all.All(e => e.Name.Length >= 3 && char.IsUpper(e.Name[0])), "names look like names");
        Expect.True(all.All(e => e.Mass > 0), "masses are positive");
    }

    public static void KnownElements()
    {
        Expect.Equal("Hydrogen", PeriodicTableLogic.ByNumber(1)!.Name, "H");
        Expect.Equal("Fe", PeriodicTableLogic.ByNumber(26)!.Symbol, "26 is iron");
        Expect.Equal("Au", PeriodicTableLogic.ByNumber(79)!.Symbol, "79 is gold");
        Expect.Equal("Pb", PeriodicTableLogic.ByNumber(82)!.Symbol, "82 is lead");
        Expect.Equal("U", PeriodicTableLogic.ByNumber(92)!.Symbol, "92 is uranium");
        Expect.Equal("Og", PeriodicTableLogic.ByNumber(118)!.Symbol, "118 is oganesson");
        Expect.Equal("Na", El("sodium").Symbol, "find by name");
        Expect.Equal("K", El("K").Symbol, "find by symbol");
        Expect.Equal("Cu", PeriodicTableLogic.Find("29")!.Symbol, "find by number");
        Expect.True(PeriodicTableLogic.Find("Zz") is null && PeriodicTableLogic.ByNumber(119) is null && PeriodicTableLogic.ByNumber(0) is null, "unknown");
        Expect.Near(55.845, El("Fe").Mass, 1e-9, "iron's mass");
        Expect.Near(12.011, El("C").Mass, 1e-9, "carbon's mass");
        Expect.Near(1.008, El("H").Mass, 1e-9, "hydrogen's mass");
    }

    public static void MassesRiseExceptForTheKnownInversions()
    {
        // Pairs where the heavier element comes first in the table: Ar/K, Co/Ni, Te/I, Th/Pa, U/Np, Pu/Am.
        int[] inversions = [18, 27, 52, 90, 92, 94];
        var all = PeriodicTableLogic.All;
        for (int i = 1; i < 118; i++)
        {
            bool inverted = all[i].Mass < all[i - 1].Mass;
            Expect.True(inverted == inversions.Contains(i), $"{all[i - 1].Symbol} -> {all[i].Symbol}: mass order");
        }
    }

    public static void PositionsOnTheGrid()
    {
        var h = El("H");
        Expect.Equal((1, 1, 1, 1), (h.Period, h.Group, h.Row, h.Column), "hydrogen");
        var he = El("He");
        Expect.Equal((1, 18), (he.Period, he.Group), "helium is group 18");
        var fe = El("Fe");
        Expect.Equal((4, 8), (fe.Period, fe.Group), "iron");
        var og = El("Og");
        Expect.Equal((7, 18, 7, 18), (og.Period, og.Group, og.Row, og.Column), "oganesson");
        var li = El("Li");
        Expect.Equal((2, 1), (li.Period, li.Group), "lithium");
        var b = El("B");
        Expect.Equal((2, 13), (b.Period, b.Group), "boron");
        var al = El("Al");
        Expect.Equal((3, 13), (al.Period, al.Group), "aluminum");
        var kr = El("Kr");
        Expect.Equal((4, 18), (kr.Period, kr.Group), "krypton");
        var hf = El("Hf");
        Expect.Equal((6, 4), (hf.Period, hf.Group), "hafnium follows lutetium into group 4");
        var rf = El("Rf");
        Expect.Equal((7, 4), (rf.Period, rf.Group), "rutherfordium");
        var cs = El("Cs");
        Expect.Equal((6, 1), (cs.Period, cs.Group), "cesium");
        var ba = El("Ba");
        Expect.Equal((6, 2), (ba.Period, ba.Group), "barium");
        var rn = El("Rn");
        Expect.Equal((6, 18), (rn.Period, rn.Group), "radon");
    }

    public static void FBlockSitsInTheTwoLowerRows()
    {
        var la = El("La");
        Expect.Equal((9, 3, 6, 0), (la.Row, la.Column, la.Period, la.Group), "lanthanum starts the first f row");
        var lu = El("Lu");
        Expect.Equal((9, 17), (lu.Row, lu.Column), "lutetium ends it");
        var ac = El("Ac");
        Expect.Equal((10, 3, 7), (ac.Row, ac.Column, ac.Period), "actinium starts the second");
        var lr = El("Lr");
        Expect.Equal((10, 17), (lr.Row, lr.Column), "lawrencium ends it");
        for (int z = 57; z <= 71; z++) Expect.Equal("lanthanide", PeriodicTableLogic.ByNumber(z)!.Category, "lanthanide " + z);
        for (int z = 89; z <= 103; z++) Expect.Equal("actinide", PeriodicTableLogic.ByNumber(z)!.Category, "actinide " + z);
    }

    public static void EveryGridCellHoldsExactlyOneElement()
    {
        var cells = PeriodicTableLogic.All.Select(e => (e.Row, e.Column)).ToList();
        Expect.Equal(118, cells.Distinct().Count(), "no two elements share a cell");
        Expect.True(cells.All(c => c.Row is >= 1 and <= 10 && c.Row != 8 && c.Column is >= 1 and <= 18), "inside the grid, row 8 stays empty");
        Expect.Equal(2, PeriodicTableLogic.All.Count(e => e.Row == 1), "period 1 has two elements");
        Expect.Equal(8, PeriodicTableLogic.All.Count(e => e.Row == 2), "period 2 has eight");
        Expect.Equal(18, PeriodicTableLogic.All.Count(e => e.Row == 4), "period 4 has eighteen");
        Expect.Equal(32, PeriodicTableLogic.All.Count(e => e.Period == 6), "period 6 has thirty-two");
        Expect.Equal(32, PeriodicTableLogic.All.Count(e => e.Period == 7), "period 7 has thirty-two");
    }

    public static void Categories()
    {
        Expect.Equal("alkali", El("Na").Category, "sodium");
        Expect.Equal("alkali", El("Fr").Category, "francium");
        Expect.Equal("alkaline", El("Ca").Category, "calcium");
        Expect.Equal("transition", El("Fe").Category, "iron");
        Expect.Equal("transition", El("Zn").Category, "zinc");
        Expect.Equal("transition", El("Hg").Category, "mercury");
        Expect.Equal("post", El("Al").Category, "aluminum");
        Expect.Equal("post", El("Pb").Category, "lead");
        Expect.Equal("metalloid", El("Si").Category, "silicon");
        Expect.Equal("metalloid", El("Sb").Category, "antimony");
        Expect.Equal("nonmetal", El("H").Category, "hydrogen");
        Expect.Equal("nonmetal", El("C").Category, "carbon");
        Expect.Equal("nonmetal", El("Se").Category, "selenium");
        Expect.Equal("halogen", El("Cl").Category, "chlorine");
        Expect.Equal("noble", El("He").Category, "helium");
        Expect.Equal("noble", El("Xe").Category, "xenon");
        Expect.Equal("actinide", El("U").Category, "uranium");
        Expect.True(PeriodicTableLogic.All.All(e => PeriodicTableLogic.Categories.Any(c => c.Id == e.Category)), "every element has a known category");
        foreach (var c in PeriodicTableLogic.Categories) Expect.True(PeriodicTableLogic.All.Any(e => e.Category == c.Id), "category used: " + c.Id);
        Expect.Equal(7, PeriodicTableLogic.All.Count(e => e.Category == "noble"), "seven noble gases");
        Expect.Equal(6, PeriodicTableLogic.All.Count(e => e.Category == "alkali"), "six alkali metals");
        Expect.Equal(6, PeriodicTableLogic.All.Count(e => e.Category == "metalloid"), "six metalloids");
        Expect.Equal(15, PeriodicTableLogic.All.Count(e => e.Category == "lanthanide"), "fifteen lanthanides");
    }

    public static void Blocks()
    {
        Expect.Equal("s", PeriodicTableLogic.Block(El("Na")), "sodium");
        Expect.Equal("s", PeriodicTableLogic.Block(El("He")), "helium");
        Expect.Equal("p", PeriodicTableLogic.Block(El("Cl")), "chlorine");
        Expect.Equal("d", PeriodicTableLogic.Block(El("Fe")), "iron");
        Expect.Equal("f", PeriodicTableLogic.Block(El("Ce")), "cerium");
    }

    public static void ElectronConfigurations()
    {
        Expect.Equal("1s1", PeriodicTableLogic.ElectronConfiguration(1), "hydrogen");
        Expect.Equal("1s2", PeriodicTableLogic.ElectronConfiguration(2), "helium");
        Expect.Equal("[He] 2s1", PeriodicTableLogic.ElectronConfiguration(3), "lithium");
        Expect.Equal("[He] 2s2 2p4", PeriodicTableLogic.ElectronConfiguration(8), "oxygen");
        Expect.Equal("[Ne] 3s1", PeriodicTableLogic.ElectronConfiguration(11), "sodium");
        Expect.Equal("[Ne] 3s2 3p5", PeriodicTableLogic.ElectronConfiguration(17), "chlorine");
        Expect.Equal("[Ar] 4s1", PeriodicTableLogic.ElectronConfiguration(19), "potassium");
        Expect.Equal("[Ar] 3d6 4s2", PeriodicTableLogic.ElectronConfiguration(26), "iron");
        Expect.Equal("[Ar] 3d5 4s1", PeriodicTableLogic.ElectronConfiguration(24), "chromium is an exception");
        Expect.Equal("[Ar] 3d10 4s1", PeriodicTableLogic.ElectronConfiguration(29), "copper is an exception");
        Expect.Equal("[Ar] 3d10 4s2 4p1", PeriodicTableLogic.ElectronConfiguration(31), "gallium");
        Expect.Equal("[Kr] 4d10 5s1", PeriodicTableLogic.ElectronConfiguration(47), "silver");
        Expect.Equal("[Xe] 4f14 5d10 6s1", PeriodicTableLogic.ElectronConfiguration(79), "gold");
        Expect.Equal("[Xe] 4f14 5d1 6s2", PeriodicTableLogic.ElectronConfiguration(71), "lutetium");
        Expect.Equal("[Xe] 4f3 6s2", PeriodicTableLogic.ElectronConfiguration(59), "praseodymium");
        Expect.Equal("[Xe] 4f14 5d10 6s2 6p4", PeriodicTableLogic.ElectronConfiguration(84), "polonium");
        Expect.Equal("[Rn] 5f3 6d1 7s2", PeriodicTableLogic.ElectronConfiguration(92), "uranium");
        Expect.Equal("[Rn] 5f14 6d10 7s2 7p6", PeriodicTableLogic.ElectronConfiguration(118), "oganesson");
        Expect.Equal("", PeriodicTableLogic.ElectronConfiguration(0), "out of range");
    }

    public static void ElectronCountsAddUp()
    {
        // Every non-exception configuration must hold exactly Z electrons.
        foreach (var e in PeriodicTableLogic.All)
        {
            string config = PeriodicTableLogic.ElectronConfiguration(e.Number);
            int total = 0;
            foreach (var part in config.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part[0] == '[')
                {
                    total += PeriodicTableLogic.Find(part.Trim('[', ']'))!.Number;
                    continue;
                }
                total += int.Parse(new string(part.Skip(2).ToArray()));
            }
            Expect.Equal(e.Number, total, "electrons in " + e.Symbol + ": " + config);
        }
    }

    public static void ElementOfTheDayCyclesThroughEverything()
    {
        var start = new DateTime(2026, 10, 5);
        var seen = Enumerable.Range(0, 118).Select(i => PeriodicTableLogic.OfTheDay(start.AddDays(i)).Number).ToList();
        Expect.Equal(118, seen.Distinct().Count(), "118 days show 118 different elements");
        Expect.Equal(PeriodicTableLogic.OfTheDay(start).Number, PeriodicTableLogic.OfTheDay(start.AddHours(17)).Number, "same all day");
        Expect.Equal(PeriodicTableLogic.OfTheDay(start).Number, PeriodicTableLogic.OfTheDay(start.AddDays(118)).Number, "then it repeats");
        Expect.True(PeriodicTableLogic.OfTheDay(new DateTime(1990, 1, 1)).Number is >= 1 and <= 118, "dates before 2000 still work");
    }

    public static void MassText()
    {
        Expect.Equal("55.85", PeriodicTableLogic.MassText(El("Fe"), 2), "two decimals");
        Expect.Equal("55.845", PeriodicTableLogic.MassText(El("Fe"), 3), "three decimals");
        Expect.Equal("56", PeriodicTableLogic.MassText(El("Fe"), 0), "no decimals");
        Expect.Equal("[98]", PeriodicTableLogic.MassText(El("Tc"), 2), "no stable isotope");
        Expect.Equal("238.03", PeriodicTableLogic.MassText(El("U"), 2), "uranium has a standard mass");
        Expect.Equal("[294]", PeriodicTableLogic.MassText(El("Og"), 1), "oganesson");
        Expect.Equal("Group 8", PeriodicTableLogic.GroupText(El("Fe")), "group text");
        Expect.Equal("f-block", PeriodicTableLogic.GroupText(El("Ce")), "f-block text");
    }

    public static void CategoryColorsParse()
    {
        var defaults = PeriodicTableLogic.ParseColors(PeriodicTableLogic.DefaultColors.Split('\n'));
        Expect.Equal(10, defaults.Count, "ten categories");
        var custom = PeriodicTableLogic.ParseColors(["noble gas | #112233", "halogen | red", "metalloid|#ABC", "nonsense | #fff"]);
        Expect.Equal("#112233", custom["noble"], "by name");
        Expect.Equal("#ABC", custom["metalloid"], "short hex");
        Expect.Equal(defaults["halogen"], custom["halogen"], "bad color keeps the default");
    }
}
