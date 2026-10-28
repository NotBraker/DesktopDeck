using System.Globalization;

namespace DesktopDeck.Models;

/// <summary>
/// The deck's color theme: one of the built-in palettes (or a custom one), an optional accent that replaces the palette's own, and
/// the font every label uses. Plain text values only (no WPF), so settings and tests can use it; <see cref="ThemePalette"/> turns it into colors.
/// </summary>
public sealed class ThemeSettings
{
    public const string IdLight = "light", IdDusk = "dusk", IdMidnight = "midnight", IdContrast = "contrast", IdCustom = "custom";
    public const string DefaultFont = "inter";

    /// <summary>The theme names, in the order the picker shows them.</summary>
    public static readonly string[] Names = [IdLight, IdDusk, IdMidnight, IdContrast, IdCustom];

    /// <summary>The font choices: id, label and the WPF font family list (ends with Segoe UI, which every Windows has).</summary>
    public static readonly (string Id, string Label, string Family)[] Fonts =
    [
        ("inter", "Inter", "Inter, Segoe UI Variable Text, Segoe UI"),
        ("variable", "Segoe UI Variable", "Segoe UI Variable Text, Segoe UI"),
        ("segoe", "Segoe UI", "Segoe UI"),
        ("bahnschrift", "Bahnschrift", "Bahnschrift, Segoe UI"),
        ("cascadia", "Cascadia", "Cascadia Code, Cascadia Mono, Consolas, Segoe UI"),
        ("georgia", "Georgia", "Georgia, Segoe UI"),
    ];

    /// <summary>"light", "dusk" (the default), "midnight", "contrast" or "custom".</summary>
    public string Name { get; set; } = IdDusk;
    /// <summary>An accent color (hex) that replaces the theme's own; empty = the theme's accent.</summary>
    public string Accent { get; set; } = "";
    /// <summary>One of the ids in <see cref="Fonts"/>.</summary>
    public string Font { get; set; } = DefaultFont;
    /// <summary>The colors of the "custom" theme.</summary>
    public CustomPalette Custom { get; set; } = new();

    public static string FamilyOf(string? font) => (Fonts.FirstOrDefault(f => f.Id == font).Family ?? Fonts[0].Family);

    public ThemeSettings Copy() => new() { Name = Name, Accent = Accent, Font = Font, Custom = Custom.Copy() };

    /// <summary>True when this is the built-in look (Dusk, no accent override, Inter).</summary>
    public bool IsDefault => Name == IdDusk && Accent.Length == 0 && Font == DefaultFont;

    /// <summary>Repairs a loaded (possibly hand-edited or null) theme block.</summary>
    public static ThemeSettings Normalized(ThemeSettings? value)
    {
        var t = value ?? new ThemeSettings();
        t.Name = (t.Name ?? "").Trim().ToLowerInvariant();
        if (!Names.Contains(t.Name)) t.Name = IdDusk;
        t.Accent = HexColor.Clean(t.Accent);
        t.Font = (t.Font ?? "").Trim().ToLowerInvariant();
        if (!Fonts.Any(f => f.Id == t.Font)) t.Font = DefaultFont;
        t.Custom = CustomPalette.Normalized(t.Custom);
        return t;
    }
}

/// <summary>The five colors the user picks for the "custom" theme. Everything else (hover, lines, secondary text) is derived from them.</summary>
public sealed class CustomPalette
{
    public string Background { get; set; } = "#1A1611";
    public string Surface { get; set; } = "#221C15";
    public string Text { get; set; } = "#EDE4D3";
    public string Muted { get; set; } = "#8E8270";
    public string Accent { get; set; } = "#C4684F";

    public CustomPalette Copy() => (CustomPalette)MemberwiseClone();

    public static CustomPalette Normalized(CustomPalette? value)
    {
        var p = value ?? new CustomPalette();
        var d = new CustomPalette();
        p.Background = HexColor.Clean(p.Background) is { Length: > 0 } b ? b : d.Background;
        p.Surface = HexColor.Clean(p.Surface) is { Length: > 0 } s ? s : d.Surface;
        p.Text = HexColor.Clean(p.Text) is { Length: > 0 } t ? t : d.Text;
        p.Muted = HexColor.Clean(p.Muted) is { Length: > 0 } m ? m : d.Muted;
        p.Accent = HexColor.Clean(p.Accent) is { Length: > 0 } a ? a : d.Accent;
        return p;
    }
}

/// <summary>Hex color text (#RGB, #ARGB, #RRGGBB, #AARRGGBB) and the small amount of color math the themes need.</summary>
public static class HexColor
{
    /// <summary>The text when it is a valid hex color, otherwise empty. Always upper-case and written in full (#RRGGBB or #AARRGGBB).</summary>
    public static string Clean(string? value)
    {
        if (!TryParse(value, out byte a, out byte r, out byte g, out byte b)) return "";
        return Format(a, r, g, b);
    }

    public static bool TryParse(string? value, out byte a, out byte r, out byte g, out byte b)
    {
        a = 255; r = g = b = 0;
        string text = value?.Trim() ?? "";
        if (text.Length < 4 || text[0] != '#' || !text.Skip(1).All(Uri.IsHexDigit)) return false;
        string digits = text[1..];
        if (digits.Length is 3 or 4) digits = string.Concat(digits.Select(c => new string(c, 2)));
        if (digits.Length is not (6 or 8)) return false;
        if (digits.Length == 8) { a = Byte(digits, 0); digits = digits[2..]; }
        r = Byte(digits, 0); g = Byte(digits, 2); b = Byte(digits, 4);
        return true;
    }

    private static byte Byte(string s, int at) => byte.Parse(s.AsSpan(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    public static string Format(byte a, byte r, byte g, byte b)
        => a == 255 ? $"#{r:X2}{g:X2}{b:X2}" : $"#{a:X2}{r:X2}{g:X2}{b:X2}";

    /// <summary><paramref name="amount"/> (0–1) of <paramref name="over"/> laid on <paramref name="under"/>, opaque.</summary>
    public static string Mix(string under, string over, double amount)
    {
        if (!TryParse(under, out _, out byte r1, out byte g1, out byte b1) || !TryParse(over, out _, out byte r2, out byte g2, out byte b2)) return under;
        byte Blend(byte x, byte y) => (byte)Math.Round(x + (y - x) * Math.Clamp(amount, 0, 1));
        return Format(255, Blend(r1, r2), Blend(g1, g2), Blend(b1, b2));
    }

    /// <summary>The color with a different opacity (0–1), as #AARRGGBB.</summary>
    public static string WithAlpha(string color, double opacity)
    {
        if (!TryParse(color, out _, out byte r, out byte g, out byte b)) return color;
        return Format((byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255), r, g, b);
    }

    /// <summary>WCAG relative luminance, 0 (black) to 1 (white). Transparency is ignored.</summary>
    public static double Luminance(string color)
    {
        if (!TryParse(color, out _, out byte r, out byte g, out byte b)) return 0;
        static double Channel(byte c) { double s = c / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);
    }

    /// <summary>WCAG contrast ratio, 1 to 21.</summary>
    public static double Contrast(string a, string b)
    {
        double x = Luminance(a), y = Luminance(b);
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }
}
