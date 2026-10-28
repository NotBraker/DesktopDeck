namespace DesktopDeck.Models;

/// <summary>
/// Every color the deck draws with, as hex text (so the built-in palettes can be checked without WPF). <c>Theme.Apply</c> turns one into brushes.
/// Line and LineStrong are translucent (#AARRGGBB); everything else is opaque.
/// </summary>
public sealed record ThemePalette(
    string Id, string Label, bool IsDark,
    string Background, string Surface, string SurfaceHover, string Surface3, string Line, string LineStrong,
    string Text, string Text2, string Muted, string Dim, string Primary, string Olive, string OnPrimary, string Danger)
{
    /// <summary>The accent colors that mean "the theme's own accent" when an item still has its original default.</summary>
    public const string OriginalPrimary = "#C4684F", OriginalOlive = "#9B9A5E";

    public static readonly ThemePalette Dusk = new(ThemeSettings.IdDusk, "Dusk", true,
        "#1A1611", "#221C15", "#2A231B", "#2E261D", "#1AEDE4D3", "#33EDE4D3",
        "#EDE4D3", "#C9BDA8", "#8E8270", "#5C5446", OriginalPrimary, OriginalOlive, "#FFF6EC", "#E07A6A");

    public static readonly ThemePalette Light = new(ThemeSettings.IdLight, "Light", false,
        "#EFE9DF", "#FBF8F3", "#F3EEE5", "#E8E1D5", "#242A2218", "#402A2218",
        "#2A2218", "#4F4436", "#6B5F50", "#A89C8A", "#B5523A", "#6F6F32", "#FFFFFF", "#B83A2B");

    public static readonly ThemePalette Midnight = new(ThemeSettings.IdMidnight, "Midnight", true,
        "#0E131C", "#151C28", "#1B2433", "#202B3D", "#1AD7E3F5", "#33D7E3F5",
        "#E4EAF4", "#B5C0D2", "#8392A8", "#526075", "#5B8DEF", "#5FB8A5", "#FFFFFF", "#F0817A");

    public static readonly ThemePalette HighContrast = new(ThemeSettings.IdContrast, "High contrast", true,
        "#000000", "#0A0A0A", "#1C1C1C", "#262626", "#66FFFFFF", "#B3FFFFFF",
        "#FFFFFF", "#F2F2F2", "#CFCFCF", "#9A9A9A", "#FFD400", "#6BFF9E", "#000000", "#FF7A7A");

    public static readonly ThemePalette[] BuiltIn = [Light, Dusk, Midnight, HighContrast];

    /// <summary>The palette for a theme: built-in or derived from the custom colors, with the accent override applied.</summary>
    public static ThemePalette For(ThemeSettings? settings)
    {
        var s = ThemeSettings.Normalized(settings?.Copy());
        var palette = s.Name switch
        {
            ThemeSettings.IdLight => Light,
            ThemeSettings.IdMidnight => Midnight,
            ThemeSettings.IdContrast => HighContrast,
            ThemeSettings.IdCustom => FromCustom(s.Custom),
            _ => Dusk,
        };
        return s.Accent.Length > 0 ? palette.WithAccent(s.Accent) : palette;
    }

    /// <summary>Derives the full palette from the five colors a user picks.</summary>
    public static ThemePalette FromCustom(CustomPalette c)
    {
        c = CustomPalette.Normalized(c.Copy());
        bool dark = HexColor.Luminance(c.Background) < 0.35;
        string text2 = HexColor.Mix(c.Text, c.Background, 0.25);
        string muted = c.Muted;
        string olive = dark ? "#9B9A5E" : "#6F6F32";
        string danger = dark ? "#E07A6A" : "#B83A2B";
        return new ThemePalette(ThemeSettings.IdCustom, "Custom", dark, c.Background, c.Surface,
            HexColor.Mix(c.Surface, c.Text, 0.05), HexColor.Mix(c.Surface, c.Text, 0.10),
            HexColor.WithAlpha(c.Text, 0.12), HexColor.WithAlpha(c.Text, 0.22),
            c.Text, text2, muted, HexColor.Mix(muted, c.Background, 0.45), c.Accent, olive, OnColor(c.Accent, dark), danger);
    }

    /// <summary>The same palette with another accent; the text on it becomes light or dark, whichever reads better.</summary>
    public ThemePalette WithAccent(string accent)
    {
        accent = HexColor.Clean(accent);
        if (accent.Length == 0) return this;
        return this with { Primary = accent, OnPrimary = OnColor(accent, IsDark) };
    }

    /// <summary>Text that is readable on a filled accent: the theme's light color unless the accent is too bright for it.</summary>
    public static string OnColor(string accent, bool dark)
    {
        string light = dark ? "#FFF6EC" : "#FFFFFF";
        const string ink = "#1A1611";
        if (HexColor.Contrast(light, accent) >= 3.0) return light;
        return HexColor.Contrast(ink, accent) > HexColor.Contrast(light, accent) ? ink : light;
    }

    /// <summary>The dimming layer in front of the desktop while the layout is being edited.</summary>
    public string Scrim => IsDark ? HexColor.WithAlpha(HexColor.Mix(Background, "#000000", 0.3), 0.55) : HexColor.WithAlpha(Background, 0.6);

    /// <summary>The translucent fill of "see-through" cards: the surface at 60%.</summary>
    public string Glass => HexColor.WithAlpha(Surface, 0.6);

    /// <summary>The slim grip bar of floating widgets.</summary>
    public string Grip => HexColor.WithAlpha(Surface3, 0.75);

    /// <summary>A very light wash of the text color, for hover and pressed states.</summary>
    public string Wash => HexColor.WithAlpha(Text, 0.08);
    public string WashStrong => HexColor.WithAlpha(Text, 0.12);

    public double ShadowOpacity => IsDark ? 0.5 : 0.22;
}
