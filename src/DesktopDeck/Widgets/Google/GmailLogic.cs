using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using DesktopDeck.Models;

namespace DesktopDeck.Widgets;

/// <summary>
/// Gmail widget logic (no WPF, no network): the trimmed cache kept in Item.Options["cache.json"], links, grouping by day,
/// sender initials and per-label colors. Message metadata only; the widget is read-only. Unit tested.
/// </summary>
public static class GmailLogic
{
    public const string DefaultQuery = "in:inbox is:unread";
    public const string Host = "mail.google.com";

    /// <summary>What is saved between runs: no bodies, only what the widget draws.</summary>
    public sealed class CacheData
    {
        /// <summary>The unread number for the badge; null when the badge is off.</summary>
        public int? Unread { get; set; }
        public List<GmailMessage> Messages { get; set; } = new();
        /// <summary>Label id to name for the labels the shown messages carry (so label colors can match by name).</summary>
        public Dictionary<string, string> LabelNames { get; set; } = new(StringComparer.Ordinal);
    }

    public sealed record Section(string Key, string Title, List<GmailMessage> Items);

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // ---------- Cache ----------

    /// <summary>The widget's cache text. Snippets and subjects are already clipped by the service; label names are kept only for labels in use.</summary>
    public static string ToCache(IEnumerable<GmailMessage> messages, int? unread, IEnumerable<GmailLabel>? labels = null)
    {
        var list = messages.ToList();
        var used = list.SelectMany(m => m.LabelIds).ToHashSet(StringComparer.Ordinal);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var label in labels ?? []) if (used.Contains(label.Id)) names[label.Id] = label.Name;
        return JsonSerializer.Serialize(new CacheData { Unread = unread, Messages = list, LabelNames = names }, Json);
    }

    public static CacheData FromCache(string? json)
    {
        try
        {
            var data = string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<CacheData>(json, Json);
            if (data is null) return new CacheData();
            data.Messages = (data.Messages ?? new()).Where(m => m is not null && !string.IsNullOrEmpty(m.Id)).ToList();
            data.LabelNames ??= new Dictionary<string, string>(StringComparer.Ordinal);
            return data;
        }
        catch (JsonException) { return new CacheData(); }
    }

    // ---------- Options ----------

    /// <summary>The search to use: what the user typed, or the default when it is empty.</summary>
    public static string QueryOf(string? text) => string.IsNullOrWhiteSpace(text) ? DefaultQuery : text.Trim();

    // ---------- Links ----------

    /// <summary>The message's conversation in Gmail: #inbox/ for mail in the inbox, #all/ for any other label (archived, sent, custom).</summary>
    public static string LinkFor(GmailMessage message) =>
        $"https://{Host}/mail/u/0/#{(message.LabelIds.Contains("INBOX", StringComparer.Ordinal) ? "inbox" : "all")}/{Uri.EscapeDataString(message.ThreadId.Length > 0 ? message.ThreadId : message.Id)}";

    public const string InboxLink = "https://mail.google.com/mail/u/0/#inbox";

    // ---------- Wording ----------

    public static string SubjectOf(GmailMessage m) => m.Subject.Length > 0 ? m.Subject : "(no subject)";

    /// <summary>The badge text: "7", "99+".</summary>
    public static string BadgeText(int unread) => ConnectedLogic.Count(unread, 100);

    /// <summary>One or two letters for the sender's circle: from the name, or from the part of the address before the "@".</summary>
    public static string InitialsOf(GmailMessage m)
    {
        string source = m.SenderName;
        if (source.Contains('@')) source = source[..source.IndexOf('@')];
        string initials = ConnectedLogic.Initials(source);
        return initials == "?" && m.SenderEmail.Length > 0 ? ConnectedLogic.Initials(m.SenderEmail[..Math.Max(1, m.SenderEmail.IndexOf('@'))]) : initials;
    }

    /// <summary>"Today", "Yesterday", the weekday for the last week, then "Oct 3" (and the year for older mail).</summary>
    public static string DayLabel(DateTime day, DateTime today, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        int days = (int)Math.Round((today.Date - day.Date).TotalDays);
        if (days == 0) return "Today";
        if (days == 1) return "Yesterday";
        if (days is > 1 and < 7) return day.ToString("dddd", culture);
        return day.Year == today.Year ? day.ToString("MMM d", culture) : day.ToString("MMM d, yyyy", culture);
    }

    /// <summary>A short age: "now", "5m", "3h", "2d", "3w".</summary>
    public static string AgeOf(GmailMessage m, DateTimeOffset now) => m.Date is { } date ? OnlineLogic.Age(date, now) : "";

    // ---------- Grouping ----------

    /// <summary>The messages to draw, newest first (the order Gmail returned them), cut to <paramref name="max"/>; with <paramref name="groupByDay"/> under day headings.</summary>
    public static List<Section> Layout(IEnumerable<GmailMessage> messages, int max, bool groupByDay, DateTime now, CultureInfo? culture = null)
    {
        var list = messages.Take(Math.Max(1, max)).ToList();
        if (!groupByDay) return list.Count == 0 ? [] : [new Section("all", "", list)];
        var sections = new List<Section>();
        foreach (var group in list.GroupBy(m => m.Date is { } d ? d.ToLocalTime().Date : DateTime.MinValue))
            sections.Add(new Section(group.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), group.Key == DateTime.MinValue ? "Earlier" : DayLabel(group.Key, now, culture), group.ToList()));
        return sections;
    }

    // ---------- Label colors ----------

    /// <summary>"Label | #color" lines. Lines without a valid color are skipped.</summary>
    public static List<(string Label, string Color)> ParseLabelColors(IEnumerable<string> lines)
    {
        var result = new List<(string, string)>();
        foreach (string raw in lines)
        {
            int bar = raw.LastIndexOf('|');
            if (bar <= 0) continue;
            string label = raw[..bar].Trim(), color = raw[(bar + 1)..].Trim();
            if (label.Length > 0 && IsHex(color)) result.Add((label, color));
        }
        return result;
    }

    private static bool IsHex(string text) =>
        text.Length is 4 or 7 or 9 && text[0] == '#' && text.Skip(1).All(Uri.IsHexDigit);

    /// <summary>
    /// The accent for a message: the color of the first rule whose label the message carries. A label matches by its name or id, any
    /// letter case; system labels also match without the "CATEGORY_" start ("promotions", "inbox", "starred").
    /// </summary>
    public static string? AccentFor(GmailMessage message, IReadOnlyList<(string Label, string Color)> rules, IReadOnlyDictionary<string, string> labelNames)
    {
        if (rules.Count == 0) return null;
        var names = new List<string>();
        foreach (string id in message.LabelIds)
        {
            names.Add(id);
            if (labelNames.TryGetValue(id, out var name)) names.Add(name);
            if (id.StartsWith("CATEGORY_", StringComparison.Ordinal)) names.Add(id["CATEGORY_".Length..]);
        }
        foreach (var (label, color) in rules)
            if (names.Any(n => string.Equals(n, label, StringComparison.CurrentCultureIgnoreCase))) return color;
        return null;
    }

    /// <summary>A stable placeholder color for a sender's initials circle.</summary>
    public static int ColorBucket(GmailMessage m, int count) => ConnectedLogic.Bucket(m.SenderEmail.Length > 0 ? m.SenderEmail.ToLowerInvariant() : m.SenderName, count);
}
