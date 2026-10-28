using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopDeck.Services;

/// <summary>Google OAuth scopes. Features ask for the ones they need through <see cref="GoogleAuth.EnsureScopesAsync(string[])"/>.</summary>
public static class GoogleScopes
{
    public const string CalendarEvents = "https://www.googleapis.com/auth/calendar.events";
    public const string CalendarReadOnly = "https://www.googleapis.com/auth/calendar.readonly";
    public const string OpenId = "openid";
    public const string Email = "email";
    // For later features (incremental consent: the user is asked again only when one of these is first needed).
    public const string Tasks = "https://www.googleapis.com/auth/tasks";
    public const string GmailReadOnly = "https://www.googleapis.com/auth/gmail.readonly";
    public const string ClassroomCourses = "https://www.googleapis.com/auth/classroom.courses.readonly";
    public const string ClassroomCourseWork = "https://www.googleapis.com/auth/classroom.coursework.me.readonly";
    /// <summary>Needed for the turned-in / missing state of each assignment.</summary>
    public const string ClassroomSubmissions = "https://www.googleapis.com/auth/classroom.student-submissions.me.readonly";

    /// <summary>What connecting an account asks for: view and edit calendars, and which account it is.</summary>
    public static readonly string[] Calendar = [CalendarEvents, CalendarReadOnly, OpenId, Email];
}

/// <summary>What Google answered to a token request.</summary>
public sealed record GoogleTokens(string AccessToken, int ExpiresInSeconds, string? RefreshToken, string Scope, string? IdToken);

/// <summary>
/// Signs in to a Google account with OAuth 2.0 for installed apps: authorization code with PKCE and a loopback redirect
/// (http://127.0.0.1:port/). You bring your own OAuth client (Desktop app type), so there is no shared key to leak.
/// The client ID, client secret and refresh token are kept encrypted in <see cref="SecretStore"/> (never in settings.json
/// or logs); access tokens live only in memory. Only one refresh runs at a time.
/// </summary>
public sealed class GoogleAuth
{
    public const string ClientIdEntry = "google.clientId", ClientSecretEntry = "google.clientSecret", RefreshEntry = "google.refresh";
    public const string ScopesEntry = "google.scopes", EmailEntry = "google.email";
    public const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    public const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    public const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";

    private readonly SecretStore secrets;
    private readonly HttpClient http;
    private readonly Func<string, Task> openBrowser;
    private readonly Func<DateTimeOffset> clock;
    private readonly SemaphoreSlim refreshGate = new(1, 1), consentGate = new(1, 1);
    private readonly object stateLock = new();
    private string? accessToken;
    private DateTimeOffset accessExpires;

    public GoogleAuth(SecretStore secrets, HttpClient? http = null, Func<string, Task>? openBrowser = null, Func<DateTimeOffset>? clock = null)
    {
        this.secrets = secrets;
        this.http = http ?? CalendarHttp.Client;
        this.openBrowser = openBrowser ?? OpenInBrowser;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Smoke renders: pretend to be connected (sample data only); every network call is refused.</summary>
    public bool SampleMode { get; set; }

    public bool HasClient => secrets.Has(ClientIdEntry) && secrets.Has(ClientSecretEntry);
    public bool IsConnected => SampleMode || secrets.Has(RefreshEntry);
    /// <summary>Smoke renders: which scopes the made-up account "has" (null = all of them), so Settings can show some Enable buttons.</summary>
    public IReadOnlyCollection<string>? SampleScopes { get; set; }
    public string? Email => secrets.Get(EmailEntry);
    public string? SavedClientId => secrets.Get(ClientIdEntry);

    public IReadOnlyList<string> GrantedScopes => SplitScopes(secrets.Get(ScopesEntry));
    public bool HasScopes(params string[] scopes) => SampleMode ? SampleScopes is null || scopes.All(s => SampleScopes.Contains(s, StringComparer.Ordinal)) : scopes.All(s => GrantedScopes.Contains(s, StringComparer.Ordinal));

    public void SaveClient(string clientId, string clientSecret)
    {
        secrets.Set(ClientIdEntry, clientId);
        secrets.Set(ClientSecretEntry, clientSecret);
    }

    // ---------- Pure helpers (tested without a network) ----------

    /// <summary>A random PKCE code verifier: 64 characters from the unreserved set (RFC 7636 asks for 43–128).</summary>
    public static string NewVerifier()
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";
        var chars = new char[64];
        for (int i = 0; i < chars.Length; i++) chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        return new string(chars);
    }

    /// <summary>The S256 code challenge: base64url (no padding) of the SHA-256 of the verifier.</summary>
    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    public static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] FromBase64Url(string text)
    {
        string s = text.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        return Convert.FromBase64String(s);
    }

    public static string[] SplitScopes(string? scopes) => (scopes ?? "").Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>The consent page address. <paramref name="redirectUri"/> must be sent again, unchanged, when the code is exchanged.</summary>
    public static string BuildAuthUrl(string clientId, string redirectUri, IEnumerable<string> scopes, string state, string challenge, string? loginHint = null, bool includeGranted = false)
    {
        var query = new List<(string, string)>
        {
            ("client_id", clientId), ("redirect_uri", redirectUri), ("response_type", "code"), ("scope", string.Join(" ", scopes)),
            ("state", state), ("code_challenge", challenge), ("code_challenge_method", "S256"),
            ("access_type", "offline"), ("prompt", "consent"),
        };
        if (includeGranted) query.Add(("include_granted_scopes", "true"));
        if (!string.IsNullOrWhiteSpace(loginHint)) query.Add(("login_hint", loginHint));
        return AuthEndpoint + "?" + string.Join("&", query.Select(q => Uri.EscapeDataString(q.Item1) + "=" + Uri.EscapeDataString(q.Item2)));
    }

    /// <summary>Reads Google's token answer. An error answer becomes a <see cref="GoogleAuthException"/> with a clear message.</summary>
    public static GoogleTokens ParseTokenResponse(string json, bool refreshing = false)
    {
        JsonElement root;
        try { root = JsonDocument.Parse(json).RootElement; }
        catch (JsonException ex) { throw new GoogleAuthException("Google sent an answer Desktop Deck could not read.", false, ex); }
        if (root.ValueKind != JsonValueKind.Object) throw new GoogleAuthException("Google sent an answer Desktop Deck could not read.");
        if (root.TryGetProperty("error", out var error))
        {
            string code = error.ValueKind == JsonValueKind.String ? error.GetString() ?? "" : "";
            string description = root.TryGetProperty("error_description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() ?? "" : "";
            throw code switch
            {
                "invalid_grant" when refreshing => new GoogleAuthException(GoogleAuthException.Expired, true),
                "invalid_grant" => new GoogleAuthException("Google did not accept the sign-in (the code expired or was already used). Press Connect to try again."),
                "invalid_client" or "unauthorized_client" => new GoogleAuthException("Google does not recognise that client ID and secret. Check them in the Google Cloud console (the client must be a \"Desktop app\")."),
                "access_denied" => new GoogleAuthException(GoogleAuthException.Cancelled),
                "admin_policy_enforced" => new GoogleAuthException(GoogleAuthException.AdminBlocked),
                _ => new GoogleAuthException("Google sign-in failed" + (description.Length > 0 ? ": " + description : code.Length > 0 ? " (" + code + ")" : "") + "."),
            };
        }
        string token = root.TryGetProperty("access_token", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() ?? "" : "";
        if (token.Length == 0) throw new GoogleAuthException("Google's answer had no access token.");
        int expires = root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out int seconds) ? seconds : 3600;
        string? refresh = root.TryGetProperty("refresh_token", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
        string scope = root.TryGetProperty("scope", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() ?? "" : "";
        string? id = root.TryGetProperty("id_token", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString() : null;
        return new GoogleTokens(token, expires, string.IsNullOrEmpty(refresh) ? null : refresh, scope, id);
    }

    /// <summary>The email address inside an ID token. The token comes straight from Google over TLS, so it is read, not verified.</summary>
    public static string? EmailFromIdToken(string? idToken)
    {
        if (string.IsNullOrEmpty(idToken)) return null;
        var parts = idToken.Split('.');
        if (parts.Length < 2) return null;
        try
        {
            using var doc = JsonDocument.Parse(FromBase64Url(parts[1]));
            return doc.RootElement.TryGetProperty("email", out var email) && email.ValueKind == JsonValueKind.String ? email.GetString() : null;
        }
        catch (Exception ex) when (ex is JsonException or FormatException) { return null; }
    }

    // ---------- Signing in ----------

    /// <summary>
    /// Opens Google's consent page in the default browser and waits for the answer. Saves the client, the refresh token and
    /// the granted scopes. Returns the account's email (or "" if Google didn't send it).
    /// </summary>
    public async Task<string> ConnectAsync(string? clientId, string? clientSecret, CancellationToken cancel = default)
    {
        if (SampleMode) throw new GoogleAuthException("Sample mode: no sign-in.");
        clientId = string.IsNullOrWhiteSpace(clientId) ? secrets.Get(ClientIdEntry) : clientId.Trim();
        clientSecret = string.IsNullOrWhiteSpace(clientSecret) ? secrets.Get(ClientSecretEntry) : clientSecret.Trim();
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret)) throw new GoogleAuthException("Paste your OAuth client ID and client secret first (see the steps above).");
        await AuthorizeAsync(clientId, clientSecret, GoogleScopes.Calendar, null, false, cancel).ConfigureAwait(false);
        SaveClient(clientId, clientSecret);
        return Email ?? "";
    }

    /// <summary>
    /// Makes sure the account has granted these scopes, asking Google again (incremental consent, in the browser) only when
    /// one is missing. Throws if nobody is connected yet.
    /// </summary>
    public Task EnsureScopesAsync(params string[] scopes) => EnsureScopesAsync(scopes, CancellationToken.None);

    public async Task EnsureScopesAsync(IEnumerable<string> scopes, CancellationToken cancel)
    {
        if (SampleMode) return;
        var wanted = scopes.ToArray();
        if (!IsConnected) throw new GoogleAuthException("Connect a Google account first (Settings → Connected accounts).", true);
        if (HasScopes(wanted)) return;
        string? clientId = secrets.Get(ClientIdEntry), clientSecret = secrets.Get(ClientSecretEntry);
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret)) throw new GoogleAuthException(GoogleAuthException.Expired, true);
        await AuthorizeAsync(clientId, clientSecret, GrantedScopes.Concat(wanted).Distinct(StringComparer.Ordinal), Email, true, cancel).ConfigureAwait(false);
    }

    private async Task AuthorizeAsync(string clientId, string clientSecret, IEnumerable<string> scopes, string? loginHint, bool incremental, CancellationToken cancel)
    {
        if (!await consentGate.WaitAsync(0, cancel).ConfigureAwait(false)) throw new GoogleAuthException("Google sign-in is already open in your browser.");
        try
        {
            string verifier = NewVerifier(), state = Base64Url(RandomNumberGenerator.GetBytes(18));
            int port = FreePort();
            string redirect = $"http://127.0.0.1:{port}/";
            using var listener = new HttpListener();
            listener.Prefixes.Add(redirect);
            try { listener.Start(); }
            catch (HttpListenerException ex) { throw new GoogleAuthException("Desktop Deck could not listen for Google's answer on this PC (a local firewall or policy may be blocking it).", false, ex); }

            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            limit.CancelAfter(TimeSpan.FromMinutes(5));
            using var abort = limit.Token.Register(() => { try { listener.Abort(); } catch (ObjectDisposedException) { } });
            await openBrowser(BuildAuthUrl(clientId, redirect, scopes, state, Challenge(verifier), loginHint, incremental)).ConfigureAwait(false);

            string? code = null;
            while (code is null)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync().ConfigureAwait(false); }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
                {
                    throw cancel.IsCancellationRequested ? new GoogleAuthException(GoogleAuthException.Cancelled) : new GoogleAuthException("Google sign-in timed out. Press Connect to try again.");
                }
                var query = context.Request.QueryString;
                if (context.Request.Url?.AbsolutePath != "/" || (query["code"] is null && query["error"] is null)) { Respond(context, 404, "Not found."); continue; }
                if (query["error"] is { } error)
                {
                    Respond(context, 200, "Google sign-in was not completed. You can close this tab and return to Desktop Deck.");
                    throw error == "access_denied" ? new GoogleAuthException(GoogleAuthException.Cancelled)
                        : error == "admin_policy_enforced" ? new GoogleAuthException(GoogleAuthException.AdminBlocked)
                        : new GoogleAuthException("Google sign-in failed (" + error + ").");
                }
                if (!string.Equals(query["state"], state, StringComparison.Ordinal)) { Respond(context, 400, "This sign-in link does not match the one Desktop Deck opened."); continue; }
                code = query["code"];
                Respond(context, 200, "Desktop Deck is connected to Google. You can close this tab and return to Desktop Deck.");
            }

            var form = new Dictionary<string, string>
            {
                ["code"] = code!, ["client_id"] = clientId, ["client_secret"] = clientSecret, ["redirect_uri"] = redirect,
                ["grant_type"] = "authorization_code", ["code_verifier"] = verifier,
            };
            var tokens = ParseTokenResponse(await PostFormAsync(TokenEndpoint, form, cancel).ConfigureAwait(false));
            Store(tokens);
            if (EmailFromIdToken(tokens.IdToken) is { Length: > 0 } email) secrets.Set(EmailEntry, email);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { throw new GoogleAuthException(GoogleAuthException.Cancelled); }
        finally { consentGate.Release(); }
    }

    private void Store(GoogleTokens tokens)
    {
        if (tokens.RefreshToken is { } refresh) secrets.Set(RefreshEntry, refresh);
        // Google lists every scope the account has granted this app so far (include_granted_scopes).
        var granted = SplitScopes(tokens.Scope);
        if (granted.Length > 0) secrets.Set(ScopesEntry, string.Join(" ", granted));
        lock (stateLock)
        {
            accessToken = tokens.AccessToken;
            accessExpires = clock().AddSeconds(Math.Max(60, tokens.ExpiresInSeconds));
        }
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try { return ((IPEndPoint)probe.LocalEndpoint).Port; }
        finally { probe.Stop(); }
    }

    private static void Respond(HttpListenerContext context, int status, string message)
    {
        try
        {
            string html = "<!doctype html><meta charset=\"utf-8\"><title>Desktop Deck</title><body style=\"font:16px Segoe UI,sans-serif;background:#1a1611;color:#ede4d3;display:grid;place-items:center;height:100vh;margin:0\"><p>"
                + WebUtility.HtmlEncode(message) + "</p></body>";
            var bytes = Encoding.UTF8.GetBytes(html);
            context.Response.StatusCode = status;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.Close();
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException) { }
    }

    private static Task OpenInBrowser(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        return Task.CompletedTask;
    }

    private async Task<string> PostFormAsync(string endpoint, Dictionary<string, string> form, CancellationToken cancel)
    {
        try
        {
            using var response = await http.PostAsync(endpoint, new FormUrlEncodedContent(form), cancel).ConfigureAwait(false);
            // Google answers errors with a JSON body too, which ParseTokenResponse turns into a clear message.
            return await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) { throw new GoogleAuthException("Could not reach Google. Check your internet connection.", false, ex); }
        catch (TaskCanceledException) when (!cancel.IsCancellationRequested) { throw new GoogleAuthException("Google took too long to answer."); }
    }

    // ---------- Access tokens ----------

    /// <summary>A valid access token, refreshed with the saved refresh token when it has (almost) expired. One refresh at a time.</summary>
    public async Task<string> GetAccessTokenAsync(CancellationToken cancel = default)
    {
        if (SampleMode) throw new GoogleAuthException("Sample mode: Google is not contacted.");
        if (Cached() is { } fresh) return fresh;
        await refreshGate.WaitAsync(cancel).ConfigureAwait(false);
        try
        {
            if (Cached() is { } again) return again;
            string? refresh = secrets.Get(RefreshEntry);
            string? clientId = secrets.Get(ClientIdEntry), clientSecret = secrets.Get(ClientSecretEntry);
            if (string.IsNullOrEmpty(refresh) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
                throw new GoogleAuthException(GoogleAuthException.Expired, true);
            var form = new Dictionary<string, string> { ["client_id"] = clientId, ["client_secret"] = clientSecret, ["refresh_token"] = refresh, ["grant_type"] = "refresh_token" };
            GoogleTokens tokens;
            try { tokens = ParseTokenResponse(await PostFormAsync(TokenEndpoint, form, cancel).ConfigureAwait(false), refreshing: true); }
            catch (GoogleAuthException ex) when (ex.NeedsReconnect)
            {
                // The refresh token was revoked or expired (Testing-mode projects expire after 7 days). Keep the client so Connect is one click.
                secrets.Remove(RefreshEntry);
                lock (stateLock) accessToken = null;
                throw;
            }
            Store(tokens);
            return tokens.AccessToken;
        }
        finally { refreshGate.Release(); }
    }

    private string? Cached()
    {
        lock (stateLock) return accessToken is not null && clock() < accessExpires.AddSeconds(-60) ? accessToken : null;
    }

    /// <summary>Forget the cached access token (after Google answered 401), so the next call refreshes.</summary>
    public void ForgetAccessToken() { lock (stateLock) accessToken = null; }

    // ---------- Disconnecting ----------

    /// <summary>Revokes the grant at Google (best effort) and removes the saved client, token, scopes and email. Returns true if Google confirmed the revoke.</summary>
    public async Task<bool> DisconnectAsync(CancellationToken cancel = default)
    {
        string? token = secrets.Get(RefreshEntry);
        bool revoked = false;
        if (!SampleMode && !string.IsNullOrEmpty(token))
        {
            try
            {
                using var response = await http.PostAsync(RevokeEndpoint, new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token }), cancel).ConfigureAwait(false);
                revoked = response.IsSuccessStatusCode;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { }
        }
        foreach (var entry in new[] { RefreshEntry, ClientIdEntry, ClientSecretEntry, ScopesEntry, EmailEntry }) secrets.Remove(entry);
        lock (stateLock) accessToken = null;
        return revoked;
    }
}
