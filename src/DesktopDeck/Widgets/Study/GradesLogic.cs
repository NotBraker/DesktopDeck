using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DesktopDeck.Widgets;

/// <summary>
/// Grades and GPA math: parsing "Class | grade | credits | level" lines (grade as a percent, a fraction or a letter),
/// a configurable letter scale, weighted average, 4.0 and 5.0 GPA, and the "what do I need on the final" formula.
/// </summary>
public static class GradesLogic
{
    /// <summary>One line of the letter scale: from <see cref="Min"/> percent up is <see cref="Letter"/>, worth <see cref="Points"/> on the GPA scale.</summary>
    public sealed record Band(double Min, string Letter, double Points);

    /// <summary>A class. <see cref="Percent"/> is null until it has a grade. <see cref="Level"/> is "", "honors" or "ap".</summary>
    public sealed record Course(string Name, double? Percent, double Credits, string Level, bool Understood, string GradeText = "");

    public sealed record Summary(double? Average, double? Gpa, double? WeightedGpa, double Credits, int Graded);

    public enum FinalStatus { Needs, Safe, OutOfReach, NoFinal }
    public sealed record FinalResult(FinalStatus Status, double Needed);

    public const string DefaultScale = "97 | A+ | 4.0\n93 | A | 4.0\n90 | A- | 3.7\n87 | B+ | 3.3\n83 | B | 3.0\n80 | B- | 2.7\n77 | C+ | 2.3\n73 | C | 2.0\n70 | C- | 1.7\n67 | D+ | 1.3\n63 | D | 1.0\n60 | D- | 0.7\n0 | F | 0.0";
    public const double MaxPercent = 200;

    // ---------- The letter scale ----------

    /// <summary>Lines "min % | letter | points", highest first. A band starting at 0 is always present (F, 0.0 if you left it out).</summary>
    public static List<Band> ParseScale(IEnumerable<string> lines)
    {
        var bands = new List<Band>();
        foreach (var raw in lines)
        {
            var parts = raw.Split('|').Select(p => p.Trim()).ToArray();
            if (parts.Length < 3 || parts[1].Length == 0 || parts[1].Length > 4) continue;
            if (!TryNumber(parts[0].TrimEnd('%', ' '), out double min) || !TryNumber(parts[2], out double points)) continue;
            if (min < 0 || min > MaxPercent || points < 0 || points > 10) continue;
            if (bands.Any(b => b.Min == min || string.Equals(b.Letter, parts[1], StringComparison.OrdinalIgnoreCase))) continue;
            bands.Add(new Band(min, parts[1], points));
        }
        if (bands.Count == 0) return ParseScale(DefaultScale.Split('\n'));
        if (bands.All(b => b.Min > 0)) bands.Add(new Band(0, "F", 0));
        return bands.OrderByDescending(b => b.Min).ToList();
    }

    public static Band BandOf(double percent, IReadOnlyList<Band> scale) => scale.FirstOrDefault(b => percent + 1e-9 >= b.Min) ?? scale[^1];

    public static Band? BandOfLetter(string letter, IReadOnlyList<Band> scale) =>
        scale.FirstOrDefault(b => string.Equals(b.Letter, letter.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The middle of a letter's band (A- of 90–93 is 91.5); the top band reaches 100 and the bottom one is 50.</summary>
    public static double? LetterToPercent(string letter, IReadOnlyList<Band> scale)
    {
        if (BandOfLetter(letter, scale) is not { } band) return null;
        if (band.Min <= 0) return Math.Min(50, scale.Count > 1 ? scale[^2].Min : 50);
        int index = scale.ToList().IndexOf(band);
        double upper = index == 0 ? 100 : scale[index - 1].Min;
        return Math.Round((band.Min + Math.Max(band.Min, upper)) / 2, 2);
    }

    // ---------- Grades ----------

    /// <summary>A percent ("92", "92.5%"), a fraction ("18/20") or a letter ("B+") as a percent; null if it is none of those.</summary>
    public static double? ParseGrade(string? text, IReadOnlyList<Band> scale)
    {
        string t = (text ?? "").Trim();
        if (t.Length == 0) return null;
        if (t.EndsWith('%')) t = t[..^1].Trim();
        int slash = t.IndexOf('/');
        if (slash > 0)
        {
            if (TryNumber(t[..slash], out double got) && TryNumber(t[(slash + 1)..], out double outOf) && outOf > 0 && got >= 0) return Math.Min(MaxPercent, Math.Round(got / outOf * 100, 4));
            return null;
        }
        if (TryNumber(t, out double percent)) return percent is >= 0 and <= MaxPercent ? percent : null;
        return LetterToPercent(t, scale);
    }

    /// <summary>Lines "Class | grade | credits | level". Credits default to 1; level is H / Honors / AP / IB / DE. A class without a grade yet is kept but not counted.</summary>
    public static List<Course> ParseCourses(IEnumerable<string> lines, IReadOnlyList<Band> scale, int max = 24)
    {
        var courses = new List<Course>();
        foreach (var raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var parts = line.Split('|').Select(p => p.Trim()).ToArray();
            if (parts[0].Length == 0) continue;
            string name = parts[0].Length > 40 ? parts[0][..40] : parts[0];
            double? percent = parts.Length > 1 ? ParseGrade(parts[1], scale) : null;
            bool understood = parts.Length < 2 || parts[1].Length == 0 || percent is not null;
            double credits = 1;
            if (parts.Length > 2 && parts[2].Length > 0)
            {
                string c = new(parts[2].TakeWhile(ch => char.IsDigit(ch) || ch is '.' or ',').ToArray());
                if (TryNumber(c, out double value) && value > 0 && value <= 100) credits = value; else understood = false;
            }
            string level = parts.Length > 3 ? LevelOf(parts[3]) : "";
            courses.Add(new Course(name, percent, credits, level, understood, parts.Length > 1 ? parts[1] : ""));
            if (courses.Count >= max) break;
        }
        return courses;
    }

    // ---------- Editing the text of the classes option ----------

    private static string[] SplitLine(string line) => line.Split('|').Select(p => p.Trim()).ToArray();

    private static int FindLine(string[] lines, string className) =>
        Array.FindIndex(lines, l => l.TrimStart().Length > 0 && l.TrimStart()[0] != '#' && string.Equals(SplitLine(l)[0], className.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Changes one field of a class line (1 = grade, 2 = credits, 3 = level), keeping the other lines as they are.</summary>
    public static string SetField(string text, string className, int field, string value)
    {
        var lines = text.Split('\n');
        int at = FindLine(lines, className);
        if (at < 0 || field < 1 || field > 3) return text;
        var parts = SplitLine(lines[at]).ToList();
        while (parts.Count <= field) parts.Add(parts.Count == 2 ? "1" : "");
        parts[field] = value.Trim();
        while (parts.Count > 1 && parts[^1].Length == 0) parts.RemoveAt(parts.Count - 1);
        lines[at] = string.Join(" | ", parts);
        return string.Join('\n', lines);
    }

    public static string RemoveClass(string text, string className)
    {
        var lines = text.Split('\n').ToList();
        int at = FindLine([.. lines], className);
        if (at >= 0) lines.RemoveAt(at);
        return string.Join('\n', lines);
    }

    public static string AddClass(string text, string line) => (text.TrimEnd().Length == 0 ? "" : text.TrimEnd() + "\n") + line.Trim();

    public static string LevelOf(string text) => text.Trim().ToLowerInvariant() switch
    {
        "h" or "hon" or "honor" or "honors" or "honours" => "honors",
        "ap" or "ib" or "de" or "dual" or "college" => "ap",
        _ => "",
    };

    public static Summary Summarize(IEnumerable<Course> courses, IReadOnlyList<Band> scale, double honorsBonus = 0.5, double apBonus = 1.0)
    {
        double credits = 0, percentSum = 0, points = 0, weightedPoints = 0;
        int graded = 0;
        foreach (var c in courses)
        {
            if (c.Percent is not { } p) continue;
            double basePoints = BandOf(p, scale).Points;
            double bonus = basePoints <= 0 ? 0 : c.Level == "ap" ? apBonus : c.Level == "honors" ? honorsBonus : 0;
            credits += c.Credits;
            percentSum += p * c.Credits;
            points += basePoints * c.Credits;
            weightedPoints += (basePoints + bonus) * c.Credits;
            graded++;
        }
        if (credits <= 0) return new Summary(null, null, null, 0, 0);
        return new Summary(percentSum / credits, points / credits, weightedPoints / credits, credits, graded);
    }

    /// <summary>0 = A, 1 = B, 2 = C, 3 = D, 4 = F: the first letter of the band, so a custom scale still colors sensibly.</summary>
    public static int Bucket(double percent, IReadOnlyList<Band> scale) => char.ToUpperInvariant(BandOf(percent, scale).Letter[0]) switch
    {
        'A' => 0, 'B' => 1, 'C' => 2, 'D' => 3, _ => 4,
    };

    public static List<Course> Sort(IEnumerable<Course> courses, string mode) => mode switch
    {
        "high" => courses.OrderByDescending(c => c.Percent ?? -1).ToList(),
        "low" => courses.OrderBy(c => c.Percent ?? 1000).ToList(),
        "name" => courses.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
        _ => courses.ToList(),
    };

    // ---------- The final exam ----------

    /// <summary>
    /// What you need on the final: the final counts <paramref name="finalWeight"/> percent, the rest of the grade is
    /// <paramref name="current"/>. needed = (target − current × (1 − w)) / w.
    /// </summary>
    public static FinalResult NeededOnFinal(double current, double finalWeight, double target, double ceiling = 100)
    {
        double w = finalWeight / 100;
        if (!double.IsFinite(w) || w <= 0 || w > 1) return new FinalResult(FinalStatus.NoFinal, double.NaN);
        double needed = (target - current * (1 - w)) / w;
        if (needed <= 0) return new FinalResult(FinalStatus.Safe, needed);
        return new FinalResult(needed > ceiling + 1e-9 ? FinalStatus.OutOfReach : FinalStatus.Needs, needed);
    }

    /// <summary>The overall grade after scoring <paramref name="score"/> on the final.</summary>
    public static double ProjectedGrade(double current, double finalWeight, double score) => current * (1 - finalWeight / 100) + score * (finalWeight / 100);

    /// <summary>"90", "90%" or a letter ("A-" means reaching the bottom of the A- band) as a percent.</summary>
    public static double? ParseTarget(string? text, IReadOnlyList<Band> scale)
    {
        string t = (text ?? "").Trim();
        if (t.EndsWith('%')) t = t[..^1].Trim();
        if (TryNumber(t, out double value)) return value is >= 0 and <= MaxPercent ? value : null;
        return BandOfLetter(t, scale)?.Min;
    }

    /// <summary>The needed score for each whole-letter target (A, B, C, D) above the current grade's floor.</summary>
    public static List<(string Letter, double Min, FinalResult Result)> FinalTable(double current, double finalWeight, IReadOnlyList<Band> scale) =>
        scale.Where(b => b.Letter.Length == 1 && b.Min > 0).OrderByDescending(b => b.Min).Select(b => (b.Letter, b.Min, NeededOnFinal(current, finalWeight, b.Min))).ToList();

    public static string DescribeFinal(FinalResult result, double target, int decimals = 1) => result.Status switch
    {
        FinalStatus.NoFinal => "The final's weight must be between 0 and 100 percent.",
        FinalStatus.Safe => $"You already have {FormatPercent(target, decimals)}: even a 0 on the final keeps you there.",
        FinalStatus.OutOfReach => $"You would need {FormatPercent(result.Needed, decimals)} on the final, more than a perfect score.",
        _ => $"You need {FormatPercent(result.Needed, decimals)} on the final to finish with {FormatPercent(target, decimals)}.",
    };

    // ---------- Text ----------

    public static string FormatPercent(double value, int decimals = 1) =>
        value.ToString(decimals <= 0 ? "0" : "0." + new string('#', Math.Min(decimals, 3)), CultureInfo.InvariantCulture) + "%";

    public static string FormatGpa(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static bool TryNumber(string text, out double value)
    {
        text = text.Trim().Replace(',', '.');
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }
}
