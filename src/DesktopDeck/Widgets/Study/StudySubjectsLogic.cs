using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DesktopDeck.Widgets;

/// <summary>Shared by the Study widgets: subject lists written as lines "Name | #color", and a few small helpers.</summary>
public static class StudySubjectsLogic
{
    public sealed record Subject(string Name, string Color);

    /// <summary>Colors handed out to subjects that don't name one, in order.</summary>
    public static readonly string[] Palette = ["#C2A14D", "#5E9BA6", "#C4684F", "#9B9A5E", "#8E7CC3", "#D98A5B", "#6FA37A", "#B8738F"];

    public const string DefaultSubjects = "History | #C2A14D\nLatin | #C4684F\nChem | #5E9BA6\nMath | #9B9A5E";

    /// <summary>"#rgb", "#rrggbb" or "#aarrggbb".</summary>
    public static bool IsHex(string? text) => text is { Length: 4 or 7 or 9 } && text[0] == '#' && text.Skip(1).All(Uri.IsHexDigit);

    /// <summary>Parses "Name | #color" lines. Duplicate names (ignoring case) and empty names are dropped.</summary>
    public static List<Subject> Parse(IEnumerable<string> lines, int max = 24)
    {
        var result = new List<Subject>();
        foreach (var raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            string name = line, color = "";
            int bar = line.IndexOf('|');
            if (bar >= 0) { name = line[..bar].Trim(); color = line[(bar + 1)..].Trim(); }
            if (name.Length == 0) continue;
            if (name.Length > 40) name = name[..40];
            if (result.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))) continue;
            if (!IsHex(color)) color = Palette[result.Count % Palette.Length];
            result.Add(new Subject(name, color));
            if (result.Count >= max) break;
        }
        return result;
    }

    public static Subject? Find(IReadOnlyList<Subject> subjects, string? name) =>
        subjects.FirstOrDefault(s => string.Equals(s.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The subject's color; unknown subjects get a color derived from their name so it stays the same.</summary>
    public static string ColorOf(IReadOnlyList<Subject> subjects, string? name)
    {
        if (Find(subjects, name) is { } known) return known.Color;
        if (string.IsNullOrWhiteSpace(name)) return "#8E8270";
        int hash = 17;
        foreach (char c in name.Trim().ToLowerInvariant()) hash = unchecked(hash * 31 + c);
        return Palette[(int)((uint)hash % (uint)Palette.Length)];
    }

    public static string Number(double value, string format = "0.##") => value.ToString(format, CultureInfo.InvariantCulture);
}
