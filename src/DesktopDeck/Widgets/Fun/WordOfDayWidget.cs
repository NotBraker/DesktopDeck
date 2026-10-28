using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using DesktopDeck.Models;

namespace DesktopDeck.Widgets;

internal sealed class WordOfDayWidget : IWidget
{
    public string Kind => "word-of-day";
    public string Name => "Word of the day";
    public string Description => "An interesting English word with its meaning and an example, offline";
    public string Section => "Fun";
    public string Glyph => "";
    public string Accent => "#5E9BA6";
    public (double Width, double Height) DefaultSize => (324, 276);
    public (double Width, double Height) MinimumSize => (228, 180);

    public IReadOnlyList<OptionSpec> Options { get; } =
    [
        OptionSpec.Toggle("pronunciation", "Show pronunciation hint", true, "What to show"),
        OptionSpec.Toggle("part", "Show word type", true, "What to show"),
        OptionSpec.Choice("partStyle", "Word type style", "full", ["full", "short"], ["noun / adjective", "n. / adj."], "What to show"),
        OptionSpec.Toggle("example", "Show example sentence", true, "What to show"),
        OptionSpec.Toggle("highlight", "Highlight the word in the example", true, "What to show"),
        OptionSpec.Toggle("difficultyBadge", "Show difficulty tag", true, "What to show"),
        OptionSpec.Choice("difficulty", "Difficulty", "all", ["all", "easy", "medium", "hard", "easy-medium", "medium-hard"], ["All words", "Easy", "Medium", "Hard", "Easy and medium", "Medium and hard"], "What to show"),
        OptionSpec.Choice("partFilter", "Only words that are", "all", WordOfDayLogic.PartsOfSpeech, ["Any kind", "Nouns", "Adjectives", "Verbs", "Adverbs"], "What to show"),
        OptionSpec.Choice("layout", "Layout", "dictionary", ["dictionary", "centered", "compact"], ["Dictionary entry", "Centered", "Compact"], "Layout"),
        OptionSpec.Number("wordSize", "Largest word size", 30, 16, 44, "Layout", "Long words shrink to fit the card."),
        OptionSpec.Number("definitionSize", "Definition size", 13, 10, 22, "Layout"),
        OptionSpec.Toggle("italicExample", "Italic example", true, "Layout"),
        OptionSpec.Color("wordColor", "Word color", "", "Colors", "Empty = the accent."),
        OptionSpec.Color("partColor", "Word type color", "", "Colors", "Empty = muted."),
        OptionSpec.Color("definitionColor", "Definition color", "", "Colors", "Empty = the text color."),
        OptionSpec.Choice("rotation", "Change the word", "daily", ["daily", "hourly", "click"], ["Every day", "Every hour", "Only when I click"], "Behavior"),
        OptionSpec.Toggle("clickNext", "Click the word for the next one", true, "Behavior"),
    ];

    public IEnumerable<Action<LauncherItem>> Samples =>
    [
        _ => { },
        item =>
        {
            item.Options["difficulty"] = "hard";
            item.Options["layout"] = "centered";
            item.Options["wordColor"] = "#C2A14D";
            item.Options["partStyle"] = "short";
        },
        item =>
        {
            item.Options["difficulty"] = "easy";
            item.Options["layout"] = "compact";
            item.Options["data.offset"] = QuoteLogic.WriteOffset(QuoteLogic.Period("daily", DateTime.Now), 2);
            item.Options["pronunciation"] = "false";
            item.Options["example"] = "false";
            item.Width = 276; item.Height = 180;
        },
    ];

    private static IReadOnlyList<WordOfDayLogic.Entry> Pool(WidgetContext ctx) => WordOfDayLogic.Filter(ctx.Get("difficulty"), ctx.Get("partFilter"));

    private static WordOfDayLogic.Entry? Current(WidgetContext ctx)
    {
        string rotation = ctx.Get("rotation");
        var now = DateTime.Now;
        return WordOfDayLogic.For(Pool(ctx), rotation, now, QuoteLogic.ReadOffset(ctx.Get("data.offset"), QuoteLogic.Period(rotation, now)));
    }

    private static void Move(WidgetContext ctx, int delta)
    {
        long period = QuoteLogic.Period(ctx.Get("rotation"), DateTime.Now);
        ctx.Set("data.offset", QuoteLogic.WriteOffset(period, QuoteLogic.ReadOffset(ctx.Get("data.offset"), period) + delta));
    }

    public IEnumerable<WidgetAction> Actions(WidgetContext ctx)
    {
        yield return new WidgetAction("Next word", "", () => Move(ctx, 1));
        yield return new WidgetAction("Previous word", "", () => Move(ctx, -1));
        yield return new WidgetAction("Back to the current word", "", () => ctx.Set("data.offset", ""));
        yield return new WidgetAction("Copy word and meaning", "", () =>
        {
            if (Current(ctx) is not { } word) return;
            try
            {
                Clipboard.SetText($"{word.Word} ({word.PartOfSpeech}): {word.Definition}\n\"{word.Example}\"");
                ctx.Status("Word copied.");
            }
            catch (ExternalException) { ctx.Status("The clipboard is busy; try again."); }
        });
    }

    private static string DifficultyColor(string difficulty) => difficulty switch { "easy" => "#7fbf7f", "hard" => "#e07a6a", _ => "#d9a441" };

    public FrameworkElement Build(WidgetContext ctx)
    {
        string rotation = ctx.Get("rotation");
        var now = DateTime.Now;
        long period = QuoteLogic.Period(rotation, now);
        var word = WordOfDayLogic.For(Pool(ctx), rotation, now, QuoteLogic.ReadOffset(ctx.Get("data.offset"), period))!;

        var badge = ctx.GetBool("difficultyBadge") && ctx.Width >= 260 ? WidgetKit.Pill(ctx, WordOfDayLogic.Title(word.Difficulty), DifficultyColor(word.Difficulty)) : null;
        var (card, body) = WidgetKit.Frame(ctx, badge, WidgetKit.IconButton("", "Next word", () => Move(ctx, 1)));

        string layout = ctx.Get("layout");
        bool centered = layout == "centered";
        bool compact = layout == "compact";
        var alignment = centered ? TextAlignment.Center : TextAlignment.Left;
        var horizontal = centered ? HorizontalAlignment.Center : HorizontalAlignment.Left;

        double pad = ctx.GetNumber("_padding");
        double room = ctx.Height - pad * 2 - (ctx.Item.ShowHeader ? 34 : 0);
        double availableWidth = ctx.Width - pad * 2 - 14;
        double wordSize = Math.Min(ctx.GetNumber("wordSize"), availableWidth / ctx.TextScale / Math.Max(4, word.Word.Length) / 0.6);
        wordSize = Math.Max(16, wordSize);
        if (room < 140) wordSize = Math.Min(wordSize, 24);

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };

        var title = WidgetKit.Text(ctx, word.Word, wordSize, ctx.GetColor("wordColor"), true);
        title.HorizontalAlignment = horizontal;
        title.TextAlignment = alignment;

        string partText = ctx.Get("partStyle") == "short" ? WordOfDayLogic.ShortPart(word.PartOfSpeech) : word.PartOfSpeech;
        Border? part = ctx.GetBool("part")
            ? new Border
            {
                CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 1, 8, 2), Background = Theme.Surface3,
                Child = WidgetKit.Text(ctx, partText, 10.5, ctx.GetColor("partColor", Theme.Text2), true), VerticalAlignment = VerticalAlignment.Center,
            }
            : null;
        TextBlock? hint = null;
        if (ctx.GetBool("pronunciation"))
        {
            hint = WidgetKit.Muted(ctx, "/ " + word.Pronunciation + " /", 11);
            hint.FontStyle = FontStyles.Italic;
            hint.VerticalAlignment = VerticalAlignment.Center;
        }

        if (compact)
        {
            var headline = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
            title.Margin = new Thickness(0, 0, 8, 0);
            headline.Children.Add(title);
            if (part is not null) headline.Children.Add(part);
            stack.Children.Add(headline);
            if (hint is not null) { hint.HorizontalAlignment = HorizontalAlignment.Left; hint.Margin = new Thickness(0, 1, 0, 0); stack.Children.Add(hint); }
        }
        else
        {
            stack.Children.Add(title);
            if (hint is not null || part is not null)
            {
                var meta = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = horizontal, Margin = new Thickness(0, 3, 0, 0) };
                if (hint is not null) { hint.Margin = new Thickness(0, 0, part is null ? 0 : 8, 0); meta.Children.Add(hint); }
                if (part is not null) meta.Children.Add(part);
                stack.Children.Add(meta);
            }
        }

        var definition = WidgetKit.Text(ctx, word.Definition, ctx.GetNumber("definitionSize"), ctx.GetColor("definitionColor", ctx.TextBrush));
        definition.TextWrapping = TextWrapping.Wrap;
        definition.TextTrimming = TextTrimming.None;
        definition.TextAlignment = alignment;
        definition.Margin = new Thickness(0, compact ? 6 : 10, 0, 0);
        stack.Children.Add(definition);

        if (ctx.GetBool("example") && !compact && room >= 190)
        {
            stack.Children.Add(new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(0, 10, 0, 8) });
            var example = WidgetKit.Muted(ctx, "", 11.5);
            example.TextWrapping = TextWrapping.Wrap;
            example.TextTrimming = TextTrimming.None;
            example.TextAlignment = alignment;
            if (ctx.GetBool("italicExample")) example.FontStyle = FontStyles.Italic;
            var found = ctx.GetBool("highlight") ? WordOfDayLogic.FindInExample(word.Example, word.Word) : null;
            if (found is { } at)
            {
                example.Inlines.Add(new Run(word.Example[..at.Start]));
                example.Inlines.Add(new Run(word.Example.Substring(at.Start, at.Length)) { Foreground = Theme.Text2, FontWeight = FontWeights.SemiBold });
                example.Inlines.Add(new Run(word.Example[(at.Start + at.Length)..]));
            }
            else example.Text = word.Example;
            stack.Children.Add(example);
        }

        if (ctx.GetBool("clickNext")) WidgetKit.Clickable(stack, () => Move(ctx, 1), "Click for another word");
        body.Children.Add(WidgetKit.Scroll(stack));

        if (rotation != "click") ctx.Tick(() => false, time => { if (QuoteLogic.Period(rotation, time.LocalDateTime) != period) ctx.Rebuild(); });
        return card;
    }
}
