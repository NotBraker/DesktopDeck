using System.Text.Json;

namespace DesktopDeck.Models;

/// <summary>
/// Linked widgets: widgets of the same kind with the same non-empty <see cref="LauncherItem.LinkId"/> share their DATA (the list, the timer, the notes…)
/// while each keeps its own LOOK (size, place, colors, card style, view and layout settings). No UI and no I/O, so it is unit-tested.
/// <para>Shared ("data"):</para>
/// <list type="bullet">
/// <item>Built-in kinds: the typed content (Tasks, Habits, Blocks, Goals, Pomodoro, Count, Text, the countdown target…), see <see cref="CopyBuiltIn"/>.</item>
/// <item>Plug-in kinds (Widgets/IWidget.cs): the options the widget does not declare, which by convention are its <c>data.*</c> keys, plus a few
/// declared keys that are really content (<see cref="SharedDeclared"/>). NOT shared: <c>_</c> appearance keys, every other declared setting, and
/// <c>cache.*</c> (downloaded data, derived from per-widget settings such as a query, so peers fetch their own).</item>
/// </list>
/// Everything is copied in place (same list, dictionary and Pomodoro objects), because tickers and menus hold on to them between redraws.
/// </summary>
public static class WidgetLinks
{
    /// <summary>
    /// The option keys a plug-in kind declares (its Customize settings), or null if unknown. Set once at startup from the widget registry; left null,
    /// only <c>data.*</c> keys and <see cref="SharedDeclared"/> are shared.
    /// </summary>
    public static Func<string, IReadOnlyCollection<string>?>? DeclaredKeys { get; set; }

    /// <summary>
    /// Declared (Customize) options that hold the user's content rather than a setting: they are edited from the widget body, or other shared data
    /// refers to them (cards store a column number, ticked steps are stored by title, progress by deck…), so peers must agree on them.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> SharedDeclared = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["homework"] = ["subjects"],
        ["study-log"] = ["subjects"],
        ["grades"] = ["classes"],
        ["flashcards"] = ["deck1", "deck2"],
        ["kanban"] = ["columns"],
        ["routine"] = ["steps"],
        ["quick-links"] = ["links"],
        ["alarms"] = ["alarms", "enabled"],
        ["month-calendar"] = ["marks"],
        ["quote"] = ["quotes"],
    };

    /// <summary>Built-in kinds with nothing to share (a launcher, a picture, a clock…). Plug-in kinds can all be linked.</summary>
    private static readonly HashSet<string> NotLinkable =
    [
        ItemKinds.Launcher, ItemKinds.Image, ItemKinds.Group, ItemKinds.Clock, ItemKinds.Media,
    ];

    public static bool CanLink(string kind) => !NotLinkable.Contains(kind) && (ItemKinds.All.Contains(kind) || ItemKinds.IsPluginId(kind));

    // ---------- Groups ----------

    /// <summary>The other widgets linked with this one (same group and same kind), in the order of <paramref name="all"/>.</summary>
    public static List<LauncherItem> Peers(LauncherItem item, IEnumerable<LauncherItem> all) =>
        item.LinkId.Length == 0 ? [] : all.Where(i => !ReferenceEquals(i, item) && i.Id != item.Id && i.LinkId == item.LinkId && i.Kind == item.Kind).ToList();

    public static bool IsLinked(LauncherItem item, IEnumerable<LauncherItem> all) => Peers(item, all).Count > 0;

    /// <summary>Other widgets of the same kind (linked or not) that this one could be linked with, excluding its own peers.</summary>
    public static List<LauncherItem> Candidates(LauncherItem item, IEnumerable<LauncherItem> all) =>
        !CanLink(item.Kind) ? [] : all.Where(i => !ReferenceEquals(i, item) && i.Id != item.Id && i.Kind == item.Kind && (item.LinkId.Length == 0 || i.LinkId != item.LinkId)).ToList();

    /// <summary>
    /// Links <paramref name="join"/> (and the widgets already linked with it) to <paramref name="keep"/>: they all get <paramref name="keep"/>'s group
    /// (a new one if neither was linked) and <paramref name="keep"/>'s data. False if they are different kinds or the kind can't be linked.
    /// </summary>
    public static bool Link(LauncherItem keep, LauncherItem join, IEnumerable<LauncherItem> all)
    {
        if (ReferenceEquals(keep, join) || keep.Id == join.Id || keep.Kind != join.Kind || !CanLink(keep.Kind)) return false;
        var list = all as IReadOnlyCollection<LauncherItem> ?? all.ToList();
        var joining = new List<LauncherItem> { join };
        joining.AddRange(Peers(join, list));
        var staying = new List<LauncherItem> { keep };
        staying.AddRange(Peers(keep, list));
        string id = keep.LinkId.Length > 0 ? keep.LinkId : join.LinkId.Length > 0 ? join.LinkId : NewLinkId();
        foreach (var member in joining.Concat(staying)) member.LinkId = id;
        foreach (var member in joining.Where(m => !staying.Contains(m))) CopyData(keep, member);
        return true;
    }

    /// <summary>Takes the widget out of its group. A widget left alone in the group is unlinked too.</summary>
    public static void Unlink(LauncherItem item, IEnumerable<LauncherItem> all)
    {
        string old = item.LinkId;
        if (old.Length == 0) return;
        item.LinkId = "";
        DropLoners(old, all);
    }

    private static void DropLoners(string linkId, IEnumerable<LauncherItem> all)
    {
        var rest = all.Where(i => i.LinkId == linkId).ToList();
        foreach (var kindGroup in rest.GroupBy(i => i.Kind))
            if (kindGroup.Count() < 2) foreach (var lone in kindGroup) lone.LinkId = "";
    }

    /// <summary>Repairs a loaded file: link ids on kinds that can't be linked, and groups with fewer than two widgets, are cleared.</summary>
    public static void Normalize(IEnumerable<LauncherItem> all)
    {
        var items = all.ToList();
        foreach (var item in items)
        {
            if (item.LinkId is null) item.LinkId = "";
            item.LinkId = item.LinkId.Trim();
            if (item.LinkId.Length > 0 && !CanLink(item.Kind)) item.LinkId = "";
        }
        foreach (var group in items.Where(i => i.LinkId.Length > 0).GroupBy(i => (i.LinkId, i.Kind)))
            if (group.Count() < 2) foreach (var lone in group) lone.LinkId = "";
    }

    public static string NewLinkId() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// Google Tasks: the widget that talks to Google for a linked group, so two peers never create the same task twice. The first peer (in list
    /// order) with sync on; the widget itself when it is not linked.
    /// </summary>
    public static LauncherItem TaskSyncOwner(LauncherItem item, IEnumerable<LauncherItem> all)
    {
        if (item.LinkId.Length == 0) return item;
        var group = all.Where(i => i.Kind == item.Kind && i.LinkId == item.LinkId && i.GoogleTasksSync).ToList();
        return group.Count > 0 ? group[0] : item;
    }

    // ---------- Which fields are data ----------

    /// <summary>True if this option of a plug-in kind is shared between linked widgets.</summary>
    public static bool IsDataKey(string kind, string key)
    {
        if (key.StartsWith('_') || key.StartsWith("cache.", StringComparison.Ordinal)) return false;
        if (SharedDeclared.TryGetValue(kind, out var content) && content.Contains(key)) return true;
        if (key.StartsWith("data.", StringComparison.Ordinal)) return true;
        // Anything the widget does not declare as a setting is something it stored itself.
        var declared = DeclaredKeys?.Invoke(kind);
        return declared is not null && !declared.Contains(key);
    }

    // ---------- Copying ----------

    /// <summary>
    /// Copies the shared data of <paramref name="from"/> into <paramref name="to"/> (same kind), in place. Returns true if anything differed, so
    /// callers redraw only widgets that really changed (this is also what keeps two linked widgets from updating each other forever).
    /// </summary>
    public static bool CopyData(LauncherItem from, LauncherItem to)
    {
        if (ReferenceEquals(from, to) || from.Kind != to.Kind || !CanLink(from.Kind)) return false;
        return ItemKinds.All.Contains(from.Kind) ? CopyBuiltIn(from, to) : CopyOptions(from, to);
    }

    private static bool CopyBuiltIn(LauncherItem from, LauncherItem to)
    {
        bool changed = false;
        switch (from.Kind)
        {
            case ItemKinds.Tasks:
                changed |= Replace(to.Tasks, from.Tasks.Select(t => t.Copy()));
                changed |= Replace(to.GoogleTaskDeletes, from.GoogleTaskDeletes.Select(d => new GoogleTaskTombstone { ListId = d.ListId, TaskId = d.TaskId }));
                // The Google sync setup travels with the tasks (their Google ids belong to that list); only one peer syncs, see TaskSyncOwner.
                changed |= Set(to.GoogleTasksWatermark, from.GoogleTasksWatermark, v => to.GoogleTasksWatermark = v);
                changed |= Set(to.GoogleTasksSync, from.GoogleTasksSync, v => to.GoogleTasksSync = v);
                changed |= Set(to.GoogleTaskListId, from.GoogleTaskListId, v => to.GoogleTaskListId = v);
                changed |= Set(to.GoogleTaskListName, from.GoogleTaskListName, v => to.GoogleTaskListName = v);
                changed |= Set(to.DailyReset, from.DailyReset, v => to.DailyReset = v);
                changed |= Set(to.LastDate, from.LastDate, v => to.LastDate = v);
                break;
            case ItemKinds.Habits:
                changed |= Replace(to.Habits, from.Habits.Select(h => new HabitEntry { Id = h.Id, Name = h.Name, Color = h.Color, SourceId = h.SourceId, Days = h.Days.ToList() }));
                break;
            case ItemKinds.Counter:
                changed |= Set(to.Count, from.Count, v => to.Count = v);
                changed |= Set(to.CountGoal, from.CountGoal, v => to.CountGoal = v);
                changed |= Set(to.Step, from.Step, v => to.Step = v);
                break;
            case ItemKinds.Timer:
                changed |= CopyPomodoro(from.Pomodoro, to.Pomodoro);
                changed |= Set(to.AutoNext, from.AutoNext, v => to.AutoNext = v);
                break;
            case ItemKinds.Schedule:
                changed |= Replace(to.Blocks, from.Blocks.Select(b => b.Copy()));
                break;
            case ItemKinds.Goals:
                changed |= Replace(to.Goals, from.Goals.Select(g => g.Copy()));
                break;
            case ItemKinds.Note:
                changed |= Set(to.Text, from.Text, v => to.Text = v);
                break;
            case ItemKinds.Countdown:
                changed |= Set(to.TargetDate, from.TargetDate, v => to.TargetDate = v);
                changed |= Set(to.TargetTime, from.TargetTime, v => to.TargetTime = v);
                changed |= Set(to.RepeatYearly, from.RepeatYearly, v => to.RepeatYearly = v);
                break;
            case ItemKinds.Weather:
                // The place and the forecast fetched for it. Units (F or C) are a display choice and stay per widget.
                changed |= Set(to.Location, from.Location, v => to.Location = v);
                changed |= Set(to.Latitude, from.Latitude, v => to.Latitude = v);
                changed |= Set(to.Longitude, from.Longitude, v => to.Longitude = v);
                changed |= Set(to.WeatherJson, from.WeatherJson, v => to.WeatherJson = v);
                changed |= Set(to.WeatherFetched, from.WeatherFetched, v => to.WeatherFetched = v);
                break;
            case ItemKinds.Folder:
                changed |= Set(to.Target, from.Target, v => to.Target = v);
                break;
        }
        return changed;
    }

    private static bool CopyOptions(LauncherItem from, LauncherItem to)
    {
        bool changed = false;
        foreach (var (key, value) in from.Options)
        {
            if (!IsDataKey(from.Kind, key)) continue;
            if (to.Options.TryGetValue(key, out var existing) && existing == value) continue;
            to.Options[key] = value;
            changed = true;
        }
        foreach (var key in to.Options.Keys.Where(k => IsDataKey(to.Kind, k) && !from.Options.ContainsKey(k)).ToList())
        {
            to.Options.Remove(key);
            changed = true;
        }
        return changed;
    }

    /// <summary>Copies a timer's settings and run state into the target's own object (a running ticker holds on to it).</summary>
    private static bool CopyPomodoro(PomodoroState from, PomodoroState to)
    {
        bool changed = false;
        changed |= Set(to.WorkMinutes, from.WorkMinutes, v => to.WorkMinutes = v);
        changed |= Set(to.BreakMinutes, from.BreakMinutes, v => to.BreakMinutes = v);
        changed |= Set(to.LongBreakMinutes, from.LongBreakMinutes, v => to.LongBreakMinutes = v);
        changed |= Set(to.LongBreakEvery, from.LongBreakEvery, v => to.LongBreakEvery = v);
        changed |= Set(to.Mode, from.Mode, v => to.Mode = v);
        changed |= Set(to.EndsAt, from.EndsAt, v => to.EndsAt = v);
        changed |= Set(to.PausedSeconds, from.PausedSeconds, v => to.PausedSeconds = v);
        changed |= Set(to.Cycle, from.Cycle, v => to.Cycle = v);
        if (to.Log.Count != from.Log.Count || from.Log.Any(p => !to.Log.TryGetValue(p.Key, out var n) || n != p.Value))
        {
            to.Log.Clear();
            foreach (var (day, count) in from.Log) to.Log[day] = count;
            changed = true;
        }
        return changed;
    }

    private static bool Set<T>(T current, T value, Action<T> assign)
    {
        if (EqualityComparer<T>.Default.Equals(current, value)) return false;
        assign(value);
        return true;
    }

    /// <summary>Makes <paramref name="target"/> hold copies of <paramref name="source"/> (same list object). True if it was different.</summary>
    private static bool Replace<T>(List<T> target, IEnumerable<T> source)
    {
        var copy = source.ToList();
        if (Same(target, copy)) return false;
        target.Clear();
        target.AddRange(copy);
        return true;
    }

    private static bool Same<T>(List<T> a, List<T> b) => a.Count == b.Count && JsonSerializer.Serialize(a) == JsonSerializer.Serialize(b);

    /// <summary>
    /// <paramref name="source"/>'s data changed: copies it to every peer. Returns the peers that were different (they need redrawing and saving).
    /// The copy goes one way from the source to each peer, so there is no chain reaction.
    /// </summary>
    public static List<LauncherItem> Propagate(LauncherItem source, IEnumerable<LauncherItem> all)
    {
        var changed = new List<LauncherItem>();
        foreach (var peer in Peers(source, all))
            if (CopyData(source, peer)) changed.Add(peer);
        return changed;
    }

    // ---------- Describing a widget ----------

    /// <summary>True if the widget holds anything the user put there (used to ask which data to keep when two widgets are linked).</summary>
    public static bool HasData(LauncherItem item) => item.Kind switch
    {
        ItemKinds.Tasks => item.Tasks.Count > 0,
        ItemKinds.Habits => item.Habits.Count > 0,
        ItemKinds.Counter => item.Count != 0,
        ItemKinds.Timer => item.Pomodoro.Log.Count > 0 || item.Pomodoro.EndsAt is not null || item.Pomodoro.PausedSeconds is not null,
        ItemKinds.Schedule => item.Blocks.Count > 0,
        ItemKinds.Goals => item.Goals.Count > 0,
        ItemKinds.Note => item.Text.Trim().Length > 0 && item.Text != "Click to write a note.",
        ItemKinds.Countdown => item.TargetDate.Length > 0,
        ItemKinds.Weather => item.Location.Length > 0,
        ItemKinds.Folder => item.Target.Length > 0,
        _ => HasPluginData(item),
    };

    /// <summary>True if a plug-in widget has stored anything shared (its own <c>data.*</c> or content options). Built-in kinds count too.</summary>
    public static bool HasSharedData(LauncherItem item) => ItemKinds.All.Contains(item.Kind) || HasPluginData(item);

    private static bool HasPluginData(LauncherItem item) =>
        !ItemKinds.All.Contains(item.Kind) && item.Options.Any(p => p.Value.Trim().Length > 0 && p.Value != "[]" && IsDataKey(item.Kind, p.Key));

    /// <summary>A short line about what the widget holds, such as "5 tasks" or "12 items". Empty if there is nothing cheap to say.</summary>
    public static string Preview(LauncherItem item)
    {
        static string Count(int n, string one, string many) => n == 0 ? "" : n == 1 ? $"1 {one}" : $"{n} {many}";
        switch (item.Kind)
        {
            case ItemKinds.Tasks: return Count(item.Tasks.Count(t => !t.Hidden), "task", "tasks");
            case ItemKinds.Habits: return Count(item.Habits.Count, "habit", "habits");
            case ItemKinds.Schedule: return Count(item.Blocks.Count, "block", "blocks");
            case ItemKinds.Goals: return Count(item.Goals.Count, "goal", "goals");
            case ItemKinds.Counter: return item.Count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
            case ItemKinds.Note:
                string text = string.Join(" ", item.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
                return text.Length > 28 ? text[..27] + "…" : text;
            case ItemKinds.Timer: return item.Pomodoro.Log.Values.Sum() is var done and > 0 ? Count(done, "session done", "sessions done") : "";
            case ItemKinds.Countdown: return item.TargetDate;
            case ItemKinds.Weather: return item.Location;
        }
        foreach (var (key, value) in item.Options)
        {
            if (!key.StartsWith("data.", StringComparison.Ordinal) || !value.TrimStart().StartsWith('[')) continue;
            try
            {
                using var doc = JsonDocument.Parse(value);
                if (doc.RootElement.ValueKind == JsonValueKind.Array) return Count(doc.RootElement.GetArrayLength(), "item", "items");
            }
            catch (JsonException) { }
        }
        return "";
    }
}
