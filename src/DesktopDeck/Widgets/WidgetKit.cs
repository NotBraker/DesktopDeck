using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shapes = System.Windows.Shapes;

namespace DesktopDeck.Widgets;

/// <summary>
/// Building blocks that give plug-in widgets the deck's look and honor the shared appearance settings
/// (text size, corner roundness, card color and opacity, text color, spacing). Prefer these over raw WPF.
/// Theme colors: Theme.Text, Theme.Text2, Theme.Muted, Theme.Dim, Theme.Surface, Theme.Surface3, Theme.Line,
/// Theme.LineStrong, Theme.Primary, Theme.Olive, Theme.Danger; Theme.Accent(hex), Theme.Tint(hex, opacity).
/// </summary>
internal static class WidgetKit
{
    /// <summary>The widget card in the item's style (Solid, See-through, Outline, No card) with the shared appearance settings and the deck-wide defaults.</summary>
    public static Border Card(WidgetContext ctx) => Appearance.Card(ctx.Defaults, ctx.Item);

    /// <summary>
    /// The standard framed widget: a card with the small uppercase title (unless the user hid it), an optional badge,
    /// header buttons on the right, and a body to fill. Returns the card (return it from Build) and the body.
    /// </summary>
    public static (Border Card, DockPanel Body) Frame(WidgetContext ctx, FrameworkElement? badge = null, params FrameworkElement[] actions)
    {
        var card = Card(ctx);
        double pad = ctx.GetNumber("_padding");
        var dock = new DockPanel { Margin = new Thickness(pad + 2, pad - 1, pad, pad), LastChildFill = true };
        card.Child = dock;
        if (!ctx.Item.ShowHeader && badge is null && actions.Length == 0) return (card, dock);
        var head = new Grid { Margin = new Thickness(0, 0, 0, 10), MinHeight = 24 };
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (ctx.Item.ShowHeader)
        {
            var title = Theme.WidgetTitle(ctx.Item.Name, Appearance.TitleBrush(ctx.Defaults, ctx.Item));
            title.FontSize = ctx.Font(10.5);
            head.Children.Add(title);
        }
        if (badge is not null) { badge.Margin = new Thickness(8, 0, 6, 0); Grid.SetColumn(badge, 1); head.Children.Add(badge); }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var action in actions) { action.Margin = new Thickness(4, 0, 0, 0); buttons.Children.Add(action); }
        Grid.SetColumn(buttons, 2);
        head.Children.Add(buttons);
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head);
        return (card, dock);
    }

    /// <summary>Text in the widget's text color and scaled size.</summary>
    public static TextBlock Text(WidgetContext ctx, string text, double size = 13, Brush? color = null, bool bold = false)
    {
        var label = Theme.Label(text, ctx.Font(size), color ?? ctx.TextBrush);
        if (bold) label.FontWeight = FontWeights.SemiBold;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        return label;
    }

    /// <summary>Muted secondary text.</summary>
    public static TextBlock Muted(WidgetContext ctx, string text, double size = 11.5) => Text(ctx, text, size, Theme.Muted);

    /// <summary>A small rounded label such as "2/5" or "Live".</summary>
    public static Border Pill(WidgetContext ctx, string text, string? accentHex = null)
    {
        var label = Theme.Label(text, ctx.Font(11), accentHex is null ? Theme.Text2 : Theme.Accent(accentHex));
        label.FontWeight = FontWeights.SemiBold;
        return new Border { CornerRadius = new CornerRadius(9), Background = accentHex is null ? Theme.Surface3 : Theme.Tint(accentHex, 0.16), Padding = new Thickness(8, 1, 8, 2), Child = label, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>A small square icon button for headers and toolbars.</summary>
    public static Button IconButton(string glyph, string tooltip, Action click, double size = 24, bool accent = false)
    {
        var button = Theme.IconButton(glyph, tooltip, size, accent);
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>A regular text button.</summary>
    public static Button Button(string text, Action click, bool prominent = false)
    {
        var button = Theme.Button(text, prominent);
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>A grey note for empty states ("Nothing here yet…").</summary>
    public static TextBlock Empty(WidgetContext ctx, string text)
    {
        var note = Theme.Label(text, ctx.Font(12), Theme.Muted);
        note.TextWrapping = TextWrapping.Wrap;
        note.Margin = new Thickness(4, 6, 4, 0);
        return note;
    }

    /// <summary>A list row with a hover highlight. Set Cursor and handle MouseLeftButtonUp to make it clickable.</summary>
    public static Border Row(UIElement child)
    {
        var row = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 5, 6, 5), Background = Brushes.Transparent, Child = child };
        row.MouseEnter += (_, _) => row.Background = Theme.SurfaceHover;
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        return row;
    }

    /// <summary>Makes an element clickable (hand cursor; handles the click so dragging doesn't start).</summary>
    public static T Clickable<T>(T element, Action click, string? tooltip = null) where T : FrameworkElement
    {
        element.Cursor = Cursors.Hand;
        if (tooltip is not null) element.ToolTip = tooltip;
        element.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return element;
    }

    /// <summary>A scrolling area without a visible scrollbar (mouse wheel works).</summary>
    public static ScrollViewer Scroll(UIElement content) => new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Margin = new Thickness(-6, 0, -4, 0) };

    /// <summary>A rounded progress bar; <paramref name="fraction"/> is 0–1.</summary>
    public static Grid Bar(double fraction, Brush fill, double height = 6, Brush? track = null)
    {
        fraction = double.IsFinite(fraction) ? Math.Clamp(fraction, 0, 1) : 0;
        var bar = new Grid { Height = height };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, fraction), GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - fraction), GridUnitType.Star) });
        var back = new Border { CornerRadius = new CornerRadius(height / 2), Background = track ?? Theme.Surface3 };
        Grid.SetColumnSpan(back, 2);
        bar.Children.Add(back);
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(height / 2), Background = fill });
        return bar;
    }

    /// <summary>A ring gauge (0–1) with optional text in the middle.</summary>
    public static FrameworkElement Ring(double fraction, Brush fill, double size, double thickness, UIElement? center = null, Brush? track = null)
    {
        fraction = double.IsFinite(fraction) ? Math.Clamp(fraction, 0, 1) : 0;
        var grid = new Grid { Width = size, Height = size };
        grid.Children.Add(new Shapes.Ellipse { Stroke = track ?? Theme.Surface3, StrokeThickness = thickness });
        if (fraction > 0)
        {
            double r = (size - thickness) / 2, c = size / 2, angle = Math.Min(fraction, 0.9999) * 2 * Math.PI;
            var start = new Point(c, c - r);
            var end = new Point(c + r * Math.Sin(angle), c - r * Math.Cos(angle));
            var figure = new PathFigure { StartPoint = start, IsClosed = false };
            figure.Segments.Add(new ArcSegment(end, new Size(r, r), 0, angle > Math.PI, SweepDirection.Clockwise, true));
            grid.Children.Add(new Shapes.Path { Data = new PathGeometry([figure]), Stroke = fill, StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        }
        if (center is not null) grid.Children.Add(center);
        return grid;
    }

    /// <summary>A Segoe Fluent Icons glyph.</summary>
    public static TextBlock Icon(string glyph, double size, Brush? color = null) => Theme.Icon(glyph, size, color);

    /// <summary>A thin horizontal line.</summary>
    public static Border Divider() => new() { Height = 1, Background = Theme.Line, Margin = new Thickness(0, 6, 0, 6) };

    /// <summary>Reads a color like "#22c55e" into a brush (falls back to the theme's primary color).</summary>
    public static Brush Color(string hex) => Theme.Accent(hex);
}

internal static class ElementExtensions
{
    public static T WithMargin<T>(this T element, double left, double top, double right, double bottom) where T : FrameworkElement
    {
        element.Margin = new Thickness(left, top, right, bottom);
        return element;
    }
}
