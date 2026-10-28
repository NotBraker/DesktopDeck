using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using DesktopDeck.Models;

namespace DesktopDeck.Widgets;

/// <summary>What a Connected widget downloads: a signature of the request (settings that change the answer), how often,
/// and the work that fetches and trims the answer into the text stored in "cache.json". <see cref="Strict"/> hides
/// cached data made with another signature (another database, another server); otherwise it stays on screen while the new request runs.</summary>
internal sealed record ConnectedSource(string Signature, TimeSpan Interval, Func<Task<string>> Fetch, bool Strict = true);

/// <summary>A failed request with wording that is already fit to show.</summary>
internal sealed class ConnectedException(string message, int? status = null, bool auth = false) : Exception(message)
{
    public int? Status { get; } = status;
    /// <summary>The token was rejected or lacks permission: say so instead of "Offline".</summary>
    public bool Auth { get; } = auth;
}

/// <summary>An HTTP answer: status, body and the few headers the Connected widgets use.</summary>
internal sealed record ConnectedReply(int Status, string Body, string? ETag, string? LastModified, string? RateRemaining)
{
    public bool Ok => Status is >= 200 and < 300;
}

/// <summary>
/// Shared plumbing for the Connected widgets (GitHub, Notion): one HttpClient, the refresh policy (only while on
/// screen, at the user's interval, never in snapshots), the cache in Item.Options ("cache.json", "cache.time", "cache.key",
/// "cache.error", "cache.errorKind"), the quiet footer ("Offline — showing 10:42 data"), setup states, and avatars.
/// Tokens are never stored here: they live in the secrets store and are read when a request is made.
/// </summary>
internal static class ConnectedKit
{
    public const string CacheJson = "cache.json", CacheTime = "cache.time", CacheKey = "cache.key", CacheError = "cache.error", CacheErrorKind = "cache.errorKind";

    public static readonly HttpClient Http = CreateClient();
    private static readonly ConditionalWeakTable<WidgetContext, ConnectedSource> Sources = new();
    private static readonly ConcurrentDictionary<string, (DateTimeOffset At, string Signature)> Attempts = new();
    private static readonly ConcurrentDictionary<string, (string? ETag, string? LastModified, string Body)> Conditional = new();

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All, PooledConnectionLifetime = TimeSpan.FromMinutes(15) };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 8 * 1024 * 1024 };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DesktopDeck/0.6");
        return client;
    }

    // ---------- Requests ----------

    /// <summary>Sends a request and returns the answer whatever its status (only network failures and timeouts throw).</summary>
    public static async Task<ConnectedReply> SendAsync(HttpRequestMessage request, CancellationToken cancel = default)
    {
        using (request)
        using (var response = await Http.SendAsync(request, cancel))
        {
            string body = response.StatusCode == HttpStatusCode.NotModified ? "" : await response.Content.ReadAsStringAsync(cancel);
            return new ConnectedReply((int)response.StatusCode, body, response.Headers.ETag?.ToString(),
                response.Content.Headers.LastModified?.ToString("R"), response.Headers.TryGetValues("x-ratelimit-remaining", out var left) ? left.FirstOrDefault() : null);
        }
    }

    /// <summary>
    /// A GET that remembers the ETag / Last-Modified of the last answer (in memory) and sends them back, so an unchanged
    /// answer costs no rate limit; a 304 returns the remembered body as a normal success.
    /// </summary>
    public static async Task<ConnectedReply> GetConditionalAsync(string key, Func<HttpRequestMessage> make, CancellationToken cancel = default)
    {
        var request = make();
        bool had = Conditional.TryGetValue(key, out var old);
        if (had && old.ETag is { Length: > 0 }) request.Headers.TryAddWithoutValidation("If-None-Match", old.ETag);
        if (had && old.LastModified is { Length: > 0 }) request.Headers.TryAddWithoutValidation("If-Modified-Since", old.LastModified);
        var reply = await SendAsync(request, cancel);
        if (reply.Status == 304 && had) return reply with { Status = 200, Body = old.Body };
        if (reply.Ok && (reply.ETag is not null || reply.LastModified is not null))
        {
            if (Conditional.Count > 64) Conditional.Clear();
            Conditional[key] = (reply.ETag, reply.LastModified, reply.Body);
        }
        return reply;
    }

    /// <summary>Forgets remembered answers whose key starts with <paramref name="prefix"/> (after something changed on the server).</summary>
    public static void ForgetConditional(string prefix)
    {
        foreach (var key in Conditional.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList()) Conditional.TryRemove(key, out _);
    }

    // ---------- Cache ----------

    public static string Cached(WidgetContext ctx)
    {
        string json = ctx.Item.Options.GetValueOrDefault(CacheJson) ?? "";
        return !ctx.IsSnapshot && Sources.TryGetValue(ctx, out var source) && source.Strict
            ? OnlineLogic.CacheForRequest(json, ctx.Item.Options.GetValueOrDefault(CacheKey), source.Signature) : json;
    }
    public static DateTimeOffset? Fetched(WidgetContext ctx) => Cached(ctx).Length == 0 ? null : OnlineLogic.ParseStamp(ctx.Item.Options.GetValueOrDefault(CacheTime));
    public static string? Error(WidgetContext ctx) => ctx.Item.Options.TryGetValue(CacheError, out var e) && e.Length > 0 ? e : null;
    public static bool ErrorIsAuth(WidgetContext ctx) => ctx.Item.Options.GetValueOrDefault(CacheErrorKind) == "auth";
    public static bool Matches(WidgetContext ctx, string signature) => ctx.Item.Options.GetValueOrDefault(CacheKey) == signature;

    /// <summary>Samples: put fixture data into the cache as if it had just been downloaded.</summary>
    public static void Fill(LauncherItem item, string json, string signature, TimeSpan? age = null, string? error = null)
    {
        item.Options[CacheJson] = json;
        item.Options[CacheKey] = signature;
        item.Options[CacheTime] = OnlineLogic.FormatStamp(DateTimeOffset.Now - (age ?? TimeSpan.FromMinutes(3)));
        if (error is not null) item.Options[CacheError] = error;
    }

    /// <summary>Removes everything cached (when the user disconnects).</summary>
    public static void ClearCache(LauncherItem item)
    {
        foreach (string key in item.Options.Keys.Where(k => k.StartsWith("cache.", StringComparison.Ordinal)).ToList()) item.Options.Remove(key);
    }

    // ---------- Refreshing ----------

    /// <summary>Keeps the data fresh while the widget is on screen: fetches now if the cache is missing, stale or made with other settings, and re-checks on every tick.</summary>
    public static void Keep(WidgetContext ctx, ConnectedSource source, Action<DateTimeOffset>? tick = null)
    {
        Sources.AddOrUpdate(ctx, source);
        if (ctx.IsSnapshot) return;
        Refresh(ctx, source, force: false);
        ctx.Tick(() => false, now =>
        {
            Refresh(ctx, source, force: false);
            tick?.Invoke(now);
        });
    }

    /// <summary>Registers the source for cache lookups without refreshing (used while the widget shows a setup state).</summary>
    public static void Watch(WidgetContext ctx, ConnectedSource source) => Sources.AddOrUpdate(ctx, source);

    /// <summary>Fetches if needed (or always, with <paramref name="force"/>). Never blocks; at most one fetch per item at a time.</summary>
    public static void Refresh(WidgetContext ctx, ConnectedSource source, bool force)
    {
        if (ctx.IsSnapshot) return;
        var item = ctx.Item;
        var now = DateTimeOffset.Now;
        bool sameRequest = Matches(ctx, source.Signature);
        if (!force)
        {
            var fetched = OnlineLogic.ParseStamp(item.Options.GetValueOrDefault(CacheTime));
            if (sameRequest && !OnlineLogic.IsStale(fetched, source.Interval, now)) return;
            if (Attempts.TryGetValue(item.Id, out var last) && last.Signature == source.Signature && now - last.At < OnlineLogic.RetryDelay(source.Interval)) return;
        }
        ctx.RunOnce("connected:" + item.Id, async () =>
        {
            Attempts[item.Id] = (DateTimeOffset.Now, source.Signature);
            string problem;
            string kind = "net";
            try
            {
                string json = await source.Fetch();
                item.Options[CacheJson] = json;
                item.Options[CacheKey] = source.Signature;
                item.Options.Remove(CacheError);
                item.Options.Remove(CacheErrorKind);
                ctx.Set(CacheTime, OnlineLogic.FormatStamp(DateTimeOffset.Now));
                return;
            }
            catch (ConnectedException ex) { problem = ex.Message; if (ex.Auth) kind = "auth"; }
            catch (HttpRequestException ex) { problem = ConnectedLogic.GenericProblem(ex.StatusCode is { } code ? (int)code : null, false, false); }
            catch (TaskCanceledException) { problem = ConnectedLogic.GenericProblem(null, true, false); }
            catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException or KeyNotFoundException) { problem = ex is FormatException { Message.Length: > 0 } f && f.InnerException is null ? f.Message : ConnectedLogic.GenericProblem(null, false, true); }
            // Keep showing the old data and remember why it is old. Data from a different request isn't shown, so drop it.
            if (source.Strict && !Matches(ctx, source.Signature)) { item.Options.Remove(CacheJson); item.Options.Remove(CacheTime); item.Options[CacheKey] = source.Signature; }
            item.Options[CacheErrorKind] = kind;
            ctx.Set(CacheError, problem);
        });
    }

    public static WidgetAction RefreshAction(WidgetContext ctx, ConnectedSource source)
        => new("Refresh now", "", () => { ctx.Status($"Refreshing {ctx.Item.Name}…"); Refresh(ctx, source, force: true); });

    /// <summary>Removes a saved token, forgets what was downloaded with it and redraws.</summary>
    public static void Disconnect(WidgetContext ctx, string secretName, string service)
    {
        ctx.RemoveSecret(secretName);
        ClearCache(ctx.Item);
        ctx.Changed();
        ctx.Status($"Disconnected from {service}. The token was removed from this PC.");
    }

    // ---------- Drawing ----------

    /// <summary>A centered setup or error state: icon, headline, explanation and an optional button.</summary>
    public static FrameworkElement Setup(WidgetContext ctx, string glyph, string title, string text, string? buttonText = null, Action? click = null, Brush? iconColor = null)
        => WidgetKit.Scroll(SetupPanel(ctx, glyph, title, text, buttonText, click, iconColor));

    /// <summary>The setup state's content before it is put in a scroller (so a widget can add to it).</summary>
    public static StackPanel SetupPanel(WidgetContext ctx, string glyph, string title, string text, string? buttonText = null, Action? click = null, Brush? iconColor = null)
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(4, 0, 4, 0) };
        bool tight = ctx.Height < 260;
        if (!tight)
        {
            var icon = WidgetKit.Icon(glyph, ctx.Font(24), iconColor ?? ctx.Accent);
            icon.HorizontalAlignment = HorizontalAlignment.Left;
            icon.Margin = new Thickness(0, 0, 0, 8);
            stack.Children.Add(icon);
        }
        var head = WidgetKit.Text(ctx, title, 13.5, bold: true);
        head.TextWrapping = TextWrapping.Wrap;
        stack.Children.Add(head);
        var body = WidgetKit.Muted(ctx, text, 11.5);
        body.TextWrapping = TextWrapping.Wrap;
        body.TextTrimming = TextTrimming.None;
        body.Margin = new Thickness(0, 3, 0, 0);
        stack.Children.Add(body);
        if (buttonText is not null && click is not null)
        {
            var button = WidgetKit.Button(buttonText, click, prominent: true);
            button.HorizontalAlignment = HorizontalAlignment.Left;
            button.Margin = new Thickness(0, 10, 0, 0);
            stack.Children.Add(button);
        }
        return stack;
    }

    /// <summary>The body before any data has arrived: "Loading…" (a snapshot says there's no sample).</summary>
    public static FrameworkElement Loading(WidgetContext ctx, string loading)
    {
        var note = WidgetKit.Empty(ctx, ctx.IsSnapshot ? "No sample data." : loading);
        note.VerticalAlignment = VerticalAlignment.Center;
        note.HorizontalAlignment = HorizontalAlignment.Center;
        return note;
    }

    /// <summary>
    /// A quiet one-line footer at the bottom of a framed body. When the last refresh failed but data is on screen:
    /// "Offline — showing 10:42 data", or the problem itself if the token was rejected. Otherwise "Updated 10:42"
    /// when <paramref name="showUpdated"/>. Nothing when there's nothing to say. Add it before the main content.
    /// </summary>
    public static void AddFooter(WidgetContext ctx, DockPanel body, bool showUpdated)
    {
        var fetched = Fetched(ctx);
        var now = DateTimeOffset.Now;
        string text = "";
        Brush color = Theme.Dim;
        if (Error(ctx) is { } problem && fetched is not null)
        {
            if (ErrorIsAuth(ctx)) { text = problem; color = Theme.Danger; }
            else { text = OnlineLogic.OfflineNote(fetched.Value, now); color = Theme.Text2; }
        }
        else if (showUpdated && fetched is { } at) text = "Updated " + OnlineLogic.DataTime(at, now);
        if (text.Length == 0) return;
        var note = WidgetKit.Text(ctx, text, 10.5, color);
        note.Margin = new Thickness(0, 6, 0, 0);
        note.ToolTip = Error(ctx) ?? text;
        // A problem the user has to act on needs its whole sentence.
        if (color == Theme.Danger) { note.TextWrapping = TextWrapping.Wrap; note.TextTrimming = TextTrimming.None; }
        DockPanel.SetDock(note, Dock.Bottom);
        body.Children.Add(note);
    }

    // ---------- Avatars ----------

    private static readonly ConcurrentDictionary<string, ImageSource> Avatars = new();
    private static readonly ConcurrentDictionary<string, DateTimeOffset> AvatarFailures = new();
    private static readonly HashSet<string> RebuildPending = [];
    private static readonly string[] Placeholder = ["#6c63ff", "#14b8a6", "#f59e0b", "#ec4899", "#3b82f6", "#22c55e", "#ef4444", "#8b5cf6"];

    /// <summary>
    /// A round avatar: the downloaded picture (small, cached for the session) or colored initials until it arrives.
    /// Downloads only happen on screen and never in snapshots.
    /// </summary>
    public static FrameworkElement Avatar(WidgetContext ctx, string? url, string name, double size)
    {
        var grid = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        var circle = new Ellipse { Width = size, Height = size };
        string key = url + "|" + (int)Math.Round(size);
        if (url is not null && Avatars.TryGetValue(key, out var image))
        {
            circle.Fill = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
            grid.Children.Add(circle);
            return grid;
        }
        circle.Fill = Theme.Tint(Placeholder[ConnectedLogic.Bucket(name, Placeholder.Length)], 0.85);
        grid.Children.Add(circle);
        var initials = Theme.Label(ConnectedLogic.Initials(name), Math.Max(7, size * 0.4), Brushes.White);
        initials.FontWeight = FontWeights.SemiBold;
        initials.HorizontalAlignment = HorizontalAlignment.Center;
        grid.Children.Add(initials);
        if (!ctx.IsSnapshot && ConnectedLogic.IsAvatarUrl(url) && (!AvatarFailures.TryGetValue(key, out var failed) || DateTimeOffset.Now - failed > TimeSpan.FromMinutes(10)))
            ctx.RunOnce("avatar:" + key, () => LoadAvatar(ctx, url!, key, size));
        return grid;
    }

    private static async Task LoadAvatar(WidgetContext ctx, string url, string key, double size)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 1_000_000) { AvatarFailures[key] = DateTimeOffset.Now; return; }
            byte[] bytes = await response.Content.ReadAsByteArrayAsync();
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = (int)Math.Clamp(Math.Round(size * 2), 24, 128);
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze();
            if (Avatars.Count > 300) Avatars.Clear();
            Avatars[key] = image;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException or IOException or InvalidOperationException or ArgumentException)
        {
            AvatarFailures[key] = DateTimeOffset.Now;
            return;
        }
        // Several avatars usually arrive together: redraw once.
        if (RebuildPending.Add(ctx.Item.Id))
            _ = ctx.Dispatcher.BeginInvoke(DispatcherPriority.Background, () => { RebuildPending.Remove(ctx.Item.Id); ctx.Rebuild(); });
    }

    /// <summary>A copy of a brush at lower opacity (for dimmed or offline content).</summary>
    public static Brush Tint(Brush brush, double opacity)
    {
        if (brush is not SolidColorBrush solid) return brush;
        var copy = solid.Clone();
        copy.Opacity = solid.Opacity * opacity;
        copy.Freeze();
        return copy;
    }
}
