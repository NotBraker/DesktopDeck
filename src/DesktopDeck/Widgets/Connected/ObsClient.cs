using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;

namespace DesktopDeck.Widgets;

internal enum ObsLink { Idle, Connecting, Connected, NeedsPassword, WrongPassword, Offline }

/// <summary>One shared obs-websocket v5 connection to a host:port. Created on demand, kept alive only while a widget on screen touches it.</summary>
internal sealed class ObsConnection(string key, Uri uri)
{
    public string Key { get; } = key;
    public Uri Uri { get; } = uri;
    public volatile ObsLink Link = ObsLink.Idle;
    public volatile string? Password;
    public ObsLogic.State State = ObsLogic.State.Empty;
    public DateTimeOffset LastConnected = DateTimeOffset.MinValue;
    public DateTimeOffset Lease = DateTimeOffset.MinValue;
    public volatile bool RetryNow;
    public ClientWebSocket? Socket;
    public readonly SemaphoreSlim SendGate = new(1, 1);
    public readonly ConcurrentDictionary<string, TaskCompletionSource<ObsLogic.Reply>> Pending = new();
    /// <summary>Per input: is it muted (from GetInputMute and InputMuteStateChanged).</summary>
    public readonly ConcurrentDictionary<string, bool> Mutes = new(StringComparer.Ordinal);
    public readonly ConcurrentDictionary<string, bool> MuteAsked = new(StringComparer.Ordinal);
}

/// <summary>
/// The obs-websocket v5 client behind the OBS widget: handshake (Hello, Identify with the salted challenge, Identified),
/// requests and responses, and events that keep the shared state current. One connection per host:port is shared by every
/// widget copy; it reconnects with backoff only while some widget on screen keeps touching it (a lease), and stops after that.
/// The protocol math lives in ObsLogic.cs.
/// </summary>
internal static class ObsClient
{
    private static readonly TimeSpan LeaseLength = TimeSpan.FromSeconds(150);
    private static readonly ConcurrentDictionary<string, ObsConnection> Connections = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, (string Connection, Action Notify)> Listeners = new(StringComparer.Ordinal);

    public const string PasswordName = "obs.password";

    /// <summary>
    /// Marks the connection as wanted for a while and starts it if it isn't running. Cheap; call on every build and tick.
    /// Never blocks: the connection runs on its own task. Returns null if the address is invalid.
    /// </summary>
    public static ObsConnection? Touch(WidgetContext ctx, string host, int port, bool refreshPassword)
    {
        if (ctx.IsSnapshot || ObsLogic.Endpoint(host, port) is not { } uri) return null;
        string key = uri.Authority;
        var connection = Connections.GetOrAdd(key, k => new ObsConnection(k, uri));
        connection.Lease = DateTimeOffset.Now + LeaseLength;
        if (refreshPassword || connection.Link == ObsLink.Idle)
        {
            string? password = ctx.GetSecret(PasswordName);
            if (!string.Equals(password, connection.Password, StringComparison.Ordinal))
            {
                connection.Password = password;
                if (connection.Link is ObsLink.NeedsPassword or ObsLink.WrongPassword) connection.RetryNow = true;
            }
        }
        ctx.RunOnce("obs:" + key, () => Task.Run(() => RunAsync(connection)));
        return connection;
    }

    /// <summary>Redraws the widget when the connection's state changes. One listener per widget item.</summary>
    public static void Listen(WidgetContext ctx, ObsConnection connection)
    {
        if (ctx.IsSnapshot) return;
        string itemId = ctx.Item.Id;
        Listeners[itemId] = (connection.Key, () => ctx.Dispatcher.BeginInvoke(DispatcherPriority.Background, ctx.Rebuild));
    }

    /// <summary>Skip the backoff wait and try now (the user clicked Retry).</summary>
    public static void RetryNow(ObsConnection connection) => connection.RetryNow = true;

    private static void Raise(ObsConnection connection)
    {
        foreach (var (connectionKey, notify) in Listeners.Values)
            if (connectionKey == connection.Key) notify();
    }

    // ---------- Requests ----------

    /// <summary>Sends a request and waits for OBS's answer (5 s). Never throws; failures come back as a reply with Ok = false.</summary>
    public static async Task<ObsLogic.Reply> RequestAsync(ObsConnection connection, string type, object? data = null)
    {
        var failed = new ObsLogic.Reply("", type, false, -1, "OBS isn't connected.", default);
        var socket = connection.Socket;
        if (socket is null || connection.Link != ObsLink.Connected || socket.State != WebSocketState.Open) return failed;
        string id = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<ObsLogic.Reply>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Pending[id] = completion;
        try
        {
            await SendTextAsync(connection, socket, ObsLogic.BuildRequest(type, id, data));
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex) when (ex is TimeoutException or WebSocketException or ObjectDisposedException or InvalidOperationException or OperationCanceledException)
        {
            return failed with { Comment = ex is TimeoutException ? "OBS didn't answer in time." : "OBS isn't connected." };
        }
        finally { connection.Pending.TryRemove(id, out _); }
    }

    /// <summary>Asks OBS for the current recording, streaming, replay buffer and scenes.</summary>
    public static async Task RefreshAsync(ObsConnection connection)
    {
        var record = RequestAsync(connection, "GetRecordStatus");
        var stream = RequestAsync(connection, "GetStreamStatus");
        var scenes = RequestAsync(connection, "GetSceneList");
        var replay = RequestAsync(connection, "GetReplayBufferStatus");
        await Task.WhenAll(record, stream, scenes, replay);
        var now = DateTimeOffset.Now;
        var state = connection.State;
        if ((await record) is { Ok: true } r) state = state with { Record = ObsLogic.ParseRecordStatus(r.Data, now) };
        if ((await stream) is { Ok: true } s) state = state with { Stream = ObsLogic.ParseStreamStatus(s.Data, now) };
        if ((await scenes) is { Ok: true } sc) { var (current, list) = ObsLogic.ParseSceneList(sc.Data); state = state with { Current = current, Scenes = list }; }
        // The replay buffer answers with an error while it is switched off in OBS; that just means "not available".
        state = state with { ReplayActive = (await replay) is { Ok: true } rp ? ObsLogic.ParseActive(rp.Data) : null };
        connection.State = state;
        foreach (string input in connection.MuteAsked.Keys.ToList()) _ = AskMuteAsync(connection, input);
        Raise(connection);
    }

    /// <summary>Learns whether an input is muted. Once per input per connection; later changes arrive as events.</summary>
    public static async Task AskMuteAsync(ObsConnection connection, string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return;
        connection.MuteAsked[input] = true;
        var reply = await RequestAsync(connection, "GetInputMute", new Dictionary<string, object> { ["inputName"] = input });
        if (reply.Ok) { connection.Mutes[input] = ObsLogic.ParseMuted(reply.Data); Raise(connection); }
        else connection.Mutes.TryRemove(input, out _);
    }

    // ---------- Connection loop ----------

    /// <summary>Runs while some widget keeps the lease alive: connect, serve, and reconnect with backoff.</summary>
    private static async Task RunAsync(ObsConnection connection)
    {
        int attempt = 0;
        while (DateTimeOffset.Now < connection.Lease)
        {
            bool reached = false;
            var before = connection.Link;
            try { reached = await ConnectAndServeAsync(connection); }
            catch (Exception ex) when (ex is WebSocketException or IOException or OperationCanceledException or ObjectDisposedException or InvalidOperationException or TimeoutException) { }
            connection.Socket = null;
            foreach (var pending in connection.Pending.Values) pending.TrySetCanceled();
            if (connection.Link is not (ObsLink.NeedsPassword or ObsLink.WrongPassword)) connection.Link = DateTimeOffset.Now < connection.Lease ? ObsLink.Offline : ObsLink.Idle;
            connection.State = connection.State with { Record = ObsLogic.Output.Off, Stream = ObsLogic.Output.Off };
            if (before != connection.Link || reached) Raise(connection);
            if (reached) attempt = 0;
            // Back off 1, 2, 4 … 30 s; a wrong password waits for a new one instead of retrying.
            double wait = connection.Link is ObsLink.NeedsPassword or ObsLink.WrongPassword ? 3600 : Math.Min(30, Math.Pow(2, Math.Min(attempt, 5)));
            attempt++;
            var until = DateTimeOffset.Now.AddSeconds(wait);
            connection.RetryNow = false;
            while (DateTimeOffset.Now < until && DateTimeOffset.Now < connection.Lease && !connection.RetryNow) await Task.Delay(250);
            if (connection.RetryNow) attempt = 0;
        }
        connection.Link = ObsLink.Idle;
    }

    /// <summary>One connection from handshake to close. True if it got as far as being identified.</summary>
    private static async Task<bool> ConnectAndServeAsync(ObsConnection connection)
    {
        connection.Link = ObsLink.Connecting;
        using var socket = new ClientWebSocket();
        socket.Options.AddSubProtocol("obswebsocket.json");
        using (var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(4)))
            await socket.ConnectAsync(connection.Uri, connectTimeout.Token);

        // Hello (op 0) -> Identify (op 1) -> Identified (op 2).
        var hello = await ReceiveMessageAsync(socket, TimeSpan.FromSeconds(5));
        if (hello is not { Op: ObsLogic.OpHello }) return false;
        var info = ObsLogic.ParseHello(hello.Data);
        if (info.AuthRequired && string.IsNullOrEmpty(connection.Password))
        {
            connection.Link = ObsLink.NeedsPassword;
            socket.Abort();
            return false;
        }
        await SendTextAsync(connection, socket, ObsLogic.BuildIdentify(info, connection.Password));
        var identified = await ReceiveMessageAsync(socket, TimeSpan.FromSeconds(5));
        if (identified is not { Op: ObsLogic.OpIdentified })
        {
            // OBS closes with code 4009 when the password is wrong.
            if ((int?)socket.CloseStatus == ObsLogic.CloseAuthenticationFailed || info.AuthRequired) connection.Link = ObsLink.WrongPassword;
            socket.Abort();
            return false;
        }

        connection.Socket = socket;
        connection.Link = ObsLink.Connected;
        connection.LastConnected = DateTimeOffset.Now;
        connection.Mutes.Clear();
        _ = RefreshAsync(connection);
        Raise(connection);

        var nextPoll = DateTimeOffset.Now.AddSeconds(30);
        Task<ObsLogic.Message?>? receiving = null;
        while (socket.State == WebSocketState.Open && DateTimeOffset.Now < connection.Lease)
        {
            receiving ??= ReceiveMessageAsync(socket, null);
            var finished = await Task.WhenAny(receiving, Task.Delay(1000));
            if (finished == receiving)
            {
                var message = await receiving;
                receiving = null;
                if (message is null) break;
                Handle(connection, message);
            }
            // Events keep the state current; a slow poll corrects any drift (and sees changes made while no event arrived).
            if (DateTimeOffset.Now >= nextPoll) { nextPoll = DateTimeOffset.Now.AddSeconds(30); _ = RefreshAsync(connection); }
        }
        if (socket.State == WebSocketState.Open)
        {
            try { using var closing = new CancellationTokenSource(TimeSpan.FromSeconds(1)); await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", closing.Token); }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException) { }
        }
        return true;
    }

    private static void Handle(ObsConnection connection, ObsLogic.Message message)
    {
        if (message.Op == ObsLogic.OpResponse)
        {
            var reply = ObsLogic.ParseReply(message.Data);
            if (connection.Pending.TryRemove(reply.RequestId, out var completion)) completion.TrySetResult(reply);
        }
        else if (message.Op == ObsLogic.OpEvent)
        {
            string type = message.Data.TryGetProperty("eventType", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "";
            if (!message.Data.TryGetProperty("eventData", out var data)) data = default;
            if (type == "InputMuteStateChanged" && data.ValueKind == JsonValueKind.Object && data.TryGetProperty("inputName", out var name) && name.GetString() is { Length: > 0 } input)
                connection.Mutes[input] = ObsLogic.ParseMuted(data);
            else if (data.ValueKind == JsonValueKind.Object)
                connection.State = ObsLogic.Apply(connection.State, type, data, DateTimeOffset.Now);
            Raise(connection);
        }
    }

    private static async Task SendTextAsync(ObsConnection connection, ClientWebSocket socket, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        await connection.SendGate.WaitAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, timeout.Token);
        }
        finally { connection.SendGate.Release(); }
    }

    /// <summary>Reads one whole text message; null when the socket closes. With a timeout, throws <see cref="TimeoutException"/> if nothing arrives.</summary>
    private static async Task<ObsLogic.Message?> ReceiveMessageAsync(ClientWebSocket socket, TimeSpan? timeout)
    {
        using var cancel = timeout is { } t ? new CancellationTokenSource(t) : new CancellationTokenSource();
        var buffer = new byte[16 * 1024];
        using var stream = new MemoryStream();
        try
        {
            while (true)
            {
                var result = await socket.ReceiveAsync(buffer, cancel.Token);
                if (result.MessageType == WebSocketMessageType.Close) return null;
                stream.Write(buffer, 0, result.Count);
                if (stream.Length > 4 * 1024 * 1024) throw new InvalidOperationException("OBS sent too much data.");
                if (result.EndOfMessage) break;
            }
        }
        catch (OperationCanceledException) when (timeout is not null) { throw new TimeoutException(); }
        catch (Exception ex) when (timeout is null && ex is WebSocketException or ObjectDisposedException or OperationCanceledException) { return null; }
        return ObsLogic.ParseMessage(Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length));
    }
}
