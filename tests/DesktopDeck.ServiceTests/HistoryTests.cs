using DesktopDeck.Models;
using DesktopDeck.Services;

/// <summary>History: undo steps and coalescing, the recycle bin, daily snapshots and media sweeping. No UI.</summary>
public static class HistoryTests
{
    private static readonly DateTime T0 = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    public static void Run(Action<bool, string> check, string folder)
    {
        string root = Path.Combine(folder, "history");
        Directory.CreateDirectory(root);
        Undo(check);
        Labels(check);
        Bin(check, root);
        Snapshots(check, root);
        KeepDays(check, root);
        Media(check, root);
    }

    /// <summary>Settings → History: the days each history is kept, and the code that trims each one with its chosen days (0 = keep forever).</summary>
    private static void KeepDays(Action<bool, string> check, string root)
    {
        var history = new HistorySettings();
        check(history.DaysFor(HistoryKeys.Journal) == 1098 && history.DaysFor(HistoryKeys.Focus) == 7 && history.DaysFor(HistoryKeys.StudyLog) == 0 && history.DaysFor("unknown") == 0, "History: built-in defaults when nothing is chosen");
        history.Days[HistoryKeys.Focus] = 30;
        history.Days[HistoryKeys.Snapshots] = -4;
        check(history.DaysFor(HistoryKeys.Focus) == 30 && history.DaysFor(HistoryKeys.Snapshots) == 14, "History: a chosen value wins; a negative one falls back to the default");
        var cleaned = HistorySettings.Normalized(new HistorySettings { Days = new(StringComparer.Ordinal) { [" focus "] = 14, [""] = 3, ["timer"] = -1, ["habits"] = 0 } });
        check(cleaned.Days.Count == 2 && cleaned.Days["focus"] == 14 && cleaned.Days["habits"] == 0, "History: blank keys and negative days are dropped, spaces trimmed, 0 kept");
        check(HistorySettings.Normalized(null).Days.Count == 0, "History: a missing block becomes empty settings");

        var day = new DateOnly(2026, 10, 4);
        var kept = new HabitEntry { Name = "Walk" };
        Trackers.Toggle(kept, day.AddDays(-500), 0);
        Trackers.Toggle(kept, day, 0);
        check(kept.Days.Count == 2, "Habits: 0 keeps every day");
        var trimmed = new HabitEntry { Name = "Walk" };
        Trackers.Toggle(trimmed, day.AddDays(-500), 0);
        Trackers.Toggle(trimmed, day, 30);
        check(trimmed.Days.Count == 1 && trimmed.Days[0] == Trackers.Key(day), "Habits: a 30-day limit drops older days when a day is ticked");

        var pomodoro = new PomodoroState { Mode = "focus", Log = new() { ["2024-01-01"] = 2 } };
        Planner.Advance(pomodoro, day, true, 0);
        check(pomodoro.Log.ContainsKey("2024-01-01") && pomodoro.Log.Count == 2, "Timer: 0 keeps every day's sessions");
        var defaults = new PomodoroState { Mode = "focus", Log = new() { ["2024-01-01"] = 2 } };
        Planner.Advance(defaults, day, true);
        check(!defaults.Log.ContainsKey("2024-01-01") && defaults.Log.Count == 1, "Timer: the default keeps 400 days");

        var bin = new DeckSettings();
        bin.Trash.Add(new TrashEntry { RemovedAt = new DateTimeOffset(T0).AddDays(-100) });
        check(TrashBin.Purge(bin, new DateTimeOffset(T0), 0).Count == 0 && bin.Trash.Count == 1, "Recycle bin: 0 days keeps everything until emptied");
        check(TrashBin.Purge(bin, new DateTimeOffset(T0), 90).Count == 1 && bin.Trash.Count == 0, "Recycle bin: a 90-day limit empties what is older");

        string keepRoot = Path.Combine(root, "keep-days");
        Directory.CreateDirectory(keepRoot);
        string settingsFile = Path.Combine(keepRoot, "settings.json");
        File.WriteAllText(settingsFile, "{}");
        for (int i = 0; i < 4; i++) DailySnapshots.TakeIfDue(settingsFile, new DateOnly(2026, 10, 1).AddDays(i), 0);
        check(DailySnapshots.List(settingsFile).Count == 4, "Snapshots: 0 keeps every day");
        DailySnapshots.TakeIfDue(settingsFile, new DateOnly(2026, 10, 5), 2);
        check(DailySnapshots.List(settingsFile).Count == 2 && DailySnapshots.List(settingsFile)[0].Day == new DateOnly(2026, 10, 5), "Snapshots: a 2-day limit keeps the newest two");
    }

    private static DeckSettings Sample()
    {
        var s = new DeckSettings
        {
            Items =
            [
                new LauncherItem { Id = "clock", Kind = ItemKinds.Clock, Name = "Clock", X = 0.1, Y = 0.1 },
                new LauncherItem { Id = "notes", Kind = ItemKinds.Note, Name = "Notes", X = 0.5, Y = 0.2, Width = 276, Height = 228 },
                new LauncherItem { Id = "weather", Kind = ItemKinds.Weather, Name = "Weather", OnTop = true, FloatX = 0.7, FloatY = 0.6, FloatWidth = 300, FloatHeight = 330 },
            ],
        };
        LayoutEngine.EnsureDefaults(s);
        return s;
    }

    private static string Json(DeckSettings s)
    {
        // The same pipeline a save runs, so the layouts mirror the items.
        var copy = SettingsStore.Parse(SettingsStore.Serialize(s));
        LayoutEngine.CaptureActive(copy);
        return SettingsStore.Serialize(copy);
    }

    private static DeckSettings Load(string json) => SettingsStore.Parse(json);

    // ---------- Undo ----------

    private static void Undo(Action<bool, string> check)
    {
        var s = Sample();
        var history = new UndoHistory();
        history.Reset(Json(s));
        check(!history.CanUndo && !history.CanRedo, "History: starts empty");

        s.Items[1].X = 0.6;
        var now = T0;
        check(history.Observe(Json(s), now) == Observed.Recorded && history.UndoLabel == "Moved Notes", "History: a change records a step labelled from what moved");
        s.Items[1].X = 0.7;
        check(history.Observe(Json(s), now.AddMilliseconds(900)) == Observed.Coalesced && history.UndoSteps.Count == 1, "History: the same kind of change within 1.5 s is the same step");
        s.Items[1].X = 0.75;
        check(history.Observe(Json(s), now.AddMilliseconds(900 + 1400)) == Observed.Coalesced && history.UndoSteps.Count == 1, "History: the window slides while changes keep coming");
        s.Items[1].X = 0.8;
        check(history.Observe(Json(s), now.AddMilliseconds(900 + 1400 + 1600)) == Observed.Recorded && history.UndoSteps.Count == 2, "History: a change after the window is a new step");

        s.Items[0].Name = "Big clock";
        history.Observe(Json(s), now.AddSeconds(10));
        s.Items[2].Hidden = true;
        history.Observe(Json(s), now.AddSeconds(10.2));
        check(history.UndoSteps.Count == 4 && history.UndoSteps[0].Label == "Hid Weather" && history.UndoSteps[1].Label == "Edited Big clock", "History: different kinds of change are separate steps even when quick");

        // Undo and redo walk the states.
        var undone = history.Undo(now.AddSeconds(20));
        check(undone is not null && undone.Label == "Hid Weather" && !Load(undone.State).Items[2].Hidden && history.CanRedo && history.RedoLabel == "Hid Weather", "History: undo gives back the state before the step");
        var again = history.Undo(now.AddSeconds(21));
        check(again is not null && Load(again.State).Items[0].Name == "Clock" && Load(again.State).Items[1].X == 0.8, "History: undo again goes one step further back");
        var redone = history.Redo(now.AddSeconds(22));
        check(redone is not null && Load(redone.State).Items[0].Name == "Big clock", "History: redo goes forward again");
        s.Items[0].Name = "Big clock";
        s.Items[2].Hidden = false;
        history.Rebase(Json(s));
        s.Items[1].Y = 0.9;
        history.Observe(Json(s), now.AddSeconds(30));
        check(!history.CanRedo && history.UndoLabel == "Moved Notes", "History: a new change clears redo");

        // A change right after an undo is not folded into an older step.
        var folded = new UndoHistory();
        var f = Sample();
        folded.Reset(Json(f));
        f.Items[1].X = 0.9;
        folded.Observe(Json(f), T0);
        folded.Undo(T0.AddMilliseconds(100));
        f.Items[1].X = 0.3;
        folded.Rebase(Json(f));
        f.Items[1].X = 0.4;
        check(folded.Observe(Json(f), T0.AddMilliseconds(200)) == Observed.Recorded && folded.UndoSteps.Count == 1 && !folded.CanRedo, "History: nothing coalesces across an undo");

        // Changed and changed back quickly: nothing to undo.
        var toggle = new UndoHistory();
        var t = Sample();
        toggle.Reset(Json(t));
        t.Items[0].Count = 5;
        toggle.Observe(Json(t), T0);
        t.Items[0].Count = 0;
        toggle.Observe(Json(t), T0.AddMilliseconds(300));
        check(!toggle.CanUndo, "History: a counter clicked up and down quickly leaves no step");

        // The cap.
        var capped = new UndoHistory(cap: 50);
        var c = Sample();
        capped.Reset(Json(c));
        int evicted = 0;
        capped.Evicted += () => evicted++;
        for (int i = 1; i <= 60; i++)
        {
            c.Items[0].X = i / 100.0;
            capped.Observe(Json(c), T0.AddSeconds(i * 10));
        }
        check(capped.UndoSteps.Count == 50 && evicted > 0, "History: at most 50 steps are kept");
        check(Load(capped.UndoSteps[^1].State).Items[0].X == 0.1, "History: the oldest steps fall off first");

        // Noise is not a change.
        var quiet = new UndoHistory();
        var q = Sample();
        quiet.Reset(Json(q));
        q.Items[2].WeatherJson = "{\"temp\":3}";
        q.Items[2].WeatherFetched = DateTimeOffset.Now;
        q.Items[1].Options["cache.json"] = "[1,2,3]";
        check(quiet.Observe(Json(q), T0) == Observed.Unchanged && !quiet.CanUndo, "History: weather and download caches are not changes");
        q.Items[1].Options["other"] = "x";
        check(quiet.Observe(Json(q), T0.AddSeconds(5)) == Observed.Recorded, "History: other options are");

        // Accounts and machine settings are outside undo.
        var scoped = HistoryState.Capture(SettingsStore.Serialize(new DeckSettings { GoogleEmail = "me@example.com", StartAtLogin = false }));
        check(!scoped.Contains("me@example.com") && !scoped.Contains("StartAtLogin") && scoped.Contains("Items"), "History: accounts and start-up are not part of a state");

        // An explicit label wins.
        var named = new UndoHistory();
        var n = Sample();
        named.Reset(Json(n));
        n.Items[0].X = 0.3;
        named.Observe(Json(n), T0, "Nudged 2 widgets");
        check(named.UndoLabel == "Nudged 2 widgets", "History: callers can name a step");
        check(named.Holds("Clock") && !named.Holds("no-such-file.png"), "History: reachable states can be searched (for media)");
    }

    private static void Labels(Action<bool, string> check)
    {
        string Label(Action<DeckSettings> change)
        {
            var s = Sample();
            string before = Json(s);
            change(s);
            return HistoryState.Describe(HistoryState.Capture(before), HistoryState.Capture(Json(s)));
        }
        check(Label(s => s.Items[0].X = 0.4) == "Moved Clock", "History labels: Moved");
        check(Label(s => s.Items[1].Width = 400) == "Resized Notes", "History labels: Resized");
        check(Label(s => s.Items[0].Hidden = true) == "Hid Clock" && Label(s => { s.Items[0].Hidden = true; s.Items[0].Hidden = false; s.Items[1].Locked = true; }) == "Locked Notes", "History labels: Hid and Locked");
        check(Label(s => s.Items[1].Text = "hello") == "Edited Notes", "History labels: Edited");
        check(Label(s => s.Items[1].Name = "") == "Edited Note", "History labels: an unnamed widget uses its kind");
        check(Label(s => s.Items.Add(new LauncherItem { Id = "x", Kind = ItemKinds.Counter, Name = "Counter" })) == "Added Counter", "History labels: Added");
        check(Label(s => s.Items.RemoveAt(0)) == "Removed Clock", "History labels: Removed");
        check(Label(s => TrashBin.Add(s, s.Items[1], T0)) == "Removed Notes", "History labels: Removed (to the bin)");
        check(Label(s => s.Items[0].X = 0.4) != Label(s => s.Items[1].X = 0.4), "History labels: name the widget");
        check(Label(s => { s.Items[0].X = 0.4; s.Items[1].X = 0.4; }) == "Moved 2 widgets", "History labels: several widgets");
        check(Label(s => s.Appearance.CardColor = "#112233") == "Changed appearance", "History labels: appearance");
        check(Label(s => s.SnapToGrid = false) == "Changed settings", "History labels: other settings");
        var layouts = Sample();
        string a = Json(layouts);
        var other = LayoutEngine.CreateFromCurrent(layouts, LayoutLayers.Floating, "Study");
        check(HistoryState.Describe(HistoryState.Capture(a), HistoryState.Capture(Json(layouts))) == "Switched to Study", "History labels: switching names the layout");
        check(HistoryState.IsDestructive("Removed Clock") && HistoryState.IsDestructive("Moved Clock") && !HistoryState.IsDestructive("Added Clock") && !HistoryState.IsDestructive("Edited Clock"), "History labels: removing and moving offer Undo");
        _ = other;
    }

    // ---------- The recycle bin ----------

    private static void Bin(Action<bool, string> check, string root)
    {
        var s = Sample();
        s.Items[1].Tasks = Enumerable.Range(0, 3).Select(i => new TaskEntry { Text = "t" + i }).ToList();
        LayoutEngine.EnsureDefaults(s);
        LayoutEngine.CaptureActive(s);
        var other = LayoutEngine.CreateFromCurrent(s, LayoutLayers.Background, "Second");
        LayoutEngine.CaptureActive(s);
        var first = LayoutEngine.Of(s, LayoutLayers.Background).First(l => l.Id != other.Id);

        var entry = TrashBin.Add(s, s.Items[1], T0);
        check(s.Items.All(i => i.Id != "notes") && s.Trash.Count == 1 && s.Trash[0] == entry && entry.Item.Tasks.Count == 3, "Bin: removing a widget moves it to the bin with its data");
        check(entry.Layer == LayoutLayers.Background && entry.LayoutEntries.ContainsKey(first.Id) && entry.LayoutEntries.ContainsKey(other.Id) && entry.LayoutEntries[other.Id].X == 0.5, "Bin: it remembers its place in every layout");
        check(s.Layouts.All(l => !l.Entries.ContainsKey("notes")), "Bin: it is out of every layout meanwhile");

        // The bin survives a save and a load.
        var store = new SettingsStore(Path.Combine(root, "bin"));
        store.Save(s);
        var loaded = store.Load();
        check(loaded.Trash.Count == 1 && loaded.Trash[0].Item.Name == "Notes" && loaded.Trash[0].Item.Tasks.Count == 3 && loaded.Trash[0].RemovedAt == T0, "Bin: it is saved with the settings");
        File.WriteAllText(store.FilePath, """{ "Items": [], "Trash": [ null, { "Id": "", "Item": null }, { "Id": "e1", "Item": { "Id": "i1", "Kind": "BOGUS!", "Name": "Odd", "Target": "", "Arguments": "", "WorkingDirectory": "", "Accent": "#fff" }, "Layer": "nonsense" } ] }""");
        var repaired = store.Load();
        check(repaired.Trash.Count == 1 && repaired.Trash[0].Item.Kind == ItemKinds.Launcher && repaired.Trash[0].Layer == LayoutLayers.Background && repaired.Trash[0].LayoutEntries.Count == 0, "Bin: a hand-edited bin is repaired on load");
        check(new SettingsStore(Path.Combine(root, "nobin")).Load().Trash.Count == 0, "Bin: older files have an empty bin");

        // Restore.
        s.Items[0].X = 0.33;
        var back = TrashBin.Restore(s, entry.Id);
        check(back is not null && back.Id == "notes" && s.Items.Contains(back) && s.Trash.Count == 0 && back.X == 0.5, "Bin: restore puts the widget back where it was");
        check(first.Entries.ContainsKey("notes") && other.Entries.ContainsKey("notes") && other.Entries["notes"].Width == 276, "Bin: restore puts it back in the layouts that had it");
        check(TrashBin.Restore(s, "nothing") is null, "Bin: restoring something that is not there does nothing");

        // An id taken meanwhile.
        var e2 = TrashBin.Add(s, s.Items.First(i => i.Id == "notes"), T0);
        s.Items.Add(new LauncherItem { Id = "notes", Name = "Squatter" });
        var again = TrashBin.Restore(s, e2.Id);
        check(again is not null && again.Id != "notes" && s.Items.Count(i => i.Id == "notes") == 1 && s.Items.Any(i => i.Id == again.Id), "Bin: a restored widget gets a new id if its id was taken");
        s.Items.RemoveAll(i => i.Name == "Squatter");

        // A layout deleted while the widget was in the bin.
        var e3 = TrashBin.Add(s, s.Items.First(i => i.Name == "Notes"), T0);
        LayoutEngine.Delete(s, other.Id);
        var third = TrashBin.Restore(s, e3.Id);
        check(third is not null && s.Layouts.All(l => l.Id != other.Id), "Bin: restoring after a layout was deleted works");

        // Expiry.
        var timed = Sample();
        TrashBin.Add(timed, timed.Items[0], T0);
        TrashBin.Add(timed, timed.Items[0], T0.AddDays(10));
        TrashBin.Add(timed, timed.Items[0], T0.AddDays(29));
        var now = new DateTimeOffset(T0).AddDays(30).AddHours(1);
        check(timed.Trash.Count == 3 && timed.Trash[0].RemovedAt > timed.Trash[2].RemovedAt, "Bin: newest first");
        var expired = TrashBin.Purge(timed, now);
        check(expired.Count == 1 && timed.Trash.Count == 2 && expired[0].RemovedAt == T0, "Bin: entries older than 30 days are emptied");
        check(TrashBin.Purge(timed, new DateTimeOffset(T0).AddDays(30).AddHours(-1)).Count == 0, "Bin: nothing is emptied before 30 days");
        check(TrashBin.DaysLeft(timed.Trash[1], now) == 10 && TrashBin.DaysLeft(timed.Trash[0], now.AddDays(100)) == 0, "Bin: days left");
        check(TrashBin.Delete(timed, timed.Trash[0].Id) is not null && timed.Trash.Count == 1 && TrashBin.Empty(timed).Count == 1 && timed.Trash.Count == 0, "Bin: delete forever and empty");
        var many = Sample();
        for (int i = 0; i < TrashBin.MaxEntries + 5; i++) TrashBin.Add(many, new LauncherItem { Id = "m" + i, Name = "M" + i }, T0.AddMinutes(i));
        check(many.Trash.Count == TrashBin.MaxEntries && many.Trash[0].Item.Id == "m" + (TrashBin.MaxEntries + 4), "Bin: it never holds more than 200 widgets");
        check(!TrashBin.IsBig(new LauncherItem { Tasks = Enumerable.Range(0, 24).Select(_ => new TaskEntry()).ToList() }) && TrashBin.IsBig(new LauncherItem { Tasks = Enumerable.Range(0, 20).Select(_ => new TaskEntry()).ToList(), Habits = Enumerable.Range(0, 5).Select(_ => new HabitEntry()).ToList() }), "Bin: only data-heavy widgets still ask before removal");
    }

    // ---------- Daily snapshots ----------

    private static void Snapshots(Action<bool, string> check, string root)
    {
        string folder = Path.Combine(root, "snap");
        var store = new SettingsStore(folder);
        var s = Sample();
        var saved = new List<string>();
        store.Saved += (_, json) => saved.Add(json);
        store.Save(s);
        check(saved.Count == 1 && saved[0] == SettingsStore.Serialize(s) && !Directory.Exists(DailySnapshots.FolderFor(store.FilePath)), "Snapshots: the first-ever save has nothing to snapshot, and Saved reports the saved JSON");
        File.WriteAllText(Path.Combine(folder, "secrets.json"), "SECRET");
        s.Items[0].Name = "Changed";
        store.Save(s);
        var today = DateOnly.FromDateTime(DateTime.Now);
        var list = DailySnapshots.List(store.FilePath);
        check(list.Count == 1 && list[0].Day == today && File.ReadAllText(list[0].Path).Contains("\"Clock\"") && !File.ReadAllText(list[0].Path).Contains("Changed"), "Snapshots: the first save of the day keeps the file as it was");
        s.Items[0].Name = "Changed again";
        store.Save(s);
        check(DailySnapshots.List(store.FilePath).Count == 1 && File.ReadAllText(list[0].Path).Contains("\"Clock\""), "Snapshots: only one snapshot per day");
        check(!Directory.EnumerateFiles(DailySnapshots.FolderFor(store.FilePath)).Any(f => File.ReadAllText(f).Contains("SECRET")) && Directory.EnumerateFiles(DailySnapshots.FolderFor(store.FilePath)).All(f => !Path.GetFileName(f).Contains("secrets")), "Snapshots: secrets.json is never copied");

        // Rotation by the date in the name.
        string snapFolder = DailySnapshots.FolderFor(store.FilePath);
        for (int i = 1; i <= 20; i++) File.WriteAllText(DailySnapshots.PathFor(store.FilePath, today.AddDays(-i)), "{}");
        File.WriteAllText(Path.Combine(snapFolder, "notes.txt"), "keep");
        File.WriteAllText(Path.Combine(snapFolder, "settings-abc.json"), "keep");
        DailySnapshots.Rotate(store.FilePath);
        var kept = DailySnapshots.List(store.FilePath);
        check(kept.Count == 14 && kept[0].Day == today && kept[^1].Day == today.AddDays(-13), "Snapshots: the last 14 days are kept");
        check(File.Exists(Path.Combine(snapFolder, "notes.txt")) && File.Exists(Path.Combine(snapFolder, "settings-abc.json")), "Snapshots: files with other names are left alone");

        // Taking tomorrow's snapshot rotates the oldest out.
        var tomorrow = today.AddDays(1);
        check(DailySnapshots.TakeIfDue(store.FilePath, tomorrow) is not null && DailySnapshots.TakeIfDue(store.FilePath, tomorrow) is null && DailySnapshots.List(store.FilePath).Count == 14 && DailySnapshots.List(store.FilePath)[^1].Day == today.AddDays(-12), "Snapshots: a new day's snapshot pushes the oldest out");

        // Backups before a restore.
        for (int i = 0; i < 7; i++) DailySnapshots.BackupBeforeRestore(store.FilePath, new DateTime(2026, 10, 6, 10, i, 0));
        check(Directory.EnumerateFiles(snapFolder, "before-restore-*").Count() == 5 && DailySnapshots.List(store.FilePath).Count == 14, "Snapshots: the last 5 pre-restore copies are kept apart from the days");
        check(DailySnapshots.Texts(store.FilePath).Count() >= 19, "Snapshots: texts include every snapshot file");

        // The preview of a restore.
        var then = Sample();
        var now = Sample();
        now.Items.RemoveAt(0);
        now.Items.Add(new LauncherItem { Id = "new", Name = "Newer", Kind = ItemKinds.Counter });
        now.Items[0].X = 0.9;
        var comparison = HistoryState.Compare(Json(then), Json(now));
        check(comparison is { WouldReturn.Count: 1, WouldLeave.Count: 1, WouldChange: 1 } && comparison.WouldReturn[0] == "Clock" && comparison.WouldLeave[0] == "Newer" && comparison.Then.Widgets == 3 && comparison.Now.Widgets == 3, "Snapshots: the preview says what comes back, goes away and changes");
        check(comparison!.Text.Contains("Clock") && comparison.Text.Contains("Newer"), "Snapshots: the preview reads as text");
        var same = HistoryState.Compare(Json(then), Json(then));
        check(same is { WouldChange: 0 } && same.Text == "The same widgets and layouts as now.", "Snapshots: an identical day says so");
        check(HistoryState.Summarize("not json") is null && HistoryState.Summarize(Json(then)) is { Widgets: 3, Floating: 1, Layouts: 2 }, "Snapshots: summaries count widgets, floating widgets and layouts");
    }

    // ---------- Media ----------

    private static void Media(Action<bool, string> check, string root)
    {
        string media = Path.Combine(root, "media");
        Directory.CreateDirectory(media);
        string used = Path.Combine(media, "aaaa.png"), orphan = Path.Combine(media, "bbbb.png"), fresh = Path.Combine(media, "cccc.png"), inHistory = Path.Combine(media, "dddd.png");
        foreach (var f in new[] { used, orphan, fresh, inHistory }) File.WriteAllText(f, "x");
        foreach (var f in new[] { used, orphan, inHistory }) File.SetCreationTimeUtc(f, T0.AddHours(-2));
        File.SetCreationTimeUtc(fresh, T0.AddMinutes(-1));
        var history = new UndoHistory();
        var s = Sample();
        s.Items[0].ImagePath = inHistory;
        history.Reset(Json(s));
        s.Items[0].ImagePath = used;
        history.Observe(Json(s), T0);
        var texts = history.States.Append(Json(s)).ToList();
        int deleted = MediaSweeper.Sweep(media, texts, T0, MediaSweeper.DefaultMinimumAge);
        check(deleted == 1 && !File.Exists(orphan) && File.Exists(used) && File.Exists(inHistory) && File.Exists(fresh), "Media: only unreferenced, settled files are deleted (history still holds the old picture)");
        // Once the step is gone the old picture can go.
        var later = new UndoHistory();
        later.Reset(Json(s));
        check(MediaSweeper.Sweep(media, later.States.ToList(), T0, MediaSweeper.DefaultMinimumAge) == 1 && !File.Exists(inHistory), "Media: a picture only a dropped step needed is deleted");
        check(MediaSweeper.Sweep(Path.Combine(root, "no-media"), [], T0, MediaSweeper.DefaultMinimumAge) == 0, "Media: no media folder is fine");
    }
}
