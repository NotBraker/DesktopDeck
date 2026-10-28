using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesktopDeck.Models;
using DesktopDeck.Services;

namespace DesktopDeck;

/// <summary>Small shared drawing helpers for calendar events (colored dots, the Google and link marks).</summary>
internal static class CalendarVisuals
{
    public static Border Dot(string hex, double size = 10) => new() { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = Theme.Accent(hex), VerticalAlignment = VerticalAlignment.Center };

    /// <summary>A tiny mark for where an event comes from: a "G" in a circle for Google, a chain link for iCal links. Null for local blocks.</summary>
    public static FrameworkElement? SourceMark(CalendarEvent ev, double size, Brush color)
    {
        switch (ev.Source)
        {
            case CalendarSource.Google:
                var letter = Theme.Label("G", size * 0.72, color);
                letter.FontWeight = FontWeights.Bold;
                letter.HorizontalAlignment = HorizontalAlignment.Center;
                letter.Margin = new Thickness(0, -1, 0, 0);
                return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), BorderBrush = color, BorderThickness = new Thickness(1), Child = letter, ToolTip = "Google Calendar", VerticalAlignment = VerticalAlignment.Center };
            case CalendarSource.Ics:
                var link = Theme.Icon(Theme.Glyphs.Link, size, color);
                link.ToolTip = "Calendar link (read-only)";
                return link;
            case CalendarSource.Reminder:
                var bell = Theme.Icon("", size, color);
                bell.ToolTip = "Reminder";
                return bell;
            default:
                return null;
        }
    }

    public static Button ColorChoice(string name, string hex, bool selected, Action click)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var dot = Dot(hex, 10);
        dot.Margin = new Thickness(0, 0, 7, 0);
        content.Children.Add(dot);
        content.Children.Add(Theme.Label(name, 12, Theme.Text));
        var button = Theme.Button("");
        button.Content = content;
        button.Padding = new Thickness(11, 6, 12, 6);
        button.MinHeight = 30;
        button.Margin = new Thickness(0, 0, 6, 6);
        button.BorderBrush = selected ? Theme.Primary : Theme.LineStrong;
        button.Background = selected ? Theme.Tint(Theme.PrimaryHex, 0.18) : Theme.Surface;
        button.Click += (_, _) => click();
        return button;
    }

    public static TextBlock Wrapped(string text, double size, Brush color, double bottom = 10)
    {
        var label = Theme.Label(text, size, color);
        label.TextWrapping = TextWrapping.Wrap;
        label.Margin = new Thickness(0, 0, 0, bottom);
        return label;
    }

    /// <summary>Opens a Google event page in the browser (https links only).</summary>
    public static void OpenLink(string url)
    {
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(); }
        catch (System.ComponentModel.Win32Exception) { }
    }
}

/// <summary>Asks whether a new entry is a local Schedule block or a Google event (shown only when a Google account can be edited).</summary>
internal sealed class AddChoiceWindow : Window
{
    internal enum Choice { None, Local, Google }
    private Choice result;

    internal AddChoiceWindow(Window owner, DateOnly date)
    {
        Owner = owner;
        Title = "Add";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Theme.Background;
        FontFamily = Theme.UiFont;
        var root = new StackPanel { Margin = new Thickness(26, 22, 26, 18) };
        root.Children.Add(Theme.Label("Local block or Google event?", 20));
        root.Children.Add(CalendarVisuals.Wrapped("For " + CalendarFormat.Day(date) + ".", 12.5, Theme.Muted, 16));
        root.Children.Add(Option("Local block", "Stays on this PC and can repeat every week, like the rest of your schedule.", Choice.Local, true));
        root.Children.Add(Option("Google event", "Saved to your Google Calendar, so it shows up on your phone too.", Choice.Google, false));
        var cancel = Theme.Button("Cancel");
        cancel.IsCancel = true;
        cancel.HorizontalAlignment = HorizontalAlignment.Right;
        root.Children.Add(cancel);
        Content = root;
        Loaded += (_, _) => Activate();
    }

    private Button Option(string title, string description, Choice choice, bool isDefault)
    {
        var stack = new StackPanel();
        var name = Theme.Label(title, 14, Theme.Text);
        name.FontWeight = FontWeights.SemiBold;
        stack.Children.Add(name);
        var text = Theme.Label(description, 11.5, Theme.Muted);
        text.TextWrapping = TextWrapping.Wrap;
        text.Margin = new Thickness(0, 3, 0, 0);
        stack.Children.Add(text);
        var button = Theme.Button("");
        button.Content = stack;
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        button.Padding = new Thickness(16, 10, 16, 11);
        button.Margin = new Thickness(0, 0, 0, 10);
        button.IsDefault = isDefault;
        if (isDefault) button.BorderBrush = Theme.Primary;
        button.Click += (_, _) => { result = choice; DialogResult = true; };
        return button;
    }

    internal static Choice Ask(Window owner, DateOnly date)
    {
        var window = new AddChoiceWindow(owner, date);
        return window.ShowDialog() == true ? window.result : Choice.None;
    }
}

/// <summary>Read-only details of an event: iCal-link events, Google events you can't edit.</summary>
internal sealed class EventDetailsWindow : Window
{
    internal EventDetailsWindow(Window owner, CalendarEvent ev)
    {
        Owner = owner;
        Title = "Event";
        Width = 500;
        SizeToContent = SizeToContent.Height;
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Theme.Background;
        FontFamily = Theme.UiFont;
        var root = new StackPanel { Margin = new Thickness(26, 22, 26, 18) };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        var dot = CalendarVisuals.Dot(ev.Color, 12);
        dot.Margin = new Thickness(0, 4, 10, 0);
        head.Children.Add(dot);
        var title = Theme.Label(ev.Title, 22);
        title.TextWrapping = TextWrapping.Wrap;
        title.MaxWidth = 400;
        head.Children.Add(title);
        root.Children.Add(head);
        root.Children.Add(CalendarVisuals.Wrapped(CalendarFormat.Range(ev), 13, Theme.Text2, 12));
        if (ev.Location.Length > 0) root.Children.Add(Line(Theme.Glyphs.Pin, ev.Location));
        string source = ev.Source switch
        {
            CalendarSource.Google => ev.CalendarLabel + " · Google Calendar" + (ev.Editable ? "" : " (view only)"),
            CalendarSource.Ics => ev.CalendarLabel + " · calendar link (view only)",
            _ => ev.CalendarLabel,
        };
        root.Children.Add(Line(Theme.Glyphs.Calendar, source));
        if (ev.IsRecurringInstance) root.Children.Add(CalendarVisuals.Wrapped("One occurrence of a repeating event.", 12, Theme.Muted, 10));
        if (ev.Description.Length > 0)
        {
            var scroll = new ScrollViewer { MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 4, 0, 12) };
            scroll.Content = CalendarVisuals.Wrapped(ev.Description, 12.5, Theme.Text2, 0);
            root.Children.Add(scroll);
        }
        var buttons = new List<Button>();
        if (ev.HtmlLink.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var open = Theme.Button("Open in browser");
            open.Click += (_, _) => CalendarVisuals.OpenLink(ev.HtmlLink);
            buttons.Add(open);
        }
        var close = Theme.Button("Close", true);
        close.IsCancel = true;
        close.IsDefault = true;
        buttons.Add(close);
        var row = ItemEditor.Row(buttons.ToArray());
        row.Margin = new Thickness(0, 6, 0, 0);
        root.Children.Add(row);
        Content = root;
        Loaded += (_, _) => Activate();
    }

    private static FrameworkElement Line(string glyph, string text)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(Theme.Icon(glyph, 12, Theme.Muted));
        var label = Theme.Label(text, 12.5, Theme.Text2);
        label.TextWrapping = TextWrapping.Wrap;
        Grid.SetColumn(label, 1);
        row.Children.Add(label);
        return row;
    }
}

/// <summary>
/// Creates or edits one Google Calendar event: title, dates, times, all-day, location, description and calendar.
/// Save sends a PATCH with only what changed; Delete asks first. For one occurrence of a repeating event both affect that
/// occurrence only, and the window says so.
/// </summary>
internal sealed class EventEditorWindow : Window
{
    private readonly CalendarFeed feed;
    private readonly CalendarEvent? existing;
    private readonly List<GoogleCalendarChoice> choices;
    private readonly TextBox titleBox, startDateBox, startTimeBox, endDateBox, endTimeBox, locationBox, descriptionBox;
    private readonly TextBlock validation, endHeading;
    private readonly Grid timeRow;
    private readonly StackPanel choiceHost;
    private readonly List<Button> actionButtons = new();
    private GoogleCalendarChoice calendar;
    private bool allDay, busy, closed;

    /// <param name="date">The day to start on for a new event.</param>
    /// <param name="startMinutes">Minutes after midnight for a new timed event (null = a sensible default).</param>
    internal EventEditorWindow(Window owner, CalendarFeed feed, CalendarEvent? existing, DateOnly date, int? startMinutes)
    {
        this.feed = feed;
        this.existing = existing;
        bool isNew = existing is null;
        bool recurring = existing?.IsRecurringInstance == true;
        choices = feed.WritableGoogleCalendars.ToList();
        if (existing is not null && choices.All(c => c.Id != existing.CalendarId))
            choices.Insert(0, new GoogleCalendarChoice { Id = existing.CalendarId, Name = existing.CalendarLabel, Color = existing.Color, AccessRole = "writer" });
        calendar = (existing is null ? choices.FirstOrDefault(c => c.Primary) ?? choices.FirstOrDefault() : choices.First(c => c.Id == existing.CalendarId))
                   ?? new GoogleCalendarChoice { Id = "primary", Name = "Primary" };

        Owner = owner;
        Title = isNew ? "Add Google event" : "Edit Google event";
        Width = 540;
        SizeToContent = SizeToContent.Height;
        MaxHeight = SystemParameters.WorkArea.Height - 24;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Theme.Background;
        Foreground = Theme.Text;
        FontFamily = Theme.UiFont;
        Closed += (_, _) => closed = true;

        var root = new StackPanel { Margin = new Thickness(26, 22, 26, 18) };
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var heading = Theme.Label(Title, 22);
        heading.Margin = new Thickness(0, 0, 0, recurring ? 8 : 16);
        root.Children.Add(heading);
        if (recurring)
        {
            var note = new Border { Background = Theme.Tint(Theme.PrimaryHex, 0.14), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 9), Margin = new Thickness(0, 0, 0, 14) };
            note.Child = CalendarVisuals.Wrapped("This is one occurrence of a repeating event. Saving or deleting changes only this occurrence, not the whole series. To change the series, use Google Calendar.", 12, Theme.Text2, 0);
            root.Children.Add(note);
        }

        DateTime start = existing?.Start ?? NewStart(date, startMinutes);
        DateTime end = existing?.End ?? start.AddHours(1);
        allDay = existing?.AllDay ?? false;
        if (allDay) end = existing!.LastDay.ToDateTime(TimeOnly.MinValue);

        titleBox = ItemEditor.Field(root, "Title", existing?.Title ?? "");

        var calendarHeading = Theme.Label("Calendar", 12, Theme.Muted);
        calendarHeading.Margin = new Thickness(0, 0, 0, 6);
        root.Children.Add(calendarHeading);
        choiceHost = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        root.Children.Add(choiceHost);
        ShowChoices(recurring);

        root.Children.Add(ItemEditor.SwitchRow("All day", allDay, v => { allDay = v; ApplyAllDay(); }));

        var dates = new Grid();
        dates.ColumnDefinitions.Add(new ColumnDefinition());
        dates.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        dates.ColumnDefinitions.Add(new ColumnDefinition());
        var startPanel = new StackPanel();
        var endPanel = new StackPanel();
        startDateBox = ItemEditor.Field(startPanel, "Starts (yyyy-mm-dd)", Day(start));
        endDateBox = ItemEditor.Field(endPanel, allDay ? "Last day (yyyy-mm-dd)" : "Ends (yyyy-mm-dd)", Day(end));
        endHeading = (TextBlock)endPanel.Children[0];
        Grid.SetColumn(endPanel, 2);
        dates.Children.Add(startPanel);
        dates.Children.Add(endPanel);
        root.Children.Add(dates);

        timeRow = new Grid();
        timeRow.ColumnDefinitions.Add(new ColumnDefinition());
        timeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        timeRow.ColumnDefinitions.Add(new ColumnDefinition());
        var startTimePanel = new StackPanel();
        var endTimePanel = new StackPanel();
        startTimeBox = ItemEditor.Field(startTimePanel, "Start time (e.g. 9:00 am)", Planner.Format(start.ToString("HH:mm", CultureInfo.InvariantCulture), false));
        endTimeBox = ItemEditor.Field(endTimePanel, "End time (e.g. 10:30 am)", Planner.Format(end.ToString("HH:mm", CultureInfo.InvariantCulture), false));
        if (existing?.AllDay == true) { startTimeBox.Text = "9:00 AM"; endTimeBox.Text = "10:00 AM"; }
        Grid.SetColumn(endTimePanel, 2);
        timeRow.Children.Add(startTimePanel);
        timeRow.Children.Add(endTimePanel);
        root.Children.Add(timeRow);
        timeRow.Visibility = allDay ? Visibility.Collapsed : Visibility.Visible;

        locationBox = ItemEditor.Field(root, "Location (optional)", existing?.Location ?? "");
        descriptionBox = ItemEditor.Field(root, "Description (optional)", existing?.Description ?? "");
        descriptionBox.AcceptsReturn = true;
        descriptionBox.TextWrapping = TextWrapping.Wrap;
        descriptionBox.Height = 84;
        descriptionBox.VerticalContentAlignment = VerticalAlignment.Top;
        descriptionBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

        validation = Theme.Label("", 12, Theme.Danger);
        validation.TextWrapping = TextWrapping.Wrap;
        validation.MinHeight = 24;
        root.Children.Add(validation);

        var actions = new Grid();
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        if (existing is not null)
        {
            var delete = Theme.Button(recurring ? "Delete this occurrence" : "Delete");
            delete.Foreground = Theme.Danger;
            delete.Margin = new Thickness(0, 0, 8, 0);
            delete.Click += async (_, _) => await DeleteAsync();
            left.Children.Add(delete);
            actionButtons.Add(delete);
            if (existing.HtmlLink.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var open = Theme.Button("Open in Google");
                open.Click += (_, _) => CalendarVisuals.OpenLink(existing.HtmlLink);
                left.Children.Add(open);
            }
        }
        actions.Children.Add(left);
        var cancel = Theme.Button("Cancel");
        cancel.IsCancel = true;
        var save = Theme.Button(isNew ? "Add" : "Save", true);
        save.IsDefault = true;
        save.Click += async (_, _) => await SaveAsync();
        actionButtons.Add(save);
        actionButtons.Add(cancel);
        var right = ItemEditor.Row(cancel, save);
        right.Margin = new Thickness(0);
        Grid.SetColumn(right, 1);
        actions.Children.Add(right);
        root.Children.Add(actions);
        Loaded += (_, _) => { Activate(); titleBox.Focus(); titleBox.SelectAll(); };
    }

    private static string Day(DateTime value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateTime NewStart(DateOnly date, int? startMinutes)
    {
        if (startMinutes is { } minutes) return date.ToDateTime(TimeOnly.MinValue).AddMinutes(Math.Clamp(minutes, 0, 23 * 60));
        var now = DateTime.Now;
        // Today: the next full hour; other days: 9:00.
        return date == DateOnly.FromDateTime(now) ? date.ToDateTime(TimeOnly.MinValue).AddHours(Math.Min(22, now.Hour + 1)) : date.ToDateTime(new TimeOnly(9, 0));
    }

    private void ShowChoices(bool locked)
    {
        choiceHost.Children.Clear();
        if (locked)
        {
            choiceHost.Children.Add(CalendarVisuals.Wrapped(calendar.Name + " (one occurrence stays on its calendar)", 12.5, Theme.Text2, 8));
            return;
        }
        if (choices.Count == 0)
        {
            choiceHost.Children.Add(CalendarVisuals.Wrapped("No editable Google calendars found. Open Settings → Connected accounts and refresh the calendar list.", 12, Theme.Danger, 8));
            return;
        }
        var wrap = new WrapPanel();
        foreach (var c in choices)
        {
            var choice = c;
            wrap.Children.Add(CalendarVisuals.ColorChoice(c.Name, c.Color, c.Id == calendar.Id, () => { calendar = choice; ShowChoices(false); }));
        }
        choiceHost.Children.Add(wrap);
        if (existing is not null) choiceHost.Children.Add(CalendarVisuals.Wrapped("Choosing another calendar moves the event there.", 11.5, Theme.Muted, 4));
    }

    private void ApplyAllDay()
    {
        timeRow.Visibility = allDay ? Visibility.Collapsed : Visibility.Visible;
        endHeading.Text = allDay ? "Last day (yyyy-mm-dd)" : "Ends (yyyy-mm-dd)";
    }

    private void Fail(string message, Control? field = null) { validation.Foreground = Theme.Danger; validation.Text = message; field?.Focus(); }

    private EventDraft? ReadDraft()
    {
        string title = titleBox.Text.Trim();
        if (title.Length == 0) { Fail("Give the event a title.", titleBox); return null; }
        if (!DateOnly.TryParseExact(startDateBox.Text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDay)) { Fail("Write the start date as yyyy-mm-dd.", startDateBox); return null; }
        if (!DateOnly.TryParseExact(endDateBox.Text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDay)) { Fail("Write the end date as yyyy-mm-dd.", endDateBox); return null; }
        if (allDay)
        {
            if (endDay < startDay) { Fail("The last day can't be before the first day.", endDateBox); return null; }
            return new EventDraft(title, startDay.ToDateTime(TimeOnly.MinValue), endDay.AddDays(1).ToDateTime(TimeOnly.MinValue), true, locationBox.Text.Trim(), descriptionBox.Text.Trim());
        }
        if (Planner.Minutes(Planner.ParseTime(startTimeBox.Text)) is not { } startMinutes) { Fail("Enter a start time like 9:00 am or 17:30.", startTimeBox); return null; }
        if (Planner.Minutes(Planner.ParseTime(endTimeBox.Text)) is not { } endMinutes) { Fail("Enter an end time like 10:30 am or 18:00.", endTimeBox); return null; }
        var from = startDay.ToDateTime(TimeOnly.MinValue).AddMinutes(startMinutes);
        var to = endDay.ToDateTime(TimeOnly.MinValue).AddMinutes(endMinutes);
        if (to <= from) { Fail("The event must end after it starts.", endTimeBox); return null; }
        return new EventDraft(title, from, to, false, locationBox.Text.Trim(), descriptionBox.Text.Trim());
    }

    private void SetBusy(bool value, string message = "")
    {
        busy = value;
        foreach (var b in actionButtons) b.IsEnabled = !value || b.IsCancel;
        validation.Foreground = Theme.Muted;
        validation.Text = message;
    }

    private async Task SaveAsync()
    {
        if (busy || ReadDraft() is not { } draft) return;
        SetBusy(true, "Saving to Google Calendar…");
        try
        {
            if (existing is null) await feed.CreateAsync(calendar, draft);
            else await feed.UpdateAsync(calendar, existing, draft);
            Finish();
        }
        catch (CalendarException ex) { if (!closed) { SetBusy(false); Fail(ex.Message); } }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            App.Log(ex);
            if (!closed) { SetBusy(false); Fail("Could not save the event."); }
        }
    }

    private async Task DeleteAsync()
    {
        if (busy || existing is null) return;
        string what = existing.IsRecurringInstance
            ? $"Delete just this occurrence of \"{existing.Title}\" ({CalendarFormat.Range(existing)})? The rest of the series stays."
            : $"Delete \"{existing.Title}\" from Google Calendar? This can't be undone from here.";
        if (MessageBox.Show(this, what, "Delete event", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        SetBusy(true, "Deleting…");
        try
        {
            await feed.DeleteAsync(existing);
            Finish();
        }
        catch (CalendarException ex) { if (!closed) { SetBusy(false); Fail(ex.Message); } }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            App.Log(ex);
            if (!closed) { SetBusy(false); Fail("Could not delete the event."); }
        }
    }

    /// <summary>Closes with success, unless the window was already closed while the request was running.</summary>
    private void Finish()
    {
        if (closed || !IsLoaded) return;
        DialogResult = true;
    }
}
