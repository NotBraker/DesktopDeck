using System.Net.Http;
using DesktopDeck.Models;

namespace DesktopDeck.Services;

/// <summary>Which external calendars a widget wants.</summary>
/// <param name="Google">Show Google events.</param>
/// <param name="GoogleCalendarIds">Only these Google calendars (still limited to those switched on in Settings); empty means all switched on.</param>
/// <param name="Ics">Show the iCal links.</param>
public sealed record CalendarQuery(bool Google, IReadOnlyCollection<string> GoogleCalendarIds, bool Ics)
{
    public static readonly CalendarQuery All = new(true, [], true);
}

/// <summary>Events for a range plus how fresh they are.</summary>
public sealed record CalendarSnapshot(IReadOnlyList<CalendarEvent> Events, bool Loading, DateTime? LastUpdated, string? Error, bool NeedsReconnect)
{
    public static readonly CalendarSnapshot Empty = new([], false, null, null, false);

    /// <summary>A quiet one-line note for the widget header, or null when everything is fine.</summary>
    public string? Note => Error is null ? null
        : NeedsReconnect ? Error
        : LastUpdated is { } at ? "Offline — showing " + at.ToString("t", System.Globalization.CultureInfo.CurrentCulture) + " data"
        : Error;
}

/// <summary>
/// Keeps Google and iCal events in memory so widgets can draw instantly. Google events are cached per calendar and week,
/// iCal feeds as a whole (and expanded per range). Asking for a range never blocks: stale or missing data is fetched on a
/// background task (at most once every 5 minutes per source; a failed fetch waits 2 minutes before trying again), and
/// <see cref="Updated"/> is raised, from any thread, when something a widget shows actually changed. On failure the last
/// data stays and <see cref="CalendarSnapshot.Note"/> says so.
/// <para>Call <see cref="Query"/> on the UI thread: it copies the calendar lists from the settings there, before any
/// background work starts.</para>
/// </summary>
public sealed class CalendarFeed
{
    public static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(5), RetryAfterFailure = TimeSpan.FromMinutes(2);

    private sealed class WeekEntry
    {
        public List<CalendarEvent> Events = [];
        public DateTime? Fetched;
        public DateTime LastAttempt = DateTime.MinValue, LastUsed;
        public bool Loading, NeedsReconnect;
        public string? Error;
    }

    private sealed class IcsEntry
    {
        public IcsCalendar? Parsed;
        public DateTime? Fetched;
        public DateTime LastAttempt = DateTime.MinValue;
        public bool Loading;
        public string? Error;
        public string Label = "", Color = "";
        public int Hash;
        public Dictionary<(DateTime, DateTime), List<CalendarEvent>> Expanded = new();
    }

    private readonly GoogleAuth auth;
    private readonly GoogleCalendarService google;
    private readonly SecretStore secrets;
    private readonly DeckSettings settings;
    private readonly HttpClient http;
    private readonly object gate = new();
    private readonly Dictionary<string, WeekEntry> weeks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IcsEntry> feeds = new(StringComparer.Ordinal);
    private List<CalendarEvent>? sample;

    public CalendarFeed(GoogleAuth auth, GoogleCalendarService google, SecretStore secrets, DeckSettings settings, HttpClient? http = null)
    {
        this.auth = auth;
        this.google = google;
        this.secrets = secrets;
        this.settings = settings;
        this.http = http ?? CalendarHttp.Client;
    }

    public GoogleAuth Auth => auth;
    public GoogleCalendarService Google => google;
    /// <summary>Raised on a background thread when events or status changed. Marshal to the UI thread before touching it.</summary>
    public event Action? Updated;
    /// <summary>Unexpected failures (for the app's error log). Messages never contain tokens or calendar addresses.</summary>
    public Action<Exception>? Log { get; set; }
    public bool IsSample => sample is not null;

    /// <summary>Smoke renders: serve these made-up events and never touch the network.</summary>
    public void UseSample(IEnumerable<CalendarEvent> events)
    {
        auth.SampleMode = true;
        sample = events.ToList();
    }

    /// <summary>Whether Google events can be shown or edited (connected, with calendar access).</summary>
    public bool GoogleReady => auth.IsConnected && auth.HasScopes(GoogleScopes.CalendarReadOnly);
    public bool GoogleCanEdit => GoogleReady && auth.HasScopes(GoogleScopes.CalendarEvents);

    /// <summary>The Google calendars switched on in Settings, in order.</summary>
    public IReadOnlyList<GoogleCalendarChoice> VisibleGoogleCalendars => settings.GoogleCalendars.Where(c => c.Visible).ToList();
    /// <summary>The calendars new events can be added to.</summary>
    public IReadOnlyList<GoogleCalendarChoice> WritableGoogleCalendars => GoogleCanEdit ? settings.GoogleCalendars.Where(c => c.CanEdit).ToList() : [];

    // ---------- Reading ----------

    /// <summary>
    /// The events touching [from, to) from the wanted external calendars, from memory. Starts background fetches for anything
    /// stale or missing; <see cref="Updated"/> fires when they land.
    /// </summary>
    public CalendarSnapshot Query(DateOnly from, DateOnly toExclusive, CalendarQuery query)
    {
        DateTime start = from.ToDateTime(TimeOnly.MinValue), end = toExclusive.ToDateTime(TimeOnly.MinValue);
        if (sample is { } fake)
        {
            return new CalendarSnapshot(fake.Where(e => (e.Source == CalendarSource.Google ? query.Google : e.Source == CalendarSource.Ics && query.Ics) && e.Overlaps(start, end)).OrderBy(e => e.Start).ToList(), false, DateTime.Now, null, false);
        }

        var calendars = query.Google && GoogleReady
            ? settings.GoogleCalendars.Where(c => c.Visible && (query.GoogleCalendarIds.Count == 0 || query.GoogleCalendarIds.Contains(c.Id))).Select(c => c.Copy()).ToList()
            : [];
        var links = query.Ics ? settings.IcsCalendars.Where(c => c.Enabled).Select(c => c.Copy()).ToList() : [];
        var mondays = new List<DateOnly>();
        for (var monday = Planner.Monday(from); monday < toExclusive; monday = monday.AddDays(7)) mondays.Add(monday);

        var events = new Dictionary<string, CalendarEvent>(StringComparer.Ordinal);
        bool loading = false, reconnect = false;
        string? error = null;
        // The saved sign-in stopped working (it expired or was revoked): say so in the widget instead of silently dropping Google events.
        if (query.Google && !auth.IsConnected && auth.HasClient && settings.GoogleCalendars.Count > 0) { error = GoogleAuthException.Expired; reconnect = true; }
        DateTime? oldest = null;
        var toFetch = new List<(GoogleCalendarChoice Calendar, List<DateOnly> Weeks)>();
        var icsToFetch = new List<IcsCalendarInfo>();
        var now = DateTime.UtcNow;
        lock (gate)
        {
            foreach (var calendar in calendars)
            {
                var stale = new List<DateOnly>();
                foreach (var monday in mondays)
                {
                    string key = WeekKey(calendar.Id, monday);
                    if (!weeks.TryGetValue(key, out var entry)) weeks[key] = entry = new WeekEntry();
                    entry.LastUsed = now;
                    foreach (var e in entry.Events) if (e.Overlaps(start, end)) events[e.Key] = e;
                    loading |= entry.Loading;
                    if (entry.Error is not null) { error ??= entry.Error; reconnect |= entry.NeedsReconnect; }
                    if (entry.Fetched is { } f) oldest = oldest is null || f < oldest ? f : oldest;
                    if (NeedsFetch(entry.Loading, entry.LastAttempt, entry.Error, now)) stale.Add(monday);
                }
                if (stale.Count > 0)
                {
                    foreach (var monday in stale) { var e = weeks[WeekKey(calendar.Id, monday)]; e.Loading = true; e.LastAttempt = now; }
                    toFetch.Add((calendar, stale));
                    loading = true;
                }
            }
            foreach (var link in links)
            {
                if (!feeds.TryGetValue(link.Id, out var entry)) feeds[link.Id] = entry = new IcsEntry();
                if (entry.Label != link.Label || entry.Color != link.Color) { entry.Label = link.Label; entry.Color = link.Color; entry.Expanded.Clear(); }
                if (entry.Parsed is not null)
                {
                    if (!entry.Expanded.TryGetValue((start, end), out var expanded))
                    {
                        if (entry.Expanded.Count > 40) entry.Expanded.Clear();
                        entry.Expanded[(start, end)] = expanded = entry.Parsed.Expand(start, end, link.Id, link.Label, link.Color);
                    }
                    foreach (var e in expanded) events[e.Key] = e;
                }
                loading |= entry.Loading;
                if (entry.Error is not null) error ??= entry.Error;
                if (entry.Fetched is { } f) oldest = oldest is null || f < oldest ? f : oldest;
                if (NeedsFetch(entry.Loading, entry.LastAttempt, entry.Error, now)) { entry.Loading = true; entry.LastAttempt = now; icsToFetch.Add(link); loading = true; }
            }
            Prune(now);
        }
        foreach (var (calendar, stale) in toFetch) _ = Task.Run(() => FetchGoogleAsync(calendar, stale));
        foreach (var link in icsToFetch) _ = Task.Run(() => FetchIcsAsync(link));
        var ordered = events.Values.OrderBy(e => e.Start).ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        return new CalendarSnapshot(ordered, loading, oldest?.ToLocalTime(), error, reconnect);
    }

    private static bool NeedsFetch(bool loading, DateTime lastAttempt, string? error, DateTime now) =>
        !loading && now - lastAttempt >= (error is null ? RefreshEvery : RetryAfterFailure);

    private static string WeekKey(string calendarId, DateOnly monday) => calendarId + "|" + monday.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private void Prune(DateTime now)
    {
        if (weeks.Count <= 240) return;
        foreach (var key in weeks.Where(w => !w.Value.Loading && now - w.Value.LastUsed > TimeSpan.FromHours(1)).Select(w => w.Key).ToList()) weeks.Remove(key);
    }

    // ---------- Background fetching ----------

    private async Task FetchGoogleAsync(GoogleCalendarChoice calendar, List<DateOnly> mondays)
    {
        List<CalendarEvent>? fetched = null;
        string? error = null;
        bool reconnect = false;
        try
        {
            var first = mondays.Min();
            var last = mondays.Max();
            fetched = await google.ListEventsAsync(calendar, first.ToDateTime(TimeOnly.MinValue), last.AddDays(7).ToDateTime(TimeOnly.MinValue)).ConfigureAwait(false);
        }
        catch (GoogleAuthException ex) { error = ex.Message; reconnect = ex.NeedsReconnect; }
        catch (CalendarException ex) { error = ex.Message; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { error = "Could not load Google Calendar events."; Log?.Invoke(ex); }

        bool changed = false;
        lock (gate)
        {
            foreach (var monday in mondays)
            {
                if (!weeks.TryGetValue(WeekKey(calendar.Id, monday), out var entry)) continue;
                entry.Loading = false;
                if (fetched is not null)
                {
                    var mine = fetched.Where(e => e.Overlaps(monday.ToDateTime(TimeOnly.MinValue), monday.AddDays(7).ToDateTime(TimeOnly.MinValue))).OrderBy(e => e.Start).ToList();
                    changed |= !entry.Events.SequenceEqual(mine) || entry.Error is not null || entry.Fetched is null;
                    entry.Events = mine;
                    entry.Fetched = DateTime.UtcNow;
                    entry.Error = null;
                    entry.NeedsReconnect = false;
                }
                else
                {
                    changed |= entry.Error != error;
                    entry.Error = error;
                    entry.NeedsReconnect = reconnect;
                }
            }
        }
        if (changed) Updated?.Invoke();
    }

    private async Task FetchIcsAsync(IcsCalendarInfo link)
    {
        IcsCalendar? parsed = null;
        int hash = 0;
        string? error = null;
        try
        {
            string? address = secrets.Get(link.Entry);
            if (string.IsNullOrEmpty(address)) throw new CalendarException($"The address for \"{link.Label}\" can't be read on this Windows account. Add the link again in Settings.");
            string text = await IcsCalendar.FetchAsync(http, address, link.Label).ConfigureAwait(false);
            hash = text.GetHashCode();
            parsed = IcsCalendar.Parse(text);
        }
        catch (CalendarException ex) { error = ex.Message; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { error = $"Could not read \"{link.Label}\"."; Log?.Invoke(new InvalidOperationException("iCal parsing failed: " + ex.GetType().Name)); }

        bool changed;
        lock (gate)
        {
            // Settings may have cleared the cache while this fetch was running; then the result is simply dropped.
            if (!feeds.TryGetValue(link.Id, out var entry)) return;
            entry.Loading = false;
            if (parsed is not null)
            {
                changed = entry.Parsed is null || entry.Error is not null || entry.Hash != hash;
                entry.Parsed = parsed;
                entry.Hash = hash;
                entry.Expanded.Clear();
                entry.Fetched = DateTime.UtcNow;
                entry.Error = null;
            }
            else
            {
                changed = entry.Error != error;
                entry.Error = error;
            }
        }
        if (changed) Updated?.Invoke();
    }

    // ---------- Control ----------

    /// <summary>Makes everything stale (keeping what is shown) and tells widgets to redraw, which refetches.</summary>
    public void Refresh()
    {
        lock (gate)
        {
            foreach (var w in weeks.Values) { w.LastAttempt = DateTime.MinValue; w.Error = null; }
            foreach (var f in feeds.Values) { f.LastAttempt = DateTime.MinValue; f.Error = null; }
        }
        Updated?.Invoke();
    }

    /// <summary>Drops everything cached (an account was disconnected, a link was removed or calendars were switched).</summary>
    public void Clear(bool raise = true)
    {
        lock (gate) { weeks.Clear(); feeds.Clear(); }
        if (raise) Updated?.Invoke();
    }

    /// <summary>Drops one iCal link's data.</summary>
    public void ForgetLink(string id)
    {
        lock (gate) feeds.Remove(id);
        Updated?.Invoke();
    }

    /// <summary>Reads a calendar address without saving it (for the "Add link" button): the event count and the feed's own name.</summary>
    public async Task<(int Events, string Name)> TestLinkAsync(string address, string label, CancellationToken cancel = default)
    {
        string text = await IcsCalendar.FetchAsync(http, address, string.IsNullOrWhiteSpace(label) ? "this calendar" : label, cancel).ConfigureAwait(false);
        var parsed = IcsCalendar.Parse(text);
        return (parsed.EventCount, parsed.Name);
    }

    // ---------- Editing Google events ----------

    public async Task<CalendarEvent> CreateAsync(GoogleCalendarChoice calendar, EventDraft draft)
    {
        var created = await google.CreateEventAsync(calendar, draft).ConfigureAwait(false);
        Apply(null, created);
        return created;
    }

    /// <summary>Saves changes to an event, first moving it to <paramref name="calendar"/> if that is a different calendar.</summary>
    public async Task<CalendarEvent> UpdateAsync(GoogleCalendarChoice calendar, CalendarEvent existing, EventDraft draft)
    {
        var current = existing;
        if (calendar.Id != existing.CalendarId)
            current = await google.MoveEventAsync(existing, calendar).ConfigureAwait(false);
        var saved = await google.UpdateEventAsync(calendar, current, draft).ConfigureAwait(false);
        Apply(existing, saved);
        return saved;
    }

    public async Task DeleteAsync(CalendarEvent existing)
    {
        await google.DeleteEventAsync(existing).ConfigureAwait(false);
        Apply(existing, null);
    }

    /// <summary>Puts the result of an edit into the cache right away, marks the affected weeks stale and tells widgets to redraw.</summary>
    private void Apply(CalendarEvent? removed, CalendarEvent? added)
    {
        lock (gate)
        {
            if (removed is not null)
                foreach (var entry in weeks.Where(w => w.Key.StartsWith(removed.CalendarId + "|", StringComparison.Ordinal)).Select(w => w.Value))
                {
                    entry.Events.RemoveAll(e => e.GoogleId == removed.GoogleId);
                    entry.LastAttempt = DateTime.MinValue;
                }
            if (added is not null)
                foreach (var monday in WeeksOf(added))
                    if (weeks.TryGetValue(WeekKey(added.CalendarId, monday), out var entry))
                    {
                        entry.Events.RemoveAll(e => e.GoogleId == added.GoogleId);
                        entry.Events.Add(added);
                        entry.LastAttempt = DateTime.MinValue;
                    }
        }
        Updated?.Invoke();
    }

    private static IEnumerable<DateOnly> WeeksOf(CalendarEvent e)
    {
        for (var monday = Planner.Monday(e.FirstDay); monday <= e.LastDay; monday = monday.AddDays(7)) yield return monday;
    }
}
