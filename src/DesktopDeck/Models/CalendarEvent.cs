using System.Globalization;

namespace DesktopDeck.Models;

/// <summary>Where a calendar event comes from.</summary>
public enum CalendarSource
{
    /// <summary>A block on a Schedule widget (stored in settings.json).</summary>
    Local,
    /// <summary>Google Calendar (view and edit).</summary>
    Google,
    /// <summary>A read-only iCal (.ics) link.</summary>
    Ics,
    /// <summary>A personal reminder (Reminders tab).</summary>
    Reminder,
}

/// <summary>
/// One event from any source, in local time. All-day events start at local midnight of their first day and end at
/// the (exclusive) midnight after their last day, like Google's own "date" fields.
/// </summary>
public sealed record CalendarEvent
{
    public CalendarSource Source { get; init; }
    /// <summary>Google calendar id, iCal link id, or the Schedule item id for local blocks.</summary>
    public string CalendarId { get; init; } = "";
    public string CalendarLabel { get; init; } = "";
    /// <summary>"#RRGGBB".</summary>
    public string Color { get; init; } = "#C4684F";
    public string Title { get; init; } = "";
    public DateTime Start { get; init; }
    public DateTime End { get; init; }
    public bool AllDay { get; init; }
    public string Location { get; init; } = "";
    public string Description { get; init; } = "";
    /// <summary>True when Desktop Deck can change it (a Google event on a calendar you may edit).</summary>
    public bool Editable { get; init; }
    public string GoogleId { get; init; } = "";
    public string ETag { get; init; } = "";
    /// <summary>The event's page on Google Calendar, if Google gave one.</summary>
    public string HtmlLink { get; init; } = "";
    /// <summary>Set when this is one occurrence of a repeating Google event (editing it changes only this occurrence).</summary>
    public string RecurringEventId { get; init; } = "";
    /// <summary>Local blocks: which Schedule item and block it came from.</summary>
    public string LocalItemId { get; init; } = "";
    public string LocalBlockId { get; init; } = "";

    public bool IsRecurringInstance => RecurringEventId.Length > 0;
    /// <summary>The last calendar day an all-day event covers (or the day a timed event ends on).</summary>
    public DateOnly FirstDay => DateOnly.FromDateTime(Start);
    public DateOnly LastDay => DateOnly.FromDateTime(AllDay || End.TimeOfDay == TimeSpan.Zero && End > Start ? End.AddTicks(-1) : End);

    /// <summary>Same event from the same place at the same time (used to drop duplicates fetched in overlapping ranges).</summary>
    public string Key => string.Join("|", (int)Source, CalendarId, GoogleId.Length > 0 ? GoogleId : LocalBlockId + ":" + Title, Start.Ticks);

    /// <summary>True when the event touches the half-open range [from, to).</summary>
    public bool Overlaps(DateTime from, DateTime to) => Start < to && (End > from || (End == Start && Start >= from));

    /// <summary>Whether the event covers any part of this calendar day.</summary>
    public bool OnDay(DateOnly day) => Overlaps(day.ToDateTime(TimeOnly.MinValue), day.AddDays(1).ToDateTime(TimeOnly.MinValue));

    /// <summary>Start and end as minutes after midnight on <paramref name="day"/>, clipped to that day (end 1440 = midnight).</summary>
    public (int Start, int End) MinutesOn(DateOnly day)
    {
        var midnight = day.ToDateTime(TimeOnly.MinValue);
        int s = (int)Math.Clamp((Start - midnight).TotalMinutes, 0, 1440);
        int e = (int)Math.Clamp(Math.Ceiling((End - midnight).TotalMinutes), 0, 1440);
        if (e <= s) e = Math.Min(1440, s + 15);
        return (s, e);
    }
}

/// <summary>A Google calendar the signed-in account can see. Saved in settings.json (no secrets).</summary>
public sealed class GoogleCalendarChoice
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>"#RRGGBB" from Google (the calendar's background color).</summary>
    public string Color { get; set; } = "#4285f4";
    /// <summary>Shown on Schedule and Agenda widgets.</summary>
    public bool Visible { get; set; } = true;
    public bool Primary { get; set; }
    /// <summary>"owner", "writer", "reader" or "freeBusyReader".</summary>
    public string AccessRole { get; set; } = "reader";

    [System.Text.Json.Serialization.JsonIgnore]
    public bool CanEdit => AccessRole is "owner" or "writer";
    public GoogleCalendarChoice Copy() => (GoogleCalendarChoice)MemberwiseClone();
}

/// <summary>A read-only iCal link. The address itself is a secret and lives encrypted in secrets.json under <see cref="Entry"/>.</summary>
public sealed class IcsCalendarInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Label { get; set; } = "";
    public string Color { get; set; } = "#14b8a6";
    public bool Enabled { get; set; } = true;
    /// <summary>The secrets.json entry holding the address.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Entry => "ics." + Id;
    public IcsCalendarInfo Copy() => (IcsCalendarInfo)MemberwiseClone();
}

/// <summary>Color helpers for calendars (pure, so tests can cover them).</summary>
public static class CalendarColors
{
    /// <summary>Google's fixed event colors by colorId.</summary>
    private static readonly Dictionary<string, string> EventPalette = new()
    {
        ["1"] = "#a4bdfc", ["2"] = "#7ae7bf", ["3"] = "#dbadff", ["4"] = "#ff887c", ["5"] = "#fbd75b", ["6"] = "#ffb878",
        ["7"] = "#46d6db", ["8"] = "#e1e1e1", ["9"] = "#5484ed", ["10"] = "#51b749", ["11"] = "#dc2127",
    };

    public static string? ForEventColorId(string? colorId) => colorId is not null && EventPalette.TryGetValue(colorId, out var color) ? color : null;

    /// <summary>"#RGB", "#RRGGBB" or "#AARRGGBB" to red, green, blue; null if it isn't one of those.</summary>
    public static (int R, int G, int B)? Parse(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        string h = hex.Trim().TrimStart('#');
        if (h.Length == 3) h = string.Concat(h.Select(c => new string(c, 2)));
        if (h.Length == 8) h = h[2..];
        if (h.Length != 6 || !int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value)) return null;
        return ((value >> 16) & 255, (value >> 8) & 255, value & 255);
    }

    /// <summary>True when dark text reads better on this color.</summary>
    public static bool IsLight(string? hex)
    {
        if (Parse(hex) is not { } c) return false;
        return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0 > 0.6;
    }

    /// <summary>A usable "#RRGGBB" (falls back when the text is not a color).</summary>
    public static string Normalize(string? hex, string fallback = "#4285f4") =>
        Parse(hex) is { } c ? $"#{c.R:x2}{c.G:x2}{c.B:x2}" : fallback;
}

/// <summary>Side-by-side lanes for items that overlap in time, shared by local blocks and external events.</summary>
public static class CalendarLanes
{
    /// <summary>
    /// Gives every item a lane so overlapping items sit side by side. Input does not need to be sorted.
    /// Returns (item, lane, lanes in its overlap cluster).
    /// </summary>
    public static List<(T Item, int Lane, int Lanes)> Assign<T>(IEnumerable<T> items, Func<T, int> start, Func<T, int> end)
    {
        var result = new List<(T, int, int)>();
        var ordered = items.Select(i => (Item: i, S: start(i), E: Math.Max(start(i) + 15, end(i)))).OrderBy(x => x.S).ThenBy(x => x.E).ToList();
        var group = new List<(T Item, int Lane)>();
        var laneEnds = new List<int>();
        int groupEnd = -1;
        foreach (var (item, s, e) in ordered)
        {
            if (s >= groupEnd && group.Count > 0)
            {
                foreach (var g in group) result.Add((g.Item, g.Lane, laneEnds.Count));
                group.Clear();
                laneEnds.Clear();
            }
            int lane = laneEnds.FindIndex(x => x <= s);
            if (lane < 0) { lane = laneEnds.Count; laneEnds.Add(e); } else laneEnds[lane] = e;
            group.Add((item, lane));
            groupEnd = Math.Max(groupEnd, e);
        }
        foreach (var g in group) result.Add((g.Item, g.Lane, laneEnds.Count));
        return result;
    }
}
