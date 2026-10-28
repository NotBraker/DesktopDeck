using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopDeck.Models;

namespace DesktopDeck.Widgets;

/// <summary>
/// GitHub: unread notifications, review requests, issues and pull requests assigned to you or created by you, and a repo
/// list with stars. Needs a personal access token (kept encrypted in the secrets store). Parsing lives in GitHubLogic.cs
/// (tests/DesktopDeck.WidgetTests/GitHubTests.cs); caching and refreshing in ConnectedKit.cs.
/// </summary>
internal sealed class GitHubWidget : IWidget
{
    private const string TokenName = "github.token";
    private const string TokenPage = "https://github.com/settings/tokens/new?scopes=notifications,repo&description=Desktop%20Deck";
    private static readonly string[] SectionLabels = ["Inbox", "Reviews", "Assigned", "Created", "Repos"];

    public string Kind => "github";
    public string Name => "GitHub";
    public string Description => "Notifications, review requests, your pull requests and issues";
    public string Section => "Connected";
    public string Glyph => "";
    public string Accent => "#8b5cf6";
    public (double Width, double Height) DefaultSize => (324, 372);
    public (double Width, double Height) MinimumSize => (228, 228);

    public IReadOnlyList<OptionSpec> Options { get; } =
    [
        OptionSpec.Toggle("showNotifications", "Show notifications", true, "What to show", "Your unread notifications (needs the notifications scope)."),
        OptionSpec.Toggle("showReviews", "Show review requests", true, "What to show", "Open pull requests waiting for your review."),
        OptionSpec.Toggle("showAssigned", "Show items assigned to you", true, "What to show"),
        OptionSpec.Toggle("showCreated", "Show items you created", false, "What to show", "Your open pull requests and issues."),
        OptionSpec.Toggle("showRepos", "Show repositories with stars", false, "What to show", "The repositories listed below, or your most recently updated ones."),
        OptionSpec.Choice("types", "Assigned / created items", "both", ["both", "pr", "issue"], ["Pull requests and issues", "Pull requests only", "Issues only"], "What to show"),
        OptionSpec.Toggle("participating", "Only notifications that involve you", false, "What to show", "Skips activity on repositories you merely watch."),
        OptionSpec.Number("maxItems", "Items per list", 8, 1, GitHubLogic.MaxItems, "What to show"),
        OptionSpec.Lines("repos", "Repositories", "", "Repositories", "One per line, like owner/repo. Used as the list, and as a filter when the next switch is on."),
        OptionSpec.Toggle("onlyListed", "Only show items from these repositories", false, "Repositories"),
        OptionSpec.Choice("layout", "Layout", "tabs", ["tabs", "stacked"], ["Tabs", "All lists in one scroll"], "Layout"),
        OptionSpec.Choice("startTab", "Open on", "auto", ["auto", "notifications", "reviews", "assigned", "created", "repos"], ["First list", "Inbox", "Review requests", "Assigned", "Created", "Repositories"], "Layout"),
        OptionSpec.Toggle("compact", "Compact (one line per item)", false, "Layout"),
        OptionSpec.Toggle("showAvatars", "Show avatars", false, "Layout", "Loads small pictures from GitHub."),
        OptionSpec.Toggle("showRepoName", "Show the repository name", true, "Layout"),
        OptionSpec.Toggle("showAge", "Show how long ago", true, "Layout"),
        OptionSpec.Toggle("showComments", "Show comment counts", true, "Layout"),
        OptionSpec.Number("titleLines", "Lines per title", 2, 1, 3, "Layout"),
        OptionSpec.Number("titleSize", "Title size", 12.5, 10, 20, "Layout"),
        OptionSpec.Color("prColor", "Pull request color", "#3fb950", "Colors"),
        OptionSpec.Color("issueColor", "Issue color", "#58a6ff", "Colors"),
        OptionSpec.Color("draftColor", "Draft color", "#8b949e", "Colors"),
        OptionSpec.Color("unreadColor", "Unread count color", "", "Colors", "Empty = accent color."),
        OptionSpec.Number("refresh", "Refresh every (minutes)", 5, 2, 120, "Behavior", "GitHub allows plenty of requests; 2 minutes is the shortest."),
        OptionSpec.Toggle("markOnOpen", "Mark a notification as read when you open it", false, "Behavior"),
        OptionSpec.Toggle("showUpdated", "Show when it last updated", false, "Behavior"),
    ];

    // ---------- Samples ----------

    public IEnumerable<Action<LauncherItem>> Samples =>
    [
        item => Fill(item),
        item => { Fill(item); item.Options["layout"] = "stacked"; item.Options["compact"] = "true"; item.Options["showCreated"] = "true"; item.Options["showUpdated"] = "true"; item.Width = 372; item.Height = 420; },
        item => { Fill(item, TimeSpan.FromHours(3), "Couldn't connect. Check your connection."); item.Options["startTab"] = "reviews"; item.Options["showAvatars"] = "true"; item.Options["showRepos"] = "true"; item.Width = 324; item.Height = 324; },
        item => { Fill(item); item.Options["startTab"] = "repos"; item.Options["showRepos"] = "true"; item.Width = 276; item.Height = 372; },
    ];

    private static string Notif(int id, bool unread, string reason, int minutes, string title, string kind, string repo)
    {
        string path = kind switch { "PullRequest" => "pulls/" + (400 + id), "Issue" => "issues/" + (100 + id), "Commit" => "commits/9fceb02", _ => "releases/" + id };
        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["id"] = id.ToString(CultureInfo.InvariantCulture), ["unread"] = unread, ["reason"] = reason,
            ["updated_at"] = DateTimeOffset.UtcNow.AddMinutes(-minutes).ToString("o", CultureInfo.InvariantCulture),
            ["subject"] = new Dictionary<string, object> { ["title"] = title, ["url"] = $"https://api.github.com/repos/{repo}/{path}", ["type"] = kind },
            ["repository"] = new Dictionary<string, object>
            {
                ["full_name"] = repo, ["html_url"] = "https://github.com/" + repo,
                ["owner"] = new Dictionary<string, object> { ["avatar_url"] = $"https://avatars.githubusercontent.com/u/{id}?v=4" },
            },
        });
    }

    private static string Hit(int number, string title, string repo, bool pr, bool draft, string author, int minutes, int comments)
    {
        var hit = new Dictionary<string, object>
        {
            ["number"] = number, ["title"] = title, ["html_url"] = $"https://github.com/{repo}/{(pr ? "pull" : "issues")}/{number}", ["repository_url"] = "https://api.github.com/repos/" + repo,
            ["draft"] = draft, ["user"] = new Dictionary<string, object> { ["login"] = author, ["avatar_url"] = $"https://avatars.githubusercontent.com/u/{number}?v=4" },
            ["updated_at"] = DateTimeOffset.UtcNow.AddMinutes(-minutes).ToString("o", CultureInfo.InvariantCulture), ["comments"] = comments,
        };
        if (pr) hit["pull_request"] = new Dictionary<string, object>();
        return JsonSerializer.Serialize(hit);
    }

    private static string Search(params string[] hits) => "{\"total_count\":" + (hits.Length + 3) + ",\"items\":[" + string.Join(",", hits) + "]}";

    /// <summary>Made-up API answers run through the real parsers.</summary>
    private static void Fill(LauncherItem item, TimeSpan? age = null, string? error = null)
    {
        var data = new GitHubLogic.Data
        {
            Notifications = GitHubLogic.ParseNotifications("[" + string.Join(",",
                Notif(1, true, "review_requested", 12, "Add dark mode to the settings page", "PullRequest", "octo-org/widgets"),
                Notif(2, true, "mention", 47, "Crash when the clipboard is empty", "Issue", "octo-org/deck"),
                Notif(3, true, "ci_activity", 95, "Release pipeline: build failed on main", "Issue", "octo-org/infra"),
                Notif(4, true, "subscribed", 190, "v2.4.0", "Release", "octo-org/widgets"),
                Notif(5, true, "assign", 410, "Speed up the first paint of the dashboard", "PullRequest", "monalisa/dashboard"),
                Notif(6, true, "comment", 1500, "Typo in the install guide", "Issue", "octo-org/docs")) + "]"),
            Unread = 9,
            Reviews = GitHubLogic.ParseSearch(Search(
                Hit(482, "Add dark mode to the settings page", "octo-org/widgets", true, false, "hubot", 12, 4),
                Hit(37, "Draft: migrate the build to .NET 10", "octo-org/deck", true, true, "ada", 140, 0),
                Hit(1203, "Fix flicker when resizing a tile", "monalisa/dashboard", true, false, "linus", 600, 11))).Items,
            Assigned = GitHubLogic.ParseSearch(Search(
                Hit(77, "Crash when the clipboard is empty", "octo-org/deck", false, false, "grace", 47, 6),
                Hit(512, "Speed up the first paint of the dashboard", "monalisa/dashboard", true, false, "ken", 410, 2),
                Hit(9, "Document the plug-in settings", "octo-org/docs", false, false, "barbara", 2900, 0))).Items,
            Created = GitHubLogic.ParseSearch(Search(
                Hit(481, "Keyboard shortcuts for the quick-add box", "octo-org/widgets", true, false, "me", 260, 1),
                Hit(76, "Idea: pin a widget to every monitor", "octo-org/deck", false, false, "me", 4300, 3))).Items,
            Repos = GitHubLogic.ParseRepos("[" +
                """{"full_name":"octo-org/widgets","description":"Small, fast widgets for your desktop","stargazers_count":1284,"forks_count":96,"open_issues_count":14,"html_url":"https://github.com/octo-org/widgets","pushed_at":"2026-10-04T18:00:00Z","language":"C#","owner":{"avatar_url":"https://avatars.githubusercontent.com/u/1?v=4"}},""" +
                """{"full_name":"octo-org/deck","description":"A dashboard for your desktop","stargazers_count":342,"forks_count":18,"open_issues_count":5,"html_url":"https://github.com/octo-org/deck","pushed_at":"2026-10-03T09:00:00Z","language":"C#","owner":{"avatar_url":"https://avatars.githubusercontent.com/u/2?v=4"}},""" +
                """{"full_name":"monalisa/dashboard","description":"","stargazers_count":57,"forks_count":3,"open_issues_count":0,"html_url":"https://github.com/monalisa/dashboard","pushed_at":"2026-09-20T09:00:00Z","language":"TypeScript","owner":{"avatar_url":"https://avatars.githubusercontent.com/u/3?v=4"}}""" + "]"),
        };
        data.Totals["reviews"] = 3; data.Totals["assigned"] = 3; data.Totals["created"] = 2;
        ConnectedKit.Fill(item, GitHubLogic.ToCache(data), "", age ?? TimeSpan.FromMinutes(2), error);
        if (error is not null) item.Options[ConnectedKit.CacheErrorKind] = "net";
    }

    // ---------- Settings ----------

    private static string[] Enabled(WidgetContext ctx)
    {
        var keys = new Dictionary<string, string> { ["notifications"] = "showNotifications", ["reviews"] = "showReviews", ["assigned"] = "showAssigned", ["created"] = "showCreated", ["repos"] = "showRepos" };
        return GitHubLogic.Sections.Where(s => ctx.GetBool(keys[s])).ToArray();
    }

    private static string[] Filter(WidgetContext ctx) => ctx.GetBool("onlyListed") ? ctx.GetLines("repos") : [];

    private static ConnectedSource Source(WidgetContext ctx)
    {
        var sections = Enabled(ctx);
        int max = ctx.GetInt("maxItems");
        string types = ctx.Get("types");
        bool participating = ctx.GetBool("participating");
        var filter = Filter(ctx);
        var listed = GitHubLogic.ExactRepos(ctx.GetLines("repos"));
        string signature = string.Join(",", sections) + "/" + types + "/" + max + "/" + participating + "/" + string.Join("|", filter) + "/" + string.Join("|", listed);
        return new ConnectedSource(signature, OnlineLogic.Interval(ctx.GetNumber("refresh"), 2), () => FetchAsync(ctx, sections, max, types, participating, filter, listed), Strict: false);
    }

    private static HttpRequestMessage Request(HttpMethod method, string url, string token)
    {
        // The token only ever goes to GitHub's API.
        if (!url.StartsWith(GitHubLogic.Api + "/", StringComparison.Ordinal)) throw new InvalidOperationException("Refusing to send the token elsewhere.");
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", GitHubLogic.ApiVersion);
        return request;
    }

    private static async Task<string> FetchAsync(WidgetContext ctx, string[] sections, int max, string types, bool participating, string[] filter, List<string> listed)
    {
        string token = ctx.GetSecret(TokenName) ?? throw new ConnectedException("Connect GitHub with a personal access token.", 401, auth: true);
        string who = "gh:" + OnlineLogic.Hash(token) + ":";
        var previous = GitHubLogic.FromCache(ctx.Item.Options.GetValueOrDefault(ConnectedKit.CacheJson));
        var data = new GitHubLogic.Data { Notifications = previous.Notifications, Reviews = previous.Reviews, Assigned = previous.Assigned, Created = previous.Created, Repos = previous.Repos, Unread = previous.Unread, Totals = previous.Totals };
        var errors = new Dictionary<string, string>();
        int fetch = GitHubLogic.FetchCount(max, filter.Length > 0);

        async Task<string> Get(string section, string url)
        {
            var reply = await ConnectedKit.GetConditionalAsync(who + url, () => Request(HttpMethod.Get, url, token));
            if (!reply.Ok) throw new ConnectedException(GitHubLogic.Problem(reply.Status, reply.RateRemaining, section), reply.Status, auth: reply.Status == 401);
            return reply.Body;
        }

        async Task Load(string section)
        {
            try
            {
                switch (section)
                {
                    case "notifications":
                        var all = GitHubLogic.ParseNotifications(await Get(section, GitHubLogic.NotificationsUrl(50, participating)));
                        var shown = all.Where(n => GitHubLogic.RepoMatches(n.Repo, filter)).ToList();
                        data.Unread = shown.Count(n => n.Unread);
                        data.Notifications = shown.Take(max).ToList();
                        break;
                    case "reviews" or "assigned" or "created":
                        var (total, items) = GitHubLogic.ParseSearch(await Get(section, GitHubLogic.SearchUrl(section, types, fetch)));
                        var kept = GitHubLogic.Filter(items, filter, max);
                        data.Totals[section] = filter.Length > 0 ? kept.Count : total;
                        if (section == "reviews") data.Reviews = kept; else if (section == "assigned") data.Assigned = kept; else data.Created = kept;
                        break;
                    case "repos":
                        var repos = new List<GitHubLogic.Repo>();
                        if (listed.Count > 0)
                        {
                            foreach (var name in listed.Take(max))
                            {
                                try { repos.AddRange(GitHubLogic.ParseRepos(await Get(section, GitHubLogic.RepoUrl(name)))); }
                                catch (ConnectedException ex) when (ex.Status == 404) { }
                            }
                        }
                        else repos.AddRange(GitHubLogic.ParseRepos(await Get(section, GitHubLogic.MyReposUrl(max))));
                        data.Repos = repos.Take(max).ToList();
                        break;
                }
            }
            catch (ConnectedException ex) when (ex.Status != 401) { errors[section] = ex.Message; }
        }

        await Task.WhenAll(sections.Select(Load));
        if (sections.Length > 0 && errors.Count == sections.Length) throw new ConnectedException(errors.Values.First(), null);
        data.Errors = errors;
        return GitHubLogic.ToCache(data);
    }

    private static void Connect(WidgetContext ctx)
    {
        string? token = ctx.AskSecret("Connect GitHub",
            "Paste a personal access token. Create a classic token at github.com/settings/tokens with the notifications and repo scopes (repo lets it read your private pull requests and issues; fine-grained tokens can't read notifications). It is saved encrypted for your Windows account only.");
        if (token is null) return;
        ctx.SetSecret(TokenName, token);
        ctx.Item.Options.Remove(ConnectedKit.CacheError);
        ctx.Item.Options.Remove(ConnectedKit.CacheErrorKind);
        ctx.Changed();
        ConnectedKit.Refresh(ctx, Source(ctx), force: true);
    }

    public IEnumerable<WidgetAction> Actions(WidgetContext ctx)
    {
        if (ctx.HasSecret(TokenName))
        {
            yield return ConnectedKit.RefreshAction(ctx, Source(ctx));
            yield return new WidgetAction("Open GitHub notifications", "", () => ctx.Open("https://github.com/notifications"));
            yield return new WidgetAction("Use a different access key…", "", () => Connect(ctx));
            yield return new WidgetAction("Disconnect GitHub", "", () => ConnectedKit.Disconnect(ctx, TokenName, "GitHub"));
        }
        else yield return new WidgetAction("Connect GitHub…", "", () => Connect(ctx));
    }

    // ---------- Drawing ----------

    public FrameworkElement Build(WidgetContext ctx)
    {
        var source = Source(ctx);
        string json = ConnectedKit.Cached(ctx);
        bool connected = ctx.IsSnapshot ? json.Length > 0 : ctx.HasSecret(TokenName);
        if (!connected)
        {
            ConnectedKit.Watch(ctx, source);
            var (setupCard, setupBody) = WidgetKit.Frame(ctx);
            var setup = ConnectedKit.SetupPanel(ctx, "", "Connect GitHub",
                "See your notifications, review requests and issues. Needs a classic token with the notifications and repo scopes; it stays encrypted on this PC.",
                "Connect GitHub…", () => Connect(ctx));
            var link = WidgetKit.Muted(ctx, "Create a token on github.com", 11);
            link.Margin = new Thickness(0, 8, 0, 0);
            WidgetKit.Clickable(link, () => ctx.Open(TokenPage), TokenPage);
            setup.Children.Add(link);
            setupBody.Children.Add(WidgetKit.Scroll(setup));
            return setupCard;
        }

        var sections = Enabled(ctx);
        var data = GitHubLogic.FromCache(json);
        int unread = GitHubLogic.UnreadCount(data);
        FrameworkElement? badge = sections.Contains("notifications") && unread > 0 ? WidgetKit.Pill(ctx, ConnectedLogic.Count(unread, 50), ctx.Get("unreadColor").Length > 0 ? ctx.Get("unreadColor") : ctx.Item.Accent) : null;
        var refresh = WidgetKit.IconButton("", "Refresh now", () => ConnectedKit.Refresh(ctx, source, force: true), 24);
        var (card, body) = WidgetKit.Frame(ctx, badge, refresh);
        ConnectedKit.Keep(ctx, source, tick: _ => ctx.Rebuild());
        ConnectedKit.AddFooter(ctx, body, ctx.GetBool("showUpdated"));

        if (sections.Length == 0) { body.Children.Add(WidgetKit.Empty(ctx, "Pick what to show in Customize…")); return card; }
        if (ConnectedKit.Fetched(ctx) is null)
        {
            body.Children.Add(ConnectedKit.Error(ctx) is { } problem
                ? ConnectedKit.Setup(ctx, "", "Can't reach GitHub", problem, ConnectedKit.ErrorIsAuth(ctx) ? "Use a different access key…" : "Try again", ConnectedKit.ErrorIsAuth(ctx) ? () => Connect(ctx) : () => ConnectedKit.Refresh(ctx, source, force: true), Theme.Danger)
                : ConnectedKit.Loading(ctx, "Checking GitHub…"));
            return card;
        }

        var now = DateTimeOffset.Now;
        var panel = new StackPanel();
        string wanted = ctx.State("tab", "");
        if (!sections.Contains(wanted)) wanted = sections.Contains(ctx.Get("startTab")) ? ctx.Get("startTab") : sections[0];
        bool tabs = ctx.Get("layout") == "tabs";
        if (tabs)
        {
            if (sections.Length > 1) panel.Children.Add(TabBar(ctx, sections, wanted, data));
            AddSection(ctx, panel, wanted, data, now, header: false);
        }
        else foreach (string section in sections) AddSection(ctx, panel, section, data, now, header: true);
        body.Children.Add(WidgetKit.Scroll(panel));
        return card;
    }

    private static int CountOf(WidgetContext ctx, string section, GitHubLogic.Data data)
    {
        int shown = section switch { "notifications" => data.Notifications.Count, "reviews" => data.Reviews.Count, "assigned" => data.Assigned.Count, "created" => data.Created.Count, _ => data.Repos.Count };
        if (section == "notifications") return Math.Max(GitHubLogic.UnreadCount(data), shown);
        return data.Totals.TryGetValue(section, out int total) && total > shown ? total : shown;
    }

    private static FrameworkElement TabBar(WidgetContext ctx, string[] sections, string selected, GitHubLogic.Data data)
    {
        var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        foreach (string section in sections)
        {
            bool on = section == selected;
            int count = CountOf(ctx, section, data);
            var text = new StackPanel { Orientation = Orientation.Horizontal };
            text.Children.Add(WidgetKit.Text(ctx, SectionLabels[Array.IndexOf(GitHubLogic.Sections, section)], 11.5, on ? ctx.TextBrush : Theme.Muted, bold: true));
            if (count > 0 && section != "repos")
            {
                var number = WidgetKit.Text(ctx, ConnectedLogic.Count(count, 100), 10.5, on ? ctx.Accent : Theme.Dim, bold: true);
                number.Margin = new Thickness(5, 1, 0, 0);
                text.Children.Add(number);
            }
            var tab = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 3, 9, 4), Margin = new Thickness(0, 0, 4, 4), Background = on ? Theme.Tint(ctx.Item.Accent, 0.22) : Theme.Surface3, Child = text };
            string target = section;
            WidgetKit.Clickable(tab, () => ctx.SetState("tab", target));
            wrap.Children.Add(tab);
        }
        return wrap;
    }

    private static void AddSection(WidgetContext ctx, StackPanel panel, string section, GitHubLogic.Data data, DateTimeOffset now, bool header)
    {
        var rows = new List<FrameworkElement>();
        string empty;
        switch (section)
        {
            case "notifications":
                rows.AddRange(data.Notifications.Select(n => NotificationRow(ctx, n, now)));
                empty = "You're all caught up.";
                break;
            case "reviews":
                rows.AddRange(data.Reviews.Select(i => ItemRow(ctx, i, now)));
                empty = "No review requests.";
                break;
            case "assigned":
                rows.AddRange(data.Assigned.Select(i => ItemRow(ctx, i, now)));
                empty = "Nothing is assigned to you.";
                break;
            case "created":
                rows.AddRange(data.Created.Select(i => ItemRow(ctx, i, now)));
                empty = "You have nothing open.";
                break;
            default:
                rows.AddRange(data.Repos.Select(r => RepoRow(ctx, r, now)));
                empty = "No repositories to show.";
                break;
        }
        if (header)
        {
            var title = WidgetKit.Text(ctx, SectionLabels[Array.IndexOf(GitHubLogic.Sections, section)].ToUpperInvariant() + (rows.Count > 0 && section != "repos" ? "  " + CountOf(ctx, section, data).ToString(CultureInfo.CurrentCulture) : ""), 10, Theme.Muted, bold: true);
            title.Margin = new Thickness(6, panel.Children.Count == 0 ? 0 : 10, 0, 2);
            panel.Children.Add(title);
        }
        if (data.Errors.TryGetValue(section, out string? problem))
        {
            var note = WidgetKit.Text(ctx, problem, 11, Theme.Danger);
            note.TextWrapping = TextWrapping.Wrap;
            note.Margin = new Thickness(6, 2, 6, 4);
            panel.Children.Add(note);
        }
        if (rows.Count == 0 && !data.Errors.ContainsKey(section))
        {
            var note = WidgetKit.Muted(ctx, empty, 12);
            note.Margin = new Thickness(6, 4, 6, 4);
            panel.Children.Add(note);
        }
        foreach (var row in rows) panel.Children.Add(row);
    }

    // ---------- Rows ----------

    private static Brush KindBrush(WidgetContext ctx, string kind, bool draft) => draft ? ctx.GetColor("draftColor", WidgetKit.Color("#8b949e"))
        : kind is "PullRequest" or "PR" ? ctx.GetColor("prColor", WidgetKit.Color("#3fb950"))
        : kind is "Issue" ? ctx.GetColor("issueColor", WidgetKit.Color("#58a6ff")) : Theme.Muted;

    private static string KindLabel(string type) => type switch { "PullRequest" => "PR", "Issue" => "Issue", "CheckSuite" => "CI", "Discussion" => "Discussion", "" => "", _ => type };

    private static FrameworkElement NotificationRow(WidgetContext ctx, GitHubLogic.Notification n, DateTimeOffset now)
    {
        var parts = new List<(string Text, Brush Color, bool Bold)> { (KindLabel(n.Type), KindBrush(ctx, n.Type, false), true) };
        if (ctx.GetBool("showRepoName") && n.Repo.Length > 0) parts.Add((n.Repo, Theme.Muted, false));
        if (GitHubLogic.ReasonText(n.Reason) is { Length: > 0 } reason) parts.Add((reason, Theme.Dim, false));
        string age = n.Updated == DateTimeOffset.MinValue ? "" : OnlineLogic.Age(n.Updated, now);
        string id = n.Id, url = n.Url;
        void Open()
        {
            if (OnlineLogic.IsWebLink(url)) ctx.Open(url);
            if (ctx.GetBool("markOnOpen")) MarkRead(ctx, id);
        }
        var menu = MainWindow.Menu();
        MainWindow.AddMenuItem(menu, "Open", Open, "");
        MainWindow.AddMenuItem(menu, "Mark as read", () => MarkRead(ctx, id), "");
        MainWindow.AddMenuItem(menu, "Copy link", () => CopyLink(ctx, url), "");
        return RowView(ctx, n.Title, parts, age, KindBrush(ctx, n.Type, false), n.Avatar, n.Repo, n.Unread, n.Title + "\n" + n.Repo + (age.Length > 0 ? "  ·  " + age + " ago" : "") + "\n\nClick: open  ·  Right-click: mark as read", Open, menu);
    }

    private static FrameworkElement ItemRow(WidgetContext ctx, GitHubLogic.Item i, DateTimeOffset now)
    {
        var parts = new List<(string Text, Brush Color, bool Bold)> { (i.IsPr ? (i.Draft ? "Draft PR" : "PR") : "Issue", KindBrush(ctx, i.IsPr ? "PR" : "Issue", i.Draft), true) };
        parts.Add(((ctx.GetBool("showRepoName") && i.Repo.Length > 0 ? i.Repo : "") + "#" + i.Number.ToString(CultureInfo.InvariantCulture), Theme.Muted, false));
        if (i.Author.Length > 0 && !string.Equals(i.Author, "me", StringComparison.Ordinal)) parts.Add(("by " + i.Author, Theme.Dim, false));
        if (ctx.GetBool("showComments") && i.Comments > 0) parts.Add((i.Comments.ToString(CultureInfo.CurrentCulture) + (i.Comments == 1 ? " comment" : " comments"), Theme.Dim, false));
        string age = i.Updated == DateTimeOffset.MinValue ? "" : OnlineLogic.Age(i.Updated, now);
        string url = i.Url;
        void Open() { if (OnlineLogic.IsWebLink(url)) ctx.Open(url); }
        var menu = MainWindow.Menu();
        MainWindow.AddMenuItem(menu, "Open", Open, "");
        MainWindow.AddMenuItem(menu, "Copy link", () => CopyLink(ctx, url), "");
        return RowView(ctx, i.Title, parts, age, KindBrush(ctx, i.IsPr ? "PR" : "Issue", i.Draft), i.Avatar, i.Author, false, i.Title + "\n" + i.Repo + " #" + i.Number + (age.Length > 0 ? "  ·  " + age + " ago" : ""), Open, menu);
    }

    private static FrameworkElement RepoRow(WidgetContext ctx, GitHubLogic.Repo r, DateTimeOffset now)
    {
        var parts = new List<(string Text, Brush Color, bool Bold)>();
        if (r.Description.Length > 0) parts.Add((r.Description, Theme.Muted, false));
        if (r.Language.Length > 0) parts.Add((r.Language, Theme.Dim, false));
        if (r.OpenIssues > 0) parts.Add((r.OpenIssues.ToString("N0", CultureInfo.CurrentCulture) + " open", Theme.Dim, false));
        string url = r.Url;
        void Open() { if (OnlineLogic.IsWebLink(url)) ctx.Open(url); }
        var menu = MainWindow.Menu();
        MainWindow.AddMenuItem(menu, "Open", Open, "");
        MainWindow.AddMenuItem(menu, "Copy link", () => CopyLink(ctx, url), "");
        string stars = "★ " + OnlineLogic.Compact(r.Stars);
        return RowView(ctx, ctx.GetBool("showRepoName") ? r.Name : r.Name[(r.Name.IndexOf('/') + 1)..], parts, stars, ctx.GetColor("unreadColor"), r.Avatar, r.Name, false,
            r.Name + "\n" + r.Description + "\n" + r.Stars.ToString("N0", CultureInfo.CurrentCulture) + " stars  ·  " + r.Forks.ToString("N0", CultureInfo.CurrentCulture) + " forks", Open, menu, rightBrush: ctx.GetColor("prColor", WidgetKit.Color("#3fb950")), single: true);
    }

    /// <summary>One list row: a lead (avatar or colored dot), the title, a small line of parts, and a right-hand note (the age, or the stars).</summary>
    private static FrameworkElement RowView(WidgetContext ctx, string title, List<(string Text, Brush Color, bool Bold)> parts, string right, Brush dot, string avatarUrl, string avatarName,
        bool bold, string tooltip, Action open, ContextMenu menu, Brush? rightBrush = null, bool single = false)
    {
        bool compact = ctx.GetBool("compact");
        double titleSize = ctx.GetNumber("titleSize");
        bool avatars = ctx.GetBool("showAvatars");
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        FrameworkElement lead;
        if (avatars) lead = ConnectedKit.Avatar(ctx, GitHubLogic.AvatarUrl(avatarUrl, 48), avatarName, compact ? 18 : 24);
        else lead = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = dot, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(1, compact ? 5 : 6, 0, 0) };
        lead.Margin = new Thickness(lead.Margin.Left, lead.Margin.Top, 9, 0);
        if (avatars) lead.VerticalAlignment = compact ? VerticalAlignment.Center : VerticalAlignment.Top;
        grid.Children.Add(lead);

        var stack = new StackPanel();
        Grid.SetColumn(stack, 1);
        var titleText = WidgetKit.Text(ctx, title, titleSize, ctx.TextBrush, bold: bold || single);
        if (!compact && !single)
        {
            titleText.TextWrapping = TextWrapping.Wrap;
            titleText.TextTrimming = TextTrimming.WordEllipsis;
            titleText.LineHeight = Math.Round(ctx.Font(titleSize) * 1.3);
            titleText.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            titleText.MaxHeight = titleText.LineHeight * ctx.GetInt("titleLines") + 1;
        }
        stack.Children.Add(titleText);
        if (!compact && parts.Count > 0)
        {
            var line = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
            if (single)
            {
                // Repositories: description on its own trimmed line, the rest small.
                var first = parts[0];
                var description = WidgetKit.Text(ctx, first.Text, 10.5, first.Color);
                stack.Children.Add(description);
                parts = parts.Skip(1).ToList();
                line.Margin = new Thickness(0, 1, 0, 0);
                if (parts.Count == 0) line = new WrapPanel();
            }
            foreach (var (text, color, isBold) in parts)
            {
                if (text.Length == 0) continue;
                var piece = WidgetKit.Text(ctx, text, 10.5, color, isBold);
                piece.Margin = new Thickness(0, 0, 8, 0);
                line.Children.Add(piece);
            }
            if (line.Children.Count > 0) stack.Children.Add(line);
        }
        grid.Children.Add(stack);
        if (right.Length > 0)
        {
            var note = WidgetKit.Text(ctx, right, 10.5, rightBrush ?? Theme.Dim, bold: rightBrush is not null);
            note.Margin = new Thickness(8, compact ? 0 : 2, 0, 0);
            note.VerticalAlignment = compact ? VerticalAlignment.Center : VerticalAlignment.Top;
            Grid.SetColumn(note, 2);
            grid.Children.Add(note);
        }
        var row = WidgetKit.Row(grid);
        row.Padding = compact ? new Thickness(6, 3, 6, 3) : new Thickness(6, 5, 6, 5);
        row.ToolTip = tooltip;
        row.ContextMenu = menu;
        WidgetKit.Clickable(row, open);
        return row;
    }

    // ---------- Actions on GitHub ----------

    private static void CopyLink(WidgetContext ctx, string url)
    {
        try { Clipboard.SetText(url); ctx.Status("Link copied."); }
        catch (System.Runtime.InteropServices.COMException) { ctx.Status("The clipboard is busy; try again."); }
    }

    /// <summary>PATCH /notifications/threads/{id}: mark one notification as read, then drop it from the cache.</summary>
    private static void MarkRead(WidgetContext ctx, string id)
    {
        ctx.RunOnce("github:read:" + id, async () =>
        {
            string? token = ctx.GetSecret(TokenName);
            if (token is null) return;
            try
            {
                var reply = await ConnectedKit.SendAsync(Request(new HttpMethod("PATCH"), GitHubLogic.ThreadUrl(id), token));
                if (!reply.Ok) { ctx.Status(GitHubLogic.Problem(reply.Status, reply.RateRemaining, "notifications")); return; }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { ctx.Status("Couldn't reach GitHub to mark it as read."); return; }
            var data = GitHubLogic.WithoutNotification(GitHubLogic.FromCache(ctx.Item.Options.GetValueOrDefault(ConnectedKit.CacheJson)), id);
            ConnectedKit.ForgetConditional("gh:");
            ctx.Set(ConnectedKit.CacheJson, GitHubLogic.ToCache(data));
        });
    }
}
