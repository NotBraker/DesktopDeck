using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Shapes = System.Windows.Shapes;
using DesktopDeck.Layout;
using DesktopDeck.Models;

namespace DesktopDeck;

/// <summary>
/// A floating widget: a small transparent window above other apps. It never takes focus from the app you're using and isn't listed in
/// Alt+Tab. The deck surface builds its content. Move it with the slim grip that fades in near its top edge, or hold Alt and drag anywhere
/// on it; resize with the corner grip that shows on hover. Where it ends up is saved per widget by <see cref="FloatLayer.Commit"/>.
/// </summary>
internal sealed class FloatWindow : Window
{
    private enum Gesture { None, Move, Resize }

    private readonly FloatLayer layer;
    private readonly Grid root = new();
    private readonly Grid holder = new();
    private readonly Border bar;
    private readonly Border corner;
    private readonly DispatcherTimer settle = new() { Interval = TimeSpan.FromMilliseconds(90) };
    /// <summary>
    /// While moving or resizing, the pointer is polled instead of trusting mouse messages: this window never activates, so Windows can stop sending
    /// it messages when the pointer outruns it or is released somewhere else. Polling follows the pointer anywhere and notices the release.
    /// </summary>
    private readonly DispatcherTimer follow = new() { Interval = TimeSpan.FromMilliseconds(10) };
    private LauncherItem item;
    private DisplayInfo? display;
    private nint handle;
    private bool shown, closed, barShown, cornerShown;
    private double contentWidth, contentHeight;
    private Gesture gesture;
    /// <summary>The last moment the widget was in use (pointer over it, a menu open, being dragged); idle fading counts from here.</summary>
    private DateTime lastUsed = DateTime.UtcNow;
    /// <summary>False while clicks pass through to the window underneath (click-through, and not switched back on with Ctrl).</summary>
    private bool takingClicks = true;
    /// <summary>The opacity (0–1) the widget is fading to or resting at.</summary>
    private double fadeTarget = 1;
    private Border? hint;
    private (int X, int Y) startCursor;
    private PixelRect startRect;
    public PixelRect Bounds { get; private set; }
    public string ItemId => item.Id;
    public LauncherItem Item => item;
    public nint Handle => handle;
    /// <summary>The size (DIP) the content was built for.</summary>
    public (double Width, double Height) ContentSize => (contentWidth, contentHeight);
    public bool IsGesturing => gesture != Gesture.None;
    /// <summary>The move grip is visible (host test).</summary>
    public bool MoveGripShown => barShown;
    /// <summary>The widget fades when idle or lets clicks through, so the layer has to watch the pointer for it.</summary>
    public bool NeedsPolling => item.FloatIdleFade || item.FloatClickThrough;
    /// <summary>Clicks pass through this window right now (it has the transparent window style; host test).</summary>
    public bool PassesClicks => handle != 0 && (NativeWindow.ExStyle(handle) & NativeWindow.ExTransparent) != 0;
    /// <summary>The opacity (0–1) the widget is fading to or resting at (host test).</summary>
    public double FadeTarget => fadeTarget;

    public FloatWindow(FloatLayer layer, LauncherItem item)
    {
        this.layer = layer;
        this.item = item;
        Title = string.IsNullOrWhiteSpace(item.Name) ? "Desktop Deck widget" : item.Name;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        FontFamily = Theme.UiFont;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        var dots = Theme.Label("⠿⠿⠿⠿⠿", 8, Theme.Muted);
        dots.HorizontalAlignment = HorizontalAlignment.Center;
        dots.VerticalAlignment = VerticalAlignment.Center;
        dots.IsHitTestVisible = false;
        bar = new Border
        {
            Height = 10, Margin = new Thickness(20, 3, 20, 0), CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Top,
            Background = Theme.Grip, BorderBrush = Theme.LineStrong, BorderThickness = new Thickness(1),
            Cursor = Cursors.SizeAll, Opacity = 0, IsHitTestVisible = false, Child = dots,
            ToolTip = "Drag to move. Or hold Alt and drag anywhere on the widget.",
        };
        corner = new Border
        {
            Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 3, 3),
            Background = Brushes.Transparent, Cursor = Cursors.SizeNWSE, Opacity = 0, IsHitTestVisible = false, ToolTip = "Drag to resize",
            Child = new Shapes.Path { Data = Geometry.Parse("M 15,5 L 5,15 M 15,10 L 10,15"), Stroke = Theme.Text2, StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round },
        };
        root.Children.Add(holder);
        root.Children.Add(bar);
        root.Children.Add(corner);
        Content = root;
        PreviewKeyDown += (_, e) => layer.TryHistoryKey(e);
        Panel.SetZIndex(bar, 5);
        Panel.SetZIndex(corner, 5);

        bar.MouseLeftButtonDown += (_, e) => Begin(Gesture.Move, e);
        corner.MouseLeftButtonDown += (_, e) => Begin(Gesture.Resize, e);
        root.PreviewMouseMove += (_, e) => { if (gesture == Gesture.None) Hover(e.GetPosition(root)); };
        root.MouseEnter += (_, _) =>
        {
            if (gesture == Gesture.None) Show(corner, ref cornerShown, true);
            if (item.FloatIdleFade) Poll(NativeWindow.Cursor(), CtrlDown(), DateTime.UtcNow);
        };
        root.MouseLeave += (_, _) => { if (gesture == Gesture.None) { Show(bar, ref barShown, false); Show(corner, ref cornerShown, false); } };
        root.MouseMove += OnGestureMove;
        root.MouseLeftButtonUp += (_, e) => { if (gesture != Gesture.None) { ApplyCursor(); Finish(); e.Handled = true; } };
        root.LostMouseCapture += (_, _) => { if (gesture != Gesture.None && !root.IsMouseCaptured) Finish(); };
        settle.Tick += (_, _) => { settle.Stop(); Rebuild(); };
        follow.Tick += (_, _) =>
        {
            if (gesture == Gesture.None) { follow.Stop(); return; }
            ApplyCursor();
            if (!ButtonDown()) Finish();
        };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Reapply));
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            NativeWindow.AddExStyle(handle, NativeWindow.ExToolWindow | NativeWindow.ExNoActivate);
            NativeWindow.Place(handle, Bounds, true);
        };
    }

    /// <summary>Moves and redraws the widget for its saved spot on a monitor (physical pixels) and its size (DIP).</summary>
    public void Update(LauncherItem current, DisplayInfo onDisplay, PixelRect bounds, double width, double height, bool rebuild)
    {
        if (closed) return;
        bool sizeChanged = Math.Abs(width - contentWidth) > 0.01 || Math.Abs(height - contentHeight) > 0.01;
        item = current;
        display = onDisplay;
        // The user is dragging it right now: let go first, the saved spot follows. A drag whose release was never seen ends here.
        if (gesture != Gesture.None)
        {
            if (ButtonDown()) return;
            Finish();
        }
        Bounds = bounds;
        contentWidth = width;
        contentHeight = height;
        Width = width;
        Height = height;
        if (rebuild || sizeChanged || holder.Children.Count == 0) Rebuild();
        if (!shown) { shown = true; Show(); }
        Reapply();
        // The options may have changed (menu, Customize, undo): follow them now rather than at the next tick.
        Poll(NativeWindow.Cursor(), CtrlDown(), DateTime.UtcNow);
    }

    // ---------- Fading when idle and letting clicks through ----------

    /// <summary>
    /// Called by the floating layer a few times a second (and when the options change). A window that lets clicks through gets no mouse messages,
    /// so the pointer and Ctrl are read directly: <paramref name="cursor"/> is in physical pixels like <see cref="Bounds"/>.
    /// </summary>
    public void Poll((int X, int Y) cursor, bool ctrl, DateTime now)
    {
        if (closed || handle == 0) return;
        bool inside = Bounds.Contains(cursor.X, cursor.Y);
        bool busy = gesture != Gesture.None || ContextMenu is { IsOpen: true };
        SetTakingClicks(FloatBehavior.TakesClicks(item.FloatClickThrough, takingClicks, inside, ctrl, busy));
        // The pointer resting on a widget that lets clicks through is not "using" it: the work underneath is.
        bool inUse = busy || (inside && takingClicks);
        if (inUse) lastUsed = now;
        SetFade(FloatBehavior.TargetOpacity(item.FloatIdleFade, item.FloatIdleOpacity, inUse, (now - lastUsed).TotalSeconds));
    }

    private void SetTakingClicks(bool takes)
    {
        if (takes == takingClicks) return;
        takingClicks = takes;
        if (takes) { NativeWindow.RemoveExStyle(handle, NativeWindow.ExTransparent); return; }
        NativeWindow.AddExStyle(handle, NativeWindow.ExTransparent);
        if (layer.ClaimClickThroughHint()) ShowHint();
    }

    private void SetFade(double target)
    {
        if (Math.Abs(target - fadeTarget) < 0.001) return;
        fadeTarget = target;
        // Quick back to full, slow and gentle on the way down.
        root.BeginAnimation(OpacityProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(target >= 1 ? 120 : 450)) { EasingFunction = new QuadraticEase() });
    }

    /// <summary>A small note, once ever, that fades away by itself: how to use a widget that lets clicks through.</summary>
    private void ShowHint()
    {
        if (hint is null)
        {
            var text = Theme.Label("Hold Ctrl to use this widget", 11.5, Theme.Text);
            text.Margin = new Thickness(10, 4, 10, 4);
            hint = new Border
            {
                Background = Theme.Grip, BorderBrush = Theme.LineStrong, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8, 0, 8, 8),
                Opacity = 0, IsHitTestVisible = false, Child = text,
            };
            Panel.SetZIndex(hint, 6);
            root.Children.Add(hint);
        }
        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(250))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(3500))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(4200))));
        hint.BeginAnimation(OpacityProperty, fade);
    }

    private static bool CtrlDown() => (GetAsyncKeyState(0x11) & 0x8000) != 0;

    /// <summary>Puts the window on its expected pixels. A window that crosses to a monitor with another scale may resize itself, so check once more when idle.</summary>
    private void Reapply()
    {
        if (closed || handle == 0 || gesture != Gesture.None) return;
        NativeWindow.Place(handle, Bounds, true);
        if (NativeWindow.Rect(handle) != Bounds) Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (!closed && gesture == Gesture.None && NativeWindow.Rect(handle) != Bounds) NativeWindow.Place(handle, Bounds, true);
        }));
    }

    public void Rebuild()
    {
        if (closed) return;
        Title = string.IsNullOrWhiteSpace(item.Name) ? "Desktop Deck widget" : item.Name;
        if (layer.Builder is not { } builder) return;
        holder.Children.Clear();
        holder.Children.Add(builder.BuildFloatContent(item, contentWidth, contentHeight));
        ContextMenu = builder.FloatMenu(item);
    }

    public void CloseFloat()
    {
        if (closed) return;
        closed = true;
        settle.Stop();
        follow.Stop();
        if (root.IsMouseCaptured) root.ReleaseMouseCapture();
        gesture = Gesture.None;
        Close();
    }

    // ---------- The grips ----------

    private void Hover(Point at)
    {
        Show(bar, ref barShown, at.Y < 26);
        Show(corner, ref cornerShown, true);
    }

    private static void Show(UIElement element, ref bool state, bool on)
    {
        if (state == on) return;
        state = on;
        element.IsHitTestVisible = on;
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(130)));
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        // Alt + drag anywhere moves the widget (the widget itself gets no click).
        if (gesture == Gesture.None && (GetAsyncKeyState(0x12) & 0x8000) != 0) Begin(Gesture.Move, e);
    }

    // ---------- Moving and resizing: manual, because DragMove can't work on a window that never activates ----------

    private void Begin(Gesture kind, MouseButtonEventArgs e)
    {
        if (handle == 0 || closed) return;
        startCursor = NativeWindow.Cursor();
        startRect = NativeWindow.Rect(handle);
        gesture = kind;
        settle.Stop();
        root.CaptureMouse();
        follow.Start();
        e.Handled = true;
    }

    private void OnGestureMove(object sender, MouseEventArgs e)
    {
        if (gesture == Gesture.None) return;
        if (e.LeftButton != MouseButtonState.Pressed) { Finish(); return; }
        ApplyCursor();
    }

    /// <summary>Puts the window where the pointer says it should be (the pointer's latest position, even if its last mouse messages are still queued).</summary>
    private void ApplyCursor()
    {
        var (cx, cy) = NativeWindow.Cursor();
        int dx = cx - startCursor.X, dy = cy - startCursor.Y;
        if (gesture == Gesture.Move) MoveTo(startRect.X + dx, startRect.Y + dy);
        else if (gesture == Gesture.Resize) ResizeTo(startRect.Width + dx, startRect.Height + dy);
    }

    /// <summary>Moves the window to a top-left corner (physical pixels), snapping to screen edges, as a drag does.</summary>
    public void MoveTo(int x, int y)
    {
        var rect = layer.Snap(new PixelRect(x, y, Bounds.Width, Bounds.Height), inside: false);
        Bounds = rect;
        NativeWindow.Place(handle, rect, true);
    }

    /// <summary>Resizes the window (physical pixels), never below the widget's minimum size; the content is rebuilt once the drag settles.</summary>
    public void ResizeTo(int widthPx, int heightPx)
    {
        double scale = display?.Scale ?? VisualTreeHelper.GetDpi(this).DpiScaleX;
        var (minW, minH) = ItemKinds.MinimumSize(item.Kind);
        int w = Math.Max((int)Math.Ceiling(minW * scale), widthPx), h = Math.Max((int)Math.Ceiling(minH * scale), heightPx);
        var rect = new PixelRect(Bounds.X, Bounds.Y, w, h);
        Bounds = rect;
        contentWidth = w / scale;
        contentHeight = h / scale;
        NativeWindow.Place(handle, rect, true);
        settle.Stop();
        settle.Start();
    }

    /// <summary>The mouse was released: save where the widget ended up (also what the host test calls after <see cref="MoveTo"/>).</summary>
    public void Finish()
    {
        if (gesture == Gesture.None && !IsLoaded) return;
        var kind = gesture;
        gesture = Gesture.None;
        follow.Stop();
        if (root.IsMouseCaptured) root.ReleaseMouseCapture();
        settle.Stop();
        if (kind == Gesture.Resize) Rebuild();
        layer.Commit(item, Bounds, contentWidth, contentHeight);
        if (!root.IsMouseOver) { Show(bar, ref barShown, false); Show(corner, ref cornerShown, false); }
    }

    /// <summary>The primary mouse button is held (the left one, or the right one when the buttons are swapped).</summary>
    private static bool ButtonDown() => (GetAsyncKeyState(SystemParameters.SwapButtons ? 0x02 : 0x01) & 0x8000) != 0;

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
}
