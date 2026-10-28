using System.Net;
using System.Net.Http;
using System.Text.Json;
using DesktopDeck.Models;
using DesktopDeck.Services;

/// <summary>Google Tasks sync, Gmail and Classroom: JSON reading and building, merge rules, and the services against a fake Google. No network.</summary>
public static class GoogleExtensionsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    public static async Task RunAsync(Action<bool, string> check, string folder)
    {
        TasksJson(check);
        TaskMerge(check);
        TaskDailyReset(check);
        TaskLocalRules(check);
        await TaskFeed(check);
        TaskStorage(check, folder);
        ApiErrors(check);
        GmailParsing(check);
        await GmailNetwork(check, folder);
        ClassroomParsing(check);
        await ClassroomNetwork(check, folder);
    }

    // ---------- Helpers ----------

    private static GoogleTask R(string id, string title, bool done = false, int updatedMinutes = 0, bool deleted = false, bool hidden = false, int? completedMinutes = null, string etag = "") => new()
    {
        Id = id, Title = title, Completed = done, Updated = T0.AddMinutes(updatedMinutes), Deleted = deleted, Hidden = hidden,
        CompletedAt = done ? T0.AddMinutes(completedMinutes ?? updatedMinutes) : null, ETag = etag.Length > 0 ? etag : "e" + updatedMinutes,
    };

    private static TaskEntry L(string text, string googleId = "", bool done = false, int? syncedMinutes = null, int? modifiedMinutes = null, bool hidden = false) => new()
    {
        Text = text, Done = done, GoogleId = googleId, GoogleListId = googleId.Length > 0 ? "@default" : "",
        GoogleUpdated = googleId.Length > 0 ? T0.AddMinutes(syncedMinutes ?? 0) : null, GoogleEtag = googleId.Length > 0 ? "e" + (syncedMinutes ?? 0) : "",
        Modified = modifiedMinutes is { } m ? T0.AddMinutes(m) : null, Hidden = hidden,
    };

    private static LauncherItem List(params TaskEntry[] tasks) => new() { Kind = ItemKinds.Tasks, GoogleTasksSync = true, DailyReset = true, Tasks = tasks.ToList() };

    private static TaskSyncPlan Plan(LauncherItem list, IReadOnlyList<GoogleTask> remote, bool incremental = false, bool complete = true, DateTimeOffset? since = null)
    {
        var snap = TaskSyncSnapshot.From(list, T0) with { ImportCompletedSince = since ?? T0.AddHours(-12), Watermark = incremental ? T0 : null };
        return GoogleTasksSync.Plan(snap, remote, incremental, complete);
    }

    private static TaskSyncOutcome Outcome(TaskSyncPlan plan, Func<PushOp, PushResult>? push = null) =>
        new("@default", plan.Pulls, plan.Pushes.Select(p => push?.Invoke(p) ?? new PushResult(p, p.Kind == PushKind.Delete ? null : new GoogleTask { Id = p.GoogleId.Length > 0 ? p.GoogleId : "new-" + p.LocalId, Title = p.Title, Completed = p.Done, Updated = T0.AddMinutes(30), ETag = "pushed" }, false, null)).ToList(), plan.Watermark);

    // ---------- Google Tasks JSON ----------

    private static void TasksJson(Action<bool, string> check)
    {
        var tasks = GoogleTasksService.ParseTasks("""
            { "kind": "tasks#tasks", "nextPageToken": "page2", "items": [
              { "id": "a", "title": "  Buy milk ", "status": "needsAction", "updated": "2026-10-05T12:00:00.000Z", "etag": "\"e1\"" },
              { "id": "b", "title": "Done thing", "status": "completed", "completed": "2026-10-05T09:30:00.000Z", "updated": "2026-10-05T09:30:01.250Z", "hidden": true, "parent": "a" },
              { "id": "c", "title": "", "status": "needsAction", "deleted": true, "updated": "2026-10-04T08:00:00-05:00" },
              { "title": "no id" } ] }
            """, out string? next);
        check(next == "page2" && tasks.Count == 3, "Tasks: items parsed, a task without an id is skipped, the next page token is read");
        check(tasks[0].Title == "Buy milk" && !tasks[0].Completed && tasks[0].Updated == new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero) && tasks[0].ETag == "\"e1\"", "Tasks: title trimmed, status, updated and etag read");
        check(tasks[1].Completed && tasks[1].Hidden && tasks[1].Parent == "a" && tasks[1].CompletedAt == new DateTimeOffset(2026, 10, 5, 9, 30, 0, TimeSpan.Zero), "Tasks: completed time, hidden flag and parent read");
        check(tasks[2].Deleted && tasks[2].Updated == new DateTimeOffset(2026, 10, 4, 13, 0, 0, TimeSpan.Zero), "Tasks: deleted flag and a time with an offset read");
        var lists = GoogleTasksService.ParseTaskLists("""{ "items": [ { "id": "L1", "title": "My Tasks" }, { "id": "L2", "title": " " }, { "title": "x" } ] }""", out _);
        check(lists.Count == 2 && lists[0].Id == "L1" && lists[0].Title == "My Tasks" && lists[1].Title == "Tasks", "Tasks: task lists parsed (a blank title gets a name, no id is skipped)");
        check(GoogleTasksService.ParseTasks("{}").Count == 0, "Tasks: an answer without items is an empty list");

        using var create = JsonDocument.Parse(GoogleTasksService.BuildTaskJson("  Call Sam ", false));
        check(create.RootElement.GetProperty("title").GetString() == "Call Sam" && create.RootElement.GetProperty("status").GetString() == "needsAction" && !create.RootElement.TryGetProperty("completed", out _), "Tasks: create body has the trimmed title and status");
        using var tick = JsonDocument.Parse(GoogleTasksService.BuildTaskJson("x", true, patch: true));
        check(tick.RootElement.GetProperty("status").GetString() == "completed" && !tick.RootElement.TryGetProperty("completed", out _), "Tasks: ticking sends status completed");
        using var untick = JsonDocument.Parse(GoogleTasksService.BuildTaskJson("x", false, patch: true));
        check(untick.RootElement.GetProperty("status").GetString() == "needsAction" && untick.RootElement.GetProperty("completed").ValueKind == JsonValueKind.Null, "Tasks: un-ticking sends needsAction and clears the completed time");

        string full = GoogleTasksService.ListTasksUrl("@default", null);
        check(full.Contains("showCompleted=true") && full.Contains("showHidden=true") && full.Contains("maxResults=100") && !full.Contains("showDeleted") && !full.Contains("updatedMin"), "Tasks: a full listing asks for completed and hidden tasks, 100 per page");
        string incremental = GoogleTasksService.ListTasksUrl("L 1", new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.FromHours(-5)), "tok/en");
        check(incremental.Contains("/lists/L%201/tasks?") && incremental.Contains("showDeleted=true") && incremental.Contains("updatedMin=2026-10-05T17%3A00%3A00.000Z") && incremental.Contains("pageToken=tok%2Fen"), "Tasks: an incremental listing uses updatedMin in UTC, asks for deletions, and escapes ids and page tokens");
    }

    // ---------- Merge rules ----------

    private static void TaskMerge(Action<bool, string> check)
    {
        // New and old remote tasks.
        var list = List();
        var plan = Plan(list, [R("n1", "New one"), R("n2", "Old finished", true, -3000, completedMinutes: -3000), R("n3", "Done today", true, 5, completedMinutes: 5), R("n4", "Cleared in Google", true, 5, hidden: true), R("n5", "Gone", deleted: true)]);
        check(plan.Pulls.Count(p => p.Kind == PullKind.Add) == 2 && plan.Pulls.Select(p => p.Remote!.Id).SequenceEqual(["n1", "n3"]), "Sync: new Google tasks are added; finished tasks from before today, cleared (hidden) ones and deleted ones are not imported");
        GoogleTasksSync.Apply(list, Outcome(plan));
        check(list.Tasks.Count == 2 && list.Tasks[0].GoogleId == "n1" && list.Tasks[0].Modified is null && list.Tasks[1].Done && list.Tasks[0].GoogleListId == "@default", "Sync: imported tasks are linked and not marked as changed");
        check(Plan(list, [R("n1", "New one", updatedMinutes: 0, etag: "e0")], incremental: true).Pulls.Count == 0 && Plan(list, [R("n1", "New one", updatedMinutes: 0, etag: "e0")], incremental: true).Pushes.Count == 0, "Sync: an unchanged task causes no work (idempotent)");

        // Local additions are created on Google; the result links them.
        var add = List(L("Typed here", modifiedMinutes: 1));
        var addPlan = Plan(add, []);
        check(addPlan.Pushes.Single().Kind == PushKind.Create && addPlan.Pushes[0].Title == "Typed here", "Sync: a task added here is created on Google");
        GoogleTasksSync.Apply(add, Outcome(addPlan));
        check(add.Tasks[0].GoogleId.StartsWith("new-") && add.Tasks[0].Modified is null && add.Tasks[0].GoogleUpdated == T0.AddMinutes(30), "Sync: the created task is linked and no longer waiting");
        check(Plan(add, []).Pushes.Count == 0, "Sync: it is not created twice");
        var existing = List(L("Was here before sync"));
        check(Plan(existing, []).Pushes.Single().Kind == PushKind.Create, "Sync: tasks that were already on the widget when sync was turned on are added to Google");

        // Ticking, un-ticking and renaming here.
        var local = List(L("Walk", "g1", syncedMinutes: 0, modifiedMinutes: 5));
        local.Tasks[0].Done = true;
        var p = Plan(local, [R("g1", "Walk", updatedMinutes: 0)], incremental: true);
        check(p.Pushes.Single() is { Kind: PushKind.Update, Done: true, GoogleId: "g1" } && p.Pulls.Count == 0, "Sync: ticking a task here sets it completed on Google");
        local.Tasks[0].Done = false;
        local.Tasks[0].Text = "Walk the dog";
        p = Plan(local, [], incremental: true);
        check(p.Pushes.Single() is { Kind: PushKind.Update, Done: false, Title: "Walk the dog" }, "Sync: un-ticking and renaming here are sent even when Google reported nothing new");

        // Google-side changes.
        var clean = List(L("Old title", "g1", syncedMinutes: 0));
        p = Plan(clean, [R("g1", "Renamed on Google", true, 10)]);
        check(p.Pulls.Single().Kind == PullKind.Update && p.Pushes.Count == 0, "Sync: a change made on Google is pulled when nothing changed here");
        GoogleTasksSync.Apply(clean, Outcome(p));
        check(clean.Tasks[0].Text == "Renamed on Google" && clean.Tasks[0].Done && clean.Tasks[0].GoogleUpdated == T0.AddMinutes(10), "Sync: pulled title, ticked state and updated time applied");

        // Conflicts: the later change wins.
        var conflict = List(L("Mine", "g1", syncedMinutes: 0, modifiedMinutes: 20));
        p = Plan(conflict, [R("g1", "Theirs", updatedMinutes: 10)]);
        check(p.Pushes.Single().Kind == PushKind.Update && p.Pulls.Count == 0, "Conflict: a later local change wins and is sent");
        conflict = List(L("Mine", "g1", syncedMinutes: 0, modifiedMinutes: 5));
        p = Plan(conflict, [R("g1", "Theirs", updatedMinutes: 10)]);
        check(p.Pulls.Single().Kind == PullKind.Update && p.Pushes.Count == 0, "Conflict: a later Google change wins and replaces the local edit");
        GoogleTasksSync.Apply(conflict, Outcome(p));
        check(conflict.Tasks[0].Text == "Theirs" && conflict.Tasks[0].Modified is null, "Conflict: the losing local change is dropped");

        // Deletions.
        var gone = List(L("Remove me", "g1", syncedMinutes: 0), L("Keep me", "g2", syncedMinutes: 0));
        p = Plan(gone, [R("g1", "Remove me", deleted: true, updatedMinutes: 10)], incremental: true);
        GoogleTasksSync.Apply(gone, Outcome(p));
        check(gone.Tasks.Count == 1 && gone.Tasks[0].GoogleId == "g2", "Sync: a task deleted on Google is removed here");
        var revive = List(L("Edited after delete", "g1", syncedMinutes: 0, modifiedMinutes: 20));
        p = Plan(revive, [R("g1", "x", deleted: true, updatedMinutes: 10)], incremental: true);
        check(p.Pushes.Single().Kind == PushKind.Create && p.Pulls.Count == 0, "Conflict: a task edited here after Google deleted it is created again");
        var deleted = List(L("Bye", "g1", syncedMinutes: 0));
        GoogleTasksSync.Delete(deleted, deleted.Tasks[0]);
        check(deleted.Tasks.Count == 0 && deleted.GoogleTaskDeletes.Single().TaskId == "g1", "Sync: deleting a linked task here remembers it for Google");
        p = Plan(deleted, [R("g1", "Bye", updatedMinutes: 0)]);
        check(p.Pulls.Count == 0 && p.Pushes.Single() is { Kind: PushKind.Delete, GoogleId: "g1" }, "Sync: a task deleted here is not brought back by the listing, and is deleted on Google");
        GoogleTasksSync.Apply(deleted, Outcome(p));
        check(deleted.GoogleTaskDeletes.Count == 0, "Sync: the delete is forgotten once Google has done it");
        var failedDelete = List();
        failedDelete.GoogleTaskDeletes.Add(new GoogleTaskTombstone { ListId = "@default", TaskId = "g9" });
        p = Plan(failedDelete, []);
        GoogleTasksSync.Apply(failedDelete, Outcome(p, op => new PushResult(op, null, false, "boom")));
        check(failedDelete.GoogleTaskDeletes.Count == 1, "Sync: a delete that failed is kept for the next sync");

        // Absence from a listing.
        var absent = List(L("Clean", "g1", syncedMinutes: 0), L("Edited", "g2", syncedMinutes: 0, modifiedMinutes: 3));
        p = Plan(absent, [], incremental: false, complete: true);
        check(p.Pulls.Single() is { Kind: PullKind.Remove } r1 && r1.LocalId == absent.Tasks[0].Id && p.Pushes.Single().GoogleId == "g2", "Sync: a complete full listing that lacks an unedited task removes it; an edited one is sent instead");
        check(Plan(absent, [], incremental: false, complete: false).Pulls.Count == 0, "Sync: a cut-off listing never removes anything");
        check(Plan(absent, [], incremental: true, complete: true).Pulls.Count == 0, "Sync: an incremental listing (only what changed) never removes anything");
        check(Plan(List(L("Only", "g1", syncedMinutes: 0)), [], incremental: true).Pulls.Count == 0, "Sync: an empty answer deletes nothing");

        // Pushing a task that Google no longer has.
        var lost = List(L("Lost", "g1", syncedMinutes: 0, modifiedMinutes: 5));
        p = Plan(lost, [], incremental: true);
        GoogleTasksSync.Apply(lost, Outcome(p, op => new PushResult(op, null, true, null)));
        check(!lost.Tasks[0].IsLinked && lost.Tasks[0].Modified is not null, "Sync: a push answered 'gone' unlinks the task so the next sync creates it again");

        // Edited again while the request was on its way.
        var racing = List(L("v1", "g1", syncedMinutes: 0, modifiedMinutes: 5));
        p = Plan(racing, [], incremental: true);
        racing.Tasks[0].Text = "v2";
        racing.Tasks[0].Modified = T0.AddMinutes(8);
        GoogleTasksSync.Apply(racing, Outcome(p));
        check(racing.Tasks[0].Modified is not null && racing.Tasks[0].Text == "v2", "Sync: an edit made while the request ran stays waiting");
        var racingPull = List(L("v1", "g1", syncedMinutes: 0));
        p = Plan(racingPull, [R("g1", "from Google", updatedMinutes: 10)]);
        racingPull.Tasks[0].Text = "typed meanwhile";
        racingPull.Tasks[0].Modified = T0.AddMinutes(11);
        GoogleTasksSync.Apply(racingPull, Outcome(p));
        check(racingPull.Tasks[0].Text == "typed meanwhile", "Sync: a pulled change does not overwrite an edit made while the request ran");

        // Watermark.
        p = Plan(List(), [R("a", "a", updatedMinutes: 3), R("b", "b", updatedMinutes: 9), R("c", "c", updatedMinutes: 1)]);
        check(p.Watermark == T0.AddMinutes(9), "Sync: the watermark is the newest 'updated' Google reported, not the local clock");
        var marked = List();
        GoogleTasksSync.Apply(marked, Outcome(p));
        check(marked.GoogleTasksWatermark == T0.AddMinutes(9), "Sync: the watermark is saved");
        var kept = Plan(List(), [], incremental: true);
        check(kept.Watermark == T0, "Sync: with nothing new the watermark stays");

        // Subtasks and the size cap.
        var big = List(Enumerable.Range(0, GoogleTasksSync.MaxTasks).Select(i => L("t" + i)).ToArray());
        check(Plan(big, [R("x", "one more")]).Pulls.Count == 0, "Sync: the list is capped so a huge Google list cannot flood the widget");
    }

    private static void TaskDailyReset(Action<bool, string> check)
    {
        var item = new LauncherItem { Kind = ItemKinds.Tasks, GoogleTasksSync = true, DailyReset = true, LastDate = "2026-10-04", Tasks = [L("Done on Google", "g1", true, 0), L("Open", "g2", false, 0), L("Done local only", "", true)] };
        Planner.RollOver(item, new DateOnly(2026, 10, 5));
        check(item.Tasks.Count == 2 && item.Tasks[0].Hidden && !item.Tasks[1].Hidden && item.Tasks.All(t => t.Text != "Done local only"), "Daily reset: finished synced tasks are only hidden (they stay on Google); other finished tasks are removed; unfinished ones carry over");
        check(Planner.VisibleTasks(item).Count == 1, "Daily reset: hidden tasks are not shown");
        var plan = Plan(item, [R("g1", "Done on Google", true, 0, completedMinutes: -600), R("g2", "Open", updatedMinutes: 0)], since: T0.AddHours(-12));
        check(plan.Pulls.Count == 0 && plan.Pushes.Count == 0 && item.Tasks[0].Hidden, "Daily reset: hidden tasks are not brought back or touched by the next sync");
        var reopened = Plan(item, [R("g1", "Done on Google", false, 40)]);
        GoogleTasksSync.Apply(item, Outcome(reopened));
        check(!item.Tasks[0].Hidden && !item.Tasks[0].Done, "Daily reset: a task reopened on Google comes back");
        item.Tasks[0].Done = true;
        item.Tasks[0].Hidden = true;
        item.Tasks[0].GoogleUpdated = T0.AddDays(-9);
        item.LastDate = "2026-10-05";
        Planner.RollOver(item, new DateOnly(2026, 10, 6));
        check(item.Tasks.All(t => t.GoogleId != "g1"), "Daily reset: a hidden task is forgotten after a week");
        var manual = new LauncherItem { Kind = ItemKinds.Tasks, GoogleTasksSync = true, Tasks = [L("a", "g1", true, 0), L("b", "", true), L("c", "g3", false, 0)] };
        check(Planner.ClearCompleted(manual) == 2 && manual.Tasks.Count == 2 && manual.Tasks[0].Hidden, "Clear completed hides synced tasks and removes the rest");
        var off = new LauncherItem { Kind = ItemKinds.Tasks, Tasks = [L("a", "g1", true, 0)] };
        check(Planner.ClearCompleted(off) == 1 && off.Tasks.Count == 0, "Clear completed removes tasks when sync is off");
    }

    private static void TaskLocalRules(Action<bool, string> check)
    {
        var list = List(L("a", "g1", syncedMinutes: 0), L("local"));
        GoogleTasksSync.Touch(list, list.Tasks[0], T0.AddHours(1));
        check(list.Tasks[0].Modified == T0.AddHours(1), "Touch marks a task as changed here");
        var off = new LauncherItem { Kind = ItemKinds.Tasks, Tasks = [L("plain")] };
        GoogleTasksSync.Touch(off, off.Tasks[0], T0);
        check(off.Tasks[0].Modified is null, "Touch does nothing for a list that never synced");
        check(GoogleTasksSync.PendingCount(list) == 1, "Pending count: one changed task");

        var switched = List(L("From old", "g1", syncedMinutes: 0), L("Edited", "g2", syncedMinutes: 0, modifiedMinutes: 4), L("Local"));
        switched.GoogleTaskDeletes.Add(new GoogleTaskTombstone { ListId = "@default", TaskId = "g7" });
        switched.GoogleTasksWatermark = T0;
        GoogleTasksSync.SwitchList(switched, "L2");
        check(switched.Tasks.Count == 2 && switched.Tasks.All(t => !t.IsLinked) && switched.Tasks.Any(t => t.Text == "Edited") && switched.Tasks.Any(t => t.Text == "Local"), "Switching list drops copies of the old list's tasks and unlinks edited or local ones so they go to the new list");
        check(switched.GoogleTaskDeletes.Count == 0 && switched.GoogleTasksWatermark is null, "Switching list forgets old deletes and the watermark");
        var same = List(L("Same", "g1", syncedMinutes: 0));
        same.Tasks[0].GoogleListId = "L2";
        GoogleTasksSync.SwitchList(same, "L2");
        check(same.Tasks.Count == 1 && same.Tasks[0].IsLinked, "Switching sync on again for the same list keeps its links");

        // A sync that finishes while Customize is open must not be lost when the edited copy replaces the item.
        var live = List(L("Linked meanwhile", "g1", syncedMinutes: 0), L("Another"));
        live.GoogleTasksWatermark = T0;
        live.GoogleTaskDeletes.Add(new GoogleTaskTombstone { ListId = "@default", TaskId = "g8" });
        var draft = new LauncherItem { Id = live.Id, Kind = ItemKinds.Tasks, GoogleTasksSync = true, GoogleTaskListId = "L2", Tasks = [new TaskEntry { Text = "Linked meanwhile" }, new TaskEntry { Text = "Another" }] };
        GoogleTasksSync.CarryOverLiveState(live, draft);
        check(draft.Tasks[0].GoogleId == "g1" && draft.Tasks.Count == 2 && draft.GoogleTaskDeletes.Single().TaskId == "g8" && draft.GoogleTasksWatermark == T0 && draft.GoogleTaskListId == "L2", "Customize: the edited copy takes the live tasks, deletes and watermark but keeps the new settings");
        draft.Tasks[0].GoogleId = "x";
        check(live.Tasks[0].GoogleId == "g1", "Customize: the carried-over tasks are copies");

        var unlink = List(L("a", "g1", syncedMinutes: 0, modifiedMinutes: 2), L("hidden", "g2", true, 0, hidden: true));
        unlink.GoogleTaskDeletes.Add(new GoogleTaskTombstone { ListId = "@default", TaskId = "g3" });
        GoogleTasksSync.Unlink(unlink);
        check(!unlink.GoogleTasksSync && unlink.Tasks.Count == 1 && !unlink.Tasks[0].IsLinked && unlink.Tasks[0].Modified is null && unlink.GoogleTaskDeletes.Count == 0, "Disconnecting unlinks every task and turns sync off, so another account never receives changes aimed at the old one");
    }

    // ---------- The background feed (against a fake Google Tasks) ----------

    private sealed class FakeTasksApi : IGoogleTasksApi
    {
        public List<GoogleTask> Remote = [];
        public bool Complete = true, Offline;
        public readonly List<string> Calls = [];
        public Task<(List<GoogleTask> Tasks, bool Complete)> ListTasksAsync(string listId, DateTimeOffset? updatedMin, CancellationToken cancel)
        {
            Calls.Add("list " + (updatedMin is null ? "full" : "since"));
            if (Offline) throw new GoogleApiException("Could not reach Google Tasks. Check your internet connection.", GoogleApiProblem.Network);
            return Task.FromResult((Remote.ToList(), Complete));
        }
        public Task<GoogleTask> CreateTaskAsync(string listId, string title, bool completed, CancellationToken cancel)
        {
            Calls.Add("create " + title);
            if (Offline) throw new GoogleApiException("offline", GoogleApiProblem.Network);
            var task = new GoogleTask { Id = "srv" + Calls.Count, Title = title, Completed = completed, Updated = T0.AddMinutes(60), ETag = "srv" };
            Remote.Add(task);
            return Task.FromResult(task);
        }
        public Task<GoogleTask> UpdateTaskAsync(string listId, string taskId, string title, bool completed, CancellationToken cancel)
        {
            Calls.Add("update " + taskId + (completed ? " done" : " open"));
            if (Remote.All(t => t.Id != taskId)) throw new GoogleApiException("gone", GoogleApiProblem.NotFound, 404);
            return Task.FromResult(new GoogleTask { Id = taskId, Title = title, Completed = completed, Updated = T0.AddMinutes(61), ETag = "srv2" });
        }
        public Task DeleteTaskAsync(string listId, string taskId, CancellationToken cancel) { Calls.Add("delete " + taskId); Remote.RemoveAll(t => t.Id == taskId); return Task.CompletedTask; }
    }

    private static async Task TaskFeed(Action<bool, string> check)
    {
        var clock = T0;
        var api = new FakeTasksApi { Remote = [R("g1", "From Google", updatedMinutes: 1)] };
        bool ready = true;
        var feed = new GoogleTasksFeed(api, () => ready, () => clock);
        var item = List(L("Typed here", modifiedMinutes: 2));
        item.Id = "tasks-1";

        check(feed.TryBegin(item.Id, false), "Feed: the first sync starts");
        check(!feed.TryBegin(item.Id, true) && feed.StatusOf(item.Id).Syncing, "Feed: only one sync of a list runs at a time");
        var outcome = await feed.RunAsync(item.Id, TaskSyncSnapshot.From(item, clock));
        check(outcome is not null && GoogleTasksSync.Apply(item, outcome) && item.Tasks.Count == 2 && item.Tasks.All(t => t.IsLinked), "Feed: a sync adds Google's tasks and creates ours");
        check(api.Calls.SequenceEqual(["list full", "create Typed here"]) && feed.StatusOf(item.Id) is { Syncing: false, Error: null, LastSynced: not null }, "Feed: it listed, then created the new task, and reports success");
        check(!feed.TryBegin(item.Id, false), "Feed: no new sync within 90 seconds");
        check(feed.TryBegin(item.Id, true), "Feed: the refresh button syncs immediately");
        await feed.RunAsync(item.Id, TaskSyncSnapshot.From(item, clock));
        clock = clock.AddSeconds(91);
        check(feed.TryBegin(item.Id, false), "Feed: after 90 seconds the next sync is due");

        // Offline: nothing is lost, and the note says so.
        api.Offline = true;
        item.Tasks[0].Text = "Edited offline";
        item.Tasks[0].Modified = clock;
        var before = item.Tasks.Select(t => (t.Text, t.GoogleId)).ToList();
        var failed = await feed.RunAsync(item.Id, TaskSyncSnapshot.From(item, clock));
        check(failed is null && item.Tasks.Select(t => (t.Text, t.GoogleId)).SequenceEqual(before) && item.Tasks[0].Modified is not null, "Offline: a failed fetch changes nothing and keeps the waiting edit");
        var status = feed.StatusOf(item.Id);
        check(status.Error is not null && status.LastSynced is not null && status.Note(clock, System.Globalization.CultureInfo.InvariantCulture)!.StartsWith("Offline — synced ") && !status.NeedsAction, "Offline: the note says 'Offline — synced <time>'");
        api.Offline = false;
        clock = clock.AddMinutes(5);
        check(feed.TryBegin(item.Id, false), "Offline: it tries again later by itself");
        var recovered = await feed.RunAsync(item.Id, TaskSyncSnapshot.From(item, clock));
        check(recovered is not null && GoogleTasksSync.Apply(item, recovered) && item.Tasks[0].Modified is null && feed.StatusOf(item.Id).Error is null, "Offline: after reconnecting the edit is sent and the note disappears");

        // A push that fails offline mid-way keeps the rest waiting.
        var half = List(L("one", modifiedMinutes: 1), L("two", modifiedMinutes: 2));
        half.Id = "tasks-2";
        api.Remote.Clear();
        feed.TryBegin(half.Id, true);
        var partial = await new GoogleTasksFeed(new FailSecondCreate(), () => true, () => clock).RunAsync(half.Id, TaskSyncSnapshot.From(half, clock));
        check(partial is not null && partial.Pushes.Count == 1, "Offline: if the connection drops during the sends, what was sent is kept and the rest waits");

        // Not connected.
        ready = false;
        check(!feed.TryBegin(item.Id, true), "Feed: nothing syncs without a connected account");
        ready = true;
        feed.Inert = true;
        check(!feed.TryBegin(item.Id, true), "Feed: an inert feed (smoke tests) never syncs");

        // Edited task that Google lost.
        var api2 = new FakeTasksApi();
        var feed2 = new GoogleTasksFeed(api2, () => true, () => clock);
        var lost = List(L("Lost", "zzz", syncedMinutes: 0, modifiedMinutes: 3));
        lost.Id = "tasks-3";
        lost.GoogleTasksWatermark = T0;
        feed2.TryBegin(lost.Id, true);
        var result = await feed2.RunAsync(lost.Id, TaskSyncSnapshot.From(lost, clock));
        GoogleTasksSync.Apply(lost, result!);
        check(!lost.Tasks[0].IsLinked && lost.Tasks[0].Modified is not null && feed2.StatusOf(lost.Id).Error is null, "Feed: updating a task Google deleted (404) unlinks it for re-creation without an error");
    }

    private sealed class FailSecondCreate : IGoogleTasksApi
    {
        private int creates;
        public Task<(List<GoogleTask> Tasks, bool Complete)> ListTasksAsync(string listId, DateTimeOffset? updatedMin, CancellationToken cancel) => Task.FromResult((new List<GoogleTask>(), true));
        public Task<GoogleTask> CreateTaskAsync(string listId, string title, bool completed, CancellationToken cancel)
        {
            if (++creates == 2) throw new GoogleApiException("offline", GoogleApiProblem.Network);
            return Task.FromResult(new GoogleTask { Id = "x" + creates, Title = title, Updated = T0, ETag = "e" });
        }
        public Task<GoogleTask> UpdateTaskAsync(string listId, string taskId, string title, bool completed, CancellationToken cancel) => throw new NotSupportedException();
        public Task DeleteTaskAsync(string listId, string taskId, CancellationToken cancel) => throw new NotSupportedException();
    }

    private static void TaskStorage(Action<bool, string> check, string folder)
    {
        var store = new SettingsStore(Path.Combine(folder, "google-tasks"));
        var item = new LauncherItem
        {
            Kind = ItemKinds.Tasks, Name = "Today", GoogleTasksSync = true, GoogleTaskListId = "L2", GoogleTaskListName = "Groceries", GoogleTasksWatermark = T0,
            Tasks = [new TaskEntry { Text = "Milk", Done = true, GoogleId = "g1", GoogleListId = "L2", GoogleEtag = "\"e\"", GoogleUpdated = T0.AddMinutes(-4), Modified = T0.AddMinutes(-1), Hidden = true }, new TaskEntry { Text = "Local" }],
            GoogleTaskDeletes = [new GoogleTaskTombstone { ListId = "L2", TaskId = "g5" }],
        };
        store.Save(new DeckSettings { Items = [item], GoogleTaskLists = [new GoogleTaskListChoice { Id = "L2", Title = "Groceries" }] });
        var back = store.Load();
        var t = back.Items[0];
        check(t.GoogleTasksSync && t.GoogleTaskListId == "L2" && t.GoogleTaskListName == "Groceries" && t.GoogleTasksWatermark == T0, "Storage: sync settings round-trip");
        var task = t.Tasks[0];
        check(task.GoogleId == "g1" && task.GoogleListId == "L2" && task.GoogleEtag == "\"e\"" && task.GoogleUpdated == T0.AddMinutes(-4) && task.Modified == T0.AddMinutes(-1) && task.Hidden && task.Done && t.Tasks[1].GoogleId == "" && t.Tasks[1].Modified is null, "Storage: a task's Google id, list, etag, updated time, changed time and hidden flag round-trip");
        check(t.GoogleTaskDeletes.Single().TaskId == "g5" && back.GoogleTaskLists.Single().Title == "Groceries", "Storage: pending deletes and the cached task lists round-trip");
        var copy = t.Copy();
        copy.Tasks[0].GoogleId = "changed";
        copy.GoogleTaskDeletes[0].TaskId = "changed";
        check(t.Tasks[0].GoogleId == "g1" && t.GoogleTaskDeletes[0].TaskId == "g5" && t.Tasks[0].Modified == task.Modified, "Storage: copying an item (the Customize draft) keeps the Google fields and is deep");
        check(new TaskEntry { GoogleId = "x", Modified = T0 }.Copy() is { GoogleId: "x", Modified: not null }, "Storage: TaskEntry.Copy keeps every field");

        File.WriteAllText(store.FilePath, """{ "Items": [ { "Id": "q1", "Kind": "tasks", "Name": "T", "Target": "", "Arguments": "", "WorkingDirectory": "", "Accent": "#fff", "X": 0, "Y": 0, "GoogleTaskListId": " ", "GoogleTaskListName": null, "GoogleTaskDeletes": [ null, { "TaskId": "" }, { "TaskId": "z" } ], "Tasks": [ { "Text": "Odd", "GoogleId": null, "GoogleEtag": null, "GoogleListId": null, "Hidden": true, "Modified": null }, { "Text": "Open and hidden", "GoogleId": "g", "Hidden": true } ] } ], "GoogleTaskLists": [ null, { "Id": "" }, { "Id": "a", "Title": null }, { "Id": "a", "Title": "dup" } ] }""");
        var odd = store.Load();
        var o = odd.Items[0];
        check(o.GoogleTaskListId == "@default" && o.GoogleTaskListName == "My Tasks" && !o.GoogleTasksSync, "Storage: missing sync settings get defaults (an old settings file loads unchanged)");
        check(o.Tasks[0].GoogleId == "" && !o.Tasks[0].Hidden && !o.Tasks[1].Hidden && o.GoogleTaskDeletes.Count == 1 && o.GoogleTaskDeletes[0].ListId == "@default", "Storage: null and inconsistent Google fields are repaired on load");
        check(odd.GoogleTaskLists.Count == 1 && odd.GoogleTaskLists[0].Title == "a", "Storage: bad task list entries are dropped, duplicates collapse, a missing title uses the id");
    }

    // ---------- Error wording ----------

    private static void ApiErrors(Action<bool, string> check)
    {
        static string Body(string reason, string message, string status = "PERMISSION_DENIED") => "{\"error\":{\"code\":403,\"message\":\"" + message + "\",\"status\":\"" + status + "\",\"errors\":[{\"reason\":\"" + reason + "\"}]}}";
        var tasks = GoogleTasksService.Info;
        var scope = GoogleApiClient.Describe(tasks, HttpStatusCode.Forbidden, Body("insufficientPermissions", "Request had insufficient authentication scopes.")) as GoogleApiException;
        check(scope is { Problem: GoogleApiProblem.ScopeMissing } && scope.Message.Contains("Enable") && scope.NeedsUserAction, "Errors: a missing permission says to press Enable");
        var newStyle = GoogleApiClient.Describe(tasks, HttpStatusCode.Forbidden, "{\"error\":{\"message\":\"x\",\"details\":[{\"@type\":\"type.googleapis.com/google.rpc.ErrorInfo\",\"reason\":\"ACCESS_TOKEN_SCOPE_INSUFFICIENT\"}]}}") as GoogleApiException;
        check(newStyle?.Problem == GoogleApiProblem.ScopeMissing, "Errors: the newer error format (details/reason) is understood too");
        var disabled = GoogleApiClient.Describe(GmailService.Info, HttpStatusCode.Forbidden, Body("accessNotConfigured", "Gmail API has not been used in project 123 before or it is disabled.")) as GoogleApiException;
        check(disabled is { Problem: GoogleApiProblem.ApiDisabled } && disabled.Message.Contains("Gmail API") && disabled.Message.Contains("Google Cloud console"), "Errors: an API that is off in the Cloud project names the API and where to enable it");
        check(GoogleApiClient.Describe(tasks, HttpStatusCode.Unauthorized, "{}") is GoogleAuthException { NeedsReconnect: true }, "Errors: 401 means connect again");
        check(GoogleApiClient.Describe(tasks, HttpStatusCode.TooManyRequests, "{}") is GoogleApiException { Problem: GoogleApiProblem.RateLimited }, "Errors: 429 is rate limiting");
        var blocked = GoogleApiClient.Describe(ClassroomService.Info, HttpStatusCode.Forbidden, Body("forbidden", "The caller does not have permission")) as GoogleApiException;
        check(blocked is { Problem: GoogleApiProblem.Blocked } && blocked.Message == "Your school blocks this app — ask your admin, or use the iCal link of your Classroom calendar instead.", "Errors: a Classroom 403 says the school blocks the app and suggests the iCal link");
        check(GoogleApiClient.Describe(GmailService.Info, HttpStatusCode.Forbidden, Body("forbidden", "Nope")) is GoogleApiException { Problem: GoogleApiProblem.Other } other && other.Message.StartsWith("Gmail refused"), "Errors: other Gmail refusals are plain refusals, not 'school blocks'");
        check(GoogleApiClient.Describe(tasks, HttpStatusCode.NotFound, "") is GoogleApiException { Problem: GoogleApiProblem.NotFound }, "Errors: 404 is not found");
        check(!GoogleApiClient.Describe(tasks, HttpStatusCode.InternalServerError, "<html>secret-token-abc</html>").Message.Contains("secret"), "Errors: an unreadable answer body is never copied into the message");
        try { GoogleAuth.ParseTokenResponse("""{"error":"admin_policy_enforced"}"""); check(false, "Errors: expected a sign-in failure"); }
        catch (GoogleAuthException ex) { check(ex.Message.Contains("school or organization blocks"), "Errors: admin_policy_enforced at sign-in says the school or organization blocks the app"); }
        var feature = GoogleFeatures.All;
        check(feature.Length == 4 && GoogleFeatures.Gmail.Scopes.SequenceEqual([GoogleScopes.GmailReadOnly]) && GoogleFeatures.Classroom.Scopes.Length == 3 && GoogleFeatures.Tasks.Scopes[0] == "https://www.googleapis.com/auth/tasks", "Features: Tasks, Gmail (read-only scope only) and Classroom (three read-only scopes) are defined");
        check(GoogleScopes.ClassroomCourseWork == "https://www.googleapis.com/auth/classroom.coursework.me.readonly" && GoogleScopes.ClassroomSubmissions == "https://www.googleapis.com/auth/classroom.student-submissions.me.readonly" && GoogleScopes.ClassroomCourses == "https://www.googleapis.com/auth/classroom.courses.readonly", "Features: Classroom scope names");
    }

    // ---------- Gmail ----------

    private static string GmailJson(string id, string from, string subject, string snippet, string? internalDate, string date, params string[] labels)
    {
        string headers = JsonSerializer.Serialize(new object[] { new { name = "From", value = from }, new { name = "subject", value = subject }, new { name = "Date", value = date }, new { name = "X-Other", value = "ignored" } });
        return "{\"id\":\"" + id + "\",\"threadId\":\"th" + id + "\",\"labelIds\":" + JsonSerializer.Serialize(labels) + ",\"snippet\":" + JsonSerializer.Serialize(snippet) + (internalDate is null ? "" : ",\"internalDate\":\"" + internalDate + "\"") + ",\"payload\":{\"headers\":" + headers + "}}";
    }

    private static void GmailParsing(Action<bool, string> check)
    {
        long ms = new DateTimeOffset(2026, 10, 5, 15, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var m = GmailService.ParseMessageJson(GmailJson("m1", "\"Ada Lovelace\" <ada@example.com>", "Hello   there", "It&#39;s &quot;fine&quot; &amp; done", ms.ToString(), "Mon, 5 Oct 2026 09:00:00 -0500", "INBOX", "UNREAD"))!;
        check(m.Id == "m1" && m.ThreadId == "thm1" && m.SenderName == "Ada Lovelace" && m.SenderEmail == "ada@example.com" && m.Subject == "Hello there" && m.Unread, "Gmail: sender, subject (spaces squashed), thread id and unread read from metadata");
        check(m.Snippet == "It's \"fine\" & done" && m.Date == new DateTimeOffset(2026, 10, 5, 15, 30, 0, TimeSpan.Zero), "Gmail: the snippet's HTML entities are decoded and the arrival time comes from internalDate");
        check(!GmailService.ParseMessageJson(GmailJson("m2", "a@b.c", "s", "", null, "Mon, 5 Oct 2026 09:00:00 -0500 (CDT)", "INBOX"))!.Unread, "Gmail: no UNREAD label means read");
        check(GmailService.ParseMessageJson(GmailJson("m2", "a@b.c", "s", "", null, "Mon, 5 Oct 2026 09:00:00 -0500 (CDT)", "INBOX"))!.Date == new DateTimeOffset(2026, 10, 5, 14, 0, 0, TimeSpan.Zero), "Gmail: with no internalDate the Date header is used, even with a '(CDT)' comment");
        check(GmailService.ParseMessageJson(GmailJson("m3", "a@b.c", "s", "", "oops", "not a date"))!.Date is null, "Gmail: unreadable dates give no date instead of failing");
        check(GmailService.ParseMessageJson("{}") is null && GmailService.ParseMessageJson("""{"id":"x"}""")!.Subject == "", "Gmail: a message without headers still parses; one without an id is skipped");

        // Encoded headers.
        check(GmailService.DecodeHeader("=?UTF-8?B?SGVsbG8gV29ybGQ=?=") == "Hello World", "Headers: base64 encoded word");
        check(GmailService.DecodeHeader("=?utf-8?B?8J+TmiBCb29r?=") == "📚 Book", "Headers: base64 UTF-8 with an emoji");
        check(GmailService.DecodeHeader("=?ISO-8859-1?Q?Caf=E9_au_lait?=") == "Café au lait", "Headers: quoted-printable ISO-8859-1 with '_' as a space");
        check(GmailService.DecodeHeader("=?UTF-8?Q?Gr=C3=BC=C3=9Fe?= von Anna") == "Grüße von Anna", "Headers: quoted-printable UTF-8 followed by plain text");
        check(GmailService.DecodeHeader("=?UTF-8?B?SGVsbG8s?=  =?UTF-8?B?IFdvcmxk?=") == "Hello, World", "Headers: whitespace between two encoded words is dropped");
        check(GmailService.DecodeHeader("Re: =?UTF-8?B?w6k=?= ok") == "Re: é ok", "Headers: plain text around an encoded word keeps its spaces");
        check(GmailService.DecodeHeader("=?UTF-8?B?4pyT?= =?UTF-8?B?IERvbmU=?=") == "✓ Done", "Headers: two words of one charset are joined before decoding");
        check(GmailService.DecodeHeader("=?UTF-8?B?ww==?= =?UTF-8?B?qQ==?=") == "é", "Headers: a character split across two encoded words is rejoined");
        check(GmailService.DecodeHeader("=?windows-1252?Q?Ren=E9e_=96_Test?=") == "Renée – Test", "Headers: windows-1252 works");
        check(GmailService.DecodeHeader("=?UTF-8?X?abc?= and =?UTF-8?B?***?=") == "=?UTF-8?X?abc?= and =?UTF-8?B?***?=", "Headers: broken encoded words are left as typed");
        check(GmailService.DecodeHeader("=?made-up-charset?B?SGk=?=") == "Hi", "Headers: an unknown charset falls back to UTF-8");
        check(GmailService.DecodeHeader(null) == "" && GmailService.DecodeHeader("plain") == "plain", "Headers: plain and empty text pass through");
        var encoded = GmailService.ParseMessageJson(GmailJson("m4", "=?UTF-8?B?SsO8cmdlbiBNw7xsbGVy?= <j.mueller@example.de>", "=?UTF-8?B?QWJyZWNobnVuZyDigJMgT2t0b2Jlcg==?=", "", ms.ToString(), ""))!;
        check(encoded.SenderName == "Jürgen Müller" && encoded.SenderEmail == "j.mueller@example.de" && encoded.Subject == "Abrechnung – Oktober", "Gmail: an encoded From name and subject are decoded in a message");

        // From headers.
        check(GmailService.ParseFrom("ada@example.com") == ("ada@example.com", "ada@example.com"), "From: a bare address is its own name");
        check(GmailService.ParseFrom("<ada@example.com>") == ("ada@example.com", "ada@example.com"), "From: an address in angle brackets only");
        check(GmailService.ParseFrom("Ada <ada@example.com>") == ("Ada", "ada@example.com"), "From: name and address");
        check(GmailService.ParseFrom("\"Lovelace, Ada\" <ada@example.com>") == ("Lovelace, Ada", "ada@example.com"), "From: a quoted name with a comma");
        check(GmailService.ParseFrom("\"=?UTF-8?B?w4lsw6g=?=\" <e@example.com>") == ("Élè", "e@example.com"), "From: an encoded word inside quotes");
        check(GmailService.ParseFrom("") == ("(unknown sender)", ""), "From: an empty header");
        check(GmailService.ParseFrom("\"ada@example.com\" <ada@example.com>").Name == "ada@example.com", "From: a name equal to the address");

        // Lists, labels and URLs.
        var (refs, estimate) = GmailService.ParseMessageList("""{"messages":[{"id":"a","threadId":"ta"},{"threadId":"x"},{"id":"b","threadId":"tb"}],"resultSizeEstimate":57}""");
        check(refs.Count == 2 && refs[0] == ("a", "ta") && estimate == 57, "Gmail: the message list and Gmail's size estimate are read");
        check(GmailService.ParseMessageList("""{"resultSizeEstimate":0}""").Messages.Count == 0, "Gmail: an empty search has no messages key");
        check(GmailService.ParseLabelUnread("""{"id":"INBOX","messagesUnread":23}""") == 23 && GmailService.ParseLabelUnread("{}") is null, "Gmail: the unread count of a label");
        var labels = GmailService.ParseLabels("""{"labels":[{"id":"INBOX","name":"INBOX"},{"id":"Label_1","name":"Work"}]}""");
        check(labels.Count == 2 && labels[1] == new GmailLabel("Label_1", "Work"), "Gmail: labels read");
        string url = GmailService.MessagesUrl("in:inbox is:unread label:\"a b\"", 6);
        check(url.StartsWith("https://gmail.googleapis.com/gmail/v1/users/me/messages?maxResults=6&q=in%3Ainbox%20is%3Aunread%20label%3A%22a%20b%22"), "Gmail: the search is URL-escaped");
        check(GmailService.MessagesUrl("", 99).Contains("maxResults=25") && !GmailService.MessagesUrl(" ", 1).Contains("q="), "Gmail: the count is capped at 25 and an empty search sends no q");
        string metadata = GmailService.MessageUrl("id/1");
        check(metadata.Contains("/messages/id%2F1?format=metadata") && metadata.Split("metadataHeaders=").Length == 4 && metadata.Contains("metadataHeaders=From") && metadata.Contains("metadataHeaders=Subject") && metadata.Contains("metadataHeaders=Date") && !metadata.Contains("format=full"), "Gmail: metadata only, one metadataHeaders parameter per header");
        check(GmailService.QueryHasUnread("in:inbox is:unread") && GmailService.QueryHasUnread("(is:UNREAD)") && !GmailService.QueryHasUnread("label:work") && !GmailService.QueryHasUnread("is:unreadish"), "Gmail: finding is:unread in a search");
    }

    // ---------- A fake Google that records what was asked ----------

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public readonly List<(string Method, string Url, string Auth)> Requests = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Requests) Requests.Add((request.Method.Method, request.RequestUri!.ToString(), request.Headers.Authorization?.ToString() ?? ""));
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Ok(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static GoogleAuth SignedIn(string folder, string name, RecordingHandler handler)
    {
        var secrets = new SecretStore(Path.Combine(folder, name));
        secrets.Set(GoogleAuth.ClientIdEntry, "id.apps.googleusercontent.com");
        secrets.Set(GoogleAuth.ClientSecretEntry, "secret");
        secrets.Set(GoogleAuth.RefreshEntry, "refresh");
        return new GoogleAuth(secrets, new HttpClient(handler), _ => throw new Exception("browser must not open"));
    }

    private static HttpResponseMessage Route(HttpRequestMessage request, Func<Uri, HttpResponseMessage> api) =>
        request.RequestUri!.Host == "oauth2.googleapis.com" ? Ok("""{"access_token":"tok","expires_in":3600}""") : api(request.RequestUri!);

    private static async Task GmailNetwork(Action<bool, string> check, string folder)
    {
        long ms = new DateTimeOffset(2026, 10, 5, 15, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var handler = new RecordingHandler(request => Route(request, uri => uri.AbsolutePath switch
        {
            "/gmail/v1/users/me/messages" => Ok("""{"messages":[{"id":"a","threadId":"ta"},{"id":"gone","threadId":"tg"},{"id":"b","threadId":"tb"}],"resultSizeEstimate":2}"""),
            "/gmail/v1/users/me/messages/a" => Ok(GmailJson("a", "A <a@x.y>", "First", "s", ms.ToString(), "", "INBOX", "UNREAD", "Label_1")),
            "/gmail/v1/users/me/messages/b" => Ok(GmailJson("b", "B <b@x.y>", "Second", "s", (ms - 1000).ToString(), "", "INBOX")),
            "/gmail/v1/users/me/messages/gone" => Ok("""{"error":{"code":404,"message":"Requested entity was not found."}}""", HttpStatusCode.NotFound),
            "/gmail/v1/users/me/labels/INBOX" => Ok("""{"id":"INBOX","messagesUnread":41}"""),
            "/gmail/v1/users/me/labels" => Ok("""{"labels":[{"id":"Label_1","name":"Work"}]}"""),
            _ => Ok("{}", HttpStatusCode.NotFound),
        }));
        var gmail = new GmailService(SignedIn(folder, "gmail", handler), new HttpClient(handler));
        var fetch = await gmail.FetchAsync("in:inbox is:unread", 5, GmailBadge.Inbox, wantLabels: true);
        check(fetch.Messages.Select(m => m.Id).SequenceEqual(["a", "b"]) && fetch.Unread == 41 && fetch.Labels.Single().Name == "Work", "Gmail fetch: messages in order (one deleted meanwhile is skipped), the inbox unread count and the label names");
        var apiCalls = handler.Requests.Where(r => r.Url.Contains("gmail.googleapis.com")).ToList();
        check(apiCalls.All(r => r.Method == "GET") && handler.Requests.All(r => r.Method == "GET" || r.Url.StartsWith("https://oauth2.googleapis.com/token")), "Gmail fetch: read-only: every Gmail request is a GET");
        check(apiCalls.All(r => !r.Url.Contains("format=full") && !r.Url.Contains("/attachments") && !r.Url.Contains("/send") && !r.Url.Contains("/modify") && !r.Url.Contains("/trash")), "Gmail fetch: never asks for bodies or attachments and never touches send, modify or trash");
        check(apiCalls.All(r => r.Auth == "Bearer tok"), "Gmail fetch: the access token goes to Google only");
        var queryBadge = new RecordingHandler(request => Route(request, uri => uri.AbsolutePath.EndsWith("/messages") ? Ok("""{"messages":[],"resultSizeEstimate":7}""") : Ok("{}")));
        var empty = await new GmailService(SignedIn(folder, "gmail2", queryBadge), new HttpClient(queryBadge)).FetchAsync("label:work", 3, GmailBadge.Query, false);
        check(empty.Messages.Count == 0 && empty.Unread == 7 && queryBadge.Requests.Any(r => r.Url.Contains("is%3Aunread")), "Gmail fetch: the badge can count unread within the search (an extra search with is:unread)");

        var denied = new RecordingHandler(request => Route(request, _ => Ok("""{"error":{"code":403,"message":"Request had insufficient authentication scopes.","status":"PERMISSION_DENIED","errors":[{"reason":"insufficientPermissions"}]}}""", HttpStatusCode.Forbidden)));
        try { await new GmailService(SignedIn(folder, "gmail3", denied), new HttpClient(denied)).FetchAsync("x", 1, GmailBadge.None, false); check(false, "Gmail fetch: expected a failure"); }
        catch (GoogleApiException ex) { check(ex.Problem == GoogleApiProblem.ScopeMissing, "Gmail fetch: a missing permission surfaces as 'Enable'"); }
    }

    // ---------- Classroom ----------

    private static void ClassroomParsing(Action<bool, string> check)
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("test-minus5", TimeSpan.FromHours(-5), "UTC-5", "UTC-5");
        var course = new ClassroomCourse { Id = "c1", Name = "Chemistry" };
        using var timed = JsonDocument.Parse("""{"dueDate":{"year":2026,"month":10,"day":6},"dueTime":{"hours":4,"minutes":59}}""");
        check(ClassroomService.ToLocalDue(timed.RootElement, zone, out bool hasTime) == new DateTime(2026, 10, 5, 23, 59, 0) && hasTime, "Classroom: a UTC due date and time become local time (04:59 UTC on Oct 6 is 23:59 on Oct 5 at UTC-5)");
        using var noMinutes = JsonDocument.Parse("""{"dueDate":{"year":2026,"month":10,"day":6},"dueTime":{"hours":17}}""");
        check(ClassroomService.ToLocalDue(noMinutes.RootElement, zone, out _) == new DateTime(2026, 10, 6, 12, 0, 0), "Classroom: a missing 'minutes' is 0 (Google leaves zero fields out)");
        using var midnight = JsonDocument.Parse("""{"dueDate":{"year":2026,"month":10,"day":6},"dueTime":{}}""");
        check(ClassroomService.ToLocalDue(midnight.RootElement, zone, out hasTime) == new DateTime(2026, 10, 5, 19, 0, 0) && hasTime, "Classroom: an empty dueTime is 00:00 UTC");
        using var dateOnly = JsonDocument.Parse("""{"dueDate":{"year":2026,"month":12,"day":31}}""");
        check(ClassroomService.ToLocalDue(dateOnly.RootElement, zone, out hasTime) == new DateTime(2026, 12, 31) && !hasTime, "Classroom: a due date without a time is that day, with no time");
        using var none = JsonDocument.Parse("""{"title":"x"}"""); using var bad = JsonDocument.Parse("""{"dueDate":{"year":2026,"month":2,"day":30}}""");
        check(ClassroomService.ToLocalDue(none.RootElement, zone, out _) is null && ClassroomService.ToLocalDue(bad.RootElement, zone, out _) is null, "Classroom: no or impossible due date gives null");
        var dst = TimeZoneInfo.TryFindSystemTimeZoneById("Pacific Standard Time", out var pacific) ? pacific : TimeZoneInfo.Utc;
        using var summer = JsonDocument.Parse("""{"dueDate":{"year":2026,"month":7,"day":2},"dueTime":{"hours":6,"minutes":59}}""");
        using var winter = JsonDocument.Parse("""{"dueDate":{"year":2026,"month":12,"day":2},"dueTime":{"hours":7,"minutes":59}}""");
        check(dst == TimeZoneInfo.Utc || (ClassroomService.ToLocalDue(summer.RootElement, dst, out _) == new DateTime(2026, 7, 1, 23, 59, 0) && ClassroomService.ToLocalDue(winter.RootElement, dst, out _) == new DateTime(2026, 12, 1, 23, 59, 0)), "Classroom: daylight saving is applied (23:59 Pacific in July and in December)");

        var work = ClassroomService.ParseCourseWork("""
            { "nextPageToken": "n2", "courseWork": [
              { "id": "w1", "title": " Lab report ", "description": "Two  pages\nplease", "alternateLink": "https://classroom.google.com/c/1/a/2", "maxPoints": 50, "workType": "ASSIGNMENT", "state": "PUBLISHED", "dueDate": {"year":2026,"month":10,"day":6}, "dueTime": {"hours":4,"minutes":59} },
              { "id": "w2", "title": "Reading", "workType": "MATERIAL", "state": "PUBLISHED" },
              { "id": "w3", "title": "Draft", "workType": "ASSIGNMENT", "state": "DRAFT" },
              { "id": "w4", "workType": "SHORT_ANSWER_QUESTION" },
              { "title": "no id" } ] }
            """, course, zone, out string? next);
        check(next == "n2" && work.Count == 2 && work[0].Id == "w1" && work[1].Id == "w4", "Classroom: materials, drafts and items without an id are skipped; the page token is read");
        check(work[0].Title == "Lab report" && work[0].Description == "Two pages please" && work[0].MaxPoints == 50 && work[0].CourseName == "Chemistry" && work[0].Due == new DateTime(2026, 10, 5, 23, 59, 0) && work[0].HasDueTime && work[0].Link.StartsWith("https://classroom.google.com/"), "Classroom: assignment fields and local due time read");
        check(work[1].Title == "(Untitled)" && work[1].Due is null && !work[1].HasDueTime, "Classroom: an untitled question without a due date");

        var subs = ClassroomService.ParseSubmissions("""{"studentSubmissions":[{"courseWorkId":"w1","state":"TURNED_IN","late":true},{"courseWorkId":"w2","state":"NEW"},{"courseWorkId":"w3","state":"RETURNED"},{"courseWorkId":"w4","state":"RECLAIMED_BY_STUDENT"},{"courseWorkId":"w5","state":"???"},{"state":"NEW"}]}""", out _);
        check(subs.Count == 5 && subs[0].Value == (SubmissionState.TurnedIn, true) && subs[1].Value.State == SubmissionState.Open && subs[2].Value.State == SubmissionState.Returned && subs[3].Value.State == SubmissionState.Open && subs[4].Value.State == SubmissionState.Unknown, "Classroom: submission states (turned in, late, new, returned, reclaimed)");
        var states = subs.ToDictionary(s => s.WorkId, s => s.Value);
        var applied = ClassroomService.ApplySubmissions(work, states);
        check(applied[0].State == SubmissionState.TurnedIn && applied[0].Late && applied[1].State == SubmissionState.Open, "Classroom: submission states are matched to assignments by id");

        var courses = ClassroomService.ParseCourses("""{"courses":[{"id":"1","name":"Math","section":"P2","courseState":"ACTIVE","alternateLink":"https://classroom.google.com/c/1"},{"id":"2","name":"Old","courseState":"ARCHIVED"},{"name":"x"},{"id":"3","name":""}]}""", out _);
        check(courses.Select(c => c.Id).SequenceEqual(["1", "3"]) && courses[0].Section == "P2" && courses[1].Name == "Course", "Classroom: only active courses with an id; blank names get a name");
        var math = new ClassroomCourse { Id = "1", Name = "AP Math", Section = "Period 2" };
        check(ClassroomService.CourseMatches(math, []) && ClassroomService.CourseMatches(math, ["math"]) && ClassroomService.CourseMatches(math, ["period 2"]) && ClassroomService.CourseMatches(math, ["1"]) && !ClassroomService.CourseMatches(math, ["chem", " "]), "Classroom: course filter lines match by name, section or id, any letter case; no lines means all");

        // Import dedupe.
        var w = (string id) => new ClassroomWork { Id = id, Title = id };
        var plan = ClassroomService.PlanImport([w("a"), w("b"), w("a"), w("c"), w("d"), w("")], new HashSet<string> { "b" }, 2);
        check(plan.ToAdd.Select(x => x.Id).SequenceEqual(["a", "c"]) && plan.Duplicates == 2 && plan.NoRoom == 1, "Import: ids already imported (or repeated) are skipped, and only as many as fit are added");
        check(ClassroomService.PlanImport([w("a")], new HashSet<string>(), 0).NoRoom == 1 && ClassroomService.PlanImport([], new HashSet<string>(), 5).ToAdd.Count == 0, "Import: no room and nothing to import");
        string urlCourse = ClassroomService.SubmissionsUrl("c 1", "tok");
        check(urlCourse.Contains("/courses/c%201/courseWork/-/studentSubmissions?userId=me") && urlCourse.Contains("pageToken=tok") && ClassroomService.CoursesUrl().Contains("studentId=me&courseStates=ACTIVE"), "Classroom: request addresses (student, active courses, all submissions of a course)");
    }

    private static async Task ClassroomNetwork(Action<bool, string> check, string folder)
    {
        var handler = new RecordingHandler(request => Route(request, uri => uri.AbsolutePath switch
        {
            "/v1/courses" => Ok("""{"courses":[{"id":"1","name":"Math","courseState":"ACTIVE"},{"id":"2","name":"Chem","courseState":"ACTIVE"},{"id":"3","name":"Skipped","courseState":"ACTIVE"}]}"""),
            "/v1/courses/1/courseWork" => Ok("""{"courseWork":[{"id":"w1","title":"Set 4","workType":"ASSIGNMENT","dueDate":{"year":2026,"month":10,"day":9}}]}"""),
            "/v1/courses/2/courseWork" => Ok("""{"courseWork":[{"id":"w2","title":"Lab","workType":"ASSIGNMENT","dueDate":{"year":2026,"month":10,"day":9}}]}"""),
            "/v1/courses/1/courseWork/-/studentSubmissions" => Ok("""{"studentSubmissions":[{"courseWorkId":"w1","state":"TURNED_IN"}]}"""),
            "/v1/courses/2/courseWork/-/studentSubmissions" => Ok("""{"error":{"code":403,"message":"Request had insufficient authentication scopes.","status":"PERMISSION_DENIED"}}""", HttpStatusCode.Forbidden),
            _ => Ok("{}", HttpStatusCode.NotFound),
        }));
        var service = new ClassroomService(SignedIn(folder, "classroom", handler), new HttpClient(handler));
        var fetch = await service.FetchAsync(c => c.Id != "3");
        check(fetch.Courses.Count == 2 && fetch.Work.Count == 2, "Classroom fetch: only the wanted courses are fetched");
        check(fetch.Work.First(x => x.Id == "w1").State == SubmissionState.TurnedIn && fetch.Work.First(x => x.Id == "w2").State == SubmissionState.Unknown && fetch.SubmissionsProblem is { Length: > 0 }, "Classroom fetch: when only the submissions call is refused, assignments are still returned (turned-in state unknown) with a note");
        check(handler.Requests.All(r => r.Method == "GET" || r.Url.StartsWith("https://oauth2.googleapis.com/token")) && !handler.Requests.Any(r => r.Url.Contains("/courses/3/")), "Classroom fetch: read-only, and skipped courses are not asked for assignments");

        var blocked = new RecordingHandler(request => Route(request, _ => Ok("""{"error":{"code":403,"message":"The caller does not have permission","status":"PERMISSION_DENIED"}}""", HttpStatusCode.Forbidden)));
        try { await new ClassroomService(SignedIn(folder, "classroom2", blocked), new HttpClient(blocked)).FetchAsync(null); check(false, "Classroom fetch: expected a refusal"); }
        catch (GoogleApiException ex) { check(ex.Problem == GoogleApiProblem.Blocked && ex.Message.StartsWith("Your school blocks this app — ask your admin, or use the iCal link"), "Classroom fetch: a refusal by the school shows the 'your school blocks this app' message"); }
    }
}
