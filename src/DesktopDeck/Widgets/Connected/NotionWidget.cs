using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopDeck.Models;

namespace DesktopDeck.Widgets;

/// <summary>
/// Notion: items from one database (title, status, date) and a quick-add box that creates a new page in it.
/// Needs an internal integration token (kept encrypted in the secrets store) and the database shared with that integration.
/// Parsing and request building live in NotionLogic.cs (tests/DesktopDeck.WidgetTests/NotionTests.cs).
/// </summary>
internal sealed class NotionWidget : IWidget
{
    private const string TokenName = "notion.token";
    private const string IntegrationsPage = "https://www.notion.so/my-integrations";

    public string Kind => "notion";
    public string Name => "Notion";
    public string Description => "Items from a Notion database, and a quick add";
    public string Section => "Connected";
    public string Glyph => "";
    public string Accent => "#e8e4da";
    public (double Width, double Height) DefaultSize => (324, 372);
    public (double Width, double Height) MinimumSize => (228, 204);

    public IReadOnlyList<OptionSpec> Options { get; } =
    [
        OptionSpec.Text("database", "Database link or ID", "", "Database", "Paste the database's link from Notion (Share, Copy link) or its id. Share the database with your integration first."),
        OptionSpec.Text("statusProperty", "Status property", "", "Database", "Empty = detect (a Status property, or a Select called Status/State/Stage, or a Done checkbox)."),
        OptionSpec.Text("dateProperty", "Date property", "", "Database", "Empty = detect (a Date property, preferring one called Due)."),
        OptionSpec.Number("maxItems", "Items to show", 10, 1, NotionLogic.MaxItems, "What to show"),
        OptionSpec.Toggle("hideDone", "Hide done items", true, "What to show", "Hides items whose status is in Done/Complete."),
        OptionSpec.Choice("sort", "Order", "date-asc", ["date-asc", "date-desc", "edited", "created"], ["Date, soonest first", "Date, latest first", "Recently edited", "Recently created"], "What to show"),
        OptionSpec.Toggle("showStatus", "Show the status", true, "What to show"),
        OptionSpec.Toggle("showDates", "Show dates", true, "What to show"),
        OptionSpec.Toggle("showTitle", "Show the database name", true, "What to show"),
        OptionSpec.Toggle("groupByStatus", "Group by status", false, "Layout", "Groups follow the order of the status options in Notion."),
        OptionSpec.Toggle("compact", "Compact (one line per item)", false, "Layout"),
        OptionSpec.Number("titleSize", "Title size", 12.5, 10, 20, "Layout"),
        OptionSpec.Number("titleLines", "Lines per title", 2, 1, 3, "Layout"),
        OptionSpec.Toggle("showDot", "Show a colored dot", true, "Layout"),
        OptionSpec.Toggle("statusColors", "Use Notion's status colors", true, "Colors", "Off = every status uses the accent color."),
        OptionSpec.Color("overdueColor", "Overdue color", "#ff7369", "Colors"),
        OptionSpec.Color("todayColor", "Due today color", "#ffa344", "Colors"),
        OptionSpec.Toggle("showAdd", "Show the + button", true, "Quick add"),
        OptionSpec.Text("addStatus", "Status for new items", "", "Quick add", "Must match an option exactly, e.g. Not started. Empty = Notion's default."),
        OptionSpec.Choice("addDate", "Date for new items", "none", ["none", "today"], ["No date", "Today"], "Quick add"),
        OptionSpec.Number("refresh", "Refresh every (minutes)", 5, 2, 120, "Behavior"),
        OptionSpec.Toggle("showUpdated", "Show when it last updated", false, "Behavior"),
    ];

    // ---------- Samples ----------

    public IEnumerable<Action<LauncherItem>> Samples =>
    [
        item => Fill(item),
        item => { Fill(item); item.Options["groupByStatus"] = "true"; item.Options["hideDone"] = "false"; item.Options["showUpdated"] = "true"; item.Width = 372; item.Height = 420; },
        item => { Fill(item, TimeSpan.FromHours(2), "Couldn't connect. Check your connection."); item.Options["compact"] = "true"; item.Options["sort"] = "edited"; item.Width = 324; item.Height = 276; },
    ];

    private const string SampleId = "0a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d";

    private static string Page(string title, string? status, string color, int? dueInDays, int minutesAgo)
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = Guid.NewGuid().ToString(), ["archived"] = false,
            ["url"] = "https://www.notion.so/" + title.Replace(' ', '-') + "-0123456789abcdef0123456789abcdef",
            ["last_edited_time"] = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo).ToString("o", CultureInfo.InvariantCulture),
            ["properties"] = new Dictionary<string, object?>
            {
                ["Name"] = new Dictionary<string, object> { ["type"] = "title", ["title"] = new object[] { new Dictionary<string, object> { ["plain_text"] = title } } },
                ["Status"] = new Dictionary<string, object?> { ["type"] = "status", ["status"] = status is null ? null : new Dictionary<string, object> { ["name"] = status, ["color"] = color } },
                ["Due"] = new Dictionary<string, object?> { ["type"] = "date", ["date"] = dueInDays is null ? null : new Dictionary<string, object?> { ["start"] = DateTime.Today.AddDays(dueInDays.Value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["end"] = null } },
            },
        });

    private static void Fill(LauncherItem item, TimeSpan? age = null, string? error = null)
    {
        const string schemaJson = """
            {"object":"database","title":[{"plain_text":"Launch plan"}],"properties":{
              "Name":{"id":"title","type":"title","title":{}},
              "Status":{"id":"s","type":"status","status":{"options":[{"id":"a","name":"Not started","color":"default"},{"id":"b","name":"In progress","color":"blue"},{"id":"c","name":"Blocked","color":"red"},{"id":"d","name":"Done","color":"green"}],"groups":[{"id":"g1","name":"To-do","option_ids":["a"]},{"id":"g2","name":"In progress","option_ids":["b","c"]},{"id":"g3","name":"Complete","option_ids":["d"]}]}},
              "Due":{"id":"dd","type":"date","date":{}},"Tags":{"id":"t","type":"multi_select","multi_select":{"options":[]}}}}
            """;
        var schema = NotionLogic.DetectSchema(schemaJson);
        string results = "{\"results\":[" + string.Join(",",
            Page("Write the release notes", "In progress", "blue", 0, 25),
            Page("Book the venue for the demo day", "Not started", "default", 2, 90),
            Page("Fix the signup email template", "Blocked", "red", -2, 200),
            Page("Record the product walkthrough", "In progress", "blue", 5, 410),
            Page("Review pricing with the team", "Not started", "default", 9, 800),
            Page("Send invites to early users", "Done", "green", -4, 1500),
            Page("Collect testimonials", null, "default", null, 3000),
            Page("Update the screenshots on the site", "Not started", "default", 21, 5200)) + "]}";
        var data = new NotionLogic.Data { Schema = schema, Rows = NotionLogic.ParseRows(results, schema) };
        item.Options["database"] = SampleId;
        ConnectedKit.Fill(item, NotionLogic.ToCache(data), "", age ?? TimeSpan.FromMinutes(2), error);
        if (error is not null) item.Options[ConnectedKit.CacheErrorKind] = "net";
    }

    // ---------- Requests ----------

    private static string? DatabaseId(WidgetContext ctx) => NotionLogic.ExtractId(ctx.Get("database"));

    private static ConnectedSource Source(WidgetContext ctx, string databaseId)
    {
        string status = ctx.Get("statusProperty").Trim(), date = ctx.Get("dateProperty").Trim(), sort = ctx.Get("sort");
        bool hideDone = ctx.GetBool("hideDone");
        int max = ctx.GetInt("maxItems");
        string signature = string.Join("/", databaseId, status, date, hideDone, sort, max);
        return new ConnectedSource(signature, OnlineLogic.Interval(ctx.GetNumber("refresh"), 2), () => FetchAsync(ctx, databaseId, status, date, hideDone, sort, max));
    }

    private static HttpRequestMessage Request(HttpMethod method, string url, string token, string? json = null)
    {
        // The token only ever goes to Notion's API.
        if (!url.StartsWith(NotionLogic.Api + "/", StringComparison.Ordinal)) throw new InvalidOperationException("Refusing to send the token elsewhere.");
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        request.Headers.TryAddWithoutValidation("Notion-Version", NotionLogic.Version);
        if (json is not null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }

    private static async Task<string> Send(HttpRequestMessage request)
    {
        var reply = await ConnectedKit.SendAsync(request);
        if (!reply.Ok) throw new ConnectedException(NotionLogic.Problem(reply.Status, reply.Body), reply.Status, auth: reply.Status == 401);
        return reply.Body;
    }

    private static async Task<string> FetchAsync(WidgetContext ctx, string databaseId, string statusName, string dateName, bool hideDone, string sort, int max)
    {
        string token = ctx.GetSecret(TokenName) ?? throw new ConnectedException("Connect Notion with an integration token.", 401, auth: true);
        var schema = NotionLogic.DetectSchema(await Send(Request(HttpMethod.Get, NotionLogic.DatabaseUrl(databaseId), token)), statusName, dateName);
        string body = await Send(Request(HttpMethod.Post, NotionLogic.QueryUrl(databaseId), token, NotionLogic.BuildQuery(schema, hideDone, sort, max)));
        var rows = NotionLogic.Visible(NotionLogic.ParseRows(body, schema), hideDone, sort, max);
        return NotionLogic.ToCache(new NotionLogic.Data { Schema = schema, Rows = rows });
    }

    // ---------- Setup and actions ----------

    private static void Connect(WidgetContext ctx)
    {
        string? token = ctx.AskSecret("Connect Notion",
            "Paste your integration's secret. Create an internal integration at notion.so/my-integrations, copy its secret here, then open your database in Notion, choose Connections in the ••• menu and add the integration. It is saved encrypted for your Windows account only.");
        if (token is null) return;
        ctx.SetSecret(TokenName, token);
        ctx.Item.Options.Remove(ConnectedKit.CacheError);
        ctx.Item.Options.Remove(ConnectedKit.CacheErrorKind);
        if (DatabaseId(ctx) is null) ChooseDatabase(ctx);
        else ctx.Changed();
        if (DatabaseId(ctx) is { } id) ConnectedKit.Refresh(ctx, Source(ctx, id), force: true);
    }

    private static void ChooseDatabase(WidgetContext ctx)
    {
        string? text = ctx.Ask("Notion database", "Paste the database's link or id (open the database in Notion, then Share, Copy link).", ctx.Get("database"));
        if (text is null) return;
        if (NotionLogic.ExtractId(text) is not { } id)
        {
            ctx.Inform("That doesn't look like a Notion database link or id. A link ends with 32 letters and digits.", "Notion");
            return;
        }
        ctx.Set("database", id);
        ConnectedKit.Refresh(ctx, Source(ctx, id), force: true);
    }

    public IEnumerable<WidgetAction> Actions(WidgetContext ctx)
    {
        if (!ctx.HasSecret(TokenName)) { yield return new WidgetAction("Connect Notion…", "", () => Connect(ctx)); yield break; }
        if (DatabaseId(ctx) is { } id)
        {
            yield return ConnectedKit.RefreshAction(ctx, Source(ctx, id));
            yield return new WidgetAction("Add a page…", "", () => AddPage(ctx));
            yield return new WidgetAction("Open the database", "", () => ctx.Open("https://www.notion.so/" + id.Replace("-", "")));
        }
        yield return new WidgetAction("Choose database…", "", () => ChooseDatabase(ctx));
        yield return new WidgetAction("Use a different access key…", "", () => Connect(ctx));
        yield return new WidgetAction("Disconnect Notion", "", () => ConnectedKit.Disconnect(ctx, TokenName, "Notion"));
    }

    /// <summary>Asks for a title and creates a page in the database with it (POST /pages).</summary>
    private static void AddPage(WidgetContext ctx)
    {
        if (DatabaseId(ctx) is not { } databaseId) { ChooseDatabase(ctx); return; }
        if (ctx.Ask("Add to Notion", "Title of the new page") is not { } entered || entered.Trim().Length == 0) return;
        string title = entered.Trim(), status = ctx.Get("addStatus").Trim();
        DateTime? date = ctx.Get("addDate") == "today" ? DateTime.Today : null;
        var source = Source(ctx, databaseId);
        ctx.RunOnce("notion:add:" + ctx.Item.Id, async () =>
        {
            string? token = ctx.GetSecret(TokenName);
            if (token is null) { ctx.Status("Connect Notion first."); return; }
            try
            {
                var schema = NotionLogic.FromCache(ctx.Item.Options.GetValueOrDefault(ConnectedKit.CacheJson)).Schema;
                if (schema.TitleProp.Length == 0)
                    schema = NotionLogic.DetectSchema(await Send(Request(HttpMethod.Get, NotionLogic.DatabaseUrl(databaseId), token)), ctx.Get("statusProperty"), ctx.Get("dateProperty"));
                await Send(Request(HttpMethod.Post, NotionLogic.PagesUrl, token, NotionLogic.BuildNewPage(databaseId, schema, title, status, date)));
                ctx.Status("Added \"" + OnlineLogic.Clip(title, 40) + "\" to Notion.");
            }
            catch (ConnectedException ex) { ctx.Inform(ex.Message, "Couldn't add the page"); return; }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or FormatException) { ctx.Inform("Couldn't reach Notion. Check your connection and try again.", "Couldn't add the page"); return; }
            ConnectedKit.Refresh(ctx, source, force: true);
        });
    }

    // ---------- Drawing ----------

    public FrameworkElement Build(WidgetContext ctx)
    {
        string json = ConnectedKit.Cached(ctx);
        bool hasToken = ctx.IsSnapshot ? json.Length > 0 : ctx.HasSecret(TokenName);
        string? databaseId = DatabaseId(ctx);
        if (!hasToken || databaseId is null)
        {
            var (setupCard, setupBody) = WidgetKit.Frame(ctx);
            StackPanel panel;
            if (!hasToken)
            {
                panel = ConnectedKit.SetupPanel(ctx, "", "Connect Notion",
                    "Create an integration at notion.so/my-integrations, paste its secret, then share your database with it (••• menu, Connections).",
                    "Connect Notion…", () => Connect(ctx));
                var link = WidgetKit.Muted(ctx, "Open notion.so/my-integrations", 11);
                link.Margin = new Thickness(0, 8, 0, 0);
                WidgetKit.Clickable(link, () => ctx.Open(IntegrationsPage), IntegrationsPage);
                panel.Children.Add(link);
            }
            else panel = ConnectedKit.SetupPanel(ctx, "", "Choose a database", "Paste the link of the Notion database to show. Share it with your integration first.", "Choose database…", () => ChooseDatabase(ctx));
            setupBody.Children.Add(WidgetKit.Scroll(panel));
            return setupCard;
        }

        var source = Source(ctx, databaseId);
        var data = NotionLogic.FromCache(json);
        var rows = NotionLogic.Visible(data.Rows, ctx.GetBool("hideDone"), ctx.Get("sort"), ctx.GetInt("maxItems"));
        var buttons = new List<FrameworkElement>();
        if (ctx.GetBool("showAdd")) buttons.Add(WidgetKit.IconButton("", "Add a page", () => AddPage(ctx), 24, accent: true));
        buttons.Add(WidgetKit.IconButton("", "Refresh now", () => ConnectedKit.Refresh(ctx, source, force: true), 24));
        var (card, body) = WidgetKit.Frame(ctx, null, buttons.ToArray());
        ConnectedKit.Keep(ctx, source, tick: _ => ctx.Rebuild());
        ConnectedKit.AddFooter(ctx, body, ctx.GetBool("showUpdated"));

        if (ConnectedKit.Fetched(ctx) is null)
        {
            body.Children.Add(ConnectedKit.Error(ctx) is { } problem
                ? ConnectedKit.Setup(ctx, "", "Can't read the database", problem, ConnectedKit.ErrorIsAuth(ctx) ? "Use a different access key…" : "Try again", ConnectedKit.ErrorIsAuth(ctx) ? () => Connect(ctx) : () => ConnectedKit.Refresh(ctx, source, force: true), Theme.Danger)
                : ConnectedKit.Loading(ctx, "Getting your database…"));
            return card;
        }

        var stack = new StackPanel();
        if (ctx.GetBool("showTitle") && data.Schema.DatabaseTitle.Length > 0)
        {
            var title = WidgetKit.Text(ctx, data.Schema.DatabaseTitle, 12, Theme.Text2, bold: true);
            title.Margin = new Thickness(6, 0, 6, 4);
            stack.Children.Add(title);
        }
        if (rows.Count == 0) stack.Children.Add(WidgetKit.Empty(ctx, ctx.GetBool("hideDone") ? "Nothing open. Nice." : "This database is empty."));
        var now = DateTime.Now;
        if (ctx.GetBool("groupByStatus") && data.Schema.StatusProp.Length > 0)
        {
            foreach (var (name, color, groupRows) in NotionLogic.Group(rows, data.Schema))
            {
                var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, stack.Children.Count == 0 ? 0 : 8, 0, 2) };
                head.Children.Add(new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = StatusBrush(ctx, color), Margin = new Thickness(0, 1, 6, 0) });
                head.Children.Add(WidgetKit.Text(ctx, name.ToUpperInvariant(), 10, Theme.Muted, bold: true));
                head.Children.Add(WidgetKit.Text(ctx, "  " + groupRows.Count.ToString(CultureInfo.CurrentCulture), 10, Theme.Dim));
                stack.Children.Add(head);
                foreach (var row in groupRows) stack.Children.Add(RowView(ctx, row, now, showStatus: false));
            }
        }
        else foreach (var row in rows) stack.Children.Add(RowView(ctx, row, now, showStatus: ctx.GetBool("showStatus")));
        body.Children.Add(WidgetKit.Scroll(stack));
        return card;
    }

    private static Brush StatusBrush(WidgetContext ctx, string? color)
        => ctx.GetBool("statusColors") ? WidgetKit.Color(NotionLogic.ColorHex(color)) : ctx.Accent;

    private static FrameworkElement RowView(WidgetContext ctx, NotionLogic.Row row, DateTime now, bool showStatus)
    {
        bool compact = ctx.GetBool("compact");
        double titleSize = ctx.GetNumber("titleSize");
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bool showDot = ctx.GetBool("showDot");
        var statusBrush = StatusBrush(ctx, row.StatusColor);
        if (showDot)
        {
            FrameworkElement lead = row.Done
                ? WidgetKit.Icon("", ctx.Font(11), WidgetKit.Color("#4dab9a"))
                : new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = row.Status.Length > 0 ? statusBrush : Theme.Dim };
            lead.VerticalAlignment = compact ? VerticalAlignment.Center : VerticalAlignment.Top;
            lead.Margin = new Thickness(row.Done ? 0 : 1, compact ? 0 : (row.Done ? 3 : 6), 9, 0);
            grid.Children.Add(lead);
        }
        var stack = new StackPanel();
        Grid.SetColumn(stack, 1);
        var title = WidgetKit.Text(ctx, row.Title, titleSize, ctx.TextBrush);
        if (!compact)
        {
            title.TextWrapping = TextWrapping.Wrap;
            title.TextTrimming = TextTrimming.WordEllipsis;
            title.LineHeight = Math.Round(ctx.Font(titleSize) * 1.3);
            title.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            title.MaxHeight = title.LineHeight * ctx.GetInt("titleLines") + 1;
        }
        stack.Children.Add(title);

        var (dateText, overdue, today) = ctx.GetBool("showDates") ? NotionLogic.DateLabel(row.Date, now) : ("", false, false);
        Brush dateBrush = overdue && !row.Done ? ctx.GetColor("overdueColor", WidgetKit.Color("#ff7369")) : today && !row.Done ? ctx.GetColor("todayColor", WidgetKit.Color("#ffa344")) : Theme.Muted;
        bool statusText = showStatus && row.Status.Length > 0;
        if (!compact && (statusText || dateText.Length > 0))
        {
            var line = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
            if (statusText)
            {
                var pill = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 0, 6, 1), Margin = new Thickness(0, 0, 8, 0), Background = ctx.GetBool("statusColors") ? Theme.Tint(NotionLogic.ColorHex(row.StatusColor), 0.18) : Theme.Tint(ctx.Item.Accent, 0.18), Child = WidgetKit.Text(ctx, row.Status, 10.5, statusBrush, bold: true) };
                line.Children.Add(pill);
            }
            if (dateText.Length > 0) line.Children.Add(WidgetKit.Text(ctx, dateText, 10.5, dateBrush, bold: overdue || today));
            stack.Children.Add(line);
        }
        grid.Children.Add(stack);
        if (compact && dateText.Length > 0)
        {
            var date = WidgetKit.Text(ctx, dateText, 10.5, dateBrush, bold: overdue || today);
            date.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(date, 2);
            grid.Children.Add(date);
        }

        var view = WidgetKit.Row(grid);
        view.Padding = compact ? new Thickness(6, 3, 6, 3) : new Thickness(6, 5, 6, 5);
        if (row.Done) view.Opacity = 0.55;
        string url = row.Url;
        view.ToolTip = row.Title + (row.Status.Length > 0 ? "\n" + row.Status : "") + (dateText.Length > 0 ? "\n" + dateText : "") + "\n\nClick: open in Notion";
        var menu = MainWindow.Menu();
        MainWindow.AddMenuItem(menu, "Open in Notion", () => OpenPage(ctx, url), "");
        MainWindow.AddMenuItem(menu, "Copy link", () => { try { Clipboard.SetText(url); ctx.Status("Link copied."); } catch (System.Runtime.InteropServices.COMException) { ctx.Status("The clipboard is busy; try again."); } }, "");
        view.ContextMenu = menu;
        WidgetKit.Clickable(view, () => OpenPage(ctx, url));
        return view;
    }

    private static void OpenPage(WidgetContext ctx, string url)
    {
        if (OnlineLogic.IsWebLink(url)) ctx.Open(url);
    }
}
