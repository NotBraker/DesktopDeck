using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesktopDeck.Models;

namespace DesktopDeck.Widgets;

/// <summary>Quick capture inbox: jot a thought in two clicks, tick it off or archive it later.</summary>
internal sealed class InboxWidget : IWidget
{
    private const string DataKey = "data.entries";

    public string Kind => "inbox";
    public string Name => "Inbox";
    public string Description => "Capture thoughts fast, sort them out later";
    public string Section => "Productivity";
    public string Glyph => "";
    public string Accent => "#6c63ff";
    public (double Width, double Height) DefaultSize => (276, 324);
    public (double Width, double Height) MinimumSize => (228, 180);

    public IReadOnlyList<OptionSpec> Options { get; } =
    [
        OptionSpec.Toggle("showTimes", "Show when captured", true, "What to show"),
        OptionSpec.Toggle("showCount", "Show the open count", true, "What to show"),
        OptionSpec.Toggle("showDone", "Show done entries", true, "What to show"),
        OptionSpec.Number("maxItems", "Most entries shown", 50, 1, 200, "What to show"),
        OptionSpec.Choice("sort", "Order", "newest", ["newest", "oldest", "abc"], ["Newest first", "Oldest first", "A to Z"], "Layout", "Pinned entries always come first."),
        OptionSpec.Toggle("doneLast", "Move done entries to the bottom", true, "Layout"),
        OptionSpec.Choice("bullet", "Bullet style", "check", ["check", "dot", "dash", "arrow", "star", "none"], ["Checkbox", "Dot", "Dash", "Arrow", "Star", "None"], "Layout"),
        OptionSpec.Toggle("wrap", "Wrap long entries", false, "Layout"),
        OptionSpec.Choice("click", "Clicking an entry", "done", ["done", "archive", "none"], ["Marks it done", "Archives it", "Does nothing"], "Behavior"),
        OptionSpec.Number("autoClear", "Delete done entries after (days)", 7, 0, 90, "Behavior", "0 keeps them."),
        OptionSpec.Toggle("clickEmpty", "Click empty space to capture", true, "Behavior"),
        OptionSpec.Text("prompt", "Capture prompt", "What's on your mind?", "Behavior"),
        OptionSpec.Color("bulletColor", "Bullet / checkbox color", "", "Colors", "Empty = accent color."),
        OptionSpec.Color("pinColor", "Pinned color", "#f59e0b", "Colors"),
    ];

    private static string SampleEntries(params (string Text, double HoursAgo, bool Done, bool Pinned)[] rows)
    {
        var now = DateTime.Now;
        var list = rows.Select(r => new InboxLogic.InboxEntry
        {
            Id = Guid.NewGuid().ToString("N")[..12], Text = r.Text, Created = InboxLogic.Stamp(now.AddHours(-r.HoursAgo)),
            Done = r.Done, DoneAt = r.Done ? InboxLogic.Stamp(now.AddHours(-r.HoursAgo / 2)) : "", Pinned = r.Pinned,
        }).ToList();
        return InboxLogic.Serialize(list);
    }

    public IEnumerable<Action<LauncherItem>> Samples =>
    [
        item => item.Options[DataKey] = SampleEntries(
            ("Book the dentist for next week", 0.1, false, false),
            ("Idea: weekly review template with three questions", 1.5, false, true),
            ("Ask Priya about the venue deposit", 3, false, false),
            ("Buy batteries for the keyboard", 26, false, false),
            ("Return the library books", 30, true, false),
            ("Look up flights to Lisbon in May", 70, false, false)),
        item =>
        {
            item.Options[DataKey] = SampleEntries(
                ("Podcast: the one about deep work", 0.4, false, false),
                ("Rename the 'misc' folder into something useful, maybe by project and year", 5, false, true),
                ("Gift idea for Dad — the field notes set", 20, false, false),
                ("Cancel the trial before Friday", 48, true, false));
            item.Options["bullet"] = "dot";
            item.Options["wrap"] = "true";
            item.Options["sort"] = "oldest";
            item.Accent = "#ec4899";
            item.Width = 324; item.Height = 276;
        },
        item =>
        {
            item.Options[DataKey] = "[]";
            item.Width = 228; item.Height = 180;
        },
    ];

    private static List<InboxLogic.InboxEntry> Load(WidgetContext ctx)
    {
        var list = InboxLogic.Parse(ctx.Get(DataKey));
        InboxLogic.AutoClear(list, DateTime.Now, ctx.GetInt("autoClear"));
        return list;
    }

    /// <summary>Loads fresh, changes the entry with <paramref name="id"/> (or the list when id is null) and saves.</summary>
    private static void Update(WidgetContext ctx, string? id, Action<List<InboxLogic.InboxEntry>, InboxLogic.InboxEntry?> change)
    {
        var list = Load(ctx);
        var entry = id is null ? null : list.FirstOrDefault(e => e.Id == id);
        if (id is not null && entry is null) return;
        change(list, entry);
        ctx.SetState("Inbox.undo", ctx.Get(DataKey), false);
        ctx.Set(DataKey, InboxLogic.Serialize(list));
    }

    private static void Capture(WidgetContext ctx)
    {
        if (ctx.Ask("Capture", ctx.Get("prompt") is { Length: > 0 } p ? p : "What's on your mind?") is not { } text || string.IsNullOrWhiteSpace(text)) return;
        Update(ctx, null, (list, _) => InboxLogic.Add(list, text, DateTime.Now));
    }

    private static void Copy(WidgetContext ctx, string text)
    {
        try { Clipboard.SetText(text); ctx.Status("Copied to the clipboard."); }
        catch (COMException) { ctx.Status("The clipboard is busy; try again."); }
    }

    public IEnumerable<WidgetAction> Actions(WidgetContext ctx)
    {
        if (ctx.State<string?>("Inbox.undo", null) is { } old) yield return new WidgetAction("Undo last change", "", () => { ctx.SetState<string?>("Inbox.undo", null, false); ctx.Set(DataKey, old); });

        var list = Load(ctx);
        bool archive = ctx.State("archive", false);
        yield return new WidgetAction("Capture…", "", () => Capture(ctx));
        int done = list.Count(e => e.Done && !e.Archived);
        if (done > 0) yield return new WidgetAction($"Delete done entries ({done})", "", () => Update(ctx, null, (l, _) => l.RemoveAll(e => e.Done && !e.Archived)));
        int archived = list.Count(e => e.Archived);
        if (archived > 0 || archive) yield return new WidgetAction(archive ? "Hide archived entries" : $"Show archived entries ({archived})", "", () => ctx.SetState("archive", !archive));
        if (archived > 0) yield return new WidgetAction("Unarchive everything", null, () => Update(ctx, null, (l, _) => l.ForEach(e => e.Archived = false)));
    }

    private ContextMenu EntryMenu(WidgetContext ctx, InboxLogic.InboxEntry entry)
    {
        string id = entry.Id;
        var menu = MainWindow.Menu();
        MainWindow.AddMenuItem(menu, entry.Done ? "Mark not done" : "Mark done", () => Update(ctx, id, (_, e) => InboxLogic.SetDone(e!, !e!.Done, DateTime.Now)), "");
        MainWindow.AddMenuItem(menu, entry.Pinned ? "Unpin" : "Pin to top", () => Update(ctx, id, (_, e) => e!.Pinned = !e.Pinned), "");
        MainWindow.AddMenuItem(menu, "Copy text", () => Copy(ctx, entry.Text), "");
        MainWindow.AddMenuItem(menu, "Edit…", () =>
        {
            if (ctx.Ask("Edit entry", "Entry", entry.Text) is { } text && text.Trim().Length > 0) Update(ctx, id, (_, e) => e!.Text = text.Trim());
        }, "");
        MainWindow.AddMenuItem(menu, entry.Archived ? "Unarchive" : "Archive", () => Update(ctx, id, (_, e) => e!.Archived = !e.Archived), "");
        MainWindow.AddMenuItem(menu, "Delete", () => Update(ctx, id, (l, e) => l.Remove(e!)), "");
        return menu;
    }

    public FrameworkElement Build(WidgetContext ctx)
    {
        var now = DateTime.Now;
        var all = Load(ctx);
        bool archive = ctx.State("archive", false);
        var shown = archive
            ? all.Where(e => e.Archived).OrderByDescending(e => e.Created, StringComparer.Ordinal).ToList()
            : InboxLogic.Visible(all, ctx.Get("sort"), ctx.GetBool("showDone"), ctx.GetBool("doneLast"));
        int open = all.Count(e => !e.Done && !e.Archived);
        var bulletBrush = ctx.GetColor("bulletColor");
        var pinBrush = ctx.GetColor("pinColor", Theme.Accent("#f59e0b"));
        string pinHex = ctx.Get("pinColor") is { Length: > 0 } ph ? ph : "#f59e0b";

        FrameworkElement? badge = archive ? WidgetKit.Pill(ctx, "Archive") : ctx.GetBool("showCount") && open > 0 ? WidgetKit.Pill(ctx, open.ToString(System.Globalization.CultureInfo.CurrentCulture), ctx.Item.Accent) : null;
        var actions = archive
            ? new FrameworkElement[] { WidgetKit.IconButton("", "Back to the inbox", () => ctx.SetState("archive", false)) }
            : [WidgetKit.IconButton("", "Capture a thought", () => Capture(ctx), accent: true)];
        var (card, body) = WidgetKit.Frame(ctx, badge, actions);
        card.Loaded += (_, _) => { if (!ctx.IsSnapshot) { string json = InboxLogic.Serialize(all); if (json != ctx.Get(DataKey)) ctx.Set(DataKey, json, false); } };

        string bullet = ctx.Get("bullet");
        string click = ctx.Get("click");
        bool times = ctx.GetBool("showTimes"), wrap = ctx.GetBool("wrap");
        int max = ctx.GetInt("maxItems");
        var list = new StackPanel();
        foreach (var entry in shown.Take(max))
        {
            string id = entry.Id;
            var grid = new Grid();
            double lead = bullet == "none" ? 0 : bullet == "check" ? ctx.Font(16) + 10 : ctx.Font(14);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(lead) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var leadColor = entry.Pinned ? pinBrush : bulletBrush;
            if (bullet == "check")
            {
                double size = ctx.Font(16);
                var box = new Border
                {
                    Width = size, Height = size, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1.5),
                    BorderBrush = entry.Done ? leadColor : Theme.Muted, Background = entry.Done ? leadColor : Brushes.Transparent,
                    VerticalAlignment = wrap ? VerticalAlignment.Top : VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, wrap ? 1 : 0, 0, 0),
                };
                if (entry.Done) box.Child = WidgetKit.Icon("", size * 0.58, Theme.Background);
                grid.Children.Add(box);
            }
            else if (bullet != "none")
            {
                var mark = WidgetKit.Text(ctx, InboxLogic.Bullet(bullet), 13, entry.Done ? Theme.Dim : leadColor, bold: true);
                mark.VerticalAlignment = wrap ? VerticalAlignment.Top : VerticalAlignment.Center;
                grid.Children.Add(mark);
            }
            var text = WidgetKit.Text(ctx, entry.Text, 13, entry.Done ? Theme.Muted : ctx.TextBrush);
            if (wrap) { text.TextWrapping = TextWrapping.Wrap; text.TextTrimming = TextTrimming.None; }
            if (entry.Done) text.TextDecorations = TextDecorations.Strikethrough;
            text.ToolTip = entry.Text;
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            var meta = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = wrap ? VerticalAlignment.Top : VerticalAlignment.Center, Margin = new Thickness(6, wrap ? 1 : 0, 0, 0) };
            if (entry.Pinned) meta.Children.Add(WidgetKit.Icon("", ctx.Font(10.5), pinBrush));
            if (times && InboxLogic.Ago(entry.Created, now) is { Length: > 0 } ago)
            {
                var when = WidgetKit.Muted(ctx, ago, 10.5);
                when.Margin = new Thickness(5, 0, 0, 0);
                if (InboxLogic.ParseStamp(entry.Created) is { } created) when.ToolTip = created.ToString("f", System.Globalization.CultureInfo.CurrentCulture);
                meta.Children.Add(when);
            }
            Grid.SetColumn(meta, 2);
            grid.Children.Add(meta);
            var row = WidgetKit.Row(grid);
            row.Padding = new Thickness(6, 4, 6, 4);
            if (entry.Pinned && !entry.Done) row.Background = Theme.Tint(pinHex, 0.08);
            if (entry.Pinned && !entry.Done) { var rest = row.Background; row.MouseLeave += (_, _) => row.Background = rest; }
            if (archive) WidgetKit.Clickable(row, () => Update(ctx, id, (_, e) => e!.Archived = false), "Click to unarchive");
            else if (click == "done") WidgetKit.Clickable(row, () => Update(ctx, id, (_, e) => InboxLogic.SetDone(e!, !e!.Done, now)), entry.Done ? "Click to mark not done" : "Click to mark done");
            else if (click == "archive") WidgetKit.Clickable(row, () => Update(ctx, id, (_, e) => e!.Archived = true), "Click to archive");
            else row.MouseLeftButtonUp += (_, e) => e.Handled = true;
            row.ContextMenu = EntryMenu(ctx, entry);
            list.Children.Add(row);
        }
        if (shown.Count > max)
        {
            var more = WidgetKit.Muted(ctx, $"+ {shown.Count - max} more", 11);
            more.Margin = new Thickness(6, 4, 0, 0);
            list.Children.Add(more);
        }
        if (shown.Count == 0)
        {
            var empty = new StackPanel { Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            var icon = WidgetKit.Icon("", ctx.Font(26), Theme.Dim);
            icon.Margin = new Thickness(0, 0, 0, 8);
            empty.Children.Add(icon);
            var note = WidgetKit.Muted(ctx, archive ? "Nothing archived." : all.Count == 0 ? "Inbox zero. Click to capture a thought." : "All clear — nothing open.", 12);
            note.TextWrapping = TextWrapping.Wrap;
            note.TextAlignment = TextAlignment.Center;
            empty.Children.Add(note);
            list.Children.Add(empty);
        }

        // Empty space under the list captures a new thought.
        var area = new Grid { Background = Brushes.Transparent };
        area.Children.Add(WidgetKit.Scroll(list));
        if (!archive && ctx.GetBool("clickEmpty"))
        {
            area.Cursor = Cursors.IBeam;
            area.MouseLeftButtonUp += (_, e) => { e.Handled = true; Capture(ctx); };
            area.ToolTip = "Click empty space to capture";
            list.Cursor = Cursors.Arrow;
        }
        body.Children.Add(area);
        // Ages ("5m") move; once a minute is plenty.
        ctx.Tick(() => false, _ => { var fresh = Load(ctx); string json = InboxLogic.Serialize(fresh); if (json != ctx.Get(DataKey)) ctx.Set(DataKey, json); else if (times) ctx.Rebuild(); });
        return card;
    }
}


