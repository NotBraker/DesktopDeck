using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopDeck.Models;

namespace DesktopDeck.Widgets;

/// <summary>
/// Flashcards with spaced repetition (Leitner boxes 1–5). Decks are Lines options "front | back"; click the card to flip it,
/// then answer Again / Good / Easy. Progress is saved in Item.Options["data.progress"]; the rules are in FlashcardsLogic.cs.
/// </summary>
internal sealed class FlashcardsWidget : IWidget
{
    public string Kind => "flashcards";
    public string Name => "Flashcards";
    public string Description => "Flashcards that come back when it is time to review: vocabulary, dates, formulas";
    public string Section => "Study";
    public string Glyph => "";
    public string Accent => "#C4684F";
    public (double Width, double Height) DefaultSize => (372, 276);
    public (double Width, double Height) MinimumSize => (252, 204);

    private const string LatinDeck =
        "aqua | water\nterra | earth, land\nignis | fire\nventus | wind\npuella | girl\npuer | boy\nfemina | woman\nvir | man\nrex | king\nregina | queen\n" +
        "servus | slave, servant\ndominus | master, lord\namicus | friend\nbellum | war\npax | peace\nvilla | house, country house\nvia | road, way\nurbs | city\n" +
        "templum | temple\nliber | book\namo | I love\nvideo | I see\naudio | I hear\nvenio | I come\nlaudo | I praise\nporto | I carry\nsum | I am\n" +
        "canis | dog\nequus | horse\nnox | night\nlux | light\nsol | sun\nluna | moon\nmare | sea";

    private const string SpanishDeck = "casa | house\nperro | dog\ngato | cat\nlibro | book\nagua | water\namigo | friend\nescuela | school\nmañana | tomorrow";

    public IReadOnlyList<OptionSpec> Options { get; } =
    [
        OptionSpec.Text("deck1Name", "First deck name", "Latin", "What to show"),
        OptionSpec.Lines("deck1", "First deck", LatinDeck, "What to show", "One card per line as front | back. Put \\n in a line for a line break; a line starting with # is ignored."),
        OptionSpec.Text("deck2Name", "Second deck name", "Deck 2", "What to show", "Optional. Switch between the two decks from the card."),
        OptionSpec.Lines("deck2", "Second deck", "", "What to show", "Same format as the first deck. Leave empty to use one deck."),
        OptionSpec.Choice("deckMode", "Study", "all", ["all", "1", "2"], ["Both decks", "First deck only", "Second deck only"], "What to show"),
        OptionSpec.Toggle("showStats", "Show due, new and today's progress", true, "What to show"),
        OptionSpec.Toggle("showBox", "Show the box number on each card", true, "What to show"),
        OptionSpec.Toggle("showIntervals", "Show the next review time on the buttons", true, "What to show"),
        OptionSpec.Number("cardTextSize", "Card text size", 22, 12, 48, "Layout", "Long cards shrink to fit."),
        OptionSpec.Color("cardColor", "Card color", "", "Colors", "Empty = a soft shade of the accent."),
        OptionSpec.Color("againColor", "Again button", "#E07A6A", "Colors"),
        OptionSpec.Color("goodColor", "Good button", "#9B9A5E", "Colors"),
        OptionSpec.Color("easyColor", "Easy button", "#5E9BA6", "Colors"),
        OptionSpec.Choice("direction", "Ask", "front", ["front", "back", "both"], ["Front first", "Back first (reverse)", "Both directions"], "Behavior", "Reverse cards keep their own progress."),
        OptionSpec.Choice("order", "Card order", "shuffle", ["shuffle", "deck"], ["Shuffled", "Deck order"], "Behavior"),
        OptionSpec.Number("newPerDay", "New cards per day", 10, 0, 200, "Behavior"),
        OptionSpec.Toggle("reviewsFirst", "Reviews before new cards", true, "Behavior"),
        OptionSpec.Text("intervals", "Days between reviews", "1, 2, 4, 8, 16", "Behavior", "Five numbers, one for each box from first to last."),
        OptionSpec.Toggle("retryAgain", "Bring \"Again\" cards back today", true, "Behavior"),
    ];

    public IEnumerable<Action<LauncherItem>> Samples =>
    [
        item =>
        {
            item.Options["deck2Name"] = "Spanish";
            item.Options["deck2"] = SpanishDeck;
            SeedProgress(item, 12, 4);
        },
        item =>
        {
            item.Options["direction"] = "back";
            item.Options["data.preview"] = "flipped";
            item.Accent = "#5E9BA6";
            item.Width = 276; item.Height = 252;
            SeedProgress(item, 6, 2);
        },
        item =>
        {
            // Everything done for today.
            item.Options["newPerDay"] = "0";
            SeedProgress(item, 34, 9, due: false);
            item.Width = 372; item.Height = 228;
        },
    ];

    private static void SeedProgress(LauncherItem item, int learned, int reviewedToday, bool due = true)
    {
        var today = DateTime.Today;
        string direction = item.Options.GetValueOrDefault("direction", "front");
        var entries = FlashcardsLogic.Entries(FlashcardsLogic.ParseDeck(LatinDeck.Split('\n')), direction);
        var progress = new Dictionary<string, FlashcardsLogic.Progress>();
        for (int i = 0; i < Math.Min(learned, entries.Count); i++)
        {
            int box = 1 + i % 5;
            int wait = due ? (i % 3 == 0 ? -1 : i % 3 == 1 ? 2 : 6) : 3 + i % 9;
            progress[entries[i].Key] = new FlashcardsLogic.Progress { Box = box, Reps = 2 + i % 4, Due = FlashcardsLogic.Day(today.AddDays(wait)), Stamp = i + 1 };
        }
        item.Options[FlashcardsLogic.ProgressKey] = FlashcardsLogic.SerializeProgress(progress);
        item.Options[FlashcardsLogic.DailyKey] = FlashcardsLogic.SerializeDaily(new FlashcardsLogic.Daily { Date = FlashcardsLogic.Day(today), Reviews = reviewedToday, New = Math.Min(reviewedToday, 3), Again = 1 });
    }

    // ---------- Decks ----------

    private static List<FlashcardsLogic.Card> Cards(WidgetContext ctx)
    {
        string mode = ctx.Get("deckMode");
        var cards = new List<FlashcardsLogic.Card>();
        if (mode != "2") cards.AddRange(FlashcardsLogic.ParseDeck(ctx.GetLines("deck1"), 0));
        if (mode != "1") cards.AddRange(FlashcardsLogic.ParseDeck(ctx.GetLines("deck2"), 1));
        return cards;
    }

    private static string DeckName(WidgetContext ctx, int deck)
    {
        string name = ctx.Get(deck == 0 ? "deck1Name" : "deck2Name").Trim();
        return name.Length > 0 ? name : deck == 0 ? "Deck 1" : "Deck 2";
    }

    private static string Seed(WidgetContext ctx) => FlashcardsLogic.Day(DateTime.Today) + ":" + ctx.State("fc.seed", 0);

    private static void Grade(WidgetContext ctx, FlashcardsLogic.Entry entry, FlashcardsLogic.Grade grade)
    {
        var today = DateTime.Today;
        var progress = FlashcardsLogic.ParseProgress(ctx.Get(FlashcardsLogic.ProgressKey));
        var daily = FlashcardsLogic.ParseDaily(ctx.Get(FlashcardsLogic.DailyKey), today);
        bool wasNew = FlashcardsLogic.IsNew(progress, entry.Key);
        progress.TryGetValue(entry.Key, out var before);
        progress[entry.Key] = FlashcardsLogic.Review(before, grade, today, FlashcardsLogic.ParseIntervals(ctx.Get("intervals")), ctx.GetBool("retryAgain"), FlashcardsLogic.NextStamp(progress));
        FlashcardsLogic.RecordReview(daily, wasNew, grade);
        ctx.Set(FlashcardsLogic.DailyKey, FlashcardsLogic.SerializeDaily(daily), rebuild: false);
        ctx.Set(FlashcardsLogic.ProgressKey, FlashcardsLogic.SerializeProgress(progress), rebuild: false);
        ctx.SetState("fc.flipped", false);
    }

    public IEnumerable<WidgetAction> Actions(WidgetContext ctx)
    {
        yield return new WidgetAction("Shuffle the cards", "", () => { ctx.SetState("fc.seed", ctx.State("fc.seed", 0) + 1, false); ctx.SetState("fc.flipped", false); });
        yield return new WidgetAction("Add a card…", Theme.Glyphs.Add, () =>
        {
            string? text = ctx.Ask("New card", "front | back");
            if (text is null) return;
            var parsed = FlashcardsLogic.ParseDeck([text]);
            if (parsed.Count == 0) { ctx.Status("A card needs a front and a back, separated by |"); return; }
            string key = ctx.Get("deckMode") == "2" ? "deck2" : "deck1";
            string existing = ctx.Get(key).TrimEnd();
            ctx.Set(key, (existing.Length == 0 ? "" : existing + "\n") + text.Trim());
        });
        yield return new WidgetAction("Study ahead", Theme.Glyphs.Next, () => ctx.SetState("fc.more", !ctx.State("fc.more", false)));
        yield return new WidgetAction("Reset progress…", Theme.Glyphs.Reset, () =>
        {
            if (!ctx.Confirm("Forget all progress and start every card from the beginning?", ctx.Item.Name)) return;
            ctx.Item.Options.Remove(FlashcardsLogic.ProgressKey);
            ctx.Item.Options.Remove(FlashcardsLogic.DailyKey);
            ctx.SetState("fc.flipped", false, false);
            ctx.Changed();
        });
    }

    // ---------- Drawing ----------

    public FrameworkElement Build(WidgetContext ctx)
    {
        var today = DateTime.Today;
        var cards = Cards(ctx);
        var entries = FlashcardsLogic.Entries(cards, ctx.Get("direction"));
        var progress = FlashcardsLogic.ParseProgress(ctx.Get(FlashcardsLogic.ProgressKey));
        var daily = FlashcardsLogic.ParseDaily(ctx.Get(FlashcardsLogic.DailyKey), today);
        var intervals = FlashcardsLogic.ParseIntervals(ctx.Get("intervals"));
        bool retry = ctx.GetBool("retryAgain");
        var plan = FlashcardsLogic.MakePlan(entries, progress, daily, today, ctx.GetInt("newPerDay"), ctx.Get("order"), Seed(ctx), ctx.GetBool("reviewsFirst"));
        bool studyingAhead = ctx.State("fc.more", false) && plan.Queue.Count == 0;
        var current = plan.Next ?? (studyingAhead && plan.Extra.Count > 0 ? plan.Extra[0] : null);
        bool flipped = (ctx.State("fc.flipped", false) || (ctx.IsSnapshot && ctx.Get("data.preview") == "flipped")) && current is not null;
        bool tight = ctx.Height < 250;

        // Header: due count and a shuffle button.
        FrameworkElement? badge = null;
        if (entries.Count > 0)
        {
            int due = plan.Queue.Count;
            badge = due > 0 ? StudyKit.PillBox(ctx, due == 1 ? "1 to go" : $"{due} to go", ctx.Item.Accent) : WidgetKit.Pill(ctx, "done");
        }
        var shuffle = WidgetKit.IconButton("", "Shuffle the cards", () => { ctx.SetState("fc.seed", ctx.State("fc.seed", 0) + 1, false); ctx.SetState("fc.flipped", false); }, 24);
        var (card, body) = WidgetKit.Frame(ctx, badge, shuffle);

        if (entries.Count == 0)
        {
            var empty = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(8) };
            empty.Children.Add(WidgetKit.Icon("", 28, Theme.Dim));
            var note = WidgetKit.Muted(ctx, "No cards yet. Open Customize and type one card per line as front | back.", 12);
            note.TextWrapping = TextWrapping.Wrap; note.TextAlignment = TextAlignment.Center; note.Margin = new Thickness(0, 8, 0, 8);
            empty.Children.Add(note);
            empty.Children.Add(WidgetKit.Button("Customize…", ctx.Customize));
            body.Children.Add(empty);
            return card;
        }

        // Footer: the three answer buttons (or "Show answer").
        var footer = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom);
        if (current is not null)
        {
            progress.TryGetValue(current.Key, out var currentProgress);
            if (!flipped)
            {
                var show = Theme.Label(studyingAhead ? "Show answer (studying ahead)" : "Show answer", ctx.Font(12), Theme.Text2);
                show.FontWeight = FontWeights.SemiBold;
                show.HorizontalAlignment = HorizontalAlignment.Center;
                var flip = new Border { CornerRadius = new CornerRadius(10), Background = Theme.Surface3, Padding = new Thickness(0, tight ? 6 : 9, 0, tight ? 6 : 9), Child = show };
                WidgetKit.Clickable(flip, () => ctx.SetState("fc.flipped", true), "Flip the card");
                footer.Children.Add(flip);
            }
            else
            {
                var buttons = new (string Label, FlashcardsLogic.Grade Grade, string Hex)[]
                {
                    ("Again", FlashcardsLogic.Grade.Again, StudyKit.ValidHex(ctx.Get("againColor"), "#E07A6A")),
                    ("Good", FlashcardsLogic.Grade.Good, StudyKit.ValidHex(ctx.Get("goodColor"), "#9B9A5E")),
                    ("Easy", FlashcardsLogic.Grade.Easy, StudyKit.ValidHex(ctx.Get("easyColor"), "#5E9BA6")),
                };
                for (int i = 0; i < 3; i++)
                {
                    footer.ColumnDefinitions.Add(new ColumnDefinition());
                    var (label, grade, hex) = buttons[i];
                    var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                    var name = Theme.Label(label, ctx.Font(12.5), Theme.Brush(hex));
                    name.FontWeight = FontWeights.SemiBold;
                    name.HorizontalAlignment = HorizontalAlignment.Center;
                    stack.Children.Add(name);
                    if (ctx.GetBool("showIntervals") && !tight)
                    {
                        var when = Theme.Label(FlashcardsLogic.IntervalText(FlashcardsLogic.PreviewDays(currentProgress, grade, intervals, retry)), ctx.Font(10), Theme.Muted);
                        when.HorizontalAlignment = HorizontalAlignment.Center;
                        stack.Children.Add(when);
                    }
                    var button = new Border { CornerRadius = new CornerRadius(10), Background = Theme.Tint(hex, 0.16), Padding = new Thickness(0, tight ? 6 : 6, 0, tight ? 6 : 6), Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 0), Child = stack };
                    Grid.SetColumn(button, i);
                    var entry = current;
                    WidgetKit.Clickable(button, () => Grade(ctx, entry, grade), label);
                    footer.Children.Add(button);
                }
            }
        }
        else
        {
            footer.Children.Add(WidgetKit.Button(plan.Extra.Count > 0 ? "Study ahead" : "Shuffle again", () => { if (plan.Extra.Count > 0) ctx.SetState("fc.more", true); else { ctx.SetState("fc.seed", ctx.State("fc.seed", 0) + 1, false); ctx.Rebuild(); } }));
        }
        body.Children.Add(footer);

        // Stats strip.
        if (ctx.GetBool("showStats") && ctx.Height >= 200)
        {
            var stats = new Grid { Margin = new Thickness(0, 0, 0, 7) };
            stats.ColumnDefinitions.Add(new ColumnDefinition());
            stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
            string left = $"{plan.DueCount} due · {plan.NewLeftToday} new";
            var text = WidgetKit.Muted(ctx, left, 11);
            var bar = WidgetKit.Bar(FlashcardsLogic.TodayFraction(daily, plan), ctx.Accent, 5);
            bar.VerticalAlignment = VerticalAlignment.Center;
            bar.Margin = new Thickness(10, 0, 0, 0);
            bar.ToolTip = $"{daily.Reviews} reviewed today";
            Grid.SetColumn(bar, 1);
            stats.Children.Add(text);
            stats.Children.Add(bar);
            DockPanel.SetDock(stats, Dock.Top);
            body.Children.Add(stats);
        }

        // Deck chips (two decks).
        bool twoDecks = ctx.GetLines("deck2").Length > 0 && ctx.GetLines("deck1").Length > 0;
        if (twoDecks && ctx.Width >= 240)
        {
            var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            string mode = ctx.Get("deckMode");
            chips.Children.Add(StudyKit.Chip(ctx, "Both", ctx.Item.Accent, mode == "all", () => ctx.Set("deckMode", "all"), size: 10.5));
            chips.Children.Add(StudyKit.Chip(ctx, DeckName(ctx, 0), ctx.Item.Accent, mode == "1", () => ctx.Set("deckMode", "1"), size: 10.5));
            chips.Children.Add(StudyKit.Chip(ctx, DeckName(ctx, 1), ctx.Item.Accent, mode == "2", () => ctx.Set("deckMode", "2"), size: 10.5));
            DockPanel.SetDock(chips, Dock.Top);
            body.Children.Add(chips);
        }

        // The card.
        body.Children.Add(current is null ? DoneCard(ctx, plan, daily, entries.Count) : CardFace(ctx, current, progress, flipped, studyingAhead, tight));
        ctx.Tick(() => false, _ => ctx.Rebuild());
        return card;
    }

    private static FrameworkElement CardFace(WidgetContext ctx, FlashcardsLogic.Entry entry, Dictionary<string, FlashcardsLogic.Progress> progress, bool flipped, bool ahead, bool tight)
    {
        string hex = ctx.Item.Accent;
        var face = StudyKit.Hex(ctx.Get("cardColor"), Theme.Tint(hex, 0.12));
        double innerWidth = Math.Max(60, ctx.Width - 2 * ctx.GetNumber("_padding") - 2 - 28);
        double baseSize = ctx.GetNumber("cardTextSize");

        var grid = new Grid();
        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        string main = flipped ? entry.Answer : entry.Prompt;
        double size = baseSize * (main.Length <= 10 ? 1.25 : main.Length <= 28 ? 1.0 : main.Length <= 60 ? 0.78 : 0.62);
        if (flipped)
        {
            var asked = WidgetKit.Muted(ctx, entry.Prompt.Replace('\n', ' '), 11.5);
            asked.HorizontalAlignment = HorizontalAlignment.Center;
            asked.MaxWidth = innerWidth;
            content.Children.Add(asked);
            content.Children.Add(new Border { Height = 1, Width = 36, Background = Theme.LineStrong, Margin = new Thickness(0, 5, 0, 7), HorizontalAlignment = HorizontalAlignment.Center });
        }
        var big = new TextBlock { Text = main, FontFamily = Theme.UiFont, FontSize = Math.Max(10, size * ctx.TextScale), FontWeight = FontWeights.SemiBold, Foreground = ctx.TextBrush, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = innerWidth };
        content.Children.Add(big);
        var fit = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = content, Margin = new Thickness(14, tight ? 14 : 18, 14, tight ? 8 : 10) };
        grid.Children.Add(fit);

        // Corner labels.
        var top = new DockPanel { Margin = new Thickness(10, 6, 10, 0), VerticalAlignment = VerticalAlignment.Top, LastChildFill = false };
        var side = Theme.Label(flipped ? (entry.Reversed ? "FRONT" : "BACK") : (entry.Reversed ? "BACK" : "FRONT"), ctx.Font(9), Theme.Dim);
        side.FontWeight = FontWeights.SemiBold;
        DockPanel.SetDock(side, Dock.Left);
        top.Children.Add(side);
        bool isNew = FlashcardsLogic.IsNew(progress, entry.Key);
        if (isNew || ctx.GetBool("showBox"))
        {
            progress.TryGetValue(entry.Key, out var p);
            var tag = Theme.Label(isNew ? "NEW" : $"BOX {p?.Box}", ctx.Font(9), isNew ? ctx.Accent : Theme.Muted);
            tag.FontWeight = FontWeights.SemiBold;
            DockPanel.SetDock(tag, Dock.Right);
            top.Children.Add(tag);
        }
        grid.Children.Add(top);

        var border = new Border { CornerRadius = new CornerRadius(12), Background = face, BorderBrush = Theme.Tint(hex, 0.35), BorderThickness = new Thickness(1), Child = grid };
        WidgetKit.Clickable(border, () => ctx.SetState("fc.flipped", !flipped), flipped ? "Click to see the question again" : "Click to flip");
        return border;
    }

    private static FrameworkElement DoneCard(WidgetContext ctx, FlashcardsLogic.Plan plan, FlashcardsLogic.Daily daily, int total)
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var icon = WidgetKit.Icon(Theme.Glyphs.Check, 26, Theme.Olive);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(icon);
        var title = WidgetKit.Text(ctx, "All done for today", 15, null, true);
        title.HorizontalAlignment = HorizontalAlignment.Center; title.Margin = new Thickness(0, 6, 0, 2);
        stack.Children.Add(title);
        int mastered = plan.BoxCounts[5];
        var sub = WidgetKit.Muted(ctx, $"{daily.Reviews} reviewed · {mastered} of {total} in the top box", 11.5);
        sub.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(sub);
        if (plan.Extra.Count > 0)
        {
            var more = WidgetKit.Muted(ctx, $"{plan.Extra.Count} more waiting for another day", 11);
            more.HorizontalAlignment = HorizontalAlignment.Center; more.Margin = new Thickness(0, 2, 0, 0);
            stack.Children.Add(more);
        }
        return new Border { CornerRadius = new CornerRadius(12), Background = Theme.Surface3, Child = stack };
    }
}
