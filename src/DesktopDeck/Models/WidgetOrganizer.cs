namespace DesktopDeck.Models;

/// <summary>
/// The rules for organizing widgets, with no UI in them: the gallery's favorites and recently used kinds, and widget groups (a "group" card
/// holds several widgets as tabs; each child points at it with <see cref="LauncherItem.GroupId"/> and is drawn only inside it).
/// </summary>
public static class WidgetOrganizer
{
    public const int MaxRecent = 8, MaxFavorites = 200;
    /// <summary>The group's option that remembers the selected tab (the child's id).</summary>
    public const string TabKey = "data.tab";
    /// <summary>Height (DIP) of a group's tab strip; the group is that much taller than the widget it started from (n × 24 − 12 stays on the grid).</summary>
    public const double TabStripHeight = 36;
    public const int MaxGroupName = 40;

    // ---------- Favorites and recently used ----------

    /// <summary>Repairs the two gallery lists loaded from a file: trimmed, no empty or repeated kinds, favorites capped, recents capped to <see cref="MaxRecent"/>.</summary>
    public static void NormalizeLists(DeckSettings settings)
    {
        settings.FavoriteWidgets = Clean(settings.FavoriteWidgets, MaxFavorites);
        settings.RecentWidgets = Clean(settings.RecentWidgets, MaxRecent);
    }

    private static List<string> Clean(List<string>? list, int max) =>
        (list ?? new()).Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).Distinct(StringComparer.Ordinal).Take(max).ToList();

    public static bool IsFavorite(DeckSettings settings, string kind) => settings.FavoriteWidgets.Contains(kind, StringComparer.Ordinal);

    /// <summary>Stars or un-stars a kind; returns whether it is a favorite now.</summary>
    public static bool ToggleFavorite(DeckSettings settings, string kind)
    {
        if (string.IsNullOrWhiteSpace(kind)) return false;
        kind = kind.Trim();
        if (settings.FavoriteWidgets.RemoveAll(k => k == kind) > 0) return false;
        settings.FavoriteWidgets.Insert(0, kind);
        NormalizeLists(settings);
        return true;
    }

    /// <summary>Records that a kind was just added from the gallery: it moves to the front of the recent list.</summary>
    public static void NoteRecent(DeckSettings settings, string kind)
    {
        if (string.IsNullOrWhiteSpace(kind)) return;
        kind = kind.Trim();
        settings.RecentWidgets.RemoveAll(k => k == kind);
        settings.RecentWidgets.Insert(0, kind);
        NormalizeLists(settings);
    }

    // ---------- Groups ----------

    public static bool IsGroup(LauncherItem item) => item.Kind == ItemKinds.Group;

    /// <summary>True when the item is a tab of a group (drawn only inside it).</summary>
    public static bool IsChild(LauncherItem item) => !string.IsNullOrEmpty(item.GroupId);

    /// <summary>The widgets in a group, in tab order (the order of the item list).</summary>
    public static List<LauncherItem> Children(DeckSettings settings, string groupId) =>
        string.IsNullOrEmpty(groupId) ? [] : settings.Items.Where(i => i.GroupId == groupId && !IsGroup(i)).ToList();

    public static LauncherItem? GroupOf(DeckSettings settings, LauncherItem child) =>
        IsChild(child) ? settings.Items.FirstOrDefault(i => i.Id == child.GroupId && IsGroup(i)) : null;

    public static IEnumerable<LauncherItem> Groups(DeckSettings settings) => settings.Items.Where(IsGroup);

    /// <summary>The tab that is showing: the remembered one, else the first.</summary>
    public static LauncherItem? Selected(DeckSettings settings, LauncherItem group)
    {
        var children = Children(settings, group.Id);
        if (children.Count == 0) return null;
        group.Options.TryGetValue(TabKey, out var id);
        return children.FirstOrDefault(c => c.Id == id) ?? children[0];
    }

    public static void Select(LauncherItem group, string childId) => group.Options[TabKey] = childId;

    /// <summary>A widget can join a group when it is not a group itself.</summary>
    public static bool CanJoin(LauncherItem item, LauncherItem group) => !IsGroup(item) && IsGroup(group) && item.Id != group.Id && item.GroupId != group.Id;

    /// <summary>Makes <paramref name="child"/> a tab of <paramref name="group"/> (moving it out of another group first) and selects it.</summary>
    public static bool AddToGroup(DeckSettings settings, LauncherItem child, LauncherItem group)
    {
        if (!CanJoin(child, group) || !settings.Items.Contains(child) || !settings.Items.Contains(group)) return false;
        // Tabs are in the order the widgets joined: the new one goes after the group's last tab.
        var last = Children(settings, group.Id).LastOrDefault() ?? group;
        settings.Items.Remove(child);
        settings.Items.Insert(settings.Items.IndexOf(last) + 1, child);
        child.GroupId = group.Id;
        child.OnTop = group.OnTop;
        Select(group, child.Id);
        return true;
    }

    /// <summary>
    /// A new group card where <paramref name="first"/> is now, holding it as its only tab. The card is as wide as the widget and one tab strip taller, and is
    /// inserted into the item list at the widget's place. Returns null when the widget cannot be grouped (it is a group, or not in the list).
    /// </summary>
    public static LauncherItem? NewGroupAround(DeckSettings settings, LauncherItem first, string name)
    {
        if (IsGroup(first) || !settings.Items.Contains(first)) return null;
        name = string.IsNullOrWhiteSpace(name) ? "Group" : name.Trim();
        if (name.Length > MaxGroupName) name = name[..MaxGroupName].TrimEnd();
        var (dw, dh) = ItemKinds.DefaultSize(first.Kind);
        var (mw, mh) = ItemKinds.MinimumSize(first.Kind);
        double w = Math.Max(mw, first.Width > 0 ? first.Width : dw), h = Math.Max(mh, first.Height > 0 ? first.Height : dh);
        double fw = Math.Max(mw, first.FloatWidth > 0 ? first.FloatWidth : w), fh = Math.Max(mh, first.FloatHeight > 0 ? first.FloatHeight : h);
        var group = new LauncherItem
        {
            Kind = ItemKinds.Group, Name = name, Accent = first.Accent, OnTop = first.OnTop, Hidden = first.Hidden, Locked = first.Locked,
            Display = first.Display, X = first.X, Y = first.Y, Width = w, Height = h + TabStripHeight,
            FloatDisplay = first.FloatDisplay, FloatX = first.FloatX, FloatY = first.FloatY, FloatWidth = fw, FloatHeight = fh + TabStripHeight,
        };
        settings.Items.Insert(settings.Items.IndexOf(first), group);
        AddToGroup(settings, first, group);
        return group;
    }

    /// <summary>Takes a widget out of its group and puts it back on the deck (or the floating layer) next to where the group is.</summary>
    public static bool Release(DeckSettings settings, LauncherItem child, int offsetIndex = 1)
    {
        if (!IsChild(child)) return false;
        var group = GroupOf(settings, child);
        child.GroupId = "";
        if (group is null) return true;
        child.OnTop = group.OnTop;
        child.Hidden = group.Hidden;
        child.Width = child.Height = child.FloatWidth = child.FloatHeight = 0;
        double step = 0.03 * offsetIndex;
        child.Display = group.Display;
        child.X = Math.Clamp(group.X + step, 0, 1);
        child.Y = Math.Clamp(group.Y + step, 0, 1);
        child.FloatDisplay = group.FloatDisplay;
        child.FloatX = Math.Clamp(group.FloatX + step, 0, 1);
        child.FloatY = Math.Clamp(group.FloatY + step, 0, 1);
        if (group.Options.TryGetValue(TabKey, out var tab) && tab == child.Id) group.Options.Remove(TabKey);
        return true;
    }

    /// <summary>Removes a group and returns its widgets to the deck, visible. Returns the widgets that were released.</summary>
    public static List<LauncherItem> Dissolve(DeckSettings settings, LauncherItem group)
    {
        var children = Children(settings, group.Id);
        for (int i = 0; i < children.Count; i++)
        {
            Release(settings, children[i], i + 1);
            children[i].Hidden = false;
        }
        settings.Items.Remove(group);
        return children;
    }

    /// <summary>Repairs groups after loading: no nesting, widgets pointing at a missing group go back to the deck, a remembered tab that is gone is forgotten, and children follow their group's layer.</summary>
    public static void NormalizeGroups(DeckSettings settings)
    {
        var groups = new Dictionary<string, LauncherItem>(StringComparer.Ordinal);
        foreach (var g in settings.Items.Where(i => i is not null && IsGroup(i) && i.Id is not null)) groups.TryAdd(g.Id, g);
        foreach (var item in settings.Items)
        {
            if (item is null) continue;
            item.GroupId ??= "";
            if (IsGroup(item) || !groups.TryGetValue(item.GroupId, out var group)) { item.GroupId = ""; continue; }
            item.OnTop = group.OnTop;
        }
        foreach (var group in groups.Values)
            if (group.Options.TryGetValue(TabKey, out var tab) && !settings.Items.Any(i => i is not null && i.Id == tab && i.GroupId == group.Id)) group.Options.Remove(TabKey);
    }
}

/// <summary>A starter layout: a name, an emoji, and the widget kinds it puts on the screen.</summary>
public sealed record WidgetTemplate(string Id, string Name, string Icon, string Description, string Accent, string[] Kinds);

public static class WidgetTemplates
{
    public static readonly WidgetTemplate[] All =
    [
        new("student", "Student", "📚", "Homework, your timetable, flashcards and a study log, with a focus timer.", "#6c63ff", ["homework", ItemKinds.Schedule, "flashcards", "study-log", ItemKinds.Timer]),
        new("vibe-coding", "Vibe coding", "💻", "GitHub, a kanban board, quick links, a timer and what's playing.", "#8b5cf6", ["github", "kanban", "quick-links", ItemKinds.Timer, ItemKinds.Media]),
        new("focus", "Focus", "🎯", "A focus session, a timer, break reminders and today's tasks. Nothing else.", "#C4684F", ["focus", ItemKinds.Timer, "break-reminder", ItemKinds.Tasks]),
        new("developer", "Developer", "🛠️", "The time, GitHub, system stats, a focus timer, quick links and tasks.", "#3b82f6", [ItemKinds.Clock, "github", "system-stats", ItemKinds.Timer, "quick-links", ItemKinds.Tasks]),
        new("minimal", "Minimal", "🌿", "The time, the weather, today's tasks and how far through the day you are.", "#22c55e", [ItemKinds.Clock, ItemKinds.Weather, ItemKinds.Tasks, "day-progress"]),
    ];

    public static WidgetTemplate? Find(string? id) => All.FirstOrDefault(t => t.Id == id);
}
