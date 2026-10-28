using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopDeck.Models;

namespace DesktopDeck.Widgets;

/// <summary>
/// OBS Studio remote control over OBS's built-in WebSocket server (OBS 28+, Tools, WebSocket Server Settings; obs-websocket v5):
/// record and stream buttons with live timers, pause, scene buttons, microphone mute and the replay buffer.
/// The protocol math is in ObsLogic.cs (tests/DesktopDeck.WidgetTests/ObsTests.cs); the shared connection is in ObsClient.cs.
/// </summary>
internal sealed class ObsWidget : IWidget
{
    private const string LinkKey = "cache.link";

    public string Kind => "obs";
    public string Name => "OBS Studio";
    public string Description => "Record, stream, switch scenes and mute your mic in OBS";
    public string Section => "Connected";
    public string Glyph => "";
    public string Accent => "#8b8fa8";
    public (double Width, double Height) DefaultSize => (276, 300);
    public (double Width, double Height) MinimumSize => (204, 180);

    public IReadOnlyList<OptionSpec> Options { get; } =
    [
        OptionSpec.Text("host", "OBS address", "127.0.0.1", "Connection", "The computer running OBS. 127.0.0.1 is this PC."),
        OptionSpec.Number("port", "OBS port", ObsLogic.DefaultPort, 1, 65535, "Connection", "Shown in OBS under Tools, WebSocket Server Settings. The password is asked for when needed and stored encrypted."),
        OptionSpec.Text("micInput", "Microphone input name", "Mic/Aux", "Connection", "The audio input to mute, exactly as named in OBS (Audio Mixer). Empty = no mute button."),
        OptionSpec.Toggle("showRecord", "Show Record", true, "What to show"),
        OptionSpec.Toggle("showStream", "Show Stream", true, "What to show"),
        OptionSpec.Toggle("showPause", "Show the pause button while recording", true, "What to show"),
        OptionSpec.Toggle("showMic", "Show the microphone mute", true, "What to show"),
        OptionSpec.Toggle("showScenes", "Show scene buttons", true, "What to show"),
        OptionSpec.Toggle("showReplay", "Show Save replay", false, "What to show", "Needs the replay buffer to be started in OBS."),
        OptionSpec.Number("maxScenes", "Most scenes shown", 12, 1, 40, "Scenes"),
        OptionSpec.Lines("hiddenScenes", "Hide these scenes", "", "Scenes", "One scene name per line."),
        OptionSpec.Choice("sceneLayout", "Scene buttons", "grid", ["grid", "list"], ["Two columns", "One per line"], "Layout"),
        OptionSpec.Toggle("compact", "Compact", false, "Layout"),
        OptionSpec.Toggle("showTimers", "Show the timers", true, "Layout"),
        OptionSpec.Choice("confirmStop", "Ask before stopping", "stream", ["none", "stream", "both"], ["Never", "Stream only", "Record and stream"], "Behavior", "Stopping a live stream by accident is awkward, so it asks by default."),
        OptionSpec.Color("recordColor", "Recording color", "#ef4444", "Colors"),
        OptionSpec.Color("liveColor", "Live color", "#ef4444", "Colors"),
        OptionSpec.Color("sceneColor", "Current scene color", "", "Colors", "Empty = accent color."),
        OptionSpec.Color("mutedColor", "Muted color", "#ef4444", "Colors"),
    ];

    // ---------- Samples ----------

    public IEnumerable<Action<LauncherItem>> Samples =>
    [
        item => Fill(item, recording: true, streaming: true, muted: false, replay: true),
        item => { Fill(item, recording: false, streaming: false, muted: true, replay: false); item.Options["sceneLayout"] = "list"; item.Options["compact"] = "true"; item.Width = 228; item.Height = 276; },
        item => { Fill(item, recording: false, streaming: false, muted: false, replay: false, age: TimeSpan.FromMinutes(25)); item.Options[LinkKey] = "offline"; },
        item => { item.Options[LinkKey] = "password"; item.Width = 228; item.Height = 228; },
        item => { Fill(item, recording: true, streaming: false, muted: false, replay: false, paused: true); item.Options["showReplay"] = "true"; item.Width = 324; item.Height = 252; },
    ];

    private static void Fill(LauncherItem item, bool recording, bool streaming, bool muted, bool replay, bool paused = false, TimeSpan? age = null)
    {
        var now = DateTimeOffset.Now;
        var state = new ObsLogic.State(
            recording ? new ObsLogic.Output(true, paused, false, false, TimeSpan.FromSeconds(42 * 60 + 17), now) : ObsLogic.Output.Off,
            streaming ? new ObsLogic.Output(true, false, false, false, TimeSpan.FromSeconds(75 * 60 + 3), now) : ObsLogic.Output.Off,
            replay, "Gameplay", ["Starting soon", "Gameplay", "Just chatting", "Face cam", "Screen share", "Be right back", "Ending"]);
        ConnectedKit.Fill(item, ObsLogic.ToCache(state, muted, now), "", age ?? TimeSpan.FromSeconds(30));
    }

    // ---------- Connection ----------

    private static (string Host, int Port) Address(WidgetContext ctx) => (ctx.Get("host"), ctx.GetInt("port"));

    /// <summary>Everything the widget draws from: the live shared connection, or cached/sample data.</summary>
    private sealed record View(ObsLink Link, ObsLogic.State State, bool? Muted, ObsConnection? Connection, bool FromCache);

    private static View Current(WidgetContext ctx, ObsConnection? connection)
    {
        var now = DateTimeOffset.Now;
        string mic = ctx.Get("micInput").Trim();
        if (ctx.IsSnapshot)
        {
            var (state, muted) = ObsLogic.FromCache(ctx.Item.Options.GetValueOrDefault(ConnectedKit.CacheJson), now, live: true);
            ObsLink link = ctx.Item.Options.GetValueOrDefault(LinkKey) switch { "offline" => ObsLink.Offline, "password" => ObsLink.NeedsPassword, _ => state.Scenes.Count > 0 ? ObsLink.Connected : ObsLink.Offline };
            return new View(link, link == ObsLink.Connected ? state : state with { Record = ObsLogic.Output.Off, Stream = ObsLogic.Output.Off }, muted, null, link != ObsLink.Connected);
        }
        if (connection is null) return new View(ObsLink.Offline, ObsLogic.State.Empty, null, null, true);
        bool? mutedNow = mic.Length > 0 && connection.Mutes.TryGetValue(mic, out bool m) ? m : null;
        if (connection.Link == ObsLink.Connected) return new View(ObsLink.Connected, connection.State, mutedNow, connection, false);
        var (cached, cachedMuted) = ObsLogic.FromCache(ctx.Item.Options.GetValueOrDefault(ConnectedKit.CacheJson), now, live: false);
        return new View(connection.Link, cached, cachedMuted, connection, true);
    }

    /// <summary>Keeps the cache (scene list, current scene, mic) current while connected, and stamps when it was last live.</summary>
    private static void UpdateCache(WidgetContext ctx, View view)
    {
        if (ctx.IsSnapshot || view.Connection is not { } connection) return;
        var options = ctx.Item.Options;
        var now = DateTimeOffset.Now;
        var stamp = OnlineLogic.ParseStamp(options.GetValueOrDefault(ConnectedKit.CacheTime));
        if (view.Link == ObsLink.Connected)
        {
            string json = ObsLogic.ToCache(view.State with { Record = ObsLogic.Output.Off, Stream = ObsLogic.Output.Off }, view.Muted, now);
            if (options.GetValueOrDefault(ConnectedKit.CacheJson) != json || stamp is null || now - stamp > TimeSpan.FromMinutes(5))
            {
                options[ConnectedKit.CacheJson] = json;
                options[ConnectedKit.CacheTime] = OnlineLogic.FormatStamp(now);
                ctx.Changed(rebuild: false);
            }
        }
        else if (connection.LastConnected > (stamp ?? DateTimeOffset.MinValue) && options.ContainsKey(ConnectedKit.CacheJson))
        {
            options[ConnectedKit.CacheTime] = OnlineLogic.FormatStamp(connection.LastConnected);
            ctx.Changed(rebuild: false);
        }
    }

    private static void AskPassword(WidgetContext ctx, string? why = null)
    {
        string? password = ctx.AskSecret("OBS WebSocket password", (why is null ? "" : why + " ") + "Enter the password from OBS, Tools, WebSocket Server Settings. It is saved encrypted for your Windows account only.");
        if (password is null) return;
        ctx.SetSecret(ObsClient.PasswordName, password);
        var (host, port) = Address(ctx);
        if (ObsClient.Touch(ctx, host, port, refreshPassword: true) is { } connection) ObsClient.RetryNow(connection);
        ctx.Rebuild();
    }

    public IEnumerable<WidgetAction> Actions(WidgetContext ctx)
    {
        var (host, port) = Address(ctx);
        yield return new WidgetAction("Reconnect to OBS", "", () =>
        {
            if (ObsClient.Touch(ctx, host, port, refreshPassword: true) is { } connection) ObsClient.RetryNow(connection);
            ctx.Status("Looking for OBS…");
        });
        yield return new WidgetAction(ctx.HasSecret(ObsClient.PasswordName) ? "Change the OBS password…" : "Set the OBS password…", "", () => AskPassword(ctx));
        if (ctx.HasSecret(ObsClient.PasswordName))
            yield return new WidgetAction("Disconnect (forget the password)", "", () =>
            {
                ctx.RemoveSecret(ObsClient.PasswordName);
                if (ObsClient.Touch(ctx, host, port, refreshPassword: true) is { } connection) ObsClient.RetryNow(connection);
                ctx.Status("The OBS password was removed from this PC.");
            });
    }

    // ---------- Requests from the buttons ----------

    private static void Send(WidgetContext ctx, ObsConnection connection, string type, object? data = null, string? input = null)
    {
        _ = Go();
        async Task Go()
        {
            var reply = await ObsClient.RequestAsync(connection, type, data);
            if (!reply.Ok) { ctx.Status(ObsLogic.Describe(type, reply.Code, reply.Comment, input)); return; }
            if (type == "ToggleInputMute" && input is not null) { connection.Mutes[input] = ObsLogic.ParseMuted(reply.Data); ctx.Rebuild(); return; }
            // Events usually say what happened; asking again soon is the safety net.
            await Task.Delay(700);
            await ObsClient.RefreshAsync(connection);
        }
    }

    private static void ToggleOutput(WidgetContext ctx, ObsConnection connection, bool record, bool active)
    {
        string confirm = ctx.Get("confirmStop");
        if (active && (confirm == "both" || (confirm == "stream" && !record)) && !ctx.Confirm(record ? "Stop recording?" : "Stop streaming? Your viewers will see the stream end.", "OBS")) return;
        Send(ctx, connection, record ? (active ? "StopRecord" : "StartRecord") : (active ? "StopStream" : "StartStream"));
    }

    // ---------- Drawing ----------

    public FrameworkElement Build(WidgetContext ctx)
    {
        var (host, port) = Address(ctx);
        var connection = ObsClient.Touch(ctx, host, port, refreshPassword: false);
        if (connection is not null) ObsClient.Listen(ctx, connection);
        var view = Current(ctx, connection);
        UpdateCache(ctx, view);
        var (card, body) = WidgetKit.Frame(ctx);

        var now = DateTimeOffset.Now;
        if (connection is not null)
        {
            ctx.Tick(() => connection.State.Record is { Active: true, Paused: false } || connection.State.Stream.Active, _ =>
            {
                ObsClient.Touch(ctx, host, port, refreshPassword: true);
                if (connection.Link == ObsLink.Connected && (connection.State.Record.Active || connection.State.Stream.Active)) ctx.Rebuild();
            });
        }

        // Why we can't control OBS right now.
        string? banner = null, bannerTitle = null;
        string glyph = "";
        switch (view.Link)
        {
            case ObsLink.NeedsPassword:
                var passwordPanel = ConnectedKit.SetupPanel(ctx, "", "OBS needs its password", "In OBS: Tools, WebSocket Server Settings shows it. It is stored encrypted on this PC.", "Enter password…", () => AskPassword(ctx));
                body.Children.Add(WidgetKit.Scroll(passwordPanel));
                return card;
            case ObsLink.WrongPassword:
                var wrongPanel = ConnectedKit.SetupPanel(ctx, "", "That password didn't work", "Check it in OBS under Tools, WebSocket Server Settings.", "Enter password…", () => AskPassword(ctx, "The saved password was refused."), Theme.Danger);
                body.Children.Add(WidgetKit.Scroll(wrongPanel));
                return card;
            case ObsLink.Offline:
                bannerTitle = "Start OBS and enable WebSocket";
                banner = "In OBS: Tools, WebSocket Server Settings, tick Enable WebSocket server. Looking on " + (ObsLogic.Endpoint(host, port)?.Authority ?? "an invalid address") + ".";
                break;
            case ObsLink.Idle or ObsLink.Connecting:
                bannerTitle = "Connecting to OBS…";
                banner = "";
                glyph = "";
                break;
        }

        if (view.Link != ObsLink.Connected && view.State.Scenes.Count == 0 && !view.Muted.HasValue)
        {
            // Nothing cached: a full friendly message.
            var panel = ConnectedKit.SetupPanel(ctx, glyph, bannerTitle ?? "OBS", banner ?? "", view.Link == ObsLink.Offline ? "Try again" : null, view.Link == ObsLink.Offline && connection is not null ? () => ObsClient.RetryNow(connection) : null, ctx.Accent);
            body.Children.Add(WidgetKit.Scroll(panel));
            return card;
        }

        if (view.Link != ObsLink.Connected) AddOfflineFooter(ctx, body, view, now);
        var content = Controls(ctx, view, connection, now);
        if (view.Link != ObsLink.Connected)
        {
            var holder = new StackPanel();
            var note = WidgetKit.Text(ctx, bannerTitle ?? "", 12, Theme.Text2, bold: true);
            note.Margin = new Thickness(4, 0, 4, 6);
            holder.Children.Add(note);
            if (!string.IsNullOrEmpty(banner) && ctx.Height >= 240)
            {
                var how = WidgetKit.Muted(ctx, banner, 10.5);
                how.TextWrapping = TextWrapping.Wrap;
                how.TextTrimming = TextTrimming.None;
                how.Margin = new Thickness(4, -3, 4, 8);
                holder.Children.Add(how);
            }
            content.Opacity = 0.4;
            content.IsHitTestVisible = false;
            holder.Children.Add(content);
            body.Children.Add(WidgetKit.Scroll(holder));
        }
        else body.Children.Add(WidgetKit.Scroll(content));
        return card;
    }

    private static void AddOfflineFooter(WidgetContext ctx, DockPanel body, View view, DateTimeOffset now)
    {
        if (OnlineLogic.ParseStamp(ctx.Item.Options.GetValueOrDefault(ConnectedKit.CacheTime)) is not { } at) return;
        var note = WidgetKit.Text(ctx, OnlineLogic.OfflineNote(at, now), 10.5, Theme.Text2);
        note.Margin = new Thickness(0, 6, 0, 0);
        DockPanel.SetDock(note, Dock.Bottom);
        body.Children.Add(note);
    }

    private static StackPanel Controls(WidgetContext ctx, View view, ObsConnection? connection, DateTimeOffset now)
    {
        var state = view.State;
        bool compact = ctx.GetBool("compact");
        bool live = view.Link == ObsLink.Connected;
        var stack = new StackPanel();

        var tiles = new List<FrameworkElement>();
        if (ctx.GetBool("showRecord")) tiles.Add(Tile(ctx, state.Record, true, now, connection, live));
        if (ctx.GetBool("showStream")) tiles.Add(Tile(ctx, state.Stream, false, now, connection, live));
        if (tiles.Count > 0)
        {
            var grid = new Grid();
            for (int i = 0; i < tiles.Count; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                tiles[i].Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 0);
                Grid.SetColumn(tiles[i], i);
                grid.Children.Add(tiles[i]);
            }
            stack.Children.Add(grid);
        }

        string mic = ctx.Get("micInput").Trim();
        bool showMic = ctx.GetBool("showMic") && mic.Length > 0, showReplay = ctx.GetBool("showReplay");
        if (showMic || showReplay)
        {
            var row = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            if (showMic)
            {
                bool? muted = view.Muted;
                var mutedBrush = ctx.GetColor("mutedColor", WidgetKit.Color("#ef4444"));
                var label = new StackPanel { Orientation = Orientation.Horizontal };
                label.Children.Add(WidgetKit.Icon(Theme.Glyphs.Mic, ctx.Font(13), muted == true ? mutedBrush : Theme.Text2));
                label.Children.Add(WidgetKit.Text(ctx, compact ? (muted == true ? "Muted" : "Mic") : "Microphone", 12, muted == true ? mutedBrush : ctx.TextBrush));
                ((TextBlock)label.Children[1]).Margin = new Thickness(7, 0, 0, 0);
                var state2 = WidgetKit.Text(ctx, muted is null ? "" : muted == true ? "Muted" : "Live", 11, muted == true ? mutedBrush : Theme.Muted, bold: true);
                var inner = new Grid();
                inner.ColumnDefinitions.Add(new ColumnDefinition());
                inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                inner.Children.Add(label);
                if (!compact) { Grid.SetColumn(state2, 1); inner.Children.Add(state2); }
                var micButton = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 6, 10, 6), Background = muted == true ? Theme.Tint("#ef4444", 0.14) : Theme.Surface3, Child = inner, ToolTip = mic + (muted == true ? " is muted. Click to unmute." : ". Click to mute.") };
                if (live && connection is not null) WidgetKit.Clickable(micButton, () => Send(ctx, connection, "ToggleInputMute", new Dictionary<string, object> { ["inputName"] = mic }, mic));
                if (live && connection is not null && muted is null && !connection.MuteAsked.ContainsKey(mic)) _ = ObsClient.AskMuteAsync(connection, mic);
                Grid.SetColumn(micButton, 0);
                row.Children.Add(micButton);
            }
            if (showReplay)
            {
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                bool ready = state.ReplayActive == true;
                var save = WidgetKit.Button("Save replay", () => { if (connection is not null) Send(ctx, connection, "SaveReplayBuffer"); });
                save.IsEnabled = live && ready && connection is not null || ctx.IsSnapshot && ready;
                save.ToolTip = ready ? "Saves the last seconds from the replay buffer" : "Start the replay buffer in OBS first";
                save.Margin = new Thickness(showMic ? 6 : 0, 0, 0, 0);
                if (!showMic) { Grid.SetColumnSpan(save, 2); save.HorizontalAlignment = HorizontalAlignment.Stretch; }
                Grid.SetColumn(save, 1);
                row.Children.Add(save);
            }
            stack.Children.Add(row);
        }

        if (ctx.GetBool("showScenes"))
        {
            var scenes = ObsLogic.VisibleScenes(state.Scenes, ctx.GetLines("hiddenScenes"), ctx.GetInt("maxScenes"));
            if (scenes.Count > 0)
            {
                var title = WidgetKit.Text(ctx, "SCENES", 10, Theme.Muted, bold: true);
                title.Margin = new Thickness(4, 10, 0, 4);
                stack.Children.Add(title);
                int columns = ctx.Get("sceneLayout") == "list" || ctx.Width < 190 ? 1 : 2;
                var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = columns };
                var currentBrush = ctx.GetColor("sceneColor");
                foreach (string scene in scenes)
                {
                    bool current = string.Equals(scene, state.Current, StringComparison.Ordinal);
                    var text = WidgetKit.Text(ctx, scene, 12, current ? ctx.TextBrush : Theme.Text2, bold: current);
                    text.HorizontalAlignment = columns == 1 ? HorizontalAlignment.Left : HorizontalAlignment.Center;
                    var button = new Border
                    {
                        CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, columns == 2 ? 4 : 0, 4), Padding = new Thickness(8, compact ? 3 : 6, 8, compact ? 4 : 7),
                        Background = current ? Theme.Tint(ctx.Item.Accent, 0.28) : Theme.Surface3, BorderThickness = new Thickness(1), BorderBrush = current ? currentBrush : Brushes.Transparent,
                        Child = text, ToolTip = scene,
                    };
                    string target = scene;
                    if (live && connection is not null) WidgetKit.Clickable(button, () => { if (!current) Send(ctx, connection, "SetCurrentProgramScene", new Dictionary<string, object> { ["sceneName"] = target }); });
                    grid.Children.Add(button);
                }
                stack.Children.Add(grid);
            }
        }
        return stack;
    }

    private static string TileHex(WidgetContext ctx, ObsLogic.Output output, bool record)
    {
        if (output.Paused || output.Reconnecting) return "#f59e0b";
        string chosen = ctx.Get(record ? "recordColor" : "liveColor");
        return chosen.Length > 0 ? chosen : "#ef4444";
    }

    /// <summary>The Record or Stream tile: a dot and label, a running timer, and a click to start or stop.</summary>
    private static FrameworkElement Tile(WidgetContext ctx, ObsLogic.Output output, bool record, DateTimeOffset now, ObsConnection? connection, bool live)
    {
        bool compact = ctx.GetBool("compact");
        var color = ctx.GetColor(record ? "recordColor" : "liveColor", WidgetKit.Color("#ef4444"));
        var amber = WidgetKit.Color("#f59e0b");
        Brush dotBrush = !output.Active ? Theme.Dim : output.Paused || output.Reconnecting ? amber : color;
        string label = output.Pending ? (output.Active ? "STOPPING" : "STARTING") : output.Reconnecting ? "RECONNECTING" : output.Paused ? "PAUSED" : output.Active ? (record ? "REC" : "LIVE") : (record ? "RECORD" : "STREAM");

        var stack = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Fill = dotBrush, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        var name = WidgetKit.Text(ctx, label, 10.5, output.Active ? ctx.TextBrush : Theme.Muted, bold: true);
        Grid.SetColumn(name, 1);
        head.Children.Add(name);
        if (record && output.Active && !output.Pending && ctx.GetBool("showPause") && live && connection is not null)
        {
            var pause = WidgetKit.IconButton(output.Paused ? Theme.Glyphs.Play : Theme.Glyphs.Pause, output.Paused ? "Resume recording" : "Pause recording", () => Send(ctx, connection, "ToggleRecordPause"), 22);
            Grid.SetColumn(pause, 2);
            head.Children.Add(pause);
        }
        stack.Children.Add(head);
        if (ctx.GetBool("showTimers") || !output.Active)
        {
            string text = output.Active ? ConnectedLogic.Duration(output.Elapsed(now)) : (compact ? "Start" : record ? "Click to record" : "Click to go live");
            var time = WidgetKit.Text(ctx, text, output.Active ? (compact || ctx.Width < 250 ? 15 : 19) : 11, output.Active ? ctx.TextBrush : Theme.Muted, bold: output.Active);
            time.Margin = new Thickness(15, output.Active ? 2 : 4, 0, 0);
            stack.Children.Add(time);
        }
        var tile = new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(10, compact ? 5 : 8, 8, compact ? 6 : 9), Child = stack,
            Background = output.Active ? Theme.Tint(TileHex(ctx, output, record), 0.16) : Theme.Surface3,
            ToolTip = output.Active ? (record ? "Click to stop recording" : "Click to stop streaming") : (record ? "Click to start recording" : "Click to start streaming"),
        };
        if (live && connection is not null && !output.Pending) WidgetKit.Clickable(tile, () => ToggleOutput(ctx, connection, record, output.Active));
        return tile;
    }
}
