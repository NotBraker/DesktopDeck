using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DesktopDeck.Models;

namespace DesktopDeck.Services;

/// <summary>What the unread badge counts.</summary>
public enum GmailBadge { None, Inbox, Query }

/// <summary>What one refresh of the Gmail widget downloads.</summary>
public sealed record GmailFetch(List<GmailMessage> Messages, int? Unread, List<GmailLabel> Labels);

/// <summary>
/// Gmail API v1, read-only: the code only ever sends GET requests, and the only permission it asks for is gmail.readonly
/// (it can never send, change or delete mail). It reads message metadata (From, Subject, Date and Gmail's snippet), never bodies
/// or attachments. JSON reading and the decoding of encoded headers are static and network-free so tests cover them.
/// </summary>
public sealed class GmailService
{
    public const string BaseUrl = "https://gmail.googleapis.com/gmail/v1/users/me";
    public const int MaxMessages = 25, ParallelGets = 4;
    public static readonly GoogleApiInfo Info = new("Gmail", "Gmail API", "Gmail (read-only)");

    private readonly GoogleApiClient api;

    public GmailService(GoogleAuth auth, HttpClient? http = null) => api = new GoogleApiClient(auth, Info, http);

    // ---------- Network ----------

    /// <summary>
    /// The latest messages for a Gmail search, the unread count for the badge, and (when label colors are used) the label names.
    /// Only GET requests are made.
    /// </summary>
    public async Task<GmailFetch> FetchAsync(string query, int max, GmailBadge badge, bool wantLabels, CancellationToken cancel = default)
    {
        max = Math.Clamp(max, 1, MaxMessages);
        var (refs, estimate) = ParseMessageList(await api.SendAsync(HttpMethod.Get, MessagesUrl(query, max), null, cancel).ConfigureAwait(false));
        var messages = new GmailMessage?[refs.Count];
        using var gate = new SemaphoreSlim(ParallelGets);
        await Task.WhenAll(refs.Select(async (r, i) =>
        {
            await gate.WaitAsync(cancel).ConfigureAwait(false);
            try { messages[i] = ParseMessageJson(await api.SendAsync(HttpMethod.Get, MessageUrl(r.Id), null, cancel).ConfigureAwait(false)); }
            catch (GoogleApiException ex) when (ex.Problem == GoogleApiProblem.NotFound) { /* deleted since the list was made */ }
            finally { gate.Release(); }
        })).ConfigureAwait(false);

        int? unread = null;
        if (badge == GmailBadge.Inbox)
            unread = ParseLabelUnread(await api.SendAsync(HttpMethod.Get, $"{BaseUrl}/labels/INBOX", null, cancel).ConfigureAwait(false));
        else if (badge == GmailBadge.Query)
            unread = QueryHasUnread(query) ? estimate : ParseMessageList(await api.SendAsync(HttpMethod.Get, MessagesUrl("(" + query + ") is:unread", 1), null, cancel).ConfigureAwait(false)).Estimate;

        var labels = wantLabels ? ParseLabels(await api.SendAsync(HttpMethod.Get, $"{BaseUrl}/labels", null, cancel).ConfigureAwait(false)) : [];
        return new GmailFetch(messages.Where(m => m is not null).Select(m => m!).ToList(), unread, labels);
    }

    // ---------- Pure: addresses ----------

    public static string MessagesUrl(string query, int max) =>
        $"{BaseUrl}/messages?maxResults={Math.Clamp(max, 1, MaxMessages)}" + (string.IsNullOrWhiteSpace(query) ? "" : "&q=" + Uri.EscapeDataString(query.Trim()));

    /// <summary>Metadata only: one metadataHeaders parameter per header.</summary>
    public static string MessageUrl(string id) =>
        $"{BaseUrl}/messages/{Uri.EscapeDataString(id)}?format=metadata&metadataHeaders=From&metadataHeaders=Subject&metadataHeaders=Date";

    public static bool QueryHasUnread(string query) => Regex.IsMatch(query ?? "", @"(^|[\s(])is:unread($|[\s)])", RegexOptions.IgnoreCase);

    // ---------- Pure: reading ----------

    public static (List<(string Id, string ThreadId)> Messages, int Estimate) ParseMessageList(string json)
    {
        var result = new List<(string, string)>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        int estimate = GoogleApiClient.Int(root, "resultSizeEstimate");
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("messages", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var m in list.EnumerateArray())
            {
                string id = GoogleApiClient.Str(m, "id");
                if (id.Length > 0) result.Add((id, GoogleApiClient.Str(m, "threadId")));
            }
        return (result, Math.Max(estimate, result.Count));
    }

    public static int? ParseLabelUnread(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("messagesUnread", out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt32(out int count) ? count : null;
    }

    public static List<GmailLabel> ParseLabels(string json)
    {
        var result = new List<GmailLabel>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("labels", out var labels) && labels.ValueKind == JsonValueKind.Array)
            foreach (var l in labels.EnumerateArray())
            {
                string id = GoogleApiClient.Str(l, "id"), name = GoogleApiClient.Str(l, "name");
                if (id.Length > 0) result.Add(new GmailLabel(id, name.Length > 0 ? name : id));
            }
        return result;
    }

    public static GmailMessage? ParseMessageJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ParseMessage(doc.RootElement);
    }

    /// <summary>One messages.get answer (format=metadata) as a trimmed message; null without an id.</summary>
    public static GmailMessage? ParseMessage(JsonElement e)
    {
        string id = GoogleApiClient.Str(e, "id");
        if (id.Length == 0) return null;
        string from = "", subject = "", date = "";
        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("payload", out var payload) && payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("headers", out var headers) && headers.ValueKind == JsonValueKind.Array)
            foreach (var h in headers.EnumerateArray())
            {
                string name = GoogleApiClient.Str(h, "name"), value = GoogleApiClient.Str(h, "value");
                if (name.Equals("From", StringComparison.OrdinalIgnoreCase) && from.Length == 0) from = value;
                else if (name.Equals("Subject", StringComparison.OrdinalIgnoreCase) && subject.Length == 0) subject = value;
                else if (name.Equals("Date", StringComparison.OrdinalIgnoreCase) && date.Length == 0) date = value;
            }
        var (senderName, email) = ParseFrom(from);
        var labels = new List<string>();
        if (e.TryGetProperty("labelIds", out var ids) && ids.ValueKind == JsonValueKind.Array)
            foreach (var l in ids.EnumerateArray()) if (l.ValueKind == JsonValueKind.String && l.GetString() is { Length: > 0 } label && labels.Count < 16) labels.Add(label);
        string cleanSubject = Squash(DecodeHeader(subject));
        return new GmailMessage
        {
            Id = id,
            ThreadId = GoogleApiClient.Str(e, "threadId") is { Length: > 0 } t ? t : id,
            SenderName = senderName,
            SenderEmail = email,
            Subject = Clip(cleanSubject, 200),
            Date = InternalDate(GoogleApiClient.Str(e, "internalDate")) ?? ParseDate(date),
            Snippet = Clip(Squash(WebUtility.HtmlDecode(GoogleApiClient.Str(e, "snippet"))), 240),
            Unread = labels.Contains("UNREAD", StringComparer.Ordinal),
            LabelIds = labels,
        };
    }

    private static string Squash(string text) => Regex.Replace(text, @"\s+", " ").Trim();
    private static string Clip(string text, int max) => text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";

    /// <summary>Gmail's internalDate: milliseconds since 1970 as text.</summary>
    public static DateTimeOffset? InternalDate(string text) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long ms) && ms > 0 && ms < 253_402_300_000_000L ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null;

    /// <summary>A Date header ("Mon, 5 Oct 2026 09:12:00 -0500 (CDT)"); a trailing comment is ignored. Null when unreadable.</summary>
    public static DateTimeOffset? ParseDate(string header)
    {
        string text = Regex.Replace(header ?? "", @"\([^)]*\)", "").Trim();
        return text.Length > 0 && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : null;
    }

    // ---------- Pure: addresses and encoded headers ----------

    /// <summary>
    /// A From header as (display name, address): <c>"Ada Lovelace" &lt;ada@example.com&gt;</c>, <c>ada@example.com</c>,
    /// <c>=?UTF-8?B?...?= &lt;a@b&gt;</c>. The name falls back to the address when the sender has none.
    /// </summary>
    public static (string Name, string Email) ParseFrom(string header)
    {
        header = (header ?? "").Trim();
        string email = "", name = header;
        int open = header.LastIndexOf('<'), close = header.LastIndexOf('>');
        if (open >= 0 && close > open)
        {
            email = header[(open + 1)..close].Trim();
            name = header[..open].Trim();
        }
        else if (header.Contains('@') && !header.Contains(' ')) { email = header; name = ""; }
        name = name.Trim();
        if (name.Length >= 2 && name[0] == '"' && name[^1] == '"') name = name[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
        name = Squash(DecodeHeader(name)).Trim('"', ' ');
        if (name.Length == 0 || name.Equals(email, StringComparison.OrdinalIgnoreCase)) name = email.Length > 0 ? email : "(unknown sender)";
        return (name, email);
    }

    private static readonly Regex EncodedWord = new(@"=\?([^?\s]+)\?([bBqQ])\?([^?\s]*)\?=", RegexOptions.Compiled);

    static GmailService()
    {
        // iso-8859-x, windows-125x and friends are common in older mail.
        try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch (InvalidOperationException) { }
    }

    /// <summary>
    /// Decodes RFC 2047 "encoded words" (<c>=?UTF-8?B?...?=</c> base64 and <c>=?ISO-8859-1?Q?...?=</c> quoted-printable, where "_" is a
    /// space). Whitespace between two encoded words is dropped, and a character split across two words of one charset is rejoined.
    /// Plain text and anything undecodable is left as it is.
    /// </summary>
    public static string DecodeHeader(string? value)
    {
        if (string.IsNullOrEmpty(value) || !value.Contains("=?", StringComparison.Ordinal)) return value ?? "";
        var result = new StringBuilder();
        var pending = new List<byte>();
        string? pendingCharset = null;
        void Flush()
        {
            if (pending.Count == 0) return;
            result.Append(GetEncoding(pendingCharset).GetString(pending.ToArray()));
            pending.Clear();
        }
        int position = 0;
        bool lastWasEncoded = false;
        foreach (Match m in EncodedWord.Matches(value))
        {
            string between = value[position..m.Index];
            byte[]? bytes = DecodeWord(m.Groups[2].Value, m.Groups[3].Value);
            if (bytes is null)
            {
                // Not decodable: keep the text as typed.
                Flush();
                result.Append(between).Append(m.Value);
                lastWasEncoded = false;
                position = m.Index + m.Length;
                continue;
            }
            string charset = m.Groups[1].Value;
            int star = charset.IndexOf('*');
            if (star > 0) charset = charset[..star];
            if (!(lastWasEncoded && string.IsNullOrWhiteSpace(between)))
            {
                Flush();
                result.Append(between);
            }
            else if (!string.Equals(pendingCharset, charset, StringComparison.OrdinalIgnoreCase)) Flush();
            pendingCharset = charset;
            pending.AddRange(bytes);
            lastWasEncoded = true;
            position = m.Index + m.Length;
        }
        Flush();
        result.Append(value[position..]);
        return result.ToString();
    }

    private static Encoding GetEncoding(string? charset)
    {
        try { return Encoding.GetEncoding(charset ?? "utf-8"); }
        catch (ArgumentException) { return Encoding.UTF8; }
    }

    private static byte[]? DecodeWord(string encoding, string text)
    {
        try
        {
            if (encoding is "B" or "b")
            {
                string padded = text.PadRight(text.Length + (4 - text.Length % 4) % 4, '=');
                return Convert.FromBase64String(padded);
            }
            var bytes = new List<byte>(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '_') bytes.Add((byte)' ');
                else if (c == '=' && i + 2 < text.Length && Uri.IsHexDigit(text[i + 1]) && Uri.IsHexDigit(text[i + 2])) { bytes.Add(Convert.ToByte(text.Substring(i + 1, 2), 16)); i += 2; }
                else bytes.AddRange(Encoding.UTF8.GetBytes(c.ToString()));
            }
            return bytes.ToArray();
        }
        catch (FormatException) { return null; }
    }
}
