using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DesktopDeck.Models;

namespace DesktopDeck.Widgets;

/// <summary>
/// The periodic table, fully offline: all 118 elements as a colored table (click one for its details), a single
/// "element of the day" card, or a list of one category. Data and tests: PeriodicTableLogic.cs.
/// </summary>
internal sealed class PeriodicTableWidget : IWidget
{
    public string Kind => "periodic-table";
    public string Name => "Periodic table";
    public string Description => "All 118 elements, categories and details, or an element of the day";
    public string Section => "Study";
    public string Glyph => "";
    public string Accent => "#5E9BA6";
    public (double Width, double Height) DefaultSize => (756, 420);
    public (double Width, double Height) MinimumSize => (516, 300);

    private static readonly string[] CategoryChoices = ["none", .. PeriodicTableLogic.Categories.Select(c => c.Id)];
    private static readonly string[] CategoryLabels = ["Nothing", .. PeriodicTableLogic.Categories.Select(c => c.Name)];

    public IReadOnlyList<OptionSpec> Options { get; } =
    [
        OptionSpec.Choice("viewMode", "View", "table", ["table", "day", "category"], ["Full table", "Element of the day", "One category"], "What to show", "The refresh button in the header also cycles through these."),
        OptionSpec.Choice("highlight", "Highlight category", "none", CategoryChoices, CategoryLabels, "What to show", "In the table the other elements fade; in the category view it is the category listed."),
        OptionSpec.Toggle("showNumbers", "Show atomic numbers", true, "What to show"),
        OptionSpec.Toggle("showMasses", "Show atomic masses in the cells", false, "What to show", "Needs big cells; hidden when they would be too small."),
        OptionSpec.Toggle("showLegend", "Show the category legend", true, "What to show", "Click a legend entry to highlight that category."),
        OptionSpec.Toggle("showDetails", "Show the details panel", true, "What to show"),
        OptionSpec.Toggle("showConfig", "Show the electron configuration", true, "What to show"),
        OptionSpec.Toggle("showFBlock", "Show the lanthanides and actinides", true, "What to show"),
        OptionSpec.Text("defaultElement", "Details panel shows", "", "What to show", "A symbol, name or number (Fe, iron, 26). Empty = the element of the day until you click another."),
        OptionSpec.Choice("labelSize", "Label size", "medium", ["small", "medium", "large"], ["Small", "Medium", "Large"], "Layout"),
        OptionSpec.Choice("cellStyle", "Cell style", "tinted", ["tinted", "solid", "outline"], ["Tinted", "Solid color", "Outline"], "Layout"),
        OptionSpec.Number("massDecimals", "Decimal places in masses", 2, 0, 3, "Layout"),
        OptionSpec.Toggle("dimOthers", "Fade the other categories when highlighting", true, "Layout"),
        OptionSpec.Lines("categoryColors", "Category colors", PeriodicTableLogic.DefaultColors, "Colors", "One per line: Category | #color."),
    ];

    public IEnumerable<Action<LauncherItem>> Samples =>
    [
        item => { item.Options["defaultElement"] = "Fe"; },
        item =>
        {
            item.Options["viewMode"] = "day";
            item.Accent = "#C4684F";
            item.Width = 468; item.Height = 252;
        },
        item =>
        {
            item.Options["viewMode"] = "category";
            item.Options["highlight"] = "noble";
            item.Options["defaultElement"] = "Ne";
            item.Width = 516; item.Height = 300;
        },
        item =>
        {
            item.Options["showMasses"] = "true";
            item.Options["highlight"] = "halogen";
            item.Options["cellStyle"] = "solid";
            item.Options["labelSize"] = "large";
            item.Options["defaultElement"] = "Cl";
            item.Width = 852; item.Height = 468;
        },
        item =>
        {
            item.Options["viewMode"] = "day";
            item.Width = 276; item.Height = 276;
        },
    ];

    // ---------- Settings as one object ----------

    private sealed record Look(Dictionary<string, string> Colors, string Style, bool Numbers, bool Masses, int Decimals, double LabelFactor, string Highlight, bool Dim, double Font);

    private static Look LookOf(WidgetContext ctx, string highlight) => new(
        PeriodicTableLogic.ParseColors(ctx.GetLines("categoryColors")),
        ctx.Get("cellStyle"), ctx.GetBool("showNumbers"), ctx.GetBool("showMasses"), ctx.GetInt("massDecimals"),
        (ctx.Get("labelSize") switch { "small" => 0.82, "large" => 1.2, _ => 1.0 }) * Math.Clamp(ctx.TextScale, 0.8, 1.3),
        highlight, ctx.GetBool("dimOthers"), ctx.TextScale);

    private static string HighlightOf(WidgetContext ctx)
    {
        string h = ctx.State<string?>("pt.hl", null) ?? ctx.Get("highlight");
        return PeriodicTableLogic.Categories.Any(c => c.Id == h) ? h : "none";
    }

    private static PeriodicTableLogic.Element Shown(WidgetContext ctx)
    {
        int selected = ctx.State("pt.sel", 0);
        if (PeriodicTableLogic.ByNumber(selected) is { } picked) return picked;
        if (PeriodicTableLogic.Find(ctx.Get("defaultElement")) is { } chosen) return chosen;
        return PeriodicTableLogic.OfTheDay(DateTime.Today);
    }

    private static void Select(WidgetContext ctx, int number) => ctx.SetState("pt.sel", ctx.State("pt.sel", 0) == number ? 0 : number);

    private static void SetHighlight(WidgetContext ctx, string id) => ctx.SetState<string?>("pt.hl", HighlightOf(ctx) == id ? "none" : id);

    public IEnumerable<WidgetAction> Actions(WidgetContext ctx)
    {
        yield return new WidgetAction("Find an element…", Theme.Glyphs.Search, () =>
        {
            string? text = ctx.Ask("Find an element", "Symbol, name or atomic number");
            if (text is null) return;
            if (PeriodicTableLogic.Find(text) is { } found) ctx.SetState("pt.sel", found.Number);
            else ctx.Status("No element called \"" + text.Trim() + "\".");
        });
        yield return new WidgetAction("Next view", Theme.Glyphs.Refresh, () => NextView(ctx));
    }

    private static void NextView(WidgetContext ctx) => ctx.Set("viewMode", ctx.Get("viewMode") switch { "table" => "day", "day" => "category", _ => "table" });

    // ---------- Drawing ----------

    public FrameworkElement Build(WidgetContext ctx)
    {
        string highlight = HighlightOf(ctx);
        var look = LookOf(ctx, highlight);
        string mode = ctx.Get("viewMode");
        var shown = Shown(ctx);

        FrameworkElement? badge = null;
        if (mode == "category" && highlight != "none") badge = StudyKit.PillBox(ctx, PeriodicTableLogic.CategoryName(highlight), look.Colors[highlight]);
        else if (mode == "table" && highlight != "none") badge = StudyKit.PillBox(ctx, PeriodicTableLogic.CategoryName(highlight), look.Colors[highlight]);
        var next = WidgetKit.IconButton(Theme.Glyphs.Refresh, "Next view (table, element of the day, one category)", () => NextView(ctx), 24);
        var (card, body) = WidgetKit.Frame(ctx, badge, next);

        double pad = ctx.GetNumber("_padding");
        double headerHeight = ctx.Item.ShowHeader || badge is not null ? 34 : 0;
        double innerW = Math.Max(60, ctx.Width - 2 * pad - 2);
        double innerH = Math.Max(60, ctx.Height - 2 * pad + 1 - headerHeight);

        bool small = mode == "table" && (innerW / 18 < 22 || innerH / 9.4 < 19);
        if (mode == "day" || small) body.Children.Add(DayCard(ctx, shown, look, innerW, innerH, mode == "day"));
        else if (mode == "category") body.Children.Add(CategoryView(ctx, shown, look, innerW, innerH));
        else body.Children.Add(Table(ctx, shown, look, innerW, innerH));
        return card;
    }

    private static string ColorOf(Look look, PeriodicTableLogic.Element e) => look.Colors.GetValueOrDefault(e.Category, "#8E8270");

    // ---------- One element as a cell ----------

    private static Border Cell(WidgetContext ctx, PeriodicTableLogic.Element e, Look look, double w, double h, bool selected, bool faded)
    {
        string hex = ColorOf(look, e);
        var tint = Theme.Brush(hex);
        Brush back = look.Style switch { "solid" => tint, "outline" => Brushes.Transparent, _ => Theme.Tint(hex, 0.26) };
        Brush border = selected ? Theme.Text : look.Style == "outline" ? Theme.Tint(hex, 0.9) : Theme.Tint(hex, 0.5);
        Brush symbolBrush = look.Style == "solid" ? Theme.Background : look.Style == "outline" ? tint : ctx.TextBrush;
        Brush smallBrush = look.Style == "solid" ? Theme.Tint("#1A1611", 0.7) : Theme.Text2;

        var grid = new Grid();
        double fit = (w - 3) / 1.3;
        double symbolSize = Math.Max(7, Math.Min(Math.Min(h * 0.44 * look.LabelFactor, fit * look.LabelFactor), fit));
        bool number = look.Numbers && w >= 24 && h >= 22;
        bool mass = look.Masses && w >= 34 && h >= 36;
        var symbol = new TextBlock
        {
            Text = e.Symbol, FontFamily = Theme.UiFont, FontSize = symbolSize, FontWeight = FontWeights.SemiBold, Foreground = symbolBrush,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, number ? h * 0.1 : 0, 0, mass ? h * 0.14 : 0),
        };
        grid.Children.Add(symbol);
        double small = Math.Clamp(h * 0.19, 5.5, 9.5);
        if (number)
            grid.Children.Add(new TextBlock { Text = e.Number.ToString(CultureInfo.InvariantCulture), FontFamily = Theme.UiFont, FontSize = small, Foreground = smallBrush, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(Math.Max(2, w * 0.06), 1, 0, 0) });
        if (mass)
            grid.Children.Add(new TextBlock { Text = PeriodicTableLogic.MassText(e, look.Decimals), FontFamily = Theme.UiFont, FontSize = small, Foreground = smallBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 1) });

        var cell = new Border
        {
            Background = back, BorderBrush = border, BorderThickness = new Thickness(selected ? 1.6 : 1), CornerRadius = new CornerRadius(Math.Min(5, Math.Min(w, h) * 0.14)),
            Margin = new Thickness(0.8), Child = grid, Opacity = faded ? 0.22 : 1, ClipToBounds = true,
        };
        return WidgetKit.Clickable(cell, () => Select(ctx, e.Number), $"{e.Name} ({e.Symbol}), number {e.Number}");
    }

    // ---------- The full table ----------

    private static FrameworkElement Table(WidgetContext ctx, PeriodicTableLogic.Element shown, Look look, double innerW, double innerH)
    {
        bool fBlock = ctx.GetBool("showFBlock");
        bool details = ctx.GetBool("showDetails"), legend = ctx.GetBool("showLegend");
        // With room, the legend sits in the gap above the f-block (two lines); otherwise it shares the empty space with the details.
        bool legendInGap = legend && fBlock && innerW >= 560 && 0.8 * innerH / 9.8 >= 25;
        double gap = legendInGap ? 0.8 : 0.4;
        double units = fBlock ? 7 + gap + 2 : 7;
        double cellW = innerW / 18, cellH = innerH / units;

        var grid = new Grid();
        for (int c = 0; c < 18; c++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int r = 1; r <= 10; r++)
            grid.RowDefinitions.Add(new RowDefinition { Height = r == 8 ? new GridLength(fBlock ? gap : 0, GridUnitType.Star) : r > 8 ? new GridLength(fBlock ? 1 : 0, GridUnitType.Star) : new GridLength(1, GridUnitType.Star) });

        bool dim = look.Highlight != "none" && look.Dim;
        foreach (var e in PeriodicTableLogic.All)
        {
            if (!fBlock && e.Row > 8) continue;
            var cell = Cell(ctx, e, look, cellW, cellH, e.Number == shown.Number && ctx.State("pt.sel", 0) == e.Number, dim && e.Category != look.Highlight);
            Grid.SetRow(cell, e.Row - 1);
            Grid.SetColumn(cell, e.Column - 1);
            grid.Children.Add(cell);
        }

        // Placeholders where the f-block belongs, and labels beside its rows.
        if (cellH >= 20 && cellW >= 24)
        {
            AddPlaceholder(grid, ctx, look, 6, "57–71", "lanthanide", cellW, cellH);
            AddPlaceholder(grid, ctx, look, 7, "89–103", "actinide", cellW, cellH);
        }
        if (fBlock && cellW * 2 >= 52)
        {
            AddRowLabel(grid, ctx, look, 9, "Lanthanides", "lanthanide", cellH);
            AddRowLabel(grid, ctx, look, 10, "Actinides", "actinide", cellH);
        }

        if (legendInGap)
        {
            var strip = LegendStrip(ctx, look, innerW, 0.8 * cellH);
            Grid.SetRow(strip, 7); Grid.SetColumnSpan(strip, 18);
            grid.Children.Add(strip);
        }

        // The empty space above the table holds the details (and the legend when it has no room elsewhere).
        double areaW = cellW * 10, areaH = cellH * 3;
        bool legendInArea = legend && !legendInGap && (details ? areaW >= 420 : true);
        if ((details || legendInArea) && areaW >= 200 && areaH >= 56)
        {
            var area = new Grid { Margin = new Thickness(1, 0, 1, 2) };
            Grid.SetColumn(area, 2); Grid.SetColumnSpan(area, 10); Grid.SetRow(area, 0); Grid.SetRowSpan(area, 3);
            double legendW = legendInArea ? (details ? 236 : areaW) : 0;
            if (details)
            {
                area.ColumnDefinitions.Add(new ColumnDefinition());
                area.Children.Add(DetailPanel(ctx, shown, look, areaW - legendW - (legendInArea ? 6 : 0), areaH - 2));
            }
            if (legendInArea)
            {
                area.ColumnDefinitions.Add(new ColumnDefinition { Width = details ? new GridLength(legendW) : new GridLength(1, GridUnitType.Star) });
                var key = Legend(ctx, look, legendW, areaH - 2, 2);
                key.Margin = new Thickness(details ? 6 : 0, 0, 0, 0);
                Grid.SetColumn(key, details ? 1 : 0);
                area.Children.Add(key);
            }
            grid.Children.Add(area);
        }
        return grid;
    }

    private static void AddPlaceholder(Grid grid, WidgetContext ctx, Look look, int row, string text, string category, double w, double h)
    {
        string hex = look.Colors.GetValueOrDefault(category, "#8E8270");
        var label = Theme.Label(text, Math.Clamp(Math.Min(h * 0.22, w * 0.2), 5.5, 9), Theme.Brush(hex));
        label.HorizontalAlignment = HorizontalAlignment.Center;
        var box = new Border { Margin = new Thickness(0.8), BorderBrush = Theme.Tint(hex, 0.45), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = label, ClipToBounds = true };
        Grid.SetRow(box, row - 1); Grid.SetColumn(box, 2);
        grid.Children.Add(box);
    }

    private static void AddRowLabel(Grid grid, WidgetContext ctx, Look look, int row, string text, string category, double cellH)
    {
        string hex = look.Colors.GetValueOrDefault(category, "#8E8270");
        var label = Theme.Label(text, Math.Clamp(cellH * 0.26, 7, 10.5), Theme.Brush(hex));
        label.HorizontalAlignment = HorizontalAlignment.Right; label.Margin = new Thickness(0, 0, 6, 0);
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetRow(label, row - 1); Grid.SetColumn(label, 0); Grid.SetColumnSpan(label, 2);
        label.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(label);
    }

    // ---------- Legend ----------

    /// <summary>The legend as wrapped chips, for the gap between the table and the f-block.</summary>
    private static FrameworkElement LegendStrip(WidgetContext ctx, Look look, double width, double height)
    {
        double font = Math.Clamp(height / 2 * 0.62, 7.5, 10.5);
        var wrap = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        foreach (var category in PeriodicTableLogic.Categories)
        {
            string hex = look.Colors.GetValueOrDefault(category.Id, category.Color);
            bool on = look.Highlight == category.Id;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(7, 0, 7, 0), Background = Brushes.Transparent };
            row.Children.Add(new Border { Width = font * 0.85, Height = font * 0.85, CornerRadius = new CornerRadius(2.5), Background = Theme.Brush(hex), Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
            var name = Theme.Label(category.Name, font, on ? ctx.TextBrush : Theme.Text2);
            name.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            row.Children.Add(name);
            WidgetKit.Clickable(row, () => SetHighlight(ctx, category.Id), on ? "Click to show every category again" : "Click to highlight " + category.Name);
            wrap.Children.Add(row);
        }
        return new Border { Child = wrap, MaxHeight = height, ClipToBounds = true };
    }

    private static FrameworkElement Legend(WidgetContext ctx, Look look, double w, double h, int columns)
    {
        int perColumn = (int)Math.Ceiling(PeriodicTableLogic.Categories.Length / (double)columns);
        double rowH = Math.Max(11, h / perColumn);
        double font = Math.Clamp(rowH * 0.62, 7.5, 11.5);
        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center };
        for (int c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int r = 0; r < perColumn; r++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(rowH) });
        for (int i = 0; i < PeriodicTableLogic.Categories.Length; i++)
        {
            var category = PeriodicTableLogic.Categories[i];
            string hex = look.Colors.GetValueOrDefault(category.Id, category.Color);
            bool on = look.Highlight == category.Id;
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent };
            var swatch = new Border { Width = Math.Max(7, font * 0.85), Height = Math.Max(7, font * 0.85), CornerRadius = new CornerRadius(2.5), Background = Theme.Brush(hex), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            var name = Theme.Label(category.Name, font, on ? ctx.TextBrush : Theme.Text2);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            row.Children.Add(swatch);
            row.Children.Add(name);
            WidgetKit.Clickable(row, () => SetHighlight(ctx, category.Id), on ? "Click to show every category again" : "Click to highlight " + category.Name);
            Grid.SetColumn(row, i / perColumn);
            Grid.SetRow(row, i % perColumn);
            grid.Children.Add(row);
        }
        return grid;
    }

    // ---------- Details ----------

    private static IEnumerable<(string Text, bool Strong, string? Hex)> DetailLines(WidgetContext ctx, PeriodicTableLogic.Element e, Look look, int lines)
    {
        var all = new List<(string, bool, string?)>
        {
            (e.Name, true, null),
            ($"No. {e.Number}  ·  {PeriodicTableLogic.MassText(e, Math.Max(2, look.Decimals))} u", false, null),
            ($"{PeriodicTableLogic.CategoryName(e.Category)}", false, ColorOf(look, e)),
            ($"{PeriodicTableLogic.GroupText(e)}  ·  Period {e.Period}  ·  {PeriodicTableLogic.Block(e)}-block", false, null),
        };
        if (ctx.GetBool("showConfig")) all.Add((PeriodicTableLogic.ElectronConfiguration(e.Number), false, null));
        return all.Take(Math.Max(2, lines));
    }

    /// <summary>The selected element: a big tile and a few lines of facts, scaled down to fit the space it gets.</summary>
    private static FrameworkElement DetailPanel(WidgetContext ctx, PeriodicTableLogic.Element e, Look look, double w, double h)
    {
        string hex = ColorOf(look, e);
        var grid = new Grid { Margin = new Thickness(8, 5, 8, 5) };
        double tileSize = Math.Clamp(Math.Min(h - 12, w * 0.26), 30, 70);
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var tileText = new Grid();
        tileText.Children.Add(new TextBlock { Text = e.Symbol, FontFamily = Theme.UiFont, FontSize = tileSize * 0.42, FontWeight = FontWeights.SemiBold, Foreground = ctx.TextBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, tileSize * 0.08, 0, 0) });
        tileText.Children.Add(new TextBlock { Text = e.Number.ToString(CultureInfo.InvariantCulture), FontFamily = Theme.UiFont, FontSize = Math.Max(7, tileSize * 0.16), Foreground = Theme.Text2, Margin = new Thickness(4, 2, 0, 0) });
        var tile = new Border { Width = tileSize, Height = tileSize, CornerRadius = new CornerRadius(8), Background = Theme.Tint(hex, 0.3), BorderBrush = Theme.Tint(hex, 0.7), BorderThickness = new Thickness(1), Child = tileText, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(tile);

        int lines = h >= 92 ? 5 : h >= 74 ? 4 : h >= 58 ? 3 : 2;
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var (text, strong, color) in DetailLines(ctx, e, look, lines))
        {
            var line = new TextBlock { Text = text, FontFamily = Theme.UiFont, FontSize = strong ? 16 : 11.5, FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal, Foreground = color is not null ? Theme.Brush(color) : strong ? ctx.TextBrush : Theme.Text2, Margin = new Thickness(0, strong ? 0 : 1, 0, 0) };
            stack.Children.Add(line);
        }
        var fit = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left, Child = stack, Margin = new Thickness(10, 0, 0, 0) };
        Grid.SetColumn(fit, 1);
        grid.Children.Add(fit);
        return new Border { CornerRadius = new CornerRadius(10), Background = Theme.Tint(hex, 0.1), BorderBrush = Theme.Tint(hex, 0.3), BorderThickness = new Thickness(1), Child = grid, ClipToBounds = true };
    }

    // ---------- Element of the day (and the fallback for small sizes) ----------

    private static FrameworkElement DayCard(WidgetContext ctx, PeriodicTableLogic.Element e, Look look, double innerW, double innerH, bool asDay)
    {
        string hex = ColorOf(look, e);
        bool horizontal = innerW / innerH > 1.15;
        bool miniTable = horizontal && innerW >= 430;
        double tileSize = horizontal ? Math.Min(innerH - 8, innerW * (miniTable ? 0.27 : 0.36)) : Math.Min(innerW * 0.5, innerH * 0.42);
        tileSize = Math.Clamp(tileSize, 56, 210);

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition());
        if (miniTable) root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (!horizontal) { root.ColumnDefinitions.Clear(); root.ColumnDefinitions.Add(new ColumnDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); }

        var tileText = new Grid();
        tileText.Children.Add(new TextBlock { Text = e.Symbol, FontFamily = Theme.UiFont, FontSize = tileSize * 0.44, FontWeight = FontWeights.SemiBold, Foreground = ctx.TextBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, tileSize * 0.06, 0, 0) });
        tileText.Children.Add(new TextBlock { Text = e.Number.ToString(CultureInfo.InvariantCulture), FontFamily = Theme.UiFont, FontSize = tileSize * 0.15, Foreground = Theme.Text2, Margin = new Thickness(8, 5, 0, 0) });
        tileText.Children.Add(new TextBlock { Text = PeriodicTableLogic.MassText(e, look.Decimals + 1), FontFamily = Theme.UiFont, FontSize = tileSize * 0.12, Foreground = Theme.Text2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 5) });
        var tile = new Border { Width = tileSize, Height = tileSize, CornerRadius = new CornerRadius(14), Background = Theme.Tint(hex, 0.3), BorderBrush = Theme.Tint(hex, 0.7), BorderThickness = new Thickness(1.2), Child = tileText, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        root.Children.Add(tile);

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        string heading = ctx.State("pt.sel", 0) == e.Number ? "SELECTED ELEMENT" : PeriodicTableLogic.Find(ctx.Get("defaultElement")) is not null ? "ELEMENT" : "ELEMENT OF THE DAY";
        stack.Children.Add(new TextBlock { Text = heading, FontFamily = Theme.UiFont, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Theme.Muted });
        stack.Children.Add(new TextBlock { Text = e.Name, FontFamily = Theme.UiFont, FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = ctx.TextBrush, Margin = new Thickness(0, 2, 0, 4) });
        stack.Children.Add(new TextBlock { Text = $"{e.Symbol}  ·  atomic number {e.Number}", FontFamily = Theme.UiFont, FontSize = 13, Foreground = Theme.Text2 });
        stack.Children.Add(new TextBlock { Text = $"Atomic mass {PeriodicTableLogic.MassText(e, Math.Max(2, look.Decimals))}", FontFamily = Theme.UiFont, FontSize = 13, Foreground = Theme.Text2, Margin = new Thickness(0, 2, 0, 0) });
        var pill = StudyKit.PillBox(ctx, PeriodicTableLogic.CategoryName(e.Category), hex, 11);
        pill.HorizontalAlignment = HorizontalAlignment.Left; pill.Margin = new Thickness(0, 8, 0, 6);
        stack.Children.Add(pill);
        stack.Children.Add(new TextBlock { Text = $"{PeriodicTableLogic.GroupText(e)}  ·  period {e.Period}  ·  {PeriodicTableLogic.Block(e)}-block", FontFamily = Theme.UiFont, FontSize = 12, Foreground = Theme.Muted });
        if (ctx.GetBool("showConfig"))
            stack.Children.Add(new TextBlock { Text = PeriodicTableLogic.ElectronConfiguration(e.Number), FontFamily = Theme.UiFont, FontSize = 14, Foreground = Theme.Text2, Margin = new Thickness(0, 6, 0, 0) });
        var fit = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = horizontal ? HorizontalAlignment.Left : HorizontalAlignment.Center, Child = stack, Margin = horizontal ? new Thickness(16, 0, 8, 0) : new Thickness(8, 8, 8, 0) };
        if (horizontal) Grid.SetColumn(fit, 1); else Grid.SetRow(fit, 1);
        root.Children.Add(fit);

        if (miniTable)
        {
            var mini = MiniTable(look, e, Math.Min((innerW * 0.3) / 18, (innerH - 8) / 9.6));
            Grid.SetColumn(mini, 2);
            mini.VerticalAlignment = VerticalAlignment.Center;
            root.Children.Add(mini);
        }
        // Click the card to step to the next element (handy for browsing).
        root.Background = Brushes.Transparent;
        root.ToolTip = "Click for the next element";
        WidgetKit.Clickable(root, () => ctx.SetState("pt.sel", e.Number % 118 + 1));
        return root;
    }

    /// <summary>A tiny 18 × 10 map of the table with one element lit, so you can see where it sits.</summary>
    private static FrameworkElement MiniTable(Look look, PeriodicTableLogic.Element e, double cell)
    {
        cell = Math.Max(3, cell);
        var canvas = new Canvas { Width = cell * 18, Height = cell * 9.6 };
        foreach (var other in PeriodicTableLogic.All)
        {
            double top = (other.Row <= 7 ? other.Row - 1 : other.Row - 1.6) * cell;
            bool me = other.Number == e.Number;
            var square = new Rectangle
            {
                Width = cell - 1, Height = cell - 1, RadiusX = 1.5, RadiusY = 1.5,
                Fill = me ? Theme.Brush(ColorOf(look, other)) : Theme.Tint(ColorOf(look, other), 0.28),
                Stroke = me ? Theme.Text : null, StrokeThickness = me ? 1.4 : 0,
            };
            Canvas.SetLeft(square, (other.Column - 1) * cell);
            Canvas.SetTop(square, top);
            canvas.Children.Add(square);
        }
        return canvas;
    }

    // ---------- One category ----------

    private static FrameworkElement CategoryView(WidgetContext ctx, PeriodicTableLogic.Element shown, Look look, double innerW, double innerH)
    {
        string id = look.Highlight == "none" ? shown.Category : look.Highlight;
        string hex = look.Colors.GetValueOrDefault(id, "#8E8270");
        var elements = PeriodicTableLogic.All.Where(e => e.Category == id).ToList();
        bool withDetails = ctx.GetBool("showDetails") && innerW >= 430 && innerH >= 150;

        var root = new DockPanel();
        // Category chips along the top.
        var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        foreach (var category in PeriodicTableLogic.Categories)
        {
            string ch = look.Colors.GetValueOrDefault(category.Id, category.Color);
            var chip = StudyKit.Chip(ctx, category.Name, ch, category.Id == id, () => ctx.SetState<string?>("pt.hl", category.Id), null, innerW < 560 ? 9.5 : 10.5);
            chip.Margin = new Thickness(0, 0, 4, 4);
            chips.Children.Add(chip);
        }
        DockPanel.SetDock(chips, Dock.Top);
        if (ctx.GetBool("showLegend")) root.Children.Add(chips);

        double listW = withDetails ? innerW * 0.6 : innerW;
        if (withDetails)
        {
            var panel = DetailPanel(ctx, elements.Contains(shown) ? shown : elements[0], look, innerW - listW - 8, Math.Min(innerH, 110));
            panel.Width = innerW - listW - 8;
            panel.VerticalAlignment = VerticalAlignment.Top;
            DockPanel.SetDock(panel, Dock.Right);
            root.Children.Add(panel);
        }

        double tileW = Math.Clamp(listW / Math.Ceiling(listW / 62), 52, 80);
        var wrap = new WrapPanel();
        foreach (var e in elements)
        {
            var tile = Cell(ctx, e, look, tileW - 4, 46, e.Number == shown.Number && ctx.State("pt.sel", 0) == e.Number, false);
            tile.Width = tileW - 4; tile.Height = 46; tile.Margin = new Thickness(2);
            wrap.Children.Add(tile);
        }
        var scroll = StudyKit.ThinScroll(wrap);
        scroll.Margin = new Thickness(0, 0, withDetails ? 8 : 0, 0);
        var title = Theme.Label($"{PeriodicTableLogic.CategoryName(id)}  ·  {elements.Count} elements", ctx.Font(11), Theme.Brush(hex));
        title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(2, 0, 0, 4);
        DockPanel.SetDock(title, Dock.Top);
        root.Children.Add(title);
        root.Children.Add(scroll);
        return root;
    }
}
