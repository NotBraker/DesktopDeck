namespace DesktopDeck.Models;

/// <summary>A removed widget waiting in the recycle bin: the widget itself, when it was removed and where it was (so Restore puts it back).</summary>
public sealed class TrashEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>The removed widget with all its data and settings.</summary>
    public LauncherItem Item { get; set; } = new();
    public DateTimeOffset RemovedAt { get; set; } = DateTimeOffset.Now;
    /// <summary><see cref="LayoutLayers.Floating"/> if it was kept on top, otherwise <see cref="LayoutLayers.Background"/>.</summary>
    public string Layer { get; set; } = LayoutLayers.Background;
    /// <summary>The monitor it was on (empty = the primary one).</summary>
    public string Display { get; set; } = "";
    /// <summary>Its place and visibility in every layout that had it, keyed by layout id: removing a widget takes it out of all layouts, restoring puts it back in.</summary>
    public Dictionary<string, LayoutEntry> LayoutEntries { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// The recycle bin (<see cref="DeckSettings.Trash"/>): removing a widget moves it here instead of destroying it; it can be restored, deleted for good,
/// and anything older than <see cref="RetentionDays"/> days is emptied automatically. No UI, no I/O: callers pass the time.
/// </summary>
public static class TrashBin
{
    public const int RetentionDays = 30;
    /// <summary>The bin never holds more than this many widgets (the oldest go first).</summary>
    public const int MaxEntries = 200;
    /// <summary>A widget holding at least this many entries (tasks, habits, blocks, goals) still asks before it is removed.</summary>
    public const int BigItemEntries = 25;

    /// <summary>True for a widget holding so much that removing it deserves a question even though it can be restored.</summary>
    public static bool IsBig(LauncherItem item) => item.Tasks.Count + item.Habits.Count + item.Blocks.Count + item.Goals.Count >= BigItemEntries;

    /// <summary>
    /// Takes the widget out of the deck and every layout and puts it in the bin (newest first). The layouts are brought up to date first so the
    /// remembered places are current. Returns the new entry.
    /// </summary>
    public static TrashEntry Add(DeckSettings settings, LauncherItem item, DateTimeOffset now)
    {
        LayoutEngine.CaptureActive(settings);
        var entry = new TrashEntry
        {
            Item = item,
            RemovedAt = now,
            Layer = item.OnTop ? LayoutLayers.Floating : LayoutLayers.Background,
            Display = item.OnTop ? item.FloatDisplay : item.Display,
        };
        foreach (var layout in settings.Layouts)
            if (layout.Entries.TryGetValue(item.Id, out var place)) entry.LayoutEntries[layout.Id] = place.Copy();
        if (!settings.Items.Remove(item)) settings.Items.RemoveAll(i => i.Id == item.Id);
        // A removed widget leaves its link group (and restoring it later does not link it again).
        WidgetLinks.Unlink(item, settings.Items);
        LayoutEngine.Forget(settings, item.Id);
        settings.Trash.Insert(0, entry);
        if (settings.Trash.Count > MaxEntries) settings.Trash.RemoveRange(MaxEntries, settings.Trash.Count - MaxEntries);
        return entry;
    }

    public static TrashEntry? Find(DeckSettings settings, string entryId) => settings.Trash.FirstOrDefault(t => t.Id == entryId);

    /// <summary>
    /// Puts a widget back where it was: on its deck (or floating) at its old place, and in every layout that still exists and had it.
    /// It gets a new id if another widget has taken its id. Returns the widget, or null when there is no such entry.
    /// </summary>
    public static LauncherItem? Restore(DeckSettings settings, string entryId)
    {
        if (Find(settings, entryId) is not { } entry) return null;
        settings.Trash.Remove(entry);
        var item = entry.Item;
        if (string.IsNullOrWhiteSpace(item.Id) || settings.Items.Any(i => i.Id == item.Id)) item.Id = Guid.NewGuid().ToString("N");
        settings.Items.Add(item);
        foreach (var (layoutId, place) in entry.LayoutEntries)
            if (LayoutEngine.Find(settings, layoutId) is { } layout && LayoutEngine.InLayer(item, layout.Layer)) layout.Entries[item.Id] = place.Copy();
        return item;
    }

    /// <summary>Deletes one entry for good. Returns it, or null.</summary>
    public static TrashEntry? Delete(DeckSettings settings, string entryId)
    {
        if (Find(settings, entryId) is not { } entry) return null;
        settings.Trash.Remove(entry);
        return entry;
    }

    /// <summary>Empties the bin. Returns what was in it.</summary>
    public static List<TrashEntry> Empty(DeckSettings settings)
    {
        var all = settings.Trash.ToList();
        settings.Trash.Clear();
        return all;
    }

    /// <summary>Deletes the entries removed more than <paramref name="days"/> days before <paramref name="now"/>. Returns them.</summary>
    /// <remarks>0 days keeps everything until it is emptied by hand.</remarks>
    public static List<TrashEntry> Purge(DeckSettings settings, DateTimeOffset now, int days = RetentionDays)
    {
        if (days <= 0) return [];
        var old = settings.Trash.Where(t => now - t.RemovedAt > TimeSpan.FromDays(days)).ToList();
        foreach (var entry in old) settings.Trash.Remove(entry);
        return old;
    }

    /// <summary>Whole days left before the entry is emptied automatically (0 = today).</summary>
    public static int DaysLeft(TrashEntry entry, DateTimeOffset now, int days = RetentionDays)
        => Math.Max(0, (int)Math.Ceiling((entry.RemovedAt + TimeSpan.FromDays(days) - now).TotalDays));
}
