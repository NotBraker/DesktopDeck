using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DesktopDeck.Widgets;

/// <summary>GitHub REST API parsing, URLs and filtering for the GitHub widget. Pure logic (unit tested, no network).</summary>
public static class GitHubLogic
{
    public const string Api = "https://api.github.com";
    public const string ApiVersion = "2022-11-28";
    public const int MaxItems = 30;

    public static readonly string[] Sections = ["notifications", "reviews", "assigned", "created", "repos"];

    public sealed record Notification(string Id, string Title, string Repo, string Type, string Reason, DateTimeOffset Updated, string Url, bool Unread, string Avatar);
    /// <summary>An issue or pull request from the search API.</summary>
    public sealed record Item(long Number, string Title, string Repo, string Url, bool IsPr, bool Draft, string Author, string Avatar, DateTimeOffset Updated, int Comments);
    public sealed record Repo(string Name, string Description, int Stars, int Forks, int OpenIssues, string Url, DateTimeOffset Pushed, string Language, string Avatar);

    /// <summary>Everything the widget shows, trimmed for the settings file ("cache.json").</summary>
    public sealed class Data
    {
        public List<Notification> Notifications { get; set; } = [];
        public List<Item> Reviews { get; set; } = [];
        public List<Item> Assigned { get; set; } = [];
        public List<Item> Created { get; set; } = [];
        public List<Repo> Repos { get; set; } = [];
        /// <summary>Unread notifications on GitHub's first page of results (before the repo filter and the item limit).</summary>
        public int Unread { get; set; }
        /// <summary>Per section ("reviews", "assigned", "created"): how many matches GitHub reports in total.</summary>
        public Dictionary<string, int> Totals { get; set; } = [];
        /// <summary>Per section: why it could not be loaded (the other sections still show).</summary>
        public Dictionary<string, string> Errors { get; set; } = [];
    }

    // ---------- URLs ----------

    public static string NotificationsUrl(int count, bool participating)
        => $"{Api}/notifications?per_page={Math.Clamp(count, 1, 50)}" + (participating ? "&participating=true" : "");

    /// <summary>The issue-search query for a section ("reviews", "assigned", "created"); <paramref name="types"/> is "both", "pr" or "issue".</summary>
    public static string SearchQuery(string section, string types)
    {
        string kind = types switch { "pr" => " is:pr", "issue" => " is:issue", _ => "" };
        return section switch
        {
            "reviews" => "is:open is:pr review-requested:@me archived:false",
            "assigned" => "is:open assignee:@me archived:false" + kind,
            _ => "is:open author:@me archived:false" + kind,
        };
    }

    public static string SearchUrl(string section, string types, int count)
        => $"{Api}/search/issues?q={Uri.EscapeDataString(SearchQuery(section, types))}&sort=updated&order=desc&per_page={Math.Clamp(count, 1, 100)}";

    public static string RepoUrl(string fullName) => $"{Api}/repos/{fullName}";
    public static string MyReposUrl(int count) => $"{Api}/user/repos?sort=pushed&direction=desc&affiliation=owner&per_page={Math.Clamp(count, 1, 50)}";
    public static string ThreadUrl(string id) => $"{Api}/notifications/threads/{Uri.EscapeDataString(id)}";

    /// <summary>How many to ask for: the display count, more when a repo filter will drop some.</summary>
    public static int FetchCount(int max, bool filtering) => Math.Clamp(filtering ? max * 3 : max, 1, 100);

    // ---------- Repo filter ----------

    private static readonly Regex RepoName = new(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", RegexOptions.Compiled);

    /// <summary>Lines like "owner/repo" or "owner/*" (or a bare "repo" name) matched against "owner/repo" (case-insensitive). No patterns matches everything.</summary>
    public static bool RepoMatches(string repo, IReadOnlyCollection<string> patterns)
    {
        if (patterns.Count == 0) return true;
        foreach (string raw in patterns)
        {
            string p = raw.Trim();
            if (p.Length == 0) continue;
            if (p.EndsWith("/*", StringComparison.Ordinal)) { if (repo.StartsWith(p[..^1], StringComparison.OrdinalIgnoreCase)) return true; }
            else if (p.Contains('/')) { if (string.Equals(repo, p, StringComparison.OrdinalIgnoreCase)) return true; }
            else if (repo.EndsWith("/" + p, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>The lines that name one exact repository (valid "owner/repo"), for the repository list.</summary>
    public static List<string> ExactRepos(IEnumerable<string> lines)
        => lines.Select(l => l.Trim().Replace("https://github.com/", "", StringComparison.OrdinalIgnoreCase).Trim('/')).Where(l => RepoName.IsMatch(l)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static List<Notification> Filter(IEnumerable<Notification> list, IReadOnlyCollection<string> patterns, int max)
        => list.Where(n => RepoMatches(n.Repo, patterns)).Take(Math.Max(0, max)).ToList();
    public static List<Item> Filter(IEnumerable<Item> list, IReadOnlyCollection<string> patterns, int max)
        => list.Where(n => RepoMatches(n.Repo, patterns)).Take(Math.Max(0, max)).ToList();

    // ---------- Parsing ----------

    /// <summary>Reads GET /notifications. Throws <see cref="FormatException"/> if it isn't a list.</summary>
    public static List<Notification> ParseNotifications(string json)
    {
        var root = Parse(json);
        if (root.ValueKind != JsonValueKind.Array) throw new FormatException("GitHub sent something unexpected.");
        var result = new List<Notification>();
        foreach (var e in root.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            string id = Str(e, "id");
            if (id.Length == 0 || !e.TryGetProperty("subject", out var subject) || subject.ValueKind != JsonValueKind.Object) continue;
            string title = OnlineLogic.Squash(Str(subject, "title"));
            if (title.Length == 0) continue;
            e.TryGetProperty("repository", out var repo);
            string repoUrl = Str(repo, "html_url");
            string avatar = repo.ValueKind == JsonValueKind.Object && repo.TryGetProperty("owner", out var owner) ? Str(owner, "avatar_url") : "";
            string type = Str(subject, "type");
            result.Add(new Notification(id, title, Str(repo, "full_name"), type, Str(e, "reason"), Date(e, "updated_at"),
                WebUrl(Str(subject, "url"), type, repoUrl), Bool(e, "unread"), avatar));
        }
        return result;
    }

    /// <summary>Reads GET /search/issues (total count and the items).</summary>
    public static (int Total, List<Item> Items) ParseSearch(string json)
    {
        var root = Parse(json);
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) throw new FormatException("GitHub sent something unexpected.");
        var result = new List<Item>();
        foreach (var e in items.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            string title = OnlineLogic.Squash(Str(e, "title"));
            string url = Str(e, "html_url");
            if (title.Length == 0 || !OnlineLogic.IsWebLink(url)) continue;
            e.TryGetProperty("user", out var user);
            result.Add(new Item(e.TryGetProperty("number", out var n) && n.TryGetInt64(out long number) ? number : 0, title, RepoFromApiUrl(Str(e, "repository_url")), url,
                e.TryGetProperty("pull_request", out _), Bool(e, "draft"), Str(user, "login"), Str(user, "avatar_url"), Date(e, "updated_at"),
                e.TryGetProperty("comments", out var c) && c.TryGetInt32(out int comments) ? comments : 0));
        }
        int total = root.TryGetProperty("total_count", out var t) && t.TryGetInt32(out int tc) ? tc : result.Count;
        return (total, result);
    }

    /// <summary>Reads GET /user/repos (a list) or GET /repos/{owner}/{repo} (one object).</summary>
    public static List<Repo> ParseRepos(string json)
    {
        var root = Parse(json);
        var result = new List<Repo>();
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (ReadRepo(root) is { } one) result.Add(one);
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in root.EnumerateArray())
                if (ReadRepo(e) is { } repo) result.Add(repo);
        }
        else throw new FormatException("GitHub sent something unexpected.");
        return result;
    }

    private static Repo? ReadRepo(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        string name = Str(e, "full_name"), url = Str(e, "html_url");
        if (name.Length == 0 || !OnlineLogic.IsWebLink(url)) return null;
        string avatar = e.TryGetProperty("owner", out var owner) ? Str(owner, "avatar_url") : "";
        return new Repo(name, OnlineLogic.Squash(Str(e, "description")), Int(e, "stargazers_count"), Int(e, "forks_count"), Int(e, "open_issues_count"), url, Date(e, "pushed_at"), Str(e, "language"), avatar);
    }

    /// <summary>An avatar address asking GitHub for a small picture (append "s=48"); empty if it isn't a GitHub avatar.</summary>
    public static string AvatarUrl(string? url, int pixels)
    {
        if (!ConnectedLogic.IsAvatarUrl(url)) return "";
        return url!.Contains("s=", StringComparison.Ordinal) ? url : url + (url.Contains('?') ? "&" : "?") + "s=" + pixels.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>"https://api.github.com/repos/octo/cat" to "octo/cat".</summary>
    public static string RepoFromApiUrl(string? apiUrl)
    {
        const string marker = "/repos/";
        if (apiUrl is null) return "";
        int at = apiUrl.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0) return "";
        var parts = apiUrl[(at + marker.Length)..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[0] + "/" + parts[1] : "";
    }

    /// <summary>
    /// The address to open for a notification. Its subject URL is an API address
    /// (https://api.github.com/repos/o/r/pulls/12), so it is mapped to the web page (https://github.com/o/r/pull/12).
    /// </summary>
    public static string WebUrl(string? subjectApiUrl, string? type, string? repoHtmlUrl)
    {
        string fallback = OnlineLogic.IsWebLink(repoHtmlUrl) ? repoHtmlUrl! : "https://github.com/notifications";
        if (subjectApiUrl is null || !subjectApiUrl.StartsWith(Api + "/repos/", StringComparison.Ordinal)) return fallback;
        var parts = subjectApiUrl[(Api.Length + "/repos/".Length)..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return fallback;
        string repo = $"https://github.com/{parts[0]}/{parts[1]}";
        if (parts.Length < 4) return repo;
        string kind = parts[2], id = parts[3];
        return kind switch
        {
            "pulls" => $"{repo}/pull/{id}",
            "issues" => $"{repo}/issues/{id}",
            "commits" => $"{repo}/commit/{id}",
            "releases" => $"{repo}/releases",
            "discussions" => $"{repo}/discussions/{id}",
            _ => type == "CheckSuite" ? $"{repo}/actions" : repo,
        };
    }

    /// <summary>A short friendly reason for a failed GitHub request.</summary>
    public static string Problem(int status, string? rateRemaining, string section)
    {
        if (status == 401) return "GitHub rejected the token. Right-click, Disconnect, then connect with a new token.";
        if (status == 403 && rateRemaining == "0") return "GitHub's rate limit is used up. It will resume soon.";
        if (status is 403 or 404)
            return section == "notifications"
                ? "GitHub won't show notifications to this token. Use a classic token with the notifications and repo scopes."
                : "GitHub refused the request. Check the token's repo access.";
        if (status == 422) return "GitHub couldn't run that search.";
        return ConnectedLogic.GenericProblem(status, false, false);
    }

    // ---------- Cache ----------

    public static string ToCache(Data data) => JsonSerializer.Serialize(data);

    public static Data FromCache(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Data();
        try { return JsonSerializer.Deserialize<Data>(json) ?? new Data(); }
        catch (JsonException) { return new Data(); }
    }

    /// <summary>The number of unread notifications GitHub reported.</summary>
    public static int UnreadCount(Data data) => Math.Max(data.Unread, 0);

    /// <summary>The cache without one notification (after it was marked as read).</summary>
    public static Data WithoutNotification(Data data, string id)
    {
        if (data.Notifications.Any(n => n.Id == id && n.Unread)) data.Unread = Math.Max(0, data.Unread - 1);
        data.Notifications = data.Notifications.Where(n => n.Id != id).ToList();
        return data;
    }

    /// <summary>"3 minutes ago"-style text is built by the widget; this gives the reason in words ("review_requested" to "Review requested").</summary>
    public static string ReasonText(string reason)
    {
        if (string.IsNullOrEmpty(reason)) return "";
        if (reason == "ci_activity") return "CI activity";
        string text = reason.Replace('_', ' ');
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    // ---------- JSON helpers ----------

    private static JsonElement Parse(string json)
    {
        try { using var doc = JsonDocument.Parse(json); return doc.RootElement.Clone(); }
        catch (JsonException ex) { throw new FormatException("GitHub sent something unexpected.", ex); }
    }
    private static string Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
    private static bool Bool(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
    private static int Int(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out int v) ? v : 0;
    private static DateTimeOffset Date(JsonElement e, string name)
        => DateTimeOffset.TryParse(Str(e, name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : DateTimeOffset.MinValue;
}
