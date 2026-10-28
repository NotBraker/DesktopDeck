using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesktopDeck.Models;

namespace DesktopDeck.Widgets;

/// <summary>Today's focus: one big statement for the day, a done toggle and the last few days' focuses.</summary>
internal sealed class FocusWidget : IWidget
{
    private const string DataKey = "data.focus";

    public string Kind => "focus";
    public string Name => "Today's focus";
    public string Description => "One big statement for today, with a done toggle and history";
    public string Section => "Productivity";
    public string Glyph => "";
    public string Accent => "#f59e0b";
    public (double Width, double Height) DefaultSize => (372, 228);
    public (double Width, double Height) MinimumSize => (228, 132);

    public IReadOnlyList<OptionSpec> Options { get; } =
    [
        OptionSpec.Toggle("showDate", "Show today's date", true, "What to show"),
        OptionSpec.Toggle("showSubtitle", "Show the subtitle", true, "What to show"),
        OptionSpec.Toggle("showDoneButton", "Show the done button", true, "What to show"),
        OptionSpec.Number("history", "Past focuses shown", 3, 0, 7, "What to show", "Shown under the statement when there is room; 0 hides them."),
        OptionSpec.Toggle("showStreak", "Show the done streak", true, "What to show"),
        OptionSpec.Number("fontScale", "Statement size", 1, 0.5, 3, "Layout", "The statement also shrinks to fit the card."),
        OptionSpec.Choice("align", "Alignment", "center", ["left", "center", "right"], ["Left", "Center", "Right"], "Layout"),
        OptionSpec.Choice("quote", "Quote style", "none", ["none", "quotes", "bar", "mark"], ["Plain", "Curly quotes", "Accent bar", "Big quote mark"], "Layout"),
        OptionSpec.Choice("weight", "Statement weight", "semibold", ["normal", "semibold", "bold"], ["Regular", "Semibold", "Bold"], "Layout"),
        OptionSpec.Color("focusColor", "Statement color", "", "Colors", "Empty = the text color."),
        OptionSpec.Color("markColor", "Quote mark / bar color", "", "Colors", "Empty = accent color."),
        OptionSpec.Color("doneColor", "Done color", "#9B9A5E", "Colors"),
        OptionSpec.Toggle("autoClear", "Start fresh at midnight", true, "Behavior", "Yesterday's focus moves to the history."),
        OptionSpec.Toggle("prompt", "Show a prompt when empty", true, "Behavior"),
        OptionSpec.Text("promptText", "Prompt text", "What's the one thing that would make today a win?", "Behavior"),
        OptionSpec.Toggle("askSubtitle", "Ask for a subtitle too", false, "Behavior", "After setting the focus, ask for a short note."),
    ];

    public IEnumerable<Action<LauncherItem>> Samples =>
    [
        item =>
        {
            var today = DateTime.Today;
            var state = new FocusLogic.FocusState
            {
                Text = "Ship the Q4 planning slides", Subtitle = "Draft by noon · review with Sam at 3", Date = FocusLogic.Key(today),
                History =
                [
                    new() { Date = FocusLogic.Key(today.AddDays(-1)), Text = "Clear the support backlog", Done = true },
                    new() { Date = FocusLogic.Key(today.AddDays(-2)), Text = "Interview two candidates", Done = true },
                    new() { Date = FocusLogic.Key(today.AddDays(-3)), Text = "Finish the budget draft", Done = false },
                ],
            };
            item.Options[DataKey] = FocusLogic.Serialize(state);
        },
        item =>
        {
            var today = DateTime.Today;
            var state = new FocusLogic.FocusState { Text = "Run 10 km before lunch", Date = FocusLogic.Key(today), Done = true };
            state.History.Add(new() { Date = FocusLogic.Key(today.AddDays(-1)), Text = "Stretch for 20 minutes", Done = true });
            item.Options[DataKey] = FocusLogic.Serialize(state);
            item.Options["quote"] = "bar";
            item.Options["align"] = "left";
            item.Options["fontScale"] = "1.3";
            item.Options["history"] = "0";
            item.Accent = "#22c55e";
            item.Width = 276; item.Height = 276;
        },
        item =>
        {
            item.Options["prompt"] = "true";
            item.Accent = "#8b5cf6";
            item.Width = 276; item.Height = 180;
        },
        item =>
        {
            item.Options[DataKey] = FocusLogic.Serialize(new FocusLogic.FocusState { Text = "Write the first chapter", Subtitle = "Two hours, no email", Date = FocusLogic.Key(DateTime.Today) });
            item.Options["quote"] = "mark";
            item.Options["align"] = "left";
            item.Options["weight"] = "bold";
            item.Options["fontScale"] = "1.6";
            item.Width = 468; item.Height = 276;
        },
    ];

    private static FocusLogic.FocusState Load(WidgetContext ctx, DateTime today)
    {
        var state = FocusLogic.Parse(ctx.Get(DataKey));
        FocusLogic.Rollover(state, today, ctx.GetBool("autoClear"), ctx.HistoryDays(HistoryKeys.Focus));
        return state;
    }

    /// <summary>Loads the saved state fresh, applies the day's rollover, changes it and saves (which redraws every copy).</summary>
    private static void Update(WidgetContext ctx, Action<FocusLogic.FocusState> change)
    {
        var state = Load(ctx, DateTime.Today);
        change(state);
        ctx.SetState("Focus.undo", ctx.Get(DataKey), false);
        ctx.Set(DataKey, FocusLogic.Serialize(state));
    }

    private static void AskFocus(WidgetContext ctx)
    {
        var current = Load(ctx, DateTime.Today);
        if (ctx.Ask("Today's focus", "The one thing for today", current.Text) is not { } text || string.IsNullOrWhiteSpace(text)) return;
        string? subtitle = null;
        if (ctx.GetBool("askSubtitle")) subtitle = ctx.Ask("Today's focus", "A short note (optional)", current.Date == FocusLogic.Key(DateTime.Today) ? current.Subtitle : "");
        Update(ctx, s =>
        {
            FocusLogic.SetFocus(s, text, DateTime.Today, ctx.HistoryDays(HistoryKeys.Focus));
            if (subtitle is not null) s.Subtitle = subtitle.Trim();
        });
    }

    private static void AskSubtitle(WidgetContext ctx)
    {
        var current = Load(ctx, DateTime.Today);
        if (ctx.Ask("Subtitle", "A short note under the focus", current.Subtitle) is not { } text) return;
        Update(ctx, s => s.Subtitle = text.Trim());
    }

    public IEnumerable<WidgetAction> Actions(WidgetContext ctx)
    {
        if (ctx.State<string?>("Focus.undo", null) is { } old) yield return new WidgetAction("Undo last change", "", () => { ctx.SetState<string?>("Focus.undo", null, false); ctx.Set(DataKey, old); });

        var state = Load(ctx, DateTime.Today);
        yield return new WidgetAction(state.Text.Length == 0 ? "Set today's focus…" : "Change focus…", "", () => AskFocus(ctx));
        if (state.Text.Length == 0) yield break;
        yield return new WidgetAction(state.Done ? "Mark not done" : "Mark done", "", () => Update(ctx, s => s.Done = !s.Done));
        yield return new WidgetAction(state.Subtitle.Length == 0 ? "Add a subtitle…" : "Edit subtitle…", "", () => AskSubtitle(ctx));
        if (state.Subtitle.Length > 0) yield return new WidgetAction("Clear subtitle", null, () => Update(ctx, s => s.Subtitle = ""));
        yield return new WidgetAction("Clear focus", "", () => Update(ctx, FocusLogic.Clear));
    }

    public FrameworkElement Build(WidgetContext ctx)
    {
        var today = DateTime.Today;
        string todayKey = FocusLogic.Key(today);
        var state = Load(ctx, today);
        bool has = state.Text.Length > 0;
        double pad = ctx.GetNumber("_padding");
        double innerWidth = Math.Max(60, ctx.Width - pad * 2 - 6);
        var doneBrush = ctx.GetColor("doneColor", Theme.Olive);
        var markBrush = ctx.GetColor("markColor");
        string doneHex = ctx.Get("doneColor") is { Length: > 0 } dh ? dh : "#9B9A5E";

        FrameworkElement? badge = null;
        int streak = ctx.GetBool("showStreak") ? FocusLogic.DoneStreak(state, today) : 0;
        if (streak >= 2) badge = WidgetKit.Pill(ctx, $"{streak}-day streak", doneHex);
        else if (ctx.GetBool("showDate")) badge = WidgetKit.Muted(ctx, today.ToString("ddd, MMM d", CultureInfo.CurrentCulture), 11);
        var edit = WidgetKit.IconButton("", has ? "Change today's focus" : "Set today's focus", () => AskFocus(ctx));
        var (card, body) = WidgetKit.Frame(ctx, badge, edit);
        if (streak >= 2 && ctx.GetBool("showDate")) card.ToolTip = today.ToString("dddd, MMMM d", CultureInfo.CurrentCulture);

        // Footer: done toggle and the history, docked bottom; the statement fills what's left.
        int historyCount = ctx.GetInt("history");
        double headerHeight = ctx.Item.ShowHeader || badge is not null ? 34 : 0;
        double room = ctx.Height - pad * 2 - headerHeight;
        var past = FocusLogic.Recent(state, historyCount);
        int historyRows = (int)Math.Clamp(Math.Floor((room - 90 - (ctx.GetBool("showDoneButton") ? 30 : 0)) / ctx.Font(20)), 0, past.Count);
        if (historyRows > 0)
        {
            var list = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            list.Children.Add(new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(0, 0, 0, 5) });
            foreach (var day in past.Take(historyRows))
            {
                var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ctx.Font(38)) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ctx.Font(18)) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                string when = FocusLogic.TryDate(day.Date, out var d) ? d.ToString("ddd", CultureInfo.CurrentCulture) : "";
                row.Children.Add(WidgetKit.Muted(ctx, when, 11));
                FrameworkElement mark = day.Done
                    ? WidgetKit.Icon("", ctx.Font(11), doneBrush)
                    : new System.Windows.Shapes.Ellipse { Width = ctx.Font(8), Height = ctx.Font(8), Stroke = Theme.Dim, StrokeThickness = 1.3, VerticalAlignment = VerticalAlignment.Center };
                mark.HorizontalAlignment = HorizontalAlignment.Left;
                Grid.SetColumn(mark, 1);
                row.Children.Add(mark);
                var text = WidgetKit.Text(ctx, day.Text, 11.5, day.Done ? Theme.Text2 : Theme.Muted);
                text.ToolTip = day.Text;
                Grid.SetColumn(text, 2);
                row.Children.Add(text);
                list.Children.Add(row);
            }
            DockPanel.SetDock(list, Dock.Bottom);
            body.Children.Add(list);
        }

        if (has && ctx.GetBool("showDoneButton"))
        {
            var box = new Border
            {
                Width = ctx.Font(16), Height = ctx.Font(16), CornerRadius = new CornerRadius(ctx.Font(8)), BorderThickness = new Thickness(1.5),
                BorderBrush = state.Done ? doneBrush : Theme.Muted, Background = state.Done ? doneBrush : Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center,
            };
            if (state.Done) box.Child = WidgetKit.Icon("", ctx.Font(9), Theme.Background);
            var label = WidgetKit.Text(ctx, state.Done ? "Done — nice work" : "Mark done", 12, state.Done ? doneBrush : Theme.Muted);
            label.Margin = new Thickness(7, 0, 0, 0);
            var toggle = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.Transparent };
            toggle.Children.Add(box);
            toggle.Children.Add(label);
            toggle.HorizontalAlignment = ctx.Get("align") switch { "left" => HorizontalAlignment.Left, "right" => HorizontalAlignment.Right, _ => HorizontalAlignment.Center };
            toggle.Margin = new Thickness(0, 6, 0, 0);
            WidgetKit.Clickable(toggle, () => Update(ctx, s => s.Done = !s.Done), state.Done ? "Mark not done" : "Mark today's focus done");
            DockPanel.SetDock(toggle, Dock.Bottom);
            body.Children.Add(toggle);
        }

        var align = ctx.Get("align") switch { "left" => TextAlignment.Left, "right" => TextAlignment.Right, _ => TextAlignment.Center };
        var horizontal = align switch { TextAlignment.Left => HorizontalAlignment.Left, TextAlignment.Right => HorizontalAlignment.Right, _ => HorizontalAlignment.Center };
        string quote = ctx.Get("quote");
        var center = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        double textWidth = innerWidth - (quote == "bar" ? 14 : 0);
        if (has)
        {
            var statement = new TextBlock
            {
                Text = FocusLogic.Quote(state.Text, quote), FontFamily = Theme.UiFont, FontSize = ctx.Font(22 * ctx.GetNumber("fontScale")),
                Foreground = state.Done ? doneBrush : ctx.GetColor("focusColor", ctx.TextBrush), TextWrapping = TextWrapping.Wrap, TextAlignment = align,
                FontWeight = ctx.Get("weight") switch { "normal" => FontWeights.Normal, "bold" => FontWeights.Bold, _ => FontWeights.SemiBold },
                Width = textWidth, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            };
            statement.LineHeight = statement.FontSize * 1.18;
            if (state.Done) statement.TextDecorations = TextDecorations.Strikethrough;
            center.Children.Add(statement);
            if (ctx.GetBool("showSubtitle") && state.Subtitle.Length > 0)
            {
                var sub = WidgetKit.Muted(ctx, state.Subtitle, 12);
                sub.TextAlignment = align;
                sub.HorizontalAlignment = horizontal;
                sub.TextWrapping = TextWrapping.Wrap;
                sub.MaxWidth = textWidth;
                sub.Margin = new Thickness(0, 6, 0, 0);
                center.Children.Add(sub);
            }
        }
        else
        {
            string text = ctx.GetBool("prompt") ? ctx.Get("promptText") : "No focus set";
            var prompt = new TextBlock
            {
                Text = text, FontFamily = Theme.UiFont, FontSize = ctx.Font(16 * Math.Min(1.4, ctx.GetNumber("fontScale"))), Foreground = Theme.Muted, FontStyle = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap, TextAlignment = align, Width = textWidth,
            };
            center.Children.Add(prompt);
            var hint = WidgetKit.Text(ctx, "Click to set today's focus", 11.5, ctx.Accent);
            hint.HorizontalAlignment = horizontal;
            hint.Margin = new Thickness(0, 8, 0, 0);
            center.Children.Add(hint);
        }

        // The statement shrinks (never grows) to fit whatever room is left.
        var fit = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = center, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = horizontal };
        FrameworkElement content = fit;
        if (quote == "bar" && has)
        {
            var bar = new Border { BorderBrush = markBrush, BorderThickness = new Thickness(3.5, 0, 0, 0), Padding = new Thickness(11, 2, 0, 2), Child = fit, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = horizontal };
            content = bar;
        }
        var stage = new Grid { Background = Brushes.Transparent, ClipToBounds = true };
        if (quote == "mark" && has)
        {
            var mark = new TextBlock { Text = "“", FontFamily = new FontFamily("Georgia"), FontSize = ctx.Font(110), Foreground = markBrush, Opacity = 0.28, Margin = new Thickness(-6, -34, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
            stage.Children.Add(mark);
        }
        stage.Children.Add(content);
        WidgetKit.Clickable(stage, () => AskFocus(ctx), has ? "Click to change today's focus" : "Click to set today's focus");
        body.Children.Add(stage);

        // A new day may clear the focus: redraw when the date changes.
        card.Loaded += (_, _) => { if (!ctx.IsSnapshot) { string json = FocusLogic.Serialize(state); if (json != ctx.Get(DataKey)) ctx.Set(DataKey, json, false); } };
        ctx.Tick(() => false, now => { if (FocusLogic.Key(now.LocalDateTime) != todayKey) { var fresh = Load(ctx, now.LocalDateTime.Date); ctx.Set(DataKey, FocusLogic.Serialize(fresh)); } });
        return card;
    }
}


