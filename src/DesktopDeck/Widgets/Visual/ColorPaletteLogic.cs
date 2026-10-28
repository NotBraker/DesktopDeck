using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DesktopDeck.Widgets;

/// <summary>Parsing, presets and copy formats for the color palette. Pure logic (unit tested).</summary>
public static class ColorPaletteLogic
{
    /// <summary>One swatch. <see cref="Hex"/> is always "#RRGGBB" in capitals.</summary>
    public sealed record Swatch(string Hex, string Name, int R, int G, int B)
    {
        public string Rgb => $"{R}, {G}, {B}";
    }

    public static readonly string[] PresetNames = ["Custom", "Dusk", "Pastel", "Material", "Nord", "Grayscale", "Sunset", "Forest"];

    /// <summary>Formats a swatch can be copied as.</summary>
    public static readonly string[] CopyFormats = ["hex", "hex-bare", "rgb", "hsl"];

    /// <summary>Reads one "#hex | name" line: #RGB or #RRGGBB (the # is optional). Null for anything else.</summary>
    public static Swatch? Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var parts = line.Split('|', 2);
        string hex = parts[0].Trim().TrimStart('#');
        if (hex.Length == 3) hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
        if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int value)) return null;
        return new Swatch("#" + hex.ToUpperInvariant(), parts.Length > 1 ? parts[1].Trim() : "", (value >> 16) & 255, (value >> 8) & 255, value & 255);
    }

    /// <summary>Every valid line of a text, at most <paramref name="max"/>.</summary>
    public static List<Swatch> ParseAll(string? text, int max = 100) =>
        (text ?? "").Split('\n').Select(Parse).OfType<Swatch>().Take(Math.Max(0, max)).ToList();

    /// <summary>The lines of a built-in palette (Custom and unknown names give Dusk).</summary>
    public static string Preset(string? name) => name switch
    {
        "Pastel" => "#F5B8C3 | Rose\n#F9D5A7 | Peach\n#F4E3A1 | Butter\n#B8DBCF | Mint\n#A9D4E8 | Sky\n#C6C1EB | Lavender",
        "Material" => "#F44336 | Red\n#FF9800 | Orange\n#FFC107 | Amber\n#4CAF50 | Green\n#2196F3 | Blue\n#9C27B0 | Purple",
        "Nord" => "#2E3440 | Polar night\n#4C566A | Storm\n#88C0D0 | Frost\n#81A1C1 | Glacier\n#A3BE8C | Aurora\n#EBCB8B | Sun",
        "Grayscale" => "#111111 | Black\n#333333 | Ink\n#666666 | Graphite\n#999999 | Ash\n#CCCCCC | Mist\n#F5F5F5 | Paper",
        "Sunset" => "#2B1B3D | Night\n#6B2E5F | Plum\n#C2456F | Berry\n#F2745F | Coral\n#F9A65A | Amber\n#FFD98E | Glow",
        "Forest" => "#1F3A2E | Pine\n#2E5E46 | Fern\n#5B8C5A | Moss\n#A3B18A | Sage\n#DAD7CD | Birch\n#8B6B4A | Bark",
        _ => "#D8AF68 | Gold\n#B398CD | Lilac\n#73BDA7 | Sage\n#E68B8B | Rose\n#6FA8DC | Sky\n#C4684F | Clay",
    };

    /// <summary>The text copied for a swatch: "hex" (#D8AF68), "hex-bare" (D8AF68), "rgb" (rgb(216, 175, 104)) or "hsl".</summary>
    public static string Format(Swatch swatch, string format, bool uppercase = true)
    {
        switch (format)
        {
            case "rgb": return $"rgb({swatch.R}, {swatch.G}, {swatch.B})";
            case "hsl":
                var (h, s, l) = Hsl(swatch.R, swatch.G, swatch.B);
                return $"hsl({h}, {s}%, {l}%)";
        }
        string hex = format == "hex-bare" ? swatch.Hex[1..] : swatch.Hex;
        return uppercase ? hex : hex.ToLowerInvariant();
    }

    /// <summary>Hue (0–359), saturation and lightness (0–100) of an RGB color.</summary>
    public static (int H, int S, int L) Hsl(int r, int g, int b)
    {
        double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
        double max = Math.Max(rf, Math.Max(gf, bf)), min = Math.Min(rf, Math.Min(gf, bf));
        double l = (max + min) / 2, d = max - min, h = 0, s = 0;
        if (d > 1e-9)
        {
            s = d / (1 - Math.Abs(2 * l - 1));
            if (max == rf) h = 60 * (((gf - bf) / d % 6 + 6) % 6);
            else if (max == gf) h = 60 * ((bf - rf) / d + 2);
            else h = 60 * ((rf - gf) / d + 4);
        }
        return ((int)Math.Round(h) % 360, (int)Math.Round(s * 100), (int)Math.Round(l * 100));
    }

    /// <summary>True for colors light enough that dark text reads best on them.</summary>
    public static bool IsLight(Swatch swatch) => (0.2126 * swatch.R + 0.7152 * swatch.G + 0.0722 * swatch.B) / 255.0 > 0.62;

    /// <summary>All of a palette's copy texts, one per line ("Name: #HEX" when it has a name).</summary>
    public static string AllAsText(IEnumerable<Swatch> swatches, string format, bool uppercase = true) =>
        string.Join("\n", swatches.Select(s => (s.Name.Length > 0 ? s.Name + ": " : "") + Format(s, format, uppercase)));
}
