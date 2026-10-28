using System.Globalization;

namespace DesktopDeck.Models;

/// <summary>
/// How one widget's card looks once the widget's own settings and the deck-wide look are combined. Colors are text ("" = the theme's color),
/// numbers are already clamped. No WPF types, so the rules can be tested without a window.
/// </summary>
/// <param name="CardStyle">One of <see cref="ItemKinds.CardStyles"/>.</param>
/// <param name="CardColor">The card color, or "" for the theme's.</param>
/// <param name="TextColor">The text color, or "" for the theme's.</param>
/// <param name="BorderColor">The border color, or "" for the theme's.</param>
/// <param name="Opacity">Card opacity, 0–100 %.</param>
/// <param name="Radius">Corner roundness, 0–40.</param>
/// <param name="TextScale">Text size in %, 60–250.</param>
/// <param name="Padding">Inner spacing, 0–40.</param>
/// <param name="FromDeck">The deck-wide look decided all of this (<see cref="AppearanceSettings.ApplyToAll"/> is on and the widget has no own look).</param>
public sealed record EffectiveLook(string CardStyle, string CardColor, string TextColor, string BorderColor, double Opacity, double Radius, double TextScale, double Padding, bool FromDeck);

/// <summary>
/// The one place that decides a widget's look. With "use one look for every widget" on, the deck-wide values win outright over a widget's own
/// (they are only read, never rewritten, so turning it off brings every widget's own look back). Widgets marked <see cref="LauncherItem.OwnLook"/>
/// are left out. With it off, a widget's own "_" options win over the deck-wide defaults, which win over the theme (the way it always worked).
/// </summary>
public static class LookResolver
{
    public const double DefaultTextScale = 100, DefaultPadding = 12;

    /// <summary>True when the deck-wide look decides this widget's appearance.</summary>
    public static bool UsesDeckLook(AppearanceSettings? deck, LauncherItem item) => deck is { ApplyToAll: true } && !item.OwnLook;

    /// <param name="deck">The deck-wide look (null = none: the built-in defaults).</param>
    /// <param name="item">The widget.</param>
    /// <param name="colorOk">Decides whether a color text can be drawn; a widget's own color that fails falls back to the deck's. Default: <see cref="AppearanceSettings.CleanColor"/>.</param>
    public static EffectiveLook Effective(AppearanceSettings? deck, LauncherItem item, Func<string, bool>? colorOk = null)
    {
        colorOk ??= text => AppearanceSettings.CleanColor(text).Length > 0;
        string Color(string? text) => !string.IsNullOrWhiteSpace(text) && colorOk(text.Trim()) ? text.Trim() : "";

        if (UsesDeckLook(deck, item))
        {
            var d = deck!;
            return new EffectiveLook(
                ItemKinds.CardStyles.Contains(d.CardStyle) ? d.CardStyle : "solid",
                Color(d.CardColor), Color(d.TextColor), Color(d.BorderColor),
                Clamp(d.CardOpacity, 0, 100, 100), Clamp(d.CornerRadius, 0, 40, AppearanceSettings.DefaultRadius),
                Clamp(d.TextScale, 60, 250, DefaultTextScale), Clamp(d.Padding, 0, 40, DefaultPadding), true);
        }

        string Own(string key, string fallback)
        {
            string own = item.Options.TryGetValue(key, out var text) ? Color(text) : "";
            return own.Length > 0 ? own : Color(fallback);
        }
        return new EffectiveLook(
            ItemKinds.CardStyles.Contains(item.CardStyle) ? item.CardStyle : "solid",
            Own("_cardColor", deck?.CardColor ?? ""), Own("_textColor", deck?.TextColor ?? ""), Own("_borderColor", deck?.BorderColor ?? ""),
            Clamp(OptionNumber(item, "_opacity") ?? deck?.CardOpacity, 0, 100, 100),
            Clamp(OptionNumber(item, "_radius") ?? deck?.CornerRadius, 0, 40, AppearanceSettings.DefaultRadius),
            Clamp(OptionNumber(item, "_textScale"), 60, 250, DefaultTextScale),
            Clamp(OptionNumber(item, "_padding"), 0, 40, DefaultPadding), false);
    }

    /// <summary>A number the item saved under <paramref name="key"/>, or null when it has none.</summary>
    public static double? OptionNumber(LauncherItem item, string key)
        => item.Options.TryGetValue(key, out var text) && double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : null;

    private static double Clamp(double? value, double min, double max, double fallback)
        => value is { } v && double.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;

    // ---------- Reset every widget ----------

    /// <summary>The "_" options that make up a widget's own look.</summary>
    public static readonly string[] LookKeys = ["_cardColor", "_textColor", "_borderColor", "_opacity", "_radius", "_textScale", "_padding"];

    /// <summary>
    /// Makes every widget take the deck's look: the card style becomes the deck's, own colors, opacity, corners, text size and spacing are cleared,
    /// and "own look" is switched off. Where the deck's text size or spacing is not the default, it is written into the widget (those two have no
    /// deck-wide fallback while the deck look is off), so the widget keeps the look if the switch is turned off later. Returns how many widgets changed.
    /// </summary>
    public static int ResetWidgets(DeckSettings settings)
    {
        var deck = settings.Appearance ??= new AppearanceSettings();
        string style = ItemKinds.CardStyles.Contains(deck.CardStyle) ? deck.CardStyle : "solid";
        int changed = 0;
        foreach (var item in settings.Items)
        {
            bool touched = item.CardStyle != style || item.OwnLook;
            item.CardStyle = style;
            item.OwnLook = false;
            bool keepScale = Math.Abs(deck.TextScale - DefaultTextScale) > 0.001, keepPadding = Math.Abs(deck.Padding - DefaultPadding) > 0.001;
            foreach (var key in LookKeys.Where(k => !(k == "_textScale" && keepScale) && !(k == "_padding" && keepPadding)))
                if (item.Options.Remove(key)) touched = true;
            if (keepScale) touched |= SetOption(item, "_textScale", deck.TextScale);
            if (keepPadding) touched |= SetOption(item, "_padding", deck.Padding);
            if (touched) changed++;
        }
        return changed;
    }

    private static bool SetOption(LauncherItem item, string key, double value)
    {
        string text = value.ToString("0.##", CultureInfo.InvariantCulture);
        if (item.Options.TryGetValue(key, out var current) && current == text) return false;
        item.Options[key] = text;
        return true;
    }
}
