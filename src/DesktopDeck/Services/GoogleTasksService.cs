using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using DesktopDeck.Models;

namespace DesktopDeck.Services;

/// <summary>
/// Google Tasks API v1: task lists, and the tasks of one list (list, create, change, delete). JSON reading and writing are
/// static and network-free so tests cover them. Network calls go through <see cref="GoogleApiClient"/>.
/// </summary>
public sealed class GoogleTasksService : IGoogleTasksApi
{
    public const string BaseUrl = "https://tasks.googleapis.com/tasks/v1";
    public const int PageSize = 100, MaxPages = 30;
    public static readonly GoogleApiInfo Info = new("Google Tasks", "Google Tasks API", "Tasks");

    private readonly GoogleApiClient api;

    public GoogleTasksService(GoogleAuth auth, HttpClient? http = null) => api = new GoogleApiClient(auth, Info, http);

    // ---------- Network ----------

    public async Task<List<GoogleTaskListChoice>> ListTaskListsAsync(CancellationToken cancel = default)
    {
        var all = new List<GoogleTaskListChoice>();
        string? page = null;
        for (int i = 0; i < 5; i++)
        {
            string json = await api.SendAsync(HttpMethod.Get, TaskListsUrl(page), null, cancel).ConfigureAwait(false);
            all.AddRange(ParseTaskLists(json, out page));
            if (page is null) break;
        }
        return all;
    }

    /// <summary>
    /// The tasks of a list, completed ones included. With <paramref name="updatedMin"/> only tasks changed since then, plus the
    /// deleted ones. <paramref name="complete"/> is false when the list was longer than we are willing to read.
    /// </summary>
    public async Task<(List<GoogleTask> Tasks, bool Complete)> ListTasksAsync(string listId, DateTimeOffset? updatedMin, CancellationToken cancel = default)
    {
        var all = new List<GoogleTask>();
        string? page = null;
        for (int i = 0; i < MaxPages; i++)
        {
            string json = await api.SendAsync(HttpMethod.Get, ListTasksUrl(listId, updatedMin, page), null, cancel).ConfigureAwait(false);
            all.AddRange(ParseTasks(json, out page));
            if (page is null) return (all, true);
        }
        return (all, false);
    }

    public async Task<GoogleTask> CreateTaskAsync(string listId, string title, bool completed, CancellationToken cancel = default)
    {
        string json = await api.SendAsync(HttpMethod.Post, $"{BaseUrl}/lists/{Uri.EscapeDataString(listId)}/tasks", BuildTaskJson(title, completed), cancel).ConfigureAwait(false);
        return ParseTaskJson(json) ?? throw new GoogleApiException("Google saved the task but sent back something Desktop Deck could not read.", GoogleApiProblem.Other);
    }

    /// <summary>Changes the title and finished state of a task (PATCH, so notes and due dates are left alone).</summary>
    public async Task<GoogleTask> UpdateTaskAsync(string listId, string taskId, string title, bool completed, CancellationToken cancel = default)
    {
        string json = await api.SendAsync(HttpMethod.Patch, TaskUrl(listId, taskId), BuildTaskJson(title, completed, patch: true), cancel).ConfigureAwait(false);
        return ParseTaskJson(json) ?? throw new GoogleApiException("Google saved the change but sent back something Desktop Deck could not read.", GoogleApiProblem.Other);
    }

    /// <summary>Deletes a task; one that is already gone counts as deleted.</summary>
    public async Task DeleteTaskAsync(string listId, string taskId, CancellationToken cancel = default)
    {
        try { await api.SendAsync(HttpMethod.Delete, TaskUrl(listId, taskId), null, cancel).ConfigureAwait(false); }
        catch (GoogleApiException ex) when (ex.Problem == GoogleApiProblem.NotFound) { /* already gone */ }
    }

    // ---------- Pure: addresses ----------

    public static string TaskListsUrl(string? pageToken = null) =>
        $"{BaseUrl}/users/@me/lists?maxResults=100" + (pageToken is null ? "" : "&pageToken=" + Uri.EscapeDataString(pageToken));

    public static string TaskUrl(string listId, string taskId) => $"{BaseUrl}/lists/{Uri.EscapeDataString(listId)}/tasks/{Uri.EscapeDataString(taskId)}";

    /// <summary>
    /// The tasks request. Completed and hidden tasks are included (the widget shows ticked tasks, and Google hides tasks
    /// that were cleared in its own app). Deleted tasks only come back with <paramref name="updatedMin"/>, which is how deletions are noticed.
    /// </summary>
    public static string ListTasksUrl(string listId, DateTimeOffset? updatedMin, string? pageToken = null)
    {
        var url = new StringBuilder($"{BaseUrl}/lists/{Uri.EscapeDataString(listId)}/tasks?maxResults={PageSize}&showCompleted=true&showHidden=true");
        if (updatedMin is { } since)
            url.Append("&showDeleted=true&updatedMin=").Append(Uri.EscapeDataString(since.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)));
        if (pageToken is not null) url.Append("&pageToken=").Append(Uri.EscapeDataString(pageToken));
        return url.ToString();
    }

    // ---------- Pure: reading ----------

    public static List<GoogleTaskListChoice> ParseTaskLists(string json, out string? nextPageToken)
    {
        var result = new List<GoogleTaskListChoice>();
        using var doc = JsonDocument.Parse(json);
        nextPageToken = GoogleApiClient.NextPage(doc.RootElement);
        if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in items.EnumerateArray())
        {
            string id = GoogleApiClient.Str(item, "id");
            if (id.Length == 0) continue;
            string title = GoogleApiClient.Str(item, "title").Trim();
            result.Add(new GoogleTaskListChoice { Id = id, Title = title.Length > 0 ? title : "Tasks" });
        }
        return result;
    }

    public static List<GoogleTask> ParseTasks(string json) => ParseTasks(json, out _);

    public static List<GoogleTask> ParseTasks(string json, out string? nextPageToken)
    {
        var result = new List<GoogleTask>();
        using var doc = JsonDocument.Parse(json);
        nextPageToken = GoogleApiClient.NextPage(doc.RootElement);
        if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in items.EnumerateArray())
            if (ParseTask(item) is { } task) result.Add(task);
        return result;
    }

    public static GoogleTask? ParseTaskJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ParseTask(doc.RootElement);
    }

    /// <summary>One task resource; null without an id.</summary>
    public static GoogleTask? ParseTask(JsonElement e)
    {
        string id = GoogleApiClient.Str(e, "id");
        if (id.Length == 0) return null;
        return new GoogleTask
        {
            Id = id,
            Title = GoogleApiClient.Str(e, "title").Trim(),
            Completed = GoogleApiClient.Str(e, "status") == "completed",
            Updated = Time(GoogleApiClient.Str(e, "updated")) ?? DateTimeOffset.MinValue,
            CompletedAt = Time(GoogleApiClient.Str(e, "completed")),
            ETag = GoogleApiClient.Str(e, "etag"),
            Deleted = GoogleApiClient.Bool(e, "deleted"),
            Hidden = GoogleApiClient.Bool(e, "hidden"),
            Parent = GoogleApiClient.Str(e, "parent"),
        };
    }

    private static DateTimeOffset? Time(string text) =>
        text.Length > 0 && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : null;

    // ---------- Pure: writing ----------

    /// <summary>
    /// The JSON body to create a task or PATCH one. Un-ticking a task must send status needsAction and clear its
    /// completed time (<paramref name="patch"/>), or Google keeps it completed.
    /// </summary>
    public static string BuildTaskJson(string title, bool completed, bool patch = false)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("title", title.Trim());
            w.WriteString("status", completed ? "completed" : "needsAction");
            if (!completed && patch) w.WriteNull("completed");
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
