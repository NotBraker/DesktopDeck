using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopDeck.Models;

namespace DesktopDeck.Widgets;

/// <summary>
/// News feeds: RSS and Atom feeds merged newest first, with source, age and an optional snippet. Click opens the
/// article. Parsing lives in NewsFeedLogic.cs (tests/DesktopDeck.WidgetTests/NewsFeedTests.cs).
/// </summary>
internal sealed class NewsFeedWidget : IWidget
{
    private const string ReadKey = "cache.read";
    private static readonly string[] Palette = ["#f59e0b", "#6c63ff", "#22c55e", "#ec4899", "#00d4ff", "#ef4444", "#14b8a6", "#8b5cf6"];

    public string Kind => "rss";
    public string Name => "News feeds";
    public string Description => "Headlines from your RSS and Atom feeds";
    public string Section => "Online";
    public string Glyph => "";
    public string Accent => "#f59e0b";
    public (double Width, double Height) DefaultSize => (324, 372);
    public (double Width, double Height) MinimumSize => (228, 180);

    public IReadOnlyList<OptionSpec> Options { get; } =
    [
        OptionSpec.Lines("feeds", "Feeds", "https://feeds.bbci.co.uk/news/world/rss.xml | BBC World\nhttps://www.theverge.com/rss/index.xml | The Verge", "What to show",
            "One feed address per line. Optional: a name and a color, e.g. https://example.com/feed | Example | #22c55e"),
        OptionSpec.Number("maxItems", "Number of headlines", 20, 1, 100, "What to show"),
        OptionSpec.Number("perFeed", "Most headlines per feed", 0, 0, 50, "What to show", "0 = no limit. Keeps one busy feed from crowding out the rest."),
        OptionSpec.Number("maxAgeDays", "Hide items older than (days)", 0, 0, 365, "What to show", "0 = show everything."),
        OptionSpec.Toggle("showSource", "Show source name", true, "What to show"),
        OptionSpec.Toggle("showAge", "Show age (5m, 3h, 2d)", true, "What to show"),
        OptionSpec.Toggle("showSummary", "Show a summary snippet", false, "What to show"),
        OptionSpec.Number("summaryLines", "Summary lines", 2, 1, 5, "What to show"),
        OptionSpec.Choice("layout", "Layout", "list", ["list", "compact", "headline"], ["List", "Compact (one line each)", "Top story + list"], "Layout"),
        OptionSpec.Number("titleLines", "Lines per headline", 2, 1, 4, "Layout"),
        OptionSpec.Number("titleSize", "Headline size", 13, 10, 20, "Layout"),
        OptionSpec.Toggle("dividers", "Lines between items", false, "Layout"),
        OptionSpec.Choice("colorMode", "Source colors", "feed", ["feed", "accent", "none"], ["A color per feed", "Accent color", "No color"], "Colors"),
        OptionSpec.Color("titleColor", "Headline color", "", "Colors", "Empty = text color."),
        OptionSpec.Toggle("dimRead", "Dim headlines you've opened", true, "Behavior"),
        OptionSpec.Number("refresh", "Refresh every (minutes)", 30, 10, 1440, "Behavior"),
        OptionSpec.Toggle("showUpdated", "Show when it last updated", false, "Behavior"),
    ];

    public IEnumerable<Action<LauncherItem>> Samples =>
    [
        item => Fill(item, TimeSpan.FromMinutes(4)),
        item => { Fill(item, TimeSpan.FromMinutes(4)); item.Options["showSummary"] = "true"; item.Options["layout"] = "headline"; item.Options["showUpdated"] = "true"; item.Width = 372; item.Height = 468; },
        item => { Fill(item, TimeSpan.FromHours(3), "Couldn't connect. Check your connection."); item.Options["layout"] = "compact"; item.Options["dividers"] = "true"; item.Width = 372; item.Height = 276; },
    ];

    /// <summary>Two made-up feeds (one RSS, one Atom) run through the real parser.</summary>
    private static void Fill(LauncherItem item, TimeSpan age, string? error = null)
    {
        item.Options["feeds"] = "https://news.example.com/rss.xml | Daily Planet\nhttps://tech.example.org/atom.xml";
        var now = DateTimeOffset.Now;
        string R(double hours) => (now - TimeSpan.FromHours(hours)).ToString("ddd, dd MMM yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture).Remove(29, 1);
        string A(double hours) => (now - TimeSpan.FromHours(hours)).ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
        string rss = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <rss version="2.0"><channel><title>Daily Planet</title>
            <item><title>City council approves the new riverside park after a long debate</title><link>https://news.example.com/park</link><description>&lt;p&gt;The plan adds &lt;b&gt;four kilometres&lt;/b&gt; of walking paths, a public pool and a night market by 2028.&lt;/p&gt;</description><pubDate>{R(0.3)}</pubDate></item>
            <item><title>Night trains return to the northern line this winter</title><link>https://news.example.com/trains</link><description>Sleeper services will run three times a week starting in December.</description><pubDate>{R(2.5)}</pubDate></item>
            <item><title>Local bakery wins national bread award for the third time</title><link>https://news.example.com/bakery</link><description>Judges praised its rye sourdough.</description><pubDate>{R(7)}</pubDate></item>
            <item><title>Storm warning lifted as the weekend turns sunny</title><link>https://news.example.com/storm</link><description>Expect clear skies and light winds through Monday.</description><pubDate>{R(26)}</pubDate></item>
            <item><title>Library extends its opening hours for exam season</title><link>https://news.example.com/library</link><pubDate>{R(50)}</pubDate></item>
            </channel></rss>
            """;
        string atom = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <feed xmlns="http://www.w3.org/2005/Atom"><title type="text">Tech Weekly</title>
            <entry><title type="html"><![CDATA[Open-source photo editor ships a redesigned toolbar]]></title><link rel="alternate" href="https://tech.example.org/photo-editor"/><published>{A(1.2)}</published><summary type="html">&lt;p&gt;Version 3.0 brings layers, a dark theme and faster exports.&lt;/p&gt;</summary></entry>
            <entry><title>Why your next laptop might charge in ten minutes</title><link href="https://tech.example.org/charging"/><updated>{A(5)}</updated><summary>New battery chemistry is moving from the lab to the factory.</summary></entry>
            <entry><title>Hands-on with a keyboard made entirely of recycled plastic</title><link href="https://tech.example.org/keyboard"/><updated>{A(30)}</updated></entry>
            </feed>
            """;
        var a = NewsFeedLogic.Parse(rss, "https://news.example.com/rss.xml");
        var b = NewsFeedLogic.Parse(atom, "https://tech.example.org/atom.xml");
        var merged = NewsFeedLogic.Merge(a.Items.Concat(b.Items), 100, 0, 0, now);
        var titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["https://news.example.com/rss.xml"] = a.Title, ["https://tech.example.org/atom.xml"] = b.Title };
        OnlineWidget.Fill(item, NewsFeedLogic.ToCache(new NewsFeedLogic.Cache(titles, [], merged)), Signature(NewsFeedLogic.ParseSources(item.Options["feeds"].Split('\n'))), age, error);
        item.Options[ReadKey] = OnlineLogic.Hash("https://news.example.com/bakery");
    }

    private static string Signature(List<NewsFeedLogic.Source> sources) => string.Join("\n", sources.Select(s => s.Url));

    private static OnlineSource Source(WidgetContext ctx)
    {
        var sources = NewsFeedLogic.ParseSources(ctx.GetLines("feeds"));
        var item = ctx.Item;
        return new OnlineSource(Signature(sources), OnlineLogic.Interval(ctx.GetNumber("refresh"), 10), async () =>
        {
            if (sources.Count == 0) return NewsFeedLogic.ToCache(new NewsFeedLogic.Cache(new(), [], []));
            var old = NewsFeedLogic.FromCache(item.Options.GetValueOrDefault(OnlineWidget.CacheJson) ?? "");
            var tasks = sources.Select(async s =>
            {
                try
                {
                    string xml = await OnlineWidget.GetAsync(s.Url);
                    return (s.Url, Feed: await Task.Run(() => NewsFeedLogic.Parse(xml, s.Url)), Error: (Exception?)null);
                }
                catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or FormatException) { return (s.Url, Feed: (NewsFeedLogic.Feed?)null, Error: ex); }
            }).ToList();
            var results = await Task.WhenAll(tasks);
            // Every feed failed: report it (the old headlines stay on screen with the offline note).
            if (results.All(r => r.Feed is null) && results.FirstOrDefault().Error is { } first) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(first);
            var titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var items = new List<NewsFeedLogic.Item>();
            var failed = new List<string>();
            foreach (var (url, feed, _) in results)
            {
                if (feed is not null) { titles[url] = feed.Title; items.AddRange(feed.Items); continue; }
                // A feed that failed this time keeps its previous headlines.
                failed.Add(url);
                if (old.Titles.TryGetValue(url, out var oldTitle)) titles[url] = oldTitle;
                items.AddRange(old.Items.Where(i => string.Equals(i.Feed, url, StringComparison.OrdinalIgnoreCase)));
            }
            return NewsFeedLogic.ToCache(new NewsFeedLogic.Cache(titles, failed, NewsFeedLogic.Merge(items, 100, 25, 0, DateTimeOffset.Now)));
        });
    }

    public IEnumerable<WidgetAction> Actions(WidgetContext ctx)
    {
        yield return OnlineWidget.RefreshAction(ctx, Source(ctx));
        yield return new WidgetAction("Add a feed…", "", () =>
        {
            string? url = ctx.Ask("Add a feed", "Feed address (RSS or Atom)", "https://");
            if (url is null) return;
            url = url.Trim();
            if (NewsFeedLogic.ParseSource(url) is null) { ctx.Inform("That doesn't look like a web address (it should start with https://).", "Add a feed"); return; }
            string feeds = ctx.Get("feeds").TrimEnd();
            ctx.Set("feeds", feeds.Length == 0 ? url : feeds + "\n" + url);
        });
        if (ctx.Item.Options.ContainsKey(ReadKey)) yield return new WidgetAction("Mark all as unread", "", () => { ctx.Item.Options.Remove(ReadKey); ctx.Changed(); });
    }

    public FrameworkElement Build(WidgetContext ctx)
    {
        var (card, body) = WidgetKit.Frame(ctx);
        var sources = NewsFeedLogic.ParseSources(ctx.GetLines("feeds"));
        var source = Source(ctx);
        DateTimeOffset now = DateTimeOffset.Now;
        // Ages change by the minute; redraw only when the shortest one would show a new number.
        OnlineWidget.Keep(ctx, source, tick: _ => { if (ctx.GetBool("showAge")) ctx.Rebuild(); });
        if (sources.Count == 0) { body.Children.Add(WidgetKit.Empty(ctx, "Add a feed address in Customize… (or right-click → Add a feed…).")); return card; }

        var cache = NewsFeedLogic.FromCache(OnlineWidget.Cached(ctx));
        bool showSource = ctx.GetBool("showSource");
        string attribution = cache.Failed.Count > 0 && OnlineWidget.Error(ctx) is null ? $"{cache.Failed.Count} feed{(cache.Failed.Count == 1 ? "" : "s")} didn't load" : "";
        var footer = OnlineWidget.Footer(ctx, ctx.GetBool("showUpdated"), attribution.Length > 0 ? attribution : null);
        if (footer is not null)
        {
            if (cache.Failed.Count > 0) footer.ToolTip = "Couldn't load:\n" + string.Join("\n", cache.Failed);
            body.Children.Add(footer);
        }
        var bySource = sources.Select((s, i) => (s, i)).ToDictionary(x => x.s.Url, x => x, StringComparer.OrdinalIgnoreCase);
        var items = NewsFeedLogic.Merge(cache.Items.Where(i => bySource.ContainsKey(i.Feed)), ctx.GetInt("maxItems"), ctx.GetInt("perFeed"), ctx.GetNumber("maxAgeDays"), now);
        if (items.Count == 0)
        {
            body.Children.Add(OnlineWidget.Fetched(ctx) is null || !OnlineWidget.Matches(ctx, source.Signature) ? OnlineWidget.Waiting(ctx, "Getting the headlines…") : WidgetKit.Empty(ctx, "No headlines right now."));
            return card;
        }

        var read = new HashSet<string>((ctx.Item.Options.GetValueOrDefault(ReadKey) ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
        string layout = ctx.Get("layout");
        string colorMode = ctx.Get("colorMode");
        bool dimRead = ctx.GetBool("dimRead"), dividers = ctx.GetBool("dividers");
        var list = new StackPanel();
        for (int index = 0; index < items.Count; index++)
        {
            var article = items[index];
            var (src, srcIndex) = bySource[article.Feed];
            Brush sourceBrush = colorMode switch
            {
                "accent" => ctx.Accent,
                "none" => Theme.Muted,
                _ => OnlineWidget.BrushOr(src.Color.Length > 0 ? src.Color : Palette[srcIndex % Palette.Length], ctx.Accent),
            };
            string sourceName = NewsFeedLogic.SourceName(src, cache.Titles);
            bool isRead = dimRead && read.Contains(OnlineLogic.Hash(article.Link.Length > 0 ? article.Link : article.Title));
            bool featured = layout == "headline" && index == 0;
            var row = layout == "compact" && !featured ? CompactRow(ctx, article, sourceName, sourceBrush, now) : FullRow(ctx, article, sourceName, sourceBrush, now, featured, showSource);
            row.Opacity = isRead ? 0.5 : 1;
            row.ToolTip = article.Title + "\n" + sourceName + (article.Published is { } p ? " · " + p.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : "") + (article.Summary.Length > 0 ? "\n\n" + article.Summary : "");
            if (article.Link.Length > 0)
            {
                var opened = article;
                WidgetKit.Clickable(row, () =>
                {
                    if (dimRead) MarkRead(ctx, opened);
                    OnlineWidget.OpenLink(ctx, opened.Link);
                });
            }
            if (index > 0 && (dividers || featured || (layout == "headline" && index == 1))) list.Children.Add(new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(6, 1, 6, 1) });
            list.Children.Add(row);
        }
        body.Children.Add(WidgetKit.Scroll(list));
        return card;
    }

    private static void MarkRead(WidgetContext ctx, NewsFeedLogic.Item article)
    {
        string hash = OnlineLogic.Hash(article.Link.Length > 0 ? article.Link : article.Title);
        var list = (ctx.Item.Options.GetValueOrDefault(ReadKey) ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(h => h != hash).Append(hash).TakeLast(300);
        ctx.Set(ReadKey, string.Join(" ", list));
    }

    private static TextBlock Lines(WidgetContext ctx, string text, double size, Brush color, int lines, bool bold = false)
    {
        var block = WidgetKit.Text(ctx, text, size, color, bold);
        double font = ctx.Font(size);
        block.TextWrapping = TextWrapping.Wrap;
        block.TextTrimming = TextTrimming.WordEllipsis;
        block.LineHeight = Math.Round(font * 1.32);
        block.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        block.MaxHeight = block.LineHeight * Math.Max(1, lines) + 1;
        block.VerticalAlignment = VerticalAlignment.Top;
        return block;
    }

    private static Border FullRow(WidgetContext ctx, NewsFeedLogic.Item article, string sourceName, Brush sourceBrush, DateTimeOffset now, bool featured, bool showSource)
    {
        var stack = new StackPanel();
        bool showAge = ctx.GetBool("showAge") && article.Published is not null;
        if (showSource || showAge)
        {
            var meta = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
            if (ctx.Get("colorMode") != "none")
                meta.Children.Add(new System.Windows.Shapes.Ellipse { Width = ctx.Font(6), Height = ctx.Font(6), Fill = sourceBrush, Margin = new Thickness(0, 1, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            if (showSource)
            {
                var name = WidgetKit.Text(ctx, sourceName, 10.5, ctx.Get("colorMode") == "none" ? Theme.Muted : sourceBrush, bold: true);
                name.MaxWidth = Math.Max(60, ctx.Width - 110);
                meta.Children.Add(name);
            }
            if (showAge) meta.Children.Add(WidgetKit.Text(ctx, (showSource ? "  ·  " : "") + OnlineLogic.Age(article.Published!.Value, now), 10.5, Theme.Muted));
            stack.Children.Add(meta);
        }
        double size = ctx.GetNumber("titleSize") * (featured ? 1.25 : 1);
        stack.Children.Add(Lines(ctx, article.Title, size, ctx.GetColor("titleColor", ctx.TextBrush), featured ? Math.Max(2, ctx.GetInt("titleLines")) + 1 : ctx.GetInt("titleLines"), bold: true));
        if ((ctx.GetBool("showSummary") || featured) && article.Summary.Length > 0)
        {
            var summary = Lines(ctx, article.Summary, 11.5, Theme.Text2, ctx.GetInt("summaryLines") + (featured ? 1 : 0));
            summary.Margin = new Thickness(0, 3, 0, 0);
            stack.Children.Add(summary);
        }
        var row = WidgetKit.Row(stack);
        row.Padding = new Thickness(6, 6, 6, 7);
        return row;
    }

    private static Border CompactRow(WidgetContext ctx, NewsFeedLogic.Item article, string sourceName, Brush sourceBrush, DateTimeOffset now)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (ctx.Get("colorMode") != "none")
            grid.Children.Add(new System.Windows.Shapes.Ellipse { Width = ctx.Font(6), Height = ctx.Font(6), Fill = sourceBrush, Margin = new Thickness(0, 1, 8, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = sourceName });
        var title = WidgetKit.Text(ctx, article.Title, ctx.GetNumber("titleSize") - 0.5, ctx.GetColor("titleColor", ctx.TextBrush));
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);
        if (ctx.GetBool("showAge") && article.Published is { } published)
        {
            var age = WidgetKit.Muted(ctx, OnlineLogic.Age(published, now), 10.5);
            age.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(age, 2);
            grid.Children.Add(age);
        }
        return WidgetKit.Row(grid);
    }
}
