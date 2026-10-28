using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopDeck;

/// <summary>
/// Decodes app icons and pictures once, at display size, as frozen bitmaps. Keys include the
/// file's write time so a replaced file is reloaded. All calls happen on the UI thread.
/// </summary>
internal static class ImageCache
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>What each deck surface's latest render used. Every monitor has its own surfaces, so one surface's
    /// render must not evict pictures another surface still shows.</summary>
    private static readonly Dictionary<object, HashSet<string>> UsedBy = new();
    private static HashSet<string>? current;
    internal static readonly HashSet<string> PictureExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico", ".tif", ".tiff", ".webp", ".jfif" };

    /// <summary>Starts tracking which entries a full render of <paramref name="owner"/> uses.</summary>
    internal static void BeginPass(object owner) => current = UsedBy[owner] = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Drops entries no surface's latest render used (old sizes, replaced or removed files),
    /// so a deck that runs for days does not accumulate decoded bitmaps.</summary>
    internal static void EndPass(object owner)
    {
        current = null;
        var used = UsedBy.Values.SelectMany(set => set).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in Cache.Keys.Where(key => !used.Contains(key)).ToList()) Cache.Remove(key);
    }

    /// <summary>A surface closed: its pictures may be dropped at the next render.</summary>
    internal static void Forget(object owner) => UsedBy.Remove(owner);

    internal static bool TryGet(string key, out ImageSource? image)
    {
        // Lookups outside a render (icons loaded after first paint) count for every surface until it renders again.
        if (current is not null) current.Add(key);
        else foreach (var set in UsedBy.Values) set.Add(key);
        return Cache.TryGetValue(key, out image);
    }

    internal static string IconKey(string path, int pixels) => "icon|" + pixels + "|" + Stamp(path);

    internal static string PictureKey(string path, int pixels) => "pic|" + pixels + "|" + Stamp(path);

    /// <summary>The icon Explorer would show for a file, shortcut, or URL file.</summary>
    internal static ImageSource? ShellIcon(string path, int pixels)
    {
        string key = IconKey(path, pixels);
        if (TryGet(key, out var cached)) return cached;
        ImageSource? result = null;
        try { result = LoadShellImage(Environment.ExpandEnvironmentVariables(path), pixels); }
        catch (Exception ex) when (ex is COMException or ArgumentException or IOException or UnauthorizedAccessException or InvalidCastException) { }
        // The shell sometimes answers "not ready yet" for shortcuts; only successes are remembered so a later render retries.
        if (result is not null) Cache[key] = result;
        return result;
    }

    /// <summary>A picture file decoded with its shorter edge no larger than <paramref name="pixels"/>.</summary>
    internal static ImageSource? Picture(string path, int pixels)
    {
        string key = PictureKey(path, pixels);
        if (TryGet(key, out var cached)) return cached;
        ImageSource? result = null;
        try
        {
            if (File.Exists(path))
            {
                // Read only the header to learn the orientation.
                int sourceWidth, sourceHeight;
                using (var stream = File.OpenRead(path))
                {
                    var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None).Frames[0];
                    sourceWidth = frame.PixelWidth;
                    sourceHeight = frame.PixelHeight;
                }
                bool wide = sourceWidth >= sourceHeight;
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(path);
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                // Bound the shorter edge so the picture stays sharp whether it fills or fits its frame.
                if (wide && sourceHeight > pixels) image.DecodePixelHeight = pixels;
                else if (!wide && sourceWidth > pixels) image.DecodePixelWidth = pixels;
                image.EndInit();
                image.Freeze();
                result = image;
            }
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or FileFormatException or COMException) { }
        Cache[key] = result;
        return result;
    }

    private static string Stamp(string path)
    {
        try { return path + "|" + File.GetLastWriteTimeUtc(Environment.ExpandEnvironmentVariables(path)).Ticks; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return path; }
    }

    private static ImageSource? LoadShellImage(string path, int pixels)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return null;
        var iid = typeof(IShellItemImageFactory).GUID;
        int hr = SHCreateItemFromParsingName(path, 0, ref iid, out var factory);
        if (hr != 0 || factory is null) return null;
        nint bitmap = 0;
        try
        {
            // ICONONLY avoids document thumbnails; BIGGERSIZEOK lets the shell hand back a crisp larger icon.
            hr = factory.GetImage(new NativeSize { Width = pixels, Height = pixels }, 0x4 | 0x1, out bitmap);
            if (hr != 0 || bitmap == 0) return null;
            return ToBitmapSource(bitmap);
        }
        finally
        {
            if (bitmap != 0) DeleteObject(bitmap);
            Marshal.ReleaseComObject(factory);
        }
    }

    private static BitmapSource? ToBitmapSource(nint bitmap)
    {
        var info = new NativeBitmap();
        if (GetObject(bitmap, Marshal.SizeOf<NativeBitmap>(), ref info) == 0 || info.Width <= 0 || info.Height <= 0) return null;
        int width = info.Width, height = Math.Abs(info.Height);
        var header = new BitmapInfoHeader { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
        var pixels = new byte[width * height * 4];
        nint dc = GetDC(0);
        try { if (GetDIBits(dc, bitmap, 0, (uint)height, pixels, ref header, 0) == 0) return null; }
        finally { ReleaseDC(0, dc); }
        bool hasAlpha = false;
        for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] != 0) { hasAlpha = true; break; }
        var source = BitmapSource.Create(width, height, 96, 96, hasAlpha ? PixelFormats.Pbgra32 : PixelFormats.Bgr32, null, pixels, width * 4);
        source.Freeze();
        return source;
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(NativeSize size, int flags, out nint bitmap);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeSize { public int Width, Height; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeBitmap { public int Type, Width, Height, WidthBytes; public ushort Planes, BitsPixel; public nint Bits; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfoHeader { public int Size, Width, Height; public short Planes, BitCount; public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, nint bindContext, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint handle);
    [DllImport("gdi32.dll")] private static extern int GetObject(nint handle, int size, ref NativeBitmap bitmap);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(nint dc, nint bitmap, uint start, uint lines, [Out] byte[] bits, ref BitmapInfoHeader info, uint usage);
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
}
