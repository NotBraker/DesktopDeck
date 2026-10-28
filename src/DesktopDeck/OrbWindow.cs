using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using DesktopDeck.Layout;
using DesktopDeck.Models;

namespace DesktopDeck;

/// <summary>
/// The control button: a small round floating button that is always on screen (unless turned off) and opens the control center. It acts like a
/// widget: drag it anywhere (press and move more than 4 px; a press that does not move is a click), and its place is saved. It sits above other
/// windows, never takes focus and is not in Alt+Tab. Right-click for quick layout switching.
/// </summary>
internal sealed class OrbWindow : Window
{
    /// <summary>The button's size (DIP) and the transparent room around it for its shadow.</summary>
    internal const double Diameter = 44, Pad = 8, Extent = Diameter + 2 * Pad;
    private const int DragThreshold = 4;

    private readonly DeckHub hub;
    private readonly Grid root = new() { Background = Brushes.Transparent };
    private readonly Ellipse ring = new() { Width = Diameter, Height = Diameter, StrokeThickness = 2 };
    private readonly Grid face = new() { Width = Diameter, Height = Diameter, RenderTransformOrigin = new Point(0.5, 0.5) };
    private readonly ScaleTransform scale = new(1, 1);
    /// <summary>While pressed the pointer is polled (as for the floating widgets): this window never activates, so Windows can stop sending mouse messages mid-drag.</summary>
    private readonly DispatcherTimer follow = new() { Interval = TimeSpan.FromMilliseconds(10) };
    private nint handle;
    private bool pressed, moved, closed, shown;
    private (int X, int Y) startCursor;
    private PixelRect startRect;

    /// <summary>Where the window is (physical pixels).</summary>
    internal PixelRect Bounds { get; private set; }
    internal nint Handle => handle;
    /// <summary>A drag is in progress (host test).</summary>
    internal bool IsGesturing => pressed && moved;

    internal OrbWindow(DeckHub hub)
    {
        this.hub = hub;
        Title = "Desktop Deck control button";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Width = Extent;
        Height = Extent;
        FontFamily = Theme.UiFont;
        UseLayoutRounding = true;

        ring.Fill = new LinearGradientBrush(((SolidColorBrush)Theme.Surface3).Color, ((SolidColorBrush)Theme.Surface).Color, 90);
        ring.Stroke = Theme.Primary;
        var glyph = Theme.Icon(Theme.Glyphs.Settings, 19, Theme.Text);
        face.Children.Add(ring);
        face.Children.Add(glyph);
        face.RenderTransform = scale;
        face.Effect = PanelKit.Shadow(14, 3, 0.55);
        root.Children.Add(face);
        Content = root;
        Cursor = Cursors.Hand;
        ToolTip = "Control center. Drag to move this button.";
        System.Windows.Automation.AutomationProperties.SetName(this, "Desktop Deck control center");

        root.MouseEnter += (_, _) => Animate(1.07, Theme.Text);
        root.MouseLeave += (_, _) => { if (!pressed) Animate(1, null); };
        root.MouseLeftButtonDown += (_, e) => Begin(e);
        root.MouseMove += (_, e) => { if (pressed) Step(); };
        root.MouseLeftButtonUp += (_, e) => { if (pressed) { Finish(); e.Handled = true; } };
        root.LostMouseCapture += (_, _) => { if (pressed && !root.IsMouseCaptured) Finish(); };
        follow.Tick += (_, _) =>
        {
            if (!pressed) { follow.Stop(); return; }
            Step();
            if (!ButtonDown()) Finish();
        };
        ContextMenuOpening += (_, _) => ContextMenu = BuildMenu();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Reapply));
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            NativeWindow.AddExStyle(handle, NativeWindow.ExToolWindow | NativeWindow.ExNoActivate);
            NativeWindow.Place(handle, Bounds, true);
        };
    }

    private void Animate(double to, Brush? stroke)
    {
        var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(110)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        ring.Stroke = stroke ?? Theme.Primary;
    }

    // ---------- Placement ----------

    /// <summary>Where the button belongs for the saved spot: on its monitor (the main one when that is gone), kept inside it.</summary>
    internal PixelRect SavedBounds(out DisplayInfo onDisplay)
    {
        onDisplay = DisplayMap.Resolve(hub.Settings.OrbDisplay, hub.Displays);
        var work = onDisplay.WorkArea;
        int px = Math.Max(1, (int)Math.Round(Extent * onDisplay.Scale));
        int x = work.X + (int)Math.Round(hub.Settings.OrbX * work.Width), y = work.Y + (int)Math.Round(hub.Settings.OrbY * work.Height);
        return FloatLayer.KeepOnScreen(new PixelRect(x, y, px, px), work, inside: true);
    }

    /// <summary>Puts the button where settings say (called when it is created and after monitors change).</summary>
    internal void Place()
    {
        if (closed || hub.Displays.Count == 0) return;
        if (pressed) return;
        Bounds = SavedBounds(out var onDisplay);
        Width = Extent;
        Height = Extent;
        if (!shown) { shown = true; Show(); }
        Reapply();
    }

    private void Reapply()
    {
        if (closed || handle == 0 || pressed) return;
        NativeWindow.Place(handle, Bounds, true);
        if (NativeWindow.Rect(handle) != Bounds) Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (!closed && !pressed && NativeWindow.Rect(handle) != Bounds) NativeWindow.Place(handle, Bounds, true);
        }));
    }

    /// <summary>Back above other topmost windows (without taking focus).</summary>
    internal void Raise() { if (!closed) NativeWindow.Raise(handle); }

    internal void CloseOrb()
    {
        if (closed) return;
        closed = true;
        follow.Stop();
        if (root.IsMouseCaptured) root.ReleaseMouseCapture();
        pressed = false;
        Close();
    }

    // ---------- Click or drag ----------

    private void Begin(MouseButtonEventArgs e)
    {
        if (handle == 0 || closed) return;
        startCursor = NativeWindow.Cursor();
        startRect = NativeWindow.Rect(handle);
        pressed = true;
        moved = false;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        scale.ScaleX = scale.ScaleY = 0.95;
        root.CaptureMouse();
        follow.Start();
        e.Handled = true;
    }

    private void Step()
    {
        var (cx, cy) = NativeWindow.Cursor();
        int dx = cx - startCursor.X, dy = cy - startCursor.Y;
        if (!moved && Math.Max(Math.Abs(dx), Math.Abs(dy)) <= DragThreshold) return;
        moved = true;
        MoveTo(startRect.X + dx, startRect.Y + dy);
    }

    /// <summary>Moves the button to a top-left corner (physical pixels), snapping to screen edges, as a drag does.</summary>
    internal void MoveTo(int x, int y)
    {
        var rect = hub.Floats.Snap(new PixelRect(x, y, Bounds.Width, Bounds.Height), inside: true);
        Bounds = rect;
        NativeWindow.Place(handle, rect, true);
    }

    /// <summary>The mouse was released: a drag saves where the button ended up, a plain press is a click.</summary>
    internal void Finish()
    {
        if (!pressed) return;
        bool wasDrag = moved;
        pressed = false;
        moved = false;
        follow.Stop();
        if (root.IsMouseCaptured) root.ReleaseMouseCapture();
        Animate(root.IsMouseOver ? 1.07 : 1, root.IsMouseOver ? Theme.Text : null);
        if (wasDrag) Commit();
        else hub.OpenControlCenter();
    }

    /// <summary>Remembers the monitor under the button's center and its place as a fraction of that monitor's work area, then saves.</summary>
    internal void Commit()
    {
        if (hub.Displays.Count == 0) return;
        var rect = Bounds;
        var onDisplay = DisplayMap.At(rect.X + rect.Width / 2.0, rect.Y + rect.Height / 2.0, hub.Displays);
        var work = onDisplay.WorkArea;
        rect = FloatLayer.KeepOnScreen(rect, work, inside: true);
        Bounds = rect;
        hub.Settings.OrbDisplay = onDisplay.Id;
        hub.Settings.OrbX = Math.Clamp((rect.X - work.X) / (double)Math.Max(1, work.Width), 0, 1);
        hub.Settings.OrbY = Math.Clamp((rect.Y - work.Y) / (double)Math.Max(1, work.Height), 0, 1);
        hub.Save();
        Reapply();
    }

    private static bool ButtonDown() => (GetAsyncKeyState(SystemParameters.SwapButtons ? 0x02 : 0x01) & 0x8000) != 0;

    // ---------- Right-click menu ----------

    private ContextMenu BuildMenu()
    {
        var menu = MainWindow.Menu();
        MainWindow.AddMenuItem(menu, "Open control center", hub.OpenControlCenter, Theme.Glyphs.Settings);
        MainWindow.AddMenuItem(menu, "New reminder…", () => hub.PromptNewReminder(), "");
        bool showFloats = hub.Advanced || !hub.FloatsVisible || hub.Settings.Items.Any(i => i.OnTop && !i.Hidden);
        if (showFloats) menu.Items.Add(new Separator());
        // The layout switchers are advanced. The simple view has just the pinned widgets' switch (offered when something is pinned, or hidden and could be shown).
        foreach (var (layer, title) in hub.Advanced ? new[] { (LayoutLayers.Floating, "Pinned layout"), (LayoutLayers.Background, "Desktop layout") } : [])
        {
            var sub = new MenuItem { Header = title, Icon = Theme.Icon(Theme.Glyphs.Pin, 14, Theme.Text2) };
            string activeId = LayoutEngine.ActiveId(hub.Settings, layer);
            foreach (var layout in LayoutEngine.Of(hub.Settings, layer))
            {
                string id = layout.Id;
                var entry = MainWindow.AddMenuItem(sub, layout.Name, () => hub.SwitchLayout(layer, id));
                entry.IsCheckable = true;
                entry.IsChecked = id == activeId;
            }
            menu.Items.Add(sub);
        }
        if (showFloats)
            MainWindow.AddMenuItem(menu, hub.FloatsVisible ? "Hide pinned widgets" : "Show pinned widgets", () => hub.SetFloatsVisible(!hub.FloatsVisible), Theme.Glyphs.Monitor);
        menu.Items.Add(new Separator());
        MainWindow.AddMenuItem(menu, "Hide this button", () => hub.SetShowOrb(false), "").ToolTip = "Bring it back from the tray menu or the control center's Settings tab.";
        return menu;
    }

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
}
