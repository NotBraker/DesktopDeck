using Windows.Media.Control;
using Windows.Storage.Streams;

namespace DesktopDeck;

/// <summary>
/// What's playing anywhere on the PC (Spotify, browsers, Media Player…) through Windows' media session API, with
/// play/pause and skip. Event-driven: nothing polls. Started the first time a media widget is shown.
/// </summary>
internal sealed class MediaService
{
    public static MediaService Instance { get; } = new();

    public sealed record Snapshot(bool HasSession, string Title, string Artist, string App, bool Playing, TimeSpan Position, TimeSpan Duration, DateTimeOffset PositionAt, byte[]? Art)
    {
        public static readonly Snapshot None = new(false, "", "", "", false, TimeSpan.Zero, TimeSpan.Zero, DateTimeOffset.Now, null);

        /// <summary>The position now, moved forward by the time since the app last reported it while playing.</summary>
        public TimeSpan PositionNow(DateTimeOffset now)
        {
            var position = Playing ? Position + (now - PositionAt) : Position;
            return Duration > TimeSpan.Zero && position > Duration ? Duration : position < TimeSpan.Zero ? TimeSpan.Zero : position;
        }
    }

    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private GlobalSystemMediaTransportControlsSession? session;
    private Task? starting;
    private string artKey = "";
    private byte[]? art;
    public Snapshot Current { get; private set; } = Snapshot.None;
    public string? Problem { get; private set; }
    /// <summary>Raised (on a background thread) whenever the track, play state or position changes.</summary>
    public event Action? Changed;

    private bool inert;

    public Task EnsureStartedAsync() => inert ? Task.CompletedTask : starting ??= StartAsync();

    /// <summary>Smoke tests and screenshots: never reads the PC's media session, and shows <paramref name="snapshot"/> (or nothing playing) instead.</summary>
    public void UseSample(Snapshot snapshot)
    {
        inert = true;
        Current = snapshot;
        Changed?.Invoke();
    }

    private async Task StartAsync()
    {
        try
        {
            manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            manager.CurrentSessionChanged += (_, _) => _ = AttachAsync();
            await AttachAsync();
        }
        catch (Exception ex)
        {
            Problem = "Windows media controls aren't available: " + ex.Message;
            Changed?.Invoke();
        }
    }

    private async Task AttachAsync()
    {
        if (session is not null)
        {
            session.MediaPropertiesChanged -= OnMediaChanged;
            session.PlaybackInfoChanged -= OnPlaybackChanged;
            session.TimelinePropertiesChanged -= OnTimelineChanged;
        }
        session = manager?.GetCurrentSession();
        if (session is not null)
        {
            session.MediaPropertiesChanged += OnMediaChanged;
            session.PlaybackInfoChanged += OnPlaybackChanged;
            session.TimelinePropertiesChanged += OnTimelineChanged;
        }
        await RefreshAsync();
    }

    private void OnMediaChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) => _ = RefreshAsync();
    private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) => _ = RefreshAsync();
    private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        var current = session;
        if (current is null) { Current = Snapshot.None; Changed?.Invoke(); return; }
        try
        {
            var props = await current.TryGetMediaPropertiesAsync();
            var playback = current.GetPlaybackInfo();
            var timeline = current.GetTimelineProperties();
            string key = props.Title + "\n" + props.Artist + "\n" + props.AlbumTitle;
            if (key != artKey)
            {
                artKey = key;
                art = props.Thumbnail is null ? null : await ReadAsync(props.Thumbnail);
            }
            var duration = timeline.EndTime - timeline.StartTime;
            Current = new Snapshot(true, props.Title ?? "", string.IsNullOrWhiteSpace(props.Artist) ? props.AlbumArtist ?? "" : props.Artist, AppName(current.SourceAppUserModelId),
                playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                timeline.Position, duration > TimeSpan.Zero ? duration : TimeSpan.Zero, timeline.LastUpdatedTime, art);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            Current = Snapshot.None;
        }
        Changed?.Invoke();
    }

    private static async Task<byte[]?> ReadAsync(IRandomAccessStreamReference reference)
    {
        try
        {
            using var stream = await reference.OpenReadAsync();
            if (stream.Size == 0 || stream.Size > 8_000_000) return null;
            using var input = stream.AsStreamForRead();
            using var copy = new MemoryStream();
            await input.CopyToAsync(copy);
            return copy.ToArray();
        }
        catch (Exception ex) when (ex is IOException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>"Spotify", "Chrome", "Edge" from an app id like "Spotify.exe" or "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify".</summary>
    public static string AppName(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "";
        string name = id.Contains('!') ? id[(id.LastIndexOf('!') + 1)..] : id;
        name = Path.GetFileNameWithoutExtension(name);
        if (name.Contains('.')) name = name[(name.LastIndexOf('.') + 1)..];
        return name.ToLowerInvariant() switch
        {
            "msedge" => "Edge",
            "chrome" => "Chrome",
            "firefox" => "Firefox",
            "app" => "Music",
            "zunemusic" or "microsoft.zunemusic" => "Media Player",
            _ => name.Length > 0 ? char.ToUpperInvariant(name[0]) + name[1..] : name,
        };
    }

    public async Task TogglePlayPauseAsync() { if (session is not null) try { await session.TryTogglePlayPauseAsync(); } catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { } }
    public async Task NextAsync() { if (session is not null) try { await session.TrySkipNextAsync(); } catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { } }
    public async Task PreviousAsync() { if (session is not null) try { await session.TrySkipPreviousAsync(); } catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { } }
}
