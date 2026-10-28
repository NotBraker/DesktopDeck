using DesktopDeck.Widgets;

public static class GitHubTests
{
    private const string Notifications = """
        [
          {"id":"1","unread":true,"reason":"review_requested","updated_at":"2026-10-04T10:00:00Z",
           "subject":{"title":"Add dark mode","url":"https://api.github.com/repos/octo-org/widgets/pulls/482","type":"PullRequest"},
           "repository":{"full_name":"octo-org/widgets","html_url":"https://github.com/octo-org/widgets","owner":{"avatar_url":"https://avatars.githubusercontent.com/u/1?v=4"}}},
          {"id":"2","unread":false,"reason":"mention","updated_at":"2026-10-04T09:00:00Z",
           "subject":{"title":"Crash   on  start","url":"https://api.github.com/repos/octo-org/deck/issues/77","type":"Issue"},
           "repository":{"full_name":"octo-org/deck","html_url":"https://github.com/octo-org/deck","owner":{"avatar_url":""}}},
          {"id":"3","unread":true,"reason":"subscribed","updated_at":"2026-10-03T09:00:00Z",
           "subject":{"title":"v2.4.0","url":"https://api.github.com/repos/octo-org/widgets/releases/99","type":"Release"},
           "repository":{"full_name":"octo-org/widgets","html_url":"https://github.com/octo-org/widgets","owner":{}}},
          {"id":"4","unread":true,"updated_at":"2026-10-03T08:00:00Z","subject":{"title":"","url":null,"type":"Issue"}},
          {"id":"5","unread":true,"reason":"subscribed","updated_at":"2026-10-03T08:00:00Z",
           "subject":{"title":"Discussion thread","url":null,"type":"Discussion"},"repository":{"full_name":"octo-org/docs","html_url":"https://github.com/octo-org/docs","owner":{}}}
        ]
        """;

    private const string SearchResult = """
        {"total_count":12,"incomplete_results":false,"items":[
          {"number":482,"title":"Add dark mode","html_url":"https://github.com/octo-org/widgets/pull/482","repository_url":"https://api.github.com/repos/octo-org/widgets",
           "pull_request":{"url":"x"},"draft":true,"user":{"login":"hubot","avatar_url":"https://avatars.githubusercontent.com/u/9?v=4"},"updated_at":"2026-10-04T10:00:00Z","comments":4},
          {"number":77,"title":"Crash on start","html_url":"https://github.com/octo-org/deck/issues/77","repository_url":"https://api.github.com/repos/octo-org/deck",
           "user":{"login":"ada"},"updated_at":"2026-10-04T09:00:00Z","comments":0},
          {"number":5,"title":"No link","html_url":"javascript:alert(1)","repository_url":"https://api.github.com/repos/o/r"}
        ]}
        """;

    public static void NotificationsAreParsedAndMappedToWebAddresses()
    {
        var list = GitHubLogic.ParseNotifications(Notifications);
        Expect.Equal(4, list.Count, "the one without a title is skipped");
        Expect.Equal("https://github.com/octo-org/widgets/pull/482", list[0].Url, "pull request address");
        Expect.Equal("https://github.com/octo-org/deck/issues/77", list[1].Url, "issue address");
        Expect.Equal("https://github.com/octo-org/widgets/releases", list[2].Url, "release address");
        Expect.Equal("https://github.com/octo-org/docs", list[3].Url, "unknown subject falls back to the repository");
        Expect.True(list[0].Unread && !list[1].Unread, "unread flags");
        Expect.Equal("Crash on start", list[1].Title, "whitespace squashed");
        Expect.Equal("octo-org/widgets", list[0].Repo, "repository name");
        Expect.Equal(new DateTimeOffset(2026, 10, 4, 10, 0, 0, TimeSpan.Zero), list[0].Updated, "time");
        Expect.Throws<FormatException>(() => GitHubLogic.ParseNotifications("""{"message":"Bad credentials"}"""), "an error object is not a list");
        Expect.Throws<FormatException>(() => GitHubLogic.ParseNotifications("<html>"), "not json");
    }

    public static void WebUrlMappingCases()
    {
        Expect.Equal("https://github.com/o/r/commit/abc", GitHubLogic.WebUrl("https://api.github.com/repos/o/r/commits/abc", "Commit", null), "commit");
        Expect.Equal("https://github.com/o/r/actions", GitHubLogic.WebUrl("https://api.github.com/repos/o/r/check-suites/9", "CheckSuite", null), "check suite");
        Expect.Equal("https://github.com/notifications", GitHubLogic.WebUrl("https://evil.example/repos/o/r/pulls/1", "PullRequest", null), "foreign addresses are never followed");
        Expect.Equal("https://github.com/o/r/discussions/3", GitHubLogic.WebUrl("https://api.github.com/repos/o/r/discussions/3", "Discussion", null), "discussion");
    }

    public static void SearchResultsDistinguishPullRequestsAndIssues()
    {
        var (total, items) = GitHubLogic.ParseSearch(SearchResult);
        Expect.Equal(12, total, "total count");
        Expect.Equal(2, items.Count, "an item without a web link is skipped");
        Expect.True(items[0].IsPr && items[0].Draft && !items[1].IsPr, "pull request, draft and issue flags");
        Expect.Equal("octo-org/widgets", items[0].Repo, "repository from the api address");
        Expect.Equal("hubot", items[0].Author, "author");
        Expect.Equal(4, items[0].Comments, "comments");
        Expect.Equal("", items[1].Avatar, "missing avatar");
        Expect.Throws<FormatException>(() => GitHubLogic.ParseSearch("""{"message":"Validation Failed"}"""), "no items list");
    }

    public static void RepositoriesFromListOrObject()
    {
        string one = """{"full_name":"o/r","description":"Hello   there","stargazers_count":1500,"forks_count":3,"open_issues_count":2,"html_url":"https://github.com/o/r","pushed_at":"2026-10-04T10:00:00Z","language":null,"owner":{"avatar_url":"x"}}""";
        var single = GitHubLogic.ParseRepos(one);
        Expect.Equal(1, single.Count, "one object");
        Expect.True(single[0].Stars == 1500 && single[0].Description == "Hello there" && single[0].Language == "", "fields");
        Expect.Equal(2, GitHubLogic.ParseRepos("[" + one + "," + one + "]").Count, "a list");
        Expect.Throws<FormatException>(() => GitHubLogic.ParseRepos("42"), "neither");
    }

    public static void RepoFilterPatterns()
    {
        Expect.True(GitHubLogic.RepoMatches("any/thing", []), "no filter shows everything");
        Expect.True(GitHubLogic.RepoMatches("Octo-Org/Widgets", ["octo-org/widgets"]), "exact, case-insensitive");
        Expect.True(GitHubLogic.RepoMatches("octo-org/deck", ["octo-org/*"]), "owner wildcard");
        Expect.True(!GitHubLogic.RepoMatches("other/deck", ["octo-org/*"]), "other owner");
        Expect.True(GitHubLogic.RepoMatches("someone/deck", ["deck"]), "bare repo name");
        Expect.True(!GitHubLogic.RepoMatches("someone/desktop-deck", ["deck"]), "bare name matches the whole repo name only");
        Expect.Equal("a/b,c/d", string.Join(",", GitHubLogic.ExactRepos(["a/b", "https://github.com/c/d/", "owner/*", "bare", "a/b", "bad name/x"])), "exact repositories only");
    }

    public static void SearchQueriesAndUrls()
    {
        Expect.Equal("is:open is:pr review-requested:@me archived:false", GitHubLogic.SearchQuery("reviews", "issue"), "review requests are always pull requests");
        Expect.Equal("is:open assignee:@me archived:false is:issue", GitHubLogic.SearchQuery("assigned", "issue"), "assigned issues");
        Expect.Equal("is:open author:@me archived:false", GitHubLogic.SearchQuery("created", "both"), "created, both kinds");
        string url = GitHubLogic.SearchUrl("assigned", "pr", 500);
        Expect.True(url.StartsWith("https://api.github.com/search/issues?q=", StringComparison.Ordinal) && url.Contains("per_page=100", StringComparison.Ordinal) && url.Contains("%40me", StringComparison.Ordinal), "search url is escaped and capped");
        Expect.Equal(30, GitHubLogic.FetchCount(10, true), "asks for more when filtering");
        Expect.Equal(100, GitHubLogic.FetchCount(60, true), "capped at 100");
        Expect.Equal("https://api.github.com/notifications?per_page=50&participating=true", GitHubLogic.NotificationsUrl(80, true), "notifications url");
    }

    public static void ProblemWording()
    {
        Expect.True(GitHubLogic.Problem(401, null, "notifications").Contains("token", StringComparison.Ordinal), "401 mentions the token");
        Expect.True(GitHubLogic.Problem(403, "0", "reviews").Contains("rate limit", StringComparison.Ordinal), "rate limit");
        Expect.True(GitHubLogic.Problem(403, "12", "notifications").Contains("classic", StringComparison.Ordinal), "notifications need a classic token");
        Expect.True(GitHubLogic.Problem(500, null, "x").Length > 0, "server trouble");
    }

    public static void CacheRoundTripAndMarkRead()
    {
        var data = new GitHubLogic.Data { Notifications = GitHubLogic.ParseNotifications(Notifications), Unread = 3 };
        data.Totals["reviews"] = 7;
        data.Errors["assigned"] = "nope";
        var back = GitHubLogic.FromCache(GitHubLogic.ToCache(data));
        Expect.Equal(4, back.Notifications.Count, "notifications survive");
        Expect.Equal(7, back.Totals["reviews"], "totals survive");
        Expect.Equal("nope", back.Errors["assigned"], "errors survive");
        Expect.Equal(new DateTimeOffset(2026, 10, 4, 10, 0, 0, TimeSpan.Zero), back.Notifications[0].Updated, "dates survive");
        GitHubLogic.WithoutNotification(back, "1");
        Expect.Equal(3, back.Notifications.Count, "removed");
        Expect.Equal(2, GitHubLogic.UnreadCount(back), "unread count follows");
        GitHubLogic.WithoutNotification(back, "2");
        Expect.Equal(2, GitHubLogic.UnreadCount(back), "removing a read one keeps the count");
        Expect.Equal(0, GitHubLogic.FromCache("garbage").Notifications.Count, "bad cache is empty");
    }

    public static void AvatarsAskForSmallPictures()
    {
        Expect.Equal("https://avatars.githubusercontent.com/u/1?v=4&s=48", GitHubLogic.AvatarUrl("https://avatars.githubusercontent.com/u/1?v=4", 48), "size added");
        Expect.Equal("", GitHubLogic.AvatarUrl("https://example.com/a.png", 48), "foreign avatar refused");
        Expect.Equal("Review requested", GitHubLogic.ReasonText("review_requested"), "reason text");
    }
}
