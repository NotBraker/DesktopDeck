using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace DesktopDeck.Widgets;

/// <summary>The sticker's glyph table, sizing and click targets. Pure logic (unit tested).</summary>
public static class StickerLogic
{
    /// <summary>Named Segoe Fluent Icons glyphs a sticker can show (checked against the font).</summary>
    public static readonly (string Name, string Glyph)[] Glyphs =
    [
        ("Star", ""), ("Heart", ""), ("Home", ""), ("Sun", ""), ("Moon", ""), ("Cloud", ""),
        ("Music", ""), ("Camera", ""), ("Photo", ""), ("Globe", ""), ("Coffee", ""), ("Calendar", ""),
        ("Clock", ""), ("Bell", ""), ("Flag", ""), ("Place", ""), ("Mail", ""), ("Chat", ""),
        ("Gift", ""), ("Idea", ""), ("Leaf", ""), ("Fire", ""), ("Bolt", ""), ("Check", ""),
        ("Smile", ""), ("Book", ""), ("Game", ""), ("Headphones", ""), ("Lock", ""), ("Palette", ""),
        ("Plane", ""), ("Shield", ""),
    ];

    public static string[] Names => Glyphs.Select(g => g.Name).ToArray();

    /// <summary>The glyph for a name; anything unknown gives the star.</summary>
    public static string GlyphFor(string? name) => Glyphs.FirstOrDefault(g => g.Name == name) is { Glyph: not null } found ? found.Glyph : Glyphs[0].Glyph;

    /// <summary>Space around the symbol inside its background shape, as a fraction of the symbol size on each side.</summary>
    public const double ShapePadding = 0.2;

    /// <summary>
    /// The symbol size to draw: what was asked for, but small enough that the shape (with its padding), the caption
    /// and the card spacing fit the card. Never below 16.
    /// </summary>
    public static double SymbolSize(double requested, double cardWidth, double cardHeight, double padding, double captionHeight, bool hasShape)
    {
        double growth = hasShape ? 1 + 2 * ShapePadding : 1;
        double room = Math.Min(cardWidth - 2 * padding, cardHeight - 2 * padding - captionHeight);
        return Math.Clamp(Math.Min(requested, room / growth), 16, Math.Max(16, requested));
    }

    private static readonly Regex Domain = new(@"^[\w-]+(\.[\w-]+)+(/\S*)?$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Makes a typed click target openable: web addresses without a scheme get https://, paths, %VARS%, schemes
    /// (https:, mailto:, ms-settings:, shell:) and everything else are left alone. Empty gives empty.
    /// </summary>
    public static string NormalizeTarget(string? target)
    {
        string text = (target ?? "").Trim().Trim('"');
        if (text.Length == 0) return "";
        if (text.Contains("://", StringComparison.Ordinal) || text.StartsWith('%')) return text;
        if (text.Length > 2 && text[1] == ':' && char.IsLetter(text[0]) && (text[2] == '\\' || text[2] == '/')) return text;
        if (text.StartsWith("\\\\", StringComparison.Ordinal)) return text;
        if (text.Contains(':', StringComparison.Ordinal)) return text;
        return Domain.IsMatch(text) ? "https://" + text : text;
    }

    /// <summary>True for text that is only an emoji or a symbol (so it should be drawn with the emoji font).</summary>
    public static bool LooksLikeEmoji(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.Length > 8) return false;
        foreach (var rune in trimmed.EnumerateRunes())
            if (rune.Value < 0x2190 && rune.Value != 0x200D && rune.Value != 0xFE0F) return false;
        return true;
    }
}
