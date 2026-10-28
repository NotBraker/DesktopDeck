using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesktopDeck.Models;

namespace DesktopDeck.Widgets;

/// <summary>
/// Master volume and mute of the default output device (Core Audio, see CoreAudio.cs). The level is read once a second
/// while the widget is on screen (off the UI thread, redrawing only when something changed); clicks, the − / + buttons
/// and the mouse wheel show the new level straight away and write it in the background. Nothing touches the audio
/// device in snapshot mode.
/// </summary>
internal sealed class VolumeWidget : IWidget
{
    public string Kind => "volume";
    public string Name => "Volume";
    public string Description => "Master volume and mute: click the bar, use − / +";
    public string Section => "Tools";
    public string Glyph => "";
    public string Accent => "#73bda7";
    public (double Width, double Height) DefaultSize => (276, 180);
    public (double Width, double Height) MinimumSize => (180, 132);

    public IReadOnlyList<OptionSpec> Options { get; } =
    [
        OptionSpec.Toggle("percent", "Show percentage", true, "What to show"),
        OptionSpec.Toggle("device", "Show device name", true, "What to show", "Hidden automatically when the widget is too short."),
        OptionSpec.Choice("icon", "Speaker icon", "level", ["level", "fixed", "none"], ["Follows the volume", "Always a speaker", "None"], "What to show", "Click the icon to mute or unmute."),
        OptionSpec.Choice("orientation", "Bar direction", "horizontal", ["horizontal", "vertical"], ["Horizontal", "Vertical"], "Layout"),
        OptionSpec.Toggle("buttons", "Show − / + buttons", true, "Layout"),
        OptionSpec.Number("thickness", "Bar thickness", 14, 6, 36, "Layout"),
        OptionSpec.Number("step", "Step size (%)", 5, 1, 25, "Behavior"),
        OptionSpec.Number("maxLevel", "Volume limit (%)", 100, 10, 100, "Behavior", "The bar, buttons and wheel never go above this."),
        OptionSpec.Toggle("unmute", "Unmute when changing the volume", true, "Behavior"),
        OptionSpec.Toggle("wheel", "Mouse wheel changes the volume", true, "Behavior"),
        OptionSpec.Color("levelColor", "Volume color", "", "Colors", "Empty = accent color."),
        OptionSpec.Color("muteColor", "Muted color", "", "Colors", "Empty = grey."),
        OptionSpec.Color("trackColor", "Bar background", "", "Colors", "Empty = theme."),
    ];

    public IEnumerable<Action<LauncherItem>> Samples =>
    [
        item => item.Options["data.sample"] = "47",
        item => { item.Options["data.sample"] = "35"; item.Options["data.muted"] = "true"; item.Options["icon"] = "fixed"; item.Options["thickness"] = "20"; },
        item => { item.Options["data.sample"] = "72"; item.Options["orientation"] = "vertical"; item.Options["levelColor"] = "#f59e0b"; item.Width = 276; item.Height = 276; },
    ];

    // ---------- Reading and writing the device ----------

    private static string ReadKey(LauncherItem item) => "volume.read:" + item.Id;
    private static string BusyKey(LauncherItem item) => "volume.busy:" + item.Id;

    private static CoreAudio.Reading? Latest(WidgetContext ctx) =>
        WidgetContext.Cache.TryGetValue(ReadKey(ctx.Item), out var value) && value is CoreAudio.Reading reading ? reading : null;

    /// <summary>Reads the device in the background; redraws only when the level, mute or device changed.</summary>
    private static void Refresh(WidgetContext ctx)
    {
        if (ctx.IsSnapshot) return;
        var item = ctx.Item;
        bool withName = ctx.GetBool("device");
        ctx.RunOnce(ReadKey(item), async () =>
        {
            var read = await Task.Run(() => CoreAudio.Read(withName));
            // A write is in flight: this reading may predate it, and the screen already shows the new level.
            if (WidgetContext.Cache.ContainsKey(BusyKey(item))) return;
            var old = Latest(ctx);
            WidgetContext.Cache[ReadKey(item)] = read;
            if (old is null || old.Value != read) ctx.Rebuild();
        });
    }

    /// <summary>Shows the new state at once, writes it in the background, then reads the device again.</summary>
    private static void Apply(WidgetContext ctx, double? level, bool? mute)
    {
        if (ctx.IsSnapshot || ctx.IsEditing) return;
        var item = ctx.Item;
        var now = Latest(ctx) ?? new CoreAudio.Reading(true, 0, false, null);
        float? newLevel = level is { } l ? (float)Math.Clamp(l, 0, 1) : null;
        WidgetContext.Cache[ReadKey(item)] = now with { Level = newLevel ?? now.Level, Muted = mute ?? now.Muted };
        WidgetContext.Cache[BusyKey(item)] = true;
        ctx.Rebuild();
        _ = Write();

        async Task Write()
        {
            try
            {
                bool ok = await Task.Run(() => CoreAudio.Write(newLevel, mute));
                if (!ok) ctx.Status("No audio output is available.");
            }
            catch (Exception ex) { App.Log(ex); }
            finally
            {
                WidgetContext.Cache.TryRemove(BusyKey(item), out _);
                Refresh(ctx);
            }
        }
    }

    private static void Nudge(WidgetContext ctx, int direction)
    {
        var now = Latest(ctx);
        if (now is null || !now.Value.Found) return;
        double max = ctx.GetNumber("maxLevel") / 100;
        bool unmute = ctx.GetBool("unmute") && now.Value.Muted;
        double next = VolumeLogic.Step(now.Value.Level, ctx.GetNumber("step"), direction, max);
        // Already at the limit: do nothing (but a + press still unmutes).
        Apply(ctx, next, unmute ? false : null);
    }

    private static void SetFromBar(WidgetContext ctx, double level)
    {
        var now = Latest(ctx);
        if (now is null || !now.Value.Found) return;
        Apply(ctx, level, ctx.GetBool("unmute") && now.Value.Muted ? false : null);
    }

    // ---------- Drawing ----------

    public FrameworkElement Build(WidgetContext ctx)
    {
        var read = ctx.IsSnapshot
            ? new CoreAudio.Reading(true, (float)(double.TryParse(ctx.Get("data.sample"), out double p) ? p / 100 : 0.47), ctx.Get("data.muted") == "true", "Speakers (Studio audio)")
            : Latest(ctx) ?? new CoreAudio.Reading(false, 0, false, null);
        bool loading = !ctx.IsSnapshot && Latest(ctx) is null;
        if (loading) Refresh(ctx);
        ctx.Tick(() => true, _ => Refresh(ctx));

        var (card, body) = WidgetKit.Frame(ctx);
        double pad = ctx.GetNumber("_padding");
        double available = ctx.Height - pad * 2 - (ctx.Item.ShowHeader ? 34 : 0);
        double max = ctx.GetNumber("maxLevel") / 100;
        bool muted = read.Muted;
        Brush levelBrush = muted ? ctx.GetColor("muteColor", Theme.Dim) : ctx.GetColor("levelColor");
        Brush trackBrush = ctx.GetColor("trackColor", Theme.Surface3);
        double thickness = ctx.GetNumber("thickness");
        bool vertical = ctx.Get("orientation") == "vertical";
        string status = loading ? "…" : !read.Found ? "No output" : muted ? "Muted" : VolumeLogic.Percent(read.Level);
        var iconBrush = !read.Found ? Theme.Muted : muted ? ctx.GetColor("muteColor", Theme.Muted) : ctx.GetColor("levelColor");

        FrameworkElement? iconButton = null;
        if (ctx.Get("icon") != "none")
        {
            double diameter = Math.Clamp(ctx.Font(36), 30, 52);
            var glyph = WidgetKit.Icon(VolumeLogic.Glyph(read.Level, muted || !read.Found, ctx.Get("icon") == "level"), diameter * 0.5, iconBrush);
            var ring = new Border
            {
                Width = diameter, Height = diameter, CornerRadius = new CornerRadius(diameter / 2), Background = Theme.Surface3,
                Child = glyph, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            };
            iconButton = WidgetKit.Clickable(ring, () => { if (read.Found) Apply(ctx, null, !muted); }, muted ? "Unmute" : "Mute");
        }
        TextBlock? percent = null;
        if (ctx.GetBool("percent"))
        {
            percent = WidgetKit.Text(ctx, status, vertical ? 22 : 26, muted ? Theme.Muted : ctx.TextBrush, bold: true);
            percent.VerticalAlignment = VerticalAlignment.Center;
        }
        FrameworkElement? minus = null, plus = null;
        if (ctx.GetBool("buttons"))
        {
            minus = WidgetKit.IconButton("", "Quieter", () => Nudge(ctx, -1), 28);
            plus = WidgetKit.IconButton("", "Louder", () => Nudge(ctx, 1), 28);
        }
        bool showDevice = ctx.GetBool("device") && available >= 100 * Math.Max(1, ctx.TextScale * 0.8);
        TextBlock? device = null;
        if (showDevice)
        {
            device = WidgetKit.Muted(ctx, loading ? "" : read.DeviceName ?? (read.Found ? "Default audio output" : "No audio device found"), 10.5);
            device.ToolTip = device.Text;
        }

        var content = new Grid { VerticalAlignment = VerticalAlignment.Center };
        if (!vertical)
        {
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            if (iconButton is not null) { iconButton.Margin = new Thickness(0, 0, 10, 0); top.Children.Add(iconButton); }
            if (percent is not null) top.Children.Add(percent);
            if (top.Children.Count > 0) { top.Margin = new Thickness(0, 0, 0, 6); content.Children.Add(top); }
            var barRow = new Grid { Height = Math.Max(28, thickness + 12) };
            barRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            barRow.ColumnDefinitions.Add(new ColumnDefinition());
            barRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (minus is not null) { minus.VerticalAlignment = VerticalAlignment.Center; minus.Margin = new Thickness(0, 0, 8, 0); barRow.Children.Add(minus); }
            var bar = BarFor(ctx, read, levelBrush, trackBrush, thickness, vertical: false, max);
            Grid.SetColumn(bar, 1);
            barRow.Children.Add(bar);
            if (plus is not null) { plus.VerticalAlignment = VerticalAlignment.Center; plus.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(plus, 2); barRow.Children.Add(plus); }
            Grid.SetRow(barRow, 1);
            content.Children.Add(barRow);
            if (device is not null) { device.Margin = new Thickness(2, 4, 0, 0); Grid.SetRow(device, 2); content.Children.Add(device); }
        }
        else
        {
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition());
            double barHeight = Math.Max(48, available - 4);
            var bar = BarFor(ctx, read, levelBrush, trackBrush, thickness, vertical: true, max);
            bar.Height = barHeight;
            bar.Margin = new Thickness(0, 0, 14, 0);
            content.VerticalAlignment = VerticalAlignment.Stretch;
            content.Children.Add(bar);
            var side = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            if (iconButton is not null) { iconButton.Margin = new Thickness(0, 0, 0, 6); side.Children.Add(iconButton); }
            if (percent is not null) { percent.Margin = new Thickness(0, 0, 0, 8); side.Children.Add(percent); }
            if (minus is not null && plus is not null)
            {
                var pair = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
                minus.Margin = new Thickness(0, 0, 6, 0);
                pair.Children.Add(minus);
                pair.Children.Add(plus);
                side.Children.Add(pair);
            }
            if (device is not null) { device.TextWrapping = TextWrapping.Wrap; device.TextTrimming = TextTrimming.CharacterEllipsis; device.MaxHeight = device.FontSize * 3.4; side.Children.Add(device); }
            Grid.SetColumn(side, 1);
            content.Children.Add(side);
        }
        body.Children.Add(content);

        if (ctx.GetBool("wheel"))
        {
            card.MouseWheel += (_, e) =>
            {
                e.Handled = true;
                if (e.Delta != 0) Nudge(ctx, e.Delta > 0 ? 1 : -1);
            };
        }
        card.ToolTip = null;
        return card;
    }

    /// <summary>The clickable bar: a track with a rounded fill, in a taller invisible hit area.</summary>
    private static Grid BarFor(WidgetContext ctx, CoreAudio.Reading read, Brush fill, Brush track, double thickness, bool vertical, double max)
    {
        double level = read.Found ? Math.Clamp(read.Level, 0, 1) : 0;
        var hit = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "Click to set the volume" };
        if (vertical)
        {
            hit.Width = Math.Max(28, thickness + 12);
            var column = new Grid { Width = thickness, HorizontalAlignment = HorizontalAlignment.Center };
            column.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Math.Max(0.0001, 1 - level), GridUnitType.Star) });
            column.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Math.Max(0.0001, level), GridUnitType.Star) });
            var back = new Border { CornerRadius = new CornerRadius(thickness / 2), Background = track };
            Grid.SetRowSpan(back, 2);
            column.Children.Add(back);
            var front = new Border { CornerRadius = new CornerRadius(thickness / 2), Background = fill };
            Grid.SetRow(front, 1);
            column.Children.Add(front);
            hit.Children.Add(column);
        }
        else
        {
            var bar = WidgetKit.Bar(level, fill, thickness, track);
            bar.VerticalAlignment = VerticalAlignment.Center;
            hit.Children.Add(bar);
        }
        hit.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            var at = e.GetPosition(hit);
            // The visible bar is inset a few pixels in the hit area; map the click onto the bar itself.
            double length = vertical ? hit.ActualHeight : hit.ActualWidth;
            double position = vertical ? at.Y : at.X;
            SetFromBar(ctx, VolumeLogic.FromPosition(position, length, vertical, max));
        };
        return hit;
    }

    public IEnumerable<WidgetAction> Actions(WidgetContext ctx) =>
    [
        new("Mute / unmute", "", () => { if (Latest(ctx) is { Found: true } now) Apply(ctx, null, !now.Muted); }),
        new("Louder", "", () => Nudge(ctx, 1)),
        new("Quieter", "", () => Nudge(ctx, -1)),
        new("Sound settings", "", () => ctx.Open("ms-settings:sound")),
    ];
}
