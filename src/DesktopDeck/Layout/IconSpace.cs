using System;
using System.Collections.Generic;

namespace DesktopDeck.Layout;

/// <summary>One desktop icon's cell on screen, in physical pixels.</summary>
public readonly record struct IconBox(int X, int Y, int Width, int Height);

/// <summary>
/// With desktop icons shown, a new widget must not land on top of them. This turns the icons' screen cells into the "occupied" rectangles
/// <see cref="DeckLayout.FindSpace"/> takes (deck DIP, relative to the deck window), or, when the icons could not be read, reserves the
/// left part of the main monitor (where Explorer starts arranging them).
/// </summary>
public static class IconSpace
{
    /// <summary>The share of the main monitor kept clear when the icon positions are unknown.</summary>
    public const double FallbackShare = 0.25;

    /// <summary>The icons that overlap the deck, as rectangles in the deck's DIP. deckX / deckY are the deck window's top-left on screen in pixels; scale is pixels per DIP.</summary>
    public static List<TileRect> Occupied(IEnumerable<IconBox> icons, int deckX, int deckY, int deckWidthPx, int deckHeightPx, double scale)
    {
        var result = new List<TileRect>();
        if (!(scale > 0) || deckWidthPx <= 0 || deckHeightPx <= 0) return result;
        foreach (var icon in icons)
        {
            if (icon.Width <= 0 || icon.Height <= 0) continue;
            int left = Math.Max(icon.X, deckX), top = Math.Max(icon.Y, deckY);
            int right = Math.Min(icon.X + icon.Width, deckX + deckWidthPx), bottom = Math.Min(icon.Y + icon.Height, deckY + deckHeightPx);
            if (right <= left || bottom <= top) continue;
            result.Add(new TileRect((left - deckX) / scale, (top - deckY) / scale, (right - left) / scale, (bottom - top) / scale));
        }
        return result;
    }

    /// <summary>The strip kept clear when the icons could not be read: the left quarter of the main monitor's deck, none elsewhere.</summary>
    public static List<TileRect> Fallback(bool mainMonitor, double deckWidthDip, double deckHeightDip)
    {
        var result = new List<TileRect>();
        if (mainMonitor && deckWidthDip > 0 && deckHeightDip > 0) result.Add(new TileRect(0, 0, deckWidthDip * FallbackShare, deckHeightDip));
        return result;
    }
}
