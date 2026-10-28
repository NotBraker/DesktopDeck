using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopDeck.Models;

namespace DesktopDeck.Widgets;

/// <summary>
/// Windows' Recent items as a clickable list, newest first. The shortcuts are read in the background at most once
/// a minute (RecentShortcuts.cs); file icons are added after the first paint. Filtering and wording live in RecentFilesLogic.cs.
/// </summary>
internal sealed class RecentFilesWidget : IWidget
{
    public string Kind => "recent-files";
    public string Name => "Recent files";
    public string Description => "What you opened lately: click to open";
    public string Section => "Basics";
    public string Glyph => "\uE81C";
    public string Accent => "#d8af68";
    public (double Width, double Height) DefaultSize => (324, 324);
    public (double Width, double Height) MinimumSize => (180, 132);

    public IReadOnlyList<OptionSpec> Options { get; } =
    [
        OptionSpec.Choice("type", "Show", "all", ["all", "documents", "images"], ["Everything", "Documents", "Pictures"], "What to show"),
        OptionSpec.Lines("extensions", "Only these extensions", "", "What to show", "One per line, e.g. pdf or *.docx. When filled, it replaces the choice above."),
        OptionSpec.Toggle("hideFolders", "Hide folders", true, "What to show"),
        OptionSpec.Toggle("network", "Include network locations", false, "What to show", "Off by default: unreachable shares can make the list slow to load."),
        OptionSpec.Number("count", "Number of items", 12, 1, 50, "What to show"),
        OptionSpec.Toggle("icons", "Show file icons", true, "Layout"),
        OptionSpec.Number("iconSize", "Icon size", 24, 16, 40, "Layout"),
        OptionSpec.Choice("subtitle", "Show under each name", "both", ["both", "folder", "date", "none"], ["Folder and when", "Folder", "When it was opened", "Nothing"], "Layout"),
        OptionSpec.Number("textSize", "File name size", 13, 10, 22, "Layout"),
        OptionSpec.Toggle("dividers", "Lines between rows", false, "Layout"),
        OptionSpec.Number("refresh", "Look for new files every (minutes)", 1, 1, 60, "Behavior", "The list is never read more often than once a minute."),
    ];

    public IEnumerable<Action<LauncherItem>> Samples =>
    [
        _ => { },
        item => { item.Options["type"] = "images"; item.Options["subtitle"] = "folder"; item.Options["iconSize"] = "32"; item.Options["dividers"] = "true"; item.Width = 276; item.Height = 276; },
        item => { item.Options["hideFolders"] = "false"; item.Options["subtitle"] = "date"; item.Options["textSize"] = "12"; item.Options["iconSize"] = "18"; item.Width = 372; item.Height = 228; },
    ];

    private sealed record Listing(DateTimeOffset At, bool Network, List<RecentFilesLogic.Entry> Entries);

    private static string ListKey => "recent-files:list";

    private static Listing? Latest() => WidgetContext.Cache.TryGetValue(ListKey, out var value) ? value as Listing : null;

    /// <summary>Reads the Recent shortcuts in the background unless they were read less than <paramref name="minutes"/> ago.</summary>
    private static void Refresh(WidgetContext ctx)
    {
        if (ctx.IsSnapshot) return;
        bool network = ctx.GetBool("network");
        var last = Latest();
        double minutes = Math.Max(1, ctx.GetNumber("refresh"));
        if (last is not null && last.Network == network && DateTimeOffset.Now - last.At < TimeSpan.FromMinutes(minutes)) return;
        ctx.RunOnce(ListKey, async () =>
        {
            var entries = await Task.Run(() => RecentShortcuts.Read(400, network));
            var old = Latest();
            WidgetContext.Cache[ListKey] = new Listing(DateTimeOffset.Now, network, entries);
            if (old is null || old.Network != network || !old.Entries.SequenceEqual(entries)) ctx.Rebuild();
        });
    }

    private static IEnumerable<RecentFilesLogic.Entry> SampleEntries(DateTime now) =>
    [
        new(@"C:\Users\sam\Documents\Quarterly report.docx", now.AddMinutes(-12), false),
        new(@"C:\Users\sam\Pictures\Morning light.jpg", now.AddHours(-2), false),
        new(@"C:\Users\sam\Documents\Budget 2026.xlsx", now.AddHours(-5), false),
        new(@"C:\Users\sam\Projects\Garden plan", now.AddHours(-9), true),
        new(@"C:\Users\sam\Downloads\Invoice-0412.pdf", now.AddDays(-1), false),
        new(@"C:\Users\sam\Pictures\Mountain lake.png", now.AddDays(-2), false),
        new(@"C:\Users\sam\Documents\Meeting notes.txt", now.AddDays(-3), false),
        new(@"C:\Users\sam\Music\Evening playlist.mp3", now.AddDays(-5), false),
        new(@"C:\Users\sam\Documents\Thesis draft.pdf", now.AddDays(-9), false),
    ];

    public FrameworkElement Build(WidgetContext ctx)
    {
        Refresh(ctx);
        string subtitleMode = ctx.Get("subtitle");
        // Once a minute: look for new files, and move the "5 min ago" wording on.
        ctx.Tick(() => false, _ =>
        {
            Refresh(ctx);
            if (subtitleMode is "both" or "date") ctx.Rebuild();
        });

        var openFolder = WidgetKit.IconButton("\uE838", "Open the Recent folder", () => { if (!ctx.IsSnapshot && !ctx.IsEditing) ctx.Open("shell:recent"); }, 24);
        var (card, body) = WidgetKit.Frame(ctx, null, openFolder);

        var now = DateTime.Now;
        var listing = ctx.IsSnapshot ? null : Latest();
        var all = ctx.IsSnapshot ? SampleEntries(now) : listing?.Entries ?? [];
        var entries = RecentFilesLogic.Filter(all, ctx.Get("type"), ctx.GetLines("extensions"), ctx.GetBool("hideFolders"), ctx.GetInt("count"));
        if (entries.Count == 0)
        {
            string note = !ctx.IsSnapshot && listing is null ? "Looking for your recent files…"
                : ctx.Get("type") != "all" || ctx.GetLines("extensions").Length > 0 ? "No recent files of that kind. Change the filter in Customize…"
                : "Nothing recent yet. Files you open in Windows show up here.";
            body.Children.Add(WidgetKit.Empty(ctx, note));
            return card;
        }

        bool showIcons = ctx.GetBool("icons");
        double iconSize = ctx.GetNumber("iconSize");
        int iconPixels = iconSize > 24 ? 48 : 32;
        var stack = new StackPanel();
        foreach (var entry in entries)
        {
            var row = new Grid { Background = Brushes.Transparent };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            if (showIcons)
            {
                var slot = new Grid { Width = iconSize, Height = iconSize, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
                var glyph = WidgetKit.Icon(GlyphFor(entry), iconSize * 0.72, Theme.Muted);
                var image = new Image { Stretch = Stretch.Uniform };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                slot.Children.Add(glyph);
                slot.Children.Add(image);
                row.Children.Add(slot);
                if (!ctx.IsSnapshot)
                {
                    // Shell icons are drawn after the first paint, like the folder widget does.
                    string path = entry.Path;
                    ctx.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        if (ImageCache.ShellIcon(path, iconPixels) is { } source) { image.Source = source; glyph.Visibility = Visibility.Collapsed; }
                    }));
                }
            }
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            string name = System.IO.Path.GetFileName(entry.Path.TrimEnd('\\'));
            text.Children.Add(WidgetKit.Text(ctx, name.Length > 0 ? name : entry.Path, ctx.GetNumber("textSize"), bold: true));
            string subtitle = RecentFilesLogic.Subtitle(entry, subtitleMode, now);
            if (subtitle.Length > 0) text.Children.Add(WidgetKit.Muted(ctx, subtitle, 10.5));
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            var item = WidgetKit.Row(row);
            WidgetKit.Clickable(item, () => { if (!ctx.IsSnapshot && !ctx.IsEditing) ctx.Open(entry.Path); }, entry.Path);
            item.ContextMenu = MenuFor(ctx, entry);
            stack.Children.Add(item);
            if (ctx.GetBool("dividers")) stack.Children.Add(WidgetKit.Divider());
        }
        body.Children.Add(WidgetKit.Scroll(stack));
        return card;
    }

    private static string GlyphFor(RecentFilesLogic.Entry entry) => RecentFilesLogic.Category(entry) switch
    {
        "folder" => "\uE8B7",
        "image" => "\uE91B",
        "document" => "\uE8A5",
        "audio" => "\uE8D6",
        "video" => "\uE714",
        _ => "\uE7C3",
    };

    private static ContextMenu MenuFor(WidgetContext ctx, RecentFilesLogic.Entry entry)
    {
        var menu = new ContextMenu();
        void Add(string title, Action run)
        {
            var item = new MenuItem { Header = title };
            item.Click += (_, e) => { e.Handled = true; if (!ctx.IsSnapshot) run(); };
            menu.Items.Add(item);
        }
        Add("Open", () => ctx.Open(entry.Path));
        Add("Show in folder", () => ShowInFolder(entry.Path));
        Add("Copy path", () =>
        {
            try { Clipboard.SetText(entry.Path); ctx.Status("Path copied."); }
            catch (ExternalException) { ctx.Status("The clipboard is busy; try again."); }
        });
        return menu;
    }

    /// <summary>Opens Explorer with the item selected.</summary>
    private static void ShowInFolder(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { }
    }

    public IEnumerable<WidgetAction> Actions(WidgetContext ctx) =>
    [
        new("Refresh the list", "\uE72C", () => { WidgetContext.Cache.TryRemove(ListKey, out _); ctx.Rebuild(); }),
        new("Open the Recent folder", "\uE838", () => ctx.Open("shell:recent")),
    ];
}
