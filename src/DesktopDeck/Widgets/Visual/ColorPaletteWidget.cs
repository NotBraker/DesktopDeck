using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DesktopDeck.Models;

namespace DesktopDeck.Widgets;

/// <summary>
/// A personal swatch collection (built-in palettes or your own "#hex | name" lines). Click a swatch to copy its color;
/// the copy format (hex, hex without #, rgb(), hsl()) is an option. Parsing and formats live in ColorPaletteLogic.cs.
/// </summary>
internal sealed class ColorPaletteWidget : IWidget
{
    public string Kind => "color-palette";
    public string Name => "Color palette";
    public string Description => "Swatches you can click to copy a color code";
    public string Section => "Visual";
    public string Glyph => "";
    public string Accent => "#b398cd";
    public (double Width, double Height) DefaultSize => (276, 276);
    public (double Width, double Height) MinimumSize => (180, 132);

    public IReadOnlyList<OptionSpec> Options { get; } =
    [
        OptionSpec.Choice("preset", "Palette", "Dusk", ColorPaletteLogic.PresetNames, ["Your own colors", "Dusk", "Pastel", "Material", "Nord", "Grayscale", "Sunset", "Forest"], "Colors", "Pick \"Your own colors\" to use the list below."),
        OptionSpec.Lines("colors", "Your colors", ColorPaletteLogic.Preset("Dusk"), "Colors", "One per line, e.g. #D8AF68 | Gold. Names are optional; #RGB works too."),
        OptionSpec.Choice("layout", "Layout", "grid", ["grid", "list"], ["Grid of swatches", "List"], "Layout"),
        OptionSpec.Choice("shape", "Swatch shape", "rounded", ["rounded", "square", "circle"], ["Wide chips", "Rounded squares", "Circles"], "Layout"),
        OptionSpec.Number("columns", "Columns (grid)", 3, 1, 8, "Layout", "Fewer are used when the widget is narrow."),
        OptionSpec.Number("size", "Swatch size", 52, 16, 140, "Layout"),
        OptionSpec.Number("gap", "Spacing", 8, 0, 24, "Layout"),
        OptionSpec.Toggle("names", "Show names", true, "What to show"),
        OptionSpec.Toggle("hex", "Show hex code", true, "What to show"),
        OptionSpec.Toggle("rgb", "Show RGB values", false, "What to show"),
        OptionSpec.Choice("copyAs", "Click copies", "hex", ColorPaletteLogic.CopyFormats, ["#D8AF68", "D8AF68 (no #)", "rgb(216, 175, 104)", "hsl(39, 61%, 61%)"], "Behavior"),
        OptionSpec.Toggle("uppercase", "Capital letters in hex codes", true, "Behavior"),
        OptionSpec.Toggle("outline", "Thin outline around swatches", true, "Colors", "Helps very dark or very light colors stand out."),
    ];

    public IEnumerable<Action<LauncherItem>> Samples =>
    [
        _ => { },
        item => { item.Options["preset"] = "Nord"; item.Options["shape"] = "circle"; item.Options["columns"] = "3"; item.Options["size"] = "48"; item.Options["rgb"] = "true"; item.Width = 372; item.Height = 276; },
        item => { item.Options["preset"] = "Pastel"; item.Options["layout"] = "list"; item.Options["rgb"] = "true"; item.Options["shape"] = "square"; item.Width = 276; item.Height = 276; },
        item => { item.Options["preset"] = "Sunset"; item.Options["shape"] = "square"; item.Options["columns"] = "2"; item.Options["names"] = "false"; item.Options["copyAs"] = "rgb"; item.Width = 180; item.Height = 228; },
    ];

    private static List<ColorPaletteLogic.Swatch> Swatches(WidgetContext ctx) =>
        ColorPaletteLogic.ParseAll(ctx.Get("preset") == "Custom" ? ctx.Get("colors") : ColorPaletteLogic.Preset(ctx.Get("preset")));

    public FrameworkElement Build(WidgetContext ctx)
    {
        var (card, body) = WidgetKit.Frame(ctx);
        var swatches = Swatches(ctx);
        if (swatches.Count == 0)
        {
            body.Children.Add(WidgetKit.Empty(ctx, "No colors yet. Add lines like \"#D8AF68 | Gold\" in Customize…"));
            return card;
        }

        double pad = ctx.GetNumber("_padding");
        double inner = Math.Max(60, ctx.Width - pad * 2 - 4);
        string shape = ctx.Get("shape");
        bool list = ctx.Get("layout") == "list";
        double gap = ctx.GetNumber("gap");
        double size = ctx.GetNumber("size");
        bool names = ctx.GetBool("names"), hex = ctx.GetBool("hex"), rgb = ctx.GetBool("rgb");
        bool outline = ctx.GetBool("outline");
        Brush edge = outline ? Theme.LineStrong : Brushes.Transparent;

        FrameworkElement Cell(ColorPaletteLogic.Swatch swatch, FrameworkElement chip, FrameworkElement? label)
        {
            var host = new StackPanel { Background = Brushes.Transparent };
            host.Children.Add(chip);
            if (label is not null) host.Children.Add(label);
            string copyText = ColorPaletteLogic.Format(swatch, ctx.Get("copyAs"), ctx.GetBool("uppercase"));
            var clickable = WidgetKit.Clickable(host, () => Copy(ctx, swatch, copyText, chip), "Copy " + copyText);
            chip.MouseEnter += (_, _) => { if (chip is Border b) b.BorderBrush = ctx.TextBrush; };
            chip.MouseLeave += (_, _) => { if (chip is Border b) b.BorderBrush = edge; };
            return clickable;
        }

        Border Chip(ColorPaletteLogic.Swatch swatch, double width, double height)
        {
            double radius = shape == "circle" ? Math.Max(width, height) / 2 : shape == "square" ? Math.Min(14, Math.Min(width, height) * 0.22) : Math.Min(12, height * 0.3);
            return new Border
            {
                Width = double.IsNaN(width) ? double.NaN : width, Height = height, CornerRadius = new CornerRadius(radius),
                Background = Theme.Brush(swatch.Hex), BorderBrush = edge, BorderThickness = new Thickness(1.5),
            };
        }

        if (list)
        {
            var rows = new StackPanel();
            foreach (var swatch in swatches)
            {
                double side = Math.Clamp(size * 0.6, 22, 44);
                var chip = Chip(swatch, side, side);
                chip.Margin = new Thickness(0, 0, 12, 0);
                chip.VerticalAlignment = VerticalAlignment.Center;
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                string title = names && swatch.Name.Length > 0 ? swatch.Name : hex ? swatch.Hex : "";
                if (title.Length > 0) text.Children.Add(WidgetKit.Text(ctx, title, 13, bold: true));
                var details = new List<string>();
                if (hex && title != swatch.Hex) details.Add(swatch.Hex);
                if (rgb) details.Add(swatch.Rgb);
                if (details.Count > 0) text.Children.Add(WidgetKit.Muted(ctx, string.Join("   ·   ", details), 10.5));
                var line = new Grid { Background = Brushes.Transparent };
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                line.ColumnDefinitions.Add(new ColumnDefinition());
                line.Children.Add(chip);
                Grid.SetColumn(text, 1);
                line.Children.Add(text);
                string copyText = ColorPaletteLogic.Format(swatch, ctx.Get("copyAs"), ctx.GetBool("uppercase"));
                var row = WidgetKit.Row(line);
                row.Margin = new Thickness(0, Math.Max(0, gap / 4 - 1), 0, Math.Max(0, gap / 4 - 1));
                WidgetKit.Clickable(row, () => Copy(ctx, swatch, copyText, chip), "Copy " + copyText);
                rows.Children.Add(row);
            }
            body.Children.Add(WidgetKit.Scroll(rows));
            return card;
        }

        // In a short widget shrink the swatches so at least one whole row (swatch and its text) is visible.
        double bodyHeight = ctx.Height - pad * 2 - (ctx.Item.ShowHeader ? 34 : 0);
        double labelHeight = (names ? ctx.Font(12) * 1.4 : 0) + (hex ? ctx.Font(11) * 1.4 : 0) + (rgb ? ctx.Font(10) * 1.4 : 0) + 5;
        size = Math.Min(size, Math.Max(20, bodyHeight - labelHeight - gap - 4));

        int columns = Math.Clamp(ctx.GetInt("columns"), 1, Math.Max(1, (int)(inner / 52)));
        columns = Math.Min(columns, swatches.Count);
        double cellWidth = (inner - 6) / columns;
        var grid = new UniformGrid { Columns = columns };
        bool centered = shape != "rounded";
        foreach (var swatch in swatches)
        {
            double usable = Math.Max(16, cellWidth - gap);
            double chipSide = Math.Min(size, usable);
            var chip = shape == "rounded" ? Chip(swatch, double.NaN, size) : Chip(swatch, chipSide, chipSide);
            chip.HorizontalAlignment = centered ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
            var label = new StackPanel { Margin = new Thickness(0, 5, 0, 0) };
            if (names && swatch.Name.Length > 0) label.Children.Add(Aligned(WidgetKit.Text(ctx, swatch.Name, 12, bold: true), centered));
            if (hex) label.Children.Add(Aligned(WidgetKit.Muted(ctx, swatch.Hex, 11), centered));
            if (rgb) label.Children.Add(Aligned(WidgetKit.Muted(ctx, swatch.Rgb, 10), centered));
            var cell = Cell(swatch, chip, label.Children.Count > 0 ? label : null);
            cell.Margin = new Thickness(gap / 2, gap / 2, gap / 2, gap / 2);
            grid.Children.Add(cell);
        }
        body.Children.Add(WidgetKit.Scroll(grid));
        return card;
    }

    private static TextBlock Aligned(TextBlock text, bool centered)
    {
        text.HorizontalAlignment = centered ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        return text;
    }

    private static void Copy(WidgetContext ctx, ColorPaletteLogic.Swatch swatch, string text, UIElement flash)
    {
        if (ctx.IsSnapshot || ctx.IsEditing) return;
        try
        {
            Clipboard.SetText(text);
            ctx.Status("Copied " + text + (swatch.Name.Length > 0 ? " (" + swatch.Name + ")" : ""));
            flash.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(350)));
        }
        catch (ExternalException) { ctx.Status("The clipboard is busy; try again."); }
    }

    public IEnumerable<WidgetAction> Actions(WidgetContext ctx) =>
    [
        new("Add a color…", "", () =>
        {
            if (ctx.Ask("Add a color", "Color (e.g. #D8AF68 | Gold)") is not { } line) return;
            if (ColorPaletteLogic.Parse(line) is not { } added) { ctx.Inform("Use a color like #D8AF68 or #D8AF68 | Gold.", "Color palette"); return; }
            // Adding to a built-in palette turns it into your own list so nothing is lost.
            string existing = ctx.Get("preset") == "Custom" ? ctx.Get("colors") : ColorPaletteLogic.Preset(ctx.Get("preset"));
            ctx.Set("colors", existing.TrimEnd() + "\n" + added.Hex + (added.Name.Length > 0 ? " | " + added.Name : ""), false);
            ctx.Set("preset", "Custom");
        }),
        new("Copy all colors", "", () =>
        {
            try
            {
                Clipboard.SetText(ColorPaletteLogic.AllAsText(Swatches(ctx), ctx.Get("copyAs"), ctx.GetBool("uppercase")));
                ctx.Status("Copied the whole palette.");
            }
            catch (ExternalException) { ctx.Status("The clipboard is busy; try again."); }
        }),
    ];
}
