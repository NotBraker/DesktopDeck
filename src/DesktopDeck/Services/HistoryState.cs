using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using DesktopDeck.Models;

namespace DesktopDeck.Services;

/// <summary>
/// What undo covers and how a change is described. A history state is the serialized settings reduced to <see cref="Scope"/> (the widgets, layouts,
/// look and the recycle bin) and nothing tied to accounts, secrets or the machine (Google, start-up), so undoing never disconnects
/// or reconnects anything. Background noise (weather and mail caches, Google task bookkeeping) is ignored when deciding whether something changed.
/// No UI; works on JSON text so it can run without a deck.
/// </summary>
public static class HistoryState
{
    /// <summary>The settings properties kept in a history state (as serialized).</summary>
    public static readonly string[] Scope =
    [
        "SchemaVersion", "Items", "Layouts", "ActiveFloatingLayout", "ActiveBackgroundLayout", "Appearance",
        "FloatsHidden", "SnapToGrid", "KeepCenterClear", "ShowTitle", "Trash",
    ];

    /// <summary>The properties restoring a state writes back (everything in <see cref="Scope"/> except the schema version).</summary>
    public static readonly string[] Restored = Scope.Where(p => p != "SchemaVersion").ToArray();

    /// <summary>Widget properties that refresh by themselves: a change to only these is not a change, and restoring a widget keeps the live values.</summary>
    public static readonly string[] VolatileItemProperties = ["WeatherJson", "WeatherFetched", "GoogleTasksWatermark"];

    /// <summary>Widget options that start with this are downloaded data (mail, school, online widgets), not settings.</summary>
    public const string VolatileOptionPrefix = "cache.";

    private static readonly string[] VolatileTaskProperties = ["GoogleEtag", "GoogleUpdated"];
    private static readonly HashSet<string> MoveProps = new(["X", "Y", "Display", "FloatX", "FloatY", "FloatDisplay"], StringComparer.Ordinal);
    private static readonly HashSet<string> SizeProps = new(["Width", "Height", "FloatWidth", "FloatHeight"], StringComparer.Ordinal);

    /// <summary>Reduces the serialized settings to a history state (compact JSON).</summary>
    public static string Capture(string settingsJson)
    {
        if (JsonNode.Parse(settingsJson) is not JsonObject root) throw new InvalidDataException("Settings must be a JSON object.");
        foreach (var name in root.Select(p => p.Key).Where(k => !Scope.Contains(k)).ToList()) root.Remove(name);
        return root.ToJsonString();
    }

    /// <summary>A fingerprint of a history state with the background noise left out: equal keys mean "nothing the user would call a change".</summary>
    public static string Key(string stateJson)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(Stripped(stateJson).ToJsonString()));
        return Convert.ToHexString(hash);
    }

    private static JsonObject Stripped(string stateJson)
    {
        var root = JsonNode.Parse(stateJson) as JsonObject ?? new JsonObject();
        foreach (var item in Objects(root["Items"])) StripItem(item);
        foreach (var entry in Objects(root["Trash"])) if (entry["Item"] is JsonObject inner) StripItem(inner);
        return root;
    }

    private static void StripItem(JsonObject item)
    {
        foreach (var name in VolatileItemProperties) item.Remove(name);
        if (item["Options"] is JsonObject options)
            foreach (var key in options.Select(p => p.Key).Where(k => k.StartsWith(VolatileOptionPrefix, StringComparison.Ordinal)).ToList()) options.Remove(key);
        foreach (var task in Objects(item["Tasks"])) foreach (var name in VolatileTaskProperties) task.Remove(name);
    }

    private static IEnumerable<JsonObject> Objects(JsonNode? array) => array is JsonArray list ? list.OfType<JsonObject>() : [];

    private static Dictionary<string, JsonObject> ById(JsonNode? array)
    {
        var map = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var o in Objects(array)) if (o["Id"]?.GetValue<string>() is { Length: > 0 } id) map[id] = o;
        return map;
    }

    private static string Str(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    private static string NameOf(JsonObject item)
    {
        string name = Str(item["Name"]).Trim();
        if (name.Length == 0)
        {
            string kind = Str(item["Kind"]);
            name = kind.Length == 0 ? "widget" : char.ToUpperInvariant(kind[0]) + kind[1..];
        }
        return name.Length > 28 ? name[..27].TrimEnd() + "…" : name;
    }

    private static string Names(IEnumerable<JsonObject> items, string noun = "widgets")
    {
        var list = items.ToList();
        return list.Count == 1 ? NameOf(list[0]) : $"{list.Count} {noun}";
    }

    /// <summary>A short label for what changed between two history states: "Moved Weather", "Removed Note", "Switched to Study", "Changed appearance"…</summary>
    public static string Describe(string beforeJson, string afterJson)
    {
        var a = Stripped(beforeJson);
        var b = Stripped(afterJson);
        var itemsA = ById(a["Items"]);
        var itemsB = ById(b["Items"]);
        var binA = ById(a["Trash"]);
        var binB = ById(b["Trash"]);
        var gone = itemsA.Where(p => !itemsB.ContainsKey(p.Key)).Select(p => p.Value).ToList();
        var added = itemsB.Where(p => !itemsA.ContainsKey(p.Key)).Select(p => p.Value).ToList();
        var binned = binB.Where(p => !binA.ContainsKey(p.Key)).ToList();
        var binGone = binA.Where(p => !binB.ContainsKey(p.Key)).Select(p => p.Value["Item"] as JsonObject ?? new JsonObject()).ToList();

        if (gone.Count > 0 && binned.Count > 0) return "Removed " + Names(gone);
        if (added.Count > 0 && binGone.Count > 0) return "Restored " + Names(added);
        if (gone.Count > 0) return "Removed " + Names(gone);
        if (added.Count > 0) return "Added " + Names(added);
        if (binGone.Count > 0) return binGone.Count == 1 ? $"Deleted {NameOf(binGone[0])} forever" : $"Deleted {binGone.Count} widgets forever";
        if (binned.Count > 0) return "Moved to the bin " + Names(binned.Select(p => p.Value["Item"] as JsonObject ?? new JsonObject()));

        var layoutsA = ById(a["Layouts"]);
        var layoutsB = ById(b["Layouts"]);
        foreach (var key in new[] { "ActiveFloatingLayout", "ActiveBackgroundLayout" })
        {
            string now = Str(b[key]);
            if (Str(a[key]) != now) return layoutsB.TryGetValue(now, out var layout) && Str(layout["Name"]).Length > 0 ? "Switched to " + Str(layout["Name"]) : "Switched layout";
        }
        if (layoutsB.Count > layoutsA.Count) return "Saved a layout";
        if (layoutsB.Count < layoutsA.Count) return "Deleted a layout";

        var verbs = new List<(string Verb, JsonObject Item)>();
        foreach (var (id, after) in itemsB)
        {
            if (!itemsA.TryGetValue(id, out var before) || before.ToJsonString() == after.ToJsonString()) continue;
            verbs.Add((ChangeVerb(before, after), after));
        }
        if (verbs.Count == 1) return $"{verbs[0].Verb} {NameOf(verbs[0].Item)}";
        if (verbs.Count > 1) return verbs.Select(v => v.Verb).Distinct().Count() == 1 ? $"{verbs[0].Verb} {verbs.Count} widgets" : $"Changed {verbs.Count} widgets";

        if (a["Appearance"]?.ToJsonString() != b["Appearance"]?.ToJsonString()) return "Changed appearance";
        if (a["Layouts"]?.ToJsonString() != b["Layouts"]?.ToJsonString()) return "Changed layouts";
        return "Changed settings";
    }

    private static string ChangeVerb(JsonObject before, JsonObject after)
    {
        var changed = before.Select(p => p.Key).Union(after.Select(p => p.Key)).Where(k => before[k]?.ToJsonString() != after[k]?.ToJsonString()).ToHashSet(StringComparer.Ordinal);
        if (changed.Count == 0) return "Changed";
        if (changed.Contains("OnTop")) return after["OnTop"]?.GetValue<bool>() == true ? "Floated" : "Docked";
        if (changed.IsSubsetOf(MoveProps)) return "Moved";
        if (changed.All(k => MoveProps.Contains(k) || SizeProps.Contains(k))) return "Resized";
        if (changed.Count == 1 && changed.Contains("Hidden")) return after["Hidden"]?.GetValue<bool>() == true ? "Hid" : "Showed";
        if (changed.Count == 1 && changed.Contains("Locked")) return after["Locked"]?.GetValue<bool>() == true ? "Locked" : "Unlocked";
        return "Edited";
    }

    /// <summary>True for the labels of changes that are easy to regret: they get an Undo button in the toast.</summary>
    public static bool IsDestructive(string label) =>
        label.StartsWith("Removed ", StringComparison.Ordinal) || label.StartsWith("Moved ", StringComparison.Ordinal) || label.StartsWith("Resized ", StringComparison.Ordinal)
        || label.StartsWith("Hid ", StringComparison.Ordinal) || label.StartsWith("Deleted ", StringComparison.Ordinal) || label.StartsWith("Docked ", StringComparison.Ordinal);

    // ---------- Summaries, for the "Restore a day" preview ----------

    /// <summary>How many widgets and layouts a saved state has.</summary>
    public sealed record Summary(int Widgets, int Floating, int Layouts, int Bin);

    public static Summary? Summarize(string settingsJson)
    {
        try
        {
            if (JsonNode.Parse(settingsJson) is not JsonObject root) return null;
            var items = Objects(root["Items"]).ToList();
            return new Summary(items.Count, items.Count(i => i["OnTop"]?.GetValue<bool>() == true), Objects(root["Layouts"]).Count(), Objects(root["Trash"]).Count());
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException) { return null; }
    }

    /// <summary>What restoring an earlier state would do to the current one.</summary>
    public sealed record Comparison(Summary Then, Summary Now, IReadOnlyList<string> WouldReturn, IReadOnlyList<string> WouldLeave, int WouldChange)
    {
        /// <summary>One or two plain sentences, e.g. "3 widgets come back (Weather, Note, Clock). 1 widget goes away (Timer). 2 change. 4 layouts instead of 3."</summary>
        public string Text
        {
            get
            {
                var parts = new List<string>();
                if (WouldReturn.Count > 0) parts.Add($"{Plural(WouldReturn.Count, "widget")} come{(WouldReturn.Count == 1 ? "s" : "")} back ({List(WouldReturn)})");
                if (WouldLeave.Count > 0) parts.Add($"{Plural(WouldLeave.Count, "widget")} go{(WouldLeave.Count == 1 ? "es" : "")} away ({List(WouldLeave)})");
                if (WouldChange > 0) parts.Add($"{Plural(WouldChange, "widget")} look{(WouldChange == 1 ? "s" : "")} different");
                if (Then.Layouts != Now.Layouts) parts.Add($"{Plural(Then.Layouts, "layout")} instead of {Now.Layouts}");
                return parts.Count == 0 ? "The same widgets and layouts as now." : string.Join(". ", parts) + ".";
            }
        }

        private static string Plural(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";
        private static string List(IReadOnlyList<string> names) => string.Join(", ", names.Take(3)) + (names.Count > 3 ? $" and {names.Count - 3} more" : "");
    }

    public static Comparison? Compare(string thenJson, string nowJson)
    {
        if (Summarize(thenJson) is not { } then || Summarize(nowJson) is not { } now) return null;
        var itemsThen = ById(Stripped(Capture(thenJson))["Items"]);
        var itemsNow = ById(Stripped(Capture(nowJson))["Items"]);
        var returns = itemsThen.Where(p => !itemsNow.ContainsKey(p.Key)).Select(p => NameOf(p.Value)).ToList();
        var leaves = itemsNow.Where(p => !itemsThen.ContainsKey(p.Key)).Select(p => NameOf(p.Value)).ToList();
        int changed = itemsThen.Count(p => itemsNow.TryGetValue(p.Key, out var other) && other.ToJsonString() != p.Value.ToJsonString());
        return new Comparison(then, now, returns, leaves, changed);
    }
}
