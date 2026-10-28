using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DesktopDeck.Widgets;

/// <summary>Notion API parsing and request building for the Notion widget. Pure logic (unit tested, no network).</summary>
public static class NotionLogic
{
    public const string Api = "https://api.notion.com/v1";
    public const string Version = "2022-06-28";
    public const int MaxItems = 50;

    public sealed record Option(string Name, string Color);

    /// <summary>The parts of a database's schema the widget uses.</summary>
    public sealed class Schema
    {
        public string DatabaseTitle { get; set; } = "";
        public string TitleProp { get; set; } = "";
        public string StatusProp { get; set; } = "";
        /// <summary>"status", "select", "checkbox" or "" when there's no status-like property.</summary>
        public string StatusType { get; set; } = "";
        public string DateProp { get; set; } = "";
        public List<Option> Options { get; set; } = [];
        public List<string> DoneNames { get; set; } = [];
    }

    public sealed record Row(string Id, string Title, string Url, string Status, string StatusColor, bool Done, string Date, string DateEnd, DateTimeOffset Edited);

    public sealed class Data
    {
        public Schema Schema { get; set; } = new();
        public List<Row> Rows { get; set; } = [];
    }

    // ---------- Database id ----------

    private static readonly Regex IdPattern = new(@"(?<![0-9a-fA-F])[0-9a-fA-F]{8}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{12}(?![0-9a-fA-F])", RegexOptions.Compiled);

    /// <summary>
    /// The database id (lowercase 8-4-4-4-12) from a bare id, a dashed id or a Notion web address. In an address the
    /// id sits in the path; the "?v=" part is the view's id and is ignored. Null if there is none.
    /// </summary>
    public static string? ExtractId(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string candidate = text.Trim();
        int cut = candidate.IndexOfAny(['?', '#']);
        if (cut >= 0) candidate = candidate[..cut];
        var matches = IdPattern.Matches(candidate);
        if (matches.Count == 0) return null;
        string hex = matches[^1].Value.Replace("-", "").ToLowerInvariant();
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
    }

    public static string DatabaseUrl(string id) => $"{Api}/databases/{id}";
    public static string QueryUrl(string id) => $"{Api}/databases/{id}/query";
    public static string PagesUrl => $"{Api}/pages";

    // ---------- Schema ----------

    private static readonly Regex DoneWords = new(@"^\W*(done|complete[d]?|finished|closed|shipped|resolved|released|archived|cancel(l)?ed)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex StatusWords = new(@"status|state|stage|progress", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CheckWords = new(@"done|complete|finished|checked|✓", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Finds the title, status and date properties in GET /databases/{id}. Names you choose
    /// (<paramref name="statusName"/>, <paramref name="dateName"/>) win; otherwise the first Status property, a Select called
    /// Status/State/Stage, a "Done" checkbox, and the first Date property (preferring one called Due).
    /// </summary>
    public static Schema DetectSchema(string json, string? statusName = null, string? dateName = null)
    {
        var root = Parse(json);
        var schema = new Schema { DatabaseTitle = PlainText(root, "title") };
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("properties", out var props) || props.ValueKind != JsonValueKind.Object)
            throw new FormatException("Notion sent something unexpected.");
        var all = props.EnumerateObject().Select(p => (Name: p.Name, Type: Str(p.Value, "type"), Value: p.Value)).ToList();
        schema.TitleProp = all.FirstOrDefault(p => p.Type == "title").Name ?? "";
        if (schema.TitleProp.Length == 0) throw new FormatException("This Notion database has no title property.");

        (string Name, string Type, JsonElement Value)? Named(string? name, params string[] types)
            => string.IsNullOrWhiteSpace(name) ? null : all.Where(p => types.Contains(p.Type) && string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)).Select(p => ((string, string, JsonElement)?)p).FirstOrDefault();

        var status = Named(statusName, "status", "select", "checkbox")
            ?? all.Where(p => p.Type == "status").Select(p => ((string, string, JsonElement)?)p).FirstOrDefault()
            ?? all.Where(p => p.Type == "select" && StatusWords.IsMatch(p.Name)).Select(p => ((string, string, JsonElement)?)p).FirstOrDefault()
            ?? all.Where(p => p.Type == "checkbox" && CheckWords.IsMatch(p.Name)).Select(p => ((string, string, JsonElement)?)p).FirstOrDefault();
        if (status is { } s)
        {
            schema.StatusProp = s.Name;
            schema.StatusType = s.Type;
            if (s.Type != "checkbox" && s.Value.TryGetProperty(s.Type, out var body) && body.ValueKind == JsonValueKind.Object && body.TryGetProperty("options", out var options) && options.ValueKind == JsonValueKind.Array)
                schema.Options = options.EnumerateArray().Select(o => new Option(Str(o, "name"), Str(o, "color"))).Where(o => o.Name.Length > 0).ToList();
            if (s.Type == "status") schema.DoneNames = StatusDoneNames(s.Value, schema.Options);
            else if (s.Type == "select") schema.DoneNames = schema.Options.Where(o => DoneWords.IsMatch(o.Name)).Select(o => o.Name).ToList();
        }

        var date = Named(dateName, "date")
            ?? all.Where(p => p.Type == "date" && p.Name.Contains("due", StringComparison.OrdinalIgnoreCase)).Select(p => ((string, string, JsonElement)?)p).FirstOrDefault()
            ?? all.Where(p => p.Type == "date").Select(p => ((string, string, JsonElement)?)p).FirstOrDefault();
        if (date is { } d) schema.DateProp = d.Name;
        return schema;
    }

    /// <summary>For a Status property: the options in a group called Complete (or named like Done).</summary>
    private static List<string> StatusDoneNames(JsonElement property, List<Option> options)
    {
        var names = new List<string>();
        if (property.TryGetProperty("status", out var status) && status.TryGetProperty("groups", out var groups) && groups.ValueKind == JsonValueKind.Array
            && status.TryGetProperty("options", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            var byId = list.EnumerateArray().ToDictionary(o => Str(o, "id"), o => Str(o, "name"), StringComparer.Ordinal);
            foreach (var group in groups.EnumerateArray())
            {
                string groupName = Str(group, "name");
                if (!groupName.Equals("Complete", StringComparison.OrdinalIgnoreCase) && !groupName.Equals("Completed", StringComparison.OrdinalIgnoreCase) && !groupName.Equals("Done", StringComparison.OrdinalIgnoreCase)) continue;
                if (group.TryGetProperty("option_ids", out var ids) && ids.ValueKind == JsonValueKind.Array)
                    foreach (var id in ids.EnumerateArray())
                        if (id.ValueKind == JsonValueKind.String && byId.TryGetValue(id.GetString() ?? "", out var name) && name.Length > 0) names.Add(name);
            }
        }
        if (names.Count == 0) names.AddRange(options.Where(o => DoneWords.IsMatch(o.Name)).Select(o => o.Name));
        return names.Distinct(StringComparer.Ordinal).ToList();
    }

    // ---------- Query / create ----------

    public static readonly string[] Sorts = ["date-asc", "date-desc", "edited", "created"];

    /// <summary>The body for POST /databases/{id}/query: how many, optionally without done items, in the chosen order.</summary>
    public static string BuildQuery(Schema schema, bool hideDone, string sort, int count)
    {
        var body = new Dictionary<string, object> { ["page_size"] = Math.Clamp(count, 1, 100) };
        if (hideDone && DoneFilter(schema) is { } filter) body["filter"] = filter;
        object sorts = (sort, schema.DateProp.Length > 0) switch
        {
            ("date-asc", true) => new object[] { new Dictionary<string, object> { ["property"] = schema.DateProp, ["direction"] = "ascending" } },
            ("date-desc", true) => new object[] { new Dictionary<string, object> { ["property"] = schema.DateProp, ["direction"] = "descending" } },
            ("created", _) => new object[] { new Dictionary<string, object> { ["timestamp"] = "created_time", ["direction"] = "descending" } },
            _ => new object[] { new Dictionary<string, object> { ["timestamp"] = "last_edited_time", ["direction"] = "descending" } },
        };
        body["sorts"] = sorts;
        return JsonSerializer.Serialize(body);
    }

    private static object? DoneFilter(Schema schema)
    {
        if (schema.StatusProp.Length == 0) return null;
        if (schema.StatusType == "checkbox") return new Dictionary<string, object> { ["property"] = schema.StatusProp, ["checkbox"] = new Dictionary<string, object> { ["equals"] = false } };
        if (schema.DoneNames.Count == 0 || (schema.StatusType != "status" && schema.StatusType != "select")) return null;
        var filters = schema.DoneNames.Select(n => (object)new Dictionary<string, object> { ["property"] = schema.StatusProp, [schema.StatusType] = new Dictionary<string, object> { ["does_not_equal"] = n } }).ToList();
        return filters.Count == 1 ? filters[0] : new Dictionary<string, object> { ["and"] = filters };
    }

    /// <summary>The body for POST /pages: a new page in the database with a title and optionally a starting status and today's date.</summary>
    public static string BuildNewPage(string databaseId, Schema schema, string title, string? status, DateTime? date)
    {
        var props = new Dictionary<string, object>
        {
            [schema.TitleProp] = new Dictionary<string, object> { ["title"] = new object[] { new Dictionary<string, object> { ["text"] = new Dictionary<string, object> { ["content"] = OnlineLogic.Clip(title, 1900) } } } },
        };
        if (!string.IsNullOrWhiteSpace(status) && schema.StatusProp.Length > 0)
        {
            if (schema.StatusType is "status" or "select") props[schema.StatusProp] = new Dictionary<string, object> { [schema.StatusType] = new Dictionary<string, object> { ["name"] = status.Trim() } };
        }
        if (date is { } day && schema.DateProp.Length > 0)
            props[schema.DateProp] = new Dictionary<string, object> { ["date"] = new Dictionary<string, object> { ["start"] = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) } };
        return JsonSerializer.Serialize(new Dictionary<string, object> { ["parent"] = new Dictionary<string, object> { ["database_id"] = databaseId }, ["properties"] = props });
    }

    // ---------- Rows ----------

    /// <summary>Reads the results of a database query. Archived pages are skipped. Throws <see cref="FormatException"/> if it isn't a result list.</summary>
    public static List<Row> ParseRows(string json, Schema schema)
    {
        var root = Parse(json);
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) throw new FormatException("Notion sent something unexpected.");
        var rows = new List<Row>();
        foreach (var page in results.EnumerateArray())
        {
            if (page.ValueKind != JsonValueKind.Object || Bool(page, "archived") || Bool(page, "in_trash")) continue;
            string id = Str(page, "id"), url = Str(page, "url");
            if (id.Length == 0 || !page.TryGetProperty("properties", out var props) || props.ValueKind != JsonValueKind.Object) continue;
            string title = TitleOf(props, schema.TitleProp);
            string status = "", color = "";
            bool done = false;
            if (schema.StatusProp.Length > 0 && props.TryGetProperty(schema.StatusProp, out var sp) && sp.ValueKind == JsonValueKind.Object)
            {
                switch (schema.StatusType)
                {
                    case "checkbox": done = Bool(sp, "checkbox"); break;
                    case "status" or "select":
                        if (sp.TryGetProperty(schema.StatusType, out var chosen) && chosen.ValueKind == JsonValueKind.Object)
                        {
                            status = Str(chosen, "name");
                            color = Str(chosen, "color");
                            if (color.Length == 0) color = schema.Options.FirstOrDefault(o => o.Name == status)?.Color ?? "";
                            done = schema.DoneNames.Contains(status, StringComparer.Ordinal);
                        }
                        break;
                }
            }
            string start = "", end = "";
            if (schema.DateProp.Length > 0 && props.TryGetProperty(schema.DateProp, out var dp) && dp.ValueKind == JsonValueKind.Object && dp.TryGetProperty("date", out var date) && date.ValueKind == JsonValueKind.Object)
            {
                start = Str(date, "start");
                end = Str(date, "end");
            }
            rows.Add(new Row(id, title.Length > 0 ? title : "Untitled", OnlineLogic.IsWebLink(url) ? url : "", status, color, done, start, end,
                DateTimeOffset.TryParse(Str(page, "last_edited_time"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var edited) ? edited : DateTimeOffset.MinValue));
        }
        return rows;
    }

    private static string TitleOf(JsonElement props, string titleProp)
    {
        if (titleProp.Length > 0 && props.TryGetProperty(titleProp, out var t) && PlainText(t, "title") is { Length: > 0 } text) return text;
        foreach (var p in props.EnumerateObject())
            if (Str(p.Value, "type") == "title") return PlainText(p.Value, "title");
        return "";
    }

    /// <summary>Joins the plain_text pieces of a rich-text array property ("title").</summary>
    private static string PlainText(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array) return "";
        return OnlineLogic.Squash(string.Concat(arr.EnumerateArray().Select(p => Str(p, "plain_text"))));
    }

    /// <summary>Applies the shown count, hiding done items client-side too (a checkbox or status could have been changed since the query).</summary>
    public static List<Row> Visible(IEnumerable<Row> rows, bool hideDone, string sort, int max)
        => Sort(rows.Where(r => !(hideDone && r.Done)), sort).Take(Math.Max(0, max)).ToList();

    /// <summary>Date order puts items with a date first (soonest or latest first), then the rest by last edit.</summary>
    public static List<Row> Sort(IEnumerable<Row> rows, string sort)
    {
        var list = rows.ToList();
        switch (sort)
        {
            case "date-asc":
            case "date-desc":
                var dated = list.Where(r => r.Date.Length > 0).Select(r => (Row: r, When: SortKey(r.Date))).ToList();
                var ordered = sort == "date-asc" ? dated.OrderBy(x => x.When) : dated.OrderByDescending(x => x.When);
                return ordered.Select(x => x.Row).Concat(list.Where(r => r.Date.Length == 0).OrderByDescending(r => r.Edited)).ToList();
            default:
                return list.OrderByDescending(r => r.Edited).ToList();
        }
    }

    private static DateTime SortKey(string date) => ParseDate(date)?.Date ?? DateTime.MaxValue;

    /// <summary>Items grouped by status in the database's own option order; items without one go last. Empty groups are dropped.</summary>
    public static List<(string Name, string Color, List<Row> Rows)> Group(IEnumerable<Row> rows, Schema schema)
    {
        var list = rows.ToList();
        var groups = new List<(string, string, List<Row>)>();
        foreach (var option in schema.Options)
        {
            var inGroup = list.Where(r => r.Status == option.Name).ToList();
            if (inGroup.Count > 0) groups.Add((option.Name, option.Color, inGroup));
        }
        var known = schema.Options.Select(o => o.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var other in list.Where(r => r.Status.Length > 0 && !known.Contains(r.Status)).GroupBy(r => r.Status))
            groups.Add((other.Key, other.First().StatusColor, other.ToList()));
        var none = list.Where(r => r.Status.Length == 0).ToList();
        if (none.Count > 0) groups.Add((schema.StatusType == "checkbox" ? "To do" : "No status", "default", none));
        return groups;
    }

    // ---------- Dates ----------

    /// <summary>The calendar date of a Notion date value (date-only or a date with time and offset), in local time.</summary>
    public static DateTime? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (text.Length == 10 && DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) return day;
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var moment)) return moment.ToLocalTime().DateTime;
        return null;
    }

    /// <summary>"Today", "Tomorrow", "Yesterday", "Fri 9" within a week, "Oct 12" otherwise (with the year when it isn't this year); overdue if before today.</summary>
    public static (string Text, bool Overdue, bool Today) DateLabel(string? start, DateTime now, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (ParseDate(start) is not { } date) return ("", false, false);
        int days = (int)(date.Date - now.Date).TotalDays;
        string text = days switch
        {
            0 => "Today",
            1 => "Tomorrow",
            -1 => "Yesterday",
            > 1 and < 7 => date.ToString("ddd d", culture),
            _ => date.Year == now.Year ? date.ToString("MMM d", culture) : date.ToString("MMM d, yyyy", culture),
        };
        if (start!.Length > 10 && days is >= -1 and <= 1) text += ", " + date.ToString("t", culture);
        return (text, days < 0, days == 0);
    }

    // ---------- Colors, errors ----------

    /// <summary>Notion's option colors as hex that reads on a dark card.</summary>
    public static string ColorHex(string? name) => name switch
    {
        "gray" => "#979a9b",
        "brown" => "#c08a6e",
        "orange" => "#ffa344",
        "yellow" => "#ffdc49",
        "green" => "#4dab9a",
        "blue" => "#529cca",
        "purple" => "#9a6dd7",
        "pink" => "#e255a1",
        "red" => "#ff7369",
        _ => "#9b9b9b",
    };

    /// <summary>A short friendly reason for a failed Notion request (<paramref name="body"/> is the error JSON, if any).</summary>
    public static string Problem(int status, string? body)
    {
        string code = "", message = "";
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                code = Str(doc.RootElement, "code");
                message = Str(doc.RootElement, "message");
            }
            catch (JsonException) { }
        }
        if (status == 401) return "Notion rejected the token. Right-click, Disconnect, then connect with a new token.";
        if (status == 404 || code == "object_not_found") return "Notion can't see this database. Open it in Notion, choose Connections in the ••• menu and add your integration.";
        if (status == 403) return "This integration isn't allowed to do that. Check its capabilities at notion.so/my-integrations.";
        if (status == 400 && message.Length > 0) return "Notion: " + OnlineLogic.Clip(message, 140);
        if (status == 429) return "Notion is rate limiting requests. Slowing down.";
        return ConnectedLogic.GenericProblem(status, false, false);
    }

    // ---------- Cache ----------

    public static string ToCache(Data data) => JsonSerializer.Serialize(data);

    public static Data FromCache(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Data();
        try { return JsonSerializer.Deserialize<Data>(json) ?? new Data(); }
        catch (JsonException) { return new Data(); }
    }

    // ---------- JSON helpers ----------

    private static JsonElement Parse(string json)
    {
        try { using var doc = JsonDocument.Parse(json); return doc.RootElement.Clone(); }
        catch (JsonException ex) { throw new FormatException("Notion sent something unexpected.", ex); }
    }
    private static string Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
    private static bool Bool(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
}
