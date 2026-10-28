using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using DesktopDeck.Models;

namespace DesktopDeck.Widgets;

/// <summary>
/// A stopwatch with laps. Its state (start time, time gathered so far, laps) lives in Item.Options under "data.*",
/// so it keeps counting across restarts, like the pomodoro timer. Logic: StopwatchLogic.cs.
/// </summary>
internal sealed class StopwatchWidget : IWidget
{
    public string Kind => "stopwatch";
    public string Name => "Stopwatch";
    public string Description => "Start, pause, laps — keeps running across restarts";
    public string Section => "Time";
    public string Glyph => "";
    public string Accent => "#22c55e";
    public (double Width, double Height) DefaultSize => (276, 276);
    public (double Width, double Height) MinimumSize => (180, 132);

    public IReadOnlyList<OptionSpec> Options { get; } =
    [
        OptionSpec.Toggle("showLaps", "Show laps", true, "What to show"),
        OptionSpec.Choice("lapColumns", "Lap times", "both", ["lap", "split", "both"], ["Lap time", "Total (split)", "Both"], "What to show"),
        OptionSpec.Toggle("markBestWorst", "Mark fastest and slowest lap", true, "What to show"),
        OptionSpec.Number("maxLaps", "Laps shown", 20, 1, 99, "What to show", "Older laps are hidden from the list."),
        OptionSpec.Choice("precision", "Precision", "tenths", ["seconds", "tenths", "hundredths"], ["Seconds", "Tenths", "Hundredths"], "Layout",
            "While running, the display refreshes once a second; laps and the paused time are exact."),
        OptionSpec.Toggle("showHours", "Always show hours", false, "Layout"),
        OptionSpec.Toggle("showRing", "Seconds ring around the time", false, "Layout"),
        OptionSpec.Color("timeColor", "Time color", "", "Colors", "Empty = text color."),
        OptionSpec.Color("ringColor", "Ring and button color", "", "Colors", "Empty = accent color."),
        OptionSpec.Color("bestColor", "Fastest lap color", "#22c55e", "Colors"),
        OptionSpec.Color("worstColor", "Slowest lap color", "#ef4444", "Colors"),
        OptionSpec.Toggle("confirmReset", "Ask before resetting", false, "Behavior"),
        OptionSpec.Number("notifyAfter", "Notify after (minutes)", 0, 0, 1440, "Behavior", "0 = never. Shows a notification once the stopwatch passes this time."),
    ];

    public IEnumerable<Action<LauncherItem>> Samples =>
    [
        // Paused with a few laps (deterministic: no start time).
        item => { item.Options[StopwatchLogic.AccumulatedKey] = "754320"; item.Options[StopwatchLogic.LapsKey] = "171200,355900,512340,689010"; },
        item =>
        {
            item.Options[StopwatchLogic.AccumulatedKey] = "41870";
            item.Options["showRing"] = "true";
            item.Options["showLaps"] = "false";
            item.Options["precision"] = "hundredths";
            item.Accent = "#6c63ff";
            item.Width = 228; item.Height = 276;
        },
        item =>
        {
            item.Options[StopwatchLogic.AccumulatedKey] = "5025400";
            item.Options[StopwatchLogic.LapsKey] = "1500200,3120400,4600900";
            item.Options["precision"] = "seconds";
            item.Options["lapColumns"] = "lap";
            item.Options["showHours"] = "true";
            item.Width = 372; item.Height = 228;
        },
    ];

    public IEnumerable<WidgetAction> Actions(WidgetContext ctx)
    {
        var state = StopwatchLogic.Read(ctx.Item.Options);
        yield return new WidgetAction(state.Running ? "Pause" : "Start", state.Running ? Theme.Glyphs.Pause : Theme.Glyphs.Play, () => Toggle(ctx));
        if (state.Running) yield return new WidgetAction("Lap", "", () => Lap(ctx));
        yield return new WidgetAction("Reset", Theme.Glyphs.Reset, () => Reset(ctx));
        if (state.Laps.Count > 0 || StopwatchLogic.Elapsed(state, DateTimeOffset.Now) > 0)
            yield return new WidgetAction("Copy times", "", () => CopyTimes(ctx));
    }

    public FrameworkElement Build(WidgetContext ctx)
    {
        var now = DateTimeOffset.Now;
        var state = StopwatchLogic.Read(ctx.Item.Options);
        string precision = ctx.Get("precision");
        bool forceHours = ctx.GetBool("showHours");
        var accent = ctx.GetColor("ringColor");
        var timeBrush = ctx.GetColor("timeColor", ctx.TextBrush);
        var laps = StopwatchLogic.LapTimes(state);

        Border? badge = laps.Count > 0 && ctx.Width >= 228 ? WidgetKit.Pill(ctx, laps.Count == 1 ? "1 lap" : $"{laps.Count} laps") : null;
        var (card, body) = WidgetKit.Frame(ctx, badge);
        double headerHeight = ctx.Item.ShowHeader || badge is not null ? 34 : 0;
        double pad = ctx.GetNumber("_padding");
        double innerW = Math.Max(60, ctx.Width - 2 * pad - 2);
        double innerH = Math.Max(40, ctx.Height - 2 * pad - headerHeight);
        // Wide and short: time and buttons on the left, laps on the right.
        bool wide = ctx.GetBool("showLaps") && innerW >= 330 && innerW >= innerH * 1.45 && innerH >= 96;
        bool showLaps = ctx.GetBool("showLaps") && (wide || innerH >= 170);
        bool stacked = showLaps && !wide;
        bool ring = ctx.GetBool("showRing") && innerH >= 150;
        double timeW = wide ? innerW * 0.5 - 10 : innerW;

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = stacked ? GridLength.Auto : new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = stacked || wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0) });
        if (wide)
        {
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(timeW) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            layout.ColumnDefinitions.Add(new ColumnDefinition());
        }

        // The time.
        string sample = StopwatchLogic.FormatText(Math.Max(StopwatchLogic.Elapsed(state, now), forceHours ? 3_600_000 : 0), precision, forceHours);
        double buttonsHeight = innerH >= 120 ? 52 : 40;
        double timeSize;
        FrameworkElement timeHost;
        var time = new TextBlock { FontFamily = Theme.UiFont, Foreground = timeBrush, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Typography.SetNumeralAlignment(time, FontNumeralAlignment.Tabular);
        var fraction = new Run { Foreground = Theme.Muted };
        var main = new Run();
        time.Inlines.Add(main);
        time.Inlines.Add(fraction);
        LiveRing? ringView = null;
        if (ring)
        {
            double ringSize = Math.Clamp(Math.Min(timeW, innerH - buttonsHeight - (stacked ? 60 : 0)), 80, 220);
            timeSize = Math.Clamp((ringSize - 30) / (sample.Length * 0.62), 12, 46) * ctx.TextScale;
            ringView = new LiveRing(ringSize, Math.Max(4, ringSize * 0.045), accent);
            ringView.Host.Children.Add(time);
            timeHost = ringView.Host;
            timeHost.Margin = new Thickness(0, 2, 0, 6);
        }
        else
        {
            double room = stacked ? Math.Min(innerH * 0.26, 64) : innerH - buttonsHeight - 8;
            timeSize = Math.Clamp(Math.Min(room * 0.8, timeW / (sample.Length * 0.6)), 16, 84) * Math.Clamp(ctx.TextScale, 0.6, 1.4);
            timeHost = time;
            time.Margin = new Thickness(0, stacked ? 2 : 0, 0, stacked ? 6 : 4);
            if (wide) time.VerticalAlignment = VerticalAlignment.Bottom;
        }
        time.FontSize = timeSize;
        fraction.FontSize = timeSize * 0.6;
        layout.Children.Add(timeHost);

        void ShowTime(DateTimeOffset at)
        {
            long elapsed = StopwatchLogic.Elapsed(state, at);
            var (m, f) = StopwatchLogic.Format(elapsed, precision, forceHours);
            main.Text = m;
            fraction.Text = f;
            ringView?.Set(elapsed % 60_000 / 60_000.0);
        }
        ShowTime(now);

        // Buttons: Reset · Start/Pause · Lap.
        long elapsedNow = StopwatchLogic.Elapsed(state, now);
        double small = innerH >= 120 ? 34 : 28, big = innerH >= 120 ? 48 : 36;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, stacked ? 8 : wide ? 6 : 0) };
        var reset = WidgetKit.IconButton(Theme.Glyphs.Reset, "Reset", () => Reset(ctx), small);
        reset.IsEnabled = !state.Running && (elapsedNow > 0 || laps.Count > 0);
        var play = WidgetKit.Clickable(new Border
        {
            Width = big, Height = big, CornerRadius = new CornerRadius(big / 2), Background = accent, Margin = new Thickness(14, 0, 14, 0),
            Child = WidgetKit.Icon(state.Running ? Theme.Glyphs.Pause : Theme.Glyphs.Play, big * 0.38, Theme.OnPrimary),
        }, () => Toggle(ctx), state.Running ? "Pause" : elapsedNow > 0 ? "Resume" : "Start");
        var lap = WidgetKit.IconButton("", "Lap", () => Lap(ctx), small);
        lap.IsEnabled = state.Running;
        buttons.Children.Add(reset);
        buttons.Children.Add(play);
        buttons.Children.Add(lap);
        Grid.SetRow(buttons, 1);
        layout.Children.Add(buttons);

        // Laps, newest first.
        if (showLaps)
        {
            FrameworkElement lapsView;
            if (laps.Count == 0)
            {
                var hint = WidgetKit.Empty(ctx, state.Running ? "Press the flag to record a lap." : "Laps appear here.");
                hint.HorizontalAlignment = HorizontalAlignment.Center;
                hint.TextAlignment = TextAlignment.Center;
                lapsView = hint;
            }
            else
            {
                var (best, worst) = ctx.GetBool("markBestWorst") ? StopwatchLogic.BestWorst(laps) : (-1, -1);
                string columns = ctx.Get("lapColumns");
                var list = new StackPanel();
                int shown = 0;
                for (int i = laps.Count - 1; i >= 0 && shown < ctx.GetInt("maxLaps"); i--, shown++)
                {
                    Brush color = i == best ? ctx.GetColor("bestColor") : i == worst ? ctx.GetColor("worstColor") : ctx.TextBrush;
                    var row = new Grid();
                    row.ColumnDefinitions.Add(new ColumnDefinition());
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var name = WidgetKit.Muted(ctx, $"Lap {i + 1}", 12);
                    row.Children.Add(name);
                    string lapText = StopwatchLogic.FormatText(laps[i].Lap, precision, forceHours);
                    string splitText = StopwatchLogic.FormatText(laps[i].Split, precision, forceHours);
                    var first = WidgetKit.Text(ctx, columns == "split" ? splitText : lapText, 12.5, columns == "split" ? ctx.TextBrush : color);
                    Typography.SetNumeralAlignment(first, FontNumeralAlignment.Tabular);
                    Grid.SetColumn(first, 1);
                    row.Children.Add(first);
                    if (columns == "both" && innerW - (wide ? timeW + 20 : 0) >= 200)
                    {
                        var second = WidgetKit.Muted(ctx, splitText, 11.5);
                        Typography.SetNumeralAlignment(second, FontNumeralAlignment.Tabular);
                        second.Margin = new Thickness(14, 0, 0, 0);
                        second.MinWidth = ctx.Font(11.5) * splitText.Length * 0.58;
                        second.TextAlignment = TextAlignment.Right;
                        Grid.SetColumn(second, 2);
                        row.Children.Add(second);
                    }
                    var line = WidgetKit.Row(row);
                    line.Padding = new Thickness(6, 3, 6, 3);
                    if (i == best || i == worst) line.ToolTip = i == best ? "Fastest lap" : "Slowest lap";
                    list.Children.Add(line);
                }
                lapsView = WidgetKit.Scroll(list);
                var top = new Border { Height = 1, Background = Theme.Line, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -4, 0, 0) };
                var wrap = new Grid();
                wrap.Children.Add(lapsView);
                wrap.Children.Add(top);
                lapsView = wrap;
            }
            if (wide)
            {
                Grid.SetColumn(lapsView, 2);
                Grid.SetRowSpan(lapsView, 3);
                if (lapsView is Grid framed) framed.Children.RemoveAt(1); // no top rule beside the time
                lapsView.VerticalAlignment = laps.Count == 0 ? VerticalAlignment.Center : VerticalAlignment.Stretch;
            }
            else Grid.SetRow(lapsView, 2);
            layout.Children.Add(lapsView);
        }
        body.Children.Add(layout);

        int notifyAfter = ctx.GetInt("notifyAfter");
        ctx.Tick(() => state.Running, at =>
        {
            if (!state.Running) return;
            ShowTime(at);
            if (notifyAfter > 0 && StopwatchLogic.Elapsed(state, at) >= notifyAfter * 60_000L && ctx.Get(StopwatchLogic.NotifiedKey) != "true")
            {
                ctx.Set(StopwatchLogic.NotifiedKey, "true", rebuild: false);
                ctx.Notify(ctx.Item.Name, $"{StopwatchLogic.FormatText(notifyAfter * 60_000L, "seconds")} have passed.");
            }
        });
        return card;
    }

    private static void Save(WidgetContext ctx, StopwatchLogic.State state)
    {
        StopwatchLogic.Write(ctx.Item.Options, state);
        ctx.Changed();
    }

    private static void Toggle(WidgetContext ctx)
    {
        var state = StopwatchLogic.Read(ctx.Item.Options);
        var now = DateTimeOffset.Now;
        Save(ctx, state.Running ? StopwatchLogic.Pause(state, now) : StopwatchLogic.Start(state, now));
    }

    private static void Lap(WidgetContext ctx)
    {
        var state = StopwatchLogic.Read(ctx.Item.Options);
        if (state.Running) Save(ctx, StopwatchLogic.Lap(state, DateTimeOffset.Now));
    }

    private static void Reset(WidgetContext ctx)
    {
        var state = StopwatchLogic.Read(ctx.Item.Options);
        bool hasData = state.Laps.Count > 0 || StopwatchLogic.Elapsed(state, DateTimeOffset.Now) > 0;
        if (hasData && ctx.GetBool("confirmReset") && !ctx.Confirm("Reset the stopwatch and clear its laps?", ctx.Item.Name)) return;
        ctx.Item.Options.Remove(StopwatchLogic.NotifiedKey);
        Save(ctx, StopwatchLogic.Reset());
    }

    private static void CopyTimes(WidgetContext ctx)
    {
        var state = StopwatchLogic.Read(ctx.Item.Options);
        string precision = ctx.Get("precision") == "seconds" ? "hundredths" : ctx.Get("precision");
        var lines = new List<string> { "Total\t" + StopwatchLogic.FormatText(StopwatchLogic.Elapsed(state, DateTimeOffset.Now), precision) };
        var laps = StopwatchLogic.LapTimes(state);
        for (int i = 0; i < laps.Count; i++)
            lines.Add(string.Create(CultureInfo.CurrentCulture, $"Lap {i + 1}\t{StopwatchLogic.FormatText(laps[i].Lap, precision)}\t{StopwatchLogic.FormatText(laps[i].Split, precision)}"));
        try { Clipboard.SetText(string.Join(Environment.NewLine, lines)); ctx.Status("Stopwatch times copied"); }
        catch (System.Runtime.InteropServices.ExternalException) { ctx.Status("Couldn't reach the clipboard"); }
    }

    /// <summary>A ring whose arc can be moved in place (the shared WidgetKit.Ring is rebuilt each time).</summary>
    private sealed class LiveRing
    {
        private readonly System.Windows.Shapes.Path arc;
        private readonly double size, thickness;
        public Grid Host { get; }

        public LiveRing(double size, double thickness, Brush fill)
        {
            this.size = size;
            this.thickness = thickness;
            Host = new Grid { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center };
            Host.Children.Add(new System.Windows.Shapes.Ellipse { Stroke = Theme.Surface3, StrokeThickness = thickness });
            arc = new System.Windows.Shapes.Path { Stroke = fill, StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            Host.Children.Add(arc);
        }

        public void Set(double fraction)
        {
            fraction = Math.Clamp(fraction, 0, 0.9999);
            if (fraction <= 0.0005) { arc.Data = null; return; }
            double r = (size - thickness) / 2, c = size / 2, angle = fraction * 2 * Math.PI;
            var figure = new PathFigure { StartPoint = new Point(c, c - r), IsClosed = false };
            figure.Segments.Add(new ArcSegment(new Point(c + r * Math.Sin(angle), c - r * Math.Cos(angle)), new Size(r, r), 0, angle > Math.PI, SweepDirection.Clockwise, true));
            arc.Data = new PathGeometry([figure]);
        }
    }
}
