using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DesktopDeck.Widgets;

/// <summary>
/// Offline periodic table: the 118 elements (symbol, name, standard atomic mass), their category, period, group and
/// position on the classic grid, electron configurations (filled by the Madelung rule plus the known exceptions), and
/// a stable "element of the day". Only symbol, name and mass are typed in; everything else is derived from the atomic number.
/// </summary>
public static class PeriodicTableLogic
{
    public sealed record Category(string Id, string Name, string Color);

    /// <summary>Grid rows: 1–7 are the periods, row 8 is a gap, rows 9 and 10 are the lanthanides and actinides. Columns are 1–18.</summary>
    public sealed record Element(int Number, string Symbol, string Name, double Mass, string Category, int Period, int Group, int Row, int Column, bool MassIsApproximate);

    public static readonly Category[] Categories =
    [
        new("alkali", "Alkali metal", "#E0705F"),
        new("alkaline", "Alkaline earth metal", "#E39A55"),
        new("transition", "Transition metal", "#C9A94A"),
        new("post", "Post-transition metal", "#8FAE72"),
        new("metalloid", "Metalloid", "#5FA89B"),
        new("nonmetal", "Reactive nonmetal", "#5E9BC9"),
        new("halogen", "Halogen", "#7F86D6"),
        new("noble", "Noble gas", "#A87BC9"),
        new("lanthanide", "Lanthanide", "#CF7FA6"),
        new("actinide", "Actinide", "#B8688A"),
    ];

    public static string DefaultColors => string.Join('\n', Categories.Select(c => c.Name + " | " + c.Color));

    // Symbol, name, standard atomic mass (the mass number of the longest-lived isotope for elements without stable ones).
    private const string Raw =
        "H Hydrogen 1.008;He Helium 4.0026;Li Lithium 6.94;Be Beryllium 9.0122;B Boron 10.81;C Carbon 12.011;N Nitrogen 14.007;O Oxygen 15.999;F Fluorine 18.998;Ne Neon 20.180;" +
        "Na Sodium 22.990;Mg Magnesium 24.305;Al Aluminum 26.982;Si Silicon 28.085;P Phosphorus 30.974;S Sulfur 32.06;Cl Chlorine 35.45;Ar Argon 39.948;K Potassium 39.098;Ca Calcium 40.078;" +
        "Sc Scandium 44.956;Ti Titanium 47.867;V Vanadium 50.942;Cr Chromium 51.996;Mn Manganese 54.938;Fe Iron 55.845;Co Cobalt 58.933;Ni Nickel 58.693;Cu Copper 63.546;Zn Zinc 65.38;" +
        "Ga Gallium 69.723;Ge Germanium 72.630;As Arsenic 74.922;Se Selenium 78.971;Br Bromine 79.904;Kr Krypton 83.798;Rb Rubidium 85.468;Sr Strontium 87.62;Y Yttrium 88.906;Zr Zirconium 91.224;" +
        "Nb Niobium 92.906;Mo Molybdenum 95.95;Tc Technetium 98;Ru Ruthenium 101.07;Rh Rhodium 102.91;Pd Palladium 106.42;Ag Silver 107.87;Cd Cadmium 112.41;In Indium 114.82;Sn Tin 118.71;" +
        "Sb Antimony 121.76;Te Tellurium 127.60;I Iodine 126.90;Xe Xenon 131.29;Cs Cesium 132.91;Ba Barium 137.33;La Lanthanum 138.91;Ce Cerium 140.12;Pr Praseodymium 140.91;Nd Neodymium 144.24;" +
        "Pm Promethium 145;Sm Samarium 150.36;Eu Europium 151.96;Gd Gadolinium 157.25;Tb Terbium 158.93;Dy Dysprosium 162.50;Ho Holmium 164.93;Er Erbium 167.26;Tm Thulium 168.93;Yb Ytterbium 173.05;" +
        "Lu Lutetium 174.97;Hf Hafnium 178.49;Ta Tantalum 180.95;W Tungsten 183.84;Re Rhenium 186.21;Os Osmium 190.23;Ir Iridium 192.22;Pt Platinum 195.08;Au Gold 196.97;Hg Mercury 200.59;" +
        "Tl Thallium 204.38;Pb Lead 207.2;Bi Bismuth 208.98;Po Polonium 209;At Astatine 210;Rn Radon 222;Fr Francium 223;Ra Radium 226;Ac Actinium 227;Th Thorium 232.04;" +
        "Pa Protactinium 231.04;U Uranium 238.03;Np Neptunium 237;Pu Plutonium 244;Am Americium 243;Cm Curium 247;Bk Berkelium 247;Cf Californium 251;Es Einsteinium 252;Fm Fermium 257;" +
        "Md Mendelevium 258;No Nobelium 259;Lr Lawrencium 266;Rf Rutherfordium 267;Db Dubnium 268;Sg Seaborgium 269;Bh Bohrium 270;Hs Hassium 277;Mt Meitnerium 278;Ds Darmstadtium 281;" +
        "Rg Roentgenium 282;Cn Copernicium 285;Nh Nihonium 286;Fl Flerovium 289;Mc Moscovium 290;Lv Livermorium 293;Ts Tennessine 294;Og Oganesson 294";

    public static readonly IReadOnlyList<Element> All = Build();

    public static Element? ByNumber(int number) => number is >= 1 and <= 118 ? All[number - 1] : null;

    public static Element? Find(string? text)
    {
        string t = (text ?? "").Trim();
        if (t.Length == 0) return null;
        if (int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out int n)) return ByNumber(n);
        return All.FirstOrDefault(e => string.Equals(e.Symbol, t, StringComparison.OrdinalIgnoreCase) || string.Equals(e.Name, t, StringComparison.OrdinalIgnoreCase));
    }

    private static List<Element> Build()
    {
        var list = new List<Element>(118);
        var rows = Raw.Split(';');
        for (int i = 0; i < rows.Length; i++)
        {
            int z = i + 1;
            var f = rows[i].Split(' ');
            var (period, group, row, column) = Place(z);
            // Elements without a stable isotope list the mass number of their longest-lived isotope in brackets.
            bool approximate = z is 43 or 61 || (z >= 84 && z is not (90 or 91 or 92));
            list.Add(new Element(z, f[0], f[1], double.Parse(f[2], CultureInfo.InvariantCulture), CategoryOf(z), period, group, row, column, approximate));
        }
        return list;
    }

    /// <summary>The category by atomic number (lanthanides are 57–71 and actinides 89–103, as in the f-block rows below the table).</summary>
    public static string CategoryOf(int z) => z switch
    {
        3 or 11 or 19 or 37 or 55 or 87 => "alkali",
        4 or 12 or 20 or 38 or 56 or 88 => "alkaline",
        >= 57 and <= 71 => "lanthanide",
        >= 89 and <= 103 => "actinide",
        (>= 21 and <= 30) or (>= 39 and <= 48) or (>= 72 and <= 80) or (>= 104 and <= 112) => "transition",
        13 or 31 or 49 or 50 or 81 or 82 or 83 or 84 or (>= 113 and <= 116) => "post",
        5 or 14 or 32 or 33 or 51 or 52 => "metalloid",
        9 or 17 or 35 or 53 or 85 or 117 => "halogen",
        2 or 10 or 18 or 36 or 54 or 86 or 118 => "noble",
        _ => "nonmetal",
    };

    /// <summary>Period, group (0 for the f-block), grid row and grid column of an element.</summary>
    public static (int Period, int Group, int Row, int Column) Place(int z)
    {
        if (z == 1) return (1, 1, 1, 1);
        if (z == 2) return (1, 18, 1, 18);
        if (z is >= 57 and <= 71) return (6, 0, 9, 3 + (z - 57));
        if (z is >= 89 and <= 103) return (7, 0, 10, 3 + (z - 89));
        (int period, int group) = z switch
        {
            <= 4 => (2, z - 2),
            <= 10 => (2, z + 8),
            <= 12 => (3, z - 10),
            <= 18 => (3, z),
            <= 36 => (4, z - 18),
            <= 54 => (5, z - 36),
            <= 56 => (6, z - 54),
            <= 86 => (6, z - 68),
            <= 88 => (7, z - 86),
            _ => (7, z - 100),
        };
        return (period, group, period, group);
    }

    public static string Block(Element e) => e.Group == 0 ? "f" : e.Group is 1 or 2 || e.Number == 2 ? "s" : e.Group >= 13 ? "p" : "d";

    /// <summary>"Group 8", or "f-block" for the lanthanides and actinides.</summary>
    public static string GroupText(Element e) => e.Group == 0 ? "f-block" : "Group " + e.Group.ToString(CultureInfo.InvariantCulture);

    public static string CategoryName(string id) => Categories.FirstOrDefault(c => c.Id == id)?.Name ?? id;

    public static string MassText(Element e, int decimals = 2)
    {
        decimals = Math.Clamp(decimals, 0, 3);
        if (e.MassIsApproximate) return "[" + Math.Round(e.Mass).ToString("0", CultureInfo.InvariantCulture) + "]";
        decimal rounded = Math.Round((decimal)e.Mass, decimals, MidpointRounding.AwayFromZero);
        return rounded.ToString(decimals == 0 ? "0" : "0." + new string('#', decimals), CultureInfo.InvariantCulture);
    }

    // ---------- Colors ----------

    /// <summary>Lines "Category name | #color". Anything missing keeps its default color.</summary>
    public static Dictionary<string, string> ParseColors(IEnumerable<string> lines)
    {
        var colors = Categories.ToDictionary(c => c.Id, c => c.Color, StringComparer.Ordinal);
        foreach (var raw in lines)
        {
            int bar = raw.IndexOf('|');
            if (bar <= 0) continue;
            string name = raw[..bar].Trim(), color = raw[(bar + 1)..].Trim();
            var category = Categories.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase) || string.Equals(c.Id, name, StringComparison.OrdinalIgnoreCase));
            if (category is not null && StudySubjectsLogic.IsHex(color)) colors[category.Id] = color;
        }
        return colors;
    }

    // ---------- Element of the day ----------

    /// <summary>One element per day, cycling through all 118 before repeating, the same all day on every device.</summary>
    public static Element OfTheDay(DateTime date)
    {
        int day = (int)(date.Date - new DateTime(2000, 1, 1)).TotalDays;
        int index = (int)(((long)day * 47 % 118 + 118) % 118);
        return All[index];
    }

    // ---------- Electron configuration ----------

    // Ground states that break the Madelung filling order.
    private static readonly Dictionary<int, string> Exceptions = new()
    {
        [24] = "[Ar] 3d5 4s1", [29] = "[Ar] 3d10 4s1", [41] = "[Kr] 4d4 5s1", [42] = "[Kr] 4d5 5s1", [44] = "[Kr] 4d7 5s1",
        [45] = "[Kr] 4d8 5s1", [46] = "[Kr] 4d10", [47] = "[Kr] 4d10 5s1", [57] = "[Xe] 5d1 6s2", [58] = "[Xe] 4f1 5d1 6s2",
        [64] = "[Xe] 4f7 5d1 6s2", [78] = "[Xe] 4f14 5d9 6s1", [79] = "[Xe] 4f14 5d10 6s1", [89] = "[Rn] 6d1 7s2", [90] = "[Rn] 6d2 7s2",
        [91] = "[Rn] 5f2 6d1 7s2", [92] = "[Rn] 5f3 6d1 7s2", [93] = "[Rn] 5f4 6d1 7s2", [96] = "[Rn] 5f7 6d1 7s2", [103] = "[Rn] 5f14 7s2 7p1",
    };

    private static readonly (int Z, string Symbol)[] NobleGases = [(2, "He"), (10, "Ne"), (18, "Ar"), (36, "Kr"), (54, "Xe"), (86, "Rn")];
    private static readonly string[] SubshellLetters = ["s", "p", "d", "f"];

    /// <summary>The electron configuration in noble-gas shorthand, e.g. "[Ar] 3d6 4s2" for iron.</summary>
    public static string ElectronConfiguration(int z)
    {
        if (z is < 1 or > 118) return "";
        if (Exceptions.TryGetValue(z, out var known)) return known;
        var shells = Fill(z);
        var core = NobleGases.LastOrDefault(g => g.Z < z);
        var coreShells = core.Z > 0 ? Fill(core.Z) : [];
        var rest = shells.Where(s => !coreShells.Any(c => c.N == s.N && c.L == s.L)).OrderBy(s => s.N).ThenBy(s => s.L).ToList();
        string text = string.Join(' ', rest.Select(s => $"{s.N}{SubshellLetters[s.L]}{s.Count}"));
        return core.Z > 0 ? $"[{core.Symbol}] {text}" : text;
    }

    private static List<(int N, int L, int Count)> Fill(int electrons)
    {
        var order = new List<(int N, int L)>();
        for (int n = 1; n <= 7; n++)
            for (int l = 0; l <= Math.Min(3, n - 1); l++) order.Add((n, l));
        var filled = new List<(int, int, int)>();
        foreach (var (n, l) in order.OrderBy(o => o.N + o.L).ThenBy(o => o.N))
        {
            if (electrons <= 0) break;
            int take = Math.Min(electrons, 2 * (2 * l + 1));
            filled.Add((n, l, take));
            electrons -= take;
        }
        return filled;
    }
}
