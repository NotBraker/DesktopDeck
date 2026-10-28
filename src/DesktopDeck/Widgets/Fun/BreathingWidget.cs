using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using DesktopDeck.Models;

namespace DesktopDeck.Widgets;

internal sealed class BreathingWidget : IWidget
{
    public string Kind => "breathing";
    public string Name => "Breathing guide";
    public string Description => "A quiet circle for box, 4–7–8 or your own breathing rhythm";
    public string Section => "Fun";
    public string Glyph => "";
    public string Accent => "#5bbf9d";
    public (double Width, double Height) DefaultSize => (276, 324);
    public (double Width, double Height) MinimumSize => (180, 180);
    public IReadOnlyList<OptionSpec> Options { get; } =
    [
        OptionSpec.Choice("pattern", "Pattern", "box", ["box", "4-7-8", "custom"], ["Box breathing", "4–7–8", "Custom"], "Rhythm"),
        OptionSpec.Number("inhale", "Custom: breathe in (seconds)", 4, 1, 30, "Rhythm"),
        OptionSpec.Number("holdIn", "Custom: hold after breathing in", 4, 0, 30, "Rhythm"),
        OptionSpec.Number("exhale", "Custom: breathe out (seconds)", 4, 1, 30, "Rhythm"),
        OptionSpec.Number("holdOut", "Custom: hold after breathing out", 4, 0, 30, "Rhythm"),
        OptionSpec.Number("cycles", "Cycles per session", 4, 1, 30, "Behavior"),
        OptionSpec.Toggle("animate", "Animate the breathing circle", true, "Behavior"),
        OptionSpec.Toggle("counter", "Show cycle counter", true, "What to show"),
        OptionSpec.Toggle("instructions", "Show instructions", true, "What to show"),
        OptionSpec.Toggle("countdown", "Show seconds left in each phase", true, "What to show"),
        OptionSpec.Number("diameter", "Circle diameter", 150, 60, 300, "Layout"),
        OptionSpec.Color("inhaleColor", "Inhale color", "", "Colors"),
        OptionSpec.Color("exhaleColor", "Exhale color", "#60a5fa", "Colors"),
        OptionSpec.Color("holdColor", "Hold color", "#b8a2db", "Colors"),
    ];
    public IEnumerable<Action<LauncherItem>> Samples =>
    [
        _ => { },
        i => { i.Options["pattern"] = "4-7-8"; i.Options["data.sample.seconds"] = "13"; i.Accent = "#60a5fa"; },
        i => { i.Options["pattern"] = "custom"; i.Options["holdIn"] = "0"; i.Options["holdOut"] = "0"; i.Options["instructions"] = "false"; i.Options["data.sample.seconds"] = "2"; },
    ];
    private sealed class Session { public DateTimeOffset? Started; }
    public FrameworkElement Build(WidgetContext ctx)
    {
        int[] pattern = ctx.Get("pattern") switch
        {
            "4-7-8" => [4, 7, 8, 0], "custom" => [ctx.GetInt("inhale"), ctx.GetInt("holdIn"), ctx.GetInt("exhale"), ctx.GetInt("holdOut")], _ => [4, 4, 4, 4],
        };
        // Session is shared by card copies but deliberately not persisted: relaunch always starts quietly.
        var session = ctx.IsSnapshot ? new Session() : (Session)WidgetContext.Cache.GetOrAdd("breathing/" + ctx.Item.Id, _ => new Session());
        bool sampleRunning = ctx.IsSnapshot && ctx.Get("data.sample.seconds").Length > 0;
        var start = WidgetKit.IconButton(session.Started is null ? Theme.Glyphs.Play : Theme.Glyphs.Pause, "Start / stop breathing session", () =>
        {
            session.Started = session.Started is null ? DateTimeOffset.Now : null;
            ctx.Changed();
        });
        var (card, body) = WidgetKit.Frame(ctx, actions: start);
        var counter = WidgetKit.Muted(ctx, "", 11);
        counter.HorizontalAlignment = HorizontalAlignment.Center;
        if (ctx.GetBool("counter")) { DockPanel.SetDock(counter, Dock.Bottom); body.Children.Add(counter); }
        if (ctx.GetBool("instructions") && ctx.Height >= 276)
        {
            var instructions = WidgetKit.Muted(ctx, "Follow the circle at a comfortable pace.", 10.5);
            instructions.TextWrapping = TextWrapping.Wrap;
            instructions.TextAlignment = TextAlignment.Center;
            instructions.Margin = new Thickness(0, 6, 0, 5);
            DockPanel.SetDock(instructions, Dock.Bottom); body.Children.Add(instructions);
        }
        double diameter = Math.Max(40, Math.Min(ctx.GetNumber("diameter"), Math.Min(ctx.Width - ctx.GetNumber("_padding") * 2 - 4, ctx.Height - ctx.GetNumber("_padding") * 2 - (ctx.Item.ShowHeader ? 34 : 0) - (ctx.GetBool("counter") ? 22 : 0) - (ctx.GetBool("instructions") && ctx.Height >= 276 ? 40 : 0))));
        var visual = new Grid { Width = diameter, Height = diameter, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var scale = new ScaleTransform(.65, .65);
        var circle = new Ellipse { StrokeThickness = 2, Fill = Theme.Tint(ctx.Item.Accent, .15), RenderTransformOrigin = new Point(.5, .5), RenderTransform = scale };
        visual.Children.Add(circle);
        var center = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var phaseLabel = WidgetKit.Text(ctx, "Ready", diameter < 100 ? 13 : 17, bold: true);
        phaseLabel.TextAlignment = TextAlignment.Center;
        var countdown = WidgetKit.Muted(ctx, "", 11); countdown.TextAlignment = TextAlignment.Center;
        center.Children.Add(phaseLabel); center.Children.Add(countdown); visual.Children.Add(center);
        body.Children.Add(visual);
        int previousPhase = -1, previousCycle = -1;
        void StopAnimation() { scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); scale.BeginAnimation(ScaleTransform.ScaleYProperty, null); }
        void Show(DateTimeOffset now)
        {
            double elapsed = sampleRunning ? ctx.GetNumber("data.sample.seconds") : session.Started is { } began ? (now - began).TotalSeconds : 0;
            var position = BreathingLogic.At(elapsed, pattern, ctx.GetInt("cycles"));
            bool running = sampleRunning || session.Started is not null;
            if (running && position.Complete)
            {
                session.Started = null; running = false; start.ToolTip = "Start another session";
                start.Content = Theme.Icon(Theme.Glyphs.Play, 12, Theme.Text2);
            }
            phaseLabel.Text = !running ? position.Complete ? "Complete" : "Ready" : position.Phase switch { 0 => "Breathe in", 2 => "Breathe out", _ => "Hold" };
            countdown.Text = running && ctx.GetBool("countdown") ? Math.Ceiling(position.Remaining) + " s" : "";
            counter.Text = running ? $"Cycle {position.Cycle} of {ctx.GetInt("cycles")}" : $"{ctx.GetInt("cycles")} cycles · {ctx.Get("pattern")}";
            var brush = ctx.GetColor(position.Phase switch { 0 => "inhaleColor", 2 => "exhaleColor", _ => "holdColor" });
            circle.Stroke = brush;
            if (!running) { StopAnimation(); scale.ScaleX = scale.ScaleY = .65; previousPhase = -1; return; }
            if (previousPhase == position.Phase && previousCycle == position.Cycle) return;
            previousPhase = position.Phase; previousCycle = position.Cycle;
            StopAnimation();
            double from = .65 + .35 * position.Expansion;
            scale.ScaleX = scale.ScaleY = from;
            if (!ctx.IsSnapshot && ctx.GetBool("animate") && position.Phase is 0 or 2)
            {
                var animation = new DoubleAnimation(from, position.Phase == 0 ? 1 : .65, TimeSpan.FromSeconds(position.Remaining));
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
            }
        }
        Show(DateTimeOffset.Now);
        card.Unloaded += (_, _) => StopAnimation();
        ctx.Tick(() => session.Started is not null, Show);
        return card;
    }
}
