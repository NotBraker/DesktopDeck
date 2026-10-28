using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesktopDeck.Models;
using DesktopDeck.Services;

namespace DesktopDeck;

// Google Calendar and iCal links on the Schedule widget (and the Agenda widget's clicks): drawing external events,
// opening their editor or details, and the "local block or Google event?" question. Network work lives in
// Services/CalendarFeed.cs; nothing here blocks, it only reads what the feed already has.
public sealed partial class MainWindow
{
    private static readonly Brush OnLight = Theme.Brush("#1A1611"), OnDark = Theme.Brush("#FFF6EC");

    internal CalendarFeed CalendarData => hub.Calendar;

    private static CalendarQuery QueryFor(LauncherItem item) => new(item.ShowGoogle, item.GoogleCalendarIds, item.ShowIcs);

    /// <summary>A quiet "Offline — showing 10:42 data" note for the widget header, or null when all is well.</summary>
    private static FrameworkElement? CalendarNote(CalendarSnapshot snapshot)
    {
        if (snapshot.Note is not { } text) return null;
        var label = Theme.Label(text, 10.5, snapshot.NeedsReconnect ? Theme.Danger : Theme.Muted);
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        label.MaxWidth = 220;
        label.ToolTip = snapshot.Error;
        return label;
    }

    // ---------- Opening events ----------

    /// <summary>Click on any event: a Google event you can edit opens the editor, anything else shows its details.</summary>
    internal void OpenCalendarEvent(CalendarEvent ev)
    {
        switch (ev.Source)
        {
            case CalendarSource.Reminder:
                hub.OpenControlCenter("reminders");
                break;
            case CalendarSource.Local:
                if (settings.Items.FirstOrDefault(i => i.Id == ev.LocalItemId) is { } owner && owner.Blocks.FirstOrDefault(b => b.Id == ev.LocalBlockId) is { } block) EditBlock(owner, block);
                break;
            case CalendarSource.Google when ev.Editable && CalendarData.GoogleCanEdit:
                Modal(() => new EventEditorWindow(DialogOwner, CalendarData, ev, ev.FirstDay, null).ShowDialog());
                break;
            default:
                Modal(() => new EventDetailsWindow(DialogOwner, ev).ShowDialog());
                break;
        }
    }

    /// <summary>A new Google event on a day (and optionally a start time), through the editor.</summary>
    internal void AddGoogleEvent(DateOnly date, int? startMinutes = null)
    {
        if (!CalendarData.GoogleCanEdit) { Inform("Connect a Google account with edit access first: Control center → Settings → Connected accounts.", "Google Calendar"); return; }
        Modal(() => new EventEditorWindow(DialogOwner, CalendarData, null, date, startMinutes).ShowDialog());
    }

    /// <summary>The + button and double-click on an empty slot: a local block, or (when Google can be edited) ask which.</summary>
    private void AddEntry(LauncherItem item, DateOnly date, int? startMinutes)
    {
        if (item.ShowGoogle && CalendarData.GoogleCanEdit && CalendarData.WritableGoogleCalendars.Count > 0)
        {
            var choice = Modal(() => AddChoiceWindow.Ask(DialogOwner, date));
            if (choice == AddChoiceWindow.Choice.None) return;
            if (choice == AddChoiceWindow.Choice.Google) { AddGoogleEvent(date, startMinutes); return; }
        }
        EditBlock(item, null, LocalTemplate(date, startMinutes));
    }

    private static ScheduleBlock LocalTemplate(DateOnly date, int? startMinutes)
    {
        var block = new ScheduleBlock { Days = [Planner.DayName(date)] };
        if (startMinutes is { } start)
        {
            start = Math.Clamp(start, 0, 23 * 60);
            int end = Math.Min(24 * 60 - 1, start + 60);
            block.Start = $"{start / 60:00}:{start % 60:00}";
            block.End = $"{end / 60:00}:{end % 60:00}";
        }
        return block;
    }

    // ---------- Drawing ----------

    /// <summary>Colors for text on an external event: readable on its fill (filled) or on the card (outline).</summary>
    private static (Brush Title, Brush Detail) ExternalText(LauncherItem item, CalendarEvent ev)
    {
        if (item.ExternalStyle == "outline") return (Theme.Text, Theme.Text2);
        return CalendarColors.IsLight(ev.Color) ? (OnLight, OnLight) : (OnDark, OnDark);
    }

    private static void StyleExternal(Border view, LauncherItem item, CalendarEvent ev)
    {
        if (item.ExternalStyle == "outline")
        {
            view.Background = Theme.Tint(ev.Color, 0.08);
            view.BorderBrush = Theme.Accent(ev.Color);
            view.BorderThickness = new Thickness(1.5);
        }
        else
        {
            view.Background = Theme.Tint(ev.Color, 0.9);
            view.BorderBrush = Brushes.Transparent;
            view.BorderThickness = new Thickness(0);
        }
    }

    private void AttachExternalInteractions(FrameworkElement view, CalendarEvent ev)
    {
        view.Cursor = Cursors.Hand;
        view.MouseLeftButtonUp += (_, e) => { OpenCalendarEvent(ev); e.Handled = true; };
        var menu = Menu();
        AddMenuItem(menu, ev.Source == CalendarSource.Google && ev.Editable && CalendarData.GoogleCanEdit ? "Edit…" : "Details…", () => OpenCalendarEvent(ev));
        if (ev.HtmlLink.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) AddMenuItem(menu, "Open in browser", () => CalendarVisuals.OpenLink(ev.HtmlLink));
        view.ContextMenu = menu;
    }

    private static ToolTip ExternalTip(CalendarEvent ev)
    {
        var tip = new StackPanel();
        var title = Theme.Label(ev.Title, 13, Theme.Text);
        title.FontWeight = FontWeights.SemiBold;
        tip.Children.Add(title);
        tip.Children.Add(Theme.Label(CalendarFormat.Range(ev), 12, Theme.Text2));
        if (ev.Location.Length > 0) tip.Children.Add(Theme.Label(ev.Location, 11.5, Theme.Muted));
        tip.Children.Add(Theme.Label(ev.CalendarLabel + (ev.Source == CalendarSource.Ics ? " (link, view only)" : ""), 11.5, Theme.Muted));
        return new ToolTip { Content = tip, Background = Theme.Surface3, BorderBrush = Theme.LineStrong };
    }

    /// <summary>A Google or iCal event in the week grid, in the lane the layout gave it.</summary>
    private Border ExternalBlock(LauncherItem item, CalendarEvent ev, DateOnly date, double width, double height, bool tight)
    {
        var (titleBrush, detailBrush) = ExternalText(item, ev);
        var (s, e) = ev.MinutesOn(date);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var stack = new StackPanel();
        if (height >= 13)
        {
            var title = Theme.Label(ev.Title, tight ? 9.5 : 10.5, titleBrush);
            title.FontWeight = FontWeights.SemiBold;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            stack.Children.Add(title);
        }
        if (height >= 30 && width >= 60)
        {
            var time = Theme.Label($"{Planner.Format($"{s / 60 % 24:00}:{s % 60:00}", false)} – {Planner.Format($"{e / 60 % 24:00}:{e % 60:00}", false)}", 9, detailBrush);
            time.TextTrimming = TextTrimming.CharacterEllipsis;
            time.Opacity = 0.85;
            stack.Children.Add(time);
        }
        grid.Children.Add(stack);
        if (height >= 14 && width >= 58 && CalendarVisuals.SourceMark(ev, 10, detailBrush) is { } mark)
        {
            mark.VerticalAlignment = VerticalAlignment.Top;
            mark.Margin = new Thickness(3, 1, 0, 0);
            Grid.SetColumn(mark, 1);
            grid.Children.Add(mark);
        }
        var view = new Border { Width = Math.Max(4, width), Height = height, CornerRadius = new CornerRadius(5), Padding = new Thickness(5, 2, 3, 2), ClipToBounds = true, Child = grid, ToolTip = ExternalTip(ev) };
        StyleExternal(view, item, ev);
        AttachExternalInteractions(view, ev);
        return view;
    }

    /// <summary>Where each all-day event goes in the strip under the day headers: columns first..last on a row.</summary>
    internal static (List<(CalendarEvent Event, int First, int Last, int Row)> Bars, int Rows, int Hidden) AllDayLayout(IEnumerable<CalendarEvent> events, DateOnly monday, int dayCount, int maxRows = 3)
    {
        var bars = new List<(CalendarEvent, int, int, int)>();
        var rowEnds = new List<int>();
        int hidden = 0;
        foreach (var ev in events.Where(e => e.AllDay).OrderBy(e => e.Start).ThenByDescending(e => e.End))
        {
            int first = Math.Max(0, ev.FirstDay.DayNumber - monday.DayNumber), last = Math.Min(dayCount - 1, ev.LastDay.DayNumber - monday.DayNumber);
            if (last < first) continue;
            int row = rowEnds.FindIndex(end => end < first);
            if (row < 0) { row = rowEnds.Count; rowEnds.Add(last); } else rowEnds[row] = last;
            if (row >= maxRows) { hidden++; continue; }
            bars.Add((ev, first, last, row));
        }
        return (bars, Math.Min(maxRows, rowEnds.Count), hidden);
    }

    /// <summary>The strip of all-day events for the week view.</summary>
    private Canvas AllDayStrip(LauncherItem item, List<(CalendarEvent Event, int First, int Last, int Row)> bars, int hidden, double gutter, double colW, double height)
    {
        var strip = new Canvas { Height = height, ClipToBounds = true };
        foreach (var (ev, first, last, row) in bars)
        {
            var (titleBrush, _) = ExternalText(item, ev);
            var label = Theme.Label(ev.Title, 9.5, titleBrush);
            label.FontWeight = FontWeights.SemiBold;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition());
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.Children.Add(label);
            if (CalendarVisuals.SourceMark(ev, 9, titleBrush) is { } mark) { mark.Margin = new Thickness(3, 0, 0, 0); Grid.SetColumn(mark, 1); content.Children.Add(mark); }
            var bar = new Border { Width = Math.Max(4, (last - first + 1) * colW - 3), Height = 16, CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 0, 3, 0), Child = content, ToolTip = ExternalTip(ev) };
            StyleExternal(bar, item, ev);
            AttachExternalInteractions(bar, ev);
            Canvas.SetLeft(bar, gutter + first * colW + 1);
            Canvas.SetTop(bar, 2 + row * 18);
            strip.Children.Add(bar);
        }
        if (hidden > 0)
        {
            var more = Theme.Label($"+{hidden}", 9, Theme.Muted);
            more.ToolTip = $"{hidden} more all-day event(s) this week";
            Canvas.SetLeft(more, 4);
            Canvas.SetTop(more, height - 15);
            strip.Children.Add(more);
        }
        return strip;
    }

    /// <summary>A row in the day view for a Google or iCal event.</summary>
    private Border ExternalRow(LauncherItem item, CalendarEvent ev, DateOnly date, bool current, bool past)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bool outline = item.ExternalStyle == "outline";
        grid.Children.Add(new Border { Width = 4, CornerRadius = new CornerRadius(2), Background = Theme.Accent(ev.Color), HorizontalAlignment = HorizontalAlignment.Left });
        var text = new StackPanel();
        var title = Theme.Label(ev.Title, 13, Theme.Text);
        title.FontWeight = FontWeights.SemiBold;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        text.Children.Add(title);
        var (s, e) = ev.MinutesOn(date);
        string when = ev.AllDay ? "All day" : $"{Planner.Format($"{s / 60 % 24:00}:{s % 60:00}", false)} – {Planner.Format($"{e / 60 % 24:00}:{e % 60:00}", false)}";
        text.Children.Add(Theme.Label(ev.Location.Length > 0 ? when + " · " + ev.Location : when, 11.5, Theme.Text2));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (current) right.Children.Add(Pill("Now", ev.Color));
        if (CalendarVisuals.SourceMark(ev, 11, Theme.Muted) is { } mark) { mark.Margin = new Thickness(6, 0, 0, 0); right.Children.Add(mark); }
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        var row = Row(grid);
        row.Margin = new Thickness(0, 0, 0, 4);
        row.Opacity = past ? 0.45 : 1;
        if (outline) { row.BorderBrush = Theme.Tint(ev.Color, 0.8); row.BorderThickness = new Thickness(1); }
        else { var tint = Theme.Tint(ev.Color, 0.16); row.Background = tint; row.MouseLeave += (_, _) => row.Background = tint; }
        row.ToolTip = ExternalTip(ev);
        AttachExternalInteractions(row, ev);
        return row;
    }

    /// <summary>The all-day chips at the top of the day view.</summary>
    private FrameworkElement AllDayChips(LauncherItem item, List<CalendarEvent> events)
    {
        var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var ev in events)
        {
            var (titleBrush, _) = ExternalText(item, ev);
            var label = Theme.Label(ev.Title, 11, titleBrush);
            label.FontWeight = FontWeights.SemiBold;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            label.MaxWidth = 200;
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(label);
            if (CalendarVisuals.SourceMark(ev, 9, titleBrush) is { } mark) { mark.Margin = new Thickness(5, 0, 0, 0); content.Children.Add(mark); }
            var chip = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(9, 2, 9, 3), Margin = new Thickness(0, 0, 6, 5), Child = content, ToolTip = ExternalTip(ev) };
            StyleExternal(chip, item, ev);
            AttachExternalInteractions(chip, ev);
            wrap.Children.Add(chip);
        }
        return wrap;
    }

    // ---------- Local blocks seen as events (for the Agenda widget) ----------

    /// <summary>Every Schedule widget's local blocks as events between two days (inclusive start, exclusive end).</summary>
    internal List<CalendarEvent> LocalScheduleEvents(DateOnly from, DateOnly toExclusive)
    {
        var events = new List<CalendarEvent>();
        foreach (var schedule in settings.Items.Where(i => i.Kind == ItemKinds.Schedule))
            for (var day = from; day < toExclusive; day = day.AddDays(1))
                foreach (var block in Planner.Occurrences(schedule.Blocks, day))
                {
                    var date = day.ToDateTime(TimeOnly.MinValue);
                    events.Add(new CalendarEvent
                    {
                        Source = CalendarSource.Local, CalendarId = schedule.Id, CalendarLabel = string.IsNullOrWhiteSpace(schedule.Name) ? "Schedule" : schedule.Name,
                        Color = CalendarColors.Normalize(block.Color, "#C4684F"), Title = block.Title, Start = date.AddMinutes(Planner.Minutes(block.Start)!.Value), End = date.AddMinutes(Planner.Minutes(block.End)!.Value),
                        LocalItemId = schedule.Id, LocalBlockId = block.Id,
                    });
                }
        return events;
    }
}
