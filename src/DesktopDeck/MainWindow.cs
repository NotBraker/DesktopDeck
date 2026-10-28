using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shapes = System.Windows.Shapes;
using System.Windows.Threading;
using DesktopDeck.Layout;
using DesktopDeck.Models;
using DesktopDeck.Services;
using Microsoft.Win32;

namespace DesktopDeck;

/// <summary>What a deck surface is: the desktop background, or the transparent widgets-only layer in front of other windows (peek, Window and Always on top).</summary>
internal enum SurfaceKind { Background, Front }

/// <summary>
/// One deck surface: the items on one monitor, either as part of the desktop (background), as a transparent
/// layer in front of other windows that shows only the widgets (front).
/// <see cref="DeckHub"/> owns all surfaces. Kept-on-top widgets are not on any surface: they float in their own windows.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const string HideGlyph = "";
    private const double ChromeTop = 14, ChromeSide = 16, ChromeHeight = 34;
    private static Brush EditDim => Theme.Scrim;
    private readonly DeckHub hub;
    private readonly DeckSettings settings;
    private readonly SettingsStore store;
    private readonly LauncherService launcher;
    private readonly Canvas canvas = new();
    /// <summary>The monitor this surface covers.</summary>
    internal DisplayInfo Display { get; set; }
    internal SurfaceKind Kind { get; }
    /// <summary>True for the transparent layer shown in front of other windows.</summary>
    internal bool IsFront => Kind == SurfaceKind.Front;
    internal bool InModal => modal > 0;
    private readonly Shapes.Rectangle gridLayer = new() { IsHitTestVisible = false };
    private readonly Shapes.Rectangle centerGuide = new() { IsHitTestVisible = false, Stroke = Theme.LineStrong, StrokeThickness = 1.5, StrokeDashArray = [6, 5], RadiusX = 18, RadiusY = 18 };
    private readonly Dictionary<string, Grid> views = new();
    private readonly Dictionary<string, TileRect> rects = new();
    private readonly Dictionary<string, Action> statusUpdaters = new();
    /// <summary>Per-item clock callbacks. Seconds() decides whether that item needs a 1 s or a 1 min tick.</summary>
    private readonly Dictionary<string, (Func<bool> Seconds, Action<DateTimeOffset> Tick)> tickers = new();
    private readonly List<LauncherItem> hiddenItems = new();
    private readonly Button settingsButton = Theme.IconButton(Theme.Glyphs.Settings, "Control center (Ctrl+Alt+S)", 36);
    private readonly StackPanel controls = new() { Orientation = Orientation.Horizontal };
    private readonly Button editButton = Theme.Button("Edit layout");
    private readonly Button addButton = Theme.Button("+ Add", true);
    private readonly Button gridButton = Theme.Button("Grid");
    private readonly Button roomButton = Theme.Button("");
    private readonly Button backButton = Theme.Button("Back to desktop");
    private readonly Button modeButton = Theme.Button("⧉ Desktop");
    private readonly Grid chromeHost = new();
    private string displayMode = "background";
    private readonly Border toast = new() { CornerRadius = new CornerRadius(10), Background = Theme.Surface3, BorderBrush = Theme.LineStrong, BorderThickness = new Thickness(1), Padding = new Thickness(14, 9, 14, 9), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    private readonly TextBlock toastText = Theme.Label("", 12.5, Theme.Text);
    /// <summary>Shown in the middle of an empty monitor's deck so it's clear how to fill it.</summary>
    private readonly TextBlock emptyHint = new() { FontFamily = Theme.UiFont, FontSize = 15, Foreground = Theme.Muted, TextAlignment = TextAlignment.Center, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly DispatcherTimer toastTimer = new() { Interval = TimeSpan.FromSeconds(3.2) };
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly DispatcherTimer ticker = new();
    private DragState? drag;
    private int modal;
    private bool peek, closingSurface;
    /// <summary>Encrypted secrets (DPAPI): sign-in tokens, passwords and private addresses.</summary>
    internal SecretStore Secrets => hub.Secrets;
    public bool IsEditing { get; private set; }

    private sealed class DragState
    {
        public required LauncherItem Item;
        public required FrameworkElement Handle;
        public required Grid View;
        public required bool Resize;
        public required Point Start;
        public required TileRect Origin;
        public TileRect Current;
        public bool Moved;
        /// <summary>Another monitor the pointer is over; dropping there moves the item to it.</summary>
        public DisplayInfo? Target;
        public Point Screen;
        /// <summary>Set when several selected widgets move together.</summary>
        public GroupDrag? Group;
    }

    internal MainWindow(DeckHub hub, DisplayInfo display, SurfaceKind kind)
    {
        this.hub = hub;
        Display = display;
        Kind = kind;
        bool front = kind == SurfaceKind.Front;
        settings = hub.Settings;
        store = hub.Store;
        launcher = hub.Launcher;
        Title = front ? "Desktop Deck" : "Desktop Deck background";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        FontFamily = Theme.UiFont;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Content = canvas;
        if (front)
        {
            // Only the widgets show; the empty space is fully transparent, so clicks there reach the apps behind.
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            canvas.Background = Brushes.Transparent;
            SourceInitialized += (_, _) => PlaceOnDisplay();
        }
        else
        {
            Background = Theme.Background;
            canvas.Background = Appearance.DeckBackground(settings, display, new Size(display.WorkArea.Width / display.Scale, display.WorkArea.Height / display.Scale), display.Scale); // 1B: appearance
            DpiChanged += (_, _) => { if (hub.HostFor(Display.Id) is not null) Dispatcher.BeginInvoke(new Action(Compensate)); };
        }
        canvas.ClipToBounds = true;
        canvas.AllowDrop = true;
        canvas.Drop += OnDrop;
        canvas.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        ApplyGridSettings();
        canvas.Children.Add(gridLayer);
        canvas.Children.Add(centerGuide);
        BuildChrome();
        Panel.SetZIndex(gridLayer, -2);
        Panel.SetZIndex(centerGuide, -1);
        InitSelection();
        saveTimer.Tick += (_, _) => FlushSave();
        ticker.Tick += (_, _) => OnTick();
        toastTimer.Tick += (_, _) => { toastTimer.Stop(); toast.Visibility = Visibility.Collapsed; };
        IsEditing = hub.IsEditing;
        Loaded += (_, _) =>
        {
            if (kind == SurfaceKind.Background && hub.HostFor(display.Id) is not null) Compensate();
            SetPresentation(hub.Mode, hub.Peeking);
            UpdateEditChrome();
            Render();
            // The deck on the main monitor also builds the floating widgets, so they appear once it is ready.
            if (kind == SurfaceKind.Background) hub.Floats.Sync(true);
        };
        canvas.SizeChanged += (_, _) => { if (IsLoaded) Render(); };
        InitRegion();
        launcher.StatusChanged += LauncherStatusChanged;
        MediaService.Instance.Changed += OnMediaChanged;
        Closed += (_, _) =>
        {
            closingSurface = true;
            ImageCache.Forget(this);
            launcher.StatusChanged -= LauncherStatusChanged; ticker.Stop(); FlushSave();
            MediaService.Instance.Changed -= OnMediaChanged;
            DisposeWatchers();
        };
        if (front) Deactivated += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(hub.CheckLeave));
        PreviewKeyDown += (_, e) =>
        {
            if (HandleEditKey(e)) return;
            if (e.Key == Key.Escape && IsEditing) { hub.SetEditing(false); e.Handled = true; }
            else if (e.Key == Key.Escape && peek) { hub.Peek(false); e.Handled = true; }
            else if (e.Key == Key.E && Keyboard.Modifiers == ModifierKeys.Control) { hub.SetEditing(!IsEditing); e.Handled = true; }
            else hub.TryHistoryKey(e);
        };
    }

    /// <summary>Closes this surface for good (display removed, front layer dismissed, or exit).</summary>
    internal void CloseSurface()
    {
        if (closingSurface) return;
        closingSurface = true;
        EndDrag(false);
        Close();
    }

    /// <summary>Front layer: cover this monitor's work area exactly (physical pixels).</summary>
    internal void PlaceOnDisplay()
    {
        if (!IsFront) return;
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        NativeWindow.Place(handle, Display.WorkArea, Topmost ? true : null);
    }

    /// <summary>
    /// A window attached to the desktop takes the desktop's DPI, which can differ from this monitor's. Scale the
    /// content so widgets have the same size on every monitor.
    /// </summary>
    private void Compensate()
    {
        double windowScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double factor = windowScale > 0 ? Display.Scale / windowScale : 1;
        canvas.LayoutTransform = Math.Abs(factor - 1) < 0.01 ? Transform.Identity : new ScaleTransform(factor, factor);
    }

    // ---------- Chrome: settings gear (left), controls (right), toast ----------

    private void BuildChrome()
    {
        settingsButton.Click += (_, _) => hub.OpenControlCenter();
        Canvas.SetLeft(settingsButton, ChromeSide);
        Canvas.SetTop(settingsButton, ChromeTop);
        Panel.SetZIndex(settingsButton, 3);
        canvas.Children.Add(settingsButton);

        foreach (var button in new[] { roomButton, backButton, addButton, gridButton }) button.Margin = new Thickness(0, 0, 8, 0);
        roomButton.Visibility = addButton.Visibility = gridButton.Visibility = backButton.Visibility = Visibility.Collapsed;
        roomButton.BorderBrush = Theme.Primary;
        roomButton.ToolTip = "Some items don't fit on this screen right now.";
        roomButton.Click += (_, _) => OpenRoomMenu();
        backButton.Click += (_, _) => hub.Peek(false);
        backButton.ToolTip = "Send the deck back behind your windows (Esc)";
        addButton.Click += (_, _) => OpenAddMenu(addButton);
        gridButton.ToolTip = "Snap positions and sizes to a grid. Hold Alt while dragging to place freely.";
        gridButton.Click += (_, _) => hub.SetSnap(!settings.SnapToGrid);
        editButton.Click += (_, _) => hub.SetEditing(!IsEditing);
        editButton.ToolTip = "Move, resize and add things (Ctrl+E)";
        modeButton.Margin = new Thickness(0, 0, 8, 0);
        modeButton.ToolTip = "Show the widgets on the desktop, as an overlay you can Alt+Tab to, or always on top of your apps";
        modeButton.Click += (_, _) => OpenModeMenu();
        foreach (var b in new[] { roomButton, backButton, addButton, gridButton, modeButton, editButton }) controls.Children.Add(b);
        // The controls sit in a box that spans the free width and lines them up against its right edge, so their position comes from the
        // layout itself (never from a measurement that can be a step behind) and they shrink rather than leave the screen.
        chromeHost.VerticalAlignment = VerticalAlignment.Top;
        chromeHost.Children.Add(new Viewbox
        {
            Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Child = controls,
        });
        Canvas.SetTop(chromeHost, ChromeTop);
        Panel.SetZIndex(chromeHost, 3);
        canvas.Children.Add(chromeHost);

        emptyHint.Text = "This monitor's deck is empty." + Environment.NewLine + "Choose Edit layout, then + Add, or drag items here from another monitor.";
        canvas.Children.Add(emptyHint);

        toast.Child = BuildToastContent();
        Canvas.SetLeft(toast, ChromeSide);
        Panel.SetZIndex(toast, 4);
        canvas.Children.Add(toast);
    }

    /// <summary>The size an element wants right now. Parents are only told about a child's change at the next layout pass, so a plain Measure can answer from a cache; this drops the caches first.</summary>
    private static Size MeasureFresh(FrameworkElement element)
    {
        void Invalidate(DependencyObject node)
        {
            if (node is UIElement ui) ui.InvalidateMeasure();
            for (int i = 0, n = VisualTreeHelper.GetChildrenCount(node); i < n; i++) Invalidate(VisualTreeHelper.GetChild(node, i));
        }
        Invalidate(element);
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return element.DesiredSize;
    }

    /// <summary>Desktop icons are shown on this deck: the gear is left out (Explorer starts its icons in that corner), and the deck is cut down to its widgets.</summary>
    private bool IconsShown => !IsFront && hub.ShowDesktopIcons;

    /// <summary>Space taken at the top-left: the settings gear (not there while desktop icons are shown).</summary>
    private double LeftChromeWidth() => IconsShown ? 0 : ChromeSide + 36;

    /// <summary>The width the top-right controls really take (they shrink when the screen is narrower than they are).</summary>
    private double RightChromeWidth()
    {
        double want = MeasureFresh(controls).Width;
        double room = Math.Max(0, canvas.ActualWidth - ChromeSide * 2 - LeftChromeWidth());
        return Math.Min(want, room);
    }

    private double leftChrome = ChromeSide + 36, rightChrome;

    /// <summary>Settings → Show desktop icons changed: the gear, the background and the deck's region follow.</summary>
    internal void ApplyDesktopIcons() { UpdateEditChrome(); ScheduleRegion(); }

    private void PlaceChrome()
    {
        settingsButton.Visibility = IconsShown ? Visibility.Collapsed : Visibility.Visible;
        leftChrome = LeftChromeWidth();
        rightChrome = RightChromeWidth();
        double left = leftChrome + 12;
        double width = Math.Max(0, canvas.ActualWidth - ChromeSide - left);
        chromeHost.Width = width;
        Canvas.SetLeft(chromeHost, left);
        Canvas.SetTop(toast, canvas.ActualHeight - 56);
        ScheduleRegion();
    }

    private static Brush GridBrush()
    {
        // One frozen tiled drawing: a dot at every grid intersection.
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, DeckLayout.Grid, DeckLayout.Grid))));
        drawing.Children.Add(new GeometryDrawing(Theme.LineStrong, null, new EllipseGeometry(new Point(DeckLayout.Grid / 2, DeckLayout.Grid / 2), 1.3, 1.3)));
        var brush = new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(-DeckLayout.Grid / 2, -DeckLayout.Grid / 2, DeckLayout.Grid, DeckLayout.Grid),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
        brush.Freeze();
        return brush;
    }

    /// <summary>Edit mode is shared by every surface; <see cref="DeckHub.SetEditing"/> calls this on each.</summary>
    internal void ApplyEditing(bool value)
    {
        if (IsEditing == value) return;
        EndDrag(false);
        IsEditing = value;
        UpdateEditChrome();
        if (value && (IsFront || !hub.Fronts.Any())) SetStatus("Drag to move • drag onto another monitor to move it there • corner to resize • double-click to customize • Esc when done" + (hub.ShowDesktopIcons ? " • desktop icons are hidden while you edit" : ""));
        Render();
    }

    private void UpdateEditChrome()
    {
        editButton.Content = IsEditing ? "Done" : "Edit layout";
        editButton.ToolTip = "Move, resize and add things (Ctrl+E)" + (IconsShown ? ". Your desktop icons are hidden while you edit and come back on Done." : "");
        editButton.Background = IsEditing ? Theme.Primary : Theme.Surface;
        editButton.Foreground = IsEditing ? Theme.OnPrimary : Theme.Text;
        // + Add is always there in the simple view (people don't find it behind Edit layout); the grid and the display mode are advanced.
        addButton.Visibility = IsEditing || !hub.Advanced ? Visibility.Visible : Visibility.Collapsed;
        gridButton.Visibility = IsEditing ? Visibility.Visible : Visibility.Collapsed;
        modeButton.Visibility = hub.Advanced ? Visibility.Visible : Visibility.Collapsed;
        // The front layer dims while editing, so the layout and grid are visible and empty space takes drops.
        if (IsFront) canvas.Background = IsEditing ? EditDim : Brushes.Transparent;
        UpdateGridButton();
        PlaceChrome();
    }

    /// <summary>Settings → Show advanced features changed: the top bar shows or hides its advanced buttons.</summary>
    internal void ApplyAdvanced() => UpdateEditChrome();

    /// <summary>
    /// Called by the app when the display mode changes. peek: a temporary visit in front of other windows
    /// (Ctrl+Alt+D) that ends when you switch away.
    /// </summary>
    public void SetPresentation(string mode, bool peeking)
    {
        bool changed = displayMode != mode || peek != (IsFront && peeking);
        displayMode = mode;
        peek = IsFront && peeking;
        backButton.Visibility = peek ? Visibility.Visible : Visibility.Collapsed;
        modeButton.Content = mode switch { "window" => "⧉ Window", "ontop" => "⧉ On top", _ => "⧉ Desktop" };
        modeButton.BorderBrush = mode == "background" ? Theme.LineStrong : Theme.Primary;
        PlaceChrome();
        if (!changed || !IsLoaded) return;
        // Messages go where you're looking: the front layer when it's shown, otherwise the desktop.
        if (IsFront && peek) SetStatus("Only your widgets are in front. Press Esc or Ctrl+Alt+D to send them back.");
        else if (IsFront && mode == "ontop") SetStatus("Your widgets are kept above other windows. Choose On the desktop in the ⧉ menu to send them back.");
        else if (IsFront && mode == "window") SetStatus("Your widgets are an overlay you can Alt+Tab to. Choose On the desktop in the ⧉ menu to send them back.");
        else if (mode == "background" && !peeking) SetStatus("Back to being the desktop background.");
    }

    private void OpenModeMenu()
    {
        var menu = Menu();
        foreach (var (mode, label, tip) in new[]
        {
            ("background", "On the desktop", "Part of the desktop, behind your windows."),
            ("window", "Over my apps (Alt+Tab)", "Your widgets as an overlay on every monitor that you can Alt+Tab to. Other apps can cover it."),
            ("ontop", "Always on top of my apps", "The same overlay, kept above every other window."),
        })
        {
            var item = AddMenuItem(menu, label, () => hub.SetMode(mode));
            item.IsCheckable = true;
            item.IsChecked = displayMode == mode;
            item.ToolTip = tip;
        }
        menu.PlacementTarget = modeButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    internal string DisplayMode => displayMode;

    private void UpdateGridButton()
    {
        gridButton.Content = settings.SnapToGrid ? "Grid: on" : "Grid: off";
        gridButton.BorderBrush = settings.SnapToGrid ? Theme.Primary : Theme.LineStrong;
    }

    // Walls just outside the canvas plus the settings gear and the top-right controls.
    private List<TileRect> Reserved()
    {
        double w = canvas.ActualWidth, h = canvas.ActualHeight, far = 100000;
        var list = new List<TileRect>
        {
            new(-far, -far, far, 3 * far), new(w, -far, far, 3 * far),
            new(-far, -far, 3 * far, far), new(-far, h, 3 * far, far),
            new(-far, -far, far + leftChrome, far + ChromeTop + 36),
        };
        double width = rightChrome + ChromeSide;
        list.Add(new(w - width, -far, width + far, far + ChromeTop + ChromeHeight));
        return list;
    }

    private List<TileRect> OccupiedExcept(string? id) =>
        Reserved().Concat(rects.Where(p => p.Key != id).Select(p => p.Value)).ToList();

    /// <summary>
    /// The room taken by the desktop icons on this deck, for placing NEW widgets (+ Add, imports, starters, templates) beside them rather than on top.
    /// Read through the shell; when that fails the left quarter of the main monitor is kept clear instead. Nothing while icons are off.
    /// Moving an existing widget is never limited by the icons (it may cover them, as any window would).
    /// </summary>
    internal List<TileRect> IconRects()
    {
        // A deck that is not on the desktop (a preview, or the desktop is not reachable) covers no icons.
        if (!IconsShown || hub.HostFor(Display.Id) is not { IsAttached: true }) return [];
        var cells = DesktopIcons.Read();
        if (cells is null) return IconSpace.Fallback(Display.IsPrimary, canvas.ActualWidth, canvas.ActualHeight);
        var area = Display.WorkArea;
        return IconSpace.Occupied(cells, area.X, area.Y, area.Width, area.Height, Display.Scale);
    }

    /// <summary>What a new widget has to stay clear of: the walls, the buttons, the other widgets and (with desktop icons shown) the icons.</summary>
    private List<TileRect> OccupiedForNew()
    {
        var list = OccupiedExcept(null);
        list.AddRange(IconRects());
        return list;
    }

    private static (double Width, double Height) SizeOf(LauncherItem item)
    {
        var (dw, dh) = ItemKinds.DefaultSize(item.Kind);
        var (mw, mh) = ItemKinds.MinimumSize(item.Kind);
        return (Math.Max(mw, item.Width > 0 ? item.Width : dw), Math.Max(mh, item.Height > 0 ? item.Height : dh));
    }

    private double GridStep => settings.SnapToGrid && (GetAsyncKeyState(0x12) & 0x8000) == 0 ? DeckLayout.Grid : 0;

    /// <summary>Redraws everything on this surface (called by the hub after shared changes).</summary>
    internal void RenderSurface() { if (IsLoaded && !closingSurface) Render(); }

    /// <summary>True if the item is laid out on this surface (drawn, or floating in its own window).</summary>
    internal bool Shows(string id) => rects.ContainsKey(id);
    internal bool IsClosed => closingSurface;
    /// <summary>Where each item sits on this surface (DIP), for the host test.</summary>
    internal IEnumerable<TileRect> LaidOut => rects.Values;
    internal Canvas Surface => canvas;
    /// <summary>True while the gear (control center) button is visible on this deck; it is left out while desktop icons are shown.</summary>
    internal bool GearShown => settingsButton.IsVisible;
    internal nint Hwnd => new System.Windows.Interop.WindowInteropHelper(this).Handle;
    /// <summary>The top-right controls' rectangle on screen in physical pixels (host test: it must stay inside the work area).</summary>
    internal PixelRect ChromeScreenBounds()
    {
        if (PresentationSource.FromVisual(controls) is null || controls.ActualWidth <= 0) return default;
        var a = controls.PointToScreen(new Point(0, 0));
        var b = controls.PointToScreen(new Point(controls.ActualWidth, controls.ActualHeight));
        return new PixelRect((int)Math.Round(a.X), (int)Math.Round(a.Y), (int)Math.Round(b.X - a.X), (int)Math.Round(b.Y - a.Y));
    }
    internal string ChromeDiagnostics() => $"canvas={canvas.ActualWidth:0.##}x{canvas.ActualHeight:0.##} window={ActualWidth:0.##}x{ActualHeight:0.##} dpi={VisualTreeHelper.GetDpi(this).DpiScaleX} host={Canvas.GetLeft(chromeHost):0.##}+{chromeHost.ActualWidth:0.##} controls={controls.ActualWidth:0.##} transform={canvas.LayoutTransform}";

    /// <summary>Smoke test: renders an item menu, the "+ Add" gallery and the mode menu to PNGs.</summary>
    internal string RenderMenusForSmoke(string folder)
    {
        var report = new List<string>();
        void Shot(FrameworkElement element, string name)
        {
            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            element.Arrange(new Rect(element.DesiredSize));
            element.UpdateLayout();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(element.ActualWidth)), Math.Max(1, (int)Math.Ceiling(element.ActualHeight)), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(element);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(folder, name));
            encoder.Save(output);
            report.Add($"{name}: {element.ActualWidth:0}x{element.ActualHeight:0}");
        }
        // A submenu's items live inside their parent: borrow them into a menu of their own for a picture, then give them back.
        void ShotSubmenus(ContextMenu top, string prefix)
        {
            foreach (var sub in top.Items.OfType<MenuItem>().Where(m => m.Items.Count > 0 && m.Visibility == Visibility.Visible).ToList())
            {
                var items = sub.Items.Cast<object>().ToList();
                var temp = Menu();
                sub.Items.Clear();
                foreach (var entry in items) temp.Items.Add(entry);
                temp.ApplyTemplate();
                Shot(temp, prefix + "-" + new string(sub.Header.ToString()!.Where(char.IsLetter).ToArray()).ToLowerInvariant() + ".png");
                report.Add($"  {sub.Header}: " + string.Join(" | ", items.Select(x => x is MenuItem mi ? mi.Header.ToString() : "-")));
                temp.Items.Clear();
                foreach (var entry in items) sub.Items.Add(entry);
            }
        }
        var sample = settings.Items.FirstOrDefault(i => i.Kind == ItemKinds.Launcher) ?? settings.Items.FirstOrDefault();
        if (sample is not null)
        {
            var menu = BuildItemMenu(sample);
            menu.ApplyTemplate();
            Shot(menu, "menu.png");
            report.Add($"menu.png entries: {menu.Items.Count} (" + string.Join(" | ", menu.Items.OfType<MenuItem>().Select(m => m.Header + (m.Items.Count > 0 ? $" [{m.Items.Count}]" : ""))) + ")");
            ShotSubmenus(menu, "menu");
            // The simple view's menu has Pin on top, Size, Hide and Remove… where the advanced one has Look, Place on screen and More (see the simple-mode design).
            var headers = menu.Items.OfType<MenuItem>().Select(m => m.Header as string).ToList();
            string[] simpleOnly = ["Pin on top", "Size", "Remove…"], advancedOnly = ["Look", "Place on screen", "More"];
            bool ok = headers.Contains("Customize…") && headers.Contains("Hide") && (hub.Advanced
                ? advancedOnly.Where(h => sample.Kind != ItemKinds.Image || h != "Look").All(headers.Contains) && !simpleOnly.Any(headers.Contains)
                : simpleOnly.All(headers.Contains) && !advancedOnly.Any(headers.Contains));
            report.Add(ok ? $"menu.png matches the {(hub.Advanced ? "advanced" : "simple")} view" : $"FAIL menu.png does not match the {(hub.Advanced ? "advanced" : "simple")} view");
        }
        // A widget that can be linked, once alone and once linked with a copy: the "More" submenu then holds Link with and Unlink.
        if (settings.Items.FirstOrDefault(i => Models.WidgetLinks.CanLink(i.Kind) && i.GroupId.Length == 0) is { } linkable)
        {
            var twin = linkable.Copy();
            twin.Id = Guid.NewGuid().ToString("N");
            twin.LinkId = "";
            settings.Items.Add(twin);
            try
            {
                var alone = BuildItemMenu(linkable);
                alone.ApplyTemplate();
                ShotSubmenus(alone, "menu-more-unlinked");
                if (Models.WidgetLinks.Link(linkable, twin, settings.Items))
                {
                    var linked = BuildItemMenu(linkable);
                    linked.ApplyTemplate();
                    Shot(linked, "menu-linked-widget.png");
                    ShotSubmenus(linked, "menu-more-linked");
                    Models.WidgetLinks.Unlink(twin, settings.Items);
                }
            }
            finally { settings.Items.Remove(twin); }
        }
        // A floating widget: "Place on screen" also holds fade when idle, how far, and click-through; its quick sizes are one level down.
        if ((settings.Items.FirstOrDefault(i => i.Kind == ItemKinds.Clock && i.GroupId.Length == 0) ?? sample)?.Copy() is { } floater)
        {
            floater.Id = Guid.NewGuid().ToString("N");
            floater.LinkId = "";
            floater.OnTop = true;
            floater.FloatIdleFade = true;
            settings.Items.Add(floater);
            try
            {
                var floating = BuildItemMenu(floater);
                floating.ApplyTemplate();
                if (floating.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header as string == "Place on screen") is { } place)
                {
                    var placeItems = place.Items.Cast<object>().ToList();
                    var temp = Menu();
                    place.Items.Clear();
                    foreach (var entry in placeItems) temp.Items.Add(entry);
                    temp.ApplyTemplate();
                    Shot(temp, "menu-floating-placeonscreen.png");
                    ShotSubmenus(temp, "menu-floating-place");
                    temp.Items.Clear();
                    foreach (var entry in placeItems) place.Items.Add(entry);
                }
            }
            finally { settings.Items.Remove(floater); }
        }
        if (settings.Items.FirstOrDefault(i => i.Kind == ItemKinds.Group) is { } group)
        {
            var groupMenu = BuildItemMenu(group);
            groupMenu.ApplyTemplate();
            Shot(groupMenu, "menu-group.png");
            if (WidgetOrganizer.Children(settings, group.Id).FirstOrDefault() is { } tabbed)
            {
                var tabMenu = BuildTabMenu(group, tabbed);
                tabMenu.ApplyTemplate();
                Shot(tabMenu, "menu-tab.png");
            }
        }
        Shot(BuildGallery(), "gallery.png");
        return string.Join(Environment.NewLine, report);
    }

    /// <summary>The items shown on this monitor. Pinned widgets float in their own windows (no room is reserved for them here) and hidden ones show nowhere.</summary>
    private IEnumerable<LauncherItem> MyItems => settings.Items.Where(i => !i.OnTop && !i.Hidden && i.GroupId.Length == 0 && hub.DisplayOf(i).Id == Display.Id);

    private void Render()
    {
        double width = canvas.ActualWidth, height = canvas.ActualHeight;
        if (width < 1 || height < 1 || closingSurface) return;
        if (!IsFront) canvas.Background = Appearance.DeckBackground(settings, Display, new Size(width, height), Display.Scale); // 1B: appearance
        ApplyGridSettings();
        EndDrag(false);
        foreach (var view in views.Values) canvas.Children.Remove(view);
        views.Clear();
        overlays.Clear();
        rects.Clear();
        // Clocks and status updates of the floating widgets (built here, shown in their own windows) keep running.
        foreach (var key in statusUpdaters.Keys.Where(k => !k.StartsWith(FloatKeyPrefix, StringComparison.Ordinal)).ToList()) statusUpdaters.Remove(key);
        foreach (var key in tickers.Keys.Where(k => !k.StartsWith(FloatKeyPrefix, StringComparison.Ordinal)).ToList()) tickers.Remove(key);
        hiddenItems.Clear();
        gridLayer.Width = width;
        gridLayer.Height = height;
        gridLayer.Visibility = IsEditing && settings.SnapToGrid ? Visibility.Visible : Visibility.Collapsed;
        centerGuide.Width = width * 0.6;
        centerGuide.Height = height * 0.6;
        Canvas.SetLeft(centerGuide, width * 0.2);
        Canvas.SetTop(centerGuide, height * 0.2);
        centerGuide.Visibility = IsEditing && settings.KeepCenterClear ? Visibility.Visible : Visibility.Collapsed;
        UpdateGridButton();
        PlaceChrome();

        var occupied = Reserved();
        ImageCache.BeginPass(this);
        foreach (var item in MyItems)
        {
            var (w, h) = SizeOf(item);
            double x = double.IsFinite(item.X) ? item.X * width : 24, y = double.IsFinite(item.Y) ? item.Y * height : 96;
            var rect = new TileRect(x, y, w, h);
            // Keep saved positions exactly; only relocate items that no longer fit.
            if (!DeckLayout.IsAllowed(rect, width, height, settings.KeepCenterClear) || occupied.Any(o => DeckLayout.Overlaps(rect, o)))
            {
                if (DeckLayout.FindSpace(x, y, w, h, width, height, occupied, settings.KeepCenterClear) is not { } p) { hiddenItems.Add(item); continue; }
                rect = rect with { X = p.X, Y = p.Y };
            }
            occupied.Add(rect);
            rects[item.Id] = rect;
            var built = BuildView(item, rect);
            views[item.Id] = built;
            canvas.Children.Add(built);
        }
        ImageCache.EndPass(this);
        AfterRenderSelection();
        PruneWatchers();
        roomButton.Content = $"{hiddenItems.Count} need room";
        roomButton.Visibility = hiddenItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        PlaceChrome();
        UpdateTicker();
        bool empty = !IsFront && rects.Count == 0 && hiddenItems.Count == 0 && (!Display.IsPrimary || settings.Items.Count == 0);
        emptyHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (empty)
        {
            emptyHint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(emptyHint, (width - emptyHint.DesiredSize.Width) / 2);
            Canvas.SetTop(emptyHint, (height - emptyHint.DesiredSize.Height) / 2);
        }
        if (settings.Items.Count == 0 && Display.IsPrimary && !IsFront) SetStatus("Your desktop, within reach. Choose Edit layout, then + Add.");
        if (!IsFront) ScheduleRegion();
    }

    private Grid BuildView(LauncherItem item, TileRect rect)
    {
        var view = new Grid { Width = rect.Width, Height = rect.Height, Tag = item.Id };
        Canvas.SetLeft(view, rect.X);
        Canvas.SetTop(view, rect.Y);
        view.Children.Add(BuildContent(item, rect.Width, rect.Height));
        if (IsEditing) view.Children.Add(BuildEditOverlay(item, view));
        view.ContextMenu = BuildItemMenu(item);
        return view;
    }

    private Border BuildEditOverlay(LauncherItem item, Grid view)
    {
        var accent = Theme.Accent(item.Accent);
        var overlay = new Border
        {
            Background = Brushes.Transparent, CornerRadius = new CornerRadius(Appearance.Radius(settings.Appearance, item)),
            BorderBrush = Theme.Tint(item.Accent, 0.6), BorderThickness = new Thickness(1.5),
            Cursor = item.Locked ? Cursors.No : Cursors.SizeAll, Focusable = true,
            ToolTip = item.Locked ? "Position locked — right-click to unlock" : null,
        };
        var layer = new Grid();
        overlay.Child = layer;
        var dots = Theme.Label("⠿⠿⠿", 10, Theme.Muted);
        dots.HorizontalAlignment = HorizontalAlignment.Left;
        dots.VerticalAlignment = VerticalAlignment.Top;
        dots.Margin = new Thickness(10, 3, 0, 0);
        layer.Children.Add(dots);
        if (LinkBadge(item) is { } linkBadge) layer.Children.Add(linkBadge);
        if (item.Locked)
        {
            var badge = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = Theme.Background, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 6, 0) };
            badge.Child = Theme.Icon(Theme.Glyphs.Lock, 12, accent);
            layer.Children.Add(badge);
        }
        else
        {
            var grip = new Border
            {
                Width = 22, Height = 22, Background = Brushes.Transparent, Cursor = Cursors.SizeNWSE,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 3, 3),
                ToolTip = "Drag to resize",
                Child = new Shapes.Path { Data = Geometry.Parse("M 14,4 L 4,14 M 14,9 L 9,14"), Stroke = accent, StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round },
            };
            grip.MouseLeftButtonDown += (_, e) => { BeginDrag(item, grip, view, true, e); e.Handled = true; };
            layer.Children.Add(grip);
        }
        overlay.MouseLeftButtonDown += (_, e) =>
        {
            overlay.Focus();
            if (e.ClickCount == 2) { EndDrag(false); EditItem(item); e.Handled = true; return; }
            if (SelectionClick(item)) { e.Handled = true; return; }
            if (!item.Locked) BeginDrag(item, overlay, view, false, e);
            e.Handled = true;
        };
        overlay.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space) { EditItem(item); e.Handled = true; }
            else if (e.Key == Key.Delete) { RemoveItem(item); e.Handled = true; }
        };
        RegisterOverlay(item, overlay);
        return overlay;
    }

    private void BeginDrag(LauncherItem item, FrameworkElement handle, Grid view, bool resize, MouseButtonEventArgs e)
    {
        if (!rects.TryGetValue(item.Id, out var origin)) return;
        EndDrag(false);
        drag = new DragState { Item = item, Handle = handle, View = view, Resize = resize, Start = e.GetPosition(canvas), Origin = origin, Current = origin };
        drag.Group = StartGroupDrag(item, resize);
        handle.MouseMove += OnDragMove;
        handle.MouseLeftButtonUp += OnDragUp;
        handle.LostMouseCapture += OnDragLost;
        handle.CaptureMouse();
        Panel.SetZIndex(view, 2);
    }

    private void OnDragMove(object sender, MouseEventArgs e)
    {
        if (drag is null || e.LeftButton != MouseButtonState.Pressed) return;
        var point = e.GetPosition(canvas);
        var delta = point - drag.Start;
        if (!drag.Moved && delta.Length < 4) return;
        drag.Moved = true;
        if (drag.Group is not null) { MoveGroup(point); return; }
        double width = canvas.ActualWidth, height = canvas.ActualHeight, grid = GridStep;
        var others = OccupiedExcept(drag.Item.Id);
        var o = drag.Origin;
        if (!drag.Resize)
        {
            // Over another monitor? Dropping there moves the item to that monitor's deck.
            drag.Screen = canvas.PointToScreen(point);
            var over = hub.Displays.Count > 1 ? DisplayMap.At(drag.Screen.X, drag.Screen.Y, hub.Displays) : Display;
            if (over.Id != Display.Id)
            {
                if (drag.Target?.Id != over.Id) SetStatus($"Release to move {ItemName(drag.Item)} to {over.Name}");
                drag.Target = over;
                drag.View.Opacity = 0.45;
                return;
            }
            if (drag.Target is not null) { drag.Target = null; drag.View.Opacity = 1; }
            var p = DeckLayout.FindSpace(o.X + delta.X, o.Y + delta.Y, o.Width, o.Height, width, height, others, settings.KeepCenterClear, grid);
            if (p is not { } spot) return;
            drag.Current = o with { X = spot.X, Y = spot.Y };
        }
        else
        {
            var (minW, minH) = ItemKinds.MinimumSize(drag.Item.Kind);
            double w = Math.Max(minW, o.Width + delta.X), h = Math.Max(minH, o.Height + delta.Y);
            if (grid > 0) { w = DeckLayout.SnapSize(w, minW); h = DeckLayout.SnapSize(h, minH); }
            bool Fits(TileRect r) => DeckLayout.IsAllowed(r, width, height, settings.KeepCenterClear) && !others.Any(x => DeckLayout.Overlaps(r, x));
            var current = drag.Current;
            var candidate = o with { Width = w, Height = h };
            if (!Fits(candidate)) candidate = o with { Width = w, Height = current.Height };
            if (!Fits(candidate)) candidate = o with { Width = current.Width, Height = h };
            if (!Fits(candidate)) return;
            drag.Current = candidate;
            drag.View.Width = candidate.Width;
            drag.View.Height = candidate.Height;
        }
        rects[drag.Item.Id] = drag.Current;
        Canvas.SetLeft(drag.View, drag.Current.X);
        Canvas.SetTop(drag.View, drag.Current.Y);
    }

    private void OnDragUp(object sender, MouseButtonEventArgs e)
    {
        if (drag is { Moved: false, Resize: false } clicked) ClickedWithoutMoving(clicked.Item);
        EndDrag(drag?.Moved == true);
        e.Handled = true;
    }

    private void OnDragLost(object sender, MouseEventArgs e) { if (drag is not null && !drag.Handle.IsMouseCaptured) EndDrag(false); }

    private void EndDrag(bool commit)
    {
        var state = drag;
        if (state is null) return;
        drag = null;
        state.Handle.MouseMove -= OnDragMove;
        state.Handle.MouseLeftButtonUp -= OnDragUp;
        state.Handle.LostMouseCapture -= OnDragLost;
        state.Handle.ReleaseMouseCapture();
        Panel.SetZIndex(state.View, 0);
        state.View.Opacity = 1;
        if (state.Group is not null) { FinishGroupDrag(state, commit); return; }
        if (commit && state.Target is { } target)
        {
            rects[state.Item.Id] = state.Origin;
            // Center the item under the pointer on the other monitor.
            hub.MoveItem(state.Item, target, state.Screen, IsFront);
            return;
        }
        if (!commit)
        {
            rects[state.Item.Id] = state.Origin;
            Canvas.SetLeft(state.View, state.Origin.X);
            Canvas.SetTop(state.View, state.Origin.Y);
            state.View.Width = state.Origin.Width;
            state.View.Height = state.Origin.Height;
            return;
        }
        Store(state.Item, state.Current);
        SaveNow();
        // The other layer shows the same item; content also picks a layout for its size, so rebuild after a resize.
        Dispatcher.BeginInvoke(new Action(hub.RenderAll), DispatcherPriority.Background);
    }

    private void Store(LauncherItem item, TileRect rect)
    {
        if (canvas.ActualWidth <= 0 || canvas.ActualHeight <= 0) return;
        item.X = rect.X / canvas.ActualWidth;
        item.Y = rect.Y / canvas.ActualHeight;
        item.Width = rect.Width;
        item.Height = rect.Height;
    }

    // ---------- Menus ----------

    /// <summary>A context menu; its look comes from the app-wide styles in <see cref="Theme.InstallStyles"/>.</summary>
    internal static ContextMenu Menu() => new();

    internal static MenuItem AddMenuItem(ItemsControl menu, string title, Action action, string? glyph = null)
    {
        var item = new MenuItem { Header = title };
        if (glyph is not null) item.Icon = Theme.Icon(glyph, 14, Theme.Text2);
        item.Click += (_, _) => action();
        menu.Items.Add(item);
        return item;
    }

    private static MenuItem AddToggle(ItemsControl menu, string title, bool value, Action<bool> changed, string? glyph = null)
    {
        var item = new MenuItem { Header = title, IsCheckable = true, IsChecked = value };
        if (glyph is not null) item.Icon = Theme.Icon(glyph, 14, Theme.Text2);
        item.Click += (_, _) => changed(item.IsChecked);
        menu.Items.Add(item);
        return item;
    }

    private ContextMenu BuildItemMenu(LauncherItem item)
    {
        var menu = Menu();
        if (item.Kind == ItemKinds.Group) AddGroupEntries(menu, item); else AddKindEntries(menu, item);
        if (menu.Items.Count > 0) menu.Items.Add(new Separator());
        AddMenuItem(menu, item.Kind == ItemKinds.Group ? "Rename group…" : "Customize…", () => EditItem(item), Theme.Glyphs.Edit);
        if (item.Kind == ItemKinds.Group) AddUngroupEntry(menu, item);
        AddCommonEntries(menu, item);
        return menu;
    }

    /// <summary>The entries that belong to one kind of widget (above Customize…).</summary>
    private void AddKindEntries(ContextMenu menu, LauncherItem item)
    {
        switch (item.Kind)
        {
            case ItemKinds.Launcher:
                AddMenuItem(menu, "Start / open", () => Launch(item));
                var stop = AddMenuItem(menu, "Stop", () => Stop(item, false));
                var force = AddMenuItem(menu, "Force stop…", () => Stop(item, true));
                menu.Opened += (_, _) =>
                {
                    var tracked = IsTracked(launcher.GetStatus(item.Id));
                    stop.IsEnabled = force.IsEnabled = tracked;
                    force.Visibility = tracked ? Visibility.Visible : Visibility.Collapsed; // only offered while there is something to force-stop
                    stop.ToolTip = force.ToolTip = tracked ? "Controls the process started by Desktop Deck." : "Available for a running process started and tracked by Desktop Deck.";
                };
                break;
            case ItemKinds.Image:
                AddMenuItem(menu, "Change picture…", () => ChangePicture(item));
                break;
            case ItemKinds.Tasks:
                AddMenuItem(menu, "Add task…", () => AddTask(item));
                if (item.GoogleTasksSync) AddMenuItem(menu, "Sync with Google Tasks now", () => { hub.RequestTasksSync(item, true); SetStatus("Syncing with Google Tasks…"); }, Theme.Glyphs.Refresh);
                AddMenuItem(menu, "Clear completed", () => { Planner.ClearCompleted(item); Changed(item); });
                AddToggle(menu, "Start fresh each day", item.DailyReset, value => { item.DailyReset = value; item.LastDate = Planner.Key(Planner.Today()); Changed(item); });
                break;
            case ItemKinds.Habits:
                AddMenuItem(menu, "Add habit…", () => AddHabit(item));
                break;
            case ItemKinds.Counter:
                AddMenuItem(menu, "Reset to 0", () => { item.Count = 0; Changed(item); });
                break;
            case ItemKinds.Clock:
                AddToggle(menu, "Show seconds", item.ShowSeconds, value => { item.ShowSeconds = value; Changed(item); UpdateTicker(); });
                AddToggle(menu, "24-hour time", item.Use24Hour, value => { item.Use24Hour = value; Changed(item); });
                break;
            case ItemKinds.Timer:
                AddMenuItem(menu, "Reset session", () => { Planner.Reset(item.Pomodoro); Changed(item); UpdateTicker(); });
                break;
            case ItemKinds.Schedule:
                AddMenuItem(menu, "Add block…", () => EditBlock(item, null));
                if (item.ShowGoogle && CalendarData.GoogleCanEdit) AddMenuItem(menu, "Add Google event…", () => AddGoogleEvent(Planner.Today()));
                if (CalendarData.GoogleReady || settings.IcsCalendars.Count > 0) AddMenuItem(menu, "Refresh Google and iCal events", () => CalendarData.Refresh(), Theme.Glyphs.Refresh);
                AddMenuItem(menu, "Jump to today", () => { weekOffsets.Remove(item.Id); dayOffsets.Remove(item.Id); RebuildContent(item); });
                AddMenuItem(menu, item.View == "day" ? "Show the whole week" : "Show one day at a time", () => { item.View = item.View == "day" ? "week" : "day"; Changed(item); });
                break;
            case ItemKinds.Goals:
                AddMenuItem(menu, "Add goal…", () => AddGoal(item, WeekShown(item)));
                AddMenuItem(menu, "Carry over unfinished from last week", () => CarryOver(item));
                break;
        }
        if (Widgets.WidgetRegistry.Find(item.Kind) is { } plugin)
        {
            var submenus = new Dictionary<string, MenuItem>(StringComparer.Ordinal);
            foreach (var action in PluginActions(plugin, item))
            {
                // Actions that name the same group share one submenu, placed where the first of them would have been.
                if (string.IsNullOrEmpty(action.Group)) { AddMenuItem(menu, action.Title, action.Run, action.Glyph); continue; }
                if (!submenus.TryGetValue(action.Group, out var submenu))
                {
                    submenu = new MenuItem { Header = action.Group };
                    menu.Items.Add(submenu);
                    submenus[action.Group] = submenu;
                }
                AddMenuItem(submenu, action.Title, action.Run, action.Glyph);
            }
        }
    }

    /// <summary>
    /// The entries that are the same for every widget: Look, Place on screen, More (group and link), Hide and, in Edit layout, Remove.
    /// The simple view has Pin on top, Size, Move to…, Hide and Remove instead (see <see cref="AddSimpleEntries"/>).
    /// </summary>
    private void AddCommonEntries(ContextMenu menu, LauncherItem item)
    {
        if (!hub.Advanced) { AddSimpleEntries(menu, item); return; }
        if (item.Kind != ItemKinds.Image) AddLookItems(menu, item);
        AddPlaceMenu(menu, item);
        if (item.Kind != ItemKinds.Group)
        {
            var more = new MenuItem { Header = "More", Icon = Theme.Icon("", 14, Theme.Text2) };
            AddGroupMenu(more, item);
            AddLinkMenu(more, item);
            menu.Items.Add(more);
        }
        AddMenuItem(menu, "Hide", () => hub.HideItem(item), HideGlyph).ToolTip = "Take this widget off the screen. The control center → Widgets shows hidden widgets again.";
        if (IsEditing)
        {
            menu.Items.Add(new Separator());
            AddMenuItem(menu, "Remove", () => RemoveItem(item), Theme.Glyphs.Delete).Foreground = Theme.Danger;
        }
    }

    /// <summary>The simple view's entries after Customize…: Pin on top, Size ›, Move to another monitor, Hide and Remove (always there, with its confirmation).</summary>
    private void AddSimpleEntries(ContextMenu menu, LauncherItem item)
    {
        AddPinToggle(menu, item);
        // Locking is an advanced control, but a widget locked there must still be freeable here.
        if (item.Locked && !item.OnTop) AddLockEntry(menu, item);
        AddSizeMenu(menu, item);
        AddMoveEntries(menu, item, separator: false);
        AddMenuItem(menu, "Hide", () => hub.HideItem(item), HideGlyph).ToolTip = "Take this widget off the screen. The control center → Widgets shows hidden widgets again.";
        AddMenuItem(menu, "Remove…", () => RemoveItem(item), Theme.Glyphs.Delete).Foreground = Theme.Danger;
    }

    private void AddPinToggle(ItemsControl menu, LauncherItem item) =>
        AddToggle(menu, "Pin on top", item.OnTop, value => hub.SetOnTop(item, value), Theme.Glyphs.Pin).ToolTip = "Keep this widget above your other windows. It moves on its own, apart from the deck: drag its top edge, or hold Alt and drag.";

    /// <summary>"Move to <i>monitor</i>" for every other monitor (only with more than one).</summary>
    private void AddMoveEntries(ItemsControl menu, LauncherItem item, bool separator)
    {
        if (hub.Displays.Count <= 1) return;
        if (separator) menu.Items.Add(new Separator());
        foreach (var display in hub.Displays.Where(d => d.Id != hub.CurrentDisplayOf(item).Id))
            AddMenuItem(menu, "Move to " + display.Name, () => hub.MoveItem(item, display, null, IsFront), Theme.Glyphs.Monitor);
    }

    /// <summary>"Place on screen ›": pin on top (and, for a pinned widget, fade when idle and click-through), lock the position, size, and send the widget to another display.</summary>
    private void AddPlaceMenu(ContextMenu menu, LauncherItem item)
    {
        var place = new MenuItem { Header = "Place on screen", Icon = Theme.Icon(Theme.Glyphs.Monitor, 14, Theme.Text2) };
        AddPinToggle(place, item);
        if (item.OnTop) AddFloatOptions(place, item);
        if (!item.OnTop) AddLockEntry(place, item);
        AddSizeMenu(place, item);
        AddMoveEntries(place, item, separator: true);
        menu.Items.Add(place);
    }

    private void AddLockEntry(ItemsControl menu, LauncherItem item) =>
        AddMenuItem(menu, item.Locked ? "Unlock position" : "Lock position", () =>
        {
            item.Locked = !item.Locked;
            SaveNow();
            hub.RenderAll();
            SetStatus(item.Locked ? $"{ItemName(item)} is locked in place." : $"{ItemName(item)} can move again.");
        }, Theme.Glyphs.Lock);

    private void OpenRoomMenu()
    {
        var menu = Menu();
        foreach (var item in hiddenItems)
        {
            var entry = new MenuItem { Header = ItemName(item) };
            if (item.Kind == ItemKinds.Launcher) AddMenuItem(entry, "Open", () => Launch(item));
            AddMenuItem(entry, "Make default size", () => { item.Width = item.Height = 0; SaveNow(); hub.RenderAll(); });
            AddMenuItem(entry, "Customize…", () => EditItem(item));
            AddMenuItem(entry, "Remove", () => RemoveItem(item));
            menu.Items.Add(entry);
        }
        menu.PlacementTarget = roomButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    // ---------- Dialog helpers (tracked so app mode is not dismissed by our own dialogs) ----------

    /// <summary>The window that owns this deck's dialogs: the control center while it is open (so they open in front of it), otherwise this deck.</summary>
    internal Window DialogOwner => hub.DialogOwner is { IsLoaded: true } owner ? owner : this;

    private T Modal<T>(Func<T> show)
    {
        modal++;
        try { return show(); }
        finally { modal--; }
    }

    private string? PickFile(string title, string filter)
    {
        var picker = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        return Modal(() => picker.ShowDialog(DialogOwner)) == true ? picker.FileName : null;
    }

    private bool Confirm(string text, string title) =>
        Modal(() => MessageBox.Show(DialogOwner, text, title, MessageBoxButton.YesNo, MessageBoxImage.Question)) == MessageBoxResult.Yes;

    internal void Inform(string text, string title) =>
        Modal(() => MessageBox.Show(DialogOwner, text, title, MessageBoxButton.OK, MessageBoxImage.Information));

    internal string? Ask(string title, string label, string value = "", string action = "Save", bool multiline = false) =>
        Modal(() => InputDialog.Ask(DialogOwner, title, label, value, action, multiline, allowEmpty: multiline));

    // ---------- Adding, editing, removing ----------

    private static string ItemName(LauncherItem item) => string.IsNullOrWhiteSpace(item.Name) ? (item.Kind == ItemKinds.Image ? "Picture" : "Item") : item.Name;

    private void AddLauncher()
    {
        if (PickFile("Choose a launcher", "Apps, scripts and shortcuts|*.exe;*.ps1;*.cmd;*.bat;*.lnk;*.url;*.py;*.js;*.vbs|All files (*.*)|*.*") is not { } file) return;
        var item = new LauncherItem { Name = Path.GetFileNameWithoutExtension(file), Target = file, WorkingDirectory = Path.GetDirectoryName(file) ?? "" };
        var editor = new ItemEditor(DialogOwner, item, true, settings) { PresetsChanged = () => hub.Save() };
        if (Modal(editor.ShowDialog) == true && editor.Result is { } result) AddNew(result);
    }

    private void AddPicture()
    {
        if (PickFile("Choose a picture", ItemEditor.PictureFilter) is { } file)
            AddNew(new LauncherItem { Kind = ItemKinds.Image, Name = "", ImagePath = file });
    }

    /// <summary>Adds an item near the requested point (default: the middle of the screen).</summary>
    private bool AddNew(LauncherItem item, Point? near = null)
    {
        try { ImportMediaFor(item); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowError("Could not copy the picture", ex); return false; }
        if (string.IsNullOrEmpty(item.Display) && !Display.IsPrimary) item.Display = Display.Id;
        if (!Place(item, near))
        {
            Inform("There is no free room for this. Make something smaller or remove an item, then try again.", "Desktop Deck is full");
            return false;
        }
        settings.Items.Add(item);
        // Chosen in "Link to…": the new widget joins that widget's group and takes its information.
        if (pendingLink is { } linkTo) { WidgetLinks.Link(linkTo, item, settings.Items); pendingLink = null; }
        if (!hub.QuietAdd) hub.SetEditing(true);
        hub.RenderAll();
        SaveNow();
        SetStatus(hub.QuietAdd ? $"Added {ItemName(item)}." : $"Added {ItemName(item)}. Drag to move, use the corner to resize.");
        return true;
    }

    /// <summary>Finds room for a new item and stores its position. False if the deck is full.</summary>
    private bool Place(LauncherItem item, Point? near = null, List<TileRect>? occupied = null)
    {
        double width = canvas.ActualWidth, height = canvas.ActualHeight;
        var (w, h) = SizeOf(item);
        var target = near ?? new Point(width / 2 - w / 2, height / 2 - h / 2);
        occupied ??= OccupiedForNew();
        var space = DeckLayout.FindSpace(target.X, target.Y, w, h, width, height, occupied, settings.KeepCenterClear, settings.SnapToGrid ? DeckLayout.Grid : 0);
        if (space is not { } p) return false;
        var rect = new TileRect(p.X, p.Y, w, h);
        Store(item, rect);
        item.Width = item.Height = 0;
        occupied.Add(rect);
        return true;
    }

    private void EditItem(LauncherItem item) => EditItem(item, null);

    /// <summary>Customize from the control center: the editor is owned by <paramref name="owner"/>.</summary>
    internal void CustomizeFrom(Window owner, LauncherItem item) => EditItem(item, owner);

    private void EditItem(LauncherItem item, Window? owner)
    {
        EndDrag(false);
        if (item.Kind == ItemKinds.Group) { RenameGroup(item); return; }
        var editor = new ItemEditor(owner ?? DialogOwner, item, false, settings, hub.LoadTaskListsAsync, hub.PreviewItem) { PresetsChanged = () => hub.Save() };
        bool? saved;
        // While the window is open the deck shows its unsaved changes; whichever way it closes, the real widget is drawn again.
        try { saved = Modal(editor.ShowDialog); }
        finally { hub.ClearPreview(); }
        if (saved != true || editor.Result is not { } result) return;
        if (result.Kind == ItemKinds.Tasks)
        {
            // A sync may have linked tasks while Customize was open: keep the live list, not the stale copy the editor started from.
            GoogleTasksSync.CarryOverLiveState(item, result);
            ApplyTasksSyncChoice(item, result);
        }
        try { ImportMediaFor(result); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowError("Could not copy the picture", ex); return; }
        var index = settings.Items.IndexOf(item);
        if (index < 0) return;
        settings.Items[index] = result;
        hub.SyncLinked(result);
        CleanupMedia(item);
        hub.RenderAll();
        SaveNow();
        SetStatus($"Saved {ItemName(result)}.");
    }

    /// <summary>Customize changed the Google Tasks settings: a new list (or sync turned on) starts from a fresh listing, and tasks linked to the old list are unlinked.</summary>
    private void ApplyTasksSyncChoice(LauncherItem before, LauncherItem after)
    {
        if (!after.GoogleTasksSync) return;
        bool listChanged = !string.Equals(before.GoogleTaskListId, after.GoogleTaskListId, StringComparison.Ordinal);
        if (!listChanged && before.GoogleTasksSync) return;
        GoogleTasksSync.SwitchList(after, after.GoogleTaskListId);
        hub.Tasks.Forget(after.Id);
    }

    private void ChangePicture(LauncherItem item)
    {
        if (PickFile("Choose a picture", ItemEditor.PictureFilter) is not { } file) return;
        var updated = item.Copy();
        updated.ImagePath = file;
        try { ImportMediaFor(updated); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowError("Could not copy the picture", ex); return; }
        var index = settings.Items.IndexOf(item);
        if (index < 0) return;
        settings.Items[index] = updated;
        CleanupMedia(item);
        hub.RenderAll();
        SaveNow();
    }

    private void RemoveItem(LauncherItem item) => RemoveItem(item, null);

    /// <summary>Remove from the control center. A group asks (its widgets go back to the desktop); anything else goes to the recycle bin. False when the user said no.</summary>
    internal bool RemoveFrom(Window owner, LauncherItem item)
    {
        if (item.Kind == ItemKinds.Group
            && MessageBox.Show(owner, $"Remove the group {ItemName(item)}? The widgets in it go back to the desktop; only the group card is removed.", "Remove widget", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return false;
        return RemoveItem(item, owner);
    }

    /// <summary>
    /// Moves a widget to the recycle bin (restorable from History for 30 days, and the toast offers Undo), out of every layout. Only a widget holding a lot
    /// of data asks first. False when the user said no.
    /// </summary>
    private bool RemoveItem(LauncherItem item, Window? owner)
    {
        EndDrag(false);
        if (item.Kind == ItemKinds.Group)
        {
            if (owner is null && !Confirm($"Remove the group {ItemName(item)}? Its widgets go back to the desktop.", "Remove group")) return false;
            hub.DissolveGroup(item);
            return true;
        }
        if (TrashBin.IsBig(item))
        {
            int days = settings.History.DaysFor(HistoryKeys.Trash);
            string text = $"Remove {ItemName(item)}? It holds a lot. It goes to the recycle bin (Control center → History), where you can restore it {(days == 0 ? "until you empty the bin" : $"for {days} days")}.";
            bool yes = owner is null ? Confirm(text, "Remove") : MessageBox.Show(owner, text, "Remove widget", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
            if (!yes) return false;
        }
        hub.TrashItem(item);
        CleanupMedia(item);
        hub.RenderAll();
        SaveNow();
        return true;
    }

    internal void ImportDesktop()
    {
        try
        {
            int added = 0, full = 0;
            var occupied = OccupiedForNew();
            foreach (var item in DesktopImporter.Discover())
            {
                if (settings.Items.Any(x => string.Equals(x.Target, item.Target, StringComparison.OrdinalIgnoreCase) && x.Arguments == item.Arguments)) continue;
                if (!Place(item, new Point(24, 96), occupied)) { full++; continue; }
                settings.Items.Add(item);
                added++;
            }
            hub.RenderAll();
            SaveNow();
            SetStatus(added > 0 ? $"Imported {added} launcher(s). Nothing was launched." : "No new desktop shortcuts or scripts to import.");
            if (full > 0) Inform($"Imported {added} launcher(s). {full} more could not fit. Remove or shrink items to make room, then import again.", "Import complete");
        }
        catch (Exception ex) { ShowError("Could not import desktop launchers", ex); }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        var point = e.GetPosition(canvas);
        foreach (var file in files.Where(File.Exists).Take(12))
        {
            var item = ImageCache.PictureExtensions.Contains(Path.GetExtension(file))
                ? new LauncherItem { Kind = ItemKinds.Image, Name = "", ImagePath = file }
                : new LauncherItem { Name = Path.GetFileNameWithoutExtension(file), Target = file, WorkingDirectory = Path.GetDirectoryName(file) ?? "" };
            if (!AddNew(item, point)) break;
            point.Offset(24, 24);
        }
        e.Handled = true;
    }

    // ---------- Media files ----------

    private bool IsManaged(string path) =>
        !string.IsNullOrWhiteSpace(path) && Path.GetFullPath(path).StartsWith(Path.GetFullPath(store.MediaDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>Copies newly picked icons/pictures into the media folder so the originals can move freely.</summary>
    private void ImportMediaFor(LauncherItem item)
    {
        item.IconPath = Import(item.IconPath);
        item.ImagePath = Import(item.ImagePath);

        string Import(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || IsManaged(path) || !File.Exists(path)) return path;
            Directory.CreateDirectory(store.MediaDirectory);
            string destination = Path.Combine(store.MediaDirectory, Guid.NewGuid().ToString("N") + Path.GetExtension(path).ToLowerInvariant());
            File.Copy(path, destination);
            return destination;
        }
    }

    /// <summary>Deletes media that no remaining item references.</summary>
    private void CleanupMedia(LauncherItem removed)
    {
        foreach (var path in new[] { removed.IconPath, removed.ImagePath })
        {
            if (!IsManaged(path)) continue;
            if (settings.Items.Any(i => string.Equals(i.IconPath, path, StringComparison.OrdinalIgnoreCase) || string.Equals(i.ImagePath, path, StringComparison.OrdinalIgnoreCase))) continue;
            // The recycle bin, undo and the daily snapshots may still need it; the media sweep removes it once they don't.
            if (hub.MediaHeld(path)) continue;
            try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    // ---------- Launching ----------

    private static bool IsTracked(string state) =>
        state.StartsWith("Running", StringComparison.OrdinalIgnoreCase) || state.StartsWith("Close requested", StringComparison.OrdinalIgnoreCase) || state.StartsWith("Force stop requested", StringComparison.OrdinalIgnoreCase);

    private void Launch(LauncherItem item)
    {
        try { launcher.Launch(item); SetStatus($"Opened {item.Name}."); }
        catch (Exception ex) { ShowError($"Could not open {item.Name}", ex); }
        RefreshStatuses();
    }

    private void Stop(LauncherItem item, bool force)
    {
        if (force && !Confirm($"Force stop {item.Name}? Any unsaved work in its managed process may be lost.", "Force stop")) return;
        try { launcher.Stop(item.Id, force); SetStatus(force ? $"Force stop requested for {item.Name}." : $"Stop requested for {item.Name}."); }
        catch (Exception ex) { ShowError($"Could not stop {item.Name}", ex); }
        RefreshStatuses();
    }

    private void LauncherStatusChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(RefreshStatuses));
    }

    private void RefreshStatuses()
    {
        foreach (var update in statusUpdaters.Values) update();
    }

    // ---------- Time: one shared ticker, day rollover ----------

    /// <summary>Runs the ticker only when something on screen needs it, aligned to the next second or minute.</summary>
    private void UpdateTicker()
    {
        ticker.Stop();
        if (tickers.Count == 0) return;
        var now = DateTime.Now;
        bool seconds = tickers.Values.Any(t => t.Seconds());
        ticker.Interval = seconds
            ? TimeSpan.FromMilliseconds(1000 - now.Millisecond + 5)
            : TimeSpan.FromMilliseconds((60 - now.Second) * 1000 - now.Millisecond + 20);
        ticker.Start();
    }

    private long lastTickMinute = -1;

    private void OnTick()
    {
        var now = DateTimeOffset.Now;
        // The shared ticker runs every second while any widget needs seconds; widgets that only need minutes
        // are still called once a minute, so they don't redraw every second.
        long minute = now.ToUnixTimeSeconds() / 60;
        bool newMinute = minute != lastTickMinute;
        lastTickMinute = minute;
        foreach (var tick in tickers.Values.ToList())
            if (newMinute || tick.Seconds()) tick.Tick(now);
        UpdateTicker();
    }

    /// <summary>Re-renders date-dependent widgets. The hub calls this at local midnight and after resume.</summary>
    internal void RefreshDay()
    {
        if (drag is null) RenderSurface();
    }

    // ---------- Saving ----------

    internal void RememberAll()
    {
        foreach (var item in settings.Items)
            if (rects.TryGetValue(item.Id, out var rect)) { item.X = rect.X / canvas.ActualWidth; item.Y = rect.Y / canvas.ActualHeight; }
    }

    /// <summary>Saves tracker clicks shortly after the last one rather than on every click.</summary>
    private void Changed(LauncherItem item, bool rerender = true)
    {
        saveTimer.Stop();
        saveTimer.Start();
        if (rerender) RebuildContent(item);
        hub.SyncLinked(item);
        hub.ItemChanged(item, this);
    }

    /// <summary>Another surface changed this item's data: redraw this surface's copy.</summary>
    internal void RebuildItem(LauncherItem item) { if (IsLoaded && !closingSurface) RebuildContent(item); }

    private void RebuildContent(LauncherItem item)
    {
        // A widget inside a group is drawn by the group's card: redraw that.
        if (item.GroupId.Length > 0 && WidgetOrganizer.GroupOf(settings, item) is { } owner) item = owner;
        // The deck that builds the floating widgets redraws their windows too.
        if (item.OnTop && this == hub.Floats.Builder) hub.Floats.Rebuild(item);
        if (!views.TryGetValue(item.Id, out var view) || !rects.TryGetValue(item.Id, out var rect)) { UpdateTicker(); return; }
        view.Children.RemoveAt(0);
        view.Children.Insert(0, BuildContent(item, rect.Width, rect.Height));
        view.ContextMenu = BuildItemMenu(item);
        UpdateTicker();
    }

    // ---------- Keep on top: widgets floating in their own windows (see FloatLayer) ----------

    private const string FloatKeyPrefix = "float:";

    /// <summary>While a float window builds its content, clocks and status updates register under this key so they survive this deck's redraws.</summary>
    private string? buildKey;
    private string KeyFor(LauncherItem item) => buildKey ?? item.Id;

    /// <summary>Builds widget content for a float window (same look and behavior as on the deck).</summary>
    internal FrameworkElement BuildFloatContent(LauncherItem item, double width, double height)
    {
        buildKey = FloatKeyPrefix + item.Id;
        try { return BuildContent(item, width, height); }
        finally { buildKey = null; UpdateTicker(); }
    }

    internal ContextMenu FloatMenu(LauncherItem item) => BuildItemMenu(item);

    /// <summary>A float went away: stop its clocks and status updates.</summary>
    internal void ForgetFloat(string id)
    {
        tickers.Remove(FloatKeyPrefix + id);
        statusUpdaters.Remove(FloatKeyPrefix + id);
        UpdateTicker();
    }

    /// <summary>Where the item sits on this deck as fractions of its size, and its size (DIP), or null when it isn't laid out here.</summary>
    internal (double X, double Y, double Width, double Height)? DeckPlacement(LauncherItem item)
    {
        if (!rects.TryGetValue(item.Id, out var rect) || canvas.ActualWidth <= 0 || canvas.ActualHeight <= 0) return null;
        return (rect.X / canvas.ActualWidth, rect.Y / canvas.ActualHeight, rect.Width, rect.Height);
    }

    /// <summary>Where an item is on screen (physical pixels) on this deck, or null when it isn't laid out here.</summary>
    internal PixelRect? ScreenRectOf(string id)
    {
        if (!rects.TryGetValue(id, out var rect) || PresentationSource.FromVisual(canvas) is null) return null;
        var a = canvas.PointToScreen(new Point(rect.X, rect.Y));
        var b = canvas.PointToScreen(new Point(rect.Right, rect.Bottom));
        return new PixelRect((int)Math.Round(a.X), (int)Math.Round(a.Y), Math.Max(1, (int)Math.Round(b.X - a.X)), Math.Max(1, (int)Math.Round(b.Y - a.Y)));
    }

    /// <summary>Stores where this item is drawn now (after it was put back near a spot and found room), so it stays there.</summary>
    internal void RememberItem(LauncherItem item)
    {
        if (!rects.TryGetValue(item.Id, out var rect) || canvas.ActualWidth <= 0 || canvas.ActualHeight <= 0) return;
        item.X = rect.X / canvas.ActualWidth;
        item.Y = rect.Y / canvas.ActualHeight;
    }

    /// <summary>Places an item near a physical-pixel point on this surface (default: the middle). False if there is no room.</summary>
    internal bool PlaceFromScreen(LauncherItem item, Point? screen)
    {
        Point? near = null;
        var (w, h) = SizeOf(item);
        if (screen is { } point && PresentationSource.FromVisual(canvas) is not null)
        {
            var local = canvas.PointFromScreen(point);
            near = new Point(local.X - w / 2, local.Y - h / 2);
        }
        // Keep the item's size; Place resets it to the default.
        double width = item.Width, height = item.Height;
        if (!Place(item, near)) return false;
        item.Width = width;
        item.Height = height;
        return true;
    }

    public void FlushSave()
    {
        if (!saveTimer.IsEnabled) return;
        SaveNow();
    }

    private bool SaveNow()
    {
        saveTimer.Stop();
        try { store.Save(settings); return true; }
        catch (Exception ex) { ShowError("Could not save the layout", ex); return false; }
    }

    /// <summary>A short message at the bottom-left that fades away by itself.</summary>
    internal void SetStatus(string text) => SetStatus(text, null, null);

    private void ShowError(string title, Exception ex)
    {
        Inform(ex.Message, title);
    }

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
}
