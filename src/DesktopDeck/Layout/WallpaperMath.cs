using System;

namespace DesktopDeck.Layout;

/// <summary>How Windows lays the wallpaper on a monitor (DESKTOP_WALLPAPER_POSITION).</summary>
public enum WallpaperPosition { Center = 0, Tile = 1, Stretch = 2, Fit = 3, Fill = 4, Span = 5 }

/// <summary>Where the wallpaper picture goes, in pixels relative to the monitor's top-left: its rectangle (it may reach outside the monitor), and whether it repeats from there.</summary>
public readonly record struct WallpaperPlan(double X, double Y, double Width, double Height, bool Tile);

/// <summary>The pure maths of drawing the Windows wallpaper the way Windows does, so the deck lines up with the real desktop.</summary>
public static class WallpaperMath
{
    /// <summary>
    /// Where the picture goes on one monitor. Center keeps the size, Tile repeats it, Stretch fills the monitor, Fit shows all of it, Fill covers the
    /// monitor (cropping), Span covers the whole virtual screen (the picture across all monitors) and this monitor shows its part.
    /// All numbers are pixels; the monitor and virtual-screen rectangles are in virtual-screen coordinates.
    /// </summary>
    public static WallpaperPlan Plan(double imageWidth, double imageHeight, double monitorX, double monitorY, double monitorWidth, double monitorHeight,
        WallpaperPosition position, double virtualX, double virtualY, double virtualWidth, double virtualHeight)
    {
        if (!(imageWidth > 0) || !(imageHeight > 0) || !(monitorWidth > 0) || !(monitorHeight > 0)) return new WallpaperPlan(0, 0, Math.Max(0, monitorWidth), Math.Max(0, monitorHeight), false);
        switch (position)
        {
            case WallpaperPosition.Center:
                return new WallpaperPlan((monitorWidth - imageWidth) / 2, (monitorHeight - imageHeight) / 2, imageWidth, imageHeight, false);
            case WallpaperPosition.Tile:
                return new WallpaperPlan(0, 0, imageWidth, imageHeight, true);
            case WallpaperPosition.Fit:
                return Centered(imageWidth, imageHeight, monitorWidth, monitorHeight, Math.Min(monitorWidth / imageWidth, monitorHeight / imageHeight));
            case WallpaperPosition.Fill:
                return Centered(imageWidth, imageHeight, monitorWidth, monitorHeight, Math.Max(monitorWidth / imageWidth, monitorHeight / imageHeight));
            case WallpaperPosition.Span:
                if (!(virtualWidth > 0) || !(virtualHeight > 0)) return Centered(imageWidth, imageHeight, monitorWidth, monitorHeight, Math.Max(monitorWidth / imageWidth, monitorHeight / imageHeight));
                var across = Centered(imageWidth, imageHeight, virtualWidth, virtualHeight, Math.Max(virtualWidth / imageWidth, virtualHeight / imageHeight));
                return across with { X = across.X + virtualX - monitorX, Y = across.Y + virtualY - monitorY };
            default:
                return new WallpaperPlan(0, 0, monitorWidth, monitorHeight, false);
        }
    }

    private static WallpaperPlan Centered(double imageWidth, double imageHeight, double areaWidth, double areaHeight, double scale)
    {
        double w = imageWidth * scale, h = imageHeight * scale;
        return new WallpaperPlan((areaWidth - w) / 2, (areaHeight - h) / 2, w, h, false);
    }

    /// <summary>The share of the picture's full size that has to be decoded to be sharp when drawn at this plan (1 = full size; less may decode smaller).</summary>
    public static double NeededScale(double imageWidth, double imageHeight, WallpaperPlan plan)
        => imageWidth > 0 && imageHeight > 0 && !plan.Tile ? Math.Min(1, Math.Max(plan.Width / imageWidth, plan.Height / imageHeight)) : 1;
}
