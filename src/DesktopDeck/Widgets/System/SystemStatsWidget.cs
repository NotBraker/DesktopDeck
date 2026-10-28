using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DesktopDeck.Models;

namespace DesktopDeck.Widgets;

/// <summary>
/// CPU and memory use as bars, rings or big numbers, each with a live history graph. Readings come from a sampler
/// shared by every copy of the widget (SystemSamplers.CpuMemory) and are only taken while the widget is on screen.
/// </summary>
internal sealed class SystemStatsWidget : IWidget
{
    private const double GiB = 1024d * 1024 * 1024;

    public string Kind => "system-stats";
    public string Name => "CPU & memory";
    public string Description => "Processor and memory use with a live history graph";
    public string Section => "System";
    public string Glyph => "";
    public string Accent => "#38bdf8";
    public (double Width, double Height) DefaultSize => (276, 228);
    public (double Width, double Height) MinimumSize => (180, 84);

    public IReadOnlyList<OptionSpec> Options { get; } =
    [
        OptionSpec.Toggle("showCpu", "Show CPU", true, "What to show"),
        OptionSpec.Toggle("showRam", "Show memory (RAM)", true, "What to show"),
        OptionSpec.Toggle("showCommit", "Show committed memory", false, "What to show", "RAM plus page file in use."),
        OptionSpec.Toggle("graph", "Show history graph", true, "What to show"),
        OptionSpec.Toggle("details", "Show amounts", true, "What to show", "Memory in GB next to the percentage."),
        OptionSpec.Toggle("showPeak", "Show peak", false, "What to show", "The highest reading in the history."),
        OptionSpec.Toggle("icons", "Show icons", true, "What to show"),
        OptionSpec.Choice("style", "Style", "bars", ["bars", "rings", "numbers"], ["Bars", "Rings", "Big numbers"], "Layout"),
        OptionSpec.Choice("graphStyle", "Graph style", "area", ["area", "line"], ["Filled", "Line only"], "Layout"),
        OptionSpec.Number("decimals", "Decimal places", 0, 0, 1, "Layout"),
        OptionSpec.Number("thickness", "Bar / ring thickness", 6, 2, 16, "Layout"),
        OptionSpec.Color("cpuColor", "CPU color", "", "Colors", "Empty = accent color."),
        OptionSpec.Color("ramColor", "Memory color", "#a78bfa", "Colors"),
        OptionSpec.Color("commitColor", "Committed memory color", "#f472b6", "Colors"),
        OptionSpec.Toggle("levelColors", "Color by threshold", true, "Colors", "Bars turn amber, then red, as use climbs."),
        OptionSpec.Color("warnColor", "Warning color", "#f59e0b", "Colors"),
        OptionSpec.Color("critColor", "Critical color", "#ef4444", "Colors"),
        OptionSpec.Color("trackColor", "Track color", "", "Colors", "Empty = theme."),
        OptionSpec.Number("interval", "Update every (seconds)", 2, 1, 10, "Behavior"),
        OptionSpec.Number("history", "History length (readings)", 40, 10, 120, "Behavior"),
        OptionSpec.Number("warn", "Warning at (%)", 70, 5, 100, "Behavior"),
        OptionSpec.Number("critical", "Critical at (%)", 90, 5, 100, "Behavior"),
        OptionSpec.Toggle("smooth", "Smooth CPU", false, "Behavior", "Average the last three readings so the number jumps less."),
    ];

    public IEnumerable<Action<LauncherItem>> Samples =>
    [
        item => { item.Options["data.sample.cpu"] = "27"; item.Options["data.sample.ram"] = "61"; item.Options["data.sample.ramTotal"] = "15.9"; },
        item =>
        {
            item.Options["style"] = "rings"; item.Options["showCommit"] = "true"; item.Width = 372; item.Height = 228;
            item.Options["data.sample.cpu"] = "74"; item.Options["data.sample.ram"] = "48"; item.Options["data.sample.ramTotal"] = "31.7";
            item.Options["data.sample.commit"] = "93"; item.Options["data.sample.commitTotal"] = "36";
        },
        item =>
        {
            item.Options["style"] = "numbers"; item.Options["showPeak"] = "true"; item.Options["graphStyle"] = "line"; item.Width = 372; item.Height = 180;
            item.Options["data.sample.cpu"] = "12"; item.Options["data.sample.ram"] = "83"; item.Options["data.sample.ramTotal"] = "7.8";
        },
    ];

    private sealed record Metric(string Key, string Label, string Short, string Glyph, string ColorKey);

    private static readonly Metric[] Metrics =
    [
        new("cpu", "CPU", "CPU", "", "cpuColor"),
        new("ram", "Memory", "RAM", "", "ramColor"),
        new("commit", "Committed", "Commit", "", "commitColor"),
    ];

    private sealed record Reading(double Percent, double Used, double Total, IReadOnlyList<double> History, bool Known);

    public FrameworkElement Build(WidgetContext ctx)
    {
        bool header = SystemKit.Header(ctx, 120);
        var (card, body) = SystemKit.Frame(ctx, header);
        var shown = Metrics.Where(m => ctx.GetBool(m.Key switch { "cpu" => "showCpu", "ram" => "showRam", _ => "showCommit" })).ToList();
        if (shown.Count == 0) { body.Children.Add(WidgetKit.Empty(ctx, "Pick what to show in Customize…")); return card; }

        int interval = ctx.GetInt("interval"), historyLength = ctx.GetInt("history");
        SystemSamplers.CpuMemory? sampler = ctx.IsSnapshot ? null : SystemSamplers.Shared($"cpumem/{ctx.Item.Id}/{interval}", () => new SystemSamplers.CpuMemory(interval));
        Reading Read(string key)
        {
            if (sampler is null)
            {
                double value = SystemKit.SampleNumber(ctx, key, key == "cpu" ? 23 : key == "ram" ? 58 : 41);
                double total = SystemKit.SampleNumber(ctx, key + "Total", key == "ram" ? 16 : 24) * GiB;
                var fake = SystemStatsLogic.FakeSeries(historyLength, value, key == "cpu" ? Math.Min(18, value * 0.6 + 4) : 2.5, key.Length * 7).Select(v => Math.Clamp(v, 0, 100)).ToArray();
                if (fake.Length > 0) fake[^1] = value;
                return new Reading(value, total * value / 100, total, fake, true);
            }
            return key switch
            {
                "cpu" => new Reading(ctx.GetBool("smooth") ? SystemStatsLogic.Average(sampler.CpuHistory, 3) : sampler.Cpu, 0, 0, sampler.CpuHistory, sampler.HasCpu),
                "ram" => new Reading(sampler.RamPercent, sampler.RamUsed, sampler.RamTotal, sampler.RamHistory, true),
                _ => new Reading(sampler.CommitPercent, sampler.CommitUsed, sampler.CommitTotal, sampler.CommitHistory, true),
            };
        }

        int decimals = ctx.GetInt("decimals");
        double warn = ctx.GetNumber("warn"), critical = ctx.GetNumber("critical"), thickness = ctx.GetNumber("thickness");
        bool graph = ctx.GetBool("graph"), details = ctx.GetBool("details"), peak = ctx.GetBool("showPeak"), icons = ctx.GetBool("icons"), levels = ctx.GetBool("levelColors");
        var track = ctx.GetColor("trackColor", Theme.Surface3);
        var room = SystemKit.Room(ctx, header);
        bool narrow = room.Width / (ctx.Get("style") == "bars" ? 1 : shown.Count) < 150;
        Brush Base(Metric m) => ctx.GetColor(m.ColorKey, m.Key == "cpu" ? ctx.Accent : Theme.Brush(m.Key == "ram" ? "#a78bfa" : "#f472b6"));
        Brush Fill(Metric m, double percent) => levels ? SystemKit.LevelBrush(ctx, SystemStatsLogic.Level(percent, warn, critical), Base(m)) : Base(m);
        string Value(Reading r) => r.Known ? SystemStatsLogic.Percent(r.Percent, decimals) : "…";
        string Detail(Metric m, Reading r)
        {
            var parts = new List<string>();
            if (details && m.Key != "cpu" && r.Total > 0) parts.Add(SystemStatsLogic.UsedOfTotal(r.Used, r.Total));
            if (peak && r.History.Count > 0) parts.Add("peak " + SystemStatsLogic.Percent(r.History.Max(), 0));
            return string.Join(" · ", parts);
        }
        SystemKit.Sparkline Graph(Metric m) => new()
        {
            Capacity = historyLength, Max = 100, Stroke = Base(m), Area = ctx.Get("graphStyle") == "area",
            Thickness = 1.6, Margin = new Thickness(0, 4, 0, 0),
        };

        var updates = new List<Action>();
        string style = ctx.Get("style");
        if (style == "rings" || style == "numbers")
        {
            var grid = new UniformGrid { Rows = 1, Columns = shown.Count };
            double colWidth = room.Width / shown.Count;
            foreach (var m in shown)
            {
                var column = new DockPanel { Margin = new Thickness(shown.Count > 1 ? 4 : 0, 0, shown.Count > 1 ? 4 : 0, 0), LastChildFill = true };
                var label = WidgetKit.Text(ctx, narrow ? m.Short : m.Label, 11.5, Theme.Text2);
                var labelRow = SystemKit.IconText(ctx, m.Glyph, Base(m), label, icons, 12);
                labelRow.HorizontalAlignment = style == "rings" ? HorizontalAlignment.Center : HorizontalAlignment.Left;
                var detail = WidgetKit.Muted(ctx, "", 10.5);
                detail.HorizontalAlignment = labelRow.HorizontalAlignment;
                bool showDetail = (details && m.Key != "cpu") || peak;
                if (style == "rings")
                {
                    double labels = ctx.Font(11.5) * 1.5 + (showDetail ? ctx.Font(10.5) * 1.45 : 0);
                    double graphHeight = graph && room.Height - labels > 118 ? Math.Min(34, room.Height - labels - 100) : 0;
                    double size = Math.Max(34, Math.Min(colWidth - 12, room.Height - labels - graphHeight - 6));
                    if (size < 52) { showDetail = false; size = Math.Max(34, Math.Min(colWidth - 12, room.Height - ctx.Font(11.5) * 1.5 - 4)); }
                    var holder = SystemKit.Holder();
                    holder.HorizontalAlignment = HorizontalAlignment.Center;
                    var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    stack.Children.Add(holder);
                    labelRow.Margin = new Thickness(0, 5, 0, 0);
                    stack.Children.Add(labelRow);
                    if (showDetail) stack.Children.Add(detail);
                    SystemKit.Sparkline? spark = null;
                    if (graphHeight >= 16)
                    {
                        spark = Graph(m);
                        spark.Height = graphHeight;
                        spark.Margin = new Thickness(2, 6, 2, 0);
                        stack.Children.Add(spark);
                    }
                    column.Children.Add(stack);
                    updates.Add(() =>
                    {
                        var r = Read(m.Key);
                        var center = WidgetKit.Text(ctx, Value(r), Math.Max(10, size * 0.22) / ctx.TextScale, bold: true);
                        center.HorizontalAlignment = HorizontalAlignment.Center;
                        holder.Child = WidgetKit.Ring(r.Percent / 100, Fill(m, r.Percent), size, Math.Min(thickness, size / 5), center, track);
                        detail.Text = Detail(m, r);
                        spark?.Set(r.History);
                    });
                }
                else
                {
                    DockPanel.SetDock(labelRow, Dock.Top);
                    column.Children.Add(labelRow);
                    double bigSize = Math.Clamp(Math.Min(room.Height * 0.3, colWidth * 0.3), 17, 40);
                    var big = WidgetKit.Text(ctx, "", bigSize, bold: true);
                    big.Margin = new Thickness(0, 1, 0, 0);
                    DockPanel.SetDock(big, Dock.Top);
                    column.Children.Add(big);
                    if (showDetail && room.Height >= 80) { DockPanel.SetDock(detail, Dock.Top); column.Children.Add(detail); }
                    double rest = room.Height - ctx.Font(11.5) * 1.5 - ctx.Font(bigSize) * 1.35 - (showDetail ? ctx.Font(10.5) * 1.45 : 0);
                    SystemKit.Sparkline? spark = null;
                    if (graph && rest >= 18)
                    {
                        spark = Graph(m);
                        spark.Height = Math.Min(70, rest);
                        spark.VerticalAlignment = VerticalAlignment.Bottom;
                        column.Children.Add(spark);
                    }
                    else column.Children.Add(new Border());
                    updates.Add(() =>
                    {
                        var r = Read(m.Key);
                        big.Text = Value(r);
                        big.Foreground = levels && r.Known && SystemStatsLogic.Level(r.Percent, warn, critical) > 0 ? Fill(m, r.Percent) : ctx.TextBrush;
                        detail.Text = Detail(m, r);
                        spark?.Set(r.History);
                    });
                }
                grid.Children.Add(column);
            }
            body.Children.Add(grid);
        }
        else
        {
            var list = new Grid();
            double rowHeight = room.Height / shown.Count;
            bool compact = rowHeight < 30;
            foreach (var m in shown)
            {
                list.RowDefinitions.Add(new RowDefinition());
                var row = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, shown.IndexOf(m) < shown.Count - 1 ? (compact ? 2 : 8) : 0) };
                Grid.SetRow(row, list.RowDefinitions.Count - 1);
                var label = WidgetKit.Text(ctx, narrow || compact ? m.Short : m.Label, 12);
                var value = WidgetKit.Text(ctx, "", 12.5, bold: true);
                var detail = WidgetKit.Muted(ctx, "", 11);
                var bar = WidgetKit.Bar(0, Base(m), Math.Min(thickness, compact ? 5 : thickness), track);
                SystemKit.Sparkline? spark = null;
                if (compact)
                {
                    var line = new Grid { VerticalAlignment = VerticalAlignment.Center };
                    line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ctx.Font(12) * 3.9 + (icons ? ctx.Font(12) + 7 : 0)) });
                    line.ColumnDefinitions.Add(new ColumnDefinition());
                    line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ctx.Font(12.5) * 3.1) });
                    line.Children.Add(SystemKit.IconText(ctx, m.Glyph, Base(m), label, icons, 12));
                    bar.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetColumn(bar, 1);
                    line.Children.Add(bar);
                    value.HorizontalAlignment = HorizontalAlignment.Right;
                    Grid.SetColumn(value, 2);
                    line.Children.Add(value);
                    row.Children.Add(line);
                }
                else
                {
                    var right = new StackPanel { Orientation = Orientation.Horizontal };
                    double room4Detail = room.Width - ctx.Font(12) * 6 - ctx.Font(12.5) * 3.2;
                    if ((details && m.Key != "cpu") || peak)
                        if (room4Detail > 60) { detail.Margin = new Thickness(0, 0, 8, 0); detail.MaxWidth = room4Detail; right.Children.Add(detail); }
                    right.Children.Add(value);
                    var head = SystemKit.Split(SystemKit.IconText(ctx, m.Glyph, Base(m), label, icons, 12.5), right);
                    head.Margin = new Thickness(0, 0, 0, 5);
                    DockPanel.SetDock(head, Dock.Top);
                    row.Children.Add(head);
                    DockPanel.SetDock(bar, Dock.Top);
                    row.Children.Add(bar);
                    double graphHeight = rowHeight - ctx.Font(12.5) * 1.4 - 5 - thickness - (shown.IndexOf(m) < shown.Count - 1 ? 8 : 0) - 6;
                    if (graph && graphHeight >= 16)
                    {
                        spark = Graph(m);
                        spark.Margin = new Thickness(0, 6, 0, 0);
                        spark.Height = Math.Min(64, graphHeight);
                        spark.VerticalAlignment = VerticalAlignment.Top;
                        row.Children.Add(spark);
                    }
                    else row.Children.Add(new Border());
                }
                list.Children.Add(row);
                updates.Add(() =>
                {
                    var r = Read(m.Key);
                    value.Text = Value(r);
                    var fill = Fill(m, r.Percent);
                    value.Foreground = levels && r.Known && SystemStatsLogic.Level(r.Percent, warn, critical) > 0 ? fill : ctx.TextBrush;
                    detail.Text = Detail(m, r);
                    SystemKit.SetBar(bar, r.Known ? r.Percent / 100 : 0, fill);
                    spark?.Set(r.History);
                });
            }
            body.Children.Add(list);
        }

        void Apply() { foreach (var update in updates) update(); }
        Apply();
        if (sampler is not null)
        {
            card.ToolTip = "Updates every " + interval.ToString(CultureInfo.CurrentCulture) + (interval == 1 ? " second" : " seconds") + " while on screen.";
            int version = sampler.Version;
            ctx.Tick(() => true, now =>
            {
                sampler.Update(now, historyLength);
                if (sampler.Version == version) return;
                version = sampler.Version;
                Apply();
            });
        }
        return card;
    }
}

