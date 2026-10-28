using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DesktopDeck.Layout;

namespace DesktopDeck;

// Settings, Display (Grid and placement): the grid the widgets snap to and the space between them. Both apply live while a slider moves.
internal sealed partial class ControlCenter
{
    private void BuildGridControls(StackPanel section)
    {
        var settings = hub.Settings;
        section.Children.Add(PanelKit.Option("Snap to grid", "Line positions and sizes up on the grid. Hold Alt while dragging to place one widget freely.", settings.SnapToGrid, on => Quietly(() => hub.SetSnap(on))));
        section.Children.Add(PanelKit.Option("Keep the center clear", "Reserve the middle 60% of the screen; nothing can be placed there.", settings.KeepCenterClear, on => Quietly(() => hub.SetKeepCenterClear(on))));
        int size = settings.GridSize, gap = settings.GridGap;
        bool pending = false;
        void Apply(bool commit)
        {
            if (commit) { Quietly(() => hub.SetGrid(size, gap, true)); return; }
            if (pending) return;
            // One redraw per pass of the dispatcher, however fast the slider moves.
            pending = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                pending = false;
                Quietly(() => hub.SetGrid(size, gap, false));
            }));
        }
        section.Children.Add(SliderRow("Grid size", "Smaller cells give finer control.", DeckLayout.MinGridSize, DeckLayout.MaxGridSize, size, v => $"{v} px",
            v => { size = v; Apply(false); }, () => Apply(true)));
        section.Children.Add(SliderRow("Gap between widgets", "The least space between two widgets. 0 lets them touch.", 0, DeckLayout.MaxGridGap, gap, v => $"{v} px",
            v => { gap = v; Apply(false); }, () => Apply(true)));
        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, PanelKit.S12) };
        var reset = PanelKit.Outline("Reset to 24 and 12", () => { Quietly(() => hub.SetGrid((int)DeckLayout.DefaultGrid, (int)DeckLayout.DefaultGap)); RefillDisplay(); }, "Back to the usual grid size and gap. Widgets that no longer fit settle in the nearest free spot.");
        reset.Margin = new Thickness(0, 0, PanelKit.S8, 0);
        buttons.Children.Add(reset);
        buttons.Children.Add(PanelKit.Outline("Reset button position", () => Quietly(hub.ResetOrb), "Put the control button back near the bottom-right corner of your main monitor."));
        section.Children.Add(buttons);
    }

    /// <summary>A label and description on the left, a slider in the middle and the value on the right.</summary>
    private static Grid SliderRow(string label, string description, int min, int max, int value, Func<int, string> format, Action<int> changed, Action committed)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, PanelKit.S16) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 150 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star), MinWidth = 140 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        var text = new StackPanel { Margin = new Thickness(0, 0, PanelKit.S16, 0) };
        var name = Theme.Label(label, 13.5);
        name.FontWeight = FontWeights.SemiBold;
        text.Children.Add(name);
        var desc = Theme.Label(description, 12, Theme.Muted);
        desc.TextWrapping = TextWrapping.Wrap;
        desc.Margin = new Thickness(0, 3, 0, 0);
        text.Children.Add(desc);
        row.Children.Add(text);
        var shown = Theme.Label(format(value), 13);
        shown.HorizontalAlignment = HorizontalAlignment.Right;
        shown.VerticalAlignment = VerticalAlignment.Top;
        shown.Margin = new Thickness(0, 2, 0, 0);
        Grid.SetColumn(shown, 2);
        row.Children.Add(shown);
        var slider = ThemedSlider(min, max, value, v => { shown.Text = format(v); changed(v); }, committed);
        slider.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(slider, 1);
        row.Children.Add(slider);
        return row;
    }

    /// <summary>A slider in the app's look (the stock one is the system's). Mouse and arrow keys set whole numbers; committed runs when it is let go.</summary>
    private static FrameworkElement ThemedSlider(int min, int max, int value, Action<int> changed, Action committed)
    {
        const double Thumb = 16;
        var track = new Grid { Height = 24, Background = Brushes.Transparent, Cursor = Cursors.Hand, Focusable = true, FocusVisualStyle = null };
        var bar = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Theme.Surface3, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Thumb / 2, 0, Thumb / 2, 0) };
        var fill = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Theme.Primary, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Thumb / 2, 0, 0, 0) };
        var thumb = new Ellipse { Width = Thumb, Height = Thumb, Fill = Theme.Primary, Stroke = Theme.Background, StrokeThickness = 2, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        track.Children.Add(bar);
        track.Children.Add(fill);
        track.Children.Add(thumb);
        System.Windows.Automation.AutomationProperties.SetName(track, "Slider");
        void Show()
        {
            double room = Math.Max(0, track.ActualWidth - Thumb);
            double at = max > min ? (value - min) / (double)(max - min) * room : 0;
            thumb.Margin = new Thickness(at, 0, 0, 0);
            fill.Width = at;
        }
        void Set(int next, bool fire)
        {
            next = Math.Clamp(next, min, max);
            if (next == value) return;
            value = next;
            Show();
            if (fire) changed(value);
        }
        int FromMouse(MouseEventArgs e)
        {
            double room = Math.Max(1, track.ActualWidth - Thumb);
            double ratio = Math.Clamp((e.GetPosition(track).X - Thumb / 2) / room, 0, 1);
            return min + (int)Math.Round(ratio * (max - min));
        }
        track.SizeChanged += (_, _) => Show();
        track.MouseLeftButtonDown += (_, e) => { track.Focus(); track.CaptureMouse(); Set(FromMouse(e), true); e.Handled = true; };
        track.MouseMove += (_, e) => { if (track.IsMouseCaptured) Set(FromMouse(e), true); };
        track.MouseLeftButtonUp += (_, e) => { if (!track.IsMouseCaptured) return; track.ReleaseMouseCapture(); committed(); e.Handled = true; };
        track.KeyDown += (_, e) =>
        {
            int step = e.Key switch { Key.Left or Key.Down => -1, Key.Right or Key.Up => 1, Key.PageDown => -4, Key.PageUp => 4, _ => 0 };
            if (step == 0) return;
            Set(value + step, true);
            committed();
            e.Handled = true;
        };
        return track;
    }
}
