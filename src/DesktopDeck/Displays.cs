using System.Runtime.InteropServices;
using DesktopDeck.Layout;

namespace DesktopDeck;

/// <summary>Lists the connected monitors in physical pixels, each with an id that stays the same across restarts.</summary>
internal static class Displays
{
    public static List<DisplayInfo> Current()
    {
        var list = new List<DisplayInfo>();
        try
        {
            EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
            {
                var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
                if (!GetMonitorInfo(monitor, ref info)) return true;
                double scale = GetDpiForMonitor(monitor, 0, out uint dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1;
                string device = info.Device;
                int number = int.TryParse(new string(device.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray()), out int n) ? n : list.Count + 1;
                list.Add(new DisplayInfo(StableId(device), number, (info.Flags & 1) != 0, info.Monitor.ToRect(), info.Work.ToRect(), scale));
                return true;
            }, 0);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        if (list.Count == 0)
        {
            // Should not happen; fall back to the primary screen as v0.4 did.
            var screen = System.Windows.Forms.Screen.PrimaryScreen!;
            var b = screen.Bounds; var w = screen.WorkingArea;
            list.Add(new DisplayInfo(screen.DeviceName, 1, true, new PixelRect(b.X, b.Y, b.Width, b.Height), new PixelRect(w.X, w.Y, w.Width, w.Height), 1));
        }
        return list.OrderByDescending(d => d.IsPrimary).ThenBy(d => d.Bounds.X).ThenBy(d => d.Bounds.Y).ToList();
    }

    /// <summary>The monitor's device interface path (it names the physical monitor and port), else the adapter output name.</summary>
    private static string StableId(string device)
    {
        var dd = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
        if (EnumDisplayDevices(device, 0, ref dd, 1) && !string.IsNullOrWhiteSpace(dd.DeviceId)) return dd.DeviceId;
        return device;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly PixelRect ToRect() => new(Left, Top, Right - Left, Bottom - Top);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    private delegate bool MonitorEnumProc(nint monitor, nint hdc, nint rect, nint data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfoEx info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice info, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);
}
