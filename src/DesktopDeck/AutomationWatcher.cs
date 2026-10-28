using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using DesktopDeck.Models;

namespace DesktopDeck;

/// <summary>
/// Watches which app is in front and whether it is fullscreen, without polling: a WinEvent hook for foreground changes, and a second hook
/// scoped to the one foreground window for its moves and resizes (entering or leaving fullscreen). Events are debounced, and windows of
/// this process, the Alt+Tab switcher and other transient shell windows never count as a change. Raises its events on the UI thread.
/// </summary>
internal sealed class ForegroundWatcher : IDisposable
{
    private const uint EventForeground = 0x0003, EventLocationChange = 0x800B, OutOfContext = 0, SkipOwnProcess = 2;
    private const int ObjectWindow = 0;
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(400);

    // The delegates are kept in fields: the hooks call them for as long as they exist, and a collected delegate would crash the app.
    private readonly WinEventProc foregroundProc, locationProc;
    private readonly DispatcherTimer timer = new() { Interval = Debounce };
    private readonly Func<IEnumerable<string>> ignore;
    private readonly uint ownProcess = (uint)Environment.ProcessId;
    private nint foregroundHook, locationHook;
    private bool disposed;

    /// <summary>The foreground app's file name (e.g. "Code.exe"), or null when unknown. Unchanged while one of our own or a transient window is in front.</summary>
    public string? Process { get; private set; }
    /// <summary>A fullscreen app, game, video or presentation is in front.</summary>
    public bool Fullscreen { get; private set; }

    public event Action<string?>? ProcessChanged;
    public event Action<bool>? FullscreenChanged;

    /// <param name="ignore">Apps that never count as fullscreen.</param>
    public ForegroundWatcher(Func<IEnumerable<string>> ignore)
    {
        this.ignore = ignore;
        foregroundProc = (_, _, _, _, _, _, _) => timer.Start();
        locationProc = (_, _, window, objectId, _, _, _) => { if (objectId == ObjectWindow && window == GetForegroundWindow()) timer.Start(); };
        timer.Tick += (_, _) => { timer.Stop(); Check(); };
    }

    /// <summary>Starts watching, and reads the current state once (without raising events).</summary>
    public void Start()
    {
        foregroundHook = SetWinEventHook(EventForeground, EventForeground, 0, foregroundProc, 0, 0, OutOfContext | SkipOwnProcess);
        Read(out var process, out var fullscreen, out _);
        Process = process;
        Fullscreen = fullscreen;
        HookLocation(GetForegroundWindow());
    }

    /// <summary>Reads the state again now (a display changed, an option changed) and raises the events for what changed.</summary>
    public void Recheck() { if (!disposed) Check(); }

    private void Check()
    {
        if (disposed) return;
        nint window = GetForegroundWindow();
        HookLocation(window);
        if (!Read(out var process, out var fullscreen, out bool change)) return;
        if (!change) return;
        if (!string.Equals(process, Process, StringComparison.OrdinalIgnoreCase)) { Process = process; ProcessChanged?.Invoke(process); }
        if (fullscreen != Fullscreen) { Fullscreen = fullscreen; FullscreenChanged?.Invoke(fullscreen); }
    }

    /// <summary>The location hook follows the foreground window only, so ordinary window movement elsewhere costs nothing.</summary>
    private void HookLocation(nint window)
    {
        if (locationHook != 0) { UnhookWinEvent(locationHook); locationHook = 0; }
        if (window == 0) return;
        uint thread = GetWindowThreadProcessId(window, out uint pid);
        if (pid == 0 || pid == ownProcess) return;
        locationHook = SetWinEventHook(EventLocationChange, EventLocationChange, 0, locationProc, pid, thread, OutOfContext | SkipOwnProcess);
    }

    /// <summary>
    /// Reads the foreground window. <paramref name="change"/> is false when it is one of ours or a transient shell window (nothing to act on).
    /// The desktop and the taskbar keep the last app's name but are not fullscreen.
    /// </summary>
    private bool Read(out string? process, out bool fullscreen, out bool change)
    {
        process = Process;
        fullscreen = Fullscreen;
        change = false;
        nint window = GetForegroundWindow();
        if (window == 0) return false;
        GetWindowThreadProcessId(window, out uint pid);
        if (pid == 0 || pid == ownProcess) return true;
        string className = ClassName(window);
        if (Transient.Contains(className)) return true;
        change = true;
        if (Shell.Contains(className)) { fullscreen = false; return true; }
        process = ProcessName(pid) ?? process;
        fullscreen = !AutomationEngine.IsIgnored(process, ignore()) && (Covers(window) || NotificationBusy());
        return true;
    }

    // Alt+Tab, the start menu and the like: the user is passing through, not changing app.
    private static readonly HashSet<string> Transient = new(StringComparer.Ordinal)
    {
        "ForegroundStaging", "MultitaskingViewFrame", "XamlExplorerHostIslandWindow", "TaskSwitcherWnd", "TaskSwitcherOverlayWnd", "Windows.UI.Core.CoreWindow",
        "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland", "SysShadow", "tooltips_class32",
    };

    // The desktop and the taskbar: not an app, and never fullscreen.
    private static readonly HashSet<string> Shell = new(StringComparer.Ordinal) { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

    /// <summary>Windows' own idea of "a fullscreen app, a game or a presentation is running" (QUNS_BUSY, QUNS_RUNNING_D3D_FULL_SCREEN, QUNS_PRESENTATION_MODE).</summary>
    private static bool NotificationBusy() => SHQueryUserNotificationState(out int state) == 0 && state is 2 or 3 or 4;

    /// <summary>The window is visible, not minimized, and fullscreen on its monitor (see <see cref="AutomationEngine.LooksFullscreen"/>).</summary>
    private static bool Covers(nint window)
    {
        if (!IsWindowVisible(window) || IsIconic(window) || !GetWindowRect(window, out var rect)) return false;
        nint monitor = MonitorFromWindow(window, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref info)) return false;
        var m = info.Monitor;
        return AutomationEngine.LooksFullscreen((uint)GetWindowLong(window, -16), IsZoomed(window), (rect.Left, rect.Top, rect.Right, rect.Bottom), (m.Left, m.Top, m.Right, m.Bottom)); // GWL_STYLE
    }

    private static string ClassName(nint window)
    {
        var text = new StringBuilder(256);
        return GetClassName(window, text, text.Capacity) > 0 ? text.ToString() : "";
    }

    private static string? ProcessName(uint pid)
    {
        nint process = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if (process == 0) return null;
        try
        {
            var text = new StringBuilder(1024);
            int size = text.Capacity;
            return QueryFullProcessImageName(process, 0, text, ref size) ? Path.GetFileName(text.ToString()) : null;
        }
        finally { CloseHandle(process); }
    }

    /// <summary>The names of the apps that have a window right now (for the "pick a running app" list).</summary>
    public static List<string> RunningApps()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var process in System.Diagnostics.Process.GetProcesses())
            {
                try { if (process.MainWindowHandle != 0 && process.Id != Environment.ProcessId) names.Add(process.ProcessName + ".exe"); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                finally { process.Dispose(); }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        return [.. names];
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        timer.Stop();
        if (foregroundHook != 0) UnhookWinEvent(foregroundHook);
        if (locationHook != 0) UnhookWinEvent(locationHook);
        foregroundHook = locationHook = 0;
    }

    private delegate void WinEventProc(nint hook, uint eventType, nint window, int objectId, int childId, uint thread, uint time);

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc proc, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern bool IsZoomed(nint window);
    [DllImport("user32.dll")] private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder text, int count);
    [DllImport("kernel32.dll")] private static extern nint OpenProcess(uint access, bool inherit, uint process);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder text, ref int size);
    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
}
