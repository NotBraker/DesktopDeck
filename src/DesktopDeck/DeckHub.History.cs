using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using DesktopDeck.Models;
using DesktopDeck.Services;

namespace DesktopDeck;

/// <summary>
/// History: undo and redo, the recycle bin and the daily snapshots. Every save is watched (see <see cref="SettingsStore.Saved"/>): a changed state
/// becomes an undo step (<see cref="UndoHistory"/>), so no call site has to say what it changed. Undoing writes the earlier state back into the live
/// settings in place, so surfaces holding a widget keep working, then redraws everything and saves.
/// </summary>
internal sealed partial class DeckHub
{
    private readonly UndoHistory history = new();
    private bool historyStarted, applying, rebaseNext;
    private string? nextLabel;

    /// <summary>The steps, the recycle bin or the snapshots changed (raised on the UI thread).</summary>
    public event Action? HistoryChanged;

    public bool CanUndo => history.CanUndo;
    public bool CanRedo => history.CanRedo;
    public string? UndoLabel => history.UndoLabel;
    public string? RedoLabel => history.RedoLabel;
    public IReadOnlyList<UndoStep> UndoSteps => history.UndoSteps;
    public IReadOnlyList<UndoStep> RedoSteps => history.RedoSteps;
    public IReadOnlyList<TrashEntry> Trash => Settings.Trash;

    // ---------- Starting ----------

    /// <summary>Called once from <see cref="Start"/>: empties the old part of the bin, then saves once so the first state history compares with is exactly what every later save produces.</summary>
    private void StartHistory()
    {
        if (historyStarted) return;
        historyStarted = true;
        Store.Saved += OnStoreSaved;
        history.Changed += () => HistoryChanged?.Invoke();
        history.Evicted += SweepMedia;
        HistoryMedia.Held = MediaHeld;
        TrashBin.Purge(Settings, DateTimeOffset.Now, Settings.History.DaysFor(HistoryKeys.Trash));
        rebaseNext = true;
        Save();
        rebaseNext = false;
        // A settings file recovered from its backup may not mention pictures the damaged one did: leave the media alone then.
        if (Store.LastLoadWarning is null) SweepMedia();
    }

    private void OnStoreSaved(DeckSettings settings, string json)
    {
        if (closing) return;
        if (!uiDispatcher.CheckAccess()) { uiDispatcher.BeginInvoke(new Action(() => OnStoreSaved(settings, json))); return; }
        if (rebaseNext || applying)
        {
            rebaseNext = false;
            history.Rebase(json);
            HistoryChanged?.Invoke();
            return;
        }
        string? label = nextLabel;
        nextLabel = null;
        if (history.Observe(json, DateTime.UtcNow, label) == Observed.Recorded && history.UndoLabel is { } step && HistoryState.IsDestructive(step))
            AnnounceUndoLater(step);
    }

    // ---------- Undo and redo ----------

    public bool Undo() => Step(redo: false);
    public bool Redo() => Step(redo: true);

    private bool Step(bool redo)
    {
        if (closing) return false;
        // Anything still waiting for its delayed save (a tracker click) becomes a step first, so it is what gets undone.
        FlushAll();
        var step = redo ? history.Redo(DateTime.UtcNow) : history.Undo(DateTime.UtcNow);
        if (step is null)
        {
            Announce(redo ? "Nothing to redo." : "Nothing to undo.");
            return false;
        }
        if (!ApplyState(step.State))
        {
            Announce("Could not " + (redo ? "redo" : "undo") + " that change.");
            return false;
        }
        // "Undid moved Weather · Redo" (or the other way round): one button to take it back.
        string what = (redo ? "Redid " : "Undid ") + Lower(step.Label) + ".";
        if (redo) Announce(what, "Undo", () => Undo());
        else Announce(what, "Redo", () => Redo());
        return true;
    }

    private static string Lower(string label) => label.Length > 1 ? char.ToLowerInvariant(label[0]) + label[1..] : label;

    /// <summary>Puts a history state into the live settings, redraws and saves. The save does not count as a new step.</summary>
    private bool ApplyState(string stateJson)
    {
        DeckSettings restored;
        try { restored = SettingsStore.Parse(stateJson); }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException) { App.Log(ex); return false; }
        applying = true;
        try
        {
            RestoreInto(restored);
            RenderAll();
            Save();
        }
        finally { applying = false; }
        StateChanged?.Invoke(StateChange.Layouts);
        LayoutHotkeysChanged?.Invoke();
        return true;
    }

    private static readonly PropertyInfo[] ItemProperties = typeof(LauncherItem).GetProperties().Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0).ToArray();

    /// <summary>
    /// Writes the restored parts of <paramref name="restored"/> (see <see cref="HistoryState.Restored"/>) into the live settings. A widget that exists on both
    /// sides keeps its live object (surfaces, floating windows and timers hold it) and takes the old values; downloaded data and Google task bookkeeping stay as they are now.
    /// </summary>
    private void RestoreInto(DeckSettings restored)
    {
        var live = Settings.Items.Where(i => i is not null).ToDictionary(i => i.Id, StringComparer.Ordinal);
        var merged = new List<LauncherItem>(restored.Items.Count);
        foreach (var then in restored.Items)
        {
            if (!live.TryGetValue(then.Id, out var now)) { merged.Add(then); continue; }
            KeepLive(now, then);
            foreach (var property in ItemProperties) property.SetValue(now, property.GetValue(then));
            merged.Add(now);
        }
        Settings.Items.Clear();
        Settings.Items.AddRange(merged);
        foreach (var name in HistoryState.Restored.Where(n => n != nameof(DeckSettings.Items)))
            if (typeof(DeckSettings).GetProperty(name) is { CanWrite: true } property) property.SetValue(Settings, property.GetValue(restored));
    }

    private static void KeepLive(LauncherItem now, LauncherItem then)
    {
        foreach (var name in HistoryState.VolatileItemProperties)
            if (typeof(LauncherItem).GetProperty(name) is { } property) property.SetValue(then, property.GetValue(now));
        foreach (var key in then.Options.Keys.Where(k => k.StartsWith(HistoryState.VolatileOptionPrefix, StringComparison.Ordinal)).ToList()) then.Options.Remove(key);
        foreach (var (key, value) in now.Options.Where(o => o.Key.StartsWith(HistoryState.VolatileOptionPrefix, StringComparison.Ordinal))) then.Options[key] = value;
        if (now.Kind == ItemKinds.Tasks) then.GoogleTaskDeletes = now.GoogleTaskDeletes;
    }

    /// <summary>Ctrl+Z undoes, Ctrl+Y or Ctrl+Shift+Z redoes (not while a text box has the keyboard: it has its own undo). True if the key was used.</summary>
    internal bool TryHistoryKey(KeyEventArgs e)
    {
        if (e.Handled || e.Key is not (Key.Z or Key.Y)) return false;
        var modifiers = Keyboard.Modifiers;
        bool undo = e.Key == Key.Z && modifiers == ModifierKeys.Control;
        bool redo = e.Key == Key.Y && modifiers == ModifierKeys.Control || e.Key == Key.Z && modifiers == (ModifierKeys.Control | ModifierKeys.Shift);
        if (!undo && !redo) return false;
        if (Keyboard.FocusedElement is TextBoxBase or PasswordBox) return false;
        if (redo) Redo(); else Undo();
        e.Handled = true;
        return true;
    }

    // ---------- Telling the user ----------

    /// <summary>A message on every deck and in the control center, optionally with a button (the "Undo" in the toast).</summary>
    private void Announce(string text, string? action = null, Action? onAction = null)
    {
        foreach (var surface in Surfaces) surface.SetStatus(text, action, onAction);
        center?.ShowToast(text, action, onAction);
    }

    /// <summary>"Removed Note · Undo": after the current handler, so it replaces any plain message set meanwhile.</summary>
    private void AnnounceUndoLater(string label)
    {
        uiDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (!closing && history.UndoLabel == label) Announce(label + ".", "Undo", () => Undo());
        }));
    }

    // ---------- The recycle bin ----------

    /// <summary>Removes a widget into the bin (no confirmation, no redraw, no save: the caller does those).</summary>
    internal void TrashItem(LauncherItem item) => TrashBin.Add(Settings, item, DateTimeOffset.Now);

    /// <summary>Puts a widget from the bin back where it was.</summary>
    public bool RestoreFromTrash(string entryId)
    {
        FlushAll();
        if (TrashBin.Restore(Settings, entryId) is not { } item) return false;
        RenderAll();
        Save();
        SetStatus($"Restored {(string.IsNullOrWhiteSpace(item.Name) ? "the widget" : item.Name)}.");
        HistoryChanged?.Invoke();
        return true;
    }

    /// <summary>Deletes one widget from the bin for good (Undo still brings it back until the history moves on).</summary>
    public void DeleteFromTrash(string entryId)
    {
        FlushAll();
        if (TrashBin.Delete(Settings, entryId) is null) return;
        Save();
        HistoryChanged?.Invoke();
    }

    public void EmptyTrash()
    {
        FlushAll();
        if (TrashBin.Empty(Settings).Count == 0) return;
        Save();
        HistoryChanged?.Invoke();
    }

    /// <summary>Sets how many days one kind of history is kept (Settings → History). Null = the built-in default; 0 = forever. Saved now; older entries go when their widget next saves (the bin at its next purge).</summary>
    public void SetHistoryDays(string key, int? days)
    {
        if (days is { } value) Settings.History.Days[key] = Math.Max(0, value);
        else Settings.History.Days.Remove(key);
        Save();
    }

    /// <summary>Empties what has been in the bin for longer than the days set in Settings → History (at start and at midnight). Not an undo step.</summary>
    private void PurgeTrash()
    {
        if (!historyStarted || TrashBin.Purge(Settings, DateTimeOffset.Now, Settings.History.DaysFor(HistoryKeys.Trash)).Count == 0) return;
        FlushAll();
        rebaseNext = true;
        Save();
        rebaseNext = false;
        SweepMedia();
        HistoryChanged?.Invoke();
    }

    // ---------- Daily snapshots ----------

    public IReadOnlyList<SnapshotInfo> Snapshots => DailySnapshots.List(Store.FilePath);

    /// <summary>What restoring a snapshot would change, for the preview: counts and names, or null if the file can't be read.</summary>
    public HistoryState.Comparison? PreviewSnapshot(SnapshotInfo snapshot)
    {
        try { return HistoryState.Compare(File.ReadAllText(snapshot.Path), SettingsStore.Serialize(Settings)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// Goes back to an earlier day: the current settings are copied aside first, widgets that are not in that day move to the recycle bin, the rest
    /// of that day's widgets, layouts and look are put back, and the change is one undo step ("Restored 5 Oct"). Returns an error message, or null.
    /// </summary>
    public string? RestoreSnapshot(SnapshotInfo snapshot)
    {
        DeckSettings then;
        try { then = SettingsStore.Parse(File.ReadAllText(snapshot.Path)); }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException or IOException or UnauthorizedAccessException) { return "That day's copy could not be read: " + ex.Message; }
        FlushAll();
        if (!Save()) return "Could not save the current settings first, so nothing was changed.";
        try { DailySnapshots.BackupBeforeRestore(Store.FilePath, DateTime.Now); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "Could not keep a copy of the current settings first, so nothing was changed: " + ex.Message; }

        var thenIds = then.Items.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var item in Settings.Items.Where(i => !thenIds.Contains(i.Id)).ToList()) TrashBin.Add(Settings, item, DateTimeOffset.Now);
        // The bin keeps what is in it now (minus widgets that are back on the deck) and gains that day's bin.
        var keep = Settings.Trash.Where(t => !thenIds.Contains(t.Item.Id)).ToList();
        then.Trash = keep.Concat(then.Trash.Where(t => keep.All(k => k.Id != t.Id) && !thenIds.Contains(t.Item.Id))).Take(TrashBin.MaxEntries).ToList();

        nextLabel = "Restored " + snapshot.Day.ToString("d MMM", CultureInfo.CurrentCulture);
        RestoreInto(then);
        RenderAll();
        Save();
        StateChanged?.Invoke(StateChange.Layouts);
        LayoutHotkeysChanged?.Invoke();
        Announce($"Restored {snapshot.Day.ToString("d MMM", CultureInfo.CurrentCulture)}.", "Undo", () => Undo());
        HistoryChanged?.Invoke();
        return null;
    }

    // ---------- Media files ----------

    /// <summary>Every text that may mention a picture: the live settings (with the bin), every state undo can reach, and the snapshot files.</summary>
    private List<string> MediaTexts()
    {
        var texts = history.States.ToList();
        texts.Add(SettingsStore.Serialize(Settings));
        texts.AddRange(DailySnapshots.Texts(Store.FilePath));
        return texts;
    }

    /// <summary>True if the picture is still needed by the bin, the history or a snapshot (so it must not be deleted yet).</summary>
    internal bool MediaHeld(string path)
    {
        string name = Path.GetFileName(path);
        if (name.Length == 0) return false;
        if (Settings.Trash.Any(t => string.Equals(t.Item.IconPath, path, StringComparison.OrdinalIgnoreCase) || string.Equals(t.Item.ImagePath, path, StringComparison.OrdinalIgnoreCase))) return true;
        return history.Holds(name) || DailySnapshots.Texts(Store.FilePath).Any(t => t.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Deletes copies in the media folder that nothing refers to any more (live, bin, history, snapshots).</summary>
    internal void SweepMedia()
    {
        try { MediaSweeper.Sweep(Store.MediaDirectory, MediaTexts(), DateTime.UtcNow, MediaSweeper.DefaultMinimumAge); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { App.Log(ex); }
    }

    // ---------- Sample data for the History tab's render ----------

    /// <summary>
    /// Smoke renders only: fills the History tab with believable content through the real code paths (a few changes, two removed widgets, and
    /// snapshots of earlier days) so the tab can be looked at.
    /// </summary>
    internal void SeedHistorySample()
    {
        var items = Settings.Items.Where(i => !i.OnTop && i.GroupId.Length == 0 && i.Kind != ItemKinds.Group).ToList();
        if (items.Count < 4) return;
        // Earlier days: copies of the settings with a widget fewer, or a layout more, written as ordinary snapshots.
        var today = DateOnly.FromDateTime(DateTime.Now);
        string baseJson = SettingsStore.Serialize(Settings);
        Directory.CreateDirectory(DailySnapshots.FolderFor(Store.FilePath));
        foreach (var (days, drop, move) in new[] { (1, 0, true), (2, 1, false), (4, 2, true), (9, 3, false) })
        {
            var earlier = SettingsStore.Parse(baseJson);
            if (drop > 0) earlier.Items.RemoveRange(Math.Max(0, earlier.Items.Count - drop), Math.Min(drop, earlier.Items.Count));
            if (move && earlier.Items.Count > 0) earlier.Items[0].X = Math.Clamp(earlier.Items[0].X + 0.05, 0, 1);
            if (days >= 4) earlier.Layouts.RemoveAll(l => l.Id != earlier.ActiveFloatingLayout && l.Id != earlier.ActiveBackgroundLayout && l.Name != "Default");
            File.WriteAllText(DailySnapshots.PathFor(Store.FilePath, today.AddDays(-days)), SettingsStore.Serialize(earlier));
        }
        // Removed widgets, as if removed 1 and 12 days ago.
        TrashBin.Add(Settings, items[^1], DateTimeOffset.Now.AddHours(-3));
        Save();
        TrashBin.Add(Settings, items[^2], DateTimeOffset.Now.AddDays(-12));
        Save();
        // A few more steps (each of a different kind, so they stay separate), then one undone so Redo is available too.
        items[0].X = Math.Clamp(items[0].X + 0.03, 0, 1);
        RenderAll();
        Save();
        items[1].X = Math.Clamp(items[1].X - 0.02, 0, 1);
        RenderAll();
        Save();
        items[0].Hidden = true;
        RenderAll();
        Save();
        Undo();
        HistoryChanged?.Invoke();
    }
}

internal sealed partial class DeckHub
{
    /// <summary>Smoke renders only: exercises undo, redo, the bin and a day's restore on the live deck and reports each check.</summary>
    internal string VerifyHistory()
    {
        var lines = new List<string>();
        void Check(bool ok, string what) => lines.Add((ok ? "ok: " : "FAIL: ") + "history: " + what);
        if (Settings.Items.FirstOrDefault(i => !i.OnTop && !i.Hidden && i.GroupId.Length == 0 && i.Kind != ItemKinds.Group) is not { } item || Primary is not { } deck) return "history: no widget to test";
        string id = item.Id, name = item.Name;
        double x0 = item.X;
        item.X = x0 > 0.5 ? x0 - 0.1 : x0 + 0.1;
        RenderAll();
        Save();
        Check(UndoLabel == "Moved " + name, $"a drag is labelled ({UndoLabel})");
        Undo();
        Check(Settings.Items.FirstOrDefault(i => i.Id == id) is { } same && ReferenceEquals(same, item) && Math.Abs(item.X - x0) < 1e-9 && CanRedo, "undo puts a moved widget back, in the same object");
        Redo();
        Check(Math.Abs(item.X - x0) > 0.05 && !CanRedo, "redo moves it again");
        Undo();
        int count = Settings.Items.Count;
        TrashItem(item);
        RenderAll();
        Save();
        Check(Settings.Items.Count == count - 1 && Trash.Any(t => t.Item.Id == id) && UndoLabel == "Removed " + name && !deck.Shows(id), "removing sends it to the bin and off the deck");
        Undo();
        Check(Settings.Items.Count == count && !Trash.Any(t => t.Item.Id == id) && deck.Shows(id), "undo brings a removed widget back (and empties its bin entry)");
        TrashItem(item);
        RenderAll();
        Save();
        var entry = Trash.First(t => t.Item.Id == id);
        Check(RestoreFromTrash(entry.Id) && Settings.Items.Count == count && deck.Shows(id) && Math.Abs(item.X - x0) < 1e-9, "restoring from the bin puts it back where it was");
        if (Snapshots.FirstOrDefault() is { } snapshot)
        {
            var error = RestoreSnapshot(snapshot);
            int after = Settings.Items.Count;
            Check(error is null && Directory.EnumerateFiles(DailySnapshots.FolderFor(Store.FilePath), "before-restore-*").Any() && UndoLabel?.StartsWith("Restored ", StringComparison.Ordinal) == true, $"restoring a day keeps a copy first and is one undo step ({error ?? UndoLabel})");
            Undo();
            Check(Settings.Items.Count == count && Settings.Items.Any(i => i.Id == id), $"undo of a day's restore ({after} -> {Settings.Items.Count} widgets)");
        }
        return string.Join("\n", lines);
    }
}

/// <summary>Lets <see cref="Appearance.ReleasePicture"/> (static) ask whether history still needs a picture.</summary>
internal static class HistoryMedia
{
    internal static Func<string, bool>? Held;
    internal static bool IsHeld(string path) => Held?.Invoke(path) ?? false;
}
