using System;
using System.Collections.Generic;

namespace DesktopDeck.Layout;

/// <summary>One visible piece of the deck (a widget card, a button, a toast) in window pixels, with its corner radius in pixels.</summary>
public readonly record struct RegionShape(int X, int Y, int Width, int Height, int Radius)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;

    /// <summary>True if the pixel is inside the shape, corners rounded the way the region draws them.</summary>
    public bool Contains(double px, double py)
    {
        if (px < X || py < Y || px >= Right || py >= Bottom) return false;
        int r = Math.Min(Radius, Math.Min(Width, Height) / 2);
        if (r <= 0) return true;
        double cx = px < X + r ? X + r : px >= Right - r ? Right - r : px;
        double cy = py < Y + r ? Y + r : py >= Bottom - r ? Bottom - r : py;
        double dx = px + 0.5 - cx, dy = py + 0.5 - cy;
        return dx * dx + dy * dy <= (double)r * r;
    }
}

/// <summary>The pure part of a deck window's region (DeckRegion has the Windows part): rectangles, radius and scale in, pixel shapes out.</summary>
public static class RegionMath
{
    /// <summary>
    /// Turns rectangles in device-independent units (relative to the window) into pixel shapes: the box is widened to whole pixels
    /// (never smaller than the drawn card) and clipped to the window; the radius is scaled by the monitor's DPI scale.
    /// Empty, non-finite and off-window shapes are dropped.
    /// </summary>
    public static List<RegionShape> Build(IEnumerable<(double X, double Y, double Width, double Height, double Radius)> dipRects, double boxScale, double radiusScale, int windowWidth, int windowHeight)
    {
        var shapes = new List<RegionShape>();
        foreach (var (x, y, w, h, radius) in dipRects)
        {
            if (!(w > 0) || !(h > 0) || !double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(w) || !double.IsFinite(h)) continue;
            int left = Math.Max(0, (int)Math.Floor(x * boxScale)), top = Math.Max(0, (int)Math.Floor(y * boxScale));
            int right = Math.Min(windowWidth, (int)Math.Ceiling((x + w) * boxScale)), bottom = Math.Min(windowHeight, (int)Math.Ceiling((y + h) * boxScale));
            if (right <= left || bottom <= top) continue;
            double r = double.IsFinite(radius) ? radius : 0;
            shapes.Add(new RegionShape(left, top, right - left, bottom - top, Math.Max(0, (int)Math.Round(r * radiusScale))));
        }
        return shapes;
    }
}
