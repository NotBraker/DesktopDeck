using System.Globalization;
using DesktopDeck.Models;

namespace DesktopDeck.Services;

/// <summary>The calls the sync makes on Google Tasks (a fake stands in for it in tests).</summary>
public interface IGoogleTasksApi
{
    Task<(List<GoogleTask> Tasks, bool Complete)> ListTasksAsync(string listId, DateTimeOffset? updatedMin, CancellationToken cancel);
    Task<GoogleTask> CreateTaskAsync(string listId, string title, bool completed, CancellationToken cancel);
    Task<GoogleTask> UpdateTaskAsync(string listId, string taskId, string title, bool completed, CancellationToken cancel);
    Task DeleteTaskAsync(string listId, string taskId, CancellationToken cancel);
}

/// <summary>How a list's sync is doing, for the little note in the widget header.</summary>
public sealed record TaskSyncStatus(bool Syncing, DateTimeOffset? LastSynced, string? Error, bool NeedsAction)
{
    public static readonly TaskSyncStatus None = new(false, null, null, false);

    /// <summary>A quiet one-line note, or null when all is well: "Offline — synced 10:42" or the problem when the user must act.</summary>
    public string? Note(DateTimeOffset now, CultureInfo? culture = null)
    {
        if (Error is null) return null;
        if (NeedsAction || LastSynced is null) return Error;
        var local = LastSynced.Value.ToOffset(now.Offset);
        string time = local.ToString("t", culture ?? CultureInfo.CurrentCulture);
        return "Offline — synced " + (local.Date == now.Date ? time : local.ToString("MMM d", culture ?? CultureInfo.CurrentCulture) + ", " + time);
    }
}

/// <summary>
/// Runs the Google Tasks sync for Today's Tasks lists in the background: at most one sync per list at a time, at most one
/// every 90 seconds unless forced (the refresh button, a local change). It never touches a list itself: the caller copies the list
/// into a <see cref="TaskSyncSnapshot"/> on the UI thread, <see cref="RunAsync"/> returns what to change, and the caller applies it
/// with <see cref="GoogleTasksSync.Apply"/> on the UI thread. A failed fetch changes nothing (local tasks are never deleted for it).
/// </summary>
public sealed class GoogleTasksFeed
{
    public static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(90);

    private sealed class State
    {
        public bool Syncing;
        public DateTimeOffset LastAttempt = DateTimeOffset.MinValue;
        public DateTimeOffset? LastSynced;
        public string? Error;
        public bool NeedsAction;
    }

    private readonly IGoogleTasksApi api;
    private readonly Func<bool> ready;
    private readonly Func<DateTimeOffset> clock;
    private readonly object gate = new();
    private readonly Dictionary<string, State> states = new(StringComparer.Ordinal);

    /// <param name="ready">True when an account with the Tasks permission is connected.</param>
    public GoogleTasksFeed(IGoogleTasksApi api, Func<bool> ready, Func<DateTimeOffset>? clock = null)
    {
        this.api = api;
        this.ready = ready;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Smoke renders: never sync.</summary>
    public bool Inert { get; set; }
    public bool Ready => !Inert && ready();

    public TaskSyncStatus StatusOf(string itemId)
    {
        lock (gate) return states.TryGetValue(itemId, out var s) ? new TaskSyncStatus(s.Syncing, s.LastSynced, s.Error, s.NeedsAction) : TaskSyncStatus.None;
    }

    /// <summary>Claims a sync for this list. False when not connected, one is already running, or the last one was less than 90 seconds ago (unless forced).</summary>
    public bool TryBegin(string itemId, bool force)
    {
        if (!Ready) return false;
        lock (gate)
        {
            if (!states.TryGetValue(itemId, out var s)) states[itemId] = s = new State();
            if (s.Syncing) return false;
            var now = clock();
            if (!force && now - s.LastAttempt < RefreshEvery && now >= s.LastAttempt) return false;
            s.Syncing = true;
            s.LastAttempt = now;
            return true;
        }
    }

    /// <summary>Forgets a list's status (the widget was deleted or sync was switched off).</summary>
    public void Forget(string itemId) { lock (gate) states.Remove(itemId); }

    /// <summary>
    /// Fetches, plans and sends. Call after <see cref="TryBegin"/> returned true. Returns what to apply to the list, or null
    /// when nothing could be fetched (the problem is in <see cref="StatusOf"/>).
    /// </summary>
    public async Task<TaskSyncOutcome?> RunAsync(string itemId, TaskSyncSnapshot snap, CancellationToken cancel = default)
    {
        string? error = null;
        bool needsAction = false;
        TaskSyncOutcome? outcome = null;
        try
        {
            bool incremental = snap.Watermark is not null;
            var (remote, complete) = await api.ListTasksAsync(snap.ListId, snap.Watermark, cancel).ConfigureAwait(false);
            var plan = GoogleTasksSync.Plan(snap, remote, incremental, complete);
            var results = new List<PushResult>();
            foreach (var op in plan.Pushes)
            {
                cancel.ThrowIfCancellationRequested();
                try { results.Add(await PushAsync(snap.ListId, op, cancel).ConfigureAwait(false)); }
                catch (GoogleApiException ex) when (ex.Problem is GoogleApiProblem.Network)
                {
                    error = ex.Message; // stop here; what was sent so far is kept, the rest waits for the next sync
                    break;
                }
                catch (GoogleApiException ex) when (ex.Problem is GoogleApiProblem.NotFound)
                {
                    results.Add(new PushResult(op, null, true, null));
                }
                catch (GoogleApiException ex)
                {
                    error ??= ex.Message;
                    results.Add(new PushResult(op, null, false, ex.Message));
                    if (ex.NeedsUserAction) { needsAction = true; break; }
                }
            }
            outcome = new TaskSyncOutcome(snap.ListId, plan.Pulls, results, plan.Watermark);
        }
        catch (GoogleApiException ex) when (ex.Problem == GoogleApiProblem.NotFound)
        {
            error = "That Google Tasks list no longer exists. Pick another one in Customize.";
            needsAction = true;
        }
        catch (GoogleApiException ex) { error = ex.Message; needsAction = ex.NeedsUserAction; }
        catch (GoogleAuthException ex) { error = ex.Message; needsAction = ex.NeedsReconnect; }
        catch (CalendarException ex) { error = ex.Message; }
        catch (OperationCanceledException) { error = "Sync was cancelled."; }
        finally
        {
            lock (gate)
            {
                if (states.TryGetValue(itemId, out var s))
                {
                    s.Syncing = false;
                    s.Error = error;
                    s.NeedsAction = needsAction;
                    if (error is null) s.LastSynced = clock();
                }
            }
        }
        return outcome;
    }

    private async Task<PushResult> PushAsync(string listId, PushOp op, CancellationToken cancel)
    {
        switch (op.Kind)
        {
            case PushKind.Create:
                return new PushResult(op, await api.CreateTaskAsync(listId, op.Title, op.Done, cancel).ConfigureAwait(false), false, null);
            case PushKind.Update:
                return new PushResult(op, await api.UpdateTaskAsync(listId, op.GoogleId, op.Title, op.Done, cancel).ConfigureAwait(false), false, null);
            default:
                await api.DeleteTaskAsync(listId, op.GoogleId, cancel).ConfigureAwait(false);
                return new PushResult(op, null, false, null);
        }
    }
}
