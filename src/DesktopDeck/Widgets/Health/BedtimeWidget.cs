using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DesktopDeck.Models;

namespace DesktopDeck.Widgets;

/// <summary>
/// Bedtime and sleep: a countdown to bedtime with a wind-down notice N minutes before, suggestions that line up with
/// 90-minute sleep cycles, and an optional 1-5 "how did I sleep" log with a 7-day strip. Nights that cross midnight and
/// separate weekend times are handled in BedtimeLogic.cs; notices are marked in "data.*" so each fires once per night.
/// </summary>
internal sealed class BedtimeWidget : IWidget
{
    public string Kind => "bedtime";
    public string Name => "Bedtime";
    public string Description => "Countdown to bedtime, wind-down reminder, sleep cycles and a sleep log";
    public string Section => "Health";
    public string Glyph => "";
    public string Accent => "#8b8cf6";
    public (double Width, double Height) DefaultSize => (324, 276);
    public (double Width, double Height) MinimumSize => (180, 132);

    public IReadOnlyList<OptionSpec> Options { get; } =
    [
        new OptionSpec("bedtime", "Bedtime", OptionType.Time, "23:00") { Group = "Schedule" },
        new OptionSpec("wake", "Wake-up time", OptionType.Time, "07:00") { Group = "Schedule" },
        OptionSpec.Toggle("weekend", "Different times on weekends", false, "Schedule", "Applies to nights that end on a Saturday or Sunday morning."),
        new OptionSpec("weekendBedtime", "Weekend bedtime", OptionType.Time, "00:00") { Group = "Schedule" },
        new OptionSpec("weekendWake", "Weekend wake-up", OptionType.Time, "09:00") { Group = "Schedule" },
        OptionSpec.Number("windDown", "Wind down this long before bed (minutes)", 30, 0, 180, "Reminders"),
        OptionSpec.Toggle("notifyWindDown", "Notify at wind-down time", true, "Reminders"),
        OptionSpec.Toggle("notifyBedtime", "Notify at bedtime", false, "Reminders"),
        OptionSpec.Toggle("sound", "Play a sound with reminders", false, "Reminders"),
        OptionSpec.Text("windMessage", "Wind-down message", "Time to wind down: dim the lights and put the screens away.", "Reminders"),
        OptionSpec.Number("fallAsleep", "Minutes to fall asleep", 15, 0, 90, "Sleep cycles", "Used to line bedtimes up with 90-minute cycles."),
        OptionSpec.Choice("suggest", "Suggestions", "auto", ["auto", "bedtimes", "wakeups", "none"], ["Automatic", "Bedtimes for my wake-up", "Wake-ups if I sleep now", "None"], "Sleep cycles"),
        OptionSpec.Toggle("log", "Log how I slept (1-5)", true, "Sleep log"),
        OptionSpec.Toggle("strip", "Show the last 7 nights", true, "Sleep log"),
        OptionSpec.Choice("clock", "Clock", "auto", ["auto", "24", "12"], ["Follow Windows", "24-hour", "12-hour"], "Layout"),
        OptionSpec.Color("moonColor", "Countdown color", "", "Colors", "Empty = the accent color."),
        OptionSpec.Color("windColor", "Wind-down color", "#f59e0b", "Colors"),
    ];

    // ---------- Samples ----------

    private static string Clock(DateTime at) => at.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static void Ratings(LauncherItem item, params int[] newestFirst)
    {
        var ratings = new Dictionary<string, int>();
        for (int ago = 0; ago < newestFirst.Length; ago++) if (newestFirst[ago] > 0) ratings[HealthLogic.DayKey(DateTime.Today.AddDays(-ago))] = newestFirst[ago];
        item.Options["data.sleep"] = BedtimeLogic.FormatRatings(ratings, DateTime.Today);
    }

    public IEnumerable<Action<LauncherItem>> Samples =>
    [
        item => { var now = DateTime.Now; item.Options["bedtime"] = Clock(now.AddMinutes(200)); item.Options["wake"] = Clock(now.AddMinutes(200 + 8 * 60)); Ratings(item, 0, 4, 3, 5, 2, 4, 4); },
        item => { var now = DateTime.Now; item.Options["bedtime"] = Clock(now.AddMinutes(22)); item.Options["wake"] = Clock(now.AddMinutes(22 + 7 * 60 + 30)); item.Options["clock"] = "12"; item.Options["windDown"] = "45"; Ratings(item, 0, 3, 4, 4, 1, 5, 3); },
        item => { var now = DateTime.Now; item.Options["bedtime"] = Clock(now.AddMinutes(-95)); item.Options["wake"] = Clock(now.AddMinutes(-95 + 8 * 60)); Ratings(item, 0, 5, 4, 3, 3, 2, 4); item.Width = 276; item.Height = 228; },
        item => { var now = DateTime.Now; item.Options["bedtime"] = Clock(now.AddMinutes(-8 * 60 - 40)); item.Options["wake"] = Clock(now.AddMinutes(-40)); Ratings(item, 4, 3, 5, 2, 4, 4, 3); },
    ];

    // ---------- Settings ----------

    private sealed record Plan(TimeSpan Bed, TimeSpan Wake, bool Weekend, TimeSpan WeekendBed, TimeSpan WeekendWake, int WindDown, int FallAsleep);

    private static Plan Settings(WidgetContext ctx) => new(
        HealthLogic.ParseTime(ctx.Get("bedtime"), TimeSpan.FromHours(23)), HealthLogic.ParseTime(ctx.Get("wake"), TimeSpan.FromHours(7)), ctx.GetBool("weekend"),
        HealthLogic.ParseTime(ctx.Get("weekendBedtime"), TimeSpan.Zero), HealthLogic.ParseTime(ctx.Get("weekendWake"), TimeSpan.FromHours(9)), ctx.GetInt("windDown"), ctx.GetInt("fallAsleep"));

    private static BedtimeNight Current(Plan p, DateTime now) => BedtimeLogic.Current(now, p.Bed, p.Wake, p.Weekend, p.WeekendBed, p.WeekendWake, p.WindDown);

    private static string Time(WidgetContext ctx, DateTime at) => HealthLogic.FormatClock(at.TimeOfDay, HealthLogic.Use24Hour(ctx.Get("clock")));
    private static string Time(WidgetContext ctx, TimeSpan at) => HealthLogic.FormatClock(at, HealthLogic.Use24Hour(ctx.Get("clock")));

    // ---------- Notices ----------

    /// <summary>
    /// Once a minute: shows the wind-down and bedtime notices. Each is marked in "data.*" first, so another copy of the
    /// widget (other monitor, floating window) running the same check sees the marker and stays quiet.
    /// </summary>
    private static void Check(WidgetContext ctx, DateTime now)
    {
        var plan = Settings(ctx);
        var night = Current(plan, now);
        var (wind, bed, windMarker, bedMarker) = BedtimeLogic.Due(night, now, ctx.Get("data.windFired"), ctx.Get("data.bedFired"));
        if (windMarker != ctx.Get("data.windFired") || bedMarker != ctx.Get("data.bedFired"))
        {
            ctx.Item.Options["data.windFired"] = windMarker;
            ctx.Item.Options["data.bedFired"] = bedMarker;
            ctx.Changed(false);
        }
        bool ring = false;
        if (wind && ctx.GetBool("notifyWindDown")) { ctx.Notify("Wind down", ctx.Get("windMessage") + $" Bedtime is {Time(ctx, night.Bed)}."); ring = true; }
        if (bed && ctx.GetBool("notifyBedtime")) { ctx.Notify("Bedtime", $"Time for bed. Wake-up is at {Time(ctx, night.Wake)}."); ring = true; }
        if (ring && ctx.GetBool("sound")) System.Media.SystemSounds.Asterisk.Play();
    }

    private static void Rate(WidgetContext ctx, DateTime wakeDate, int rating)
    {
        if (ctx.IsEditing) return;
        var ratings = BedtimeLogic.ParseRatings(ctx.Get("data.sleep"));
        BedtimeLogic.Toggle(ratings, wakeDate, rating);
        ctx.Item.Options["data.sleep"] = BedtimeLogic.FormatRatings(ratings, DateTime.Today, ctx.HistoryDays(HistoryKeys.Bedtime));
        ctx.Changed(true);
    }

    public IEnumerable<WidgetAction> Actions(WidgetContext ctx)
    {
        if (!ctx.GetBool("log")) yield break;
        for (int rating = 5; rating >= 1; rating--)
        {
            int r = rating;
            yield return new($"{BedtimeLogic.RatingWord(r)} ({r}/5)", null, () => Rate(ctx, DateTime.Today, r), "Log last night");
        }
    }

    // ---------- Drawing ----------

    public FrameworkElement Build(WidgetContext ctx)
    {
        var now = DateTime.Now;
        var plan = Settings(ctx);
        if (!ctx.IsSnapshot)
        {
            Check(ctx, now);
            ctx.Tick(() => false, at => { Check(ctx, at.LocalDateTime); ctx.Rebuild(); });
        }
        var night = Current(plan, now);
        var accent = ctx.GetColor("moonColor");
        var windBrush = ctx.GetColor("windColor", Theme.Brush("#f59e0b"));
        var duration = BedtimeLogic.SleepDuration(night.Bed.TimeOfDay, night.Wake.TimeOfDay);
        double cycles = BedtimeLogic.Cycles(duration, plan.FallAsleep);

        string pill = night.Phase switch { BedtimePhase.WindDown => "Wind down", BedtimePhase.Asleep => "Sleep window", _ => "Bed " + Time(ctx, night.Bed) };
        var badge = ctx.Width >= 228 ? WidgetKit.Pill(ctx, pill, night.Phase == BedtimePhase.WindDown ? "#f59e0b" : null) : null;
        var (card, body) = WidgetKit.Frame(ctx, badge);
        double room = ctx.Height - ctx.GetNumber("_padding") * 2 - (ctx.Item.ShowHeader ? 34 : 0);

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        // 1. The countdown.
        string big, small;
        Brush bigBrush = accent;
        switch (night.Phase)
        {
            case BedtimePhase.Asleep:
                big = "Sleep window";
                small = $"Wake at {Time(ctx, night.Wake)} · in {HealthLogic.FormatSpan(night.Wake - now)}";
                break;
            case BedtimePhase.WindDown:
                big = HealthLogic.FormatSpan(night.Bed - now);
                small = "until bedtime · wind down";
                bigBrush = windBrush;
                break;
            default:
                big = HealthLogic.FormatSpan(night.Bed - now);
                small = "until bedtime at " + Time(ctx, night.Bed);
                break;
        }
        var bigText = WidgetKit.Text(ctx, big, room < 100 ? 20 : night.Phase == BedtimePhase.Asleep ? 22 : 28, bigBrush, true);
        stack.Children.Add(bigText);
        stack.Children.Add(WidgetKit.Muted(ctx, small, 11.5));
        double used = 62;

        // 2. The plan.
        if (room >= 100)
        {
            string plan1 = $"{Time(ctx, night.Bed)} to {Time(ctx, night.Wake)}  ·  {Math.Round(duration.TotalHours, 1).ToString("0.#", CultureInfo.CurrentCulture)} h  ·  {BedtimeLogic.CycleText(cycles)} cycles";
            stack.Children.Add(WidgetKit.Text(ctx, plan1, 11, Theme.Text2).WithMargin(0, 6, 0, 0));
            used += 24;
        }

        // 3. Sleep-cycle suggestions.
        string suggest = ctx.Get("suggest");
        if (suggest == "auto") suggest = night.Phase == BedtimePhase.Day ? "bedtimes" : "wakeups";
        if (suggest != "none" && room >= used + 48)
        {
            var row = new UniformGrid { Rows = 1, Margin = new Thickness(0, 10, 0, 0) };
            string heading;
            if (suggest == "bedtimes")
            {
                heading = $"Fall asleep at one of these to wake at {Time(ctx, night.Wake)} between cycles";
                foreach (var (c, bed) in BedtimeLogic.BedtimesFor(night.Wake.TimeOfDay, plan.FallAsleep)) row.Children.Add(Suggestion(ctx, Time(ctx, bed), $"{c} cycles"));
            }
            else
            {
                heading = "If you fall asleep now, wake up at";
                foreach (var (c, at) in BedtimeLogic.WakeTimesFrom(now, plan.FallAsleep)) row.Children.Add(Suggestion(ctx, Time(ctx, at), $"{c} cycles"));
            }
            if (room >= used + 64 && ctx.Width >= 228)
            {
                var title = WidgetKit.Muted(ctx, suggest == "bedtimes" ? "Bedtimes for your wake-up" : heading, 10.5);
                title.Margin = new Thickness(0, 10, 0, 0);
                title.ToolTip = heading;
                stack.Children.Add(title);
                row.Margin = new Thickness(0, 4, 0, 0);
                used += 16;
            }
            stack.Children.Add(row);
            used += 40;
        }

        // 4. How did I sleep?
        var ratings = BedtimeLogic.ParseRatings(ctx.Get("data.sleep"));
        var todaysWake = now.Date + (BedtimeLogic.PlanFor(now.Date, plan.Bed, plan.Wake, plan.Weekend, plan.WeekendBed, plan.WeekendWake).Wake);
        if (ctx.GetBool("log") && now >= todaysWake && room >= used + 36)
        {
            int current = ratings.GetValueOrDefault(HealthLogic.DayKey(now));
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            var label = WidgetKit.Muted(ctx, current > 0 ? $"Last night: {BedtimeLogic.RatingWord(current)}" : "How did you sleep?", 11);
            label.Margin = new Thickness(0, 0, 10, 0);
            if (ctx.Width >= 228) line.Children.Add(label);
            for (int r = 1; r <= 5; r++) line.Children.Add(RatingDot(ctx, r, current, now.Date));
            stack.Children.Add(line);
            used += 38;
        }

        // 5. The last seven nights.
        if (ctx.GetBool("strip") && ctx.GetBool("log") && room >= used + 52)
        {
            var days = BedtimeLogic.Recent(ratings, now.Date, 7);
            var grid = new UniformGrid { Rows = 1, Columns = 7, Height = 40, Margin = new Thickness(0, 10, 0, 0) };
            foreach (var (day, rating) in days)
            {
                bool today = day.Date == now.Date;
                var cell = new Grid { Margin = new Thickness(3, 0, 3, 0) };
                cell.RowDefinitions.Add(new RowDefinition());
                cell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var track = new Border { Background = Theme.Surface3, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 0, 3) };
                var inner = new Grid();
                inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Math.Max(0.0001, 5 - rating), GridUnitType.Star) });
                inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Math.Max(0.0001, rating), GridUnitType.Star) });
                var bar = new Border { CornerRadius = new CornerRadius(3), Background = Theme.Accent(BedtimeLogic.RatingColor(rating)), Opacity = rating == 0 ? 0 : 1 };
                Grid.SetRow(bar, 1);
                inner.Children.Add(bar);
                track.Child = inner;
                cell.Children.Add(track);
                var letter = WidgetKit.Text(ctx, day.ToString("ddd", CultureInfo.CurrentCulture)[..1], 9, today ? ctx.TextBrush : Theme.Dim, today);
                letter.HorizontalAlignment = HorizontalAlignment.Center;
                Grid.SetRow(letter, 1);
                cell.Children.Add(letter);
                cell.ToolTip = rating == 0 ? $"{day:dddd}: not logged" : $"{day:dddd}: {BedtimeLogic.RatingWord(rating)} ({rating}/5)";
                grid.Children.Add(cell);
            }
            stack.Children.Add(grid);
        }

        body.Children.Add(stack);
        return card;
    }

    private static FrameworkElement Suggestion(WidgetContext ctx, string time, string caption)
    {
        var s = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
        s.Children.Add(WidgetKit.Text(ctx, time, 12.5, bold: true));
        s.Children.Add(WidgetKit.Muted(ctx, caption, 9.5));
        return s;
    }

    private static FrameworkElement RatingDot(WidgetContext ctx, int rating, int current, DateTime wakeDate)
    {
        var color = Theme.Accent(BedtimeLogic.RatingColor(rating));
        bool selected = rating == current;
        var label = WidgetKit.Text(ctx, rating.ToString(CultureInfo.InvariantCulture), 11, selected ? Brushes.White : Theme.Text2, true);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        var dot = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 6, 0),
            Background = selected ? color : Theme.Surface3, BorderBrush = color, BorderThickness = new Thickness(selected ? 0 : 1.2), Child = label,
        };
        return WidgetKit.Clickable(dot, () => Rate(ctx, wakeDate, rating), $"{rating}/5: {BedtimeLogic.RatingWord(rating)}{(selected ? " (click again to clear)" : "")}");
    }
}
