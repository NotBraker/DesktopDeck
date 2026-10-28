using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DesktopDeck.Models;
using DesktopDeck.Services;

namespace DesktopDeck.Widgets;

/// <summary>
/// Gmail, read-only: an unread badge and the latest messages for a Gmail search (sender, subject, age, snippet). Click one to open
/// its conversation in Gmail. Only message metadata is downloaded (never bodies), nothing is ever sent, changed or deleted, and what is
/// downloaded stays on this PC (trimmed, in this widget's settings). Logic and tests: GmailLogic.cs, GmailService.cs.
/// </summary>
internal sealed class GmailWidget : IWidget
{
    private static readonly string[] AvatarColors = ["#6c63ff", "#14b8a6", "#f59e0b", "#ec4899", "#3b82f6", "#22c55e", "#ef4444", "#8b5cf6"];

    public string Kind => "gmail";
    public string Name => "Gmail";
    public string Description => "Unread count and your latest messages (read-only)";
    public string Section => "Connected";
    public string Glyph => "";
    public string Accent => "#ea4335";
    public (double Width, double Height) DefaultSize => (324, 372);
    public (double Width, double Height) MinimumSize => (228, 180);

    public IReadOnlyList<OptionSpec> Options { get; } =
    [
        OptionSpec.Text("query", "Gmail search", GmailLogic.DefaultQuery, "What to show", "The same search you would type in Gmail, for example is:unread or label:work."),
        OptionSpec.Number("maxItems", "Messages to show", 6, 1, GmailService.MaxMessages, "What to show"),
        OptionSpec.Choice("badge", "Count on the badge", "inbox", ["inbox", "query", "none"], ["Unread in your Inbox", "Unread in this search", "No badge"], "What to show"),
        OptionSpec.Toggle("showSnippet", "Show the first lines of each message", true, "Layout"),
        OptionSpec.Toggle("showAvatars", "Show sender initials in a circle", true, "Layout"),
        OptionSpec.Toggle("groupByDay", "Group by day", false, "Layout"),
        OptionSpec.Toggle("boldUnread", "Bold unread messages", true, "Layout"),
        OptionSpec.Toggle("compact", "Compact (one line per message)", false, "Layout"),
        OptionSpec.Toggle("showAge", "Show how long ago", true, "Layout"),
        OptionSpec.Number("snippetLines", "Lines of preview", 1, 1, 3, "Layout"),
        OptionSpec.Lines("labelColors", "Color by label", "", "Colors", "One per line: Label | #color, for example Work | #4285f4. Gmail's own labels, such as Starred, work too."),
        OptionSpec.Number("refresh", "Refresh every (minutes)", 5, 2, 120, "Behavior", "Only while the widget is on screen; 2 minutes is the shortest."),
        OptionSpec.Toggle("inboxZero", "Show \"Inbox zero\" when nothing matches", true, "Behavior"),
        OptionSpec.Toggle("showUpdated", "Show when it last updated", false, "Behavior"),
    ];

    // ---------- Samples (made-up mail run through the real parser) ----------

    public IEnumerable<Action<LauncherItem>> Samples =>
    [
        item => Fill(item),
        item => { Fill(item); item.Options["compact"] = "true"; item.Options["groupByDay"] = "true"; item.Options["maxItems"] = "8"; item.Options["labelColors"] = "Work | #4285f4\nPromotions | #f59e0b\nStarred | #ea4335"; item.Width = 372; item.Height = 372; },
        item => { Fill(item, empty: true); },
        item => { Fill(item, age: TimeSpan.FromHours(2), error: "Couldn't connect. Check your connection."); item.Options["showSnippet"] = "false"; item.Options["showAvatars"] = "false"; item.Options["snippetLines"] = "2"; item.Width = 276; item.Height = 324; },
    ];

    private static string Msg(string id, string from, string subject, string snippet, int minutesAgo, string[] labels)
    {
        var headers = new[]
        {
            new Dictionary<string, string> { ["name"] = "From", ["value"] = from },
            new Dictionary<string, string> { ["name"] = "Subject", ["value"] = subject },
            new Dictionary<string, string> { ["name"] = "Date", ["value"] = "Mon, 5 Oct 2026 09:00:00 +0000" },
        };
        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["id"] = id, ["threadId"] = "t" + id, ["snippet"] = snippet, ["labelIds"] = labels,
            ["internalDate"] = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            ["payload"] = new Dictionary<string, object> { ["headers"] = headers },
        });
    }

    private static void Fill(LauncherItem item, bool empty = false, TimeSpan? age = null, string? error = null)
    {
        var messages = empty ? [] : new[]
        {
            Msg("a1", "\"Priya Nair\" <priya@example.com>", "Draft for Thursday's review", "Hi! I added comments to slides 4&#39;&ndash;9. Could you look before lunch? The numbers on the last slide still need a source.", 14, ["INBOX", "UNREAD", "Label_1"]),
            Msg("a2", "Campus Library <noreply@library.example.edu>", "=?UTF-8?B?8J+TmiBZb3VyIGJvb2sgaXMgZHVlIHRvbW9ycm93?=", "Reminder: &quot;Designing Data-Intensive Applications&quot; is due back on Oct 6. Renew online in two clicks.", 95, ["INBOX", "UNREAD", "CATEGORY_UPDATES"]),
            Msg("a3", "Marco Bellini <marco@example.org>", "=?ISO-8859-1?Q?Caf=E9_on_Saturday=3F?=", "Are you still in? I booked a table for four at the place by the river.", 230, ["INBOX", "UNREAD", "STARRED"]),
            Msg("a4", "Orbit Deals <hello@orbit.example.com>", "48 hours only: 30% off everything", "Stock up on cables, stands and desk lamps. Prices go back up on Wednesday at midnight.", 400, ["INBOX", "UNREAD", "CATEGORY_PROMOTIONS"]),
            Msg("a5", "GitHub <notifications@github.com>", "[octo-org/deck] Fix flicker when resizing a tile (#482)", "ada-lovelace approved these changes. Merging is blocked until the required checks pass.", 1500, ["INBOX", "UNREAD", "Label_1"]),
            Msg("a6", "Sam Ortega <sam@example.com>", "Notes from the call", "Thanks again for the time. Summary and next steps are below; shout if I missed anything.", 3100, ["INBOX", "UNREAD"]),
            Msg("a7", "Dana Whitfield <dana@example.com>", "Invoice 2026-114", "Attached is the invoice for September. Payment terms are 30 days as agreed.", 4600, ["INBOX", "UNREAD", "Label_2"]),
            Msg("a8", "Weekly Digest <digest@news.example.com>", "Your week in review", "Here are the five stories our readers loved most this week.", 7000, ["INBOX", "UNREAD", "CATEGORY_UPDATES"]),
        }.Select(m => GmailService.ParseMessageJson(m)!).ToList();
        var labels = new[] { new GmailLabel("Label_1", "Work"), new GmailLabel("Label_2", "Billing") };
        ConnectedKit.Fill(item, GmailLogic.ToCache(messages, empty ? 0 : 23, labels), "", age ?? TimeSpan.FromMinutes(2), error);
        if (error is not null) item.Options[ConnectedKit.CacheErrorKind] = "net";
    }

    // ---------- Refreshing ----------

    private static GmailBadge BadgeOf(WidgetContext ctx) => ctx.Get("badge") switch { "query" => GmailBadge.Query, "none" => GmailBadge.None, _ => GmailBadge.Inbox };

    private static ConnectedSource Source(WidgetContext ctx)
    {
        string query = GmailLogic.QueryOf(ctx.Get("query"));
        int max = ctx.GetInt("maxItems");
        var badge = BadgeOf(ctx);
        bool labels = GmailLogic.ParseLabelColors(ctx.GetLines("labelColors")).Count > 0;
        string signature = query + "/" + max + "/" + badge + "/" + labels;
        return new ConnectedSource(signature, OnlineLogic.Interval(ctx.GetNumber("refresh"), 2), () => GoogleKit.Guard(async () =>
        {
            var fetch = await ctx.Gmail.FetchAsync(query, max, badge, labels);
            return GmailLogic.ToCache(fetch.Messages, fetch.Unread, fetch.Labels);
        }), Strict: false);
    }

    public IEnumerable<WidgetAction> Actions(WidgetContext ctx)
    {
        if (ctx.GoogleReady(GoogleFeatures.Gmail)) yield return ConnectedKit.RefreshAction(ctx, Source(ctx));
        yield return new WidgetAction("Open Gmail", "", () => ctx.Open(GmailLogic.InboxLink));
    }

    // ---------- Drawing ----------

    public FrameworkElement Build(WidgetContext ctx)
    {
        var source = Source(ctx);
        string json = ConnectedKit.Cached(ctx);
        bool ready = ctx.IsSnapshot ? json.Length > 0 : ctx.GoogleReady(GoogleFeatures.Gmail);
        if (!ready)
        {
            ConnectedKit.Watch(ctx, source);
            return GoogleKit.Setup(ctx, GoogleFeatures.Gmail, Glyph, "Connect Gmail", "See your unread count and latest messages here. Read-only: Desktop Deck can never send, change or delete mail, and it only reads who, subject, date and a short snippet.");
        }

        var data = GmailLogic.FromCache(json);
        var badgeMode = BadgeOf(ctx);
        FrameworkElement? badge = badgeMode != GmailBadge.None && data.Unread is > 0 ? WidgetKit.Pill(ctx, GmailLogic.BadgeText(data.Unread.Value), ctx.Item.Accent) : null;
        var refresh = WidgetKit.IconButton(Theme.Glyphs.Refresh, "Refresh now", () => { ctx.Status("Checking Gmail…"); ConnectedKit.Refresh(ctx, source, force: true); }, 24);
        var (card, body) = WidgetKit.Frame(ctx, badge, refresh);
        ConnectedKit.Keep(ctx, source, tick: _ => ctx.Rebuild());
        ConnectedKit.AddFooter(ctx, body, ctx.GetBool("showUpdated"));

        if (ConnectedKit.Fetched(ctx) is null)
        {
            body.Children.Add(ConnectedKit.Error(ctx) is { } problem
                ? ConnectedKit.Setup(ctx, "", "Can't reach Gmail", problem, ConnectedKit.ErrorIsAuth(ctx) ? "Open Settings" : "Try again", ConnectedKit.ErrorIsAuth(ctx) ? ctx.OpenSettings : () => ConnectedKit.Refresh(ctx, source, force: true), Theme.Danger)
                : ConnectedKit.Loading(ctx, "Checking Gmail…"));
            return card;
        }

        var now = DateTimeOffset.Now;
        if (data.Messages.Count == 0)
        {
            body.Children.Add(ctx.GetBool("inboxZero") ? InboxZero(ctx) : WidgetKit.Empty(ctx, "No messages match this search."));
            return card;
        }

        var rules = GmailLogic.ParseLabelColors(ctx.GetLines("labelColors"));
        var sections = GmailLogic.Layout(data.Messages, ctx.GetInt("maxItems"), ctx.GetBool("groupByDay"), now.LocalDateTime);
        var list = new StackPanel();
        foreach (var section in sections)
        {
            if (section.Title.Length > 0)
            {
                var heading = WidgetKit.Text(ctx, section.Title.ToUpperInvariant(), 10, Theme.Muted, bold: true);
                heading.Margin = new Thickness(4, list.Children.Count == 0 ? 0 : 8, 0, 3);
                list.Children.Add(heading);
            }
            foreach (var message in section.Items) list.Children.Add(Row(ctx, message, data, rules, now));
        }
        body.Children.Add(WidgetKit.Scroll(list));
        return card;
    }

    private static FrameworkElement InboxZero(WidgetContext ctx)
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var icon = WidgetKit.Icon(Theme.Glyphs.Check, ctx.Font(30), Theme.Olive);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(icon);
        var title = WidgetKit.Text(ctx, "Inbox zero", 16, bold: true);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(title);
        var note = WidgetKit.Muted(ctx, "Nothing needs you right now.", 11.5);
        note.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(note);
        return stack;
    }

    private static FrameworkElement Row(WidgetContext ctx, GmailMessage m, GmailLogic.CacheData data, IReadOnlyList<(string Label, string Color)> rules, DateTimeOffset now)
    {
        bool compact = ctx.GetBool("compact"), bold = ctx.GetBool("boldUnread") && m.Unread;
        bool avatars = ctx.GetBool("showAvatars") && !compact, snippet = ctx.GetBool("showSnippet") && !compact && m.Snippet.Length > 0;
        string? accent = GmailLogic.AccentFor(m, rules, data.LabelNames);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        // With label colors on, every row keeps the same indent so the text lines up.
        grid.ColumnDefinitions[0].Width = new GridLength(rules.Count > 0 ? 10 : 0);
        if (accent is not null)
            grid.Children.Add(new Border { Width = 3, CornerRadius = new CornerRadius(2), Background = Theme.Accent(accent), Margin = new Thickness(0, 1, 0, 1), HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Colored by its label" });
        if (avatars)
        {
            string color = AvatarColors[GmailLogic.ColorBucket(m, AvatarColors.Length)];
            var circle = new Grid { Width = 28, Height = 28, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 9, 0) };
            circle.Children.Add(new Ellipse { Fill = Theme.Tint(color, 0.85) });
            var initials = Theme.Label(GmailLogic.InitialsOf(m), ctx.Font(11), Brushes.White);
            initials.FontWeight = FontWeights.SemiBold;
            initials.HorizontalAlignment = HorizontalAlignment.Center;
            initials.VerticalAlignment = VerticalAlignment.Center;
            circle.Children.Add(initials);
            Grid.SetColumn(circle, 1);
            grid.Children.Add(circle);
        }
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        string age = ctx.GetBool("showAge") ? GmailLogic.AgeOf(m, now) : "";
        if (compact)
        {
            var line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ctx.Width < 300 ? 82 : 108) });
            line.ColumnDefinitions.Add(new ColumnDefinition());
            line.Children.Add(WidgetKit.Text(ctx, m.SenderName, 12, bold ? ctx.TextBrush : Theme.Text2, bold));
            var subject = WidgetKit.Text(ctx, GmailLogic.SubjectOf(m), 12, bold ? ctx.TextBrush : Theme.Muted, bold);
            Grid.SetColumn(subject, 1);
            line.Children.Add(subject);
            text.Children.Add(line);
        }
        else
        {
            text.Children.Add(WidgetKit.Text(ctx, m.SenderName, 12.5, ctx.TextBrush, bold));
            text.Children.Add(WidgetKit.Text(ctx, GmailLogic.SubjectOf(m), 12, bold ? ctx.TextBrush : Theme.Text2, bold));
            if (snippet)
            {
                var preview = WidgetKit.Muted(ctx, m.Snippet, 11);
                int lines = Math.Max(1, ctx.GetInt("snippetLines"));
                preview.TextWrapping = lines > 1 ? TextWrapping.Wrap : TextWrapping.NoWrap;
                preview.MaxHeight = ctx.Font(11) * 1.45 * lines;
                text.Children.Add(preview);
            }
        }
        Grid.SetColumn(text, 2);
        grid.Children.Add(text);
        var right = new StackPanel { VerticalAlignment = compact ? VerticalAlignment.Center : VerticalAlignment.Top, Margin = new Thickness(8, compact ? 0 : 2, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        if (age.Length > 0) right.Children.Add(new TextBlock { Text = age, FontFamily = Theme.UiFont, FontSize = ctx.Font(10.5), Foreground = Theme.Muted, HorizontalAlignment = HorizontalAlignment.Right });
        if (m.Unread && !ctx.GetBool("boldUnread")) right.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = ctx.Accent, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 3, 0, 0) });
        Grid.SetColumn(right, 3);
        grid.Children.Add(right);

        var row = WidgetKit.Row(grid);
        if (compact) row.Padding = new Thickness(6, 3, 6, 3);
        string url = GmailLogic.LinkFor(m);
        void Open() { if (ConnectedLogic.IsLinkOn(url, GmailLogic.Host)) ctx.Open(url); }
        WidgetKit.Clickable(row, Open, GmailLogic.SubjectOf(m) + "\n" + m.SenderName + (m.SenderEmail.Length > 0 ? " <" + m.SenderEmail + ">" : "") + "\n\nClick: open in Gmail");
        var menu = MainWindow.Menu();
        MainWindow.AddMenuItem(menu, "Open in Gmail", Open, "");
        MainWindow.AddMenuItem(menu, "Copy link", () => { try { Clipboard.SetText(url); ctx.Status("Link copied."); } catch (System.Runtime.InteropServices.COMException) { } }, "");
        row.ContextMenu = menu;
        return row;
    }
}
