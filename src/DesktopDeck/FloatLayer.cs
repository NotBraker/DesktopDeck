using System.Runtime.InteropServices;
using System.Windows.Threading;
using DesktopDeck.Layout;
using DesktopDeck.Models;

namespace DesktopDeck;

/// <summary>
/// The floating layer: every kept-on-top widget (<see cref="LauncherItem.OnTop"/>) in its own small window above other apps.
/// It is independent of the decks: it shows in every display mode, has its own saved position per widget
/// (<see cref="LauncherItem.FloatDisplay"/>, <see cref="LauncherItem.FloatX"/>, <see cref="LauncherItem.FloatY"/>, size), and is moved by
/// dragging the widget itself. One manager here (not one per deck) owns the windows.
/// </summary>
internal sealed class FloatLayer
{
    /// <summary>Within this many pixels of a screen edge a dragged widget snaps to it.</summary>
    public const int SnapDistance = 12;
    /// <summary>At least this much of a floating widget always stays on a screen.</summary>
    public const int KeepVisible = 40;

    private readonly DeckHub hub;
    private readonly Dictionary<string, FloatWindow> windows = new();
    /// <summary>
    /// Watches the pointer for floats that fade when idle or let clicks through (such a window gets no mouse messages of its own). It runs only
    /// while at least one float needs it, a few times a second, and does nothing more than read the pointer and Ctrl.
    /// </summary>
    private readonly DispatcherTimer watch = new() { Interval = TimeSpan.FromMilliseconds(120) };

    public FloatLayer(DeckHub hub)
    {
        this.hub = hub;
        watch.Tick += (_, _) => Watch();
    }

    /// <summary>Ctrl+Z / Ctrl+Y while a floating widget has the keyboard.</summary>
    public bool TryHistoryKey(System.Windows.Input.KeyEventArgs e) => hub.TryHistoryKey(e);

    public IReadOnlyCollection<FloatWindow> Windows => windows.Values;
    public bool Has(string id) => windows.ContainsKey(id);
    public FloatWindow? Find(string id) => windows.GetValueOrDefault(id);

    /// <summary>The deck surface that builds float content (it owns the clocks, menus and dialogs widgets use).</summary>
    public MainWindow? Builder => hub.Primary is { IsLoaded: true, IsClosed: false } primary ? primary : null;

    /// <summary>Size of a widget in DIP while floating: its own float size, else its deck size, else the kind's default (never below the minimum).</summary>
    public static (double Width, double Height) SizeFor(LauncherItem item)
    {
        var (dw, dh) = ItemKinds.DefaultSize(item.Kind);
        var (mw, mh) = ItemKinds.MinimumSize(item.Kind);
        double w = item.FloatWidth > 0 ? item.FloatWidth : item.Width > 0 ? item.Width : dw;
        double h = item.FloatHeight > 0 ? item.FloatHeight : item.Height > 0 ? item.Height : dh;
        return (Math.Max(mw, w), Math.Max(mh, h));
    }

    /// <summary>A widget that was kept on top before floats had their own position floats where it sat on its deck.</summary>
    private static void EnsurePlaced(LauncherItem item)
    {
        if (item.FloatDisplay.Length > 0 || item.FloatX != 0 || item.FloatY != 0 || item.FloatWidth != 0 || item.FloatHeight != 0) return;
        item.FloatDisplay = item.Display;
        item.FloatX = double.IsFinite(item.X) ? Math.Clamp(item.X, 0, 1) : 0;
        item.FloatY = double.IsFinite(item.Y) ? Math.Clamp(item.Y, 0, 1) : 0;
    }

    /// <summary>Where a floating widget is on screen (physical pixels). A missing monitor means the main one; the result stays on screen.</summary>
    public PixelRect BoundsFor(LauncherItem item, DisplayInfo display, double widthDip, double heightDip)
    {
        var work = display.WorkArea;
        int w = Math.Max(1, (int)Math.Round(widthDip * display.Scale)), h = Math.Max(1, (int)Math.Round(heightDip * display.Scale));
        int x = work.X + (int)Math.Round(item.FloatX * work.Width), y = work.Y + (int)Math.Round(item.FloatY * work.Height);
        return KeepOnScreen(new PixelRect(x, y, w, h), work, inside: true);
    }

    /// <summary>Moves a rectangle into a work area: entirely inside when it fits, otherwise with at least <see cref="KeepVisible"/> pixels showing.</summary>
    public static PixelRect KeepOnScreen(PixelRect rect, PixelRect work, bool inside)
    {
        int x = rect.X, y = rect.Y;
        if (inside && rect.Width <= work.Width) x = Math.Clamp(x, work.X, work.Right - rect.Width);
        else x = Math.Clamp(x, work.X + KeepVisible - rect.Width, work.Right - KeepVisible);
        if (inside && rect.Height <= work.Height) y = Math.Clamp(y, work.Y, work.Bottom - rect.Height);
        else y = Math.Clamp(y, work.Y, work.Bottom - KeepVisible);
        return rect with { X = x, Y = y };
    }

    /// <summary>Snaps a dragged rectangle to the work-area edges of the monitor under its center, and keeps it on screen.</summary>
    public PixelRect Snap(PixelRect rect, bool inside)
    {
        if (hub.Displays.Count == 0) return rect;
        var display = DisplayMap.At(rect.X + rect.Width / 2.0, rect.Y + rect.Height / 2.0, hub.Displays);
        var work = display.WorkArea;
        int x = rect.X, y = rect.Y;
        if (Math.Abs(x - work.X) <= SnapDistance) x = work.X;
        else if (Math.Abs(rect.Right - work.Right) <= SnapDistance) x = work.Right - rect.Width;
        if (Math.Abs(y - work.Y) <= SnapDistance) y = work.Y;
        else if (Math.Abs(rect.Bottom - work.Bottom) <= SnapDistance) y = work.Bottom - rect.Height;
        return KeepOnScreen(rect with { X = x, Y = y }, work, inside);
    }

    /// <summary>
    /// Creates, moves and closes the float windows so they match the settings. Rebuilds their content when asked
    /// (and for new ones). Does nothing for widgets that are hidden, or at all while the floating layer is switched off.
    /// </summary>
    public void Sync(bool rebuild)
    {
        SyncCore(rebuild);
        // Placing a float puts it on top of every other topmost window; the control center, when open, stays above them.
        hub.KeepCenterOnTop();
    }

    private void SyncCore(bool rebuild)
    {
        try { SyncWindows(rebuild); }
        finally { UpdateWatch(); }
    }

    private void SyncWindows(bool rebuild)
    {
        var settings = hub.Settings;
        var wanted = hub.FloatsShown ? settings.Items.Where(i => i.OnTop && !i.Hidden && i.GroupId.Length == 0).ToList() : [];
        foreach (var (id, window) in windows.ToList())
        {
            if (wanted.Any(i => i.Id == id)) continue;
            windows.Remove(id);
            window.CloseFloat();
            Builder?.ForgetFloat(id);
        }
        if (wanted.Count == 0 || hub.Displays.Count == 0 || Builder is null) return;
        foreach (var item in wanted)
        {
            EnsurePlaced(item);
            var (w, h) = SizeFor(item);
            var display = DisplayMap.Resolve(item.FloatDisplay, hub.Displays);
            bool isNew = !windows.TryGetValue(item.Id, out var window);
            if (isNew) { window = new FloatWindow(this, item); windows[item.Id] = window; }
            window!.Update(item, display, BoundsFor(item, display, w, h), w, h, rebuild || isNew);
        }
    }

    /// <summary>Starts or stops the pointer watch to match what the floats need.</summary>
    private void UpdateWatch()
    {
        bool needed = windows.Values.Any(w => w.NeedsPolling);
        if (needed && !watch.IsEnabled) watch.Start();
        else if (!needed && watch.IsEnabled) watch.Stop();
    }

    private void Watch()
    {
        var cursor = NativeWindow.Cursor();
        bool ctrl = (GetAsyncKeyState(0x11) & 0x8000) != 0;
        var now = DateTime.UtcNow;
        foreach (var window in windows.Values.ToList()) if (window.NeedsPolling) window.Poll(cursor, ctrl, now);
    }

    /// <summary>True the first time ever a float lets clicks through (then remembered): the window shows a short "Hold Ctrl to use this widget" note.</summary>
    public bool ClaimClickThroughHint()
    {
        if (hub.Settings.ClickThroughHintShown) return false;
        hub.Settings.ClickThroughHintShown = true;
        // Saved a moment later: this runs while the windows are being updated.
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => hub.Save()));
        return true;
    }

    /// <summary>Redraws one widget's float window (its data changed).</summary>
    public void Rebuild(LauncherItem item) => windows.GetValueOrDefault(item.Id)?.Rebuild();

    /// <summary>Puts every float back above other topmost windows (e.g. after the always-on-top deck window was clicked).</summary>
    public void Raise() { foreach (var window in windows.Values) NativeWindow.Raise(window.Handle); }

    /// <summary>
    /// A float finished moving or resizing (mouse released): remember the monitor under its center, its position as a fraction of that
    /// monitor's work area, and its size, then save. The widget keeps its saved monitor otherwise, so a monitor that is unplugged for a while
    /// does not lose it.
    /// </summary>
    public void Commit(LauncherItem item, PixelRect rect, double widthDip, double heightDip)
    {
        if (hub.Displays.Count == 0) return;
        var display = DisplayMap.At(rect.X + rect.Width / 2.0, rect.Y + rect.Height / 2.0, hub.Displays);
        var work = display.WorkArea;
        rect = KeepOnScreen(rect, work, inside: true);
        item.FloatDisplay = display.Id;
        item.FloatX = Math.Clamp((rect.X - work.X) / (double)Math.Max(1, work.Width), 0, 1);
        item.FloatY = Math.Clamp((rect.Y - work.Y) / (double)Math.Max(1, work.Height), 0, 1);
        item.FloatWidth = Math.Round(widthDip, 2);
        item.FloatHeight = Math.Round(heightDip, 2);
        hub.Save();
        Sync(rebuild: false);
    }

    public void CloseAll()
    {
        foreach (var window in windows.Values.ToList()) window.CloseFloat();
        windows.Clear();
        UpdateWatch();
    }

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
}
