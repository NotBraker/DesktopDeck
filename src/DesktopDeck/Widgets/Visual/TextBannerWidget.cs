using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using DesktopDeck.Models;

namespace DesktopDeck.Widgets;

/// <summary>
/// Big decorative text: a heading, a name or a motto, sized to fill its box (or a fixed size), in a chosen font,
/// with an optional two-color gradient and shadow or glow. {date}, {time}, {weekday}… update every minute.
/// </summary>
internal sealed class TextBannerWidget : IWidget
{
    public string Kind => "text-banner";
    public string Name => "Text banner";
    public string Description => "Big decorative text: a heading, name or motto";
    public string Section => "Visual";
    public string Glyph => "";
    public string Accent => "#ec4899";
    public (double Width, double Height) DefaultSize => (372, 180);
    public (double Width, double Height) MinimumSize => (180, 84);

    private static readonly string[] Fonts =
        ["Segoe UI Variable Display", "Segoe UI", "Georgia", "Consolas", "Cascadia Code", "Bahnschrift", "Impact", "Segoe Script", "Segoe Print", "Times New Roman"];

    public IReadOnlyList<OptionSpec> Options { get; } =
    [
        new OptionSpec("text", "Text", OptionType.LongText, "Stay curious")
        {
            Group = "What to show",
            Hint = "Line breaks are kept. Live tokens: {date} {shortdate} {time} {weekday} {day} {month} {year} {greeting}.",
        },
        OptionSpec.Choice("font", "Font", "Segoe UI Variable Display", Fonts, null, "Font"),
        OptionSpec.Choice("weight", "Weight", "semibold", ["light", "regular", "semibold", "bold", "black"], ["Light", "Regular", "Semibold", "Bold", "Black"], "Font"),
        OptionSpec.Toggle("italic", "Italic", false, "Font"),
        OptionSpec.Choice("case", "Letter case", "none", ["none", "upper", "lower", "title"], ["As typed", "UPPERCASE", "lowercase", "Title Case"], "Font"),
        OptionSpec.Choice("sizeMode", "Size", "fit", ["fit", "fixed"], ["Fit to the box", "Fixed size"], "Layout"),
        OptionSpec.Number("fontSize", "Fixed size", 40, 8, 300, "Layout", "Used when Size is Fixed."),
        OptionSpec.Number("maxSize", "Largest size when fitting", 160, 12, 400, "Layout"),
        OptionSpec.Toggle("wrap", "Wrap long lines", true, "Layout", "Off = only your own line breaks start a new line."),
        OptionSpec.Choice("alignH", "Horizontal alignment", "center", ["left", "center", "right"], ["Left", "Center", "Right"], "Layout"),
        OptionSpec.Choice("alignV", "Vertical alignment", "center", ["top", "center", "bottom"], ["Top", "Center", "Bottom"], "Layout"),
        OptionSpec.Number("lineSpacing", "Line spacing", 1.0, 0.6, 2.5, "Layout"),
        OptionSpec.Choice("fill", "Fill", "solid", ["solid", "gradient"], ["One color", "Two-color gradient"], "Colors"),
        OptionSpec.Color("color", "Color", "", "Colors", "Empty = the text color."),
        OptionSpec.Color("color2", "Second gradient color", "#f59e0b", "Colors"),
        OptionSpec.Choice("direction", "Gradient direction", "horizontal", ["horizontal", "vertical", "diagonal", "diagonal-up"], ["Left → right", "Top → bottom", "Diagonal ↘", "Diagonal ↗"], "Colors"),
        OptionSpec.Choice("effect", "Effect", "none", ["none", "shadow", "glow"], ["None", "Drop shadow", "Glow"], "Colors"),
        OptionSpec.Color("effectColor", "Shadow / glow color", "", "Colors", "Empty = black shadow or accent-colored glow."),
        OptionSpec.Number("effectSize", "Shadow / glow size", 14, 2, 48, "Colors"),
        OptionSpec.Toggle("clock24", "24-hour time for {time}", false, "Behavior"),
    ];

    public IEnumerable<Action<LauncherItem>> Samples =>
    [
        item => { item.Options["fill"] = "gradient"; item.Options["color"] = "#f472b6"; item.Options["color2"] = "#fbbf24"; item.Options["effect"] = "glow"; item.Options["effectColor"] = "#66ec4899"; },
        item =>
        {
            item.Options["text"] = "{greeting}, Sam\nIt's {weekday}";
            item.Options["font"] = "Georgia"; item.Options["weight"] = "regular"; item.Options["italic"] = "true"; item.Options["alignH"] = "left";
            item.Width = 372; item.Height = 228;
        },
        item =>
        {
            item.Options["text"] = "ship it"; item.Options["font"] = "Impact"; item.Options["case"] = "upper"; item.Options["weight"] = "regular";
            item.Options["fill"] = "gradient"; item.Options["color"] = "#22d3ee"; item.Options["color2"] = "#6c63ff"; item.Options["direction"] = "vertical";
            item.Options["effect"] = "shadow"; item.CardStyle = "none"; item.Width = 468; item.Height = 132;
        },
    ];

    public FrameworkElement Build(WidgetContext ctx)
    {
        var card = WidgetKit.Card(ctx);
        double pad = ctx.GetNumber("_padding") + 4;
        double boxWidth = Math.Max(20, ctx.Width - 2 * pad), boxHeight = Math.Max(20, ctx.Height - 2 * pad);
        string raw = ctx.Get("text");
        string text = TextBannerLogic.Render(raw, ctx.Get("case"), DateTime.Now, CultureInfo.CurrentCulture, ctx.GetBool("clock24"));
        if (text.Trim().Length == 0) text = "Your text here";

        string font = ctx.Get("font");
        var family = new FontFamily((Fonts.Contains(font) ? font : Fonts[0]) + ", Segoe UI");
        var block = new TextBlock
        {
            Text = text,
            FontFamily = family,
            FontWeight = ctx.Get("weight") switch { "light" => FontWeights.Light, "regular" => FontWeights.Normal, "bold" => FontWeights.Bold, "black" => FontWeights.Black, _ => FontWeights.SemiBold },
            FontStyle = ctx.GetBool("italic") ? FontStyles.Italic : FontStyles.Normal,
            TextAlignment = ctx.Get("alignH") switch { "left" => TextAlignment.Left, "right" => TextAlignment.Right, _ => TextAlignment.Center },
            TextWrapping = ctx.GetBool("wrap") ? TextWrapping.Wrap : TextWrapping.NoWrap,
        };
        TextOptions.SetTextFormattingMode(block, TextFormattingMode.Ideal);
        var halign = ctx.Get("alignH") switch { "left" => HorizontalAlignment.Left, "right" => HorizontalAlignment.Right, _ => HorizontalAlignment.Center };
        var valign = ctx.Get("alignV") switch { "top" => VerticalAlignment.Top, "bottom" => VerticalAlignment.Bottom, _ => VerticalAlignment.Center };
        double spacing = ctx.GetNumber("lineSpacing");
        void SetSize(double size)
        {
            block.FontSize = size;
            // Italic and script fonts overhang their advance box; a little room keeps the last letter whole.
            block.Padding = new Thickness(size * 0.06, 0, size * (ctx.GetBool("italic") || font.StartsWith("Segoe S", StringComparison.Ordinal) ? 0.18 : 0.06), size * 0.04);
            if (Math.Abs(spacing - 1) > 0.01)
            {
                block.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                block.LineHeight = Math.Max(1, size * family.LineSpacing * spacing);
            }
        }

        FrameworkElement content;
        double scale = 1;
        if (ctx.Get("sizeMode") == "fixed")
        {
            SetSize(ctx.Font(ctx.GetNumber("fontSize")));
            block.MaxWidth = boxWidth;
            block.TextTrimming = TextTrimming.CharacterEllipsis;
            block.HorizontalAlignment = halign;
            block.VerticalAlignment = valign;
            content = block;
        }
        else
        {
            // Lay the text out at 100 px, try a few line widths and keep the one that can be drawn largest.
            const double baseSize = 100;
            SetSize(baseSize);
            block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var natural = block.DesiredSize;
            double bestScale = 0, bestWidth = double.PositiveInfinity;
            int tries = block.TextWrapping == TextWrapping.Wrap ? Math.Min(6, Math.Max(1, text.Split(' ', '\n').Length)) : 1;
            for (int lines = 1; lines <= tries; lines++)
            {
                double width = lines == 1 ? double.PositiveInfinity : natural.Width / lines * 1.08;
                block.MaxWidth = width;
                block.Measure(new Size(width, double.PositiveInfinity));
                var size = block.DesiredSize;
                if (size.Width <= 0 || size.Height <= 0) continue;
                double s = Math.Min(boxWidth / size.Width, boxHeight / size.Height);
                if (s > bestScale * 1.02) { bestScale = s; bestWidth = width; }
            }
            block.MaxWidth = bestWidth;
            block.Measure(new Size(bestWidth, double.PositiveInfinity));
            var measured = block.DesiredSize;
            scale = Math.Min(bestScale, ctx.Font(ctx.GetNumber("maxSize")) / baseSize);
            if (scale <= 0 || !double.IsFinite(scale)) scale = 0.2;
            content = new Viewbox
            {
                Child = block, Stretch = Stretch.Fill,
                Width = measured.Width * scale, Height = measured.Height * scale,
                HorizontalAlignment = halign, VerticalAlignment = valign,
            };
        }

        // ---- Fill ----
        var first = ctx.GetColor("color", ctx.TextBrush);
        if (ctx.Get("fill") == "gradient" && first is SolidColorBrush a && ctx.GetColor("color2", ctx.Accent) is SolidColorBrush b)
        {
            var (start, end) = ctx.Get("direction") switch
            {
                "vertical" => (new Point(0.5, 0), new Point(0.5, 1)),
                "diagonal" => (new Point(0, 0), new Point(1, 1)),
                "diagonal-up" => (new Point(0, 1), new Point(1, 0)),
                _ => (new Point(0, 0.5), new Point(1, 0.5)),
            };
            var gradient = new LinearGradientBrush(a.Color, b.Color, start, end);
            gradient.Freeze();
            block.Foreground = gradient;
        }
        else block.Foreground = first;

        // ---- Shadow / glow (sized in screen pixels whatever the scale) ----
        string effect = ctx.Get("effect");
        if (effect != "none")
        {
            double size = ctx.GetNumber("effectSize") / scale;
            bool glow = effect == "glow";
            var color = ctx.GetColor("effectColor", glow ? ctx.Accent : Brushes.Black) is SolidColorBrush c ? c.Color : Colors.Black;
            block.Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(color.R, color.G, color.B),
                Opacity = color.A / 255.0 * (glow ? 0.95 : 0.6),
                BlurRadius = size,
                ShadowDepth = glow ? 0 : Math.Max(1, size / 4),
                Direction = 300,
                RenderingBias = RenderingBias.Quality,
            };
        }

        card.Child = new Grid { Margin = new Thickness(pad), Children = { content } };
        // Tokens like {time} change every minute; plain text never needs a redraw (a no-op ticker replaces any old one).
        bool live = TextBannerLogic.HasTokens(raw);
        ctx.Tick(() => false, now => { if (live && now.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture) != ctx.State("banner.minute", "")) { ctx.SetState("banner.minute", now.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture), false); ctx.Rebuild(); } });
        return card;
    }

    public IEnumerable<WidgetAction> Actions(WidgetContext ctx) =>
    [
        new("Edit text…", "", () =>
        {
            if (ctx.Ask("Text banner", "Text (tokens like {time} and {weekday} update live)", ctx.Get("text"), multiline: true) is { } value) ctx.Set("text", value);
        }),
    ];
}

