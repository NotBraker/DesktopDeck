using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DesktopDeck.Models;

namespace DesktopDeck.Services;

/// <summary>What the user typed into the event editor. For all-day events Start is the first day and End the (exclusive) day after the last.</summary>
public sealed record EventDraft(string Title, DateTime Start, DateTime End, bool AllDay, string Location, string Description);

/// <summary>
/// Google Calendar API v3: list calendars and events, create, change (PATCH) and delete events. JSON reading and writing
/// are static and network-free so tests cover them. Network calls use <see cref="GoogleAuth"/> for the access token and
/// retry once with a fresh token if Google answers 401.
/// </summary>
public sealed class GoogleCalendarService
{
    public const string BaseUrl = "https://www.googleapis.com/calendar/v3";
    private readonly GoogleAuth auth;
    private readonly HttpClient http;

    public GoogleCalendarService(GoogleAuth auth, HttpClient? http = null)
    {
        this.auth = auth;
        this.http = http ?? CalendarHttp.Client;
    }

    // ---------- Network ----------

    /// <summary>The account's calendars. Whether each is switched on defaults to Google's own "selected" flag.</summary>
    public async Task<List<GoogleCalendarChoice>> ListCalendarsAsync(CancellationToken cancel = default)
    {
        var all = new List<GoogleCalendarChoice>();
        string? page = null;
        for (int i = 0; i < 5; i++)
        {
            string url = BaseUrl + "/users/me/calendarList?maxResults=250&minAccessRole=freeBusyReader" + (page is null ? "" : "&pageToken=" + Uri.EscapeDataString(page));
            string json = await SendAsync(HttpMethod.Get, url, null, cancel).ConfigureAwait(false);
            all.AddRange(ParseCalendarList(json, out page));
            if (page is null) break;
        }
        return all;
    }

    /// <summary>The events of one calendar that touch [from, to) (local time); repeating events come as separate instances.</summary>
    public async Task<List<CalendarEvent>> ListEventsAsync(GoogleCalendarChoice calendar, DateTime from, DateTime to, CancellationToken cancel = default)
    {
        var all = new List<CalendarEvent>();
        string? page = null;
        for (int i = 0; i < 8; i++)
        {
            string json = await SendAsync(HttpMethod.Get, ListEventsUrl(calendar.Id, from, to, page), null, cancel).ConfigureAwait(false);
            all.AddRange(ParseEvents(json, calendar, out page));
            if (page is null) break;
        }
        return all;
    }

    public async Task<CalendarEvent> CreateEventAsync(GoogleCalendarChoice calendar, EventDraft draft, CancellationToken cancel = default)
    {
        string json = await SendAsync(HttpMethod.Post, $"{BaseUrl}/calendars/{Uri.EscapeDataString(calendar.Id)}/events", BuildEventJson(draft, null, TimeZoneInfo.Local), cancel).ConfigureAwait(false);
        return ParseEventJson(json, calendar) ?? throw new CalendarException("Google saved the event but sent back something Desktop Deck could not read.");
    }

    /// <summary>Changes only what differs from <paramref name="existing"/>. For an instance of a repeating event this changes that instance only.</summary>
    public async Task<CalendarEvent> UpdateEventAsync(GoogleCalendarChoice calendar, CalendarEvent existing, EventDraft draft, CancellationToken cancel = default)
    {
        string body = BuildEventJson(draft, existing, TimeZoneInfo.Local);
        if (body == "{}") return existing;
        string json = await SendAsync(HttpMethod.Patch, $"{BaseUrl}/calendars/{Uri.EscapeDataString(existing.CalendarId)}/events/{Uri.EscapeDataString(existing.GoogleId)}", body, cancel).ConfigureAwait(false);
        return ParseEventJson(json, calendar) ?? existing;
    }

    /// <summary>Moves an event to another calendar (not possible for one instance of a repeating event).</summary>
    public async Task<CalendarEvent> MoveEventAsync(CalendarEvent existing, GoogleCalendarChoice destination, CancellationToken cancel = default)
    {
        string url = $"{BaseUrl}/calendars/{Uri.EscapeDataString(existing.CalendarId)}/events/{Uri.EscapeDataString(existing.GoogleId)}/move?destination={Uri.EscapeDataString(destination.Id)}";
        string json = await SendAsync(HttpMethod.Post, url, null, cancel).ConfigureAwait(false);
        return ParseEventJson(json, destination) ?? existing with { CalendarId = destination.Id, CalendarLabel = destination.Name };
    }

    /// <summary>Deletes an event (for one instance of a repeating event, only that instance).</summary>
    public async Task DeleteEventAsync(CalendarEvent existing, CancellationToken cancel = default)
    {
        try { await SendAsync(HttpMethod.Delete, $"{BaseUrl}/calendars/{Uri.EscapeDataString(existing.CalendarId)}/events/{Uri.EscapeDataString(existing.GoogleId)}", null, cancel).ConfigureAwait(false); }
        catch (CalendarException ex) when (ex.Message.StartsWith("That event no longer exists", StringComparison.Ordinal)) { /* already gone */ }
    }

    private async Task<string> SendAsync(HttpMethod method, string url, string? body, CancellationToken cancel)
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
                if (!response.IsSuccessStatusCode) throw DescribeError(response.StatusCode, text);
                return text;
            }
            catch (HttpRequestException ex) { throw new CalendarException("Could not reach Google Calendar. Check your internet connection.", ex); }
            catch (TaskCanceledException ex) when (!cancel.IsCancellationRequested) { throw new CalendarException("Google Calendar took too long to answer.", ex); }
        }
    }

    /// <summary>A readable error for a failed Google Calendar call (never includes tokens).</summary>
    public static CalendarException DescribeError(HttpStatusCode status, string body)
    {
        string reason = "", message = "";
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                message = error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? "" : "";
                if (error.TryGetProperty("errors", out var list) && list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0 && list[0].TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String) reason = r.GetString() ?? "";
            }
        }
        catch (JsonException) { }
        int code = (int)status;
        return status switch
        {
            HttpStatusCode.Unauthorized => new GoogleAuthException(GoogleAuthException.Expired, true),
            HttpStatusCode.NotFound or HttpStatusCode.Gone => new CalendarException("That event no longer exists on Google Calendar."),
            HttpStatusCode.Forbidden when reason is "insufficientPermissions" or "ACCESS_TOKEN_SCOPE_INSUFFICIENT" => new CalendarException("Google Calendar access is view-only. Press Connect in Settings → Connected accounts to allow editing."),
            HttpStatusCode.Forbidden when reason is "accessNotConfigured" || message.Contains("has not been used in project", StringComparison.OrdinalIgnoreCase) => new CalendarException("The Google Calendar API is not switched on for your Google Cloud project. Enable it in the Google Cloud console (APIs & Services → Library)."),
            HttpStatusCode.Forbidden when reason is "rateLimitExceeded" or "userRateLimitExceeded" => new CalendarException("Google is limiting requests right now. Desktop Deck will try again later."),
            HttpStatusCode.Forbidden => new CalendarException("Google Calendar refused that change" + (message.Length > 0 ? ": " + message : "") + "."),
            HttpStatusCode.TooManyRequests => new CalendarException("Google is limiting requests right now. Desktop Deck will try again later."),
            _ => new CalendarException($"Google Calendar returned an error (HTTP {code})" + (message.Length > 0 ? ": " + message : "") + "."),
        };
    }

    // ---------- Pure: addresses ----------

    /// <summary>The events request for a time range (singleEvents expands repeats into instances).</summary>
    public static string ListEventsUrl(string calendarId, DateTime from, DateTime to, string? pageToken = null)
    {
        static string Utc(DateTime local) => DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        string url = $"{BaseUrl}/calendars/{Uri.EscapeDataString(calendarId)}/events?singleEvents=true&orderBy=startTime&maxResults=250&timeMin={Uri.EscapeDataString(Utc(from))}&timeMax={Uri.EscapeDataString(Utc(to))}";
        return pageToken is null ? url : url + "&pageToken=" + Uri.EscapeDataString(pageToken);
    }

    // ---------- Pure: reading ----------

    public static List<GoogleCalendarChoice> ParseCalendarList(string json) => ParseCalendarList(json, out _);

    public static List<GoogleCalendarChoice> ParseCalendarList(string json, out string? nextPageToken)
    {
        var result = new List<GoogleCalendarChoice>();
        nextPageToken = null;
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("nextPageToken", out var next) && next.ValueKind == JsonValueKind.String) nextPageToken = next.GetString();
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in items.EnumerateArray())
        {
            string id = Str(item, "id");
            if (id.Length == 0 || Bool(item, "deleted")) continue;
            string name = Str(item, "summaryOverride");
            if (name.Length == 0) name = Str(item, "summary");
            bool primary = Bool(item, "primary");
            result.Add(new GoogleCalendarChoice
            {
                Id = id,
                Name = name.Length > 0 ? name : id,
                Color = CalendarColors.Normalize(Str(item, "backgroundColor"), "#4285f4"),
                Primary = primary,
                AccessRole = Str(item, "accessRole") is { Length: > 0 } role ? role : "reader",
                // What the user has ticked in Google Calendar itself; the primary calendar is always on by default.
                Visible = primary || Bool(item, "selected"),
            });
        }
        return result;
    }

    /// <summary>
    /// Updates the saved list from a fresh one: keeps the user's show/hide choices, adds new calendars, drops the ones that are gone.
    /// </summary>
    public static List<GoogleCalendarChoice> MergeCalendars(IEnumerable<GoogleCalendarChoice> saved, IEnumerable<GoogleCalendarChoice> fresh)
    {
        var old = saved.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var merged = new List<GoogleCalendarChoice>();
        foreach (var calendar in fresh)
        {
            if (old.TryGetValue(calendar.Id, out var before)) calendar.Visible = before.Visible;
            merged.Add(calendar);
        }
        return merged.OrderByDescending(c => c.Primary).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static List<CalendarEvent> ParseEvents(string json, GoogleCalendarChoice calendar) => ParseEvents(json, calendar, out _);

    public static List<CalendarEvent> ParseEvents(string json, GoogleCalendarChoice calendar, out string? nextPageToken)
    {
        var result = new List<CalendarEvent>();
        nextPageToken = null;
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("nextPageToken", out var next) && next.ValueKind == JsonValueKind.String) nextPageToken = next.GetString();
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in items.EnumerateArray())
            if (ParseEvent(item, calendar) is { } parsed) result.Add(parsed);
        return result;
    }

    public static CalendarEvent? ParseEventJson(string json, GoogleCalendarChoice calendar)
    {
        using var doc = JsonDocument.Parse(json);
        return ParseEvent(doc.RootElement, calendar);
    }

    /// <summary>One event resource as an event in local time; null for cancelled or declined events and ones without a start.</summary>
    public static CalendarEvent? ParseEvent(JsonElement e, GoogleCalendarChoice calendar)
    {
        if (e.ValueKind != JsonValueKind.Object || Str(e, "status") == "cancelled") return null;
        if (e.TryGetProperty("attendees", out var attendees) && attendees.ValueKind == JsonValueKind.Array
            && attendees.EnumerateArray().Any(a => Bool(a, "self") && Str(a, "responseStatus") == "declined")) return null;
        if (!e.TryGetProperty("start", out var start) || !TryReadTime(start, out var from, out bool allDay)) return null;
        DateTime to;
        if (!e.TryGetProperty("end", out var end) || !TryReadTime(end, out to, out _)) to = allDay ? from.AddDays(1) : from;
        if (allDay && to <= from) to = from.AddDays(1);
        string title = Str(e, "summary").Trim();
        string type = Str(e, "eventType");
        return new CalendarEvent
        {
            Source = CalendarSource.Google,
            CalendarId = calendar.Id,
            CalendarLabel = calendar.Name,
            Color = CalendarColors.ForEventColorId(Str(e, "colorId")) ?? calendar.Color,
            Title = title.Length == 0 ? "(No title)" : title,
            Start = from,
            End = to,
            AllDay = allDay,
            Location = Str(e, "location").Trim(),
            Description = PlainText(Str(e, "description")),
            Editable = calendar.CanEdit && type is "" or "default" or "outOfOffice" or "focusTime" or "workingLocation",
            GoogleId = Str(e, "id"),
            ETag = Str(e, "etag"),
            HtmlLink = Str(e, "htmlLink"),
            RecurringEventId = Str(e, "recurringEventId"),
        };
    }

    /// <summary>Reads a "start" or "end" object: a date (all-day) or a dateTime with an offset, converted to local time.</summary>
    private static bool TryReadTime(JsonElement time, out DateTime local, out bool allDay)
    {
        local = default;
        allDay = false;
        if (time.ValueKind != JsonValueKind.Object) return false;
        if (time.TryGetProperty("dateTime", out var dateTime) && dateTime.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(dateTime.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var moment))
        {
            local = DateTime.SpecifyKind(moment.LocalDateTime, DateTimeKind.Unspecified);
            return true;
        }
        if (time.TryGetProperty("date", out var date) && date.ValueKind == JsonValueKind.String
            && DateTime.TryParseExact(date.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            local = day;
            allDay = true;
            return true;
        }
        return false;
    }

    private static string Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static bool Bool(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>Google descriptions may contain a little HTML; show them as text.</summary>
    public static string PlainText(string html)
    {
        if (html.Length == 0 || !html.Contains('<') && !html.Contains('&')) return html.Trim();
        string text = Regex.Replace(html, @"<\s*br\s*/?\s*>|</\s*(p|div|li)\s*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", "");
        return WebUtility.HtmlDecode(text).Trim();
    }

    // ---------- Pure: writing ----------

    /// <summary>
    /// The JSON body to create an event (<paramref name="existing"/> null) or to PATCH one: only the fields that changed.
    /// Switching between all-day and timed clears the other kind of time. "{}" means nothing changed.
    /// </summary>
    public static string BuildEventJson(EventDraft draft, CalendarEvent? existing, TimeZoneInfo zone)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            bool patch = existing is not null;
            if (!patch || draft.Title.Trim() != existing!.Title) w.WriteString("summary", draft.Title.Trim());
            if (!patch ? draft.Location.Length > 0 : draft.Location.Trim() != existing!.Location) w.WriteString("location", draft.Location.Trim());
            if (!patch ? draft.Description.Length > 0 : draft.Description.Trim() != existing!.Description) w.WriteString("description", draft.Description.Trim());
            bool timeChanged = !patch || draft.AllDay != existing!.AllDay || draft.Start != existing.Start || draft.End != existing.End;
            if (timeChanged)
            {
                WriteTime(w, "start", draft.Start, draft.AllDay, patch, zone);
                WriteTime(w, "end", draft.End, draft.AllDay, patch, zone);
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteTime(Utf8JsonWriter w, string name, DateTime value, bool allDay, bool patch, TimeZoneInfo zone)
    {
        w.WriteStartObject(name);
        if (allDay)
        {
            w.WriteString("date", value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            if (patch) w.WriteNull("dateTime");
        }
        else
        {
            w.WriteString("dateTime", FormatWithOffset(value, zone));
            if (IanaId(zone) is { } iana) w.WriteString("timeZone", iana);
            if (patch) w.WriteNull("date");
        }
        w.WriteEndObject();
    }

    /// <summary>"2026-10-05T09:00:00-05:00": a local wall-clock time with its UTC offset in the zone.</summary>
    public static string FormatWithOffset(DateTime wall, TimeZoneInfo zone)
    {
        wall = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        var offset = zone.IsInvalidTime(wall) ? zone.GetUtcOffset(wall.AddHours(1)) : zone.GetUtcOffset(wall);
        string sign = offset < TimeSpan.Zero ? "-" : "+";
        offset = offset.Duration();
        return wall.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) + $"{sign}{(int)offset.TotalHours:00}:{offset.Minutes:00}";
    }

    private static string? IanaId(TimeZoneInfo zone)
    {
        if (zone.Id.Contains('/')) return zone.Id;
        if (zone.Id == "UTC") return "UTC";
        return TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? iana : null;
    }
}
