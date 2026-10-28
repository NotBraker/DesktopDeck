using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace DesktopDeck.Services;

/// <summary>What kind of problem a Google API call had (so a widget can offer the right button).</summary>
public enum GoogleApiProblem
{
    Other,
    /// <summary>The sign-in expired or was revoked: press Connect again.</summary>
    NeedsReconnect,
    /// <summary>The account has not granted this feature's permission yet: press Enable.</summary>
    ScopeMissing,
    /// <summary>The API is not switched on in the user's Google Cloud project.</summary>
    ApiDisabled,
    /// <summary>The account's admin (a school or company) does not allow this app.</summary>
    Blocked,
    RateLimited,
    NotFound,
    Network,
}

/// <summary>A Google API problem with a message that is safe to show (never contains tokens or message contents).</summary>
public sealed class GoogleApiException : CalendarException
{
    public GoogleApiProblem Problem { get; }
    public int Status { get; }
    public GoogleApiException(string message, GoogleApiProblem problem, int status = 0, Exception? inner = null) : base(message, inner) { Problem = problem; Status = status; }

    /// <summary>True when the widget should show a button that fixes it (Connect or Enable) rather than "Offline".</summary>
    public bool NeedsUserAction => Problem is GoogleApiProblem.NeedsReconnect or GoogleApiProblem.ScopeMissing or GoogleApiProblem.ApiDisabled or GoogleApiProblem.Blocked;
}

/// <summary>Which Google API a client talks to, and the wording used when it fails.</summary>
/// <param name="Name">"Google Tasks".</param>
/// <param name="ApiName">The name in the Google Cloud console library, "Google Tasks API".</param>
/// <param name="Feature">What the Enable button in Settings is called: "Tasks".</param>
/// <param name="BlockedMessage">What a 403 that is not about scopes or the API being off means (a school or company policy for Classroom); null = a plain refusal.</param>
public sealed record GoogleApiInfo(string Name, string ApiName, string Feature, string? BlockedMessage = null);

/// <summary>One Google feature Desktop Deck can ask permission for, shown in Settings → Connected accounts.</summary>
public sealed record GoogleFeature(string Key, string Name, string Description, string ApiName, string[] Scopes);

public static class GoogleFeatures
{
    public static readonly GoogleFeature Calendar = new("calendar", "Calendar", "Schedule and Agenda widgets, and adding or changing events", "Google Calendar API", [GoogleScopes.CalendarReadOnly, GoogleScopes.CalendarEvents]);
    public static readonly GoogleFeature Tasks = new("tasks", "Tasks", "Two-way sync between Google Tasks and Today's Tasks", "Google Tasks API", [GoogleScopes.Tasks]);
    public static readonly GoogleFeature Gmail = new("gmail", "Gmail (read-only)", "The Gmail widget: unread count and latest messages. Desktop Deck can never send, change or delete mail", "Gmail API", [GoogleScopes.GmailReadOnly]);
    public static readonly GoogleFeature Classroom = new("classroom", "Classroom", "The Classroom widget: your courses and upcoming assignments (read-only)", "Google Classroom API", [GoogleScopes.ClassroomCourses, GoogleScopes.ClassroomCourseWork, GoogleScopes.ClassroomSubmissions]);

    public static readonly GoogleFeature[] All = [Calendar, Tasks, Gmail, Classroom];
}

/// <summary>
/// The HTTP plumbing shared by the Tasks, Gmail and Classroom services: bearer token from <see cref="GoogleAuth"/>, one retry
/// with a fresh token after a 401, and readable errors. Calendar keeps its own copy of this (it predates it).
/// </summary>
public sealed class GoogleApiClient
{
    private readonly GoogleAuth auth;
    private readonly HttpClient http;
    public GoogleApiInfo Info { get; }

    public GoogleApiClient(GoogleAuth auth, GoogleApiInfo info, HttpClient? http = null)
    {
        this.auth = auth;
        Info = info;
        this.http = http ?? CalendarHttp.Client;
    }

    public GoogleAuth Auth => auth;

    /// <summary>Sends a request and returns the answer body. <paramref name="body"/> is JSON. Throws <see cref="GoogleApiException"/>.</summary>
    public async Task<string> SendAsync(HttpMethod method, string url, string? body, CancellationToken cancel)
    {
        for (int attempt = 0; ; attempt++)
        {
            string token = await auth.GetAccessTokenAsync(cancel).ConfigureAwait(false);
            try
            {
                using var request = new HttpRequestMessage(method, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                using var response = await http.SendAsync(request, cancel).ConfigureAwait(false);
                string text = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0) { auth.ForgetAccessToken(); continue; }
                if (!response.IsSuccessStatusCode) throw Describe(Info, response.StatusCode, text);
                return text;
            }
            catch (HttpRequestException ex) { throw new GoogleApiException($"Could not reach {Info.Name}. Check your internet connection.", GoogleApiProblem.Network, 0, ex); }
            catch (TaskCanceledException ex) when (!cancel.IsCancellationRequested) { throw new GoogleApiException($"{Info.Name} took too long to answer.", GoogleApiProblem.Network, 0, ex); }
        }
    }

    /// <summary>A readable error for a failed call (never includes tokens or the answer's contents).</summary>
    public static CalendarException Describe(GoogleApiInfo info, HttpStatusCode status, string body)
    {
        var (reason, message, rpcStatus) = ReadError(body);
        int code = (int)status;
        string text = (message + " " + reason).ToLowerInvariant();
        bool scope = reason is "insufficientPermissions" or "ACCESS_TOKEN_SCOPE_INSUFFICIENT" or "insufficient_scope"
            || text.Contains("insufficient authentication scopes", StringComparison.Ordinal);
        bool disabled = reason is "accessNotConfigured" or "SERVICE_DISABLED"
            || text.Contains("has not been used in project", StringComparison.Ordinal)
            || text.Contains("api has not been enabled", StringComparison.Ordinal)
            || (text.Contains("is disabled", StringComparison.Ordinal) && text.Contains("api", StringComparison.Ordinal));
        bool limited = reason is "rateLimitExceeded" or "userRateLimitExceeded" or "RATE_LIMIT_EXCEEDED" or "quotaExceeded" || rpcStatus == "RESOURCE_EXHAUSTED";
        if (status == HttpStatusCode.Unauthorized) return new GoogleAuthException(GoogleAuthException.Expired, true);
        if (status is HttpStatusCode.Forbidden or HttpStatusCode.BadRequest or HttpStatusCode.TooManyRequests)
        {
            if (scope) return new GoogleApiException($"{info.Name} access has not been allowed yet. Press Enable for {info.Feature} in Settings → Connected accounts.", GoogleApiProblem.ScopeMissing, code);
            if (disabled) return new GoogleApiException($"The {info.ApiName} is not switched on for your Google Cloud project. Enable it in the Google Cloud console (APIs & Services → Library).", GoogleApiProblem.ApiDisabled, code);
            if (limited || status == HttpStatusCode.TooManyRequests) return new GoogleApiException("Google is limiting requests right now. Desktop Deck will try again later.", GoogleApiProblem.RateLimited, code);
        }
        if (status == HttpStatusCode.Forbidden)
            return info.BlockedMessage is { } blocked
                ? new GoogleApiException(blocked, GoogleApiProblem.Blocked, code)
                : new GoogleApiException($"{info.Name} refused the request" + (message.Length > 0 ? ": " + Shorten(message) : "") + ".", GoogleApiProblem.Other, code);
        if (status is HttpStatusCode.NotFound or HttpStatusCode.Gone) return new GoogleApiException($"That item no longer exists on {info.Name}.", GoogleApiProblem.NotFound, code);
        return new GoogleApiException($"{info.Name} returned an error (HTTP {code})" + (message.Length > 0 ? ": " + Shorten(message) : "") + ".", GoogleApiProblem.Other, code);
    }

    private static string Shorten(string text) => text.Length <= 160 ? text : text[..160] + "…";

    /// <summary>The first "reason" in Google's error body (old and new styles), its message, and the gRPC-style status word.</summary>
    public static (string Reason, string Message, string RpcStatus) ReadError(string body)
    {
        string reason = "", message = "", status = "";
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                message = error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? "" : "";
                status = error.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() ?? "" : "";
                if (error.TryGetProperty("errors", out var list) && list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0 && list[0].TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String) reason = r.GetString() ?? "";
                if (reason.Length == 0 && error.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
                    foreach (var detail in details.EnumerateArray())
                        if (detail.ValueKind == JsonValueKind.Object && detail.TryGetProperty("reason", out var dr) && dr.ValueKind == JsonValueKind.String) { reason = dr.GetString() ?? ""; break; }
            }
        }
        catch (JsonException) { }
        return (reason, message, status);
    }

    // ---------- Small JSON readers shared by the parsers ----------

    public static string Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    public static bool Bool(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
    public static int Int(JsonElement e, string name, int fallback = 0) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : fallback;
    public static string? NextPage(JsonElement root) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty("nextPageToken", out var next) && next.ValueKind == JsonValueKind.String && next.GetString() is { Length: > 0 } token ? token : null;
}
