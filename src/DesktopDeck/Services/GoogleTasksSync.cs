using DesktopDeck.Models;

namespace DesktopDeck.Services;

/// <summary>Everything the sync needs to know about a Today's Tasks list at one moment (copied on the UI thread).</summary>
public sealed record TaskSyncSnapshot(string ListId, IReadOnlyList<TaskEntry> Tasks, IReadOnlyList<GoogleTaskTombstone> Deletes, DateTimeOffset? Watermark, DateTimeOffset ImportCompletedSince)
{
    /// <summary>Finished Google tasks older than this are not imported (a long-time Google Tasks user has years of them).</summary>
    public static DateTimeOffset ImportSince(LauncherItem list, DateTimeOffset now) =>
        list.DailyReset ? new DateTimeOffset(now.LocalDateTime.Date, TimeZoneInfo.Local.GetUtcOffset(now.LocalDateTime.Date)) : now.AddDays(-7);

    public static TaskSyncSnapshot From(LauncherItem list, DateTimeOffset now) => new(
        string.IsNullOrWhiteSpace(list.GoogleTaskListId) ? "@default" : list.GoogleTaskListId,
        list.Tasks.Select(t => t.Copy()).ToList(),
        list.GoogleTaskDeletes.Select(d => new GoogleTaskTombstone { ListId = d.ListId, TaskId = d.TaskId }).ToList(),
        list.GoogleTasksWatermark,
        ImportSince(list, now));
}

public enum PullKind { Add, Update, Remove }
public enum PushKind { Create, Update, Delete }

/// <summary>A change to make here because of what Google has.</summary>
/// <param name="ModifiedAtPlan">The local task's Modified time when the plan was made: the change is dropped if the task was edited again since.</param>
public sealed record PullOp(PullKind Kind, string LocalId, GoogleTask? Remote, DateTimeOffset? ModifiedAtPlan);

/// <summary>A change to send to Google because of what is here.</summary>
public sealed record PushOp(PushKind Kind, string LocalId, string GoogleId, string Title, bool Done, DateTimeOffset? ModifiedAtPlan);

public sealed record TaskSyncPlan(List<PullOp> Pulls, List<PushOp> Pushes, DateTimeOffset? Watermark);

/// <summary>What one push did. <see cref="Gone"/>: Google no longer has that task (it was deleted there).</summary>
public sealed record PushResult(PushOp Op, GoogleTask? Remote, bool Gone, string? Error);

public sealed record TaskSyncOutcome(string ListId, List<PullOp> Pulls, List<PushResult> Pushes, DateTimeOffset? Watermark);

/// <summary>
/// Two-way merge between a Today's Tasks list and a Google Tasks list. Pure (no network, no clock): <see cref="Plan"/> decides
/// what to change on each side, <see cref="Apply"/> puts the result into the live list on the UI thread.
/// <para>Rules: a task is "dirty" when it has a local <see cref="TaskEntry.Modified"/> time. If only one side changed, that change
/// wins. If both changed, the later change wins (local Modified against Google's updated). Local tasks are never deleted because a fetch
/// failed or came back empty: deletions come only from Google saying "deleted", or from a complete listing that lacks a task that was
/// not edited here. Finished tasks cleared by the daily reset stay on Google; they are only hidden here.</para>
/// </summary>
public static class GoogleTasksSync
{
    public const int MaxTasks = 400;

    /// <param name="incremental">The remote list holds only what changed since the watermark (and deletions).</param>
    /// <param name="listComplete">The whole answer was read (not cut off), so a task missing from a full listing really is gone.</param>
    public static TaskSyncPlan Plan(TaskSyncSnapshot snap, IReadOnlyList<GoogleTask> remote, bool incremental, bool listComplete)
    {
        var pulls = new List<PullOp>();
        var pushes = new List<PushOp>();
        var byGoogleId = new Dictionary<string, TaskEntry>(StringComparer.Ordinal);
        foreach (var t in snap.Tasks)
            if (t.IsLinked && (t.GoogleListId.Length == 0 || t.GoogleListId == snap.ListId)) byGoogleId.TryAdd(t.GoogleId, t);
        var doomed = snap.Deletes.Where(d => d.ListId == snap.ListId).Select(d => d.TaskId).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var handled = new HashSet<string>(StringComparer.Ordinal);
        int count = snap.Tasks.Count;

        foreach (var r in remote)
        {
            if (!seen.Add(r.Id)) continue;
            if (doomed.Contains(r.Id)) continue; // deleted here; the push below deletes it there
            if (!byGoogleId.TryGetValue(r.Id, out var local))
            {
                if (r.Deleted || (r.Completed && r.Hidden)) continue;
                if (r.Completed && (r.CompletedAt ?? r.Updated) < snap.ImportCompletedSince) continue;
                if (count >= MaxTasks) continue;
                pulls.Add(new PullOp(PullKind.Add, "", r, null));
                count++;
                continue;
            }
            handled.Add(local.Id);
            bool dirty = local.Modified is not null;
            if (r.Deleted)
            {
                if (!dirty || local.Modified <= r.Updated) pulls.Add(new PullOp(PullKind.Remove, local.Id, r, local.Modified));
                else pushes.Add(new PushOp(PushKind.Create, local.Id, "", local.Text, local.Done, local.Modified)); // edited after it was deleted there: bring it back
                continue;
            }
            bool remoteChanged = r.Updated != local.GoogleUpdated || (r.ETag.Length > 0 && local.GoogleEtag.Length > 0 && r.ETag != local.GoogleEtag);
            if (!remoteChanged)
            {
                if (dirty) pushes.Add(Update(local));
            }
            else if (!dirty || local.Modified <= r.Updated) pulls.Add(new PullOp(PullKind.Update, local.Id, r, local.Modified));
            else pushes.Add(Update(local));
        }

        foreach (var local in snap.Tasks)
        {
            if (handled.Contains(local.Id)) continue;
            if (!local.IsLinked)
            {
                if (!local.Hidden) pushes.Add(new PushOp(PushKind.Create, local.Id, "", local.Text, local.Done, local.Modified));
                continue;
            }
            if (local.GoogleListId.Length > 0 && local.GoogleListId != snap.ListId) continue; // belongs to another list
            if (local.Modified is not null) pushes.Add(Update(local));
            else if (!incremental && listComplete && !seen.Contains(local.GoogleId))
                pulls.Add(new PullOp(PullKind.Remove, local.Id, null, null)); // a complete listing no longer has it, and it was not edited here
        }

        foreach (var id in doomed) pushes.Add(new PushOp(PushKind.Delete, "", id, "", false, null));

        DateTimeOffset? watermark = snap.Watermark;
        foreach (var r in remote)
            if (r.Updated != DateTimeOffset.MinValue && (watermark is null || r.Updated > watermark)) watermark = r.Updated;
        return new TaskSyncPlan(pulls, pushes, watermark);
    }

    private static PushOp Update(TaskEntry t) => new(PushKind.Update, t.Id, t.GoogleId, t.Text, t.Done, t.Modified);

    /// <summary>
    /// Puts a sync's result into the live list. Changes whose local task was edited again since the plan was made are skipped
    /// (the next sync sorts them out). Returns true when anything in the list changed.
    /// </summary>
    public static bool Apply(LauncherItem list, TaskSyncOutcome outcome)
    {
        bool changed = false;
        TaskEntry? Find(string id) => id.Length == 0 ? null : list.Tasks.FirstOrDefault(t => t.Id == id);

        foreach (var pull in outcome.Pulls)
        {
            var r = pull.Remote;
            switch (pull.Kind)
            {
                case PullKind.Add when r is not null:
                    if (list.Tasks.Any(t => t.GoogleId == r.Id) || list.Tasks.Count >= MaxTasks) break;
                    list.Tasks.Add(new TaskEntry { Text = TextOf(r), Done = r.Completed, GoogleId = r.Id, GoogleListId = outcome.ListId, GoogleEtag = r.ETag, GoogleUpdated = r.Updated });
                    changed = true;
                    break;
                case PullKind.Update when r is not null && Find(pull.LocalId) is { } update && update.Modified == pull.ModifiedAtPlan:
                    update.Text = TextOf(r);
                    update.Done = r.Completed;
                    update.GoogleEtag = r.ETag;
                    update.GoogleUpdated = r.Updated;
                    update.GoogleListId = outcome.ListId;
                    update.Modified = null;
                    if (!r.Completed) update.Hidden = false;
                    changed = true;
                    break;
                case PullKind.Remove when Find(pull.LocalId) is { } gone && gone.Modified == pull.ModifiedAtPlan:
                    list.Tasks.Remove(gone);
                    changed = true;
                    break;
            }
        }

        foreach (var push in outcome.Pushes)
        {
            switch (push.Op.Kind)
            {
                case PushKind.Delete:
                    if (push.Error is null) changed |= list.GoogleTaskDeletes.RemoveAll(d => d.TaskId == push.Op.GoogleId && d.ListId == outcome.ListId) > 0;
                    break;
                default:
                    if (Find(push.Op.LocalId) is not { } task) break;
                    if (push.Remote is { } saved)
                    {
                        task.GoogleId = saved.Id;
                        task.GoogleListId = outcome.ListId;
                        task.GoogleEtag = saved.ETag;
                        task.GoogleUpdated = saved.Updated;
                        // Edited again while the request was on its way: keep it dirty so the next sync sends that too.
                        if (task.Modified == push.Op.ModifiedAtPlan) task.Modified = null;
                        changed = true;
                    }
                    else if (push.Gone)
                    {
                        // Google no longer has it: it becomes a task that only lives here until the next sync recreates it.
                        task.GoogleId = ""; task.GoogleListId = ""; task.GoogleEtag = ""; task.GoogleUpdated = null; task.Hidden = false;
                        task.Modified ??= DateTimeOffset.UtcNow;
                        changed = true;
                    }
                    break;
            }
        }

        if (outcome.Watermark is { } mark && mark != list.GoogleTasksWatermark) { list.GoogleTasksWatermark = mark; changed = true; }
        return changed;
    }

    private static string TextOf(GoogleTask r) => r.Title.Length > 0 ? r.Title : "(No title)";

    /// <summary>
    /// The user picked another Google list (or turned sync off and on): forget what is linked to the old one. Tasks that were only
    /// copies of Google tasks are dropped (they are still on Google); tasks edited here and not yet sent become plain local tasks, and are
    /// sent to the new list. Returns true if anything changed.
    /// </summary>
    public static bool SwitchList(LauncherItem list, string newListId)
    {
        bool changed = false;
        foreach (var task in list.Tasks.ToList())
        {
            if (!task.IsLinked || task.GoogleListId == newListId) continue;
            changed = true;
            if (task.Modified is null) { list.Tasks.Remove(task); continue; }
            task.GoogleId = ""; task.GoogleListId = ""; task.GoogleEtag = ""; task.GoogleUpdated = null; task.Hidden = false;
        }
        changed |= list.GoogleTaskDeletes.RemoveAll(d => d.ListId != newListId) > 0 || list.GoogleTasksWatermark is not null;
        list.GoogleTasksWatermark = null;
        return changed;
    }

    /// <summary>
    /// Customize works on a copy of the widget, and syncs keep running (and keep linking tasks) while it is open. When the edited copy
    /// replaces the live item, it must take the live item's tasks, pending deletes and watermark; the Customize window never edits those.
    /// Otherwise tasks that were linked meanwhile would look new and be created on Google a second time.
    /// </summary>
    public static void CarryOverLiveState(LauncherItem live, LauncherItem edited)
    {
        edited.Tasks = live.Tasks.Select(t => t.Copy()).ToList();
        edited.GoogleTaskDeletes = live.GoogleTaskDeletes.Select(d => new GoogleTaskTombstone { ListId = d.ListId, TaskId = d.TaskId }).ToList();
        edited.GoogleTasksWatermark = live.GoogleTasksWatermark;
    }

    /// <summary>
    /// Disconnecting Google (or another account signing in later): nothing here may point at the old account's ids any more.
    /// Every task becomes a plain local task and sync is switched off.
    /// </summary>
    public static void Unlink(LauncherItem list)
    {
        foreach (var task in list.Tasks.ToList())
        {
            if (task.Hidden) { list.Tasks.Remove(task); continue; }
            task.GoogleId = ""; task.GoogleListId = ""; task.GoogleEtag = ""; task.GoogleUpdated = null; task.Modified = null;
        }
        list.GoogleTaskDeletes.Clear();
        list.GoogleTasksWatermark = null;
        list.GoogleTasksSync = false;
    }

    // ---------- Marking local changes ----------

    /// <summary>Call after any change made here to a task (text, ticked state, new task).</summary>
    public static void Touch(LauncherItem list, TaskEntry task, DateTimeOffset now)
    {
        // A linked task is marked even while sync is off, so a later sync does not overwrite what was changed in between.
        if (list.GoogleTasksSync || task.IsLinked) task.Modified = now.ToUniversalTime();
    }

    /// <summary>Deleting a task here: a linked one is remembered so the next sync deletes it on Google as well.</summary>
    public static void Delete(LauncherItem list, TaskEntry task)
    {
        list.Tasks.Remove(task);
        if (task.IsLinked && !list.GoogleTaskDeletes.Any(d => d.TaskId == task.GoogleId))
            list.GoogleTaskDeletes.Add(new GoogleTaskTombstone { ListId = task.GoogleListId.Length > 0 ? task.GoogleListId : list.GoogleTaskListId, TaskId = task.GoogleId });
    }

    /// <summary>Tasks that still have something to send (for a "syncing…" hint).</summary>
    public static int PendingCount(LauncherItem list) => list.Tasks.Count(t => t.Modified is not null && !t.Hidden) + list.GoogleTaskDeletes.Count;
}
