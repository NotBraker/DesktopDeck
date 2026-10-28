using System;
using System.Collections.Generic;
using System.Linq;

namespace DesktopDeck.Layout;

/// <summary>A monitor as the layout sees it: physical-pixel rectangles plus its DPI scale.</summary>
public sealed record DisplayInfo(string Id, int Number, bool IsPrimary, PixelRect Bounds, PixelRect WorkArea, double Scale)
{
    public string Name => $"Display {Number} · {Bounds.Width}×{Bounds.Height}" + (IsPrimary ? " (main)" : "");
}

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool Contains(double x, double y) => x >= X && x < Right && y >= Y && y < Bottom;
}

/// <summary>Decides which connected monitor shows an item.</summary>
public static class DisplayMap
{
    /// <summary>
    /// The item's saved display if it is connected, otherwise the primary. An empty saved display means the primary,
    /// and an item on a disconnected monitor keeps its saved id so it returns there when the monitor does.
    /// </summary>
    public static DisplayInfo Resolve(string? saved, IReadOnlyList<DisplayInfo> displays)
    {
        if (displays.Count == 0) throw new ArgumentException("At least one display is required.", nameof(displays));
        var primary = displays.FirstOrDefault(d => d.IsPrimary) ?? displays[0];
        if (string.IsNullOrEmpty(saved)) return primary;
        return displays.FirstOrDefault(d => string.Equals(d.Id, saved, StringComparison.OrdinalIgnoreCase)) ?? primary;
    }

    /// <summary>The display containing a physical-pixel point, or the nearest one.</summary>
    public static DisplayInfo At(double x, double y, IReadOnlyList<DisplayInfo> displays)
    {
        if (displays.Count == 0) throw new ArgumentException("At least one display is required.", nameof(displays));
        return displays.FirstOrDefault(d => d.Bounds.Contains(x, y))
            ?? displays.OrderBy(d => Distance(d.Bounds, x, y)).First();
    }

    private static double Distance(PixelRect r, double x, double y)
    {
        double dx = Math.Max(Math.Max(r.X - x, 0), x - r.Right), dy = Math.Max(Math.Max(r.Y - y, 0), y - r.Bottom);
        return dx * dx + dy * dy;
    }
}
