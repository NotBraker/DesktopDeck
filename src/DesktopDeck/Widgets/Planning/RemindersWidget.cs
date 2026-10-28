using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopDeck.Models;
using DesktopDeck.Services;

namespace DesktopDeck.Widgets;

/// <summary>
/// Your personal reminders (the same list as the control center's Reminders tab): what needs an answer first, then what rings next.
/// The + button takes a sentence ("remind me at 5pm to call mom"). Click the circle for Done; right-click for Snooze, Edit, Delete.
/// </summary>
internal sealed class RemindersWidget : IWidget
{
    private const string Bell = "";

    public string Kind => "reminders";
    public string Name => "Reminders";
    public string Description => "Your upcoming reminders, with a + to add one in plain words";
    public string Section => "Planning";
    public string Glyph => Bell;
    public string Accent => "#ec4899";
    public (double Width, double Height) DefaultSize => (324, 276);
    public (double Width, double Height) MinimumSize => (204, 132);

    public IReadOnlyList<OptionSpec> Options { get; } =
    [
        OptionSpec.Number("maxItems", "Most reminders to show", 6, 1, 30, "What to show"),
        OptionSpec.Toggle("showMissed", "Show missed reminders first", true, "What to show", "Reminders that rang or came in while you were away and are not done yet."),
        OptionSpec.Toggle("showRepeating", "Show repeating reminders", true, "What to show"),
        OptionSpec.Toggle("groupHeaders", "Group by Today, Tomorrow and Later", false, "Layout"),
        OptionSpec.Toggle("showNote", "Show the note", false, "Layout"),
        OptionSpec.Toggle("showRelative", "Show \"in 20 min\" after the time", true, "Layout"),
        OptionSpec.Toggle("showRepeat", "Show how a reminder repeats", true, "Layout"),
        OptionSpec.Toggle("compact", "Compact (one line per reminder)", false, "Layout"),
        OptionSpec.Choice("timeFormat", "Time format", "app", ["app", "12h", "24h"], ["As set in Reminders", "12-hour", "24-hour"], "Layout"),
        OptionSpec.Toggle("showCheck", "Show the Done circle", true, "Behavior"),
        OptionSpec.Toggle("showAdd", "Show the + button", true, "Behavior"),
        OptionSpec.Color("missedColor", "Missed color", "#E07A6A", "Colors"),
    ];

    public IEnumerable<Action<LauncherItem>> Samples =>
    [
        _ => { },
        item => { item.Options["compact"] = "true"; item.Options["timeFormat"] = "24h"; item.Options["showRelative"] = "false"; item.Width = 276; item.Height = 276; },
        item => { item.Options["groupHeaders"] = "true"; item.Options["showNote"] = "true"; item.Options["maxItems"] = "9"; item.Width = 324; item.Height = 420; },
    ];

    public IEnumerable<WidgetAction> Actions(WidgetContext ctx)
    {
        yield return new WidgetAction("New reminder…", Theme.Glyphs.Add, () => Add(ctx));
        yield return new WidgetAction("Open all reminders", Bell, () => ctx.Reminders.OpenReminders());
    }

    private static void Add(WidgetContext ctx)
    {
        ctx.Reminders.PromptNewReminder((title, label, value) => ctx.Ask(title, label, value));
    }

    public FrameworkElement Build(WidgetContext ctx)
    {
        var now = DateTimeOffset.Now;
        TimeZoneInfo zone = ctx.IsSnapshot ? TimeZoneInfo.Local : ctx.Reminders.Engine.Zone;
        var items = ctx.IsSnapshot ? ReminderSamples.Make(now, zone) : ctx.Reminders.Engine.Items.Select(r => r).ToList();
        bool h24 = ctx.Get("timeFormat") switch { "24h" => true, "12h" => false, _ => !ctx.IsSnapshot && ctx.Reminders.ReminderOptions.Use24Hour };
        bool compact = ctx.GetBool("compact"), showNote = ctx.GetBool("showNote"), relative = ctx.GetBool("showRelative"), showRepeat = ctx.GetBool("showRepeat"), check = ctx.GetBool("showCheck"), headers = ctx.GetBool("groupHeaders");
        var missedBrush = ctx.GetColor("missedColor", Theme.Danger);

        var groups = ReminderGroups.Build(items, now, zone);
        var missed = ctx.GetBool("showMissed") ? groups.Missed : new List<Reminder>();
        var upcoming = groups.Today.Concat(groups.Tomorrow).Concat(groups.Later).Concat(ctx.GetBool("showRepeating") ? groups.Repeating : [])
            .OrderBy(r => ReminderService.RingAt(r) ?? r.Due).ToList();
        int max = ctx.GetInt("maxItems");

        FrameworkElement? badge = null;
        if (ctx.Width >= 228) badge = missed.Count > 0 ? WidgetKit.Pill(ctx, $"{missed.Count} missed", "#E07A6A") : WidgetKit.Pill(ctx, upcoming.Count == 0 ? "None" : $"{upcoming.Count} upcoming");
        var actions = new List<FrameworkElement>();
        if (ctx.GetBool("showAdd")) actions.Add(WidgetKit.IconButton(Theme.Glyphs.Add, "New reminder", () => Add(ctx), accent: true));
        var (card, body) = WidgetKit.Frame(ctx, badge, [.. actions]);

        if (missed.Count + upcoming.Count == 0)
        {
            body.Children.Add(WidgetKit.Empty(ctx, "Nothing coming up. Press + and type something like \"in 20 minutes stretch\"."));
            ctx.Tick(() => false, _ => ctx.Rebuild());
            return card;
        }

        var list = new StackPanel();
        int shown = 0;
        string lastGroup = "";
        void AddRow(Reminder r, bool isMissed)
        {
            if (shown >= max) return;
            shown++;
            var at = ReminderService.RingAt(r) ?? r.Due;
            if (headers && !isMissed)
            {
                int offset = (int)(ReminderSchedule.Wall(at, zone).Date - ReminderSchedule.Wall(now, zone).Date).TotalDays;
                string group = offset <= 0 ? "Today" : offset == 1 ? "Tomorrow" : "Later";
                if (group != lastGroup)
                {
                    lastGroup = group;
                    var head = WidgetKit.Text(ctx, group, compact ? 11 : 12, Theme.Muted, bold: true);
                    head.Margin = new Thickness(4, list.Children.Count == 0 ? 0 : 8, 0, 2);
                    list.Children.Add(head);
                }
            }
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(check ? 26 : 12) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            if (check)
            {
                var circle = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1.5), BorderBrush = isMissed ? missedBrush : Theme.LineStrong, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "Done" };
                var captured = r;
                circle.MouseEnter += (_, _) => circle.Background = Theme.Tint("#9B9A5E", 0.35);
                circle.MouseLeave += (_, _) => circle.Background = Brushes.Transparent;
                circle.MouseLeftButtonUp += (_, e) => { e.Handled = true; if (!ctx.IsSnapshot) ctx.Reminders.CompleteReminder(captured.Id); };
                grid.Children.Add(circle);
            }
            else grid.Children.Add(new Border { Width = 4, CornerRadius = new CornerRadius(2), Background = isMissed ? missedBrush : ctx.Accent, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 2) });
            string when = ReminderFormat.When(isMissed ? r.LastFired ?? r.Due : at, now, zone, h24);
            if (isMissed) when = (r.Attention == ReminderAttention.Missed ? "Missed " : "Rang ") + when;
            else if (relative) when += " · " + ReminderFormat.Relative(at, now);
            if (showRepeat && r.IsRepeating) when += " · " + ReminderFormat.RepeatText(r.Repeat);
            var text = new StackPanel();
            if (compact)
            {
                var line = new StackPanel { Orientation = Orientation.Horizontal };
                var time = WidgetKit.Muted(ctx, ReminderFormat.Time(ReminderSchedule.Wall(at, zone), h24), 11);
                time.Margin = new Thickness(0, 0, 8, 0);
                line.Children.Add(time);
                line.Children.Add(WidgetKit.Text(ctx, r.Text, 12.5, isMissed ? missedBrush : null));
                text.Children.Add(line);
            }
            else
            {
                text.Children.Add(WidgetKit.Text(ctx, r.Text, 13, isMissed ? missedBrush : null, bold: true));
                text.Children.Add(WidgetKit.Muted(ctx, when, 11.5));
            }
            if (showNote && r.Note.Length > 0) text.Children.Add(WidgetKit.Muted(ctx, r.Note, 11));
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            var row = WidgetKit.Row(grid);
            if (compact) row.Padding = new Thickness(6, 2, 6, 2);
            if (!ctx.IsSnapshot)
            {
                var target = r;
                WidgetKit.Clickable(row, () => ctx.Reminders.OpenReminders(), r.Note.Length > 0 ? r.Note : "Open the Reminders tab");
                row.ContextMenuOpening += (_, _) => row.ContextMenu = RowMenu(ctx, target);
            }
            list.Children.Add(row);
        }
        foreach (var r in missed) AddRow(r, true);
        foreach (var r in upcoming) AddRow(r, false);
        int total = missed.Count + upcoming.Count;
        if (total > shown)
        {
            var more = WidgetKit.Muted(ctx, $"+{total - shown} more", 11);
            more.Margin = new Thickness(6, 8, 0, 0);
            list.Children.Add(more);
        }
        body.Children.Add(WidgetKit.Scroll(list));
        ctx.Tick(() => false, _ => ctx.Rebuild());
        return card;
    }

    private static ContextMenu RowMenu(WidgetContext ctx, Reminder r)
    {
        var hub = ctx.Reminders;
        var menu = MainWindow.Menu();
        MainWindow.AddMenuItem(menu, "Done", () => hub.CompleteReminder(r.Id), Theme.Glyphs.Check);
        var snooze = new MenuItem { Header = "Snooze", Icon = Theme.Icon(Bell, 14, Theme.Text2) };
        foreach (var (choice, label) in new[] { (SnoozeChoice.Minutes5, "5 minutes"), (SnoozeChoice.Minutes10, "10 minutes"), (SnoozeChoice.Minutes30, "30 minutes"), (SnoozeChoice.Minutes60, "1 hour"), (SnoozeChoice.Tomorrow, "Tomorrow") })
        {
            var picked = choice;
            MainWindow.AddMenuItem(snooze, label, () => hub.SnoozeReminder(r.Id, picked));
        }
        menu.Items.Add(snooze);
        MainWindow.AddMenuItem(menu, "Edit…", () => Edit(ctx, r), Theme.Glyphs.Edit);
        MainWindow.AddMenuItem(menu, "Delete", () => hub.DeleteReminder(r.Id), Theme.Glyphs.Delete).Foreground = Theme.Danger;
        return menu;
    }

    /// <summary>Editing in plain words: the reminder as a sentence, read again when saved.</summary>
    private static void Edit(WidgetContext ctx, Reminder original)
    {
        var hub = ctx.Reminders;
        if (hub.Engine.Find(original.Id) is not { } r) return;
        string text = ReminderFormat.ToNatural(r, DateTimeOffset.Now, hub.Engine.Zone), label = "What and when?";
        while (true)
        {
            if (ctx.Ask("Edit reminder", label, text) is not { } answer) return;
            var outcome = hub.PreviewReminder(answer);
            if (outcome.Value is { } parsed)
            {
                hub.UpdateReminder(r.Id, parsed.Text, parsed.Note.Length > 0 ? parsed.Note : r.Note, parsed.Due, parsed.Repeat);
                return;
            }
            label = outcome.Error ?? label;
            text = answer;
        }
    }
}
