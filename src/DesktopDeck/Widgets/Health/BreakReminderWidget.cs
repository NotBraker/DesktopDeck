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
/// 20-20-20 eye breaks and stretch reminders: a ring counts down to the next break, a notification (and optional
/// sound) says when, a short countdown runs during the break, and every Nth break can be a longer stretch. The
/// schedule is a pure state machine (BreakReminderLogic.cs); this file saves it in Item.Options ("data.*") and draws it.
/// </summary>
internal sealed class BreakReminderWidget : IWidget
{
    public string Kind => "break-reminder";
    public string Name => "Break reminder";
    public string Description => "Eye-rest and stretch reminders with a countdown ring";
    public string Section => "Health";
    public string Glyph => "";
    public string Accent => "#38bdf8";
    public (double Width, double Height) DefaultSize => (276, 228);
    public (double Width, double Height) MinimumSize => (180, 132);

    public IReadOnlyList<OptionSpec> Options { get; } =
    [
        OptionSpec.Number("interval", "Break every (minutes)", 20, 1, 240, "Schedule", "The 20-20-20 rule: every 20 minutes, look 20 feet away for 20 seconds."),
        OptionSpec.Number("breakSeconds", "Eye break length (seconds)", 20, 5, 600, "Schedule"),
        OptionSpec.Number("stretchEvery", "Stretch on every Nth break (0 = never)", 3, 0, 20, "Schedule"),
        OptionSpec.Number("stretchSeconds", "Stretch break length (seconds)", 120, 10, 1800, "Schedule"),
        OptionSpec.Number("snooze", "Snooze (minutes)", 5, 1, 120, "Schedule"),
        OptionSpec.Toggle("activeOnly", "Only during active hours", true, "Active hours"),
        new OptionSpec("activeFrom", "From", OptionType.Time, "09:00") { Group = "Active hours" },
        new OptionSpec("activeTo", "Until", OptionType.Time, "18:00") { Group = "Active hours", Hint = "Outside these hours the countdown waits for the next morning." },
        OptionSpec.Toggle("weekdaysOnly", "Weekdays only", false, "Active hours"),
        OptionSpec.Toggle("notify", "Show a notification when a break starts", true, "Alerts"),
        OptionSpec.Toggle("notifyEnd", "Show a notification when it ends", false, "Alerts"),
        OptionSpec.Toggle("sound", "Play a sound", false, "Alerts"),
        OptionSpec.Text("eyeMessage", "Eye break message", "Look at something 20 feet (6 m) away for 20 seconds.", "Alerts"),
        OptionSpec.Text("stretchMessage", "Stretch message", "Stand up, roll your shoulders and stretch your back.", "Alerts"),
        OptionSpec.Toggle("showCount", "Show breaks taken today", true, "What to show"),
        OptionSpec.Toggle("showClock", "Show the time of the next break", true, "What to show"),
        OptionSpec.Choice("clock", "Clock", "auto", ["auto", "24", "12"], ["Follow Windows", "24-hour", "12-hour"], "What to show"),
        OptionSpec.Toggle("buttons", "Show the buttons", true, "Layout"),
        OptionSpec.Color("ringColor", "Countdown color", "", "Colors", "Empty = the accent color."),
        OptionSpec.Color("breakColor", "Break color", "", "Colors", "Empty = green."),
    ];

    // ---------- Samples ----------

    private static string Stamp(DateTime at) => HealthLogic.FormatStamp(at);

    public IEnumerable<Action<LauncherItem>> Samples =>
    [
        item => { item.Options["data.nextDue"] = Stamp(HealthLogic.CeilMinute(DateTime.Now.AddMinutes(12))); item.Options["data.day"] = HealthLogic.DayKey(DateTime.Today); item.Options["data.taken"] = "4"; item.Options["activeOnly"] = "false"; },
        item => { item.Options["data.breakEnd"] = Stamp(DateTime.Now.AddSeconds(13)); item.Options["data.kind"] = "eye"; item.Options["data.day"] = HealthLogic.DayKey(DateTime.Today); item.Options["data.taken"] = "5"; },
        item => { item.Options["data.paused"] = "540"; item.Options["data.day"] = HealthLogic.DayKey(DateTime.Today); item.Options["data.taken"] = "2"; item.Options["clock"] = "12"; item.Options["interval"] = "30"; },
        item => { item.Options["data.breakEnd"] = Stamp(DateTime.Now.AddSeconds(84)); item.Options["data.kind"] = "stretch"; item.Options["data.day"] = HealthLogic.DayKey(DateTime.Today); item.Options["data.taken"] = "2"; item.Width = 372; item.Height = 180; },
    ];

    // ---------- Saved state ----------

    private static BreakReminderSettings Settings(WidgetContext ctx)
        => new(ctx.GetInt("interval"), ctx.GetInt("breakSeconds"), ctx.GetInt("stretchEvery"), ctx.GetInt("stretchSeconds"), ctx.GetBool("activeOnly"),
               HealthLogic.ParseTime(ctx.Get("activeFrom"), TimeSpan.FromHours(9)), HealthLogic.ParseTime(ctx.Get("activeTo"), TimeSpan.FromHours(18)), ctx.GetBool("weekdaysOnly"));

    private static BreakReminderState Load(WidgetContext ctx, DateTime now)
    {
        string day = ctx.Get("data.day");
        return new BreakReminderState(
            HealthLogic.ParseStamp(ctx.Get("data.nextDue")), HealthLogic.ParseStamp(ctx.Get("data.breakEnd")), ctx.Get("data.kind") == "stretch",
            double.TryParse(ctx.Get("data.paused"), NumberStyles.Float, CultureInfo.InvariantCulture, out double left) && left >= 0 ? left : null,
            day.Length > 0 ? day : HealthLogic.DayKey(now), int.TryParse(ctx.Get("data.taken"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int taken) ? Math.Max(0, taken) : 0);
    }

    /// <summary>Writes the state without redrawing; the caller decides what to redraw.</summary>
    private static void Save(WidgetContext ctx, BreakReminderState s)
    {
        var o = ctx.Item.Options;
        o["data.nextDue"] = s.NextDue is { } due ? HealthLogic.FormatStamp(due) : "";
        o["data.breakEnd"] = s.BreakEnd is { } end ? HealthLogic.FormatStamp(end) : "";
        o["data.kind"] = s.BreakIsStretch ? "stretch" : "eye";
        o["data.paused"] = s.PausedLeftSeconds is { } left ? left.ToString("0.#", CultureInfo.InvariantCulture) : "";
        o["data.day"] = s.Day;
        o["data.taken"] = s.Taken.ToString(CultureInfo.InvariantCulture);
        ctx.Changed(false);
    }

    /// <summary>
    /// Moves the saved state to <paramref name="now"/> and raises the notification. Every copy of the widget calls this
    /// from its own ticker; the first one saves the new state, so the others see no event and nothing fires twice.
    /// </summary>
    private static BreakReminderEvent Advance(WidgetContext ctx, DateTime now)
    {
        var cfg = Settings(ctx);
        var before = Load(ctx, now);
        var after = BreakReminderLogic.Step(before, now, cfg, out var ev);
        if (after != before) Save(ctx, after);
        switch (ev)
        {
            case BreakReminderEvent.BreakStarted:
                ctx.Item.Options["data.lastKind"] = after.BreakIsStretch ? "stretch" : "eye";
                if (ctx.GetBool("notify")) ctx.Notify(after.BreakIsStretch ? "Stretch break" : "Eye break", BreakReminderLogic.Describe(after.BreakIsStretch, ctx.Get("eyeMessage"), ctx.Get("stretchMessage")));
                if (ctx.GetBool("sound")) System.Media.SystemSounds.Asterisk.Play();
                break;
            case BreakReminderEvent.BreakEnded:
                if (ctx.GetBool("notifyEnd")) ctx.Notify("Break over", "Nice. Next break at " + HealthLogic.FormatClock((after.NextDue ?? now).TimeOfDay, HealthLogic.Use24Hour(ctx.Get("clock"))));
                if (ctx.GetBool("sound")) System.Media.SystemSounds.Asterisk.Play();
                break;
        }
        return ev;
    }

    /// <summary>Button handler: change the state, save, redraw every copy.</summary>
    private static void Apply(WidgetContext ctx, Func<BreakReminderState, DateTime, BreakReminderSettings, BreakReminderState> change)
    {
        var now = DateTime.Now;
        Advance(ctx, now);
        Save(ctx, change(Load(ctx, now), now, Settings(ctx)));
        ctx.Changed(true);
    }

    // ---------- Drawing ----------

    public FrameworkElement Build(WidgetContext ctx)
    {
        var now = DateTime.Now;
        var cfg = Settings(ctx);
        if (!ctx.IsSnapshot)
        {
            Advance(ctx, now);
            ctx.Tick(() => BreakReminderLogic.NeedsSeconds(Load(ctx, DateTime.Now)), at =>
            {
                var ev = Advance(ctx, at.LocalDateTime);
                if (ev != BreakReminderEvent.None) ctx.Changed(true); else ctx.Rebuild();
            });
        }
        var state = Load(ctx, now);
        // A fresh widget (or a sample without data) shows its first countdown.
        if (state.NextDue is null && state.BreakEnd is null && state.PausedLeftSeconds is null) state = BreakReminderLogic.Step(state, now, cfg, out _);
        if (state.Day != HealthLogic.DayKey(now)) state = state with { Day = HealthLogic.DayKey(now), Taken = 0 };

        var phase = BreakReminderLogic.PhaseOf(state);
        var left = BreakReminderLogic.Remaining(state, now);
        double fraction = 1 - BreakReminderLogic.Progress(state, now, cfg);
        var ringBrush = phase == BreakReminderLogic.Phase.Break ? ctx.GetColor("breakColor", Theme.Brush("#22c55e")) : phase == BreakReminderLogic.Phase.Paused ? Theme.Muted : ctx.GetColor("ringColor");
        bool stretch = state.BreakIsStretch;

        string badgeText = phase == BreakReminderLogic.Phase.Paused ? "Paused" : ctx.GetBool("showCount") ? state.Taken + (state.Taken == 1 ? " break" : " breaks") : "";
        var badge = ctx.Width >= 228 && badgeText.Length > 0 ? WidgetKit.Pill(ctx, badgeText) : null;
        var (card, body) = WidgetKit.Frame(ctx, badge);
        double bodyHeight = ctx.Height - ctx.GetNumber("_padding") * 2 - (ctx.Item.ShowHeader ? 34 : 0);

        string dial = phase == BreakReminderLogic.Phase.Break ? HealthLogic.FormatSpan(left, seconds: true) : HealthLogic.FormatSpan(left);
        string caption = phase switch { BreakReminderLogic.Phase.Break => stretch ? "stretch" : "look away", BreakReminderLogic.Phase.Paused => "paused", _ => "until break" };
        string title = phase switch { BreakReminderLogic.Phase.Break => stretch ? "Stretch and stand" : "Look away", BreakReminderLogic.Phase.Paused => "Paused", _ => "Next break" };
        string detail = phase switch
        {
            BreakReminderLogic.Phase.Break => BreakReminderLogic.Describe(stretch, ctx.Get("eyeMessage"), ctx.Get("stretchMessage")),
            BreakReminderLogic.Phase.Paused => "Press play when you're ready.",
            _ => ctx.GetBool("showClock") && state.NextDue is { } due ? "at " + HealthLogic.FormatClock(due.TimeOfDay, HealthLogic.Use24Hour(ctx.Get("clock"))) + (BreakReminderLogic.IsStretchNext(state.Taken, cfg) ? " · stretch" : " · eye break") : "",
        };

        bool compact = bodyHeight < 112;
        bool horizontal = compact || ctx.Width >= 228;
        double ringSize = compact ? Math.Clamp(bodyHeight - 2, 44, 80) : horizontal ? Math.Clamp(bodyHeight - 4, 90, 124) : Math.Clamp(Math.Min(ctx.Width - 70, bodyHeight - (ctx.GetBool("buttons") ? 44 : 0)), 64, 120);
        var centre = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var number = WidgetKit.Text(ctx, dial, Math.Max(11, ringSize * (dial.Length > 5 ? 0.17 : 0.22)), bold: true);
        number.HorizontalAlignment = HorizontalAlignment.Center;
        centre.Children.Add(number);
        if (ringSize >= 80)
        {
            var cap = WidgetKit.Muted(ctx, caption, 10);
            cap.HorizontalAlignment = HorizontalAlignment.Center;
            centre.Children.Add(cap);
        }
        var ring = WidgetKit.Ring(fraction, ringBrush, ringSize, Math.Max(5, ringSize * 0.085), centre);
        ring.HorizontalAlignment = HorizontalAlignment.Center;
        ring.VerticalAlignment = VerticalAlignment.Center;

        var buttons = ctx.GetBool("buttons") ? Buttons(ctx, phase) : null;

        if (horizontal)
        {
            var grid = new Grid { VerticalAlignment = VerticalAlignment.Center };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(ring);
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(compact ? 10 : 16, 0, 0, 0) };
            texts.Children.Add(WidgetKit.Text(ctx, title, compact ? 13 : 15, bold: true));
            if (detail.Length > 0)
            {
                var d = WidgetKit.Muted(ctx, detail, compact ? 10.5 : 11.5);
                d.TextWrapping = TextWrapping.Wrap;
                d.MaxHeight = ctx.Font(11.5) * (compact ? 2.7 : 3.9);
                d.Margin = new Thickness(0, 3, 0, 0);
                texts.Children.Add(d);
            }
            if (!compact && ctx.GetBool("showCount") && phase != BreakReminderLogic.Phase.Paused && badge is null)
                texts.Children.Add(WidgetKit.Muted(ctx, $"{state.Taken} taken today", 10.5).WithMargin(0, 4, 0, 0));
            if (buttons is not null && !compact) { buttons.Margin = new Thickness(0, 12, 0, 0); texts.Children.Add(buttons); }
            else if (buttons is not null && ctx.Width >= 228) { buttons.Margin = new Thickness(0, 6, 0, 0); texts.Children.Add(buttons); }
            Grid.SetColumn(texts, 1);
            grid.Children.Add(texts);
            body.Children.Add(grid);
        }
        else
        {
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(ring);
            if (buttons is not null) { buttons.HorizontalAlignment = HorizontalAlignment.Center; buttons.Margin = new Thickness(0, 10, 0, 0); stack.Children.Add(buttons); }
            body.Children.Add(stack);
        }
        return card;
    }

    private static StackPanel Buttons(WidgetContext ctx, BreakReminderLogic.Phase phase)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        void Add(string glyph, string tip, Func<BreakReminderState, DateTime, BreakReminderSettings, BreakReminderState> change, bool accent = false)
        {
            var button = WidgetKit.IconButton(glyph, tip, () => { if (!ctx.IsEditing) Apply(ctx, change); }, 30, accent);
            button.Margin = new Thickness(0, 0, 6, 0);
            row.Children.Add(button);
        }
        switch (phase)
        {
            case BreakReminderLogic.Phase.Break:
                Add(Theme.Glyphs.Check, "Done: count this break", (s, now, cfg) => BreakReminderLogic.Finish(s, now, cfg), true);
                Add(Theme.Glyphs.Next, "Skip this break", (s, now, cfg) => BreakReminderLogic.Skip(s, now, cfg));
                break;
            case BreakReminderLogic.Phase.Paused:
                Add(Theme.Glyphs.Play, "Resume", (s, now, cfg) => BreakReminderLogic.Resume(s, now, cfg), true);
                break;
            default:
                Add(Theme.Glyphs.Pause, "Pause the reminders", (s, now, cfg) => BreakReminderLogic.Pause(s, now, cfg));
                Add(Theme.Glyphs.Timer, $"Snooze {ctx.GetInt("snooze")} minutes", (s, now, cfg) => BreakReminderLogic.Snooze(s, now, ctx.GetInt("snooze")));
                Add(Theme.Glyphs.Play, "Take a break now", (s, now, cfg) => BreakReminderLogic.BreakNow(s, now, cfg), true);
                break;
        }
        return row;
    }

    public IEnumerable<WidgetAction> Actions(WidgetContext ctx)
    {
        yield return new("Take a break now", Theme.Glyphs.Play, () => Apply(ctx, (s, now, cfg) => BreakReminderLogic.BreakNow(s, now, cfg)));
        bool paused = Load(ctx, DateTime.Now).PausedLeftSeconds is not null;
        yield return paused
            ? new("Resume reminders", Theme.Glyphs.Play, () => Apply(ctx, (s, now, cfg) => BreakReminderLogic.Resume(s, now, cfg)))
            : new("Pause reminders", Theme.Glyphs.Pause, () => Apply(ctx, (s, now, cfg) => BreakReminderLogic.Pause(s, now, cfg)));
        yield return new($"Snooze {ctx.GetInt("snooze")} minutes", Theme.Glyphs.Timer, () => Apply(ctx, (s, now, cfg) => BreakReminderLogic.Snooze(s, now, ctx.GetInt("snooze"))));
        yield return new("Reset today's count", Theme.Glyphs.Reset, () => Apply(ctx, (s, now, cfg) => s with { Taken = 0 }));
    }
}
