using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace DesktopDeck.Widgets;

/// <summary>Quick capture inbox: entries, ordering (pinned first), auto-clearing done entries and "5 m ago" times. Pure logic (unit tested).</summary>
public static class InboxLogic
{
    /// <summary>One captured thought. Times are local, "yyyy-MM-ddTHH:mm:ss".</summary>
    public sealed class InboxEntry
    {
        public string Id { get; set; } = "";
        public string Text { get; set; } = "";
        public string Created { get; set; } = "";
        public bool Done { get; set; }
        public string DoneAt { get; set; } = "";
        public bool Pinned { get; set; }
        public bool Archived { get; set; }
    }

    private const string TimeFormat = "yyyy-MM-ddTHH:mm:ss";
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string Stamp(DateTime time) => time.ToString(TimeFormat, CultureInfo.InvariantCulture);

    public static DateTime? ParseStamp(string text) =>
        DateTime.TryParseExact(text, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;

    public static List<InboxEntry> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            var list = JsonSerializer.Deserialize<List<InboxEntry>>(json, Json) ?? new();
            list.RemoveAll(e => e is null || string.IsNullOrWhiteSpace(e.Text));
            foreach (var e in list)
            {
                e.Id = string.IsNullOrEmpty(e.Id) ? Guid.NewGuid().ToString("N")[..12] : e.Id;
                e.Created ??= "";
                e.DoneAt ??= "";
            }
            return list.Take(500).ToList();
        }
        catch (JsonException) { return new(); }
    }

    public static string Serialize(List<InboxEntry> entries) => JsonSerializer.Serialize(entries, Json);

    /// <summary>Captures a thought (trimmed; blank text is ignored). Returns the new entry or null.</summary>
    public static InboxEntry? Add(List<InboxEntry> entries, string text, DateTime now)
    {
        text = text.Trim(); if (text.Length > 500) text = text[..500]; if (entries.Count >= 500) return null;
        if (text.Length == 0) return null;
        var entry = new InboxEntry { Id = Guid.NewGuid().ToString("N")[..12], Text = text, Created = Stamp(now) };
        entries.Add(entry);
        return entry;
    }

    public static void SetDone(InboxEntry entry, bool done, DateTime now)
    {
        entry.Done = done;
        entry.DoneAt = done ? Stamp(now) : "";
    }

    /// <summary>Deletes done entries finished more than <paramref name="days"/> days ago (0 = never). Returns how many were removed.</summary>
    public static int AutoClear(List<InboxEntry> entries, DateTime now, int days)
    {
        if (days <= 0) return 0;
        var cutoff = now.AddDays(-days);
        return entries.RemoveAll(e => e.Done && (ParseStamp(e.DoneAt) ?? ParseStamp(e.Created) ?? now) < cutoff);
    }

    /// <summary>The visible entries in display order: pinned first, then by <paramref name="sort"/> ("newest", "oldest", "abc"); done ones last when <paramref name="doneLast"/>.</summary>
    public static List<InboxEntry> Visible(IEnumerable<InboxEntry> entries, string sort, bool showDone, bool doneLast, bool showArchived = false)
    {
        var shown = entries.Where(e => (showArchived || !e.Archived) && (showDone || !e.Done));
        var ordered = shown.OrderByDescending(e => e.Pinned);
        if (doneLast) ordered = ordered.ThenBy(e => e.Done);
        ordered = sort switch
        {
            "oldest" => ordered.ThenBy(e => e.Created, StringComparer.Ordinal),
            "abc" => ordered.ThenBy(e => e.Text, StringComparer.CurrentCultureIgnoreCase),
            _ => ordered.ThenByDescending(e => e.Created, StringComparer.Ordinal),
        };
        return ordered.ToList();
    }

    /// <summary>"now", "5m", "3h", "yesterday", "Tue", "Mar 4".</summary>
    public static string Ago(string created, DateTime now)
    {
        if (ParseStamp(created) is not { } time) return "";
        var age = now - time;
        if (age.TotalMinutes < 1) return "now";
        if (age.TotalMinutes < 60) return $"{(int)age.TotalMinutes}m";
        if (time.Date == now.Date) return $"{(int)age.TotalHours}h";
        if (time.Date == now.Date.AddDays(-1)) return "yesterday";
        if (age.TotalDays < 7) return time.ToString("ddd", CultureInfo.CurrentCulture);
        return time.ToString(time.Year == now.Year ? "MMM d" : "MMM d, yyyy", CultureInfo.CurrentCulture);
    }

    /// <summary>The bullet character for a style ("dot", "dash", "arrow", "star"); empty for "none" and "check" (drawn as a box).</summary>
    public static string Bullet(string style) => style switch
    {
        "dot" => "•",
        "dash" => "–",
        "arrow" => "›",
        "star" => "✶",
        _ => "",
    };
}

