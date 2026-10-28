using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesktopDeck.Models;
using DesktopDeck.Services;

namespace DesktopDeck;

// The Reminders tab: a quick-add box that understands sentences (with a live preview), the lists, and the notification options.
internal sealed partial class ControlCenter
{
    private const string BellGlyph = "";
    private StackPanel? reminderLists;
    private bool showDoneReminders;

    private FrameworkElement BuildRemindersTab()
    {
        var stack = new StackPanel();
        stack.Children.Add(BuildQuickAdd());
        reminderLists = new StackPanel { Margin = new Thickness(0, PanelKit.S16, 0, 0) };
        stack.Children.Add(reminderLists);
        RenderReminderLists();
        var options = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
        BuildReminderOptions(options);
        stack.Children.Add(options);

        // Rings, new reminders and answers from toasts or the widget redraw the lists (the typed text stays).
        Action changed = () => { if (!closed) RenderReminderLists(); };
        hub.RemindersChanged += changed;
        Closed += (_, _) => hub.RemindersChanged -= changed;
        return stack;
    }

    // ---------- Quick add ----------

    private FrameworkElement BuildQuickAdd()
    {
        var card = new Border { Background = Theme.Surface, BorderBrush = Theme.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(PanelKit.S16) };
        var inner = new StackPanel();
        card.Child = inner;
        var heading = Theme.Label("Add a reminder", 14.5);
        heading.FontWeight = FontWeights.SemiBold;
        inner.Children.Add(heading);

        var row = new Grid { Margin = new Thickness(0, PanelKit.S12, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var box = Theme.TextField("");
        box.FontSize = 14;
        box.Tag = "reminder-add";
        var hint = Theme.Label("Remind me at 5pm to call mom", 14, Theme.Dim);
        hint.IsHitTestVisible = false;
        hint.Margin = new Thickness(12, 0, 0, 0);
        box.TextChanged += (_, _) => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var field = new Grid();
        field.Children.Add(box);
        field.Children.Add(hint);
        row.Children.Add(field);
        var add = Theme.Button("Add", prominent: true);
        add.Margin = new Thickness(PanelKit.S8, 0, 0, 0);
        add.MinWidth = 76;
        Grid.SetColumn(add, 1);
        row.Children.Add(add);
        inner.Children.Add(row);

        var preview = Theme.Label("", 12.5, Theme.Muted);
        preview.TextWrapping = TextWrapping.Wrap;
        preview.Margin = new Thickness(2, PanelKit.S8, 0, 0);
        inner.Children.Add(preview);
        void ShowHint() { preview.Text = "Try: in 20 minutes stretch · tomorrow 8am homework · every monday 7:30 trash"; preview.Foreground = Theme.Dim; }
        preview.ToolTip = "More examples: on oct 12 dentist · every 2 hours drink water";
        ShowHint();

        void Update()
        {
            if (box.Text.Trim().Length == 0) { ShowHint(); return; }
            var outcome = hub.PreviewReminder(box.Text);
            if (outcome.Value is { } parsed) { preview.Text = $"→ {parsed.Text} · {parsed.When}"; preview.Foreground = Theme.Olive; }
            else { preview.Text = (outcome.When.Length > 0 ? outcome.When + " — " : "") + outcome.Error; preview.Foreground = Theme.Muted; }
        }
        box.TextChanged += (_, _) => Update();
        void Submit()
        {
            if (box.Text.Trim().Length == 0) return;
            var result = hub.AddReminder(box.Text);
            if (result.Ok) { box.Clear(); ShowHint(); box.Focus(); }
            else { preview.Text = result.Error; preview.Foreground = Theme.Danger; }
        }
        add.Click += (_, _) => Submit();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Submit(); e.Handled = true; } };
        Loaded += (_, _) => { if (current == "reminders" && !smoke) box.Focus(); };
        return card;
    }

    // ---------- Lists ----------

    private void RenderReminderLists()
    {
        if (reminderLists is null) return;
        reminderLists.Children.Clear();
        var now = DateTimeOffset.Now;
        var groups = ReminderGroups.Build(hub.Engine.Items, now, hub.Engine.Zone);
        if (groups.IsEmpty)
        {
            reminderLists.Children.Add(PanelKit.Empty("No reminders yet. Type one above, like \"remind me at 5pm to call mom\"."));
            return;
        }
        bool first = true;
        void Group(string title, string? note, List<Reminder> items, Func<Reminder, FrameworkElement> row, FrameworkElement? extra = null)
        {
            if (items.Count == 0) return;
            var host = new StackPanel { Margin = new Thickness(0, first ? 0 : PanelKit.S16, 0, 0) };
            first = false;
            var body = PanelKit.Section(host, $"{title} ({items.Count})", note);
            for (int i = 0; i < items.Count; i++)
            {
                var element = row(items[i]);
                if (i < items.Count - 1) element.Margin = new Thickness(0, 0, 0, PanelKit.S4);
                body.Children.Add(element);
            }
            if (extra is not null) body.Children.Add(extra);
            (body.Parent as Border)!.Padding = new Thickness(PanelKit.S12, PanelKit.S12, PanelKit.S12, PanelKit.S12);
            reminderLists.Children.Add(host);
        }
        Group("Missed", "Rang or came in while you were away, and not marked done yet.", groups.Missed, r => ReminderRow(r, now, missed: true));
        Group("Today", null, groups.Today, r => ReminderRow(r, now));
        Group("Tomorrow", null, groups.Tomorrow, r => ReminderRow(r, now));
        Group("Later", null, groups.Later, r => ReminderRow(r, now));
        Group("Repeating", null, groups.Repeating, r => ReminderRow(r, now, repeating: true));
        if (groups.Done.Count > 0)
        {
            FrameworkElement? clear = null;
            if (showDoneReminders)
            {
                var clearButton = PanelKit.Quiet("Clear completed", () => hub.ClearDoneReminders(), danger: true);
                clearButton.HorizontalAlignment = HorizontalAlignment.Left;
                clear = clearButton;
            }
            if (showDoneReminders) Group("Done", null, groups.Done.Take(20).ToList(), r => ReminderRow(r, now, done: true), clear);
            else
            {
                var show = PanelKit.Quiet($"Show {groups.Done.Count} completed", () => { showDoneReminders = true; RenderReminderLists(); });
                show.HorizontalAlignment = HorizontalAlignment.Left;
                show.Margin = new Thickness(0, PanelKit.S12, 0, PanelKit.S12);
                reminderLists.Children.Add(show);
            }
        }
    }

    private FrameworkElement ReminderRow(Reminder r, DateTimeOffset now, bool missed = false, bool repeating = false, bool done = false)
    {
        var zone = hub.Engine.Zone;
        bool h24 = hub.ReminderOptions.Use24Hour;
        var row = new Grid { MinHeight = 44 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // The round check: Done (a repeating reminder skips to its next time).
        var check = new Border
        {
            Width = 22, Height = 22, CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1.5), Margin = new Thickness(PanelKit.S4, 0, PanelKit.S12, 0), VerticalAlignment = VerticalAlignment.Center,
            BorderBrush = done ? Theme.Olive : Theme.LineStrong, Background = done ? Theme.Tint("#9B9A5E", 0.3) : Brushes.Transparent, Cursor = done ? Cursors.Arrow : Cursors.Hand,
            Child = Theme.Icon(Theme.Glyphs.Check, 11, done ? Theme.Olive : Brushes.Transparent), ToolTip = done ? "Done" : repeating && !missed ? "Done: skip to the next time" : "Mark done",
        };
        if (!done)
        {
            var mark = (TextBlock)check.Child;
            check.MouseEnter += (_, _) => { mark.Foreground = Theme.Olive; check.BorderBrush = Theme.Olive; };
            check.MouseLeave += (_, _) => { mark.Foreground = Brushes.Transparent; check.BorderBrush = Theme.LineStrong; };
            check.MouseLeftButtonUp += (_, e) => { hub.CompleteReminder(r.Id); e.Handled = true; };
        }
        row.Children.Add(check);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = Theme.Label(r.Text, 13.5, done ? Theme.Muted : Theme.Text);
        title.FontWeight = FontWeights.SemiBold;
        title.TextWrapping = TextWrapping.Wrap;
        if (done) title.TextDecorations = TextDecorations.Strikethrough;
        text.Children.Add(title);
        var sub = new List<string>();
        var ringAt = ReminderService.RingAt(r) ?? r.Due;
        if (done) sub.Add("Done " + ReminderFormat.When(r.DoneAt ?? r.Due, now, zone, h24));
        else if (missed) sub.Add((r.Attention == ReminderAttention.Missed ? "Missed " : "Rang ") + ReminderFormat.When(r.LastFired ?? r.Due, now, zone, h24));
        else
        {
            sub.Add((r.SnoozedUntil is not null ? "Snoozed to " : "") + ReminderFormat.When(ringAt, now, zone, h24) + " · " + ReminderFormat.Relative(ringAt, now));
        }
        if (repeating) sub.Add(ReminderFormat.RepeatWithTime(r.Repeat, h24));
        else if (r.IsRepeating && missed) sub.Add(ReminderFormat.RepeatText(r.Repeat));
        if (r.Note.Length > 0) sub.Add(r.Note);
        var detail = Theme.Label(string.Join("  ·  ", sub), 12, Theme.Muted);
        detail.TextWrapping = TextWrapping.Wrap;
        detail.Margin = new Thickness(0, 2, 0, 0);
        text.Children.Add(detail);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (missed)
        {
            var snooze = PanelKit.Quiet("Snooze ▾", () => { });
            snooze.Click += (_, _) => OpenSnoozeMenu(snooze, r.Id);
            actions.Children.Add(snooze);
        }
        if (!done) actions.Children.Add(Theme.IconButton("", "Edit", 28).With(b => { b.Background = Brushes.Transparent; b.Click += (_, _) => EditReminder(r.Id); }));
        actions.Children.Add(Theme.IconButton(Theme.Glyphs.Delete, "Delete", 28).With(b => { b.Background = Brushes.Transparent; b.Click += (_, _) => hub.DeleteReminder(r.Id); }));
        Grid.SetColumn(actions, 2);
        row.Children.Add(actions);

        var card = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(PanelKit.S8, PanelKit.S4, PanelKit.S8, PanelKit.S4), Background = Brushes.Transparent, Child = row };
        card.MouseEnter += (_, _) => card.Background = PanelKit.Hover;
        card.MouseLeave += (_, _) => card.Background = Brushes.Transparent;
        return card;
    }

    private void OpenSnoozeMenu(Button anchor, string id)
    {
        var menu = ReminderToast.SnoozeMenu(choice => hub.SnoozeReminder(id, choice));
        menu.PlacementTarget = anchor;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void EditReminder(string id)
    {
        if (hub.Engine.Find(id) is not { } reminder) return;
        var editor = new ReminderEditor(this, hub, reminder);
        editor.ShowDialog();
        if (!closed) { RaiseOnly(); }
    }

    // ---------- Notification options ----------

    private void BuildReminderOptions(StackPanel stack)
    {
        var o = hub.ReminderOptions;
        var card = PanelKit.Section(stack, "Notifications", "How a reminder tells you. Dismissing a card never answers it: it stays under Missed until you mark it done or snooze it.");
        card.Children.Add(PanelKit.Option("Pop-up card", "A small card at the top right of your main monitor with Done, Snooze and Open.", o.Toasts, on => hub.ChangeReminderOptions(s => s.Toasts = on)));
        card.Children.Add(PanelKit.Option("Play a sound", "A short sound when a reminder rings.", o.Sound, on => hub.ChangeReminderOptions(s => s.Sound = on)));
        card.Children.Add(PanelKit.Option("Do not disturb", "Nothing pops up or plays until you turn this off. What comes due meanwhile is listed under Missed.", o.DoNotDisturb, on => hub.ChangeReminderOptions(s => s.DoNotDisturb = on)));
        var more = PanelKit.Collapsible(card, "reminders.more", "More", nested: true);
        card = more.Body;
        card.Children.Add(PanelKit.Option("Tray balloon", "Also show the Windows notification from the tray icon.", o.TrayBalloon, on => hub.ChangeReminderOptions(s => s.TrayBalloon = on)));
        card.Children.Add(PanelKit.Option("Quiet hours", "Like Do not disturb, every day between the two times below (they can cross midnight, like 22:00 to 07:00).", o.QuietHours, on => hub.ChangeReminderOptions(s => s.QuietHours = on)));
        var times = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, PanelKit.S16) };
        times.Children.Add(Theme.Label("From", 12.5, Theme.Muted));
        times.Children.Add(TimeBox(o.QuietFrom, text => hub.ChangeReminderOptions(s => s.QuietFrom = text)));
        times.Children.Add(Theme.Label("to", 12.5, Theme.Muted));
        times.Children.Add(TimeBox(o.QuietTo, text => hub.ChangeReminderOptions(s => s.QuietTo = text)));
        card.Children.Add(times);
        card.Children.Add(PanelKit.Option("24-hour times", "Show 17:00 instead of 5:00 PM in reminders.", o.Use24Hour, on => hub.ChangeReminderOptions(s => s.Use24Hour = on)));
        var defaults = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, PanelKit.S16) };
        defaults.Children.Add(Theme.Label("A day with no time rings at", 12.5, Theme.Muted));
        defaults.Children.Add(TimeBox(o.DefaultTime, text => hub.ChangeReminderOptions(s => s.DefaultTime = text)));
        defaults.Children.Add(Theme.Label("and \"Snooze until tomorrow\" at", 12.5, Theme.Muted));
        defaults.Children.Add(TimeBox(o.TomorrowAt, text => hub.ChangeReminderOptions(s => s.TomorrowAt = text)));
        card.Children.Add(defaults);
    }

    /// <summary>A small box for a time like 22:00 or 7:30 pm; text that is not a time goes back to what it was.</summary>
    private static TextBox TimeBox(string value, Action<string> changed)
    {
        var box = Theme.TextField(value);
        box.Width = 72;
        box.Margin = new Thickness(PanelKit.S8, 0, PanelKit.S8, 0);
        string last = value;
        void Commit()
        {
            var parsed = ReminderSettings.ParseTime(box.Text, TimeSpan.MinValue);
            if (parsed == TimeSpan.MinValue) { box.Text = last; return; }
            last = box.Text = ReminderSettings.FormatTime(parsed);
            changed(last);
        }
        box.LostKeyboardFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
        return box;
    }
}

internal static class ButtonExtensions
{
    internal static Button With(this Button button, Action<Button> setup) { setup(button); return button; }
}
