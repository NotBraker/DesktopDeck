using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using DesktopDeck.Models;
using Shapes = System.Windows.Shapes;

namespace DesktopDeck.Widgets;

/// <summary>
/// A decoration: one big symbol (a named Segoe Fluent Icons glyph, an emoji or a few letters) on an optional shape,
/// with a caption, and optionally a click target. The glyph table and sizing live in StickerLogic.cs.
/// </summary>
internal sealed class StickerWidget : IWidget
{
    public string Kind => "sticker";
    public string Name => "Sticker";
    public string Description => "A big symbol, emoji or word to brighten your deck";
    public string Section => "Visual";
    public string Glyph => "";
    public string Accent => "#d8af68";
    public (double Width, double Height) DefaultSize => (180, 180);
    public (double Width, double Height) MinimumSize => (180, 132);

    public IReadOnlyList<OptionSpec> Options { get; } =
    [
        OptionSpec.Choice("glyph", "Symbol", "Star", StickerLogic.Names, null, "What to show"),
        OptionSpec.Text("custom", "Emoji or short text instead", "", "What to show", "Type an emoji or a word to use instead of the symbol. Leave empty to use the symbol."),
        OptionSpec.Text("caption", "Caption", "", "What to show"),
        OptionSpec.Number("size", "Symbol size", 76, 16, 240, "Layout", "Shrinks automatically to fit the widget."),
        OptionSpec.Number("rotation", "Rotation (degrees)", 0, -180, 180, "Layout"),
        OptionSpec.Choice("shape", "Background shape", "none", ["none", "circle", "rounded", "blob"], ["None", "Circle", "Rounded square", "Blob"], "Layout"),
        OptionSpec.Number("captionSize", "Caption size", 13, 9, 28, "Layout"),
        OptionSpec.Color("color", "Symbol color", "", "Colors", "Empty = accent color."),
        OptionSpec.Color("background", "Shape color", "", "Colors", "Empty = a soft tint of the accent color."),
        OptionSpec.Color("captionColor", "Caption color", "", "Colors", "Empty = the text color."),
        OptionSpec.Toggle("shadow", "Drop shadow", false, "Colors"),
        OptionSpec.Number("shadowSize", "Shadow softness", 16, 2, 40, "Colors"),
        OptionSpec.Text("target", "Click opens", "", "Behavior", "A web address, file or folder. Leave empty for a plain decoration."),
    ];

    public IEnumerable<Action<LauncherItem>> Samples =>
    [
        item => { item.Options["caption"] = "Make something"; item.Options["shape"] = "circle"; },
        item => { item.Options["custom"] = "🌿"; item.Options["shape"] = "rounded"; item.Options["caption"] = "Take a breath"; item.Options["background"] = "#2E4A3A"; },
        item => { item.Options["glyph"] = "Heart"; item.Options["color"] = "#E68B8B"; item.Options["rotation"] = "-12"; item.Options["shape"] = "blob"; item.Options["shadow"] = "true"; item.Options["target"] = "https://example.com"; item.Width = 276; item.Height = 228; },
    ];

    public FrameworkElement Build(WidgetContext ctx)
    {
        var card = WidgetKit.Card(ctx);
        double pad = ctx.GetNumber("_padding");
        string caption = ctx.Get("caption").Trim();
        string custom = ctx.Get("custom").Trim();
        string shape = ctx.Get("shape");
        bool hasShape = shape != "none";
        double captionSize = ctx.Font(ctx.GetNumber("captionSize"));
        double captionHeight = caption.Length > 0 ? captionSize * 1.45 + 8 : 0;
        double size = StickerLogic.SymbolSize(ctx.Font(ctx.GetNumber("size")), ctx.Width, ctx.Height, pad + 4, captionHeight, hasShape);
        var symbolBrush = ctx.GetColor("color");

        // ---- The symbol ----
        FrameworkElement symbol;
        if (custom.Length > 0)
        {
            bool emoji = StickerLogic.LooksLikeEmoji(custom);
            var text = new TextBlock
            {
                Text = custom, FontSize = size * 0.9, Foreground = symbolBrush, TextWrapping = TextWrapping.NoWrap,
                FontFamily = emoji ? new FontFamily("Segoe UI Emoji, Segoe UI Symbol") : new FontFamily("Segoe UI Variable Display, Segoe UI"),
                FontWeight = emoji ? FontWeights.Normal : FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            // Longer words shrink to the room there is (the shape's width, or the card's).
            double room = Math.Max(24, (hasShape ? size * (1 + 2 * StickerLogic.ShapePadding) * 0.86 : ctx.Width - 2 * pad - 8));
            symbol = new Viewbox { Child = text, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, MaxWidth = room, MaxHeight = size * 1.1 };
        }
        else symbol = WidgetKit.Icon(StickerLogic.GlyphFor(ctx.Get("glyph")), size, symbolBrush);
        symbol.HorizontalAlignment = HorizontalAlignment.Center;
        symbol.VerticalAlignment = VerticalAlignment.Center;
        symbol.RenderTransformOrigin = new Point(0.5, 0.5);
        symbol.RenderTransform = new RotateTransform(ctx.GetNumber("rotation"));

        // ---- The shape behind it ----
        FrameworkElement visual = symbol;
        if (hasShape)
        {
            double side = size * (1 + 2 * StickerLogic.ShapePadding);
            var fill = ctx.Get("background").Trim().Length > 0 ? ctx.GetColor("background") : Theme.Tint(ctx.Item.Accent, 0.22);
            var holder = new Grid { Width = side, Height = side };
            holder.Children.Add(shape switch
            {
                "circle" => new Border { CornerRadius = new CornerRadius(side / 2), Background = fill },
                "rounded" => new Border { CornerRadius = new CornerRadius(side * 0.24), Background = fill },
                _ => (UIElement)new Shapes.Path { Data = Blob, Fill = fill, Stretch = Stretch.Fill },
            });
            holder.Children.Add(symbol);
            visual = holder;
        }
        if (ctx.GetBool("shadow"))
            visual.Effect = new DropShadowEffect { BlurRadius = ctx.GetNumber("shadowSize"), ShadowDepth = Math.Min(8, ctx.GetNumber("shadowSize") / 4 + 1), Direction = 280, Opacity = 0.5, Color = Colors.Black };

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(visual);
        if (caption.Length > 0)
        {
            var label = WidgetKit.Text(ctx, caption, ctx.GetNumber("captionSize"), ctx.GetColor("captionColor", ctx.TextBrush), bold: true);
            label.Margin = new Thickness(0, 8, 0, 0);
            label.TextAlignment = TextAlignment.Center;
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.MaxWidth = Math.Max(40, ctx.Width - 2 * pad - 8);
            stack.Children.Add(label);
        }
        card.Child = stack;

        string target = StickerLogic.NormalizeTarget(ctx.Get("target"));
        if (target.Length > 0)
            WidgetKit.Clickable(card, () => { if (!ctx.IsSnapshot && !ctx.IsEditing) ctx.Open(target); }, target);
        return card;
    }

    /// <summary>A soft, slightly uneven outline (drawn in a 100 × 100 box).</summary>
    private static readonly Geometry Blob = MakeBlob();

    private static Geometry MakeBlob()
    {
        var geometry = Geometry.Parse("M54,3 C80,1 98,22 96,50 C94,79 72,99 45,96 C18,93 2,74 4,46 C6,21 28,5 54,3 Z");
        geometry.Freeze();
        return geometry;
    }
}
