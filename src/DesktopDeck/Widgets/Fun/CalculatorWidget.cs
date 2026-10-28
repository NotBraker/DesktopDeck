using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesktopDeck.Models;

namespace DesktopDeck.Widgets;

internal sealed class CalculatorWidget : IWidget
{
    private enum KeyKind { Digit, Operator, Equals, Clear, Memory, Function }

    public string Kind => "calculator";
    public string Name => "Calculator";
    public string Description => "A tap-friendly calculator with memory and a history of your sums";
    public string Section => "Fun";
    public string Glyph => "";
    public string Accent => "#5E9BA6";
    public (double Width, double Height) DefaultSize => (276, 372);
    public (double Width, double Height) MinimumSize => (228, 276);

    public IReadOnlyList<OptionSpec> Options { get; } =
    [
        OptionSpec.Toggle("history", "Show the history", true, "What to show"),
        OptionSpec.Number("tapeLines", "History lines shown", 2, 1, 6, "What to show", "Fewer are shown when the card is short."),
        OptionSpec.Number("historyCount", "Calculations to remember", 20, 1, 50, "What to show"),
        OptionSpec.Toggle("memory", "Show memory keys (MC MR M+ M−)", true, "What to show"),
        OptionSpec.Toggle("copy", "Show copy button", true, "What to show"),
        OptionSpec.Choice("layout", "Keypad layout", "standard", ["standard", "compact"], ["Standard", "Compact"], "Layout", "Compact uses smaller keys and a slimmer display."),
        OptionSpec.Choice("buttonStyle", "Key style", "soft", ["soft", "outline", "plain"], ["Filled", "Outlined", "Plain"], "Layout"),
        OptionSpec.Choice("keyShape", "Key shape", "rounded", ["rounded", "pill", "square"], ["Rounded", "Pill", "Square"], "Layout"),
        OptionSpec.Number("keySize", "Largest key height", 44, 24, 64, "Layout", "Keys shrink to fit a short card."),
        OptionSpec.Number("keyText", "Key text size (%)", 100, 70, 150, "Layout"),
        OptionSpec.Number("keyGap", "Space between keys", 3, 0, 8, "Layout"),
        OptionSpec.Number("decimals", "Decimal places (−1 = automatic)", -1, -1, 10, "Layout"),
        OptionSpec.Toggle("thousands", "Thousands separator", true, "Layout"),
        OptionSpec.Color("operatorColor", "Operator keys color", "", "Colors", "Empty = the accent."),
        OptionSpec.Color("equalsColor", "Equals key color", "", "Colors", "Empty = the accent."),
        OptionSpec.Color("displayColor", "Display color", "", "Colors", "Empty = the text color."),
        OptionSpec.Toggle("tapeRecall", "Tap a history line to reuse its result", true, "Behavior"),
    ];

    public IEnumerable<Action<LauncherItem>> Samples =>
    [
        item =>
        {
            item.Options["data.state"] = CalculatorLogic.Encode(CalculatorLogic.Run(CalculatorLogic.Initial, "1", "2", "5", "*", "8", "=", "M+"));
            item.Options["data.tape"] = "125|*|8|1000\n48|/|6|8";
        },
        item =>
        {
            item.Options["layout"] = "compact";
            item.Options["buttonStyle"] = "outline";
            item.Options["keyShape"] = "pill";
            item.Options["data.state"] = CalculatorLogic.Encode(CalculatorLogic.Run(CalculatorLogic.Initial, "2", "5", "0", "+", "1", "5", "%", "="));
            item.Options["data.tape"] = "250|+|37.5|287.5";
            item.Accent = "#C2A14D";
        },
        item =>
        {
            item.Options["buttonStyle"] = "plain";
            item.Options["keyShape"] = "square";
            item.Options["operatorColor"] = "#8b5cf6";
            item.Options["equalsColor"] = "#22c55e";
            item.Options["decimals"] = "2";
            item.Options["data.state"] = CalculatorLogic.Encode(CalculatorLogic.Run(CalculatorLogic.Initial, "1", "2", "3", "4", "5", "+", "6", "7", ".", "8"));
            item.Options["data.tape"] = "1234.5|-|0.25|1234.25\n19.99|*|3|59.97";
            item.Width = 228; item.Height = 276;
        },
    ];

    // ---------- Engine glue ----------

    private static CalculatorLogic.State State(WidgetContext ctx) => CalculatorLogic.Decode(ctx.Get("data.state"));

    private static NumberFormatInfo Format => CultureInfo.CurrentCulture.NumberFormat;

    private static void Save(WidgetContext ctx, CalculatorLogic.State state)
    {
        ctx.SetState("calculator.undo", ctx.Get("data.state"), false);
        ctx.Set("data.state", CalculatorLogic.Encode(state));
    }

    private static void Press(WidgetContext ctx, string key)
    {
        var update = CalculatorLogic.Press(State(ctx), key);
        if (update.Done is { } done)
            ctx.Set("data.tape", CalculatorLogic.PushHistory(ctx.Get("data.tape"), done, ctx.GetInt("historyCount")), false);
        Save(ctx, update.State);
    }

    private static void Copy(WidgetContext ctx)
    {
        string text = CalculatorLogic.PlainResult(State(ctx), ctx.GetInt("decimals"), Format);
        if (text.Length == 0) { ctx.Status("There is no result to copy."); return; }
        try
        {
            Clipboard.SetText(text);
            ctx.Status("Result copied: " + text);
        }
        catch (ExternalException) { ctx.Status("The clipboard is busy; try again."); }
    }

    public IEnumerable<WidgetAction> Actions(WidgetContext ctx)
    {
        yield return new WidgetAction("Copy result", "", () => Copy(ctx));
        if (ctx.State<string?>("calculator.undo", null) is { } previous)
            yield return new WidgetAction("Undo last key", "", () => { ctx.SetState<string?>("calculator.undo", null, false); ctx.Set("data.state", previous); });
        yield return new WidgetAction("Clear history", "", () => ctx.Set("data.tape", ""));
    }

    // ---------- Drawing ----------

    private static Border MakeKey(WidgetContext ctx, string label, KeyKind kind, bool active, double fontSize, double radius, Action click)
    {
        string style = ctx.Get("buttonStyle");
        string opHex = ctx.Get("operatorColor").Trim().Length > 0 ? ctx.Get("operatorColor") : ctx.Item.Accent;
        string eqHex = ctx.Get("equalsColor").Trim().Length > 0 ? ctx.Get("equalsColor") : ctx.Item.Accent;
        Brush fill = style == "soft" ? Theme.Surface3 : Brushes.Transparent;
        Brush edge = style == "outline" ? Theme.LineStrong : Brushes.Transparent;
        Brush ink = ctx.TextBrush;
        switch (kind)
        {
            case KeyKind.Operator:
                fill = active ? Theme.Accent(opHex) : style == "plain" ? Brushes.Transparent : Theme.Tint(opHex, style == "soft" ? 0.2 : 0.07);
                edge = style == "outline" || active ? Theme.Tint(opHex, active ? 1 : 0.55) : Brushes.Transparent;
                ink = active ? Theme.OnPrimary : Theme.Accent(opHex);
                break;
            case KeyKind.Equals:
                fill = Theme.Accent(eqHex);
                edge = fill;
                ink = Theme.OnPrimary;
                break;
            case KeyKind.Clear:
                ink = Theme.Danger;
                break;
            case KeyKind.Memory:
                ink = Theme.Text2;
                if (style == "soft") fill = Theme.Surface;
                break;
            case KeyKind.Function:
                ink = Theme.Text2;
                break;
        }
        var text = Theme.Label(label, ctx.Font(fontSize), ink);
        text.FontWeight = kind is KeyKind.Digit or KeyKind.Operator or KeyKind.Equals ? FontWeights.SemiBold : FontWeights.Medium;
        text.HorizontalAlignment = HorizontalAlignment.Center;
        var key = new Border
        {
            Background = fill, BorderBrush = edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(radius),
            Child = text, Cursor = Cursors.Hand, Focusable = false,
        };
        key.MouseEnter += (_, _) => key.Opacity = 0.86;
        key.MouseLeave += (_, _) => key.Opacity = 1;
        key.MouseLeftButtonDown += (_, e) => { key.Opacity = 0.6; e.Handled = true; };
        return WidgetKit.Clickable(key, () => { key.Opacity = 1; click(); });
    }

    public FrameworkElement Build(WidgetContext ctx)
    {
        var state = State(ctx);
        int decimals = ctx.GetInt("decimals");
        bool thousands = ctx.GetBool("thousands");
        bool compact = ctx.Get("layout") == "compact";
        bool memoryKeys = ctx.GetBool("memory");

        var actions = new List<FrameworkElement>();
        if (ctx.GetBool("copy")) actions.Add(WidgetKit.IconButton("", "Copy result", () => Copy(ctx)));
        var (card, body) = WidgetKit.Frame(ctx, state.HasMemory ? WidgetKit.Pill(ctx, "M", ctx.Item.Accent) : null, actions.ToArray());

        // ----- Space budget -----
        double pad = ctx.GetNumber("_padding");
        double gap = ctx.GetNumber("keyGap");
        double displayHeight = (compact ? 54 : 66) * Math.Max(1, ctx.TextScale);
        double room = ctx.Height - pad * 2 - (ctx.Item.ShowHeader ? 34 : 0) - displayHeight - 6;
        double rowWeight = memoryKeys ? (compact ? 5.6 : 5.8) : 5;
        double minKey = compact ? 22 : 26;
        int tapeLines = 0;
        if (ctx.GetBool("history"))
        {
            int available = CalculatorLogic.ReadHistory(ctx.Get("data.tape")).Count;
            tapeLines = (int)Math.Clamp(Math.Floor((room - rowWeight * (minKey + gap * 2) - 8) / 14), 0, Math.Min(ctx.GetInt("tapeLines"), available));
        }

        // ----- Display -----
        string screen = CalculatorLogic.Screen(state, decimals, thousands, Format);
        var display = new StackPanel { Margin = new Thickness(2, 0, 2, 6), Height = displayHeight, VerticalAlignment = VerticalAlignment.Bottom };
        var expression = WidgetKit.Muted(ctx, CalculatorLogic.Expression(state, decimals, thousands, Format) is { Length: > 0 } line ? line : " ", 10.5);
        expression.TextAlignment = TextAlignment.Right;
        double width = ctx.Width - pad * 2 - 12;
        double baseSize = compact ? 28 : 34;
        double size = state.Error.Length > 0
            ? 14
            : Math.Clamp(width / ctx.TextScale / Math.Max(4, screen.Length) / 0.6, 15, baseSize);
        var main = WidgetKit.Text(ctx, screen, size, state.Error.Length > 0 ? Theme.Danger : ctx.GetColor("displayColor", ctx.TextBrush), true);
        main.TextAlignment = TextAlignment.Right;
        main.HorizontalAlignment = HorizontalAlignment.Right;
        main.ToolTip = screen;
        if (state.Error.Length > 0) { main.TextWrapping = TextWrapping.Wrap; main.TextTrimming = TextTrimming.None; }
        display.Children.Add(expression);
        display.Children.Add(main);
        if (ctx.GetBool("copy") && state.Error.Length == 0) WidgetKit.Clickable(display, () => Copy(ctx), "Click to copy the result");
        else display.Background = Brushes.Transparent;
        // Everything is anchored to the bottom (tape, keypad, display) so large cards leave their spare room under the title.
        DockPanel.SetDock(display, Dock.Bottom);

        // ----- Tape -----
        double tapeHeight = 0;
        if (tapeLines > 0)
        {
            var tape = new StackPanel { Margin = new Thickness(2, 6, 2, 0) };
            tape.Children.Add(new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(0, 0, 0, 3) });
            foreach (var calc in CalculatorLogic.ReadHistory(ctx.Get("data.tape")).Take(tapeLines))
            {
                var row = WidgetKit.Muted(ctx, CalculatorLogic.FormatCalculation(calc, decimals, thousands, Format), 10.5);
                row.TextAlignment = TextAlignment.Right;
                row.Margin = new Thickness(0, 1, 0, 1);
                var entry = calc;
                if (ctx.GetBool("tapeRecall")) WidgetKit.Clickable(row, () => Save(ctx, CalculatorLogic.Recall(State(ctx), entry.Result)), "Use this result");
                tape.Children.Add(row);
            }
            DockPanel.SetDock(tape, Dock.Bottom);
            body.Children.Add(tape);
            tapeHeight = tapeLines * 16 + 12;
        }

        // ----- Keypad -----
        var rows = new List<(string Key, string Label, KeyKind Kind)[]>();
        if (memoryKeys)
            rows.Add([("MC", "MC", KeyKind.Memory), ("MR", "MR", KeyKind.Memory), ("M+", "M+", KeyKind.Memory), ("M-", "M−", KeyKind.Memory)]);
        rows.Add([("C", "C", KeyKind.Clear), ("back", "⌫", KeyKind.Function), ("%", "%", KeyKind.Function), ("/", "÷", KeyKind.Operator)]);
        rows.Add([("7", "7", KeyKind.Digit), ("8", "8", KeyKind.Digit), ("9", "9", KeyKind.Digit), ("*", "×", KeyKind.Operator)]);
        rows.Add([("4", "4", KeyKind.Digit), ("5", "5", KeyKind.Digit), ("6", "6", KeyKind.Digit), ("-", "−", KeyKind.Operator)]);
        rows.Add([("1", "1", KeyKind.Digit), ("2", "2", KeyKind.Digit), ("3", "3", KeyKind.Digit), ("+", "+", KeyKind.Operator)]);
        rows.Add([("neg", "±", KeyKind.Function), ("0", "0", KeyKind.Digit), (".", Format.NumberDecimalSeparator, KeyKind.Digit), ("=", "=", KeyKind.Equals)]);

        double keyArea = Math.Max(60, room - tapeHeight + gap);
        double tallest = ctx.GetNumber("keySize") * (compact ? 0.85 : 1);
        double keyRowsMax = rows.Count * (tallest + gap) - (memoryKeys ? tallest * 0.4 : 0);
        var pad4 = new Grid { Margin = new Thickness(-gap / 2), Height = Math.Min(keyArea, keyRowsMax), VerticalAlignment = VerticalAlignment.Bottom };
        for (int c = 0; c < 4; c++) pad4.ColumnDefinitions.Add(new ColumnDefinition());
        for (int r = 0; r < rows.Count; r++)
        {
            bool slim = memoryKeys && r == 0;
            pad4.RowDefinitions.Add(new RowDefinition { Height = new GridLength(slim ? 0.8 : 1, GridUnitType.Star), MaxHeight = tallest * (slim ? 0.6 : 1) + gap });
            for (int c = 0; c < 4; c++)
            {
                var (key, label, kind) = rows[r][c];
                double fontSize = kind == KeyKind.Memory ? 10.5 : (compact ? 13 : 15) * ctx.GetNumber("keyText") / 100;
                double radius = ctx.Get("keyShape") switch { "pill" => 20, "square" => 3, _ => compact ? 6 : 9 };
                bool active = kind == KeyKind.Operator && state.Op == key && !state.Entered;
                string pressed = key;
                var element = MakeKey(ctx, label, kind, active, fontSize, radius, () => Press(ctx, pressed));
                element.Margin = new Thickness(gap / 2);
                element.MinHeight = 0;
                Grid.SetRow(element, r);
                Grid.SetColumn(element, c);
                pad4.Children.Add(element);
            }
        }
        DockPanel.SetDock(pad4, Dock.Bottom);
        body.Children.Add(pad4);
        body.Children.Add(display);
        body.Children.Add(new Border()); // fills the space above the display
        return card;
    }
}
