using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesktopDeck.Models;

namespace DesktopDeck.Widgets;

/// <summary>A morning or evening routine: steps to tick off that reset each day at a chosen time, with progress and a streak.</summary>
internal sealed class RoutineWidget : IWidget
{
    private const string DataKey = "data.routine";

    public string Kind => "routine";
    public string Name => "Morning routine";
    public string Description => "A daily checklist that resets itself, with progress and a streak";
    public string Section => "Productivity";
    public string Glyph => "";
    public string Accent => "#14b8a6";
    public (double Width, double Height) DefaultSize => (276, 324);
    public (double Width, double Height) MinimumSize => (228, 180);

    public IReadOnlyList<OptionSpec> Options { get; } =
    [
        OptionSpec.Lines("steps", "Steps", "Drink a glass of water\nStretch | 5 min\nMake the bed\nPlan the day | 10 min\nNo phone for 30 min", "What to show", "One step per line. Add \"| 5 min\" for a note on the right."),
        OptionSpec.Toggle("showProgress", "Show progress", true, "What to show"),
        OptionSpec.Toggle("showStreak", "Show the streak", true, "What to show"),
        OptionSpec.Toggle("showDetails", "Show step notes", true, "What to show"),
        OptionSpec.Choice("progressStyle", "Progress style", "bar", ["bar", "ring", "count"], ["Bar", "Ring", "Count only"], "Layout"),
        OptionSpec.Choice("doneStyle", "Finished steps", "strike", ["strike", "dim", "bottom", "hide"], ["Strike through", "Dim", "Move to the bottom", "Hide"], "Layout"),
        OptionSpec.Toggle("numbered", "Number the steps", false, "Layout"),
        OptionSpec.Choice("checkShape", "Checkbox shape", "rounded", ["rounded", "circle", "square"], ["Rounded", "Circle", "Square"], "Layout"),
        new OptionSpec("resetTime", "New day starts at", OptionType.Time, "04:00") { Group = "Behavior", Hint = "Ticks clear at this time each day." },
        OptionSpec.Toggle("activeOnly", "Only during active hours", false, "Behavior"),
        new OptionSpec("activeFrom", "Active from", OptionType.Time, "06:00") { Group = "Behavior" },
        new OptionSpec("activeTo", "Active until", OptionType.Time, "10:00") { Group = "Behavior" },
        OptionSpec.Choice("outside", "Outside active hours", "dim", ["dim", "summary"], ["Dim the list", "Show a summary only"], "Behavior"),
        OptionSpec.Toggle("celebrate", "Celebrate when complete", true, "Behavior"),
        OptionSpec.Text("celebration", "Celebration message", "Routine complete — have a great day!", "Behavior"),
        OptionSpec.Color("checkColor", "Checkbox color", "", "Colors", "Empty = accent color."),
        OptionSpec.Color("barColor", "Progress color", "", "Colors", "Empty = accent color."),
        OptionSpec.Color("celebrateColor", "Celebration color", "#9B9A5E", "Colors"),
    ];

    private static string SampleState(int doneToday, int streakDays, IReadOnlyList<string> steps, TimeSpan reset)
    {
        var day = RoutineLogic.RoutineDay(DateTime.Now, reset);
        var state = new RoutineLogic.RoutineState { Day = RoutineLogic.Key(day), Done = steps.Take(doneToday).ToList() };
        for (int i = streakDays; i >= 1; i--) state.Completed.Add(RoutineLogic.Key(day.AddDays(-i)));
        if (doneToday >= steps.Count) state.Completed.Add(state.Day);
        return RoutineLogic.Serialize(state);
    }

    public IEnumerable<Action<LauncherItem>> Samples =>
    [
        item => item.Options[DataKey] = SampleState(2, 6, ["Drink a glass of water", "Stretch", "Make the bed", "Plan the day", "No phone for 30 min"], TimeSpan.FromHours(4)),
        item =>
        {
            item.Name = "Evening wind-down";
            item.Accent = "#8b5cf6";
            item.Options["steps"] = "Tidy the desk\nWrite tomorrow's top 3\nRead | 20 min\nLights out by 11";
            item.Options["progressStyle"] = "ring";
            item.Options["numbered"] = "true";
            item.Options["checkShape"] = "circle";
            item.Options[DataKey] = SampleState(4, 12, ["Tidy the desk", "Write tomorrow's top 3", "Read", "Lights out by 11"], TimeSpan.FromHours(4));
        },
        item =>
        {
            item.Options["doneStyle"] = "bottom";
            item.Options["activeOnly"] = "true";
            var now = DateTime.Now.TimeOfDay;
            // Make the sample sit outside its active hours, whatever time the smoke test runs.
            var from = TimeSpan.FromHours((now.Hours + 2) % 24);
            item.Options["activeFrom"] = from.ToString("hh\\:mm", CultureInfo.InvariantCulture);
            item.Options["activeTo"] = TimeSpan.FromHours((now.Hours + 5) % 24).ToString("hh\\:mm", CultureInfo.InvariantCulture);
            item.Options[DataKey] = SampleState(3, 2, ["Drink a glass of water", "Stretch", "Make the bed", "Plan the day", "No phone for 30 min"], TimeSpan.FromHours(4));
            item.Width = 324; item.Height = 276;
        },
    ];

    private static TimeSpan Reset(WidgetContext ctx) => RoutineLogic.ParseTime(ctx.Get("resetTime"), TimeSpan.FromHours(4));

    private static bool Active(WidgetContext ctx, DateTime now) =>
        !ctx.GetBool("activeOnly") || RoutineLogic.IsActive(now.TimeOfDay, RoutineLogic.ParseTime(ctx.Get("activeFrom"), TimeSpan.FromHours(6)), RoutineLogic.ParseTime(ctx.Get("activeTo"), TimeSpan.FromHours(10)));

    private static void Update(WidgetContext ctx, Action<RoutineLogic.RoutineState, List<RoutineLogic.RoutineStep>> change)
    {
        var state = RoutineLogic.Parse(ctx.Get(DataKey));
        RoutineLogic.Normalize(state, DateTime.Now, Reset(ctx));
        change(state, RoutineLogic.Steps(ctx.GetLines("steps")));
        ctx.SetState("Routine.undo", ctx.Get(DataKey), false);
        ctx.Set(DataKey, RoutineLogic.Serialize(state));
    }

    public IEnumerable<WidgetAction> Actions(WidgetContext ctx) =>
    [
        new WidgetAction("Undo last routine change", "", () => { if (ctx.State<string?>("Routine.undo", null) is { } old) { ctx.SetState<string?>("Routine.undo", null, false); ctx.Set(DataKey, old); } }),
        new WidgetAction("Tick every step", "", () => Update(ctx, (s, steps) => RoutineLogic.SetAll(s, true, steps, DateTime.Now, Reset(ctx)))),
        new WidgetAction("Uncheck every step", "", () => Update(ctx, (s, steps) => RoutineLogic.SetAll(s, false, steps, DateTime.Now, Reset(ctx)))),
        new WidgetAction("Edit steps…", "", ctx.Customize),
    ];

    private static Border CheckBox(WidgetContext ctx, bool done, Brush color, string shape)
    {
        double size = ctx.Font(17);
        var box = new Border
        {
            Width = size, Height = size, BorderThickness = new Thickness(1.5), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(shape switch { "circle" => size / 2, "square" => 2, _ => 5 }),
            BorderBrush = done ? color : Theme.Muted, Background = done ? color : Brushes.Transparent,
        };
        if (done) box.Child = WidgetKit.Icon("", size * 0.58, Theme.Background);
        return box;
    }

    private static string Clock(TimeSpan time) => DateTime.Today.Add(time).ToString("t", CultureInfo.CurrentCulture);

    public FrameworkElement Build(WidgetContext ctx)
    {
        var now = DateTime.Now;
        var reset = Reset(ctx);
        var routineDay = RoutineLogic.RoutineDay(now, reset);
        var state = RoutineLogic.Parse(ctx.Get(DataKey));
        RoutineLogic.Normalize(state, now, reset);
        var steps = RoutineLogic.Steps(ctx.GetLines("steps"));
        int done = RoutineLogic.CountDone(state, steps);
        bool complete = RoutineLogic.IsComplete(state, steps);
        int streak = RoutineLogic.Streak(state, routineDay);
        bool active = Active(ctx, now);
        var check = ctx.GetColor("checkColor");
        var barBrush = ctx.GetColor("barColor");
        string celebrateHex = ctx.Get("celebrateColor") is { Length: > 0 } c ? c : "#9B9A5E";
        var celebrateBrush = ctx.GetColor("celebrateColor", Theme.Olive);
        double fraction = steps.Count == 0 ? 0 : (double)done / steps.Count;
        string style = ctx.Get("progressStyle");
        bool showProgress = ctx.GetBool("showProgress") && steps.Count > 0;

        // Header badge: streak and/or progress.
        var badges = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (ctx.GetBool("showStreak") && streak > 0)
        {
            var flame = WidgetKit.Pill(ctx, $"{streak}d streak", streak >= 3 ? celebrateHex : null);
            flame.ToolTip = $"{streak} day{(streak == 1 ? "" : "s")} in a row fully done · best {Math.Max(streak, RoutineLogic.Best(state))}";
            badges.Children.Add(flame);
        }
        if (showProgress)
        {
            if (style == "ring")
            {
                var label = WidgetKit.Text(ctx, $"{done}", 9.5, complete ? celebrateBrush : ctx.TextBrush, bold: true);
                label.HorizontalAlignment = HorizontalAlignment.Center;
                var ring = WidgetKit.Ring(fraction, complete ? celebrateBrush : barBrush, ctx.Font(24), 3, label);
                ring.Margin = new Thickness(6, 0, 0, 0);
                ring.ToolTip = $"{done} of {steps.Count} done";
                badges.Children.Add(ring);
            }
            else
            {
                var pill = WidgetKit.Pill(ctx, $"{done}/{steps.Count}", complete ? celebrateHex : null);
                pill.Margin = new Thickness(6, 0, 0, 0);
                badges.Children.Add(pill);
            }
        }
        var (card, body) = WidgetKit.Frame(ctx, badges.Children.Count > 0 ? badges : null);
        card.Loaded += (_, _) => { if (!ctx.IsSnapshot) { string json = RoutineLogic.Serialize(state); if (json != ctx.Get(DataKey)) ctx.Set(DataKey, json, false); } };

        if (steps.Count == 0)
        {
            var empty = WidgetKit.Empty(ctx, "No steps yet. Click here to add your routine's steps.");
            WidgetKit.Clickable(empty, ctx.Customize);
            body.Children.Add(empty);
            return card;
        }

        var activeFrom = RoutineLogic.ParseTime(ctx.Get("activeFrom"), TimeSpan.FromHours(6));
        if (!active && ctx.Get("outside") == "summary")
        {
            var summary = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var icon = WidgetKit.Icon("", ctx.Font(26), Theme.Muted);
            icon.Margin = new Thickness(0, 0, 0, 8);
            summary.Children.Add(icon);
            var line = WidgetKit.Text(ctx, complete ? "Done for today" : $"{done} of {steps.Count} done", 14, bold: true);
            line.HorizontalAlignment = HorizontalAlignment.Center;
            summary.Children.Add(line);
            var next = WidgetKit.Muted(ctx, $"Starts at {Clock(activeFrom)}");
            next.HorizontalAlignment = HorizontalAlignment.Center;
            next.Margin = new Thickness(0, 3, 0, 0);
            summary.Children.Add(next);
            body.Children.Add(summary);
            ScheduleTick(ctx, routineDay, active, reset);
            return card;
        }

        if (showProgress && style == "bar")
        {
            var bar = WidgetKit.Bar(fraction, complete ? celebrateBrush : barBrush, 6);
            bar.Margin = new Thickness(2, 0, 2, 8);
            DockPanel.SetDock(bar, Dock.Top);
            body.Children.Add(bar);
        }

        if (complete && ctx.GetBool("celebrate") && ctx.Get("celebration").Trim().Length > 0)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(WidgetKit.Icon("", ctx.Font(13), celebrateBrush));
            var text = WidgetKit.Text(ctx, ctx.Get("celebration").Trim(), 12, celebrateBrush, bold: true);
            text.Margin = new Thickness(8, 0, 0, 0);
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            row.Children.Add(text);
            var banner = new Border { Background = Theme.Tint(celebrateHex, 0.14), CornerRadius = new CornerRadius(9), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 6, 0, 0), Child = row, ToolTip = ctx.Get("celebration").Trim() };
            DockPanel.SetDock(banner, Dock.Bottom);
            body.Children.Add(banner);
        }

        if (!active)
        {
            var note = WidgetKit.Muted(ctx, $"Resting · starts at {Clock(activeFrom)}", 11);
            note.Margin = new Thickness(2, 6, 0, 0);
            DockPanel.SetDock(note, Dock.Bottom);
            body.Children.Add(note);
        }

        string doneStyle = ctx.Get("doneStyle");
        string shape = ctx.Get("checkShape");
        bool numbered = ctx.GetBool("numbered"), details = ctx.GetBool("showDetails");
        var ordered = steps.Select((s, i) => (Step: s, Index: i, Done: RoutineLogic.IsDone(state, s.Title))).ToList();
        if (doneStyle == "bottom") ordered = ordered.OrderBy(x => x.Done).ThenBy(x => x.Index).ToList();
        if (doneStyle == "hide") ordered = ordered.Where(x => !x.Done).ToList();
        var list = new StackPanel();
        foreach (var (step, index, isDone) in ordered)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ctx.Font(17) + 11) });
            if (numbered) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ctx.Font(20)) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(CheckBox(ctx, isDone, check, shape));
            int col = 1;
            if (numbered)
            {
                var number = WidgetKit.Muted(ctx, (index + 1).ToString(CultureInfo.CurrentCulture) + ".", 12);
                Grid.SetColumn(number, col++);
                grid.Children.Add(number);
            }
            bool faded = isDone && doneStyle is "strike" or "dim" or "bottom";
            var title = WidgetKit.Text(ctx, step.Title, 13, isDone ? Theme.Muted : ctx.TextBrush);
            if (isDone && doneStyle is "strike" or "bottom") title.TextDecorations = TextDecorations.Strikethrough;
            title.ToolTip = step.Title;
            Grid.SetColumn(title, col++);
            grid.Children.Add(title);
            if (details && step.Detail.Length > 0)
            {
                var detail = WidgetKit.Muted(ctx, step.Detail, 11);
                detail.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetColumn(detail, col);
                grid.Children.Add(detail);
            }
            var row = WidgetKit.Row(grid);
            row.Padding = new Thickness(6, 4, 6, 4);
            if (faded) row.Opacity = 0.8;
            string key = step.Title;
            WidgetKit.Clickable(row, () => Update(ctx, (s, all) => RoutineLogic.Toggle(s, key, all, DateTime.Now, Reset(ctx))));
            list.Children.Add(row);
        }
        if (ordered.Count == 0) list.Children.Add(WidgetKit.Empty(ctx, "Every step is done."));
        var scroll = WidgetKit.Scroll(list);
        if (!active) scroll.Opacity = 0.45;
        body.Children.Add(scroll);
        ScheduleTick(ctx, routineDay, active, reset);
        return card;
    }

    /// <summary>Redraws when the routine day turns over or the active window opens or closes.</summary>
    private void ScheduleTick(WidgetContext ctx, DateTime routineDay, bool active, TimeSpan reset) =>
        ctx.Tick(() => false, now =>
        {
            var local = now.LocalDateTime;
            if (RoutineLogic.RoutineDay(local, reset) != routineDay) { var fresh = RoutineLogic.Parse(ctx.Get(DataKey)); RoutineLogic.Normalize(fresh, local, reset); ctx.Set(DataKey, RoutineLogic.Serialize(fresh)); } else if (Active(ctx, local) != active) ctx.Rebuild();
        });
}


