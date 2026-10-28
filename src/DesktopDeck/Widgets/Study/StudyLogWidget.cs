using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopDeck.Models;

namespace DesktopDeck.Widgets;

/// <summary>
/// Study time log: start and stop a session for a subject, see today and this week per subject, a weekly goal ring,
/// your streak and recent sessions. The running session is saved with its start time, so it keeps counting across
/// restarts like the stopwatch. Logic and tests: StudyLogLogic.cs.
/// </summary>
internal sealed class StudyLogWidget : IWidget
{
    public string Kind => "study-log";
    public string Name => "Study log";
    public string Description => "Time your study sessions, weekly goal and streak";
    public string Section => "Study";
    public string Glyph => "";
    public string Accent => "#C2A14D";
    public (double Width, double Height) DefaultSize => (468, 324);
    public (double Width, double Height) MinimumSize => (276, 204);

    public IReadOnlyList<OptionSpec> Options { get; } =
    [
        OptionSpec.Lines("subjects", "Subjects", StudySubjectsLogic.DefaultSubjects, "What to show", "One per line as Name | #color. Each one becomes a start button."),
        OptionSpec.Number("weeklyGoal", "Weekly goal (hours, 0 = none)", 10, 0, 100, "What to show"),
        OptionSpec.Toggle("showRing", "Show the goal ring", true, "What to show"),
        OptionSpec.Toggle("showStreak", "Show the streak", true, "What to show"),
        OptionSpec.Choice("chart", "Chart", "bars", ["bars", "days", "none"], ["Bars per subject", "Days of the week", "No chart"], "What to show"),
        OptionSpec.Toggle("showToday", "Mark today's part of each bar", true, "What to show"),
        OptionSpec.Toggle("showHistory", "Show recent sessions", true, "What to show", "Right-click a session to delete it."),
        OptionSpec.Number("historyCount", "Sessions listed", 5, 1, 30, "What to show"),
        OptionSpec.Choice("weekStart", "Week starts on", "monday", ["monday", "sunday", "saturday"], ["Monday", "Sunday", "Saturday"], "Layout"),
        OptionSpec.Choice("roundMinutes", "Round shown times to", "0", ["0", "5", "10", "15"], ["The minute", "5 minutes", "10 minutes", "15 minutes"], "Layout"),
        OptionSpec.Color("ringColor", "Ring color", "", "Colors", "Empty = the accent."),
        OptionSpec.Color("goalColor", "Goal reached color", "#9B9A5E", "Colors"),
        OptionSpec.Color("streakColor", "Streak color", "#E0A24F", "Colors"),
        OptionSpec.Number("streakMinutes", "Minutes that count as a study day", 10, 1, 240, "Behavior"),
        OptionSpec.Number("minSession", "Ignore sessions shorter than (minutes)", 1, 0, 30, "Behavior"),
        OptionSpec.Number("maxHours", "Stop a forgotten session after (hours, 0 = never)", 6, 0, 24, "Behavior"),
        OptionSpec.Toggle("notifyGoal", "Notify when the weekly goal is reached", true, "Behavior"),
    ];

    public IEnumerable<Action<LauncherItem>> Samples =>
    [
        item => { Seed(item, running: false); },
        item =>
        {
            Seed(item, running: true);
            item.Options["chart"] = "days";
            item.Options["showHistory"] = "false";
            item.Accent = "#5E9BA6";
            item.Width = 372; item.Height = 276;
        },
        item =>
        {
            Seed(item, running: false);
            item.Options["roundMinutes"] = "15";
            item.Options["weeklyGoal"] = "6";
            item.Options["weekStart"] = "sunday";
            item.Width = 276; item.Height = 324;
        },
    ];

    private static void Seed(LauncherItem item, bool running)
    {
        var now = DateTime.Now;
        string[] subjects = ["History", "Latin", "Chem", "Math"];
        var sessions = new List<StudyLogLogic.Session>();
        for (int back = 8; back >= 0; back--)
        {
            if (back is 3 or 6) continue;
            var day = now.Date.AddDays(-back);
            for (int i = 0; i < 1 + back % 3; i++)
            {
                var start = day.AddHours(15 + i * 1.8);
                sessions.Add(new StudyLogLogic.Session
                {
                    Start = StudyLogLogic.FormatStamp(start),
                    End = StudyLogLogic.FormatStamp(start.AddMinutes(25 + (back * 7 + i * 13) % 50)),
                    Subject = subjects[(back + i) % subjects.Length],
                });
            }
        }
        sessions = sessions.Where(s => s.EndTime < now.AddMinutes(-30) && s.StartTime.Date != now.Date).ToList();
        // Some time earlier today, so the bars and the ring have something to show on any weekday.
        foreach (var (ago, minutes, subject) in new[] { (200, 50, "Math"), (130, 35, "Latin"), (70, 25, "History") })
            sessions.Add(StudyLogLogic.Manual(subject, minutes, now.AddMinutes(-ago + minutes)));
        item.Options[StudyLogLogic.SessionsKey] = StudyLogLogic.SerializeSessions(sessions);
        if (running) StudyLogLogic.WriteRun(item.Options, new StudyLogLogic.Run(now.AddMinutes(-23).AddSeconds(-12), "Latin"));
    }

    // ---------- Starting and stopping ----------

    private static List<StudySubjectsLogic.Subject> Subjects(WidgetContext ctx) => StudySubjectsLogic.Parse(ctx.GetLines("subjects"));

    private static void SaveSessions(WidgetContext ctx, Action<List<StudyLogLogic.Session>> change, bool rebuild = true)
    {
        var list = StudyLogLogic.ParseSessions(ctx.Get(StudyLogLogic.SessionsKey));
        change(list);
        ctx.Set(StudyLogLogic.SessionsKey, StudyLogLogic.SerializeSessions(list, DateTime.Now, ctx.HistoryDays(HistoryKeys.StudyLog)), rebuild);
    }

    private static void Stop(WidgetContext ctx, bool rebuild = true)
    {
        if (StudyLogLogic.ReadRun(ctx.Item.Options) is not { } run) return;
        var session = StudyLogLogic.Finish(run, DateTime.Now, ctx.GetInt("minSession") * 60, ctx.GetNumber("maxHours"));
        StudyLogLogic.WriteRun(ctx.Item.Options, null);
        if (session is null) { ctx.Changed(rebuild); ctx.Status("That session was too short to log."); return; }
        SaveSessions(ctx, list => list.Add(session), rebuild);
    }

    private static void Start(WidgetContext ctx, string subject)
    {
        if (StudyLogLogic.ReadRun(ctx.Item.Options) is not null) Stop(ctx, rebuild: false);
        StudyLogLogic.WriteRun(ctx.Item.Options, new StudyLogLogic.Run(DateTime.Now, subject));
        ctx.Set("data.lastSubject", subject);
    }

    private static void Toggle(WidgetContext ctx, string subject)
    {
        if (StudyLogLogic.ReadRun(ctx.Item.Options) is { } run && string.Equals(run.Subject, subject, StringComparison.OrdinalIgnoreCase)) Stop(ctx);
        else Start(ctx, subject);
    }

    private static string DefaultSubject(WidgetContext ctx, List<StudySubjectsLogic.Subject> subjects)
    {
        string last = ctx.Get("data.lastSubject");
        if (StudySubjectsLogic.Find(subjects, last) is { } known) return known.Name;
        return subjects.Count > 0 ? subjects[0].Name : "Study";
    }

    public IEnumerable<WidgetAction> Actions(WidgetContext ctx)
    {
        var subjects = Subjects(ctx);
        bool running = StudyLogLogic.ReadRun(ctx.Item.Options) is not null;
        yield return new WidgetAction(running ? "Stop studying" : "Start studying", running ? Theme.Glyphs.Pause : Theme.Glyphs.Play, () => { if (running) Stop(ctx); else Start(ctx, DefaultSubject(ctx, subjects)); });
        yield return new WidgetAction("Log time by hand…", Theme.Glyphs.Add, () =>
        {
            string? text = ctx.Ask("Log study time", "How long and for what? e.g. 45 math, latin 1h30");
            if (text is null) return;
            if (StudyLogLogic.ParseManual(text, subjects) is not { } entry) { ctx.Status("Couldn't read that. Try: 45 math"); return; }
            string subject = entry.Subject.Length > 0 ? entry.Subject : DefaultSubject(ctx, subjects);
            SaveSessions(ctx, list => list.Add(StudyLogLogic.Manual(subject, entry.Minutes, DateTime.Now)));
        });
        if (running) yield return new WidgetAction("Discard this session", Theme.Glyphs.Delete, () => { StudyLogLogic.WriteRun(ctx.Item.Options, null); ctx.Changed(); });
        yield return new WidgetAction("Delete the last session", Theme.Glyphs.Delete, () => SaveSessions(ctx, list => { if (list.Count > 0) list.RemoveAt(list.Count - 1); }));
        yield return new WidgetAction("Clear all history…", Theme.Glyphs.Delete, () =>
        {
            if (ctx.Confirm("Delete every logged study session?", ctx.Item.Name)) SaveSessions(ctx, list => list.Clear());
        });
    }

    // ---------- Drawing ----------

    public FrameworkElement Build(WidgetContext ctx)
    {
        var now = DateTime.Now;
        var subjects = Subjects(ctx);
        var sessions = StudyLogLogic.ParseSessions(ctx.Get(StudyLogLogic.SessionsKey));
        var run = StudyLogLogic.ReadRun(ctx.Item.Options);
        double maxHours = ctx.GetNumber("maxHours");
        int round = int.Parse(ctx.Get("roundMinutes"), CultureInfo.InvariantCulture);
        var weekStart = StudyLogLogic.WeekStart(now, StudyLogLogic.WeekStartOption(ctx.Get("weekStart")));
        var week = StudyLogLogic.TotalsBySubject(sessions, run, now, weekStart, weekStart.AddDays(7), maxHours);
        var today = StudyLogLogic.TotalsBySubject(sessions, run, now, now.Date, now.Date.AddDays(1), maxHours);
        var weekTotal = week.Values.Aggregate(TimeSpan.Zero, (a, b) => a + b);
        var todayTotal = today.Values.Aggregate(TimeSpan.Zero, (a, b) => a + b);
        double goal = ctx.GetNumber("weeklyGoal");
        double fraction = StudyLogLogic.GoalFraction(weekTotal, goal);
        var (streak, best) = StudyLogLogic.Streak(sessions, run, now, ctx.GetInt("streakMinutes"), maxHours);
        bool wide = ctx.Width >= 400;
        bool tall = ctx.Height >= 260;

        FrameworkElement? badge = null;
        if (ctx.GetBool("showStreak"))
        {
            badge = streak > 0 ? StudyKit.PillBox(ctx, streak == 1 ? "1 day streak" : $"{streak} day streak", StudyKit.ValidHex(ctx.Get("streakColor"), "#E0A24F")) : WidgetKit.Pill(ctx, "no streak yet");
            badge.ToolTip = $"Best streak: {best} day{(best == 1 ? "" : "s")} (a day counts with {ctx.GetInt("streakMinutes")}+ minutes)";
        }
        var (card, body) = WidgetKit.Frame(ctx, badge);

        // Top: timer and subject buttons on the left, the goal ring on the right.
        var top = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel();

        string runSubject = run?.Subject ?? "";
        var clock = new TextBlock { FontFamily = Theme.UiFont, FontSize = ctx.Font(wide || tall ? 30 : 24), FontWeight = FontWeights.SemiBold, Foreground = run is null ? Theme.Muted : ctx.TextBrush, VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Documents.Typography.SetNumeralAlignment(clock, FontNumeralAlignment.Tabular);
        TimeSpan Elapsed(DateTime at) => run is null ? TimeSpan.Zero : StudyLogLogic.EffectiveEnd(run, at, maxHours) - run.Start;
        clock.Text = StudyLogLogic.FormatClock(Elapsed(now));

        var main = new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center,
            Background = run is null ? ctx.GetColor("ringColor") : Theme.Danger,
            Child = WidgetKit.Icon(run is null ? Theme.Glyphs.Play : Theme.Glyphs.Pause, 15, Theme.OnPrimary),
        };
        WidgetKit.Clickable(main, () => { if (run is null) Start(ctx, DefaultSubject(ctx, subjects)); else Stop(ctx); }, run is null ? "Start studying" : "Stop and save this session");
        var timerText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        timerText.Children.Add(clock);
        string caption = run is null ? (todayTotal > TimeSpan.Zero ? $"{StudyLogLogic.FormatDuration(todayTotal, round)} today" : "Pick a subject to start") : $"{runSubject}  ·  {StudyLogLogic.FormatDuration(todayTotal, round)} today";
        var captionText = WidgetKit.Muted(ctx, caption, 11);
        timerText.Children.Add(captionText);
        var timerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        timerRow.Children.Add(main);
        timerRow.Children.Add(timerText);
        left.Children.Add(timerRow);

        var chips = new WrapPanel();
        foreach (var subject in subjects)
        {
            bool on = run is not null && string.Equals(run.Subject, subject.Name, StringComparison.OrdinalIgnoreCase);
            var name = subject.Name;
            chips.Children.Add(StudyKit.Chip(ctx, name, subject.Color, on, () => Toggle(ctx, name), on ? "Stop " + name : "Study " + name, 11));
        }
        left.Children.Add(chips);
        top.Children.Add(left);

        if (ctx.GetBool("showRing"))
        {
            double ringSize = wide ? 88 : tall ? 76 : 64;
            var center = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            string weekText = StudyLogLogic.FormatHours(weekTotal);
            var big = Theme.Label(weekText, ctx.Font(ringSize * 0.26), ctx.TextBrush);
            big.FontWeight = FontWeights.SemiBold; big.HorizontalAlignment = HorizontalAlignment.Center;
            center.Children.Add(big);
            var small = Theme.Label(goal > 0 ? $"of {goal.ToString("0.#", CultureInfo.InvariantCulture)} h" : "hours", ctx.Font(Math.Max(8, ringSize * 0.13)), Theme.Muted);
            small.HorizontalAlignment = HorizontalAlignment.Center;
            center.Children.Add(small);
            Brush fill = fraction >= 1 ? StudyKit.Hex(ctx.Get("goalColor"), Theme.Olive) : ctx.GetColor("ringColor");
            var ring = WidgetKit.Ring(fraction, fill, ringSize, Math.Max(6, ringSize * 0.1), center);
            ring.Margin = new Thickness(10, 0, 0, 0);
            ring.VerticalAlignment = VerticalAlignment.Top;
            ring.ToolTip = goal > 0 ? $"{StudyLogLogic.FormatDuration(weekTotal, round)} of {goal:0.#} h this week ({(int)Math.Round(fraction * 100)}%)" : $"{StudyLogLogic.FormatDuration(weekTotal, round)} this week";
            Grid.SetColumn(ring, 1);
            top.Children.Add(ring);
        }
        DockPanel.SetDock(top, Dock.Top);
        body.Children.Add(top);

        // Below: the chart and recent sessions, scrolling if they do not fit.
        var list = new StackPanel();
        string chart = ctx.Get("chart");
        if (chart == "bars") list.Children.Add(Bars(ctx, subjects, week, today, round));
        else if (chart == "days") list.Children.Add(Days(ctx, sessions, run, now, weekStart, maxHours, goal));
        if (ctx.GetBool("showHistory") && sessions.Count > 0) list.Children.Add(History(ctx, sessions, subjects, now, round));
        if (chart == "none" && !ctx.GetBool("showHistory")) list.Children.Add(WidgetKit.Muted(ctx, $"{StudyLogLogic.FormatDuration(weekTotal, round)} this week", 12));
        body.Children.Add(StudyKit.ThinScroll(list));

        // The clock moves every second while running; the rest redraws once a minute.
        ctx.Tick(() => run is not null, tick =>
        {
            var at = tick.LocalDateTime;
            if (run is not null)
            {
                if (StudyLogLogic.IsOverLimit(run, at, maxHours))
                {
                    Stop(ctx);
                    ctx.Notify(ctx.Item.Name, $"Stopped your {runSubject} session after {maxHours:0.#} hours.");
                    return;
                }
                clock.Text = StudyLogLogic.FormatClock(Elapsed(at));
                if (at.Second == 0) { ctx.Rebuild(); return; }
                CheckGoal(ctx, goal, weekTotal + TimeSpan.FromSeconds(1), weekStart);
            }
            else ctx.Rebuild();
        });
        return card;
    }

    private static void CheckGoal(WidgetContext ctx, double goal, TimeSpan weekTotal, DateTime weekStart)
    {
        if (goal <= 0 || !ctx.GetBool("notifyGoal") || weekTotal.TotalHours < goal) return;
        string key = StudyLogLogic.FormatStamp(weekStart);
        if (ctx.Get("data.goalNotified") == key) return;
        ctx.Set("data.goalNotified", key, rebuild: false);
        ctx.Notify(ctx.Item.Name, $"You reached your weekly goal of {goal:0.#} hours. Well done!");
    }

    private static FrameworkElement Bars(WidgetContext ctx, List<StudySubjectsLogic.Subject> subjects, Dictionary<string, TimeSpan> week, Dictionary<string, TimeSpan> today, int round)
    {
        var names = subjects.Select(s => s.Name).ToList();
        foreach (var extra in week.Keys.Where(k => !names.Contains(k, StringComparer.OrdinalIgnoreCase))) names.Add(extra);
        double max = Math.Max(1, names.Select(n => week.GetValueOrDefault(n).TotalMinutes).DefaultIfEmpty(0).Max());
        var panel = new StackPanel();
        panel.Children.Add(StudyKit.SectionLabel(ctx, "This week", Theme.Muted));
        if (names.Count == 0) { panel.Children.Add(WidgetKit.Empty(ctx, "Add subjects in Customize to see them here.")); return panel; }
        foreach (var name in names)
        {
            var w = week.GetValueOrDefault(name);
            var t = today.GetValueOrDefault(name);
            string hex = StudySubjectsLogic.ColorOf(subjects, name);
            var row = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Clamp(ctx.Width * 0.2, 56, 90)) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = WidgetKit.Text(ctx, name, 12);
            label.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(label);
            var bars = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            bars.Children.Add(WidgetKit.Bar(w.TotalMinutes / max, Theme.Tint(hex, ctx.GetBool("showToday") ? 0.4 : 1), 8));
            if (ctx.GetBool("showToday") && t > TimeSpan.Zero) bars.Children.Add(WidgetKit.Bar(t.TotalMinutes / max, Theme.Brush(hex), 8, Brushes.Transparent));
            Grid.SetColumn(bars, 1);
            row.Children.Add(bars);
            var value = WidgetKit.Muted(ctx, StudyLogLogic.FormatDuration(w, round), 11.5);
            value.ToolTip = $"{name}: {StudyLogLogic.FormatDuration(w, round)} this week, {StudyLogLogic.FormatDuration(t, round)} today";
            value.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(value, 2);
            row.Children.Add(value);
            panel.Children.Add(row);
        }
        return panel;
    }

    private static FrameworkElement Days(WidgetContext ctx, List<StudyLogLogic.Session> sessions, StudyLogLogic.Run? run, DateTime now, DateTime weekStart, double maxHours, double goalHours)
    {
        var totals = StudyLogLogic.PerDay(sessions, run, now, weekStart, 7, maxHours);
        double scale = Math.Max(Math.Max(totals.Max(t => t.TotalMinutes), goalHours > 0 ? goalHours * 60 / 7 * 1.3 : 0), 30);
        var panel = new StackPanel();
        panel.Children.Add(StudyKit.SectionLabel(ctx, "This week by day", Theme.Muted));
        var grid = new Grid { Height = 92, Margin = new Thickness(0, 2, 0, 0) };
        for (int i = 0; i < 7; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var day = weekStart.AddDays(i);
            bool isToday = day.Date == now.Date;
            bool future = day.Date > now.Date;
            var column = new Grid { Margin = new Thickness(3, 0, 3, 0) };
            column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            column.RowDefinitions.Add(new RowDefinition());
            column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var value = Theme.Label(totals[i] > TimeSpan.Zero ? StudyLogLogic.FormatDuration(totals[i]).Replace(" ", "") : "", ctx.Font(9), Theme.Muted);
            value.HorizontalAlignment = HorizontalAlignment.Center;
            column.Children.Add(value);
            double frac = Math.Clamp(totals[i].TotalMinutes / scale, 0, 1);
            var track = new Grid { Margin = new Thickness(0, 2, 0, 3) };
            track.Children.Add(new Border { Background = Theme.Surface3, CornerRadius = new CornerRadius(4), Opacity = future ? 0.4 : 1 });
            var fill = new Border { Background = isToday ? ctx.Accent : Theme.Tint(ctx.Item.Accent, 0.55), CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Stretch };
            bool hasTime = totals[i] > TimeSpan.Zero;
            track.SizeChanged += (_, e) => fill.Height = Math.Max(hasTime ? 3 : 0, e.NewSize.Height * frac);
            track.Children.Add(fill);
            Grid.SetRow(track, 1);
            column.Children.Add(track);
            var dayLabel = Theme.Label(day.ToString("ddd", CultureInfo.CurrentCulture)[..Math.Min(3, day.ToString("ddd", CultureInfo.CurrentCulture).Length)], ctx.Font(9.5), isToday ? ctx.TextBrush : Theme.Muted);
            dayLabel.HorizontalAlignment = HorizontalAlignment.Center;
            dayLabel.FontWeight = isToday ? FontWeights.SemiBold : FontWeights.Normal;
            Grid.SetRow(dayLabel, 2);
            column.Children.Add(dayLabel);
            Grid.SetColumn(column, i);
            column.ToolTip = $"{day:dddd}: {StudyLogLogic.FormatDuration(totals[i])}";
            grid.Children.Add(column);
        }
        panel.Children.Add(grid);
        return panel;
    }

    private static FrameworkElement History(WidgetContext ctx, List<StudyLogLogic.Session> sessions, List<StudySubjectsLogic.Subject> subjects, DateTime now, int round)
    {
        var panel = new StackPanel();
        panel.Children.Add(StudyKit.SectionLabel(ctx, "Recent sessions", Theme.Muted));
        foreach (var s in sessions.OrderByDescending(s => s.Start, StringComparer.Ordinal).Take(ctx.GetInt("historyCount")))
        {
            var start = s.StartTime;
            string day = start.Date == now.Date ? "Today" : start.Date == now.Date.AddDays(-1) ? "Yesterday" : start.ToString("ddd d MMM", CultureInfo.CurrentCulture);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var dot = StudyKit.Dot(Theme.Brush(StudySubjectsLogic.ColorOf(subjects, s.Subject)), 8);
            dot.Margin = new Thickness(0, 0, 8, 0);
            grid.Children.Add(dot);
            var title = WidgetKit.Text(ctx, (s.Subject.Length > 0 ? s.Subject : "Study") + "  ", 12);
            var when = WidgetKit.Muted(ctx, $"{day}, {start.ToString("t", CultureInfo.CurrentCulture)}", 11);
            var text = new StackPanel { Orientation = Orientation.Horizontal };
            text.Children.Add(title);
            text.Children.Add(when);
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            var length = WidgetKit.Muted(ctx, StudyLogLogic.FormatDuration(s.Length, round), 11.5);
            Grid.SetColumn(length, 2);
            grid.Children.Add(length);
            var row = WidgetKit.Row(grid);
            row.Padding = new Thickness(4, 3, 4, 3);
            string stamp = s.Start;
            StudyKit.AttachMenu(row, [StudyKit.Item("Delete this session", () => SaveSessions(ctx, list => list.RemoveAll(x => x.Start == stamp)))]);
            panel.Children.Add(row);
        }
        return panel;
    }
}
