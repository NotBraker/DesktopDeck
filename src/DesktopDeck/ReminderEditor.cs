using System.Windows;
using System.Windows.Controls;
using DesktopDeck.Models;
using DesktopDeck.Services;

namespace DesktopDeck;

/// <summary>Edits one reminder: its text, when (in plain words, with a live reading of it) and a note.</summary>
internal sealed class ReminderEditor : Window
{
    private readonly DeckHub hub;
    private readonly Reminder reminder;
    private readonly TextBox textBox, whenBox, noteBox;
    private readonly TextBlock preview;
    private readonly string originalWhen;

    internal ReminderEditor(Window owner, DeckHub hub, Reminder reminder)
    {
        this.hub = hub;
        this.reminder = reminder;
        Owner = owner;
        Title = "Edit reminder";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Theme.Background;
        FontFamily = Theme.UiFont;
        var root = new StackPanel { Margin = new Thickness(26, 22, 26, 18) };
        var heading = Theme.Label("Edit reminder", 22);
        heading.Margin = new Thickness(0, 0, 0, 16);
        root.Children.Add(heading);
        textBox = ItemEditor.Field(root, "Reminder", reminder.Text);
        originalWhen = ReminderFormat.WhenNatural(reminder, DateTimeOffset.Now, hub.Engine.Zone);
        whenBox = ItemEditor.Field(root, "When (in words: tomorrow at 5pm, every monday 7:30, in 2 hours, oct 12)", originalWhen);
        preview = Theme.Label("", 12.5, Theme.Muted);
        preview.TextWrapping = TextWrapping.Wrap;
        preview.Margin = new Thickness(0, -6, 0, 12);
        root.Children.Add(preview);
        noteBox = ItemEditor.Field(root, "Note (optional)", reminder.Note);

        var cancel = Theme.Button("Cancel");
        cancel.IsCancel = true;
        var save = Theme.Button("Save", true);
        save.IsDefault = true;
        save.Click += (_, _) => Save();
        var row = ItemEditor.Row(cancel, save);
        row.Margin = new Thickness(0, 4, 0, 0);
        root.Children.Add(row);
        Content = root;
        whenBox.TextChanged += (_, _) => Update();
        Update();
        Loaded += (_, _) => { Activate(); textBox.Focus(); textBox.SelectAll(); };
    }

    private ParseOutcome Read() => hub.PreviewReminder(whenBox.Text, requireText: false);

    private void Update()
    {
        if (whenBox.Text == originalWhen) { preview.Text = "→ " + (reminder.IsRepeating ? ReminderFormat.RepeatWithTime(reminder.Repeat, hub.ReminderOptions.Use24Hour) + " · " : "") + ReminderFormat.When(reminder.Due, DateTimeOffset.Now, hub.Engine.Zone, hub.ReminderOptions.Use24Hour, " at "); preview.Foreground = Theme.Olive; return; }
        var outcome = Read();
        preview.Text = outcome.Value is { } v ? "→ " + v.When : outcome.Error;
        preview.Foreground = outcome.Value is null ? Theme.Danger : Theme.Olive;
    }

    private void Save()
    {
        string text = textBox.Text.Trim();
        if (text.Length == 0) { textBox.Focus(); return; }
        DateTimeOffset due = reminder.Due;
        var repeat = reminder.Repeat;
        // Unchanged words keep the exact time and repeat (an "every 2 hours" keeps its rhythm).
        if (whenBox.Text != originalWhen)
        {
            if (Read().Value is not { } parsed) { whenBox.Focus(); return; }
            due = parsed.Due;
            repeat = parsed.Repeat;
        }
        hub.UpdateReminder(reminder.Id, text, noteBox.Text, due, repeat);
        DialogResult = true;
    }
}
