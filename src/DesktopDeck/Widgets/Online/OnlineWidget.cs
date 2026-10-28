using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml;

namespace DesktopDeck.Widgets;

/// <summary>What an Online widget downloads: a signature of the request (settings that change the answer), how
/// often, and the work that fetches and trims the response into the text stored in "cache.json".</summary>
internal sealed record OnlineSource(string Signature, TimeSpan Interval, Func<Task<string>> Fetch);

/// <summary>
/// Shared plumbing for the Online widgets: one HttpClient, the refresh policy (only while on screen, at the user's
/// interval, never in snapshots), the cache in Item.Options ("cache.json", "cache.time", "cache.key", "cache.error")
/// and the quiet footer ("Updated 10:42" / "Offline — showing 10:42 data" / attribution).
/// </summary>
internal static class OnlineWidget
{
    public const string CacheJson = "cache.json", CacheTime = "cache.time", CacheKey = "cache.key", CacheError = "cache.error";

    private static readonly HttpClient Http = CreateClient();
    private static readonly ConditionalWeakTable<WidgetContext, OnlineSource> Sources = new();
    /// <summary>When each item last tried (and with which request), so failures don't hammer the service.</summary>
    private static readonly ConcurrentDictionary<string, (DateTimeOffset At, string Signature)> Attempts = new();

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All, PooledConnectionLifetime = TimeSpan.FromMinutes(15) };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 8 * 1024 * 1024 };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DesktopDeck/0.6");
        return client;
    }

    /// <summary>GET as text; throws <see cref="HttpRequestException"/> (with the status code) on anything but success.</summary>
    public static async Task<string> GetAsync(string url, CancellationToken cancel = default)
    {
        using var response = await Http.GetAsync(url, cancel);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase}", null, response.StatusCode);
        return await response.Content.ReadAsStringAsync(cancel);
    }

    // ---------- Cache ----------

    public static string Cached(WidgetContext ctx)
    {
        string json = ctx.Item.Options.GetValueOrDefault(CacheJson) ?? "";
        return !ctx.IsSnapshot && Sources.TryGetValue(ctx, out var source)
            ? OnlineLogic.CacheForRequest(json, ctx.Item.Options.GetValueOrDefault(CacheKey), source.Signature) : json;
    }
    public static DateTimeOffset? Fetched(WidgetContext ctx) => Cached(ctx).Length == 0 ? null : OnlineLogic.ParseStamp(ctx.Item.Options.GetValueOrDefault(CacheTime));
    public static string? Error(WidgetContext ctx) => ctx.Item.Options.TryGetValue(CacheError, out var e) && e.Length > 0 ? e : null;
    /// <summary>The cache was made with the current settings.</summary>
    public static bool Matches(WidgetContext ctx, string signature) => ctx.Item.Options.GetValueOrDefault(CacheKey) == signature;

    /// <summary>Samples: put fixture data into the cache as if it had just been downloaded.</summary>
    public static void Fill(Models.LauncherItem item, string json, string signature, TimeSpan? age = null, string? error = null)
    {
        item.Options[CacheJson] = json;
        item.Options[CacheKey] = signature;
        item.Options[CacheTime] = OnlineLogic.FormatStamp(DateTimeOffset.Now - (age ?? TimeSpan.FromMinutes(3)));
        if (error is not null) item.Options[CacheError] = error;
    }

    // ---------- Refreshing ----------

    /// <summary>
    /// Keeps the data fresh while the widget is on screen: fetches now if the cache is missing, stale or made with
    /// other settings, and checks again on every tick (each minute, or each second when <paramref name="everySecond"/>
    /// says so — the check itself is cheap). <paramref name="tick"/> runs on each tick too (e.g. to rotate or redraw ages).
    /// </summary>
    public static void Keep(WidgetContext ctx, OnlineSource source, Action<DateTimeOffset>? tick = null, Func<bool>? everySecond = null)
    {
        Sources.AddOrUpdate(ctx, source);
        if (ctx.IsSnapshot) return;
        Refresh(ctx, source, force: false);
        ctx.Tick(everySecond ?? (() => false), now =>
        {
            Refresh(ctx, source, force: false);
            tick?.Invoke(now);
        });
    }

    /// <summary>Fetches if needed (or always, with <paramref name="force"/>). Never blocks; at most one fetch per item at a time.</summary>
    public static void Refresh(WidgetContext ctx, OnlineSource source, bool force)
    {
        if (ctx.IsSnapshot) return;
        var item = ctx.Item;
        var now = DateTimeOffset.Now;
        bool sameRequest = Matches(ctx, source.Signature);
        if (!force)
        {
            if (sameRequest && !OnlineLogic.IsStale(Fetched(ctx), source.Interval, now)) return;
            if (Attempts.TryGetValue(item.Id, out var last) && last.Signature == source.Signature && now - last.At < OnlineLogic.RetryDelay(source.Interval)) return;
        }
        ctx.RunOnce("online:" + item.Id, async () =>
        {
            Attempts[item.Id] = (DateTimeOffset.Now, source.Signature);
            string? problem = null;
            try
            {
                string json = await source.Fetch();
                item.Options[CacheJson] = json;
                item.Options[CacheKey] = source.Signature;
                item.Options.Remove(CacheError);
                ctx.Set(CacheTime, OnlineLogic.FormatStamp(DateTimeOffset.Now));
                return;
            }
            catch (HttpRequestException ex) { problem = OnlineLogic.Problem(ex.StatusCode is { } code ? (int)code : null, false, false); }
            catch (TaskCanceledException) { problem = OnlineLogic.Problem(null, true, false); }
            catch (Exception ex) when (ex is FormatException or JsonException or XmlException or InvalidOperationException or KeyNotFoundException)
            {
                problem = ex is FormatException { Message.Length: > 0 } f && f.InnerException is null && !f.Message.StartsWith("The input", StringComparison.Ordinal) ? f.Message : OnlineLogic.Problem(null, false, true);
            }
            // Keep showing the old data; remember why it's old. A different request's data isn't shown, so drop it.
            if (!Matches(ctx, source.Signature)) { item.Options.Remove(CacheJson); item.Options.Remove(CacheTime); item.Options[CacheKey] = source.Signature; }
            ctx.Set(CacheError, problem ?? "Couldn't refresh.");
        });
    }

    public static WidgetAction RefreshAction(WidgetContext ctx, OnlineSource source)
        => new("Refresh now", "", () => { ctx.Status($"Refreshing {ctx.Item.Name}…"); Refresh(ctx, source, force: true); });

    // ---------- Drawing ----------

    /// <summary>The body before any data has arrived: "Loading…", or why it failed.</summary>
    public static FrameworkElement Waiting(WidgetContext ctx, string loading = "Loading…")
    {
        if (Error(ctx) is { } problem)
        {
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 2, 0) };
            var icon = WidgetKit.Icon("", ctx.Font(20), Theme.Muted);
            icon.HorizontalAlignment = HorizontalAlignment.Left;
            icon.Margin = new Thickness(2, 0, 0, 6);
            stack.Children.Add(icon);
            var text = WidgetKit.Empty(ctx, problem);
            text.Margin = new Thickness(2, 0, 2, 0);
            stack.Children.Add(text);
            return stack;
        }
        var note = WidgetKit.Empty(ctx, ctx.IsSnapshot ? "No sample data." : loading);
        note.VerticalAlignment = VerticalAlignment.Center;
        note.HorizontalAlignment = HorizontalAlignment.Center;
        return note;
    }

    /// <summary>
    /// A one-line footer: the offline note when the last refresh failed, otherwise "Updated 10:42" (if
    /// <paramref name="showUpdated"/>), plus an attribution on the right (clickable when <paramref name="attributionUrl"/> is set).
    /// Null when there's nothing to say.
    /// </summary>
    public static FrameworkElement? Footer(WidgetContext ctx, bool showUpdated, string? attribution = null, string? attributionUrl = null)
    {
        var now = DateTimeOffset.Now;
        var fetched = Fetched(ctx);
        string left = "";
        bool offline = Error(ctx) is not null && fetched is not null;
        if (offline) left = OnlineLogic.OfflineNote(fetched!.Value, now);
        else if (showUpdated && fetched is { } at) left = "Updated " + OnlineLogic.DataTime(at, now);
        if (left.Length == 0 && string.IsNullOrEmpty(attribution)) return null;

        if (!string.IsNullOrEmpty(attribution) && (ctx.Width < 300 || offline))
        {
            var stack = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            if (left.Length > 0) { var note = WidgetKit.Text(ctx, left, 10.5, Theme.Muted); note.TextWrapping = TextWrapping.Wrap; stack.Children.Add(note); }
            var credit = WidgetKit.Text(ctx, attribution, 10.5, Theme.Dim);
            credit.TextWrapping = TextWrapping.Wrap;
            if (attributionUrl is not null) WidgetKit.Clickable(credit, () => ctx.Open(attributionUrl), attributionUrl);
            stack.Children.Add(credit);
            DockPanel.SetDock(stack, Dock.Bottom);
            return stack;
        }

        var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (left.Length > 0)
        {
            var note = WidgetKit.Text(ctx, left, 10.5, offline ? Theme.Text2 : Theme.Dim);
            if (offline) note.ToolTip = Error(ctx);
            grid.Children.Add(note);
        }
        if (!string.IsNullOrEmpty(attribution))
        {
            var credit = WidgetKit.Text(ctx, attribution, 10.5, Theme.Dim);
            credit.Margin = new Thickness(8, 0, 0, 0);
            credit.HorizontalAlignment = HorizontalAlignment.Right;
            if (attributionUrl is not null) WidgetKit.Clickable(credit, () => ctx.Open(attributionUrl), attributionUrl);
            Grid.SetColumn(credit, 1);
            if (left.Length == 0) { Grid.SetColumn(credit, 0); Grid.SetColumnSpan(credit, 2); }
            grid.Children.Add(credit);
        }
        DockPanel.SetDock(grid, Dock.Bottom);
        return grid;
    }

    /// <summary>Adds the footer (if any) to the bottom of a framed body. Call before adding the main content.</summary>
    public static void AddFooter(WidgetContext ctx, DockPanel body, bool showUpdated, string? attribution = null, string? attributionUrl = null)
    {
        if (Footer(ctx, showUpdated, attribution, attributionUrl) is { } footer) body.Children.Add(footer);
    }

    /// <summary>Opens a downloaded link if it is a web address.</summary>
    public static void OpenLink(WidgetContext ctx, string? url)
    {
        if (OnlineLogic.IsWebLink(url)) ctx.Open(url!);
    }

    /// <summary>A brush from "#rrggbb" text, or the fallback.</summary>
    public static Brush BrushOr(string? hex, Brush fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try { return Theme.Brush(hex.Trim()); }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException) { return fallback; }
    }
}
