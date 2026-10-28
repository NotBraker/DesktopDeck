using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DesktopDeck.Widgets;

/// <summary>Drawing helpers shared by the System widgets (live bars, sparklines, threshold colors, compact frames).</summary>
internal static class SystemKit
{
    public const string OkHex = "#22c55e", WarnHex = "#f59e0b", CriticalHex = "#ef4444";

    public static CultureInfo Culture => CultureInfo.CurrentCulture;

    /// <summary>
    /// The framed card; when <paramref name="header"/> is false (the widget is too short) the title row is left out
    /// so the content still fits.
    /// </summary>
    public static (Border Card, DockPanel Body) Frame(WidgetContext ctx, bool header, FrameworkElement? badge = null, params FrameworkElement[] actions)
    {
        if (header) return WidgetKit.Frame(ctx, badge, actions);
        var card = WidgetKit.Card(ctx);
        double pad = ctx.GetNumber("_padding");
        var dock = new DockPanel { Margin = new Thickness(pad + 2, Math.Max(0, pad - 4), pad, Math.Max(0, pad - 4)), LastChildFill = true };
        card.Child = dock;
        return (card, dock);
    }

    /// <summary>Whether the title row fits: shown when the user wants it and the widget is at least <paramref name="minHeight"/> tall.</summary>
    public static bool Header(WidgetContext ctx, double minHeight) => ctx.Item.ShowHeader && ctx.Height >= minHeight;

    /// <summary>Room left for content inside the card (after padding and, if shown, the title row).</summary>
    public static Size Room(WidgetContext ctx, bool header)
    {
        double pad = ctx.GetNumber("_padding");
        double height = ctx.Height - (header ? 2 * pad - 1 + Math.Max(24, ctx.Font(10.5) * 1.6) + 10 : 2 * Math.Max(0, pad - 4));
        return new Size(Math.Max(0, ctx.Width - 2 * pad - 2), Math.Max(0, height));
    }

    /// <summary>The fill for a level: 0 = <paramref name="normal"/>, 1 = the warn color, 2 = the critical color.</summary>
    public static Brush LevelBrush(WidgetContext ctx, int level, Brush normal, string warnKey = "warnColor", string criticalKey = "critColor")
        => level switch
        {
            2 => ctx.GetColor(criticalKey, Theme.Brush(CriticalHex)),
            1 => ctx.GetColor(warnKey, Theme.Brush(WarnHex)),
            _ => normal,
        };

    /// <summary>Moves a <see cref="WidgetKit.Bar"/> to a new value and color without rebuilding it.</summary>
    public static void SetBar(Grid bar, double fraction, Brush fill)
    {
        fraction = double.IsFinite(fraction) ? Math.Clamp(fraction, 0, 1) : 0;
        if (bar.ColumnDefinitions.Count < 2 || bar.Children.Count < 2) return;
        bar.ColumnDefinitions[0].Width = new GridLength(Math.Max(0.0001, fraction), GridUnitType.Star);
        bar.ColumnDefinitions[1].Width = new GridLength(Math.Max(0.0001, 1 - fraction), GridUnitType.Star);
        if (bar.Children[1] is Border fillBorder) fillBorder.Background = fill;
    }

    /// <summary>Left and right content on one line (right side auto-sized).</summary>
    public static Grid Split(UIElement left, UIElement? right, double gap = 8)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(left);
        if (right is FrameworkElement r)
        {
            r.Margin = new Thickness(gap, r.Margin.Top, r.Margin.Right, r.Margin.Bottom);
            Grid.SetColumn(r, 1);
            grid.Children.Add(r);
        }
        return grid;
    }

    /// <summary>A glyph followed by text, vertically centered.</summary>
    public static StackPanel IconText(WidgetContext ctx, string glyph, Brush glyphBrush, TextBlock text, bool showIcon = true, double glyphSize = 13)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (showIcon)
        {
            var icon = WidgetKit.Icon(glyph, ctx.Font(glyphSize), glyphBrush);
            icon.Margin = new Thickness(0, 1, 7, 0);
            row.Children.Add(icon);
        }
        row.Children.Add(text);
        return row;
    }

    /// <summary>A brush with reduced opacity (for fills under a line).</summary>
    public static Brush Faded(Brush brush, double opacity)
    {
        if (brush is not SolidColorBrush solid) return brush;
        var copy = solid.Clone();
        copy.Opacity = solid.Opacity * opacity;
        copy.Freeze();
        return copy;
    }

    /// <summary>
    /// A small history graph drawn to its own size, newest value at the right. Up to two series (e.g. download
    /// over upload); call <see cref="Set"/> to update it in place.
    /// </summary>
    internal sealed class Sparkline : FrameworkElement
    {
        private double[] first = [], second = [];

        /// <summary>How many samples fill the width (fewer samples grow in from the right).</summary>
        public int Capacity { get; set; } = 40;
        /// <summary>Fixed top value (100 for percents); null scales to the data.</summary>
        public double? Max { get; set; }
        public Func<IReadOnlyList<double>, double>? AutoMax { get; set; }
        public Brush Stroke { get; set; } = Theme.Primary;
        public Brush? SecondStroke { get; set; }
        /// <summary>Fill under the line.</summary>
        public bool Area { get; set; } = true;
        public double Thickness { get; set; } = 1.6;
        public bool Baseline { get; set; } = true;

        public void Set(IReadOnlyList<double> values, IReadOnlyList<double>? secondValues = null)
        {
            first = values.ToArray();
            second = secondValues?.ToArray() ?? [];
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w < 6 || h < 6) return;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
            if (Baseline) dc.DrawRectangle(Theme.Line, null, new Rect(0, h - 1, w, 1));
            double max = Max ?? (AutoMax?.Invoke(first.Concat(second).ToList()) ?? Math.Max(1e-9, first.Concat(second).DefaultIfEmpty(0).Max() * 1.15));
            if (max <= 0) max = 1;
            if (SecondStroke is not null) Draw(dc, second, SecondStroke, w, h, max);
            Draw(dc, first, Stroke, w, h, max);
        }

        private void Draw(DrawingContext dc, double[] values, Brush stroke, double w, double h, double max)
        {
            if (values.Length < 2) return;
            int capacity = Math.Max(Math.Max(2, Capacity), values.Length);
            double step = w / (capacity - 1), half = Thickness / 2;
            var points = new Point[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                double v = double.IsFinite(values[i]) ? Math.Clamp(values[i] / max, 0, 1) : 0;
                points[i] = new Point(w - (values.Length - 1 - i) * step, h - half - v * (h - Thickness));
            }
            if (Area && stroke is SolidColorBrush solid)
            {
                var area = new StreamGeometry();
                using (var g = area.Open())
                {
                    g.BeginFigure(new Point(points[0].X, h), true, true);
                    g.PolyLineTo(points, false, true);
                    g.LineTo(new Point(points[^1].X, h), false, true);
                }
                area.Freeze();
                var top = solid.Color;
                var fill = new LinearGradientBrush(Color.FromArgb((byte)(top.A * 0.34 * solid.Opacity), top.R, top.G, top.B), Color.FromArgb(0, top.R, top.G, top.B), 90);
                fill.Freeze();
                dc.DrawGeometry(fill, null, area);
            }
            var line = new StreamGeometry();
            using (var g = line.Open())
            {
                g.BeginFigure(points[0], false, false);
                g.PolyLineTo(points.Skip(1).ToList(), true, true);
            }
            line.Freeze();
            var pen = new Pen(stroke, Thickness) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            pen.Freeze();
            dc.DrawGeometry(null, pen, line);
        }
    }

    /// <summary>A holder whose child is replaced on every update (for rings, which are cheap to redraw).</summary>
    public static Border Holder(UIElement? child = null) => new() { Child = child };

    /// <summary>Reads "data.sample.KEY" for previews, with a fallback.</summary>
    public static double SampleNumber(WidgetContext ctx, string key, double fallback)
        => ctx.Item.Options.TryGetValue("data.sample." + key, out var text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public static string SampleText(WidgetContext ctx, string key, string fallback)
        => ctx.Item.Options.TryGetValue("data.sample." + key, out var text) ? text : fallback;
}
