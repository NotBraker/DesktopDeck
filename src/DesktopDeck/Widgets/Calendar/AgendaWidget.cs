using System.Windows;
using System.Windows.Controls;
using DesktopDeck.Models;
using DesktopDeck.Services;

namespace DesktopDeck.Widgets;

/// <summary>
/// Upcoming events from Google Calendar, iCal links and your Schedule blocks, grouped by day. Click one to open it
/// (Google events you can edit open the editor). Pure grouping lives in AgendaLogic.cs (unit tested).
/// </summary>
internal sealed class AgendaWidget : IWidget
{
    public string Kind => "agenda";
    public string Name => "Agenda";
    public string Description => "What's coming up: Google, iCal and Schedule events by day";
    public string Section => "Connected";
    public string Glyph => Theme.Glyphs.Calendar;
    public string Accent => "#4285f4";
    public (double Width, double Height) DefaultSize => (324, 372);
    public (double Width, double Height) MinimumSize => (204, 132);

    public IReadOnlyList<OptionSpec> Options { get; } =
    [
        OptionSpec.Number("days", "Days ahead", 7, 1, 30, "What to show"),
        OptionSpec.Number("maxItems", "Most events to show", 12, 1, 60, "What to show"),
        OptionSpec.Toggle("google", "Google Calendar events", true, "Sources", "Needs a connected account in Settings → Connected accounts."),
        OptionSpec.Toggle("ics", "Calendars from iCal links", true, "Sources"),
        OptionSpec.Toggle("reminders", "Your reminders", true, "Sources", "The personal reminders from the Reminders tab."),
        OptionSpec.Toggle("local", "Schedule blocks from this deck", true, "Sources"),
        OptionSpec.Toggle("hideAllDay", "Hide all-day events", false, "What to show"),
        OptionSpec.Toggle("showEmptyDays", "Show days with nothing planned", false, "What to show"),
        OptionSpec.Toggle("showLocation", "Show the location", true, "Layout"),
        OptionSpec.Toggle("showColor", "Show the calendar color", true, "Layout"),
        OptionSpec.Toggle("showSource", "Small icon on Google and iCal events", true, "Layout"),
        OptionSpec.Toggle("compact", "Compact (one line per event)", false, "Layout"),
        OptionSpec.Choice("timeFormat", "Time format", "12h", ["12h", "24h"], ["12-hour", "24-hour"], "Layout"),
        OptionSpec.Toggle("showEnd", "Show end times", false, "Layout"),
        OptionSpec.Choice("dayHeaders", "Day headings", "relative", ["relative", "date"], ["Today, Tomorrow, then dates", "Always the date"], "Layout"),
        OptionSpec.Toggle("highlightNow", "Highlight what is happening now", true, "Behavior"),
    ];

    public IEnumerable<Action<LauncherItem>> Samples =>
    [
        _ => { },
        item => { item.Options["compact"] = "true"; item.Options["timeFormat"] = "24h"; item.Options["showLocation"] = "false"; item.Width = 276; item.Height = 324; },
        item => { item.Options["showEmptyDays"] = "true"; item.Options["days"] = "5"; item.Options["showEnd"] = "true"; item.Options["dayHeaders"] = "date"; item.Options["hideAllDay"] = "true"; },
    ];

    public IEnumerable<WidgetAction> Actions(WidgetContext ctx)
    {
        yield return new WidgetAction("Refresh now", Theme.Glyphs.Refresh, () => ctx.Calendar.Refresh());
        if (ctx.Calendar.GoogleCanEdit) yield return new WidgetAction("Add Google event…", Theme.Glyphs.Add, () => ctx.AddGoogleEvent(DateOnly.FromDateTime(DateTime.Now)));
    }

    public FrameworkElement Build(WidgetContext ctx)
    {
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        var options = new AgendaOptions(ctx.GetInt("days"), ctx.GetInt("maxItems"), ctx.GetBool("google"), ctx.GetBool("ics"), ctx.GetBool("local"), ctx.GetBool("hideAllDay"), ctx.GetBool("showEmptyDays"), ctx.Get("timeFormat") == "24h", ctx.GetBool("reminders"));
        List<CalendarEvent> events;
        CalendarSnapshot? snapshot = null;
        bool anySource;
        if (ctx.IsSnapshot)
        {
            // Smoke renders never touch the feed, the network or the user's data.
            events = AgendaLogic.Sample(now);
            anySource = true;
        }
        else
        {
            snapshot = ctx.Calendar.Query(today, today.AddDays(options.Days), new CalendarQuery(options.Google, [], options.Ics));
            events = snapshot.Events.ToList();
            if (options.Local) events.AddRange(ctx.LocalScheduleEvents(today, today.AddDays(options.Days)));
            if (options.Reminders) events.AddRange(ctx.Reminders.ReminderEvents(today, today.AddDays(options.Days)));
            anySource = (options.Google && ctx.Calendar.GoogleReady) || (options.Ics && snapshot.Events.Any(e => e.Source == CalendarSource.Ics)) || options.Local || options.Reminders;
        }

        FrameworkElement? note = snapshot?.Note is { } text ? WidgetKit.Muted(ctx, text, 10.5) : null;
        var (card, body) = WidgetKit.Frame(ctx, note);
        var result = AgendaLogic.Group(events, now, options);
        if (result.Days.Count == 0 || result.Days.All(d => d.Events.Count == 0 && !options.ShowEmptyDays))
        {
            string message = !anySource ? "Connect Google Calendar or add a calendar link in Settings → Connected accounts."
                : snapshot is { Loading: true, LastUpdated: null } ? "Loading your calendars…"
                : $"Nothing coming up in the next {options.Days} day{(options.Days == 1 ? "" : "s")}.";
            body.Children.Add(WidgetKit.Empty(ctx, message));
            ctx.Tick(() => false, _ => ctx.Rebuild());
            return card;
        }

        bool compact = ctx.GetBool("compact"), showColor = ctx.GetBool("showColor"), showLocation = ctx.GetBool("showLocation"), showSource = ctx.GetBool("showSource"), withEnd = ctx.GetBool("showEnd"), highlight = ctx.GetBool("highlightNow");
        bool relative = ctx.Get("dayHeaders") != "date";
        var list = new StackPanel();
        foreach (var day in result.Days)
        {
            var head = WidgetKit.Text(ctx, AgendaLogic.DayLabel(day.Day, today, relative), compact ? 11 : 12, day.Day == today ? ctx.Accent : Theme.Muted, bold: true);
            head.Margin = new Thickness(4, list.Children.Count == 0 ? 0 : compact ? 6 : 10, 0, compact ? 1 : 3);
            list.Children.Add(head);
            if (day.Events.Count == 0) { var none = WidgetKit.Muted(ctx, "Nothing planned", 11); none.Margin = new Thickness(6, 0, 0, 0); list.Children.Add(none); }
            foreach (var ev in day.Events)
            {
                bool isNow = highlight && AgendaLogic.IsNow(ev, now);
                string time = AgendaLogic.TimeText(ev, day.Day, options.Use24Hour, withEnd);
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(showColor ? 12 : 0) });
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                if (showColor) grid.Children.Add(new Border { Width = 4, CornerRadius = new CornerRadius(2), Background = Theme.Accent(ev.Color), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 1, 0, 1) });
                var text2 = new StackPanel();
                if (compact)
                {
                    var line = new StackPanel { Orientation = Orientation.Horizontal };
                    var when = WidgetKit.Muted(ctx, time, 11);
                    when.Margin = new Thickness(0, 0, 8, 0);
                    line.Children.Add(when);
                    line.Children.Add(WidgetKit.Text(ctx, ev.Title, 12.5, bold: isNow));
                    text2.Children.Add(line);
                }
                else
                {
                    text2.Children.Add(WidgetKit.Text(ctx, ev.Title, 13, bold: true));
                    string detail = showLocation && ev.Location.Length > 0 ? time + " · " + ev.Location : time;
                    text2.Children.Add(WidgetKit.Muted(ctx, detail, 11.5));
                }
                Grid.SetColumn(text2, 1);
                grid.Children.Add(text2);
                var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                if (isNow) right.Children.Add(WidgetKit.Pill(ctx, "Now", ev.Color));
                if (showSource && CalendarVisuals.SourceMark(ev, 11, Theme.Muted) is { } mark) { mark.Margin = new Thickness(6, 0, 0, 0); right.Children.Add(mark); }
                Grid.SetColumn(right, 2);
                grid.Children.Add(right);
                var row = WidgetKit.Row(grid);
                if (compact) row.Padding = new Thickness(6, 2, 6, 2);
                if (isNow) { var tint = Theme.Tint(ev.Color, 0.14); row.Background = tint; row.MouseLeave += (_, _) => row.Background = tint; }
                var captured = ev;
                WidgetKit.Clickable(row, () => ctx.OpenEvent(captured), ev.CalendarLabel + (ev.Location.Length > 0 ? " · " + ev.Location : ""));
                list.Children.Add(row);
            }
        }
        if (result.More > 0)
        {
            var more = WidgetKit.Muted(ctx, $"+{result.More} more", 11);
            more.Margin = new Thickness(6, 8, 0, 0);
            list.Children.Add(more);
        }
        body.Children.Add(WidgetKit.Scroll(list));
        ctx.Tick(() => false, _ => ctx.Rebuild());
        return card;
    }
}
