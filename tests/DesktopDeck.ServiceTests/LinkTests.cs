using DesktopDeck.Models;
using DesktopDeck.Services;

/// <summary>Linked widgets: which fields are shared, linking and unlinking, removal, and that copying settles instead of looping. No UI.</summary>
public static class LinkTests
{
    // The options each plug-in kind declares (the real registry supplies these in the app).
    private static readonly Dictionary<string, string[]> Declared = new()
    {
        ["homework"] = ["subjects", "showSubject", "showDone", "groupMode", "compact", "overdueColor"],
        ["focus"] = ["layout", "compact", "activeFrom", "activeTo"],
        ["kanban"] = ["columns", "columnColors", "wip", "compact"],
        ["gmail"] = ["query", "compact"],
        ["world-clocks"] = ["places"],
    };

    public static void Run(Action<bool, string> check, string folder)
    {
        WidgetLinks.DeclaredKeys = kind => Declared.GetValueOrDefault(kind);
        try
        {
            Fields(check);
            Groups(check);
            Removal(check, folder);
            Sync(check);
        }
        finally { WidgetLinks.DeclaredKeys = null; }
    }

    private static LauncherItem Homework(string name, string items = "", bool floating = false) => new()
    {
        Kind = "homework", Name = name, OnTop = floating,
        Options = new(StringComparer.Ordinal) { ["data.items"] = items, ["_textScale"] = "120", ["compact"] = "true" },
    };

    private static void Fields(Action<bool, string> check)
    {
        var a = Homework("Homework", "[{\"title\":\"Essay\"},{\"title\":\"Quiz\"}]");
        a.Options["subjects"] = "Math | #fff\nLatin | #000";
        a.Options["showDone"] = "false";
        a.Options["cache.stuff"] = "downloaded";
        var b = Homework("Homework (floating)", "", floating: true);
        b.Options["_textScale"] = "80";
        b.Options["compact"] = "false";
        b.Width = 372; b.FloatX = 0.7; b.Accent = "#112233";
        check(WidgetLinks.CopyData(a, b), "Links: Homework data is copied to the other widget");
        check(b.Options["data.items"] == a.Options["data.items"], "Links: the assignment list is shared");
        check(b.Options["subjects"] == a.Options["subjects"], "Links: Homework subjects are content, so they are shared even though Customize declares them");
        check(b.Options["_textScale"] == "80" && b.Options["compact"] == "false", "Links: appearance (_) and declared settings stay per widget");
        check(!b.Options.ContainsKey("showDone") && !b.Options.ContainsKey("cache.stuff"), "Links: other settings and downloaded caches are not copied");
        check(b.Name == "Homework (floating)" && b.OnTop && b.Width == 372 && b.FloatX == 0.7 && b.Accent == "#112233", "Links: name, place, size and accent stay per widget");
        check(!WidgetLinks.CopyData(a, b), "Links: copying twice changes nothing the second time");
        a.Options.Remove("data.items");
        check(WidgetLinks.CopyData(a, b) && !b.Options.ContainsKey("data.items"), "Links: a data key removed on one side is removed on the other");

        // Focus: all its data lives under data.focus; settings (declared) are its own.
        var f1 = new LauncherItem { Kind = "focus", Options = new() { ["data.focus"] = "{\"goal\":\"Write\"}", ["layout"] = "wide", ["activeFrom"] = "08:00" } };
        var f2 = new LauncherItem { Kind = "focus", Options = new() { ["layout"] = "compact" } };
        WidgetLinks.CopyData(f1, f2);
        check(f2.Options["data.focus"] == f1.Options["data.focus"] && f2.Options["layout"] == "compact" && !f2.Options.ContainsKey("activeFrom"), "Links: Focus shares its goal and keeps its own layout and settings");

        // Kanban columns are referenced by the cards, so they travel with them; the column colors are looks.
        var k1 = new LauncherItem { Kind = "kanban", Options = new() { ["data.cards"] = "[]", ["columns"] = "Ideas\nMaking", ["columnColors"] = "#111" } };
        var k2 = new LauncherItem { Kind = "kanban", Options = new() { ["columns"] = "To do\nDoing\nDone", ["columnColors"] = "#222" } };
        WidgetLinks.CopyData(k1, k2);
        check(k2.Options["columns"] == "Ideas\nMaking" && k2.Options["columnColors"] == "#222", "Links: Kanban columns follow the cards, column colors stay");

        // A plug-in with no declared content: its own query stays, anything undeclared that it stored is data (even without a data. prefix).
        var g1 = new LauncherItem { Kind = "gmail", Options = new() { ["query"] = "in:inbox", ["seenIds"] = "1,2", ["cache.json"] = "{}" } };
        var g2 = new LauncherItem { Kind = "gmail", Options = new() { ["query"] = "label:school" } };
        WidgetLinks.CopyData(g1, g2);
        check(g2.Options["query"] == "label:school" && g2.Options["seenIds"] == "1,2" && !g2.Options.ContainsKey("cache.json"), "Links: undeclared keys are shared, declared settings and caches are not");
        check(WidgetLinks.IsDataKey("homework", "data.items") && !WidgetLinks.IsDataKey("homework", "_radius") && !WidgetLinks.IsDataKey("homework", "groupMode"), "Links: data key rule");

        // Built-in kinds.
        var t1 = new LauncherItem { Kind = ItemKinds.Tasks, Name = "Today", DailyReset = true, LastDate = "2026-10-08", HideCompleted = true, Accent = "#111", CardStyle = "glass", Width = 300, Tasks = [new TaskEntry { Text = "Ship", Done = true, GoogleId = "g1", GoogleListId = "L" }], GoogleTasksSync = true, GoogleTaskListId = "L", GoogleTasksWatermark = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), GoogleTaskDeletes = [new GoogleTaskTombstone { ListId = "L", TaskId = "x" }] };
        var t2 = new LauncherItem { Kind = ItemKinds.Tasks, Name = "Tasks 2", CardStyle = "solid", Width = 180, Accent = "#999" };
        var theirList = t2.Tasks;
        check(WidgetLinks.CopyData(t1, t2) && t2.Tasks.Count == 1 && t2.Tasks[0].Text == "Ship" && t2.Tasks[0].GoogleId == "g1" && !ReferenceEquals(t1.Tasks[0], t2.Tasks[0]), "Links: Tasks are copied (deep)");
        check(ReferenceEquals(theirList, t2.Tasks), "Links: lists are filled in place, so open menus and tickers keep working");
        check(t2.GoogleTasksSync && t2.GoogleTaskListId == "L" && t2.GoogleTasksWatermark == t1.GoogleTasksWatermark && t2.GoogleTaskDeletes.Count == 1 && t2.DailyReset && t2.LastDate == "2026-10-08", "Links: Google Tasks bookkeeping and the daily reset travel with the tasks");
        check(!t2.HideCompleted && t2.CardStyle == "solid" && t2.Width == 180 && t2.Accent == "#999" && t2.Name == "Tasks 2", "Links: Tasks view and look settings stay per widget");
        t2.Tasks[0].Done = false;
        check(WidgetLinks.CopyData(t1, t2) && t2.Tasks[0].Done, "Links: a changed task is copied back");

        var m1 = new LauncherItem { Kind = ItemKinds.Timer, AutoNext = true, TimerSound = false, Pomodoro = new PomodoroState { WorkMinutes = 50, Mode = "break", Cycle = 2, EndsAt = new DateTimeOffset(2026, 10, 8, 9, 0, 0, TimeSpan.Zero), Log = { ["2026-10-08"] = 3 } } };
        var m2 = new LauncherItem { Kind = ItemKinds.Timer, TimerSound = true };
        var theirs = m2.Pomodoro;
        WidgetLinks.CopyData(m1, m2);
        check(ReferenceEquals(theirs, m2.Pomodoro) && m2.Pomodoro.WorkMinutes == 50 && m2.Pomodoro.Mode == "break" && m2.Pomodoro.Cycle == 2 && m2.Pomodoro.EndsAt == m1.Pomodoro.EndsAt && m2.Pomodoro.Log["2026-10-08"] == 3, "Links: timer state is copied into the peer's own state object");
        check(m2.AutoNext && m2.TimerSound, "Links: the timer's next-session setting is shared, its sound is per widget");
        check(!WidgetLinks.CopyData(m1, m2), "Links: identical timers need no update");

        var n1 = new LauncherItem { Kind = ItemKinds.Note, Text = "Buy milk", ShowHeader = false, Accent = "#E2B657" };
        var n2 = new LauncherItem { Kind = ItemKinds.Note, Text = "Click to write a note.", ShowHeader = true, Accent = "#222222" };
        WidgetLinks.CopyData(n1, n2);
        check(n2.Text == "Buy milk" && n2.ShowHeader && n2.Accent == "#222222", "Links: a sticky note shares its text, not its look");

        var c1 = new LauncherItem { Kind = ItemKinds.Counter, Count = 7, CountGoal = 10, Step = 2, Accent = "#111" };
        var c2 = new LauncherItem { Kind = ItemKinds.Counter, Accent = "#222" };
        WidgetLinks.CopyData(c1, c2);
        check(c2.Count == 7 && c2.CountGoal == 10 && c2.Step == 2 && c2.Accent == "#222", "Links: counters share the count, goal and step");

        var h1 = new LauncherItem { Kind = ItemKinds.Habits, View = "week", Habits = [new HabitEntry { Name = "Read", Days = ["2026-10-07"] }] };
        var h2 = new LauncherItem { Kind = ItemKinds.Habits, View = "today" };
        WidgetLinks.CopyData(h1, h2);
        h1.Habits[0].Days.Add("2026-10-08");
        check(h2.Habits[0].Days.Count == 1 && h2.View == "today", "Links: habits are copied deeply, the today/week view stays");
        check(WidgetLinks.CopyData(h1, h2) && h2.Habits[0].Days.Count == 2, "Links: new habit days follow");

        var s1 = new LauncherItem { Kind = ItemKinds.Schedule, Blocks = [new ScheduleBlock { Title = "School", Days = ["Mon"] }], StartHour = 7, View = "day", ShowGoogle = false };
        var s2 = new LauncherItem { Kind = ItemKinds.Schedule, StartHour = 9, ShowGoogle = true };
        WidgetLinks.CopyData(s1, s2);
        check(s2.Blocks.Count == 1 && s2.Blocks[0].Title == "School" && s2.StartHour == 9 && s2.ShowGoogle && s2.View == "", "Links: schedules share their blocks, not their hours or calendar choices");

        var gl1 = new LauncherItem { Kind = ItemKinds.Goals, Goals = [new WeeklyGoal { Text = "Run", Milestones = [new Milestone { Text = "5 km" }] }] };
        var gl2 = new LauncherItem { Kind = ItemKinds.Goals };
        WidgetLinks.CopyData(gl1, gl2);
        check(gl2.Goals.Count == 1 && gl2.Goals[0].Milestones.Count == 1 && !ReferenceEquals(gl1.Goals[0], gl2.Goals[0]), "Links: goals and their steps are copied deeply");

        var cd1 = new LauncherItem { Kind = ItemKinds.Countdown, TargetDate = "2026-12-24", TargetTime = "18:00", RepeatYearly = true, Name = "Xmas" };
        var cd2 = new LauncherItem { Kind = ItemKinds.Countdown, Name = "Other" };
        WidgetLinks.CopyData(cd1, cd2);
        check(cd2.TargetDate == "2026-12-24" && cd2.TargetTime == "18:00" && cd2.RepeatYearly && cd2.Name == "Other", "Links: countdowns share their target");

        check(!WidgetLinks.CopyData(c1, new LauncherItem { Kind = ItemKinds.Timer }) && !WidgetLinks.CopyData(c1, c1), "Links: different kinds, or a widget with itself, copy nothing");
        check(!WidgetLinks.CanLink(ItemKinds.Launcher) && !WidgetLinks.CanLink(ItemKinds.Image) && !WidgetLinks.CanLink(ItemKinds.Group) && !WidgetLinks.CanLink(ItemKinds.Clock) && WidgetLinks.CanLink(ItemKinds.Timer) && WidgetLinks.CanLink("homework"), "Links: only widgets that hold something can be linked");
    }

    private static void Groups(Action<bool, string> check)
    {
        var a = new LauncherItem { Kind = ItemKinds.Tasks, Name = "A", Tasks = [new TaskEntry { Text = "one" }] };
        var b = new LauncherItem { Kind = ItemKinds.Tasks, Name = "B" };
        var c = new LauncherItem { Kind = ItemKinds.Tasks, Name = "C", Tasks = [new TaskEntry { Text = "three" }] };
        var timer = new LauncherItem { Kind = ItemKinds.Timer };
        var all = new List<LauncherItem> { a, b, c, timer };
        check(WidgetLinks.Peers(a, all).Count == 0 && !WidgetLinks.IsLinked(a, all), "Links: nothing is linked at first");
        check(WidgetLinks.Candidates(a, all).SequenceEqual([b, c]), "Links: the candidates are the other widgets of the same kind");
        check(WidgetLinks.Link(a, b, all) && a.LinkId.Length > 0 && a.LinkId == b.LinkId, "Links: two unlinked widgets get a new shared link");
        check(b.Tasks.Count == 1 && b.Tasks[0].Text == "one", "Links: the second widget takes the first one's data");
        check(WidgetLinks.Peers(a, all).SequenceEqual([b]) && WidgetLinks.Peers(b, all).SequenceEqual([a]), "Links: peers are the other widgets of the group");
        check(WidgetLinks.Candidates(a, all).SequenceEqual([c]), "Links: widgets already linked together are no longer candidates");
        check(WidgetLinks.Link(a, c, all) && c.LinkId == a.LinkId && c.Tasks[0].Text == "one", "Links: joining an existing group takes that group's data");
        check(WidgetLinks.Peers(c, all).Count == 2, "Links: a group can have three widgets");
        check(!WidgetLinks.Link(a, timer, all) && timer.LinkId.Length == 0, "Links: different kinds can't be linked");
        check(!WidgetLinks.Link(a, a, all), "Links: a widget can't be linked with itself");
        string id = a.LinkId;
        WidgetLinks.Unlink(c, all);
        check(c.LinkId.Length == 0 && a.LinkId == id && b.LinkId == id, "Links: unlinking one widget leaves the others linked");
        WidgetLinks.Unlink(b, all);
        check(b.LinkId.Length == 0 && a.LinkId.Length == 0, "Links: a widget left alone in a group is unlinked too");
        check(c.Tasks.Count == 1 && b.Tasks.Count == 1, "Links: unlinking keeps each widget's data");

        // Keep the other one's data: linking a to c with c's data kept.
        a.Tasks = [new TaskEntry { Text = "A's" }];
        c.Tasks = [new TaskEntry { Text = "C's" }];
        WidgetLinks.Link(c, a, all);
        check(a.Tasks[0].Text == "C's" && a.LinkId == c.LinkId, "Links: the data of the widget you keep replaces the other's");

        // Two groups joined: everybody ends up in one.
        var d = new LauncherItem { Kind = ItemKinds.Tasks }; var e = new LauncherItem { Kind = ItemKinds.Tasks };
        all.AddRange([d, e]);
        WidgetLinks.Link(d, e, all);
        string left = c.LinkId, right = d.LinkId;
        WidgetLinks.Link(c, d, all);
        check(left != right && new[] { a, c, d, e }.All(x => x.LinkId == c.LinkId), "Links: linking two groups merges them");

        // Normalizing a loaded file.
        var lone = new LauncherItem { Kind = ItemKinds.Note, LinkId = "x" };
        var pair1 = new LauncherItem { Kind = "homework", LinkId = "p" }; var pair2 = new LauncherItem { Kind = "homework", LinkId = "p" };
        var launcher = new LauncherItem { Kind = ItemKinds.Launcher, LinkId = "p" };
        var kindMix = new LauncherItem { Kind = ItemKinds.Counter, LinkId = "p" };
        WidgetLinks.Normalize([lone, pair1, pair2, launcher, kindMix]);
        check(lone.LinkId == "" && launcher.LinkId == "" && kindMix.LinkId == "" && pair1.LinkId == "p" && pair2.LinkId == "p", "Links: a loaded file keeps real groups and drops lone, wrong-kind and unlinkable ids");
    }

    private static void Removal(Action<bool, string> check, string folder)
    {
        var s = new DeckSettings();
        var a = Homework("A", "[1]"); var b = Homework("B", "[1]", true); var c = Homework("C", "[1]");
        s.Items.AddRange([a, b, c]);
        WidgetLinks.Link(a, b, s.Items);
        WidgetLinks.Link(a, c, s.Items);
        string id = a.LinkId;
        var entry = TrashBin.Add(s, c, DateTimeOffset.Now);
        check(entry.Item.LinkId == "" && a.LinkId == id && b.LinkId == id, "Links: removing one of three widgets unlinks it and keeps the other two linked");
        TrashBin.Add(s, b, DateTimeOffset.Now);
        check(a.LinkId == "" && s.Trash.All(t => t.Item.LinkId == ""), "Links: removing the second-to-last widget leaves the last one unlinked");
        var restored = TrashBin.Restore(s, entry.Id)!;
        check(restored.LinkId == "" && s.Items.All(i => i.LinkId == ""), "Links: restoring from the recycle bin does not link it again");

        // The links survive saving and loading, and a damaged group is repaired.
        var store = new SettingsStore(Path.Combine(folder, "links"));
        var saved = new DeckSettings();
        var x = Homework("X", "[1]"); var y = Homework("Y", "[1]", true); var z = Homework("Z", "[1]");
        saved.Items.AddRange([x, y, z]);
        WidgetLinks.Link(x, y, saved.Items);
        z.LinkId = "orphan";
        store.Save(saved);
        var back = store.Load();
        check(back.Items[0].LinkId.Length > 0 && back.Items[0].LinkId == back.Items[1].LinkId && back.Items[2].LinkId == "", "Links: a link survives a restart, an orphaned id is dropped on load");

        // Hiding, floating and moving widgets never touch the link.
        y.Hidden = true; y.OnTop = false; y.Display = "monitor-2";
        check(WidgetLinks.IsLinked(x, saved.Items) && WidgetLinks.IsLinked(y, saved.Items), "Links: hiding, floating and moving a widget keep it linked");
    }

    private static void Sync(Action<bool, string> check)
    {
        var a = Homework("A", "[1]"); var b = Homework("B", "[1]", true); var c = Homework("C", "[1]");
        var all = new List<LauncherItem> { a, b, c };
        WidgetLinks.Link(a, b, all); WidgetLinks.Link(a, c, all);
        a.Options["data.items"] = "[1,2]";
        var changed = WidgetLinks.Propagate(a, all);
        check(changed.Count == 2 && b.Options["data.items"] == "[1,2]" && c.Options["data.items"] == "[1,2]", "Links: a change is copied to every peer");
        check(WidgetLinks.Propagate(a, all).Count == 0 && WidgetLinks.Propagate(b, all).Count == 0 && WidgetLinks.Propagate(c, all).Count == 0, "Links: after one pass nothing differs, so peers never update each other again (no loop)");
        b.Options["data.items"] = "[1,2,3]";
        WidgetLinks.Propagate(b, all);
        check(a.Options["data.items"] == "[1,2,3]" && c.Options["data.items"] == "[1,2,3]", "Links: a change made on any peer reaches the rest");
        var lone = Homework("L", "[9]");
        all.Add(lone);
        check(WidgetLinks.Propagate(lone, all).Count == 0 && a.Options["data.items"] == "[1,2,3]", "Links: an unlinked widget changes only itself");

        // Only one linked task list talks to Google.
        var t1 = new LauncherItem { Kind = ItemKinds.Tasks, GoogleTasksSync = true };
        var t2 = new LauncherItem { Kind = ItemKinds.Tasks, GoogleTasksSync = true };
        var t3 = new LauncherItem { Kind = ItemKinds.Tasks };
        var tasks = new List<LauncherItem> { t1, t2, t3 };
        check(WidgetLinks.TaskSyncOwner(t2, tasks) == t2, "Links: an unlinked list syncs by itself");
        WidgetLinks.Link(t1, t2, tasks);
        WidgetLinks.Link(t1, t3, tasks);
        check(WidgetLinks.TaskSyncOwner(t2, tasks) == t1 && WidgetLinks.TaskSyncOwner(t3, tasks) == t1 && WidgetLinks.TaskSyncOwner(t1, tasks) == t1, "Links: of linked lists the first one syncs for all");

        // What the question and the conflict dialog show.
        check(!WidgetLinks.HasData(new LauncherItem { Kind = ItemKinds.Tasks }) && WidgetLinks.HasData(new LauncherItem { Kind = ItemKinds.Tasks, Tasks = [new TaskEntry()] }), "Links: HasData tells an empty widget from one with content");
        check(!WidgetLinks.HasData(new LauncherItem { Kind = "world-clocks", Options = new() { ["places"] = "Paris", ["_radius"] = "20" } }) && WidgetLinks.HasData(Homework("H", "[{\"a\":1}]")), "Links: plug-in HasData looks only at shared keys");
        check(WidgetLinks.Preview(new LauncherItem { Kind = ItemKinds.Tasks, Tasks = [new TaskEntry(), new TaskEntry()] }) == "2 tasks" && WidgetLinks.Preview(Homework("H", "[{\"a\":1},{\"a\":2},{\"a\":3}]")) == "3 items", "Links: previews say how much a widget holds");
    }
}
