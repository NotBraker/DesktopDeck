using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DesktopDeck.Widgets;

/// <summary>Dice &amp; random: dice, coin flips, random numbers, picking from a list, yes/no. Pure logic (pass in the Random).</summary>
public static class DiceLogic
{
    public static readonly int[] Sides = [4, 6, 8, 10, 12, 20, 100];

    /// <summary>Rolls <paramref name="count"/> dice (1–6) with <paramref name="sides"/> faces (2–1000).</summary>
    public static int[] Roll(Random random, int count, int sides)
    {
        count = Math.Clamp(count, 1, 6);
        sides = Math.Clamp(sides, 2, 1000);
        var faces = new int[count];
        for (int i = 0; i < count; i++) faces[i] = random.Next(1, sides + 1);
        return faces;
    }

    /// <summary>"d20" → 20; "6" → 6; anything else → 6.</summary>
    public static int ParseSides(string? text)
    {
        string t = (text ?? "").Trim().TrimStart('d', 'D');
        return int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= 2 && n <= 1000 ? n : 6;
    }

    /// <summary>True = heads.</summary>
    public static bool Coin(Random random) => random.Next(2) == 0;

    /// <summary>A whole number from min to max inclusive (swapped if reversed).</summary>
    public static long Number(Random random, long min, long max)
    {
        if (min > max) (min, max) = (max, min);
        if (min == max) return min;
        if (max != long.MaxValue) return random.NextInt64(min, max + 1);
        if (min != long.MinValue) return random.NextInt64(min - 1, max) + 1;
        var bytes = new byte[8];
        random.NextBytes(bytes);
        return BitConverter.ToInt64(bytes);
    }

    /// <summary>"Yes" / "No", or also "Maybe".</summary>
    public static string YesNo(Random random, bool maybe) => maybe
        ? random.Next(3) switch { 0 => "Yes", 1 => "No", _ => "Maybe" }
        : random.Next(2) == 0 ? "Yes" : "No";

    /// <summary>
    /// Picks one item. With <paramref name="noRepeat"/>, items already in <paramref name="used"/> are skipped until all
    /// have been picked, then the round starts over. Returns the item and the new used list (null item if empty).
    /// </summary>
    public static (string? Item, List<string> Used) Pick(Random random, IReadOnlyList<string> items, IEnumerable<string> used, bool noRepeat)
    {
        if (items.Count == 0) return (null, used.ToList());
        if (!noRepeat) return (items[random.Next(items.Count)], []);
        var usedList = used.Where(items.Contains).ToList();
        var left = items.Where(i => !usedList.Contains(i)).ToList();
        if (left.Count == 0) { usedList.Clear(); left = items.ToList(); }
        string pick = left[random.Next(left.Count)];
        usedList.Add(pick);
        return (pick, usedList);
    }

    /// <summary>
    /// Pip positions for a d6 face (1–6) on a 3×3 grid as (column, row) pairs, 0–2 each.
    /// </summary>
    public static (int Col, int Row)[] Pips(int face) => face switch
    {
        1 => [(1, 1)],
        2 => [(0, 0), (2, 2)],
        3 => [(0, 0), (1, 1), (2, 2)],
        4 => [(0, 0), (2, 0), (0, 2), (2, 2)],
        5 => [(0, 0), (2, 0), (1, 1), (0, 2), (2, 2)],
        6 => [(0, 0), (2, 0), (0, 1), (2, 1), (0, 2), (2, 2)],
        _ => [],
    };

    /// <summary>The outline used to draw a die: d4 triangle, d6 square, d8 diamond, d10 kite, d12 pentagon, d20 hexagon, d100 circle.</summary>
    public static string Shape(int sides) => sides switch
    {
        4 => "triangle", 8 => "diamond", 10 => "kite", 12 => "pentagon", 20 => "hexagon", 100 => "circle", _ => "square",
    };

    /// <summary>Corner points of a polygon shape in a unit square (0–1 each); empty for "square" and "circle".</summary>
    public static (double X, double Y)[] ShapePoints(string shape)
    {
        static (double, double)[] Regular(int corners, double cy)
            => Enumerable.Range(0, corners).Select(i =>
            {
                double angle = (-90 + 360.0 * i / corners) * Math.PI / 180;
                return (0.5 + 0.48 * Math.Cos(angle), cy + 0.48 * Math.Sin(angle));
            }).ToArray();
        return shape switch
        {
            "triangle" => [(0.5, 0.04), (0.97, 0.92), (0.03, 0.92)],
            "diamond" => [(0.5, 0.0), (1.0, 0.5), (0.5, 1.0), (0.0, 0.5)],
            "kite" => [(0.5, 0.0), (0.96, 0.38), (0.5, 1.0), (0.04, 0.38)],
            "pentagon" => Regular(5, 0.52),
            "hexagon" => Regular(6, 0.5),
            _ => [],
        };
    }

    /// <summary>Short label for the dice setup: "2d6", "d20".</summary>
    public static string DiceLabel(int count, int sides)
        => (count > 1 ? count.ToString(CultureInfo.InvariantCulture) : "") + "d" + sides.ToString(CultureInfo.InvariantCulture);

    /// <summary>The biggest possible total for the dice, for context ("2–12").</summary>
    public static string Range(int count, int sides)
        => count.ToString(CultureInfo.InvariantCulture) + "–" + (count * sides).ToString(CultureInfo.InvariantCulture);

    /// <summary>History text for a dice roll: "d6: 3 + 5 = 8" or "d20: 17".</summary>
    public static string Describe(int[] faces, int sides, bool sum)
    {
        string label = (faces.Length > 1 ? faces.Length.ToString(CultureInfo.InvariantCulture) : "") + "d" + sides.ToString(CultureInfo.InvariantCulture);
        string values = string.Join(faces.Length > 1 && sum ? " + " : ", ", faces.Select(f => f.ToString(CultureInfo.InvariantCulture)));
        return faces.Length > 1 && sum ? $"{label}: {values} = {faces.Sum().ToString(CultureInfo.InvariantCulture)}" : $"{label}: {values}";
    }

    /// <summary>Adds an entry to the front of a newline-separated history, keeping at most <paramref name="keep"/>.</summary>
    public static string PushHistory(string history, string entry, int keep)
    {
        var lines = new List<string> { entry.Replace('\n', ' ').Replace('\r', ' ') };
        lines.AddRange(history.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
        return string.Join("\n", lines.Take(Math.Max(0, keep)));
    }

    /// <summary>Reads "1, 100", "1-100", "1 to 100" or "−5 – 5" into two whole numbers (null if there are not two).</summary>
    public static (long Min, long Max)? ParseRange(string? text)
    {
        var numbers = System.Text.RegularExpressions.Regex.Matches((text ?? "").Replace('−', '-'), @"(?<![\d])-?\d+")
            .Select(m => long.TryParse(m.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n) ? (long?)n : null)
            .OfType<long>().ToArray();
        if (numbers.Length < 2) return null;
        return (Math.Min(numbers[0], numbers[1]), Math.Max(numbers[0], numbers[1]));
    }

    /// <summary>Marks a history entry with the mode it came from ("coin|Heads") so each mode shows only its own rolls.</summary>
    public static string Tag(string mode, string text) => mode.Replace('|', ' ') + "|" + text.Replace('\n', ' ').Replace('\r', ' ');

    /// <summary>The newest <paramref name="take"/> entries of <paramref name="mode"/> from a tagged history, newest first.</summary>
    public static List<string> HistoryFor(string history, string mode, int take)
    {
        string prefix = mode + "|";
        return history.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith(prefix, StringComparison.Ordinal))
            .Select(l => l[prefix.Length..]).Take(Math.Max(0, take)).ToList();
    }

    /// <summary>Encodes faces for storage: "3,5".</summary>
    public static string Encode(IEnumerable<int> faces) => string.Join(",", faces.Select(f => f.ToString(CultureInfo.InvariantCulture)));

    /// <summary>Decodes "3,5" (ignoring junk).</summary>
    public static int[] Decode(string? text) => (text ?? "").Split(',')
        .Select(s => int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0)
        .Where(n => n > 0).ToArray();
}

