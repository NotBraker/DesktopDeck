using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using DesktopDeck.Layout;

namespace DesktopDeck;

/// <summary>
/// Attaches one deck window above Explorer's icon view, below ordinary application windows, covering one
/// monitor's work area. The deck never leaves the desktop: bringing it forward uses separate front windows.
/// </summary>
internal sealed class NativeDesktopHost : IDisposable
{
    private const long WsChild = 0x40000000L, WsPopup = 0x80000000L;
    private readonly Window window;
    private readonly DispatcherTimer retry;
    private nint handle;
    private int attempts;
    /// <summary>The monitor work area to cover, in physical pixels.</summary>
    public PixelRect Area { get; set; }
    public bool IsAttached { get; private set; }
    /// <summary>The deck is attached to the desktop (again): raised after every successful attach, e.g. after Explorer restarted.</summary>
    public event Action? Attached;
    public bool HasWindow => handle != 0 && IsWindow(handle);
    public nint Handle => handle;
    /// <summary>The real parent (GetParent would return the owner for top-level windows).</summary>
    public nint Parent => handle == 0 ? 0 : GetAncestor(handle, 1);
    public bool IsChild => handle != 0 && (GetWindowLongPtr(handle, -16).ToInt64() & WsChild) != 0;
    public string Diagnostics => $"Attached={IsAttached}; Window={handle}; Parent={Parent}; ChildStyle={IsChild}; DIP={window.ActualWidth}x{window.ActualHeight}; DPI={GetDpiForWindow(handle)}; Area={Area}";

    public NativeDesktopHost(Window window, PixelRect area)
    {
        this.window = window;
        Area = area;
        retry = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        retry.Tick += (_, _) => { if (Attach() || ++attempts >= 15) retry.Stop(); };
        window.SourceInitialized += (_, _) => { handle = new WindowInteropHelper(window).Handle; Reconnect(); };
    }

    public void Reconnect()
    {
        attempts = 0;
        if (!Attach()) retry.Start(); else retry.Stop();
        Reposition();
    }

    public bool Attach()
    {
        IsAttached = false;
        if (handle == 0 || !IsWindow(handle)) return false;
        nint container = 0;
        EnumWindows((hwnd, _) =>
        {
            if (FindWindowEx(hwnd, 0, "SHELLDLL_DefView", null) == 0) return true;
            container = hwnd;
            return false;
        }, 0);
        if (container == 0) return false;
        if (GetAncestor(handle, 1) == container && IsChild) { IsAttached = true; Reposition(); Attached?.Invoke(); return true; }
        long oldStyle = GetWindowLongPtr(handle, -16).ToInt64();
        SetWindowLongPtr(handle, -16, (nint)((oldStyle | WsChild) & ~WsPopup));
        Marshal.SetLastPInvokeError(0);
        var previous = SetParent(handle, container);
        if (previous == 0 && Marshal.GetLastWin32Error() != 0)
        {
            SetWindowLongPtr(handle, -16, (nint)oldStyle);
            return false;
        }
        IsAttached = true;
        Reposition();
        Attached?.Invoke();
        return true;
    }

    public void Reposition()
    {
        if (handle == 0) return;
        var point = new PointNative { X = Area.X, Y = Area.Y };
        if (IsAttached) ScreenToClient(GetParent(handle), ref point);
        // HWND_TOP is within the desktop parent, never above ordinary app windows. Unattached: the bottom of the z-order.
        SetWindowPos(handle, IsAttached ? 0 : new nint(1), point.X, point.Y, Area.Width, Area.Height, 0x0010 | 0x0040);
    }

    /// <summary>The window's rectangle on screen, in physical pixels (for the host test).</summary>
    public PixelRect ScreenRect()
    {
        if (handle == 0 || !GetWindowRect(handle, out var r)) return default;
        return new PixelRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    public void Dispose() { retry.Stop(); }

    [StructLayout(LayoutKind.Sequential)] private struct PointNative { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RectNative { public int Left, Top, Right, Bottom; }
    private delegate bool EnumProc(nint hwnd, nint data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowEx(nint parent, nint after, string? className, string? title);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetParent(nint hwnd, nint parent);
    [DllImport("user32.dll")] private static extern nint GetParent(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out RectNative rect);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(nint hwnd, ref PointNative point);
}

/// <summary>Small Win32 helpers for the top-level windows (front layer, deck window, floating widgets).</summary>
internal static class NativeWindow
{
    public const long ExToolWindow = 0x80, ExNoActivate = 0x08000000, ExAppWindow = 0x40000, ExTopmost = 0x8, ExLayered = 0x80000, ExTransparent = 0x20;

    public static void Place(nint hwnd, PixelRect rect, bool? topmost)
    {
        if (hwnd == 0) return;
        nint after = topmost switch { true => -1, false => -2, null => 0 };
        // SWP_NOACTIVATE, plus SWP_NOZORDER when the z-order is left alone.
        SetWindowPos(hwnd, after, rect.X, rect.Y, rect.Width, rect.Height, 0x0010 | (topmost is null ? 0x0004u : 0));
    }

    /// <summary>Moves a window to the top of the topmost windows without activating, moving or resizing it.</summary>
    public static void Raise(nint hwnd)
    {
        if (hwnd == 0) return;
        // SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE
        SetWindowPos(hwnd, -1, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
    }

    /// <summary>Makes a window topmost or not, without moving, resizing or activating it.</summary>
    public static void SetTopmost(nint hwnd, bool topmost)
    {
        if (hwnd == 0) return;
        SetWindowPos(hwnd, topmost ? -1 : -2, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
    }

    /// <summary>The mouse pointer in physical pixels.</summary>
    public static (int X, int Y) Cursor() => GetCursorPos(out var p) ? (p.X, p.Y) : (0, 0);

    public static void AddExStyle(nint hwnd, long flags)
    {
        if (hwnd == 0) return;
        SetWindowLongPtr(hwnd, -20, (nint)(GetWindowLongPtr(hwnd, -20).ToInt64() | flags));
    }

    public static void RemoveExStyle(nint hwnd, long flags)
    {
        if (hwnd == 0) return;
        SetWindowLongPtr(hwnd, -20, (nint)(GetWindowLongPtr(hwnd, -20).ToInt64() & ~flags));
    }

    public static long ExStyle(nint hwnd) => hwnd == 0 ? 0 : GetWindowLongPtr(hwnd, -20).ToInt64();

    public static PixelRect Rect(nint hwnd)
    {
        if (hwnd == 0 || !GetWindowRect(hwnd, out var r)) return default;
        return new PixelRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    /// <summary>
    /// Makes a window of this process the foreground window even when another app is in front (briefly shares that app's input queue).
    /// For when the user just clicked, pressed a hotkey or chose a tray item here, so Windows would allow it anyway but is not always quick to.
    /// </summary>
    public static void Foreground(nint hwnd)
    {
        if (hwnd == 0) return;
        nint current = GetForegroundWindow();
        uint currentThread = current == 0 ? 0 : GetWindowThreadProcessId(current, out _);
        uint mine = GetCurrentThreadId();
        bool attached = currentThread != 0 && currentThread != mine && AttachThreadInput(mine, currentThread, true);
        SetForegroundWindow(hwnd);
        if (attached) AttachThreadInput(mine, currentThread, false);
    }

    [StructLayout(LayoutKind.Sequential)] private struct RectNative { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct PointNative { public int X, Y; }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint thread, uint attachTo, bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out PointNative point);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out RectNative rect);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
}
