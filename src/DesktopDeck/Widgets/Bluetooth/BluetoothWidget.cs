using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using DesktopDeck.Models;

namespace DesktopDeck.Widgets;

/// <summary>
/// Bluetooth radio switch and the paired devices with connection state, device-type icons and battery levels. The device list
/// comes from <see cref="BluetoothService"/> (Windows device watchers, started only while a Bluetooth widget is on screen);
/// nothing touches Windows in snapshot mode, where the widget draws sample devices from the "data.sample" option instead.
/// Windows has no public API for connecting or disconnecting most devices, so clicking one opens Bluetooth settings.
/// The radio is only ever switched when the user clicks the switch (or the matching menu entry).
/// </summary>
internal sealed class BluetoothWidget : IWidget
{
    public string Kind => "bluetooth";
    public string Name => "Bluetooth";
    public string Description => "Bluetooth on/off switch and your paired devices with battery levels";
    public string Section => "Connected";
    public string Glyph => "";
    public string Accent => "#4f8ff7";
    public (double Width, double Height) DefaultSize => (324, 276);
    public (double Width, double Height) MinimumSize => (180, 132);

    private const string PendingKey = "bluetooth.switching";
    private const string NotifiedKey = "state.notified";
    private const string SettingsUri = "ms-settings:bluetooth";
    private const string AddDeviceUri = "ms-settings:connecteddevices";

    public IReadOnlyList<OptionSpec> Options { get; } =
    [
        OptionSpec.Choice("filter", "Devices to list", "all", ["all", "connected"], ["All paired devices", "Only connected devices"], "What to show"),
        OptionSpec.Toggle("showBattery", "Show battery level", true, "What to show", "Only where Windows reports it (most headphones, mice, keyboards and phones)."),
        OptionSpec.Toggle("batteryDisconnected", "Battery of disconnected devices", false, "What to show", "Shown dimmed; the value may be out of date."),
        OptionSpec.Toggle("showIcons", "Show device type icons", true, "What to show", "Headphones, speaker, mouse, keyboard, phone, controller, watch, computer or generic."),
        OptionSpec.Toggle("showAddress", "Show Bluetooth addresses", false, "What to show"),
        OptionSpec.Toggle("showSwitch", "Show the on/off switch", true, "What to show", "Hidden automatically when the widget is narrow; the right-click menu always has it."),
        OptionSpec.Toggle("showHint", "Show the hint at the bottom", true, "What to show", "Hidden automatically when the widget is short."),
        OptionSpec.Choice("layout", "Device layout", "list", ["list", "compact", "tiles"], ["List", "Compact list", "Tiles"], "Layout"),
        OptionSpec.Choice("sort", "Sort devices", "connected", BluetoothLogic.SortModes, ["Connected first", "By name", "Lowest battery first"], "Layout"),
        OptionSpec.Number("maxDevices", "Most devices to list", 12, 1, 50, "Layout"),
        OptionSpec.Number("iconSize", "Icon size", 30, 16, 48, "Layout"),
        OptionSpec.Choice("batteryStyle", "Battery display", "icon", ["icon", "percent", "bar"], ["Icon and percentage", "Percentage only", "Bar and percentage"], "Layout"),
        OptionSpec.Color("connectedColor", "Connected color", "", "Colors", "Empty = accent color."),
        OptionSpec.Color("disconnectedColor", "Disconnected color", "", "Colors", "Empty = grey."),
        OptionSpec.Color("lowColor", "Low battery color", "#f59e0b", "Colors"),
        OptionSpec.Number("lowThreshold", "Low battery at or below (%)", 20, 5, 50, "Behavior"),
        OptionSpec.Toggle("lowNotify", "Notify when a connected device's battery is low", true, "Behavior", "Once per device per day."),
        OptionSpec.Choice("clickAction", "Clicking a device", "settings", ["settings", "none"], ["Opens Bluetooth settings", "Does nothing"], "Behavior", "Windows doesn't let apps connect or disconnect most devices."),
        OptionSpec.Toggle("confirmOff", "Ask before turning Bluetooth off", true, "Behavior", "Turning it off disconnects every device, including a Bluetooth keyboard or mouse."),
        OptionSpec.Lines("hidden", "Hidden devices", "", "Behavior", "One name or address per line. Right-click a device and choose Hide this device to add it."),
    ];

    public IEnumerable<Action<LauncherItem>> Samples =>
    [
        item => item.Options["data.sample"] = BluetoothLogic.DefaultSampleJson,
        item => { item.Options["data.sample"] = BluetoothLogic.DefaultSampleJson; item.Options["layout"] = "tiles"; item.Options["batteryStyle"] = "bar"; item.Options["showAddress"] = "true"; },
        item =>
        {
            item.Options["data.sample"] = BluetoothLogic.DefaultSampleJson;
            item.Options["layout"] = "compact";
            item.Options["sort"] = "battery";
            item.Options["batteryDisconnected"] = "true";
            item.Width = 276;
            item.Height = 276;
        },
        item => { item.Options["data.sample"] = """{"radio":"off","devices":[]}"""; },
        item => { item.Options["data.sample"] = """{"radio":"none","devices":[]}"""; item.Options["showHint"] = "false"; },
        // Problem states at the smallest size, where the text and buttons have the least room.
        item => { item.Options["data.sample"] = """{"radio":"denied","devices":[]}"""; item.Width = 180; item.Height = 132; },
        item => { item.Options["data.sample"] = """{"radio":"off","devices":[]}"""; item.Options["_textScale"] = "150"; item.Width = 180; item.Height = 180; },
        item => item.Options["data.sample"] = """{"radio":"on","devices":[]}""",
        item => { item.Options["data.sample"] = """{"radio":"on","devices":[{"name":"Pocket Phone","connected":false,"kind":"phone"}]}"""; item.Options["filter"] = "connected"; item.Width = 180; item.Height = 132; },
        item => { item.Options["data.sample"] = BluetoothLogic.DefaultSampleJson; item.Options["filter"] = "connected"; item.Options["hidden"] = "Studio Headphones\nDesk Speaker\nTravel Mouse"; item.Width = 180; item.Height = 132; },
    ];

    // ---------- Reading what to draw ----------

    private sealed record View(BtRadio Radio, IReadOnlyList<BtDevice> Devices, bool Ready, long Version);

    private static View ReadView(WidgetContext ctx)
    {
        if (ctx.IsSnapshot)
        {
            var sample = BluetoothLogic.ParseSample(ctx.Get("data.sample")) ?? BluetoothLogic.ParseSample(BluetoothLogic.DefaultSampleJson)!;
            return new View(sample.Radio, sample.Devices, true, 0);
        }
        var now = BluetoothService.Instance.Current;
        return new View(now.Radio, now.Devices, now.Ready, now.Version);
    }

    // ---------- Actions ----------

    private static void OpenSettings(WidgetContext ctx)
    {
        if (ctx.IsSnapshot) return;
        ctx.Open(SettingsUri);
    }

    private static void AddDevice(WidgetContext ctx)
    {
        if (ctx.IsSnapshot) return;
        ctx.Open(AddDeviceUri);
    }

    private static void RefreshDevices(WidgetContext ctx)
    {
        if (ctx.IsSnapshot || ctx.IsEditing) return;
        BluetoothService.Instance.Refresh();
    }

    /// <summary>Turns the radio on or off. Only called from a click; the switch shows the target at once and the result when Windows answers.</summary>
    private static void SwitchRadio(WidgetContext ctx, bool on)
    {
        if (ctx.IsSnapshot || ctx.IsEditing || WidgetContext.Cache.ContainsKey(PendingKey)) return;
        if (!on && ctx.GetBool("confirmOff") && !ctx.Confirm("Turn Bluetooth off? Connected devices will disconnect.", "Bluetooth")) return;
        WidgetContext.Cache[PendingKey] = on;
        ctx.Rebuild();
        _ = Run();

        async Task Run()
        {
            string? problem;
            try { problem = await Task.Run(() => BluetoothService.Instance.SetRadioAsync(on)); }
            catch (Exception ex) { App.Log(ex); problem = "Couldn't change Bluetooth: " + ex.Message; }
            await ctx.Dispatcher.InvokeAsync(() =>
            {
                WidgetContext.Cache.TryRemove(PendingKey, out _);
                if (problem is not null) ctx.Status(problem);
                ctx.Rebuild();
            });
        }
    }

    private static void CopyText(WidgetContext ctx, string text, string what)
    {
        try { Clipboard.SetText(text); ctx.Status(what + " copied."); }
        catch (COMException) { ctx.Status("The clipboard is busy; try again."); }
    }

    private static void Hide(WidgetContext ctx, BtDevice device) => ctx.Set("hidden", BluetoothLogic.AddHidden(ctx.Get("hidden"), device));
    private static void Unhide(WidgetContext ctx, BtDevice device) => ctx.Set("hidden", BluetoothLogic.RemoveHidden(ctx.Get("hidden"), device));

    public IEnumerable<WidgetAction> Actions(WidgetContext ctx)
    {
        var radio = BluetoothService.Instance.Current.Radio;
        if (BluetoothLogic.CanSwitch(radio))
            yield return new WidgetAction(radio == BtRadio.On ? "Turn Bluetooth off" : "Turn Bluetooth on", "", () => SwitchRadio(ctx, radio != BtRadio.On));
        yield return new WidgetAction("Refresh devices", "", () => RefreshDevices(ctx));
        yield return new WidgetAction("Bluetooth settings", "", () => OpenSettings(ctx));
        yield return new WidgetAction("Add a device", "", () => AddDevice(ctx));
        int hidden = ctx.GetLines("hidden").Length;
        if (hidden > 0)
        {
            bool reveal = ctx.State("reveal", false);
            yield return new WidgetAction(reveal ? "Hide hidden devices again" : $"Show hidden devices ({hidden})", null, () => ctx.SetState("reveal", !reveal));
        }
    }

    /// <summary>Tells the user once per device per day when a connected device is low on battery.</summary>
    private static void CheckLowBattery(WidgetContext ctx)
    {
        if (ctx.IsSnapshot || !ctx.GetBool("lowNotify")) return;
        var now = BluetoothService.Instance.Current;
        if (now.Radio != BtRadio.On) return;
        var today = DateOnly.FromDateTime(DateTime.Now);
        var notified = BluetoothLogic.ParseNotified(ctx.Get(NotifiedKey));
        var due = BluetoothLogic.DueForLowBattery(now.Devices, ctx.GetInt("lowThreshold"), ctx.GetLines("hidden"), notified, today);
        if (due.Count == 0) return;
        foreach (var device in due)
        {
            string key = BluetoothLogic.NotifyKey(device);
            notified[key] = today;
            // Another copy of this widget (second monitor, floating window) may have announced it a moment ago.
            if (!WidgetContext.Cache.TryAdd($"bluetooth.low:{ctx.Item.Id}:{key}:{today:yyyyMMdd}", true)) continue;
            ctx.Notify("Bluetooth battery low", $"{device.Name} is at {BluetoothLogic.BatteryText(device.Battery)}.");
        }
        ctx.Set(NotifiedKey, BluetoothLogic.FormatNotified(notified, today), false);
    }

    // ---------- Drawing ----------

    private sealed record Look(Brush Connected, Brush Disconnected, Brush Low, Brush Name, Brush NameDim, bool Battery, bool BatteryOffline, bool Icons, bool Address, string BatteryStyle, int Threshold, double IconSize, bool Click);

    public FrameworkElement Build(WidgetContext ctx)
    {
        var view = ReadView(ctx);
        bool reveal = ctx.State("reveal", false);
        var hiddenLines = ctx.GetLines("hidden");
        var arrangement = BluetoothLogic.Arrange(view.Devices, ctx.Get("filter") == "connected", ctx.Get("sort"), hiddenLines, reveal, ctx.GetInt("maxDevices"));
        var look = new Look(
            ctx.GetColor("connectedColor"), ctx.GetColor("disconnectedColor", Theme.Muted), ctx.GetColor("lowColor", Theme.Accent("#f59e0b")),
            ctx.TextBrush, Faded(ctx.TextBrush, 0.72),
            ctx.GetBool("showBattery"), ctx.GetBool("batteryDisconnected"), ctx.GetBool("showIcons"), ctx.GetBool("showAddress"),
            // Narrow cards: a plain percentage and a smaller icon leave room for the name.
            ctx.Width < 232 && ctx.Get("batteryStyle") == "icon" ? "percent" : ctx.Get("batteryStyle"), ctx.GetInt("lowThreshold"),
            ctx.Width < 232 ? Math.Min(ctx.GetNumber("iconSize"), 26) : ctx.GetNumber("iconSize"), ctx.Get("clickAction") == "settings");

        bool listing = view.Radio == BtRadio.On;
        bool wide = ctx.Width >= 300; // the "3 connected" badge shares the header with the title and three buttons
        FrameworkElement? badge = null;
        if (listing && view.Ready && wide && BluetoothLogic.Summary(arrangement.Connected, view.Devices.Count) is { Length: > 0 } summary)
            badge = WidgetKit.Pill(ctx, summary, arrangement.Connected > 0 ? ctx.Item.Accent : null);

        var buttons = new List<FrameworkElement>();
        if ((ctx.GetBool("showSwitch") && view.Radio is BtRadio.On or BtRadio.Off or BtRadio.Disabled) || WidgetContext.Cache.ContainsKey(PendingKey))
            buttons.Add(RadioSwitch(ctx, view.Radio, look.Connected));
        if (ctx.Width >= 190) buttons.Add(WidgetKit.IconButton("", "Refresh the device list", () => RefreshDevices(ctx), 24));
        if (ctx.Width >= 220) buttons.Add(WidgetKit.IconButton("", "Open Bluetooth settings", () => OpenSettings(ctx), 24));
        var (card, body) = WidgetKit.Frame(ctx, badge, buttons.ToArray());

        FrameworkElement main;
        if (!listing && view.Radio != BtRadio.Unknown) main = NotePanel(ctx, view.Radio);
        else if (!view.Ready && view.Devices.Count == 0) main = WidgetKit.Empty(ctx, "Looking for Bluetooth devices…");
        else if (arrangement.Shown.Count == 0) main = EmptyList(ctx, view, arrangement);
        else
        {
            bool tall = ctx.Height >= 240;
            if (tall && ctx.GetBool("showHint"))
            {
                var hint = WidgetKit.Muted(ctx, look.Click
                    ? "Click a device to open Bluetooth settings. Windows doesn’t let apps connect or disconnect most devices."
                    : "Windows doesn’t let apps connect or disconnect most devices. Right-click a device for more.", 10);
                hint.TextWrapping = TextWrapping.Wrap;
                hint.TextTrimming = TextTrimming.None;
                hint.Margin = new Thickness(2, 6, 0, 0);
                DockPanel.SetDock(hint, Dock.Bottom);
                body.Children.Add(hint);
            }
            main = DeviceList(ctx, arrangement, hiddenLines, reveal, look);
        }
        body.Children.Add(main);

        if (!ctx.IsSnapshot)
        {
            long version = view.Version;
            IDisposable? subscription = null;
            card.Loaded += (_, _) =>
            {
                subscription?.Dispose();
                subscription = BluetoothService.Instance.Subscribe(() => ctx.Dispatcher.BeginInvoke(() => { if (subscription is not null) Refreshed(ctx); }));
                // Something may have changed between drawing and getting on screen.
                if (BluetoothService.Instance.Current.Version != version) ctx.Dispatcher.BeginInvoke(() => { if (subscription is not null) Refreshed(ctx); });
                else CheckLowBattery(ctx);
            };
            card.Unloaded += (_, _) => { subscription?.Dispose(); subscription = null; };
        }
        return card;
    }

    private static void Refreshed(WidgetContext ctx)
    {
        CheckLowBattery(ctx);
        ctx.Rebuild();
    }

    private static Brush Faded(Brush brush, double opacity)
    {
        if (brush is not SolidColorBrush solid) return brush;
        var copy = solid.Clone();
        copy.Opacity = solid.Opacity * opacity;
        copy.Freeze();
        return copy;
    }

    private static Brush Tinted(Brush brush, double opacity) => Faded(brush, opacity);

    // ---------- Header switch ----------

    private static FrameworkElement RadioSwitch(WidgetContext ctx, BtRadio radio, Brush onBrush)
    {
        bool pending = WidgetContext.Cache.TryGetValue(PendingKey, out var target) && target is bool;
        bool on = pending ? (bool)target! : radio == BtRadio.On;
        bool enabled = BluetoothLogic.CanSwitch(radio) && !pending;
        var knob = new Ellipse { Width = 14, Height = 14, Fill = Theme.OnPrimary, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left, Opacity = on ? 1 : 0.7 };
        var track = new Border
        {
            Width = 36, Height = 20, CornerRadius = new CornerRadius(10), Padding = new Thickness(3, 0, 3, 0), Child = knob,
            Background = on ? onBrush : Theme.Surface3, BorderBrush = Theme.LineStrong, BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center, Opacity = enabled ? 1 : 0.55,
        };
        if (enabled) WidgetKit.Clickable(track, () => SwitchRadio(ctx, !on), on ? "Turn Bluetooth off" : "Turn Bluetooth on");
        else track.ToolTip = pending ? "Waiting for Windows…" : "Bluetooth isn’t available right now";
        return track;
    }

    // ---------- Empty and off states ----------

    private static FrameworkElement NotePanel(WidgetContext ctx, BtRadio radio)
    {
        var (title, detail) = BluetoothLogic.RadioNote(radio);
        bool tight = ctx.Height < 190;
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = Math.Max(120, ctx.Width - ctx.GetNumber("_padding") * 2 - 8) };
        if (!tight)
        {
            double size = Math.Clamp(ctx.Font(46), 40, 60);
            var circle = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = Theme.Surface3, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10), Child = WidgetKit.Icon(radio == BtRadio.Off ? "" : "", size * 0.46, Theme.Muted) };
            panel.Children.Add(circle);
        }
        var heading = WidgetKit.Text(ctx, title, tight ? 12.5 : 14, ctx.TextBrush, bold: true);
        heading.TextAlignment = TextAlignment.Center;
        heading.TextWrapping = TextWrapping.Wrap;
        heading.TextTrimming = TextTrimming.None;
        heading.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(heading);
        if (!tight && detail.Length > 0)
        {
            var text = WidgetKit.Muted(ctx, detail, 11.5);
            text.TextAlignment = TextAlignment.Center;
            text.TextWrapping = TextWrapping.Wrap;
            text.TextTrimming = TextTrimming.None;
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.Margin = new Thickness(0, 4, 0, 0);
            panel.Children.Add(text);
        }
        var button = radio == BtRadio.Off
            ? WidgetKit.Button("Turn on", () => SwitchRadio(ctx, true), prominent: true)
            : WidgetKit.Button(ctx.Width < 230 ? "Settings" : "Open Bluetooth settings", () => OpenSettings(ctx), prominent: true);
        button.HorizontalAlignment = HorizontalAlignment.Center;
        button.Margin = new Thickness(0, tight ? 6 : 12, 0, 0);
        panel.Children.Add(button);
        return panel;
    }

    private static FrameworkElement EmptyList(WidgetContext ctx, View view, BtArrangement arrangement)
    {
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = Math.Max(120, ctx.Width - ctx.GetNumber("_padding") * 2 - 8) };
        bool onlyConnected = ctx.Get("filter") == "connected" && view.Devices.Count > 0 && arrangement.Hidden == 0;
        var heading = WidgetKit.Text(ctx, onlyConnected ? "No devices connected" : arrangement.Hidden > 0 ? "All devices are hidden" : "No paired devices", ctx.Height < 190 ? 12.5 : 14, ctx.TextBrush, bold: true);
        heading.HorizontalAlignment = HorizontalAlignment.Center;
        heading.TextAlignment = TextAlignment.Center;
        heading.TextWrapping = TextWrapping.Wrap;
        heading.TextTrimming = TextTrimming.None;
        panel.Children.Add(heading);
        if (ctx.Height >= 190)
        {
            string detail = onlyConnected ? $"{view.Devices.Count} paired. Connect one in Bluetooth settings." : arrangement.Hidden > 0 ? "Right-click the widget to show hidden devices." : "Add a phone, headphones or mouse in Bluetooth settings.";
            var text = WidgetKit.Muted(ctx, detail, 11.5);
            text.TextAlignment = TextAlignment.Center;
            text.TextWrapping = TextWrapping.Wrap;
            text.TextTrimming = TextTrimming.None;
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.Margin = new Thickness(0, 4, 0, 0);
            panel.Children.Add(text);
        }
        bool toSettings = onlyConnected || arrangement.Hidden > 0;
        var button = WidgetKit.Button(toSettings ? (ctx.Width < 230 ? "Settings" : "Bluetooth settings") : "Add a device", () => { if (toSettings) OpenSettings(ctx); else AddDevice(ctx); });
        button.HorizontalAlignment = HorizontalAlignment.Center;
        button.Margin = new Thickness(0, ctx.Height < 190 ? 6 : 10, 0, 0);
        panel.Children.Add(button);
        return panel;
    }

    // ---------- The device list ----------

    private static FrameworkElement DeviceList(WidgetContext ctx, BtArrangement arrangement, string[] hiddenLines, bool reveal, Look look)
    {
        string layout = ctx.Get("layout");
        var stack = new StackPanel();
        if (layout == "tiles")
        {
            double pad = ctx.GetNumber("_padding");
            double inner = ctx.Width - (pad * 2 + 4);
            int columns = Math.Max(1, (int)Math.Floor(inner / 118));
            var grid = new UniformGrid { Columns = columns, Margin = new Thickness(0, 0, 0, 0) };
            foreach (var device in arrangement.Shown)
                grid.Children.Add(Tile(ctx, device, hiddenLines, look, ctx.Width < 232));
            stack.Children.Add(grid);
        }
        else
        {
            foreach (var device in arrangement.Shown)
                stack.Children.Add(layout == "compact" ? CompactRow(ctx, device, hiddenLines, look) : ListRow(ctx, device, hiddenLines, look));
        }

        var notes = new List<string>();
        if (arrangement.More > 0) notes.Add($"+{arrangement.More} more");
        if (arrangement.Hidden > 0 && !reveal) notes.Add($"{arrangement.Hidden} hidden");
        if (reveal && arrangement.Hidden > 0) notes.Add("showing hidden devices");
        if (notes.Count > 0)
        {
            var footer = WidgetKit.Muted(ctx, string.Join(" · ", notes), 10.5);
            footer.Margin = new Thickness(8, 4, 0, 2);
            if (arrangement.Hidden > 0) WidgetKit.Clickable(footer, () => { if (!ctx.IsEditing) ctx.SetState("reveal", !reveal); }, reveal ? "Hide them again" : "Show hidden devices");
            stack.Children.Add(footer);
        }

        var scroll = WidgetKit.Scroll(stack);
        double offset = ctx.State("scroll", 0.0);
        if (offset > 0) scroll.Loaded += (_, _) => scroll.ScrollToVerticalOffset(offset);
        scroll.ScrollChanged += (_, e) => { if (e.VerticalChange != 0) ctx.SetState("scroll", scroll.VerticalOffset, false); };
        return scroll;
    }

    private static Brush StatusBrush(BtDevice device, Look look) => device.Connected ? look.Connected : look.Disconnected;

    private static FrameworkElement IconCircle(BtDevice device, Look look, double size, bool dimmed)
    {
        var brush = StatusBrush(device, look);
        var circle = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), VerticalAlignment = VerticalAlignment.Center,
            Background = device.Connected ? Tinted(brush, 0.2) : Theme.Surface3,
            Child = WidgetKit.Icon(BluetoothLogic.Glyph(device.Kind), size * 0.5, brush),
            Opacity = dimmed ? 0.55 : 1,
        };
        return circle;
    }

    private static FrameworkElement StatusDot(BtDevice device, Look look)
    {
        var dot = new Ellipse { Width = 8, Height = 8, Fill = StatusBrush(device, look), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 8, 0) };
        return dot;
    }

    private static bool ShowsBattery(BtDevice device, Look look) => look.Battery && device.Battery is not null && (device.Connected || look.BatteryOffline);

    /// <summary>The battery readout: icon + percent, percent, or bar + percent. Low levels use the warning color.</summary>
    private static FrameworkElement? BatteryView(WidgetContext ctx, BtDevice device, Look look, bool horizontalBar)
    {
        if (!ShowsBattery(device, look)) return null;
        int level = device.Battery!.Value;
        bool low = device.Connected && BluetoothLogic.IsLow(level, look.Threshold);
        Brush brush = low ? look.Low : device.Connected ? Theme.Text2 : Theme.Muted;
        var text = WidgetKit.Text(ctx, BluetoothLogic.BatteryText(level), 11.5, brush, bold: low);
        text.VerticalAlignment = VerticalAlignment.Center;
        FrameworkElement result;
        switch (look.BatteryStyle)
        {
            case "percent":
                result = text;
                break;
            case "bar":
                var bar = WidgetKit.Bar(level / 100.0, low ? look.Low : device.Connected ? look.Connected : look.Disconnected, 5, Theme.LineStrong);
                bar.Width = 34;
                bar.VerticalAlignment = VerticalAlignment.Center;
                if (horizontalBar)
                {
                    var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    bar.Margin = new Thickness(0, 0, 6, 0);
                    line.Children.Add(bar);
                    line.Children.Add(text);
                    result = line;
                }
                else
                {
                    var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                    text.HorizontalAlignment = HorizontalAlignment.Right;
                    bar.Margin = new Thickness(0, 3, 0, 0);
                    column.Children.Add(text);
                    column.Children.Add(bar);
                    result = column;
                }
                break;
            default:
                var icon = WidgetKit.Icon(BluetoothLogic.BatteryGlyph(level), ctx.Font(15), brush);
                icon.Margin = new Thickness(0, 0, 3, 0);
                var pair = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                pair.Children.Add(icon);
                pair.Children.Add(text);
                result = pair;
                break;
        }
        if (!device.Connected) result.Opacity = 0.7;
        return result;
    }

    private static string Tooltip(BtDevice device, Look look)
    {
        var lines = new List<string> { device.Name, BluetoothLogic.KindLabel(device.Kind) + (device.Connected ? ", connected" : ", not connected") };
        if (device.Battery is { } b) lines.Add("Battery " + BluetoothLogic.BatteryText(b) + (device.Connected ? "" : " (last known)"));
        if (BluetoothLogic.FormatAddress(device.Address) is { Length: > 0 } address) lines.Add(address);
        if (look.Click) lines.Add("Click to open Bluetooth settings");
        return string.Join("\n", lines);
    }

    /// <summary>Makes the element clickable (when the setting says so), adds the hover tooltip and the device's menu.</summary>
    private static void Wire(WidgetContext ctx, FrameworkElement element, BtDevice device, string[] hiddenLines, Look look)
    {
        element.ToolTip = Tooltip(device, look);
        if (look.Click) WidgetKit.Clickable(element, () => { if (!ctx.IsEditing) OpenSettings(ctx); });
        element.ContextMenu = DeviceMenu(ctx, device, BluetoothLogic.IsHidden(device, hiddenLines));
    }

    private static ContextMenu DeviceMenu(WidgetContext ctx, BtDevice device, bool hidden)
    {
        var menu = MainWindow.Menu();
        MainWindow.AddMenuItem(menu, "Bluetooth settings", () => OpenSettings(ctx), "");
        MainWindow.AddMenuItem(menu, "Add a device", () => AddDevice(ctx), "");
        menu.Items.Add(new Separator());
        MainWindow.AddMenuItem(menu, "Copy device name", () => CopyText(ctx, device.Name, "Name"), "");
        if (BluetoothLogic.FormatAddress(device.Address).Length > 0) MainWindow.AddMenuItem(menu, "Copy address", () => CopyText(ctx, BluetoothLogic.FormatAddress(device.Address), "Address"), "");
        if (hidden) MainWindow.AddMenuItem(menu, "Unhide this device", () => Unhide(ctx, device), "");
        else MainWindow.AddMenuItem(menu, "Hide this device", () => Hide(ctx, device), "");
        menu.Items.Add(new Separator());
        MainWindow.AddMenuItem(menu, "Refresh devices", () => RefreshDevices(ctx), "");
        MainWindow.AddMenuItem(menu, "Customize…", ctx.Customize, "");
        return menu;
    }

    private static FrameworkElement ListRow(WidgetContext ctx, BtDevice device, string[] hiddenLines, Look look)
    {
        bool hidden = BluetoothLogic.IsHidden(device, hiddenLines);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (look.Icons)
        {
            var icon = IconCircle(device, look, look.IconSize, hidden);
            icon.Margin = new Thickness(0, 0, 10, 0);
            grid.Children.Add(icon);
        }
        else grid.Children.Add(StatusDot(device, look));

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = WidgetKit.Text(ctx, device.Name, 13, device.Connected ? look.Name : look.NameDim, bold: device.Connected);
        text.Children.Add(name);
        var status = WidgetKit.Text(ctx, BluetoothLogic.StatusLine(device, look.Address), 10.5, device.Connected ? look.Connected : Theme.Muted);
        text.Children.Add(status);
        if (hidden) name.Opacity = status.Opacity = 0.55;
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        if (BatteryView(ctx, device, look, horizontalBar: false) is { } battery)
        {
            battery.Margin = new Thickness(8, 0, 2, 0);
            Grid.SetColumn(battery, 2);
            grid.Children.Add(battery);
        }
        var row = WidgetKit.Row(grid);
        Wire(ctx, row, device, hiddenLines, look);
        return row;
    }

    private static FrameworkElement CompactRow(WidgetContext ctx, BtDevice device, string[] hiddenLines, Look look)
    {
        bool hidden = BluetoothLogic.IsHidden(device, hiddenLines);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (look.Icons)
        {
            var icon = IconCircle(device, look, Math.Min(look.IconSize, 24), hidden);
            icon.Margin = new Thickness(0, 0, 8, 0);
            grid.Children.Add(icon);
        }
        else grid.Children.Add(StatusDot(device, look));

        var name = WidgetKit.Text(ctx, device.Name, 12.5, device.Connected ? look.Name : look.NameDim, bold: device.Connected);
        if (hidden) name.Opacity = 0.55;
        var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        line.Children.Add(name);
        if (look.Address && BluetoothLogic.FormatAddress(device.Address) is { Length: > 0 } address)
        {
            var addressText = WidgetKit.Muted(ctx, address, 10);
            addressText.Margin = new Thickness(8, 0, 0, 0);
            line.Children.Add(addressText);
        }
        var nameHost = new Grid { VerticalAlignment = VerticalAlignment.Center };
        nameHost.Children.Add(line);
        Grid.SetColumn(nameHost, 1);
        grid.Children.Add(nameHost);

        if (BatteryView(ctx, device, look, horizontalBar: true) is { } battery)
        {
            battery.Margin = new Thickness(8, 0, 2, 0);
            Grid.SetColumn(battery, 2);
            grid.Children.Add(battery);
        }
        var row = WidgetKit.Row(grid);
        row.Padding = new Thickness(6, 3, 6, 3);
        Wire(ctx, row, device, hiddenLines, look);
        return row;
    }

    private static FrameworkElement Tile(WidgetContext ctx, BtDevice device, string[] hiddenLines, Look look, bool narrow)
    {
        bool hidden = BluetoothLogic.IsHidden(device, hiddenLines);
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (look.Icons)
        {
            var icon = IconCircle(device, look, Math.Min(look.IconSize, 34), hidden);
            icon.HorizontalAlignment = HorizontalAlignment.Left;
            top.Children.Add(icon);
        }
        else
        {
            var dot = StatusDot(device, look);
            dot.HorizontalAlignment = HorizontalAlignment.Left;
            top.Children.Add(dot);
        }
        if (BatteryView(ctx, device, look, horizontalBar: false) is { } battery)
        {
            battery.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(battery, 1);
            top.Children.Add(battery);
        }
        layout.Children.Add(top);
        var name = WidgetKit.Text(ctx, device.Name, 12.5, device.Connected ? look.Name : look.NameDim, bold: device.Connected);
        name.Margin = new Thickness(0, 7, 0, 0);
        Grid.SetRow(name, 1);
        layout.Children.Add(name);
        var lower = new StackPanel();
        var status = WidgetKit.Text(ctx, BluetoothLogic.StatusLine(device, false), 10, device.Connected ? look.Connected : Theme.Muted);
        lower.Children.Add(status);
        if (look.Address && !narrow && BluetoothLogic.FormatAddress(device.Address) is { Length: > 0 } address) lower.Children.Add(WidgetKit.Muted(ctx, address, 9.5));
        Grid.SetRow(lower, 2);
        layout.Children.Add(lower);
        if (hidden) { name.Opacity = lower.Opacity = 0.55; }

        var tile = new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 8, 9, 8), Margin = new Thickness(3),
            Background = device.Connected ? Tinted(look.Connected, 0.13) : Theme.Surface3, Child = layout,
            BorderBrush = device.Connected ? Tinted(look.Connected, 0.35) : Theme.Line, BorderThickness = new Thickness(1),
        };
        Wire(ctx, tile, device, hiddenLines, look);
        return tile;
    }
}
