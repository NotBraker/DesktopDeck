using System.Runtime.InteropServices;
using DesktopDeck.Layout;

namespace DesktopDeck;

/// <summary>
/// The region of a deck window: the window only exists (is drawn, takes clicks) inside it, so the windows beneath it
/// (Explorer's icon view) show and get the clicks everywhere else. The shapes come from <see cref="RegionMath"/> (pure); this class owns the HRGN handling.
/// </summary>
internal static class DeckRegion
{
    /// <summary>Creates one region that is the union of the shapes (an empty region when there are none). The caller owns it until SetWindowRgn accepts it.</summary>
    public static nint Combine(IReadOnlyList<RegionShape> shapes)
    {
        nint total = CreateRectRgn(0, 0, 0, 0);
        foreach (var s in shapes)
        {
            // CreateRoundRectRgn's right/bottom are exclusive-ish; the ellipse size is the corner diameter.
            nint part = s.Radius > 0
                ? CreateRoundRectRgn(s.X, s.Y, s.Right + 1, s.Bottom + 1, s.Radius * 2, s.Radius * 2)
                : CreateRectRgn(s.X, s.Y, s.Right, s.Bottom);
            CombineRgn(total, total, part, 2); // RGN_OR
            DeleteObject(part);
        }
        return total;
    }

    /// <summary>Sets the window's region. On success the system owns the region; on failure it is freed here.</summary>
    public static bool Apply(nint window, IReadOnlyList<RegionShape> shapes)
    {
        if (window == 0) return false;
        nint region = Combine(shapes);
        if (SetWindowRgn(window, region, true) != 0) return true;
        DeleteObject(region);
        return false;
    }

    /// <summary>Removes the window's region: it covers its whole rectangle again.</summary>
    public static void Clear(nint window) { if (window != 0) SetWindowRgn(window, 0, true); }

    /// <summary>1 = null, 2 = a rectangle, 3 = several, 0 = the window has no region.</summary>
    public static int Kind(nint window)
    {
        if (window == 0) return 0;
        nint probe = CreateRectRgn(0, 0, 0, 0);
        int kind = GetWindowRgn(window, probe);
        DeleteObject(probe);
        return kind == 0 ? 0 : kind;
    }

    /// <summary>The bounding box of the window's region (window pixels), or null if it has none.</summary>
    public static (int Left, int Top, int Right, int Bottom)? Bounds(nint window)
    {
        nint probe = CreateRectRgn(0, 0, 0, 0);
        try
        {
            if (GetWindowRgn(window, probe) == 0) return null;
            return GetRgnBox(probe, out var r) == 0 ? null : (r.Left, r.Top, r.Right, r.Bottom);
        }
        finally { DeleteObject(probe); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct RectNative { public int Left, Top, Right, Bottom; }
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(nint dest, nint a, nint b, int mode);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] private static extern int GetRgnBox(nint region, out RectNative box);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint hwnd, nint region, bool redraw);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(nint hwnd, nint region);
}
