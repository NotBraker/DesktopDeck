using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopDeck.Models;

namespace DesktopDeck.Widgets;

internal sealed class QuoteWidget : IWidget
{
    public string Kind => "quote";
    public string Name => "Quote of the day";
    public string Description => "A short classic quote each day, offline, plus your own";
    public string Section => "Fun";
    public string Glyph => "";
    public string Accent => "#C2A14D";
    public (double Width, double Height) DefaultSize => (324, 228);
    public (double Width, double Height) MinimumSize => (228, 180);

    public IReadOnlyList<OptionSpec> Options { get; } =
    [
        OptionSpec.Choice("source", "Quote source", "built-in", ["built-in", "mine", "both"], ["Built-in collection", "Only my quotes", "Both"], "What to show"),
        OptionSpec.Toggle("cat-motivation", "Motivation quotes", true, "What to show"),
        OptionSpec.Toggle("cat-wisdom", "Wisdom quotes", true, "What to show"),
        OptionSpec.Toggle("cat-humor", "Humor quotes", true, "What to show"),
        OptionSpec.Toggle("cat-focus", "Focus quotes", true, "What to show"),
        OptionSpec.Toggle("cat-calm", "Calm quotes", true, "What to show"),
        OptionSpec.Lines("quotes", "My quotes", "", "What to show", "One per line, written as: quote — author"),
        OptionSpec.Toggle("author", "Show author", true, "What to show"),
        OptionSpec.Toggle("category", "Show category tag", true, "What to show"),
        OptionSpec.Choice("marks", "Quote marks", "curly", ["none", "curly", "straight", "guillemets", "corner", "big"], ["None", "Curly “ ”", "Straight \" \"", "Guillemets « »", "Corner 「 」", "Big opening mark"], "Layout"),
        OptionSpec.Choice("alignment", "Alignment", "center", ["left", "center", "right"], ["Left", "Centered", "Right"], "Layout"),
        OptionSpec.Toggle("italic", "Italic quote", true, "Layout"),
        OptionSpec.Number("size", "Largest quote size", 20, 12, 36, "Layout", "Long quotes shrink to fit the card."),
        OptionSpec.Toggle("rule", "Accent line above the author", true, "Layout"),
        OptionSpec.Color("quoteColor", "Quote color", "", "Colors", "Empty = the text color."),
        OptionSpec.Color("authorColor", "Author color", "", "Colors", "Empty = muted."),
        OptionSpec.Color("markColor", "Quote mark and line color", "", "Colors", "Empty = the accent."),
        OptionSpec.Choice("rotation", "Change the quote", "daily", ["daily", "hourly", "click"], ["Every day", "Every hour", "Only when I click"], "Behavior"),
        OptionSpec.Toggle("clickNext", "Click the quote for the next one", true, "Behavior"),
    ];

    public IEnumerable<Action<LauncherItem>> Samples =>
    [
        _ => { },
        item =>
        {
            item.Options["source"] = "mine";
            item.Options["quotes"] = "Small steps still move you forward. — The studio\nMake room for a little wonder. — Notebook";
            item.Options["marks"] = "big";
            item.Options["alignment"] = "left";
            item.Options["italic"] = "false";
            item.Accent = "#5E9BA6";
        },
        item =>
        {
            item.Options["cat-motivation"] = "false"; item.Options["cat-wisdom"] = "false"; item.Options["cat-focus"] = "false"; item.Options["cat-calm"] = "false";
            item.Options["marks"] = "guillemets";
            item.Options["alignment"] = "right";
            item.Options["data.offset"] = QuoteLogic.WriteOffset(QuoteLogic.Period("daily", DateTime.Now), 3);
        },
    ];

    private static string[] Categories(WidgetContext ctx) => QuoteLogic.Categories.Where(c => ctx.GetBool("cat-" + c)).ToArray();

    private static IReadOnlyList<QuoteLogic.Entry> Pool(WidgetContext ctx)
        => QuoteLogic.Pool(ctx.Get("source"), Categories(ctx), ctx.GetLines("quotes"));

    private static long Offset(WidgetContext ctx, long period) => QuoteLogic.ReadOffset(ctx.Get("data.offset"), period);

    private static void Move(WidgetContext ctx, int delta)
    {
        long period = QuoteLogic.Period(ctx.Get("rotation"), DateTime.Now);
        ctx.Set("data.offset", QuoteLogic.WriteOffset(period, Offset(ctx, period) + delta));
    }

    private static QuoteLogic.Entry? Current(WidgetContext ctx)
    {
        string rotation = ctx.Get("rotation");
        long period = QuoteLogic.Period(rotation, DateTime.Now);
        return QuoteLogic.For(Pool(ctx), rotation, DateTime.Now, Offset(ctx, period));
    }

    private static void Copy(WidgetContext ctx)
    {
        if (Current(ctx) is not { } quote) return;
        try
        {
            Clipboard.SetText(quote.Text + (quote.Author.Length > 0 ? " — " + quote.Author : ""));
            ctx.Status("Quote copied.");
        }
        catch (ExternalException) { ctx.Status("The clipboard is busy; try again."); }
    }

    public IEnumerable<WidgetAction> Actions(WidgetContext ctx)
    {
        yield return new WidgetAction("Next quote", "", () => Move(ctx, 1));
        yield return new WidgetAction("Previous quote", "", () => Move(ctx, -1));
        yield return new WidgetAction("Back to the current quote", "", () => ctx.Set("data.offset", ""));
        yield return new WidgetAction("Copy this quote", "", () => Copy(ctx));
        yield return new WidgetAction("Add my own quote…", "", () =>
        {
            if (ctx.Ask("Add a quote", "Quote — author", "") is not { } line || QuoteLogic.ParseLine(line) is null) return;
            string existing = ctx.Get("quotes").TrimEnd();
            ctx.Set("quotes", existing.Length == 0 ? line.Trim() : existing + "\n" + line.Trim(), false);
            if (ctx.Get("source") == "built-in") ctx.Set("source", "both", false);
            ctx.Changed();
        });
    }

    public FrameworkElement Build(WidgetContext ctx)
    {
        string rotation = ctx.Get("rotation");
        var now = DateTime.Now;
        long period = QuoteLogic.Period(rotation, now);
        var quote = QuoteLogic.For(Pool(ctx), rotation, now, Offset(ctx, period));

        var badge = quote is not null && ctx.GetBool("category") && ctx.Width >= 260 ? WidgetKit.Pill(ctx, char.ToUpperInvariant(quote.Category[0]) + quote.Category[1..]) : null;
        var next = WidgetKit.IconButton("", "Next quote", () => Move(ctx, 1));
        var (card, body) = WidgetKit.Frame(ctx, badge, next);

        if (quote is null)
        {
            body.Children.Add(WidgetKit.Clickable(WidgetKit.Empty(ctx, "No quotes selected. Click to choose categories or add your own."), ctx.Customize));
            return card;
        }

        var alignment = ctx.Get("alignment") switch { "left" => TextAlignment.Left, "right" => TextAlignment.Right, _ => TextAlignment.Center };
        var horizontal = alignment switch { TextAlignment.Left => HorizontalAlignment.Left, TextAlignment.Right => HorizontalAlignment.Right, _ => HorizontalAlignment.Center };
        var markBrush = ctx.GetColor("markColor");
        bool big = ctx.Get("marks") == "big";
        bool showAuthor = ctx.GetBool("author") && quote.Author.Length > 0;

        double pad = ctx.GetNumber("_padding");
        double room = ctx.Height - pad * 2 - (ctx.Item.ShowHeader ? 34 : 0) - 8;
        double availableWidth = ctx.Width - pad * 2 - 12;
        double reserved = (showAuthor ? 30 : 0) + (big ? 26 : 0);
        string shown = QuoteLogic.Wrap(quote.Text, ctx.Get("marks"));
        double size = QuoteLogic.FitFontSize(shown, ctx.GetNumber("size"), 11, availableWidth / ctx.TextScale, (room - reserved) / ctx.TextScale);

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
        if (big)
        {
            var mark = WidgetKit.Text(ctx, "“", 44, markBrush, true);
            mark.FontFamily = new FontFamily("Georgia, Cambria, Segoe UI");
            mark.HorizontalAlignment = horizontal;
            mark.Margin = new Thickness(0, -4, 0, -ctx.Font(16));
            mark.Opacity = 0.75;
            stack.Children.Add(mark);
        }
        var text = WidgetKit.Text(ctx, shown, size, ctx.GetColor("quoteColor", ctx.TextBrush));
        text.TextWrapping = TextWrapping.Wrap;
        text.TextTrimming = TextTrimming.None;
        text.TextAlignment = alignment;
        if (ctx.GetBool("italic")) text.FontStyle = FontStyles.Italic;
        stack.Children.Add(text);

        if (showAuthor)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = horizontal, Margin = new Thickness(0, 10, 0, 0) };
            if (ctx.GetBool("rule")) line.Children.Add(new Border { Width = 16, Height = 2, CornerRadius = new CornerRadius(1), Background = markBrush, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            line.Children.Add(WidgetKit.Text(ctx, quote.Author, 11.5, ctx.GetColor("authorColor", Theme.Muted), true));
            stack.Children.Add(line);
        }

        if (ctx.GetBool("clickNext")) WidgetKit.Clickable(stack, () => Move(ctx, 1), "Click for another quote");
        body.Children.Add(WidgetKit.Scroll(stack));

        if (rotation != "click") ctx.Tick(() => false, time => { if (QuoteLogic.Period(rotation, time.LocalDateTime) != period) ctx.Rebuild(); });
        return card;
    }
}
