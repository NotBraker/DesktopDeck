using System.Globalization;
using System.Text.RegularExpressions;

namespace DesktopDeck.Models;

/// <summary>
/// The deck-wide look ("theme for cards" and the deck background). A widget's own appearance options
/// (item.Options "_cardColor", "_opacity", "_radius", "_borderColor", "_textColor") always win; these are the
/// defaults used when a widget has none. Empty colors mean "the theme's color".
/// </summary>
public sealed class AppearanceSettings
{
    public const double DefaultRadius = 14;
    public static readonly string[] BackgroundKinds = ["theme", "solid", "gradient", "image", "wallpaper"];
    public static readonly string[] BackgroundFits = ["fill", "fit", "tile", "center"];

    /// <summary>Default card color for every widget (empty = theme).</summary>
    public string CardColor { get; set; } = "";
    /// <summary>Default card opacity, 0–100 %.</summary>
    public double CardOpacity { get; set; } = 100;
    /// <summary>Default corner roundness, 0–40.</summary>
    public double CornerRadius { get; set; } = DefaultRadius;
    /// <summary>Default text color (empty = theme).</summary>
    public string TextColor { get; set; } = "";
    /// <summary>Default card border color (empty = theme).</summary>
    public string BorderColor { get; set; } = "";
    /// <summary>Every widget uses this deck-wide look; widgets' own appearance settings are ignored while on (except widgets with LauncherItem.OwnLook).</summary>
    public bool ApplyToAll { get; set; }
    /// <summary>The default card style, one of <see cref="ItemKinds.CardStyles"/>.</summary>
    public string CardStyle { get; set; } = "solid";
    /// <summary>Default text size in %, 60–250.</summary>
    public double TextScale { get; set; } = 100;
    /// <summary>Default inner spacing, 0–40.</summary>
    public double Padding { get; set; } = 12;
    /// <summary>The deck background per display id; the empty id means "all displays" (used by any display without its own).</summary>
    public Dictionary<string, BackgroundStyle> Backgrounds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when nothing differs from the built-in theme.</summary>
    public bool IsDefault => CardColor.Length == 0 && TextColor.Length == 0 && BorderColor.Length == 0
        && CardOpacity >= 99.999 && Math.Abs(CornerRadius - DefaultRadius) < 0.001 && Backgrounds.Count == 0
        && !ApplyToAll && CardStyle == "solid" && Math.Abs(TextScale - 100) < 0.001 && Math.Abs(Padding - 12) < 0.001;

    /// <summary>The background that applies to a display: its own, else the all-displays one, else null (theme).</summary>
    public BackgroundStyle? BackgroundFor(string? displayId)
    {
        if (!string.IsNullOrEmpty(displayId) && Backgrounds.TryGetValue(displayId, out var own)) return own;
        return Backgrounds.TryGetValue("", out var all) ? all : null;
    }

    private static readonly Regex Hex = new("^#([0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", RegexOptions.Compiled);
    private static readonly Regex Named = new("^[A-Za-z]{3,24}$", RegexOptions.Compiled);

    /// <summary>A color text we can hand to the WPF color parser: #RGB, #ARGB, #RRGGBB, #AARRGGBB or a color name. Anything else becomes empty.</summary>
    public static string CleanColor(string? value)
    {
        string text = value?.Trim() ?? "";
        return Hex.IsMatch(text) || Named.IsMatch(text) ? text : "";
    }

    /// <summary>Repairs a loaded (possibly hand-edited or null) appearance block.</summary>
    public static AppearanceSettings Normalized(AppearanceSettings? value)
    {
        var a = value ?? new AppearanceSettings();
        a.CardColor = CleanColor(a.CardColor);
        a.TextColor = CleanColor(a.TextColor);
        a.BorderColor = CleanColor(a.BorderColor);
        a.CardOpacity = double.IsFinite(a.CardOpacity) ? Math.Clamp(a.CardOpacity, 0, 100) : 100;
        a.CornerRadius = double.IsFinite(a.CornerRadius) ? Math.Clamp(a.CornerRadius, 0, 40) : DefaultRadius;
        string cardStyle = (a.CardStyle ?? "").Trim().ToLowerInvariant();
        a.CardStyle = ItemKinds.CardStyles.Contains(cardStyle) ? cardStyle : "solid";
        a.TextScale = double.IsFinite(a.TextScale) ? Math.Clamp(a.TextScale, 60, 250) : 100;
        a.Padding = double.IsFinite(a.Padding) ? Math.Clamp(a.Padding, 0, 40) : 12;
        var cleaned = new Dictionary<string, BackgroundStyle>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, style) in a.Backgrounds ?? new())
        {
            if (style is null) continue;
            style.Normalize();
            // A "theme" entry is the same as having none, except as an override of the all-displays background.
            cleaned[(key ?? "").Trim()] = style;
        }
        a.Backgrounds = cleaned;
        return a;
    }
}

/// <summary>The background of one display's deck (or of all displays).</summary>
public sealed class BackgroundStyle
{
    /// <summary>"theme" (the built-in dusk color), "solid", "gradient", "image" or "wallpaper" (the current Windows wallpaper, read only).</summary>
    public string Kind { get; set; } = "theme";
    public string Color { get; set; } = "#1A1611";
    /// <summary>Gradient end color.</summary>
    public string Color2 { get; set; } = "#3A2A22";
    /// <summary>Gradient direction in degrees, like CSS: 0 = bottom to top, 90 = left to right, 180 = top to bottom.</summary>
    public double Angle { get; set; } = 135;
    /// <summary>Image backgrounds: a copy in the deck's media folder.</summary>
    public string ImagePath { get; set; } = "";
    /// <summary>"fill" (cover, cropping), "fit" (whole picture), "tile" or "center".</summary>
    public string Fit { get; set; } = "fill";
    /// <summary>A black overlay for readability, 0–80 %.</summary>
    public double Dim { get; set; }
    /// <summary>Blur of the picture, 0–20.</summary>
    public double Blur { get; set; }

    /// <summary>
    /// Not saved. True for the deck's own drawing of the Windows wallpaper while desktop icons are shown: it is laid out exactly as Windows does,
    /// ignoring Fit, Dim and Blur, so the deck lines up with the real desktop around its widgets.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool MatchWindows { get; set; }

    public bool UsesImage => Kind is "image" or "wallpaper";

    public BackgroundStyle Copy() => (BackgroundStyle)MemberwiseClone();

    public void Normalize()
    {
        Kind = Kind?.Trim().ToLowerInvariant() ?? "";
        if (!AppearanceSettings.BackgroundKinds.Contains(Kind)) Kind = "theme";
        Color = AppearanceSettings.CleanColor(Color) is { Length: > 0 } c ? c : "#1A1611";
        Color2 = AppearanceSettings.CleanColor(Color2) is { Length: > 0 } c2 ? c2 : "#3A2A22";
        Angle = double.IsFinite(Angle) ? ((Angle % 360) + 360) % 360 : 135;
        ImagePath ??= "";
        Fit = Fit?.Trim().ToLowerInvariant() ?? "";
        if (!AppearanceSettings.BackgroundFits.Contains(Fit)) Fit = "fill";
        Dim = double.IsFinite(Dim) ? Math.Clamp(Dim, 0, 80) : 0;
        Blur = double.IsFinite(Blur) ? Math.Clamp(Blur, 0, 20) : 0;
    }

    /// <summary>A text key that changes whenever anything that affects the picture changes.</summary>
    public string Signature => string.Join("|", Kind, Color, Color2, Angle.ToString("0.#", CultureInfo.InvariantCulture), ImagePath, Fit,
        Dim.ToString("0.#", CultureInfo.InvariantCulture), Blur.ToString("0.#", CultureInfo.InvariantCulture), MatchWindows);
}
