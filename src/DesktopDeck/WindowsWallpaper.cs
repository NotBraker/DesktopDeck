using System.Runtime.InteropServices;
using System.Windows.Media;
using DesktopDeck.Layout;

namespace DesktopDeck;

/// <summary>What Windows shows on one monitor: the picture file ("" for a plain color), how it is laid out, and the color behind it.</summary>
internal sealed record WallpaperLook(string Path, WallpaperPosition Position, Color Background, bool FromShell);

/// <summary>
/// The Windows wallpaper per monitor, read through IDesktopWallpaper (read only: nothing here ever sets a wallpaper). It is read once and
/// kept until <see cref="Invalidate"/> (the app calls it when Windows says the wallpaper changed). When the shell can't be asked, the old
/// single-path reading stands in.
/// </summary>
internal static class WindowsWallpaper
{
    private sealed record Monitor(string DevicePath, PixelRect Rect, string Path);
    private sealed record Snapshot(List<Monitor> Monitors, WallpaperPosition Position, Color Background);

    private static Snapshot? cached;

    /// <summary>Changes whenever the wallpaper was re-read, so cached drawings of it can be told apart.</summary>
    internal static int Version { get; private set; }

    /// <summary>Forget what was read: the wallpaper, its layout or its slideshow picture changed.</summary>
    internal static void Invalidate() { cached = null; Version++; }

    /// <summary>The look of this display's wallpaper, or the single system wallpaper when the display isn't known to the shell.</summary>
    internal static WallpaperLook For(DisplayInfo? display)
    {
        var snapshot = cached ??= Read();
        if (snapshot is null) return new WallpaperLook(Appearance.WallpaperPath(), WallpaperPosition.Fill, ((SolidColorBrush)Theme.Background).Color, false);
        Monitor? found = null;
        if (display is not null)
        {
            found = snapshot.Monitors.FirstOrDefault(m => string.Equals(m.DevicePath, display.Id, StringComparison.OrdinalIgnoreCase))
                    ?? snapshot.Monitors.FirstOrDefault(m => m.Rect == display.Bounds);
        }
        found ??= snapshot.Monitors.FirstOrDefault();
        return new WallpaperLook(found?.Path ?? "", snapshot.Position, snapshot.Background, true);
    }

    /// <summary>The bounding box of every monitor on screen (the area a spanned wallpaper covers).</summary>
    internal static PixelRect VirtualScreen(IEnumerable<DisplayInfo> displays)
    {
        var list = displays.ToList();
        if (list.Count == 0) return default;
        int left = list.Min(d => d.Bounds.X), top = list.Min(d => d.Bounds.Y);
        int right = list.Max(d => d.Bounds.X + d.Bounds.Width), bottom = list.Max(d => d.Bounds.Y + d.Bounds.Height);
        return new PixelRect(left, top, right - left, bottom - top);
    }

    private static Snapshot? Read()
    {
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD"));
            if (type is null || Activator.CreateInstance(type) is not IDesktopWallpaper wallpaper) return null;
            try
            {
                var monitors = new List<Monitor>();
                uint count = wallpaper.GetMonitorDevicePathCount();
                for (uint i = 0; i < count; i++)
                {
                    try
                    {
                        string id = wallpaper.GetMonitorDevicePathAt(i);
                        if (string.IsNullOrEmpty(id)) continue;
                        var rect = wallpaper.GetMonitorRECT(id);
                        monitors.Add(new Monitor(id, new PixelRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top), wallpaper.GetWallpaper(id) ?? ""));
                    }
                    catch (COMException) { } // a monitor Windows lists but is not showing
                }
                if (monitors.Count == 0) return null;
                uint colorRef = wallpaper.GetBackgroundColor();
                var color = Color.FromRgb((byte)(colorRef & 0xFF), (byte)((colorRef >> 8) & 0xFF), (byte)((colorRef >> 16) & 0xFF));
                int raw = wallpaper.GetPosition();
                var position = Enum.IsDefined(typeof(WallpaperPosition), raw) ? (WallpaperPosition)raw : WallpaperPosition.Fill;
                return new Snapshot(monitors, position, color);
            }
            finally { Marshal.FinalReleaseComObject(wallpaper); }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException or InvalidOperationException or NotSupportedException
                                       or MarshalDirectiveException or SEHException or DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct RectNative { public int Left, Top, Right, Bottom; }

    // Only the methods up to GetPosition are declared (the vtable order matters, the rest is never called). Nothing here sets anything.
    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper();
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint monitorIndex);
        uint GetMonitorDevicePathCount();
        RectNative GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId);
        void SetBackgroundColor();
        uint GetBackgroundColor();
        void SetPosition();
        int GetPosition();
    }
}
