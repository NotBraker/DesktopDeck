using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopDeck.Models;
using DesktopDeck.Services;

namespace DesktopDeck;

// The History tab: undo and redo, the recycle bin and "Restore a day…". Also the small toast the control center shows for undoable changes
// (the decks' own toast sits behind the control center's backdrop).
internal sealed partial class ControlCenter
{
    private const int RecentSteps = 15;
    private bool historyHooked, historyRefreshPending;
    private Border? toastHost;
    private StackPanel? toastRow;
    private DispatcherTimer? toastClock;

    private FrameworkElement BuildHistoryTab()
    {
        if (!historyHooked)
        {
            historyHooked = true;
            hub.HistoryChanged += OnHistoryChanged;
            Closed += (_, _) => hub.HistoryChanged -= OnHistoryChanged;
        }
        var stack = new StackPanel();
        AddUndoSection(stack);
        AddBinSection(stack);
        AddDaysSection(stack);
        return stack;
    }

    /// <summary>Steps, the bin or the snapshots changed: redraw the tab once, after whatever caused it has finished.</summary>
    private void OnHistoryChanged()
    {
        if (historyRefreshPending || closed) return;
        historyRefreshPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            historyRefreshPending = false;
            if (closed) return;
            if (current == "history") Rebuild("history"); else cache.Remove("history");
        }));
    }

    // ---------- Undo and redo ----------

    private void AddUndoSection(StackPanel stack)
    {
        var body = PanelKit.Section(stack, "Undo and redo",
            "Ctrl+Z undoes the last change and Ctrl+Y (or Ctrl+Shift+Z) redoes it, on the deck, the widgets in front and here. The last 50 changes are kept while Desktop Deck is running.");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, PanelKit.S12) };
        var undo = Theme.Button(hub.CanUndo ? "Undo " + Trim(hub.UndoLabel!, 34) : "Nothing to undo", hub.CanUndo);
        undo.IsEnabled = hub.CanUndo;
        undo.MinHeight = 34;
        undo.ToolTip = "Ctrl+Z";
        undo.Click += (_, _) => hub.Undo();
        var redo = Theme.Button(hub.CanRedo ? "Redo " + Trim(hub.RedoLabel!, 34) : "Nothing to redo");
        redo.IsEnabled = hub.CanRedo;
        redo.MinHeight = 34;
        redo.Margin = new Thickness(PanelKit.S8, 0, 0, 0);
        redo.ToolTip = "Ctrl+Y";
        redo.Click += (_, _) => hub.Redo();
        buttons.Children.Add(undo);
        buttons.Children.Add(redo);
        body.Children.Add(buttons);

        var undone = hub.RedoSteps;
        var steps = hub.UndoSteps;
        if (steps.Count == 0 && undone.Count == 0)
        {
            body.Children.Add(PanelKit.Hint("No changes yet. Move, resize, edit or remove something and it will show up here."));
            return;
        }
        foreach (var step in undone.Reverse()) body.Children.Add(StepRow(step, undone: true));
        foreach (var step in steps.Take(RecentSteps)) body.Children.Add(StepRow(step, undone: false));
        if (steps.Count > RecentSteps) body.Children.Add(PanelKit.Hint($"…and {steps.Count - RecentSteps} earlier changes."));
        else body.Children.Add(new Border { Height = PanelKit.S4 });
    }

    private static FrameworkElement StepRow(UndoStep step, bool undone)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, PanelKit.S8) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = Theme.Label(undone ? step.Label + "  (undone)" : step.Label, 13, undone ? Theme.Muted : Theme.Text);
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        if (undone) label.FontStyle = FontStyles.Italic;
        row.Children.Add(label);
        var when = Theme.Label(Ago(step.At.ToLocalTime()), 12, Theme.Muted);
        Grid.SetColumn(when, 1);
        row.Children.Add(when);
        return row;
    }

    // ---------- The recycle bin ----------

    private void AddBinSection(StackPanel stack)
    {
        int days = hub.Settings.History.DaysFor(HistoryKeys.Trash);
        var body = PanelKit.Section(stack, "Recycle bin", days == 0
            ? "Removed widgets wait here until you empty the bin, with everything saved in them. Restore puts one back where it was."
            : $"Removed widgets wait here for {days} days, with everything saved in them, then they are deleted for good. Restore puts one back where it was.");
        var entries = hub.Trash.ToList();
        if (entries.Count == 0)
        {
            body.Children.Add(PanelKit.Empty(days == 0 ? "Nothing here. A widget you remove stays here until you empty the bin." : $"Nothing here. A widget you remove stays here for {days} days."));
            return;
        }
        var header = new Grid { Margin = new Thickness(0, 0, 0, PanelKit.S8) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(Theme.Label($"{entries.Count} {(entries.Count == 1 ? "widget" : "widgets")}", 12, Theme.Muted));
        var empty = PanelKit.Quiet("Empty the bin", () =>
        {
            if (MessageBox.Show(this, $"Delete {(entries.Count == 1 ? "the widget" : $"all {entries.Count} widgets")} in the recycle bin for good? Ctrl+Z can still bring them back until Desktop Deck restarts.", "Empty the recycle bin", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) hub.EmptyTrash();
        }, danger: true);
        Grid.SetColumn(empty, 1);
        header.Children.Add(empty);
        body.Children.Add(header);
        var now = DateTimeOffset.Now;
        foreach (var entry in entries) body.Children.Add(BinRow(entry, now, days));
    }

    private FrameworkElement BinRow(TrashEntry entry, DateTimeOffset now, int days)
    {
        var item = entry.Item;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tile = PanelKit.IconTile(KindGlyph(item), string.IsNullOrWhiteSpace(item.Accent) ? Theme.PrimaryHex : item.Accent, 36);
        tile.Margin = new Thickness(0, 0, PanelKit.S12, 0);
        grid.Children.Add(tile);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, PanelKit.S8, 0) };
        var name = Theme.Label(DisplayName(item), 13.5);
        name.FontWeight = FontWeights.SemiBold;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        text.Children.Add(name);
        int left = TrashBin.DaysLeft(entry, now, days);
        string where = entry.Layer == LayoutLayers.Floating ? "pinned" : "on the desktop";
        string due = days == 0 ? "kept until emptied" : left <= 0 ? "deleted today" : left == 1 ? "1 day left" : left + " days left";
        var detail = Theme.Label($"{KindName(item)} · {where} · removed {Ago(entry.RemovedAt.LocalDateTime)} · {due}", 11.5, Theme.Muted);
        detail.TextTrimming = TextTrimming.CharacterEllipsis;
        text.Children.Add(detail);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(PanelKit.Outline("Restore", () => hub.RestoreFromTrash(entry.Id), "Put it back where it was"));
        actions.Children.Add(PanelKit.Quiet("Delete forever", () =>
        {
            if (MessageBox.Show(this, $"Delete {DisplayName(item)} for good?", "Delete forever", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) hub.DeleteFromTrash(entry.Id);
        }, danger: true, tip: "Delete it for good"));
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);

        var card = PanelKit.HoverCard(grid);
        card.Padding = new Thickness(PanelKit.S12, PanelKit.S8, PanelKit.S8, PanelKit.S8);
        card.Margin = new Thickness(0, 0, 0, PanelKit.S8);
        return card;
    }

    // ---------- Restore a day ----------

    private void AddDaysSection(StackPanel stack)
    {
        var body = PanelKit.Section(stack, "Restore a day…",
            "A copy of your widgets, layouts and look from the start of each of the last 14 days. Restoring asks first, keeps a copy of how things are now, and Ctrl+Z undoes it. How long things are kept: Settings › Keep history for…");
        var days = hub.Snapshots;
        if (days.Count == 0)
        {
            body.Children.Add(PanelKit.Empty("No earlier days yet. The first copy is made the next day you change something."));
            return;
        }
        foreach (var day in days) body.Children.Add(DayRow(day));
    }

    private FrameworkElement DayRow(SnapshotInfo snapshot)
    {
        var preview = hub.PreviewSnapshot(snapshot);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { Margin = new Thickness(0, 0, PanelKit.S12, 0) };
        var today = DateOnly.FromDateTime(DateTime.Now);
        string date = snapshot.Day == today ? "Today (the start of the day)" : snapshot.Day == today.AddDays(-1) ? "Yesterday" : snapshot.Day.ToString("dddd d MMMM", CultureInfo.CurrentCulture);
        var title = Theme.Label(date, 13.5);
        title.FontWeight = FontWeights.SemiBold;
        text.Children.Add(title);
        string counts = preview is null ? "This copy could not be read." : $"{Plural(preview.Then.Widgets, "widget")} · {preview.Then.Floating} pinned · {Plural(preview.Then.Layouts, "layout")}";
        text.Children.Add(Theme.Label(counts, 11.5, Theme.Muted));
        if (preview is not null)
        {
            var changes = Theme.Label("If restored: " + preview.Text, 12, Theme.Text2);
            changes.TextWrapping = TextWrapping.Wrap;
            changes.Margin = new Thickness(0, 4, 0, 0);
            text.Children.Add(changes);
        }
        grid.Children.Add(text);
        var restore = PanelKit.Outline("Restore…", () => RestoreDay(snapshot, date, preview), "Go back to this day");
        restore.IsEnabled = preview is not null;
        restore.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(restore, 1);
        grid.Children.Add(restore);
        var card = PanelKit.HoverCard(grid);
        card.Padding = new Thickness(PanelKit.S12, PanelKit.S12, PanelKit.S12, PanelKit.S12);
        card.Margin = new Thickness(0, 0, 0, PanelKit.S8);
        return card;
    }

    private void RestoreDay(SnapshotInfo snapshot, string date, HistoryState.Comparison? preview)
    {
        string question = $"Go back to {date.ToLowerInvariant()}?\n\n{preview?.Text}\n\nA copy of how things are now is kept first, widgets that are not in that day go to the recycle bin, and Ctrl+Z undoes the restore.";
        if (MessageBox.Show(this, question, "Restore a day", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        if (hub.RestoreSnapshot(snapshot) is { } problem) MessageBox.Show(this, problem, "Restore a day", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ---------- Helpers ----------

    private static string Plural(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";

    private static string Trim(string text, int max) => text.Length > max ? text[..(max - 1)].TrimEnd() + "…" : text;

    private static string Ago(DateTime local)
    {
        var span = DateTime.Now - local;
        if (span < TimeSpan.FromMinutes(1)) return "just now";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes} min ago";
        if (span < TimeSpan.FromDays(1)) return $"{(int)span.TotalHours} h ago";
        int days = (int)span.TotalDays;
        return days == 1 ? "yesterday" : $"{days} days ago";
    }

    // ---------- The toast ----------

    /// <summary>A short message at the bottom of the panel, with an optional button (Undo/Redo). Fades away by itself.</summary>
    internal void ShowToast(string text, string? actionLabel, Action? action)
    {
        if (closed) return;
        if (toastHost is null)
        {
            toastRow = new StackPanel { Orientation = Orientation.Horizontal };
            toastHost = new Border
            {
                CornerRadius = new CornerRadius(10), Background = Theme.Surface3, BorderBrush = Theme.LineStrong, BorderThickness = new Thickness(1), Padding = new Thickness(14, 9, 14, 9),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, ShadowRoom + 20),
                Effect = PanelKit.Shadow(20, 6, 0.5), Visibility = Visibility.Collapsed, Child = toastRow,
            };
            Panel.SetZIndex(toastHost, 10);
            root.Children.Add(toastHost);
            toastClock = new DispatcherTimer();
            toastClock.Tick += (_, _) => { toastClock.Stop(); toastHost.Visibility = Visibility.Collapsed; };
        }
        toastRow!.Children.Clear();
        toastRow.Children.Add(Theme.Label(text, 12.5, Theme.Text));
        bool withAction = actionLabel is not null && action is not null;
        if (withAction)
        {
            var button = PanelKit.Quiet(actionLabel!, () => { toastHost.Visibility = Visibility.Collapsed; toastClock!.Stop(); action!(); });
            button.Foreground = Theme.Primary;
            button.Margin = new Thickness(PanelKit.S12, -6, -8, -6);
            toastRow.Children.Add(button);
        }
        toastHost.IsHitTestVisible = withAction;
        toastClock!.Interval = TimeSpan.FromSeconds(withAction ? 8 : 3.2);
        toastHost.Visibility = Visibility.Visible;
        toastClock.Stop();
        toastClock.Start();
    }
}
