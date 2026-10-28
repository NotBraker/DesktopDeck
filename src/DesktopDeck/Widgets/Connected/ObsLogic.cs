using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopDeck.Widgets;

/// <summary>
/// obs-websocket v5 protocol math for the OBS widget: the authentication string, message building and parsing,
/// status parsing and how events change the known state. Pure logic (unit tested, no network).
/// Protocol: https://github.com/obsproject/obs-websocket/blob/master/docs/generated/protocol.md
/// </summary>
public static class ObsLogic
{
    public const int DefaultPort = 4455;
    public const int RpcVersion = 1;
    /// <summary>Event categories to subscribe to: Scenes (4), Inputs (8) and Outputs (64). High-volume events are never requested.</summary>
    public const int EventSubscriptions = 4 | 8 | 64;

    public const int OpHello = 0, OpIdentify = 1, OpIdentified = 2, OpReidentify = 3, OpEvent = 5, OpRequest = 6, OpResponse = 7;
    /// <summary>The close code OBS uses when the password is wrong.</summary>
    public const int CloseAuthenticationFailed = 4009;

    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();

    // ---------- Handshake ----------

    /// <summary>
    /// The "authentication" value for Identify: base64(SHA256(base64(SHA256(password + salt)) + challenge)).
    /// </summary>
    public static string AuthString(string password, string salt, string challenge)
    {
        string secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }

    public sealed record Hello(int RpcVersion, bool AuthRequired, string Challenge, string Salt);

    /// <summary>Reads the "d" part of the Hello message (op 0).</summary>
    public static Hello ParseHello(JsonElement data)
    {
        int rpc = data.TryGetProperty("rpcVersion", out var r) && r.TryGetInt32(out int v) ? v : 1;
        if (data.TryGetProperty("authentication", out var auth) && auth.ValueKind == JsonValueKind.Object)
            return new Hello(rpc, true, Str(auth, "challenge"), Str(auth, "salt"));
        return new Hello(rpc, false, "", "");
    }

    /// <summary>The Identify message (op 1). Pass a password only when the Hello asked for authentication.</summary>
    public static string BuildIdentify(Hello hello, string? password)
    {
        var data = new Dictionary<string, object> { ["rpcVersion"] = Math.Min(RpcVersion, Math.Max(1, hello.RpcVersion)), ["eventSubscriptions"] = EventSubscriptions };
        if (hello.AuthRequired) data["authentication"] = AuthString(password ?? "", hello.Salt, hello.Challenge);
        return JsonSerializer.Serialize(new Dictionary<string, object> { ["op"] = OpIdentify, ["d"] = data });
    }

    /// <summary>A request message (op 6).</summary>
    public static string BuildRequest(string type, string id, object? data = null)
    {
        var d = new Dictionary<string, object> { ["requestType"] = type, ["requestId"] = id };
        if (data is not null) d["requestData"] = data;
        return JsonSerializer.Serialize(new Dictionary<string, object> { ["op"] = OpRequest, ["d"] = d });
    }

    /// <summary>One message from OBS: its op code and the "d" payload (an empty object if missing).</summary>
    public sealed record Message(int Op, JsonElement Data);

    /// <summary>Null if the text isn't a v5 message.</summary>
    public static Message? ParseMessage(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("op", out var op) || !op.TryGetInt32(out int code)) return null;
            JsonElement data = root.TryGetProperty("d", out var d) ? d.Clone() : EmptyObject;
            return new Message(code, data);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>The outcome of a request (op 7).</summary>
    public sealed record Reply(string RequestId, string RequestType, bool Ok, int Code, string? Comment, JsonElement Data);

    public static Reply ParseReply(JsonElement d)
    {
        bool ok = false;
        int code = 0;
        string? comment = null;
        if (d.TryGetProperty("requestStatus", out var status) && status.ValueKind == JsonValueKind.Object)
        {
            ok = status.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.True;
            code = status.TryGetProperty("code", out var c) && c.TryGetInt32(out int n) ? n : 0;
            comment = status.TryGetProperty("comment", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
        }
        var data = d.TryGetProperty("responseData", out var rd) ? rd.Clone() : EmptyObject;
        return new Reply(Str(d, "requestId"), Str(d, "requestType"), ok, code, comment, data);
    }

    /// <summary>A short, human wording for a failed request.</summary>
    public static string Describe(string requestType, int code, string? comment, string? inputName = null)
    {
        if (code == 600 && requestType.Contains("Input", StringComparison.Ordinal) && !string.IsNullOrEmpty(inputName))
            return $"OBS has no input called \"{inputName}\". Check the name in Customize.";
        if (code == 207) return "OBS isn't ready yet — try again in a moment.";
        if (code == 500 || code == 501 || code == 503)
            return string.IsNullOrWhiteSpace(comment) ? "OBS refused that (it may already be in that state)." : "OBS: " + comment.Trim().TrimEnd('.') + ".";
        return string.IsNullOrWhiteSpace(comment) ? $"OBS couldn't do that (code {code})." : "OBS: " + comment.Trim().TrimEnd('.') + ".";
    }

    // ---------- Address ----------

    /// <summary>ws://host:port, or null if the host is empty or has characters a host can't have.</summary>
    public static Uri? Endpoint(string? host, int port)
    {
        host = (host ?? "").Trim();
        foreach (string prefix in new[] { "ws://", "wss://", "http://", "https://" })
            if (host.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) host = host[prefix.Length..];
        int slash = host.IndexOf('/');
        if (slash >= 0) host = host[..slash];
        if (host.Length == 0) host = "127.0.0.1";
        // "host:port" typed into the host box wins over the port option.
        int colon = host.LastIndexOf(':');
        if (colon > 0 && !host.Contains(']') && host.IndexOf(':') == colon && int.TryParse(host[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int typed)) { port = typed; host = host[..colon]; }
        if (port is < 1 or > 65535) return null;
        if (host.Any(c => !(char.IsLetterOrDigit(c) || c is '.' or '-' or ':' or '[' or ']' or '_'))) return null;
        if (host.Contains(':') && !host.StartsWith('[')) host = "[" + host + "]";
        return Uri.TryCreate($"ws://{host}:{port.ToString(CultureInfo.InvariantCulture)}", UriKind.Absolute, out var uri) ? uri : null;
    }

    // ---------- State ----------

    /// <summary>The state of one output (recording or streaming) as of <see cref="AsOf"/>.</summary>
    public sealed record Output(bool Active, bool Paused, bool Reconnecting, bool Pending, TimeSpan Duration, DateTimeOffset AsOf)
    {
        public static readonly Output Off = new(false, false, false, false, TimeSpan.Zero, DateTimeOffset.MinValue);

        /// <summary>How long it has been running: frozen while paused, zero when off.</summary>
        public TimeSpan Elapsed(DateTimeOffset now)
            => !Active ? TimeSpan.Zero : Paused ? Duration : Duration + (now > AsOf ? now - AsOf : TimeSpan.Zero);
    }

    /// <summary>What is known about OBS.</summary>
    public sealed record State(Output Record, Output Stream, bool? ReplayActive, string Current, IReadOnlyList<string> Scenes)
    {
        public static readonly State Empty = new(Output.Off, Output.Off, null, "", []);
    }

    public static Output ParseRecordStatus(JsonElement d, DateTimeOffset now)
    {
        bool active = Bool(d, "outputActive");
        return new Output(active, active && Bool(d, "outputPaused"), false, false, active ? ReadDuration(d) : TimeSpan.Zero, now);
    }

    public static Output ParseStreamStatus(JsonElement d, DateTimeOffset now)
    {
        bool active = Bool(d, "outputActive");
        return new Output(active, false, active && Bool(d, "outputReconnecting"), false, active ? ReadDuration(d) : TimeSpan.Zero, now);
    }

    private static TimeSpan ReadDuration(JsonElement d)
    {
        if (d.TryGetProperty("outputDuration", out var ms) && ms.ValueKind == JsonValueKind.Number && ms.TryGetDouble(out double millis) && double.IsFinite(millis) && millis >= 0)
            return TimeSpan.FromMilliseconds(Math.Min(millis, TimeSpan.FromDays(30).TotalMilliseconds));
        return ParseTimecode(Str(d, "outputTimecode"));
    }

    /// <summary>"01:02:03.456" to a time span (zero if it can't be read).</summary>
    public static TimeSpan ParseTimecode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return TimeSpan.Zero;
        var parts = text.Trim().Split(':');
        if (parts.Length != 3) return TimeSpan.Zero;
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int h) || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int m)
            || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double s) || !double.IsFinite(s)) return TimeSpan.Zero;
        return TimeSpan.FromSeconds(h * 3600.0 + m * 60.0 + s);
    }

    /// <summary>Scene names in the order OBS shows them (the API lists them bottom-up, so the highest index comes first).</summary>
    public static (string Current, List<string> Scenes) ParseSceneList(JsonElement d)
    {
        var scenes = new List<(string Name, int Index)>();
        if (d.TryGetProperty("scenes", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var scene in list.EnumerateArray())
            {
                string name = Str(scene, "sceneName");
                if (name.Length == 0) continue;
                scenes.Add((name, scene.TryGetProperty("sceneIndex", out var i) && i.TryGetInt32(out int index) ? index : scenes.Count));
            }
        return (Str(d, "currentProgramSceneName"), scenes.OrderByDescending(s => s.Index).Select(s => s.Name).ToList());
    }

    public static bool ParseMuted(JsonElement d) => Bool(d, "inputMuted");

    /// <summary>The "outputActive" flag of a status answer (replay buffer, virtual camera…).</summary>
    public static bool ParseActive(JsonElement d) => Bool(d, "outputActive");

    /// <summary>The new state after an OBS event. Muting events are handled by the caller (they are per input).</summary>
    public static State Apply(State state, string eventType, JsonElement data, DateTimeOffset now)
    {
        switch (eventType)
        {
            case "RecordStateChanged":
                return state with { Record = Transition(state.Record, Str(data, "outputState"), Bool(data, "outputActive"), now) };
            case "StreamStateChanged":
                return state with { Stream = Transition(state.Stream, Str(data, "outputState"), Bool(data, "outputActive"), now) };
            case "ReplayBufferStateChanged":
                return state with { ReplayActive = Bool(data, "outputActive") };
            case "CurrentProgramSceneChanged":
                return Str(data, "sceneName") is { Length: > 0 } scene ? state with { Current = scene } : state;
            case "SceneListChanged":
                var (_, scenes) = ParseSceneList(data);
                return state with { Scenes = scenes };
            default:
                return state;
        }
    }

    private static Output Transition(Output was, string outputState, bool active, DateTimeOffset now)
    {
        switch (outputState)
        {
            case "OBS_WEBSOCKET_OUTPUT_STARTING": return was with { Pending = true };
            case "OBS_WEBSOCKET_OUTPUT_STARTED": return new Output(true, false, false, false, TimeSpan.Zero, now);
            case "OBS_WEBSOCKET_OUTPUT_STOPPING": return was with { Pending = true };
            case "OBS_WEBSOCKET_OUTPUT_STOPPED": return Output.Off with { AsOf = now };
            case "OBS_WEBSOCKET_OUTPUT_PAUSED": return was with { Active = true, Paused = true, Pending = false, Duration = was.Elapsed(now), AsOf = now };
            case "OBS_WEBSOCKET_OUTPUT_RESUMED": return was with { Active = true, Paused = false, Pending = false, AsOf = now };
            case "OBS_WEBSOCKET_OUTPUT_RECONNECTING": return was with { Active = true, Reconnecting = true, Pending = false, Duration = was.Elapsed(now), AsOf = now };
            case "OBS_WEBSOCKET_OUTPUT_RECONNECTED": return was with { Active = true, Reconnecting = false, Pending = false, AsOf = now };
            default: return active ? (was.Active ? was : new Output(true, false, false, false, TimeSpan.Zero, now)) : Output.Off with { AsOf = now };
        }
    }

    /// <summary>The scenes to show: skips hidden names (case-insensitive) and limits the count.</summary>
    public static List<string> VisibleScenes(IEnumerable<string> scenes, IEnumerable<string> hidden, int max)
    {
        var hide = new HashSet<string>(hidden.Select(h => h.Trim()), StringComparer.OrdinalIgnoreCase);
        return scenes.Where(s => !hide.Contains(s)).Take(Math.Max(0, max)).ToList();
    }

    // ---------- Cache (what the widget shows before OBS answers) ----------

    private sealed record CacheDto(string Current, List<string> Scenes, bool? MicMuted, bool? ReplayActive, bool Recording, bool RecordingPaused, double RecordSeconds, bool Streaming, double StreamSeconds);

    /// <summary>The scene list and last known controls, small enough to keep in the widget's settings.</summary>
    public static string ToCache(State state, bool? micMuted, DateTimeOffset now)
        => JsonSerializer.Serialize(new CacheDto(state.Current, state.Scenes.Take(40).ToList(), micMuted, state.ReplayActive,
            state.Record.Active, state.Record.Paused, state.Record.Elapsed(now).TotalSeconds, state.Stream.Active, state.Stream.Elapsed(now).TotalSeconds));

    /// <summary>Reads the cache; <paramref name="live"/> keeps the recording/streaming flags (only snapshots do — real data is stale offline).</summary>
    public static (State State, bool? MicMuted) FromCache(string? json, DateTimeOffset now, bool live)
    {
        if (string.IsNullOrWhiteSpace(json)) return (State.Empty, null);
        try
        {
            var dto = JsonSerializer.Deserialize<CacheDto>(json);
            if (dto is null) return (State.Empty, null);
            var scenes = (dto.Scenes ?? []).Where(s => !string.IsNullOrEmpty(s)).ToList();
            var record = live && dto.Recording ? new Output(true, dto.RecordingPaused, false, false, TimeSpan.FromSeconds(Math.Max(0, dto.RecordSeconds)), now) : Output.Off;
            var stream = live && dto.Streaming ? new Output(true, false, false, false, TimeSpan.FromSeconds(Math.Max(0, dto.StreamSeconds)), now) : Output.Off;
            return (new State(record, stream, dto.ReplayActive, dto.Current ?? "", scenes), dto.MicMuted);
        }
        catch (JsonException) { return (State.Empty, null); }
    }

    // ---------- JSON helpers ----------

    private static string Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
    private static bool Bool(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
}
