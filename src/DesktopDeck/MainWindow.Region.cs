using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopDeck.Layout;
using DesktopDeck.Models;

namespace DesktopDeck;

// The deck window's region (see DeckRegion): only the visible widgets and buttons exist; the rest of the monitor belongs to the windows beneath.
public sealed partial class MainWindow
{
    private bool regionPending, regionActive;
    /// <summary>The shapes last applied (window pixels), for the host test.</summary>
    internal IReadOnlyList<RegionShape> RegionShapes { get; private set; } = [];
    /// <summary>True while this deck's window is cut down to its widgets.</summary>
    internal bool HasRegion => regionActive;

    /// <summary>Hooks the changes that can alter what the deck shows (buttons, toasts, the empty hint, moves, resizes, DPI); each re-applies the region, debounced.</summary>
    private void InitRegion()
    {
        if (IsFront) return;
        controls.SizeChanged += (_, _) => ScheduleRegion();
        controls.IsVisibleChanged += (_, _) => ScheduleRegion();
        toast.SizeChanged += (_, _) => ScheduleRegion();
        toast.IsVisibleChanged += (_, _) => ScheduleRegion();
        emptyHint.IsVisibleChanged += (_, _) => ScheduleRegion();
        settingsButton.IsVisibleChanged += (_, _) => ScheduleRegion();
        // Not SizeChanged / LocationChanged of the window: SetWindowRgn itself raises them (WM_WINDOWPOSCHANGED), which would loop. The deck is moved and
        // resized only by the hub (display changes), which re-attaches it (NativeDesktopHost.Attached) and redraws it (canvas size changes).
        DpiChanged += (_, _) => ScheduleRegion();
    }

    /// <summary>Applies the region after the layout that is about to happen; bursts of changes apply once.</summary>
    internal void ScheduleRegion()
    {
        if (regionPending || closingSurface || IsFront) return;
        // Nothing to do (or undo) while the icons are off and no region was ever set.
        if (!regionActive && !hub.ShowDesktopIcons) return;
        regionPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => { regionPending = false; ApplyRegionNow(); }));
    }

    /// <summary>The visible pieces of this deck as pixel shapes in window coordinates.</summary>
    internal List<RegionShape> CollectRegionShapes()
    {
        double windowScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var found = new List<(double, double, double, double, double)>();
        void Add(FrameworkElement? element, double radius)
        {
            if (element is null || !element.IsVisible || element.ActualWidth < 1 || element.ActualHeight < 1) return;
            var box = element.TransformToVisual(this).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            found.Add((box.X, box.Y, box.Width, box.Height, radius));
        }
        foreach (var (id, view) in views)
        {
            var item = settings.Items.FirstOrDefault(i => i.Id == id);
            // The card's radius is in canvas units; shapes are in window units, so scale the radius to window units too (canvas unit x factor).
            double factor = Display.Scale / windowScale;
            Add(view, item is null ? 0 : Appearance.Radius(settings.Appearance, item) * factor);
        }
        double chromeFactor = Display.Scale / windowScale;
        Add(settingsButton, 18 * chromeFactor);
        Add(controls, 10 * chromeFactor);
        Add(toast, 10 * chromeFactor);
        Add(emptyHint, 0);
        return RegionMath.Build(found, windowScale, windowScale, (int)Math.Ceiling(ActualWidth * windowScale), (int)Math.Ceiling(ActualHeight * windowScale));
    }

    /// <summary>Sets or removes the region now: set while desktop icons are shown, removed while editing the layout, on a front layer or when the deck is not attached to the desktop.</summary>
    internal void ApplyRegionNow()
    {
        if (closingSurface || IsFront || !IsLoaded) return;
        var host = hub.HostFor(Display.Id);
        nint hwnd = Hwnd;
        if (!hub.ShowDesktopIcons || IsEditing || host is null || !host.IsAttached || hwnd == 0)
        {
            RegionShapes = [];
            if (regionActive) { DeckRegion.Clear(hwnd); regionActive = false; }
            return;
        }
        UpdateLayout();
        var shapes = CollectRegionShapes();
        // Setting the same region again would only repaint (and SetWindowRgn raises window-position messages).
        if (regionActive && shapes.SequenceEqual(RegionShapes)) return;
        RegionShapes = shapes;
        regionActive = DeckRegion.Apply(hwnd, shapes);
    }
}
