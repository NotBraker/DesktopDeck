using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DesktopDeck.Models;

namespace DesktopDeck.Widgets;

internal sealed class UnitConverterWidget : IWidget
{
    public string Kind => "unit-converter";
    public string Name => "Unit converter";
    public string Description => "Eight categories, quick unit switching and conversion tables";
    public string Section => "Fun";
    public string Glyph => "";
    public string Accent => "#60a5fa";
    public (double Width, double Height) DefaultSize => (324, 324);
    public (double Width, double Height) MinimumSize => (228, 180);
    public IReadOnlyList<OptionSpec> Options { get; } =
    [
        OptionSpec.Choice("category", "Category", "Length", UnitConverterLogic.Categories, group: "Units"),
        OptionSpec.Text("from", "From unit", "m", "Units", "Click a unit on the card to cycle through units in the selected category."),
        OptionSpec.Text("to", "To unit", "ft", "Units", "Use the abbreviations shown on the card. Invalid units reset to the category defaults."),
        OptionSpec.Number("value", "Value", 1, -1e12, 1e12, "Behavior"),
        OptionSpec.Number("step", "Step for quick buttons", 1, .000001, 1e9, "Behavior"),
        OptionSpec.Number("decimals", "Decimal places", 3, 0, 10, "Layout"),
        OptionSpec.Toggle("grouping", "Thousands separators", true, "Layout"),
        OptionSpec.Toggle("table", "Show conversion table", true, "What to show"),
        OptionSpec.Number("rows", "Rows in the table", 4, 1, 12, "What to show"),
        OptionSpec.Toggle("controls", "Show the quick buttons", true, "What to show"),
        OptionSpec.Color("resultColor", "Result color", "", "Colors"),
        OptionSpec.Toggle("showCategory", "Show category label", true, "What to show"),
    ];
    public IEnumerable<Action<LauncherItem>> Samples =>
    [
        i => { i.Options["value"] = "5"; },
        i => { i.Options["category"] = "Temperature"; i.Options["from"] = "C"; i.Options["to"] = "F"; i.Options["value"] = "21"; i.Accent = "#fb923c"; },
        i => { i.Options["category"] = "Data size"; i.Options["from"] = "GiB"; i.Options["to"] = "MB"; i.Options["value"] = "2"; },
    ];
    public FrameworkElement Build(WidgetContext ctx)
    {
        var units = UnitConverterLogic.In(ctx.Get("category"));
        if (units.Length == 0) units = UnitConverterLogic.In("Length");
        string from = units.Any(u => u.Id == ctx.Get("from")) ? ctx.Get("from") : units[0].Id;
        string to = units.Any(u => u.Id == ctx.Get("to")) ? ctx.Get("to") : units[1].Id;
        double value = ctx.GetNumber("value");
        string Format(double n) => n.ToString((ctx.GetBool("grouping") ? "N" : "F") + ctx.GetInt("decimals"), CultureInfo.CurrentCulture);
        void Cycle(string key, string current) => ctx.Set(key, units[(Array.FindIndex(units, u => u.Id == current) + 1) % units.Length].Id);
        void AskValue()
        {
            if (ctx.Ask("Convert a value", "Value in " + from, value.ToString(CultureInfo.CurrentCulture)) is not { } input) return;
            if (double.TryParse(input, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out double v) && double.IsFinite(v) && Math.Abs(v) <= 1e12) ctx.Set("value", v);
            else ctx.Inform("Enter a number between −1 trillion and 1 trillion.", "Unit converter");
        }
        var (card, body) = WidgetKit.Frame(ctx, actions: WidgetKit.IconButton("", "Swap units", () =>
        {
            ctx.Item.Options["from"] = to; ctx.Item.Options["to"] = from; ctx.Changed();
        }));
        var list = new StackPanel();
        if (ctx.GetBool("showCategory") && ctx.Height >= 228) list.Children.Add(WidgetKit.Muted(ctx, units[0].Category.ToUpperInvariant(), 10));
        var source = WidgetKit.Clickable(WidgetKit.Text(ctx, Format(value), 20), AskValue, "Click to enter a value");
        list.Children.Add(source);
        var switches = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 6) };
        switches.Children.Add(WidgetKit.Clickable(WidgetKit.Text(ctx, from + "  ›", 12), () => Cycle("from", from), "Next source unit"));
        switches.Children.Add(WidgetKit.Clickable(WidgetKit.Text(ctx, "   →   " + to + "  ›", 12), () => Cycle("to", to), "Next target unit"));
        list.Children.Add(switches);
        var result = WidgetKit.Text(ctx, Format(UnitConverterLogic.Convert(value, from, to)) + " " + to, 25, ctx.GetColor("resultColor"), true);
        result.ToolTip = result.Text;
        list.Children.Add(result);
        if (ctx.GetBool("controls") && ctx.Height >= 228)
        {
            var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 3) };
            foreach (int direction in new[] { -1, 1 })
            {
                int sign = direction;
                var button = WidgetKit.Button(sign < 0 ? "−" : "+", () => ctx.Set("value", Math.Clamp(value + sign * ctx.GetNumber("step"), -1e12, 1e12)));
                button.Margin = new Thickness(0, 0, 6, 0); controls.Children.Add(button);
            }
            list.Children.Add(controls);
        }
        if (ctx.GetBool("table") && ctx.Height >= 276)
        {
            list.Children.Add(WidgetKit.Divider());
            foreach (var unit in units.Where(u => u.Id != from && u.Id != to).Take(ctx.GetInt("rows")))
                list.Children.Add(WidgetKit.Muted(ctx, Format(UnitConverterLogic.Convert(value, from, unit.Id)) + " " + unit.Id));
        }
        body.Children.Add(WidgetKit.Scroll(list));
        return card;
    }
}
