using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace DesktopDeck.Widgets;

/// <summary>RSS 2.0 / RSS 1.0 / Atom parsing, merging and caching for the News feeds widget. Pure logic (unit tested).</summary>
public static class NewsFeedLogic
{
    /// <summary>One configured feed: "URL", optionally followed by "| Name | #color".</summary>
    public sealed record Source(string Url, string Name, string Color);

    public sealed record Item(string Title, string Link, string Summary, DateTimeOffset? Published, string Feed);

    public sealed record Feed(string Title, List<Item> Items);

    /// <summary>Reads one line of the Feeds option. Returns null for lines that aren't web addresses.</summary>
    public static Source? ParseSource(string line)
    {
        var parts = line.Split('|').Select(p => p.Trim()).ToArray();
        if (parts.Length == 0 || !OnlineLogic.IsWebLink(parts[0])) return null;
        string name = "", color = "";
        foreach (var part in parts.Skip(1))
        {
            if (Regex.IsMatch(part, "^#([0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")) color = part;
            else if (part.Length > 0 && name.Length == 0) name = part;
        }
        return new Source(parts[0], name, color);
    }

    public static List<Source> ParseSources(IEnumerable<string> lines)
        => lines.Select(ParseSource).OfType<Source>().GroupBy(s => s.Url, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();

    /// <summary>Parses an RSS or Atom document. Throws <see cref="FormatException"/> if it is neither.</summary>
    public static Feed Parse(string xml, string feedUrl, int maxItems = 25, int maxSummary = 220)
    {
        XDocument doc;
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true, IgnoreProcessingInstructions = true, MaxCharactersInDocument = 20_000_000 };
            using var reader = XmlReader.Create(new StringReader(xml.TrimStart('﻿', ' ', '\t', '\r', '\n')), settings);
            doc = XDocument.Load(reader);
        }
        catch (XmlException ex) { throw new FormatException("That address isn't an RSS or Atom feed.", ex); }

        var root = doc.Root ?? throw new FormatException("That address isn't an RSS or Atom feed.");
        string kind = root.Name.LocalName.ToLowerInvariant();
        if (kind is not ("rss" or "feed" or "rdf" or "channel")) throw new FormatException("That address isn't an RSS or Atom feed.");
        bool atom = kind == "feed";

        // Title: <channel><title> for RSS (including RSS 1.0, where channel is a sibling of the items), <feed><title> for Atom.
        var channel = atom ? root : root.Name.LocalName == "channel" ? root : Child(root, "channel") ?? root;
        string title = OnlineLogic.StripHtml(Child(channel, "title")?.Value);

        var entries = root.Descendants().Where(e => e.Name.LocalName == (atom ? "entry" : "item"));
        var items = new List<Item>();
        foreach (var entry in entries)
        {
            string itemTitle = OnlineLogic.Clip(OnlineLogic.StripHtml(Child(entry, "title")?.Value), 300);
            string link = atom ? AtomLink(entry) : RssLink(entry);
            if (link.Length > 0 && !OnlineLogic.IsWebLink(link) && Uri.TryCreate(feedUrl, UriKind.Absolute, out var baseUri) && Uri.TryCreate(baseUri, link, out var full)) link = full.ToString();
            if (!OnlineLogic.IsWebLink(link)) link = "";
            string summaryHtml = (atom
                ? Child(entry, "summary") ?? Child(entry, "content")
                : Child(entry, "description") ?? Child(entry, "encoded") ?? Child(entry, "summary"))?.Value ?? "";
            string summary = OnlineLogic.Clip(OnlineLogic.StripHtml(summaryHtml), maxSummary);
            if (itemTitle.Length == 0) itemTitle = OnlineLogic.Clip(summary, 120);
            if (itemTitle.Length == 0) continue;
            if (summary == itemTitle) summary = "";
            var date = atom
                ? ParseDate(Child(entry, "published")?.Value) ?? ParseDate(Child(entry, "updated")?.Value)
                : ParseDate(Child(entry, "pubDate")?.Value) ?? ParseDate(Child(entry, "date")?.Value) ?? ParseDate(Child(entry, "published")?.Value) ?? ParseDate(Child(entry, "updated")?.Value);
            items.Add(new Item(itemTitle, link, summary, date, feedUrl));
            if (items.Count >= maxItems) break;
        }
        return new Feed(title, items);
    }

    private static XElement? Child(XElement parent, string localName) => parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

    private static string AtomLink(XElement entry)
    {
        var links = entry.Elements().Where(e => e.Name.LocalName == "link").ToList();
        var best = links.FirstOrDefault(l => (string?)l.Attribute("rel") is null or "alternate") ?? links.FirstOrDefault();
        return ((string?)best?.Attribute("href") ?? best?.Value ?? "").Trim();
    }

    private static string RssLink(XElement item)
    {
        string link = Child(item, "link")?.Value.Trim() ?? "";
        if (link.Length == 0)
        {
            // Some RSS feeds use an Atom link inside items.
            var atomLink = item.Elements().FirstOrDefault(e => e.Name.LocalName == "link" && e.Attribute("href") is not null);
            link = ((string?)atomLink?.Attribute("href"))?.Trim() ?? "";
        }
        if (link.Length == 0 && Child(item, "guid") is { } guid && !string.Equals((string?)guid.Attribute("isPermaLink"), "false", StringComparison.OrdinalIgnoreCase))
            link = guid.Value.Trim();
        return link;
    }

    private static readonly Dictionary<string, int> Zones = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GMT"] = 0, ["UT"] = 0, ["UTC"] = 0, ["Z"] = 0,
        ["EST"] = -5, ["EDT"] = -4, ["CST"] = -6, ["CDT"] = -5, ["MST"] = -7, ["MDT"] = -6, ["PST"] = -8, ["PDT"] = -7,
        ["BST"] = 1, ["CET"] = 1, ["CEST"] = 2, ["EET"] = 2, ["EEST"] = 3, ["JST"] = 9, ["IST"] = 5, ["AEST"] = 10, ["AEDT"] = 11,
    };

    private static readonly string[] Months = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

    /// <summary>Reads RFC 822 dates ("Mon, 05 Oct 2026 08:30:00 +0200", "5 Oct 26 08:30 EST") and ISO 8601 dates.</summary>
    public static DateTimeOffset? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = OnlineLogic.Squash(text);
        var m = Regex.Match(text, @"^(?:[A-Za-z]{2,9},?\s*)?(\d{1,2})[\s-]+([A-Za-z]{3,9})\.?[\s-]+(\d{2,4})(?:,?\s+|T)(\d{1,2}):(\d{2})(?::(\d{2}))?(?:\.\d+)?\s*(.*)$");
        if (m.Success)
        {
            int month = Array.IndexOf(Months, m.Groups[2].Value[..3].ToLowerInvariant()) + 1;
            int year = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            if (year < 100) year += year < 70 ? 2000 : 1900;
            int day = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), hour = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture), minute = int.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture);
            int second = m.Groups[6].Success ? int.Parse(m.Groups[6].Value, CultureInfo.InvariantCulture) : 0;
            if (month > 0 && day is >= 1 and <= 31 && hour < 24 && minute < 60 && second < 61)
            {
                var offset = ParseZone(m.Groups[7].Value.Trim());
                try { return new DateTimeOffset(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)), hour, minute, Math.Min(second, 59), offset); }
                catch (ArgumentException) { }
            }
        }
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out var parsed) ? parsed : null;
    }

    private static TimeSpan ParseZone(string zone)
    {
        if (zone.Length == 0) return TimeSpan.Zero;
        var m = Regex.Match(zone, @"^(?:GMT|UTC)?([+-])(\d{1,2}):?(\d{2})?");
        if (m.Success)
        {
            int hours = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), minutes = m.Groups[3].Success ? int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
            if (hours > 14 || minutes > 59) return TimeSpan.Zero;
            var span = new TimeSpan(hours, minutes, 0);
            return m.Groups[1].Value == "-" ? -span : span;
        }
        string word = zone.Split(' ')[0];
        return Zones.TryGetValue(word, out int h) ? TimeSpan.FromHours(h) : TimeSpan.Zero;
    }

    /// <summary>
    /// Newest first across feeds (undated items keep their feed order after the dated ones), without duplicates
    /// (same link or same title), at most <paramref name="perFeed"/> per feed (0 = no limit), skipping items older than
    /// <paramref name="maxAgeDays"/> (0 = keep all), and at most <paramref name="max"/> in total.
    /// </summary>
    public static List<Item> Merge(IEnumerable<Item> items, int max, int perFeed, double maxAgeDays, DateTimeOffset now)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var result = new List<Item>();
        var ordered = items.Select((item, index) => (item, index))
            .OrderByDescending(x => x.item.Published.HasValue)
            .ThenByDescending(x => x.item.Published ?? DateTimeOffset.MinValue)
            .ThenBy(x => x.index)
            .Select(x => x.item);
        foreach (var item in ordered)
        {
            if (maxAgeDays > 0 && item.Published is { } published && now - published > TimeSpan.FromDays(maxAgeDays)) continue;
            if (seen.Contains("t:" + item.Title) || (item.Link.Length > 0 && seen.Contains("l:" + item.Link))) continue;
            seen.Add("t:" + item.Title);
            if (item.Link.Length > 0) seen.Add("l:" + item.Link);
            int count = counts.GetValueOrDefault(item.Feed);
            if (perFeed > 0 && count >= perFeed) continue;
            counts[item.Feed] = count + 1;
            result.Add(item);
            if (result.Count >= max) break;
        }
        return result;
    }

    // ---------- Cache: {"feeds":{"url":"title"},"failed":["url"],"items":[{"t","l","s","d","f"}]} ----------

    public sealed record Cache(Dictionary<string, string> Titles, List<string> Failed, List<Item> Items);

    public static string ToCache(Cache cache)
    {
        var payload = new
        {
            feeds = cache.Titles,
            failed = cache.Failed,
            items = cache.Items.Select(i => new { t = i.Title, l = i.Link, s = i.Summary, d = i.Published?.ToUnixTimeSeconds(), f = i.Feed }),
        };
        return JsonSerializer.Serialize(payload);
    }

    /// <summary>Reads the cache; an empty cache for anything unreadable.</summary>
    public static Cache FromCache(string json)
    {
        var titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var failed = new List<string>();
        var items = new List<Item>();
        if (string.IsNullOrWhiteSpace(json)) return new Cache(titles, failed, items);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("feeds", out var feeds) && feeds.ValueKind == JsonValueKind.Object)
                foreach (var p in feeds.EnumerateObject()) titles[p.Name] = p.Value.GetString() ?? "";
            if (root.TryGetProperty("failed", out var bad) && bad.ValueKind == JsonValueKind.Array)
                failed.AddRange(bad.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0));
            if (root.TryGetProperty("items", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var e in list.EnumerateArray())
                {
                    DateTimeOffset? date = e.TryGetProperty("d", out var d) && d.ValueKind == JsonValueKind.Number ? DateTimeOffset.FromUnixTimeSeconds(d.GetInt64()) : null;
                    items.Add(new Item(Str(e, "t"), Str(e, "l"), Str(e, "s"), date, Str(e, "f")));
                }
        }
        catch (JsonException) { }
        return new Cache(titles, failed, items);
    }

    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>What the widget calls a feed: the user's name, else the feed's own title, else its domain.</summary>
    public static string SourceName(Source source, IReadOnlyDictionary<string, string> titles)
    {
        if (source.Name.Length > 0) return source.Name;
        if (titles.TryGetValue(source.Url, out var title) && title.Length > 0) return OnlineLogic.Clip(title, 40);
        return OnlineLogic.Domain(source.Url);
    }
}
