using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using DesktopDeck.Models;

namespace DesktopDeck.Widgets;

internal sealed class DiceWidget : IWidget
{
    private static readonly string[] Modes = ["dice", "coin", "number", "pick", "yes-no"];
    private static readonly string[] ModeNames = ["Dice", "Coin", "Number", "Pick", "Y / N"];
    private static readonly string[] SideChoices = ["d4", "d6", "d8", "d10", "d12", "d20", "d100"];
    private const int HistoryStored = 60;

    public string Kind => "dice";
    public string Name => "Dice & random";
    public string Description => "Roll dice, flip a coin, draw a number, pick from your list or ask yes/no";
    public string Section => "Fun";
    public string Glyph => "";
    public string Accent => "#C4684F";
    public (double Width, double Height) DefaultSize => (276, 324);
    public (double Width, double Height) MinimumSize => (180, 180);

    public IReadOnlyList<OptionSpec> Options { get; } =
    [
        OptionSpec.Choice("mode", "Mode", "dice", Modes, ["Dice", "Coin flip", "Random number", "Pick from my list", "Yes / No"], "What to show"),
        OptionSpec.Number("count", "Number of dice", 2, 1, 6, "What to show"),
        OptionSpec.Choice("sides", "Die type", "d6", SideChoices, group: "What to show"),
        OptionSpec.Number("min", "Smallest number", 1, -1_000_000_000, 1_000_000_000, "What to show"),
        OptionSpec.Number("max", "Largest number", 100, -1_000_000_000, 1_000_000_000, "What to show"),
        OptionSpec.Lines("items", "Choices to pick from", "Take a walk\nRead a chapter\nSketch something\nMake a cup of tea", "What to show", "One choice per line."),
        OptionSpec.Text("headsLabel", "Coin side one", "Heads", "What to show"),
        OptionSpec.Text("tailsLabel", "Coin side two", "Tails", "What to show"),
        OptionSpec.Toggle("sum", "Show the total of several dice", true, "What to show"),
        OptionSpec.Number("history", "Past results to show", 4, 0, 20, "What to show", "0 hides the history."),
        OptionSpec.Toggle("tabs", "Show mode tabs", true, "Layout", "Needs a card at least 260 wide."),
        OptionSpec.Toggle("controls", "Show quick settings (die type, count, range)", true, "Layout", "Needs a taller card."),
        OptionSpec.Toggle("pips", "Draw dots on six-sided dice", true, "Layout"),
        OptionSpec.Number("dieSize", "Largest die size", 56, 28, 90, "Layout"),
        OptionSpec.Color("faceColor", "Die face color", "", "Colors", "Empty = ivory."),
        OptionSpec.Color("pipColor", "Dots and numbers on dice", "", "Colors", "Empty = dark."),
        OptionSpec.Color("coinColor", "Coin color", "#d4af37", "Colors"),
        OptionSpec.Color("resultColor", "Result text color", "", "Colors", "Empty = the text color."),
        OptionSpec.Color("yesColor", "\"Yes\" color", "#7fbf7f", "Colors"),
        OptionSpec.Color("noColor", "\"No\" color", "#e07a6a", "Colors"),
        OptionSpec.Toggle("noRepeat", "Pick without repeating until all are used", true, "Behavior"),
        OptionSpec.Toggle("maybe", "Let yes/no answer \"Maybe\" too", false, "Behavior"),
        OptionSpec.Toggle("animate", "Pop animation when rolling", true, "Behavior"),
    ];

    public IEnumerable<Action<LauncherItem>> Samples =>
    [
        item =>
        {
            item.Options["data.mode"] = "dice";
            item.Options["data.faces"] = "3,5";
            item.Options["data.history"] = string.Join("\n", DiceLogic.Tag("dice", "2d6: 3 + 5 = 8"), DiceLogic.Tag("dice", "2d6: 1 + 4 = 5"), DiceLogic.Tag("dice", "2d6: 6 + 2 = 8"));
        },
        item =>
        {
            item.Options["mode"] = "pick";
            item.Options["data.mode"] = "pick";
            item.Options["data.main"] = "Take a walk";
            item.Options["data.sub"] = "3 left this round";
            item.Options["data.history"] = string.Join("\n", DiceLogic.Tag("pick", "Take a walk"), DiceLogic.Tag("pick", "Read a chapter"));
            item.Accent = "#5E9BA6";
        },
        item =>
        {
            item.Options["mode"] = "dice";
            item.Options["sides"] = "d20";
            item.Options["count"] = "1";
            item.Options["data.mode"] = "dice";
            item.Options["data.faces"] = "17";
            item.Accent = "#8b5cf6";
        },
    ];

    // ---------- Rolling ----------

    private static int Sides(WidgetContext ctx) => DiceLogic.ParseSides(ctx.Get("sides"));

    private static string[] Choices(WidgetContext ctx)
        => ctx.GetLines("items").Take(200).Select(x => x[..Math.Min(150, x.Length)]).Distinct().ToArray();

    private static void Roll(WidgetContext ctx)
    {
        string mode = ctx.Get("mode");
        var random = Random.Shared;
        string main = "", sub = "", history;
        int[] faces = [];
        switch (mode)
        {
            case "coin":
                main = DiceLogic.Coin(random) ? Label(ctx, "headsLabel", "Heads") : Label(ctx, "tailsLabel", "Tails");
                history = main;
                break;
            case "number":
            {
                long min = (long)ctx.GetNumber("min"), max = (long)ctx.GetNumber("max");
                if (min > max) (min, max) = (max, min);
                main = DiceLogic.Number(random, min, max).ToString("N0", CultureInfo.CurrentCulture);
                sub = $"from {min.ToString("N0", CultureInfo.CurrentCulture)} to {max.ToString("N0", CultureInfo.CurrentCulture)}";
                history = main;
                break;
            }
            case "pick":
            {
                var items = Choices(ctx);
                var pick = DiceLogic.Pick(random, items, ctx.Get("data.used").Split('\n'), ctx.GetBool("noRepeat"));
                main = pick.Item ?? "Add some choices first";
                if (pick.Item is not null && ctx.GetBool("noRepeat"))
                {
                    int left = items.Length - pick.Used.Count;
                    sub = left > 0 ? $"{left} left this round" : "last one this round";
                }
                ctx.Set("data.used", string.Join("\n", pick.Used), false);
                history = main;
                break;
            }
            case "yes-no":
                main = DiceLogic.YesNo(random, ctx.GetBool("maybe"));
                history = main;
                break;
            default:
                faces = DiceLogic.Roll(random, ctx.GetInt("count"), Sides(ctx));
                history = DiceLogic.Describe(faces, Sides(ctx), ctx.GetBool("sum"));
                mode = "dice";
                break;
        }
        ctx.Set("data.mode", mode, false);
        ctx.Set("data.faces", DiceLogic.Encode(faces), false);
        ctx.Set("data.main", main, false);
        ctx.Set("data.sub", sub, false);
        ctx.Set("data.history", DiceLogic.PushHistory(ctx.Get("data.history"), DiceLogic.Tag(mode, history), HistoryStored), false);
        ctx.SetState("dice.pop", true, false);
        ctx.Changed();
    }

    private static string Label(WidgetContext ctx, string key, string fallback)
    {
        string text = ctx.Get(key).Trim();
        return text.Length == 0 ? fallback : text.Length > 24 ? text[..24] : text;
    }

    public IEnumerable<WidgetAction> Actions(WidgetContext ctx)
    {
        yield return new WidgetAction("Roll", "", () => Roll(ctx));
        yield return new WidgetAction("Set number range…", "", () => AskRange(ctx));
        yield return new WidgetAction("Edit choices…", "", () => AskChoices(ctx));
        yield return new WidgetAction("Clear history", "", () => ctx.Set("data.history", ""));
    }

    private static void AskRange(WidgetContext ctx)
    {
        if (ctx.Ask("Random number", "Smallest and largest, like 1 - 100", $"{ctx.GetInt("min")} - {ctx.GetInt("max")}") is not { } text) return;
        if (DiceLogic.ParseRange(text) is not { } range) { ctx.Inform("Type two whole numbers, like 1 - 100.", "Random number"); return; }
        ctx.Set("min", Math.Clamp(range.Min, -1_000_000_000, 1_000_000_000).ToString(CultureInfo.InvariantCulture), false);
        ctx.Set("max", Math.Clamp(range.Max, -1_000_000_000, 1_000_000_000).ToString(CultureInfo.InvariantCulture));
    }

    private static void AskChoices(WidgetContext ctx)
    {
        if (ctx.Ask("Choices", "One choice per line", ctx.Get("items"), multiline: true) is not { } text) return;
        ctx.Set("data.used", "", false);
        ctx.Set("items", text);
    }

    // ---------- Drawing ----------

    private static Border Chip(WidgetContext ctx, string text, bool selected, Action click, string tooltip)
    {
        // Chips shrink with a smaller text size but never grow, so the die row always fits the card.
        var label = Theme.Label(text, Math.Min(ctx.Font(11), 11), selected ? Theme.OnPrimary : Theme.Text2);
        label.FontWeight = FontWeights.SemiBold;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        var chip = new Border
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 2, 7, 3), Margin = new Thickness(0, 0, 4, 0), Child = label,
            Background = selected ? Theme.Accent(ctx.Item.Accent) : Theme.Surface3,
        };
        return WidgetKit.Clickable(chip, click, tooltip);
    }

    private static FrameworkElement Face(WidgetContext ctx, int face, int sides, double size)
    {
        var fill = ctx.GetColor("faceColor", Theme.Text);
        var ink = ctx.GetColor("pipColor", Theme.Surface);
        var outline = Theme.LineStrong;
        var grid = new Grid { Width = size, Height = size, Margin = new Thickness(4) };
        string shape = DiceLogic.Shape(sides);
        var points = DiceLogic.ShapePoints(shape);
        if (points.Length > 0)
            grid.Children.Add(new Polygon { Points = new PointCollection(points.Select(p => new Point(p.X * size, p.Y * size))), Fill = fill, Stroke = outline, StrokeThickness = 1.2, StrokeLineJoin = PenLineJoin.Round });
        else if (shape == "circle")
            grid.Children.Add(new Ellipse { Fill = fill, Stroke = outline, StrokeThickness = 1.2 });
        else
            grid.Children.Add(new Border { Background = fill, BorderBrush = outline, BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(size * 0.2) });

        if (sides == 6 && ctx.GetBool("pips") && face is >= 1 and <= 6)
        {
            double inset = size * 0.22, span = size - inset * 2, diameter = size * 0.15;
            var canvas = new Canvas { Width = size, Height = size };
            foreach (var (col, row) in DiceLogic.Pips(face))
            {
                var pip = new Ellipse { Width = diameter, Height = diameter, Fill = ink };
                Canvas.SetLeft(pip, inset + col * span / 2 - diameter / 2);
                Canvas.SetTop(pip, inset + row * span / 2 - diameter / 2);
                canvas.Children.Add(pip);
            }
            grid.Children.Add(canvas);
        }
        else
        {
            string text = face.ToString(CultureInfo.CurrentCulture);
            double factor = text.Length >= 3 ? 0.3 : text.Length == 2 ? 0.4 : 0.48;
            var number = Theme.Label(text, Math.Max(8, ctx.Font(size * factor)), ink);
            number.FontWeight = FontWeights.Bold;
            number.HorizontalAlignment = HorizontalAlignment.Center;
            number.Margin = new Thickness(0, shape == "triangle" ? size * 0.16 : 0, 0, 0);
            grid.Children.Add(number);
        }
        return grid;
    }

    private static FrameworkElement Coin(WidgetContext ctx, string text, double size)
    {
        var baseColor = ctx.GetColor("coinColor", Theme.Accent("#d4af37"));
        var coin = new Grid { Width = size, Height = size, Margin = new Thickness(0, 2, 0, 2) };
        coin.Children.Add(new Ellipse { Fill = baseColor, Stroke = Theme.LineStrong, StrokeThickness = 1.5 });
        coin.Children.Add(new Ellipse { Margin = new Thickness(size * 0.09), Stroke = Theme.Tint("#000000", 0.28), StrokeThickness = 1.5 });
        var letter = Theme.Label(text.Length > 0 ? text[..1].ToUpperInvariant() : "?", Math.Max(10, ctx.Font(size * 0.42)), Theme.Tint("#000000", 0.62));
        letter.FontWeight = FontWeights.Bold;
        letter.HorizontalAlignment = HorizontalAlignment.Center;
        coin.Children.Add(letter);
        return coin;
    }

    public FrameworkElement Build(WidgetContext ctx)
    {
        string mode = ctx.Get("mode");
        int modeIndex = Math.Max(0, Array.IndexOf(Modes, mode));
        string accent = ctx.Item.Accent;
        bool current = ctx.Get("data.mode") == mode;
        int[] faces = current && mode == "dice" ? DiceLogic.Decode(ctx.Get("data.faces")).Take(6).ToArray() : [];
        string main = current ? ctx.Get("data.main") : "";
        string sub = current ? ctx.Get("data.sub") : "";
        int sides = Sides(ctx);

        string badgeText = mode switch
        {
            "dice" => DiceLogic.DiceLabel(ctx.GetInt("count"), sides),
            "coin" => "Coin",
            "number" => $"{ctx.GetInt("min")}–{ctx.GetInt("max")}",
            "pick" => Choices(ctx).Length + " choices",
            _ => "Yes / No",
        };
        FrameworkElement? badge = ctx.Width >= 228
            ? WidgetKit.Clickable(WidgetKit.Pill(ctx, badgeText), () => ctx.Set("mode", Modes[(modeIndex + 1) % Modes.Length]), "Click to switch mode")
            : null;
        var (card, body) = WidgetKit.Frame(ctx, badge, WidgetKit.IconButton("", "Roll", () => Roll(ctx)));

        // ----- Space budget -----
        double pad = ctx.GetNumber("_padding");
        double inner = ctx.Height - pad * 2 - (ctx.Item.ShowHeader ? 34 : 0) - 4;
        double innerWidth = ctx.Width - pad * 2 - 6;
        bool tabs = ctx.GetBool("tabs") && ctx.Width >= 260 && ctx.Height >= 228;
        double controlsHeight = mode == "dice" ? 58 : mode is "number" or "pick" ? 28 : 0;
        bool controls = ctx.GetBool("controls") && controlsHeight > 0 && ctx.Height >= (mode == "dice" ? 312 : 270);
        double used = (tabs ? 30 : 0) + (controls ? controlsHeight : 0);
        double minStage = mode == "dice" ? 118 : 84;
        int historyLines = ctx.Height >= 228 ? (int)Math.Clamp(Math.Floor((inner - used - minStage - 10) / 15), 0, ctx.GetInt("history")) : 0;
        var past = historyLines > 0 ? DiceLogic.HistoryFor(ctx.Get("data.history"), mode, historyLines) : [];
        // Newest result is already on the stage, so history starts with the one before it.
        if (current && past.Count > 0 && (main.Length > 0 || faces.Length > 0)) past = DiceLogic.HistoryFor(ctx.Get("data.history"), mode, historyLines + 1).Skip(1).ToList();
        if (past.Count == 0) historyLines = 0;
        used += historyLines > 0 ? historyLines * 15 + 10 : 0;
        double stageHeight = inner - used;

        // ----- Tabs -----
        if (tabs)
        {
            var strip = Theme.Segmented(ModeNames, modeIndex, index => ctx.Set("mode", Modes[index]), 24, accent);
            strip.Margin = new Thickness(0, 0, 0, 6);
            DockPanel.SetDock(strip, Dock.Top);
            body.Children.Add(strip);
        }

        // ----- History -----
        if (historyLines > 0)
        {
            var log = new StackPanel { Margin = new Thickness(2, 4, 2, 0) };
            log.Children.Add(new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(0, 0, 0, 4) });
            foreach (string line in past.Take(historyLines))
            {
                var row = WidgetKit.Muted(ctx, line, 10.5);
                row.HorizontalAlignment = HorizontalAlignment.Center;
                log.Children.Add(row);
            }
            DockPanel.SetDock(log, Dock.Bottom);
            body.Children.Add(log);
        }

        // ----- Quick settings -----
        if (controls)
        {
            var bar = new StackPanel { Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            if (mode == "dice")
            {
                var kinds = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                foreach (string side in SideChoices)
                {
                    string choice = side;
                    kinds.Children.Add(Chip(ctx, side, DiceLogic.ParseSides(side) == sides, () => ctx.Set("sides", choice), "Roll " + side));
                }
                bar.Children.Add(kinds);
                var count = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0) };
                int dice = ctx.GetInt("count");
                count.Children.Add(Chip(ctx, "−", false, () => ctx.Set("count", Math.Max(1, dice - 1)), "Fewer dice"));
                var countLabel = WidgetKit.Muted(ctx, dice == 1 ? "1 die" : dice + " dice", 11);
                countLabel.Margin = new Thickness(4, 0, 8, 0);
                count.Children.Add(countLabel);
                count.Children.Add(Chip(ctx, "+", false, () => ctx.Set("count", Math.Min(6, dice + 1)), "More dice"));
                bar.Children.Add(count);
            }
            else if (mode == "number")
                bar.Children.Add(Chip(ctx, "Change range…", false, () => AskRange(ctx), "Set the smallest and largest number"));
            else
                bar.Children.Add(Chip(ctx, "Edit choices…", false, () => AskChoices(ctx), "Edit the list of choices"));
            DockPanel.SetDock(bar, Dock.Bottom);
            body.Children.Add(bar);
        }

        // ----- Stage -----
        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var resultBrush = ctx.GetColor("resultColor", ctx.TextBrush);
        bool hasResult = mode == "dice" ? faces.Length > 0 : main.Length > 0;

        if (!hasResult)
        {
            var glyph = WidgetKit.Icon(mode switch { "coin" => "", "number" => "", "pick" => "", "yes-no" => "", _ => "" }, ctx.Font(26), Theme.Dim);
            glyph.Margin = new Thickness(0, 0, 0, 6);
            content.Children.Add(glyph);
            var hint = WidgetKit.Muted(ctx, ctx.IsEditing ? "Rolls when the deck is locked" : "Click to roll", 12);
            hint.HorizontalAlignment = HorizontalAlignment.Center;
            content.Children.Add(hint);
        }
        else if (mode == "dice")
        {
            int cols = faces.Length <= 3 ? faces.Length : faces.Length == 4 ? 4 : 3;
            int rows = (int)Math.Ceiling(faces.Length / (double)cols);
            bool showTotal = ctx.GetBool("sum") && faces.Length > 1;
            double totalRoom = showTotal ? 34 : 0;
            double dieSize = Math.Min(ctx.GetNumber("dieSize") * (faces.Length == 1 ? 1.4 : 1), Math.Min((innerWidth - 8) / cols - 8, (stageHeight - totalRoom) / rows - 8));
            dieSize = Math.Max(26, dieSize);
            var wrap = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Width = Math.Min(innerWidth, cols * (dieSize + 8) + 2) };
            foreach (int face in faces) wrap.Children.Add(Face(ctx, face, sides, dieSize));
            content.Children.Add(wrap);
            if (showTotal)
            {
                var total = WidgetKit.Text(ctx, faces.Sum().ToString(CultureInfo.CurrentCulture), 22, resultBrush, true);
                total.HorizontalAlignment = HorizontalAlignment.Center;
                total.ToolTip = string.Join(" + ", faces);
                content.Children.Add(total);
            }
        }
        else
        {
            double big = Math.Clamp(stageHeight / 3.2, 22, 40);
            switch (mode)
            {
                case "coin":
                    content.Children.Add(Coin(ctx, main, Math.Clamp(stageHeight - 50, 40, 78)));
                    var coinText = WidgetKit.Text(ctx, main, 20, resultBrush, true);
                    coinText.HorizontalAlignment = HorizontalAlignment.Center;
                    content.Children.Add(coinText);
                    break;
                case "yes-no":
                    string yesNo = main;
                    string hex = yesNo == "Yes" ? ctx.Get("yesColor") : yesNo == "No" ? ctx.Get("noColor") : "#d9a441";
                    var answer = WidgetKit.Text(ctx, yesNo, Math.Min(52, big * 1.4), hex.Length > 0 ? WidgetKit.Color(hex) : resultBrush, true);
                    answer.HorizontalAlignment = HorizontalAlignment.Center;
                    content.Children.Add(answer);
                    break;
                default:
                    var result = WidgetKit.Text(ctx, main, mode == "pick" && main.Length > 14 ? Math.Min(24, big) : big, resultBrush, true);
                    result.TextWrapping = TextWrapping.Wrap;
                    result.TextTrimming = TextTrimming.None;
                    result.TextAlignment = TextAlignment.Center;
                    result.HorizontalAlignment = HorizontalAlignment.Center;
                    content.Children.Add(result);
                    break;
            }
            if (sub.Length > 0)
            {
                var caption = WidgetKit.Muted(ctx, sub, 11);
                caption.HorizontalAlignment = HorizontalAlignment.Center;
                caption.Margin = new Thickness(0, 4, 0, 0);
                content.Children.Add(caption);
            }
        }

        if (ctx.State("dice.pop", false) && !ctx.IsSnapshot)
        {
            ctx.SetState("dice.pop", false, false);
            if (ctx.GetBool("animate"))
            {
                var scale = new ScaleTransform(0.55, 0.55);
                content.RenderTransformOrigin = new Point(0.5, 0.5);
                content.RenderTransform = scale;
                var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 };
                var grow = new DoubleAnimation(0.55, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
                content.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(200)));
            }
        }

        var stage = new Border { Background = Brushes.Transparent, Child = content };
        WidgetKit.Clickable(stage, () => Roll(ctx), "Click to roll");
        body.Children.Add(stage);
        return card;
    }
}
