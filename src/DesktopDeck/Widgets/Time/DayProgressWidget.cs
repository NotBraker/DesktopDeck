using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using DesktopDeck.Models;

namespace DesktopDeck.Widgets;

/// <summary>
/// Example plug-in widget (copy this pattern): progress through the day, week, month and year as bars or rings.
/// Pure math lives in DayProgressLogic.cs (unit tested in tests/DesktopDeck.WidgetTests/DayProgressTests.cs).
/// </summary>
internal sealed class DayProgressWidget : IWidget
{
    public string Kind => "day-progress";
    public string Name => "Day progress";
    public string Description => "How far through the day, week, month and year";
    public string Section => "Time";
    public string Glyph => "";
    public string Accent => "#14b8a6";
    public (double Width, double Height) DefaultSize => (276, 228);
    public (double Width, double Height) MinimumSize => (180, 132);

    public IReadOnlyList<OptionSpec> Options { get; } =
    [
        OptionSpec.Toggle("day", "Show today", true, "What to show"),
        OptionSpec.Toggle("week", "Show this week", true, "What to show"),
        OptionSpec.Toggle("month", "Show this month", true, "What to show"),
        OptionSpec.Toggle("year", "Show this year", true, "What to show"),
        OptionSpec.Choice("style", "Style", "bars", ["bars", "rings"], ["Bars", "Rings"], "Layout"),
        OptionSpec.Choice("detail", "Detail text", "percent", ["percent", "remaining", "both", "none"], ["Percent", "Time left", "Both", "None"], "Layout"),
        OptionSpec.Number("decimals", "Decimal places", 0, 0, 3, "Layout"),
        OptionSpec.Number("thickness", "Bar / ring thickness", 8, 2, 24, "Layout"),
        OptionSpec.Choice("weekStart", "Week starts on", "Monday", ["Monday", "Sunday", "Saturday"], null, "Behavior"),
        OptionSpec.Color("dayColor", "Today color", "", "Colors", "Empty = accent color."),
        OptionSpec.Color("weekColor", "Week color", "#6c63ff", "Colors"),
        OptionSpec.Color("monthColor", "Month color", "#f59e0b", "Colors"),
        OptionSpec.Color("yearColor", "Year color", "#ec4899", "Colors"),
        OptionSpec.Color("trackColor", "Track color", "", "Colors", "Empty = theme."),
    ];

    public IEnumerable<Action<LauncherItem>> Samples =>
    [
        _ => { },
        item => { item.Options["style"] = "rings"; item.Options["detail"] = "both"; item.Width = 468; item.Height = 180; },
    ];

    public FrameworkElement Build(WidgetContext ctx)
    {
        var (card, body) = WidgetKit.Frame(ctx);
        var now = DateTime.Now;
        var weekStart = Enum.TryParse<DayOfWeek>(ctx.Get("weekStart"), out var ws) ? ws : DayOfWeek.Monday;
        var rows = new (string Key, string Label, string ColorKey)[] { ("day", "Today", "dayColor"), ("week", "Week", "weekColor"), ("month", now.ToString("MMMM", CultureInfo.CurrentCulture), "monthColor"), ("year", now.Year.ToString(CultureInfo.CurrentCulture), "yearColor") }
            .Where(r => ctx.GetBool(r.Key)).ToList();
        if (rows.Count == 0) { body.Children.Add(WidgetKit.Empty(ctx, "Pick what to show in Customize…")); return card; }
        string detail = ctx.Get("detail");
        int decimals = ctx.GetInt("decimals");
        double thickness = ctx.GetNumber("thickness");
        var track = ctx.GetColor("trackColor", Theme.Surface3);
        string Text(string key)
        {
            double f = DayProgressLogic.Fraction(key, now, weekStart);
            string percent = (f * 100).ToString("F" + decimals, CultureInfo.CurrentCulture) + "%";
            string left = DayProgressLogic.Remaining(key, now, weekStart);
            return detail switch { "remaining" => left, "both" => $"{percent} · {left}", "none" => "", _ => percent };
        }

        double bodyHeight = Math.Max(1, ctx.Height - ctx.GetNumber("_padding") * 2 - (ctx.Item.ShowHeader ? 34 : 0));
        bool rings = ctx.Get("style") == "rings" && bodyHeight >= 90 * ctx.TextScale;
        int visibleRows = rings
            ? Math.Max(1, (int)((ctx.Width - ctx.GetNumber("_padding") * 2) / (85 * ctx.TextScale)))
            : Math.Max(1, (int)(bodyHeight / (ctx.Font(12.5) * 1.4 + 6 + thickness + 8)));
        rows = rows.Take(visibleRows).ToList();
        if (rings)
        {
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Columns = rows.Count };
            double size = Math.Max(40, Math.Min((ctx.Width - 40) / rows.Count - 10, ctx.Height - (ctx.Item.ShowHeader ? 90 : 60) - (detail is "remaining" or "both" ? 20 : 0)));
            foreach (var (key, label, colorKey) in rows)
            {
                var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                var center = WidgetKit.Text(ctx, ((int)Math.Round(DayProgressLogic.Fraction(key, now, weekStart) * 100)).ToString(CultureInfo.CurrentCulture) + "%", size * 0.22, bold: true);
                center.HorizontalAlignment = HorizontalAlignment.Center;
                stack.Children.Add(WidgetKit.Ring(DayProgressLogic.Fraction(key, now, weekStart), ctx.GetColor(colorKey), size, thickness, center, track));
                var name = WidgetKit.Muted(ctx, label);
                name.HorizontalAlignment = HorizontalAlignment.Center;
                name.Margin = new Thickness(0, 6, 0, 0);
                stack.Children.Add(name);
                if (detail is "remaining" or "both")
                {
                    var left = WidgetKit.Muted(ctx, DayProgressLogic.Remaining(key, now, weekStart), 10.5);
                    left.HorizontalAlignment = HorizontalAlignment.Center;
                    stack.Children.Add(left);
                }
                grid.Children.Add(stack);
            }
            body.Children.Add(grid);
        }
        else
        {
            var list = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            foreach (var (key, label, colorKey) in rows)
            {
                var head = new Grid { Margin = new Thickness(0, 2, 0, 4) };
                head.ColumnDefinitions.Add(new ColumnDefinition());
                head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                head.Children.Add(WidgetKit.Text(ctx, label, 12.5));
                var value = WidgetKit.Muted(ctx, Text(key), 11.5);
                Grid.SetColumn(value, 1);
                head.Children.Add(value);
                list.Children.Add(head);
                var bar = WidgetKit.Bar(DayProgressLogic.Fraction(key, now, weekStart), ctx.GetColor(colorKey), thickness, track);
                bar.Margin = new Thickness(0, 0, 0, 8);
                list.Children.Add(bar);
            }
            body.Children.Add(list);
        }
        // Percentages move slowly: once a minute is plenty.
        ctx.Tick(() => false, _ => ctx.Rebuild());
        return card;
    }
}
