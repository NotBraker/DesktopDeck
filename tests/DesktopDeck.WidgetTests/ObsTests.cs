using System.Text.Json;
using DesktopDeck.Widgets;

public static class ObsTests
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    public static void AuthStringMatchesTheProtocolDocsExample()
    {
        // The worked example from the obs-websocket v5 protocol documentation.
        Expect.Equal("1Ct943GAT+6YQUUX47Ia/ncufilbe6+oD6lY+5kaCu4=",
            ObsLogic.AuthString("supersecretpassword", "lM1GncleQOaCu9lT1yeUZhFYnqhsLLP1G5lAGo3ixaI=", "+IxH4CnCiqpX1rM9scsNynZzbOe4KhDeYcTNS3PDaeY="), "auth string");
    }

    public static void HelloWithAndWithoutAuthentication()
    {
        var open = ObsLogic.ParseHello(Json("""{"obsWebSocketVersion":"5.5.0","rpcVersion":1}"""));
        Expect.True(!open.AuthRequired, "no auth block means no password");
        var locked = ObsLogic.ParseHello(Json("""{"rpcVersion":1,"authentication":{"challenge":"c","salt":"s"}}"""));
        Expect.True(locked.AuthRequired && locked.Challenge == "c" && locked.Salt == "s", "challenge and salt read");
    }

    public static void IdentifyCarriesAuthenticationOnlyWhenAsked()
    {
        using var plain = JsonDocument.Parse(ObsLogic.BuildIdentify(new ObsLogic.Hello(1, false, "", ""), "ignored"));
        var d = plain.RootElement.GetProperty("d");
        Expect.Equal(1, plain.RootElement.GetProperty("op").GetInt32(), "op 1");
        Expect.True(!d.TryGetProperty("authentication", out _), "no authentication when not required");
        Expect.Equal(ObsLogic.EventSubscriptions, d.GetProperty("eventSubscriptions").GetInt32(), "subscriptions");
        using var secured = JsonDocument.Parse(ObsLogic.BuildIdentify(new ObsLogic.Hello(1, true, "challenge", "salt"), "pw"));
        Expect.Equal(ObsLogic.AuthString("pw", "salt", "challenge"), secured.RootElement.GetProperty("d").GetProperty("authentication").GetString()!, "authentication value");
        Expect.True((ObsLogic.EventSubscriptions & 4) != 0 && (ObsLogic.EventSubscriptions & 64) != 0, "scenes and outputs");
    }

    public static void RequestsAndRepliesRoundTrip()
    {
        using var request = JsonDocument.Parse(ObsLogic.BuildRequest("SetCurrentProgramScene", "id1", new Dictionary<string, object> { ["sceneName"] = "Gameplay" }));
        var d = request.RootElement.GetProperty("d");
        Expect.Equal(6, request.RootElement.GetProperty("op").GetInt32(), "op 6");
        Expect.Equal("Gameplay", d.GetProperty("requestData").GetProperty("sceneName").GetString()!, "request data");
        var reply = ObsLogic.ParseReply(Json("""{"requestType":"GetInputMute","requestId":"id1","requestStatus":{"result":true,"code":100},"responseData":{"inputMuted":true}}"""));
        Expect.True(reply.Ok && reply.RequestId == "id1" && ObsLogic.ParseMuted(reply.Data), "ok reply");
        var failed = ObsLogic.ParseReply(Json("""{"requestType":"GetInputMute","requestId":"id2","requestStatus":{"result":false,"code":600,"comment":"No input was found."}}"""));
        Expect.True(!failed.Ok && failed.Code == 600, "failed reply");
        Expect.True(ObsLogic.Describe("GetInputMute", 600, failed.Comment, "Mic/Aux").Contains("Mic/Aux", StringComparison.Ordinal), "friendly input error");
    }

    public static void GarbageMessagesAreIgnored()
    {
        Expect.True(ObsLogic.ParseMessage("not json") is null, "not json");
        Expect.True(ObsLogic.ParseMessage("[1,2]") is null, "array");
        Expect.True(ObsLogic.ParseMessage("""{"d":{}}""") is null, "no op");
        Expect.Equal(5, ObsLogic.ParseMessage("""{"op":5,"d":{"eventType":"x"}}""")!.Op, "op read");
    }

    public static void RecordStatusAndElapsed()
    {
        var now = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
        var active = ObsLogic.ParseRecordStatus(Json("""{"outputActive":true,"outputPaused":false,"outputTimecode":"00:01:30.500","outputDuration":90500,"outputBytes":1}"""), now);
        Expect.True(active.Active && !active.Paused, "active");
        Expect.Near(90.5, active.Elapsed(now).TotalSeconds, 1e-6, "duration at the time of the answer");
        Expect.Near(100.5, active.Elapsed(now.AddSeconds(10)).TotalSeconds, 1e-6, "timer keeps running");
        var paused = ObsLogic.ParseRecordStatus(Json("""{"outputActive":true,"outputPaused":true,"outputDuration":5000}"""), now);
        Expect.Near(5, paused.Elapsed(now.AddMinutes(5)).TotalSeconds, 1e-6, "paused time is frozen");
        var off = ObsLogic.ParseRecordStatus(Json("""{"outputActive":false,"outputDuration":5000}"""), now);
        Expect.Equal(TimeSpan.Zero, off.Elapsed(now.AddHours(1)), "inactive is zero");
        var fromTimecode = ObsLogic.ParseStreamStatus(Json("""{"outputActive":true,"outputReconnecting":true,"outputTimecode":"01:02:03.000"}"""), now);
        Expect.Near(3723, fromTimecode.Elapsed(now).TotalSeconds, 1e-6, "timecode fallback");
        Expect.True(fromTimecode.Reconnecting, "reconnecting");
    }

    public static void SceneListIsShownInObsOrder()
    {
        var (current, scenes) = ObsLogic.ParseSceneList(Json("""{"currentProgramSceneName":"B","scenes":[{"sceneName":"A","sceneIndex":0},{"sceneName":"B","sceneIndex":1},{"sceneName":"C","sceneIndex":2}]}"""));
        Expect.Equal("B", current, "current scene");
        Expect.Equal("C,B,A", string.Join(",", scenes), "highest index first, as OBS draws them");
        Expect.Equal("C,A", string.Join(",", ObsLogic.VisibleScenes(scenes, ["b"], 5)), "hidden scenes skipped, case-insensitive");
        Expect.Equal(1, ObsLogic.VisibleScenes(scenes, [], 1).Count, "limited");
    }

    public static void EventsChangeTheState()
    {
        var t0 = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
        var state = ObsLogic.State.Empty;
        state = ObsLogic.Apply(state, "RecordStateChanged", Json("""{"outputActive":true,"outputState":"OBS_WEBSOCKET_OUTPUT_STARTED"}"""), t0);
        Expect.True(state.Record.Active, "recording started");
        Expect.Near(60, state.Record.Elapsed(t0.AddSeconds(60)).TotalSeconds, 1e-6, "timer counts from the start event");
        state = ObsLogic.Apply(state, "RecordStateChanged", Json("""{"outputActive":true,"outputState":"OBS_WEBSOCKET_OUTPUT_PAUSED"}"""), t0.AddSeconds(60));
        Expect.True(state.Record.Paused, "paused");
        Expect.Near(60, state.Record.Elapsed(t0.AddSeconds(500)).TotalSeconds, 1e-6, "pause freezes the timer");
        state = ObsLogic.Apply(state, "RecordStateChanged", Json("""{"outputActive":true,"outputState":"OBS_WEBSOCKET_OUTPUT_RESUMED"}"""), t0.AddSeconds(100));
        Expect.Near(70, state.Record.Elapsed(t0.AddSeconds(110)).TotalSeconds, 1e-6, "resume continues from the paused time");
        state = ObsLogic.Apply(state, "RecordStateChanged", Json("""{"outputActive":false,"outputState":"OBS_WEBSOCKET_OUTPUT_STOPPED"}"""), t0.AddSeconds(120));
        Expect.True(!state.Record.Active, "stopped");
        state = ObsLogic.Apply(state, "StreamStateChanged", Json("""{"outputActive":true,"outputState":"OBS_WEBSOCKET_OUTPUT_STARTED"}"""), t0);
        state = ObsLogic.Apply(state, "StreamStateChanged", Json("""{"outputActive":true,"outputState":"OBS_WEBSOCKET_OUTPUT_RECONNECTING"}"""), t0.AddSeconds(30));
        Expect.True(state.Stream.Reconnecting && state.Stream.Active, "reconnecting stream");
        state = ObsLogic.Apply(state, "CurrentProgramSceneChanged", Json("""{"sceneName":"Intro"}"""), t0);
        Expect.Equal("Intro", state.Current, "scene changed");
        state = ObsLogic.Apply(state, "SceneListChanged", Json("""{"scenes":[{"sceneName":"X","sceneIndex":0},{"sceneName":"Y","sceneIndex":1}]}"""), t0);
        Expect.Equal("Y,X", string.Join(",", state.Scenes), "scene list replaced");
        Expect.Equal(state, ObsLogic.Apply(state, "SomethingElse", Json("{}"), t0), "unknown events change nothing");
    }

    public static void EndpointParsing()
    {
        Expect.Equal("ws://127.0.0.1:4455/", ObsLogic.Endpoint("", 4455)!.ToString(), "default host");
        Expect.Equal("ws://192.168.1.5:4466/", ObsLogic.Endpoint("ws://192.168.1.5:4466/", 4455)!.ToString(), "scheme and port in the host box");
        Expect.Equal("ws://studio-pc:4455/", ObsLogic.Endpoint("  studio-pc ", 4455)!.ToString(), "trimmed");
        Expect.True(ObsLogic.Endpoint("bad host!", 4455) is null, "invalid characters");
        Expect.True(ObsLogic.Endpoint("localhost", 0) is null, "invalid port");
    }

    public static void CacheKeepsScenesButNotStaleOutputs()
    {
        var now = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
        var state = new ObsLogic.State(new ObsLogic.Output(true, false, false, false, TimeSpan.FromMinutes(5), now), ObsLogic.Output.Off, true, "B", ["A", "B"]);
        string json = ObsLogic.ToCache(state, true, now);
        var (offline, muted) = ObsLogic.FromCache(json, now, live: false);
        Expect.Equal("A,B", string.Join(",", offline.Scenes), "scenes cached");
        Expect.True(!offline.Record.Active && muted == true && offline.Current == "B", "offline view keeps scenes, mic and current scene but not the recording");
        var (snapshot, _) = ObsLogic.FromCache(json, now, live: true);
        Expect.Near(300, snapshot.Record.Elapsed(now).TotalSeconds, 1e-6, "snapshots keep the recording");
        Expect.Equal(ObsLogic.State.Empty, ObsLogic.FromCache("garbage", now, false).State, "bad cache is empty");
    }

    public static void SharedHelpers()
    {
        Expect.Equal("01:05", ConnectedLogic.Duration(TimeSpan.FromSeconds(65)), "mm:ss");
        Expect.Equal("1:01:01", ConnectedLogic.Duration(TimeSpan.FromSeconds(3661)), "h:mm:ss");
        Expect.Equal("00:00", ConnectedLogic.Duration(TimeSpan.FromSeconds(-5)), "negative is zero");
        Expect.Equal("50+", ConnectedLogic.Count(50, 50), "capped count");
        Expect.Equal("OC", ConnectedLogic.Initials("octo cat"), "two words");
        Expect.Equal("M", ConnectedLogic.Initials("monalisa"), "one word");
        Expect.True(ConnectedLogic.IsAvatarUrl("https://avatars.githubusercontent.com/u/1?v=4"), "known image hosts");
        Expect.True(!ConnectedLogic.IsAvatarUrl("http://avatars.githubusercontent.com/u/1") && !ConnectedLogic.IsAvatarUrl("https://evil.example/a.png") && !ConnectedLogic.IsAvatarUrl("file:///C:/a.png"), "other addresses are never fetched");
        Expect.True(ConnectedLogic.IsLinkOn("https://example.com/invite/abc", "example.com") && !ConnectedLogic.IsLinkOn("https://notexample.com/x", "example.com") && !ConnectedLogic.IsLinkOn("javascript:alert(1)", "example.com"), "link allow-list");
    }
}
