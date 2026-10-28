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
/// Water tracker: a daily goal in ml, fl oz or cups, add buttons in several sizes, a ring, glass or bar, the last
/// seven days, an undo for misclicks and optional reminders during active hours. Amounts are stored in millilitres
/// ("data.log"); the day changes at midnight because the log is keyed by date. Pure logic: HydrationLogic.cs.
/// </summary>
internal sealed class HydrationWidget : IWidget
{
    public string Kind => "hydration";
    public string Name => "Hydration";
    public string Description => "Track water against a daily goal, with quick add buttons and reminders";
    public string Section => "Health";
    public string Glyph => "";
    public string Accent => "#2dd4bf";
    public (double Width, double Height) DefaultSize => (276, 228);
    public (double Width, double Height) MinimumSize => (180, 132);

    public IReadOnlyList<OptionSpec> Options { get; } =
    [
        OptionSpec.Choice("unit", "Unit", "ml", ["ml", "oz", "cups"], ["Millilitres", "Fluid ounces (US)", "Cups (US)"], "Goal", "1 fl oz = 29.6 ml, 1 cup = 237 ml. Amounts below are in this unit."),
        OptionSpec.Number("goal", "Daily goal", 2000, 1, 20000, "Goal", "For example 2000 ml, 64 oz or 8 cups. A number too big for the unit is read as ml."),
        OptionSpec.Number("cup", "Cup size", 250, 1, 5000, "Goal", "For example 250 ml, 8 oz or 1 cup."),
        OptionSpec.Text("sizes", "Add buttons (multiples of a cup)", "0.5, 1, 2", "Goal", "Up to six, e.g. 0.5, 1, 2 gives half a cup, a cup and two cups."),
        OptionSpec.Choice("style", "Style", "ring", ["ring", "glass", "bar"], ["Progress ring", "Glass", "Bar"], "Layout"),
        OptionSpec.Toggle("history", "Show the last 7 days", true, "Layout"),
        OptionSpec.Toggle("remaining", "Show what's left to drink", true, "Layout"),
        OptionSpec.Toggle("undo", "Show an undo button", true, "Layout"),
        OptionSpec.Toggle("remind", "Remind me to drink", false, "Reminders"),
        OptionSpec.Number("remindEvery", "Remind every (minutes)", 60, 10, 480, "Reminders", "The timer restarts each time you add water. Reminders stop once the goal is met."),
        new OptionSpec("activeFrom", "Active from", OptionType.Time, "08:00") { Group = "Reminders" },
        new OptionSpec("activeTo", "Active until", OptionType.Time, "21:00") { Group = "Reminders" },
        OptionSpec.Toggle("sound", "Play a sound with reminders", false, "Reminders"),
        OptionSpec.Toggle("notifyGoal", "Notify when the goal is reached", true, "Reminders"),
        OptionSpec.Color("waterColor", "Water color", "", "Colors", "Empty = the accent color."),
        OptionSpec.Color("goalColor", "Goal reached color", "#22c55e", "Colors"),
    ];

    // ---------- Samples ----------

    private static void Fill(LauncherItem item, params double[] daysAgoNewestFirst)
    {
        var log = new Dictionary<string, double>();
        for (int ago = 0; ago < daysAgoNewestFirst.Length; ago++) HydrationLogic.Add(log, DateTime.Today.AddDays(-ago), daysAgoNewestFirst[ago]);
        item.Options["data.log"] = HydrationLogic.FormatLog(log, DateTime.Today);
    }

    public IEnumerable<Action<LauncherItem>> Samples =>
    [
        item => Fill(item, 1250, 2100, 1800, 2400, 900, 2000, 1500),
        item => { item.Options["style"] = "glass"; item.Options["unit"] = "oz"; item.Options["goal"] = "64"; item.Options["cup"] = "8"; item.Options["sizes"] = "0.5, 1, 1.5, 2"; Fill(item, 1130, 1900, 2050, 1200, 2300, 1000, 1700); item.Width = 324; item.Height = 276; },
        item => { item.Options["style"] = "bar"; item.Options["unit"] = "cups"; item.Options["goal"] = "8"; item.Options["cup"] = "1"; item.Options["sizes"] = "0.5, 1, 2"; Fill(item, 2100, 1700, 800, 1900, 2000, 1300, 2200); item.Options["data.lastAdd"] = "236"; item.Options["data.lastDay"] = HealthLogic.DayKey(DateTime.Today); },
    ];

    // ---------- Settings ----------

    private static string Unit(WidgetContext ctx) => ctx.Get("unit") is "oz" or "cups" ? ctx.Get("unit") : "ml";
    private static double GoalMl(WidgetContext ctx) => HydrationLogic.EnteredToMl(ctx.GetNumber("goal"), Unit(ctx));
    private static double CupMl(WidgetContext ctx) => Math.Max(1, HydrationLogic.EnteredToMl(ctx.GetNumber("cup"), Unit(ctx)));
    private static TimeSpan Interval(WidgetContext ctx) => TimeSpan.FromMinutes(ctx.GetNumber("remindEvery"));
    private static TimeSpan From(WidgetContext ctx) => HealthLogic.ParseTime(ctx.Get("activeFrom"), TimeSpan.FromHours(8));
    private static TimeSpan To(WidgetContext ctx) => HealthLogic.ParseTime(ctx.Get("activeTo"), TimeSpan.FromHours(21));

    // ---------- Actions ----------

    private static void AddWater(WidgetContext ctx, double ml)
    {
        if (ctx.IsEditing || ml <= 0) return;
        var today = DateTime.Today;
        var log = HydrationLogic.ParseLog(ctx.Get("data.log"));
        double before = HydrationLogic.Total(log, today);
        HydrationLogic.Add(log, today, ml);
        double after = HydrationLogic.Total(log, today);
        var o = ctx.Item.Options;
        o["data.log"] = HydrationLogic.FormatLog(log, today, ctx.HistoryDays(HistoryKeys.Hydration));
        o["data.lastAdd"] = ml.ToString("0.##", CultureInfo.InvariantCulture);
        o["data.lastDay"] = HealthLogic.DayKey(today);
        if (ctx.GetBool("remind")) o["data.remindAt"] = HealthLogic.FormatStamp(HealthLogic.ScheduleAfter(DateTime.Now, Interval(ctx), true, From(ctx), To(ctx), false));
        if (HydrationLogic.ReachedGoal(before, after, GoalMl(ctx), ctx.Get("data.goalDay"), today))
        {
            o["data.goalDay"] = HealthLogic.DayKey(today);
            if (ctx.GetBool("notifyGoal")) ctx.Notify("Hydration", $"Goal reached: {HydrationLogic.Format(after, Unit(ctx))} today. Nice.");
        }
        ctx.Changed(true);
    }

    private static void Undo(WidgetContext ctx)
    {
        if (ctx.IsEditing) return;
        var today = DateTime.Today;
        if (!double.TryParse(ctx.Get("data.lastAdd"), NumberStyles.Float, CultureInfo.InvariantCulture, out double ml) || ml <= 0 || ctx.Get("data.lastDay") != HealthLogic.DayKey(today)) return;
        var log = HydrationLogic.ParseLog(ctx.Get("data.log"));
        HydrationLogic.Add(log, today, -ml);
        ctx.Item.Options["data.log"] = HydrationLogic.FormatLog(log, today, ctx.HistoryDays(HistoryKeys.Hydration));
        ctx.Item.Options["data.lastAdd"] = "";
        ctx.Changed(true);
    }

    private static bool CanUndo(WidgetContext ctx)
        => double.TryParse(ctx.Get("data.lastAdd"), NumberStyles.Float, CultureInfo.InvariantCulture, out double ml) && ml > 0 && ctx.Get("data.lastDay") == HealthLogic.DayKey(DateTime.Today);

    /// <summary>Reminder check, once a minute while the widget is on screen. The first copy to run saves the next time, so copies don't repeat it.</summary>
    private static void CheckReminder(WidgetContext ctx, DateTime now)
    {
        if (!ctx.GetBool("remind")) return;
        var log = HydrationLogic.ParseLog(ctx.Get("data.log"));
        double total = HydrationLogic.Total(log, now), goal = GoalMl(ctx);
        var (due, fire) = HydrationLogic.StepReminder(HealthLogic.ParseStamp(ctx.Get("data.remindAt")), now, Interval(ctx), true, From(ctx), To(ctx), total >= goal);
        string next = due is { } d ? HealthLogic.FormatStamp(d) : "";
        if (next != ctx.Get("data.remindAt")) { ctx.Item.Options["data.remindAt"] = next; ctx.Changed(false); }
        if (!fire) return;
        ctx.Notify("Time for water", HydrationLogic.Reminder(goal - total, Unit(ctx)));
        if (ctx.GetBool("sound")) System.Media.SystemSounds.Asterisk.Play();
    }

    public IEnumerable<WidgetAction> Actions(WidgetContext ctx)
    {
        string unit = Unit(ctx);
        yield return new($"Add a cup ({HydrationLogic.Format(CupMl(ctx), unit)})", Theme.Glyphs.Add, () => AddWater(ctx, CupMl(ctx)));
        if (CanUndo(ctx)) yield return new("Undo the last drink", Theme.Glyphs.Back, () => Undo(ctx));
        yield return new("Reset today", Theme.Glyphs.Reset, () =>
        {
            if (!ctx.Confirm("Set today's water back to zero?", "Hydration")) return;
            var log = HydrationLogic.ParseLog(ctx.Get("data.log"));
            log.Remove(HealthLogic.DayKey(DateTime.Today));
            ctx.Item.Options["data.log"] = HydrationLogic.FormatLog(log, DateTime.Today, ctx.HistoryDays(HistoryKeys.Hydration));
            ctx.Item.Options["data.lastAdd"] = "";
            ctx.Changed(true);
        });
    }

    // ---------- Drawing ----------

    public FrameworkElement Build(WidgetContext ctx)
    {
        var now = DateTime.Now;
        string unit = Unit(ctx);
        if (!ctx.IsSnapshot && ctx.GetBool("remind"))
        {
            CheckReminder(ctx, now);
            ctx.Tick(() => false, at => CheckReminder(ctx, at.LocalDateTime));
        }

        var log = HydrationLogic.ParseLog(ctx.Get("data.log"));
        double goal = GoalMl(ctx), total = HydrationLogic.Total(log, now), fraction = HydrationLogic.Fraction(total, goal);
        bool met = total >= goal;
        var water = ctx.GetColor("waterColor");
        var fill = met ? ctx.GetColor("goalColor", Theme.Olive) : water;
        string style = ctx.Get("style");

        var badge = ctx.Width >= 228 ? WidgetKit.Pill(ctx, $"{(int)Math.Round(fraction * 100)}%", met ? "#22c55e" : null) : null;
        var (card, body) = WidgetKit.Frame(ctx, badge);
        double bodyHeight = ctx.Height - ctx.GetNumber("_padding") * 2 - (ctx.Item.ShowHeader ? 34 : 0);
        double bodyWidth = ctx.Width - ctx.GetNumber("_padding") * 2 - 2;
        bool compact = bodyHeight < 112;
        bool history = ctx.GetBool("history") && !compact && bodyHeight >= 168;

        var sizes = HydrationLogic.ParseMultipliers(ctx.Get("sizes"));
        if (sizes.Count == 0) sizes = [1];
        var chips = Chips(ctx, sizes, unit, compact ? (ctx.Width >= 228 ? 3 : 1) : 6, compact ? 26 : 30);

        if (history)
        {
            var strip = History(ctx, log, now, goal, water, ctx.GetColor("goalColor", Theme.Olive));
            DockPanel.SetDock(strip, Dock.Bottom);
            body.Children.Add(strip);
        }

        double size = compact ? Math.Clamp(bodyHeight - 2, 44, 76) : style == "glass" ? Math.Clamp(bodyHeight - (history ? 92 : 50), 70, 104) : Math.Clamp(bodyHeight - (history ? 92 : 50), 76, 112);
        FrameworkElement texts = Texts(ctx, total, goal, fraction, met, unit, compact, fill);

        if (style == "bar" && !compact)
        {
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(texts);
            var bar = WidgetKit.Bar(fraction, fill, 10);
            bar.Margin = new Thickness(0, 8, 0, 12);
            stack.Children.Add(bar);
            stack.Children.Add(chips);
            body.Children.Add(stack);
            return card;
        }

        FrameworkElement graphic = style == "glass" ? Glass(size, fraction, fill, ctx) : Ring(ctx, size, fraction, fill, total, unit);
        graphic.VerticalAlignment = VerticalAlignment.Center;
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.Children.Add(graphic);
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(compact ? 10 : 14, 0, 0, 0) };
        right.Children.Add(texts);
        if (compact) { chips.Margin = new Thickness(0, 6, 0, 0); right.Children.Add(chips); }
        Grid.SetColumn(right, 1);
        top.Children.Add(right);
        if (compact) { body.Children.Add(top); return card; }
        var column = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        column.Children.Add(top);
        chips.Margin = new Thickness(0, 10, 0, 0);
        column.Children.Add(chips);
        body.Children.Add(column);
        return card;
    }

    private static FrameworkElement Texts(WidgetContext ctx, double total, double goal, double fraction, bool met, string unit, bool compact, Brush fill)
    {
        var stack = new StackPanel();
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(WidgetKit.Text(ctx, HydrationLogic.Format(total, unit, withUnit: false), compact ? 18 : 24, bold: true));
        var unitText = WidgetKit.Muted(ctx, " " + HydrationLogic.UnitName(unit), compact ? 11 : 12.5);
        unitText.VerticalAlignment = VerticalAlignment.Bottom;
        unitText.Margin = new Thickness(0, 0, 0, compact ? 2 : 3);
        line.Children.Add(unitText);
        stack.Children.Add(line);
        string sub = met ? "Goal reached" : ctx.GetBool("remaining") ? HydrationLogic.Format(goal - total, unit) + " to go" : "of " + HydrationLogic.Format(goal, unit);
        stack.Children.Add(WidgetKit.Text(ctx, sub, compact ? 10.5 : 11.5, met ? fill : Theme.Muted, met));
        if (!compact && ctx.GetBool("remaining") && !met) stack.Children.Add(WidgetKit.Text(ctx, "of " + HydrationLogic.Format(goal, unit), 10.5, Theme.Dim));
        return stack;
    }

    private static FrameworkElement Ring(WidgetContext ctx, double size, double fraction, Brush fill, double total, string unit)
    {
        var drop = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M12,2 C12,2 5,10 5,15 A7,7 0 0 0 19,15 C19,10 12,2 12,2 Z"),
            Fill = fraction >= 1 ? fill : Theme.Dim,
            Stretch = Stretch.Uniform,
            Width = size * 0.3,
            Height = size * 0.3,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        return WidgetKit.Ring(fraction, fill, size, Math.Max(6, size * 0.1), drop);
    }

    /// <summary>A drinking glass filled to <paramref name="fraction"/>: one shape painted with a hard-edged gradient.</summary>
    private static FrameworkElement Glass(double height, double fraction, Brush fill, WidgetContext ctx)
    {
        var brush = (fill as SolidColorBrush)?.Color ?? Colors.DeepSkyBlue;
        double level = 1 - Math.Clamp(fraction, 0, 1);
        var paint = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        var empty = Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF);
        paint.GradientStops.Add(new GradientStop(empty, 0));
        paint.GradientStops.Add(new GradientStop(empty, level));
        paint.GradientStops.Add(new GradientStop(brush, level));
        paint.GradientStops.Add(new GradientStop(brush, 1));
        var shape = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M4,2 L56,2 L49,76 Q48.5,80 44.5,80 L15.5,80 Q11.5,80 11,76 Z"),
            Fill = paint,
            Stroke = Theme.LineStrong,
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round,
        };
        var canvas = new Canvas { Width = 60, Height = 82 };
        canvas.Children.Add(shape);
        return new Viewbox { Height = height, Width = height * 60 / 82, Child = canvas };
    }

    private static Border Chip(WidgetContext ctx, string text, double height, Action click, string tip)
    {
        var label = WidgetKit.Text(ctx, text, 12, bold: true);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        var chip = new Border { Height = height, MinWidth = 40, Padding = new Thickness(10, 0, 10, 0), CornerRadius = new CornerRadius(8), Background = Theme.Surface3, Child = label, Margin = new Thickness(0, 0, 6, 0) };
        chip.MouseEnter += (_, _) => chip.Background = Theme.SurfaceHover;
        chip.MouseLeave += (_, _) => chip.Background = Theme.Surface3;
        return WidgetKit.Clickable(chip, click, tip);
    }

    private static StackPanel Chips(WidgetContext ctx, List<double> multiples, string unit, int max, double height)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        double cupMl = CupMl(ctx);
        foreach (double m in multiples.Take(max))
        {
            double ml = cupMl * m;
            double inUnit = HydrationLogic.FromMl(ml, unit);
            row.Children.Add(Chip(ctx, HydrationLogic.ButtonLabel(inUnit, unit), height, () => AddWater(ctx, ml), $"Add {HydrationLogic.Format(ml, unit)}"));
        }
        if (ctx.GetBool("undo") && CanUndo(ctx))
        {
            var undo = WidgetKit.IconButton(Theme.Glyphs.Back, "Undo the last drink", () => Undo(ctx), height);
            row.Children.Add(undo);
        }
        return row;
    }

    private static FrameworkElement History(WidgetContext ctx, Dictionary<string, double> log, DateTime now, double goal, Brush water, Brush goalBrush)
    {
        var days = HydrationLogic.Recent(log, now, 7);
        var grid = new UniformGrid { Rows = 1, Columns = 7, Margin = new Thickness(0, 8, 0, 0), Height = 40 };
        foreach (var (day, ml) in days)
        {
            bool today = day.Date == now.Date;
            var cell = new Grid { Margin = new Thickness(3, 0, 3, 0) };
            cell.RowDefinitions.Add(new RowDefinition());
            cell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var track = new Border { Background = Theme.Surface3, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 0, 3) };
            var inner = new Grid();
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Math.Max(0.0001, 1 - HydrationLogic.Fraction(ml, goal)), GridUnitType.Star) });
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Math.Max(0.0001, HydrationLogic.Fraction(ml, goal)), GridUnitType.Star) });
            var bar = new Border { CornerRadius = new CornerRadius(3), Background = ml >= goal ? goalBrush : water, Opacity = today ? 1 : 0.6 };
            Grid.SetRow(bar, 1);
            inner.Children.Add(bar);
            track.Child = inner;
            cell.Children.Add(track);
            var letter = WidgetKit.Text(ctx, day.ToString("ddd", CultureInfo.CurrentCulture)[..1], 9, today ? ctx.TextBrush : Theme.Dim, today);
            letter.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetRow(letter, 1);
            cell.Children.Add(letter);
            cell.ToolTip = $"{day:dddd}: {HydrationLogic.Format(ml, Unit(ctx))}";
            grid.Children.Add(cell);
        }
        return grid;
    }
}
