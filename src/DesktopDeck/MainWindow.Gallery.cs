using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopDeck.Layout;
using DesktopDeck.Models;
using DesktopDeck.Services;

namespace DesktopDeck;

// The "+ Add" gallery: search, category tabs (All, Favorites, Recent and the sections), a star on each tile, and the helpers templates use to make widgets.
public sealed partial class MainWindow
{
    private const string AllTab = "all", FavoritesTab = "favorites", RecentTab = "recent";
    private static readonly string StarFilled = "", StarOutline = "";
    private const string GroupGlyph = "";

    /// <summary>One thing the gallery can add.</summary>
    private sealed record GalleryEntry(string Kind, string Section, string Name, string Description, string Glyph, string Accent, Action Add);

    private void OpenAddMenu(FrameworkElement anchor)
    {
        // The control center has a real keyboard (typing in the search box works there); the deck behind the windows is not a reliable place to type.
        if (hub.ShowAddGallery(Display)) return;
        var popup = new Popup
        {
            PlacementTarget = anchor, Placement = PlacementMode.Bottom, HorizontalOffset = -720, VerticalOffset = 6,
            StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade,
        };
        popup.Child = BuildGallery(() => popup.IsOpen = false);
        popup.IsOpen = true;
    }

    /// <summary>
    /// The sections the gallery shows, in order. Widgets keep their own <see cref="Widgets.IWidget.Section"/> (the finer grouping); the gallery folds
    /// those into fewer chips here. A section that isn't mapped shows as itself, after the ones listed.
    /// </summary>
    private static readonly string[] DisplaySections = ["Basics", "Time", "Planning & productivity", "Study", "Info & media", "System", "Health", "Fun & visual", "Connected"];

    private static string DisplaySection(string section, string kind) => kind switch
    {
        "bluetooth" => "System",
        "agenda" => "Planning & productivity",
        _ => section switch
        {
            "Planning" or "Productivity" => "Planning & productivity",
            "Online" => "Info & media",
            "Tools" => "System",
            "Fun" or "Visual" => "Fun & visual",
            _ => section,
        },
    };

    private static int DisplayOrder(string section) => Array.IndexOf(DisplaySections, section) is var i && i < 0 ? 99 : i;

    /// <summary>Everything that can be added, in the order the gallery shows it.</summary>
    private IEnumerable<GalleryEntry> GalleryEntries() => GalleryEntriesBySection().Select(e => e with { Section = DisplaySection(e.Section, e.Kind) });

    private IEnumerable<GalleryEntry> GalleryEntriesBySection() =>
    [
        new(ItemKinds.Launcher, "Basics", "Launcher", "Open an app, file, script or shortcut", Theme.Glyphs.App, "#C4684F", AddLauncher),
        new(ItemKinds.Image, "Basics", "Picture", "A photo or image on your desktop", Theme.Glyphs.Picture, "#B47A4E", AddPicture),
        new(ItemKinds.Note, "Basics", "Sticky note", "Jot something down", Theme.Glyphs.Note, "#E2B657", () => AddNew(new LauncherItem { Kind = ItemKinds.Note, Name = "Note", Accent = "#E2B657", ShowHeader = false, Text = "Click to write a note." })),
        new(ItemKinds.Folder, "Basics", "Folder", "Newest files in Downloads or any folder", Theme.Glyphs.Folder, "#00d4ff", () => AddNew(new LauncherItem { Kind = ItemKinds.Folder, Name = "Downloads", Accent = "#00d4ff", Target = FolderListing.Downloads })),
        new(ItemKinds.Group, "Basics", "Group", "Several widgets as tabs of one card", GroupGlyph, "#6c63ff", AddGroup),
        new(ItemKinds.Clock, "Time", "Time", "Clock and date", Theme.Glyphs.Time, "#9B9A5E", () => AddNew(new LauncherItem { Kind = ItemKinds.Clock, Name = "Time" })),
        new(ItemKinds.Timer, "Time", "Timer", "Pomodoro focus timer", Theme.Glyphs.Timer, "#C4684F", () => AddNew(new LauncherItem { Kind = ItemKinds.Timer, Name = "Focus timer" })),
        new(ItemKinds.Countdown, "Time", "Countdown", "Days until a date or event", Theme.Glyphs.Countdown, "#ec4899", AddCountdown),
        new(ItemKinds.Schedule, "Time", "Schedule", "Your week of recurring blocks", Theme.Glyphs.Calendar, "#6c63ff", () => AddNew(new LauncherItem { Kind = ItemKinds.Schedule, Name = "Schedule" })),
        new(ItemKinds.Tasks, "Planning", "Today's tasks", "A checklist that rolls over each day", Theme.Glyphs.Tasks, "#C4684F", () => AddNew(new LauncherItem { Kind = ItemKinds.Tasks, Name = "Today's Tasks", DailyReset = true, LastDate = Planner.Key(Planner.Today()) })),
        new(ItemKinds.Habits, "Planning", "Habits", "Daily habits with streaks", Theme.Glyphs.Habits, "#22c55e", () => AddNew(new LauncherItem { Kind = ItemKinds.Habits, Name = "Habits", View = "today" })),
        new(ItemKinds.Goals, "Planning", "Weekly goals", "Goals with steps and progress", Theme.Glyphs.Goals, "#9B9A5E", () => AddNew(new LauncherItem { Kind = ItemKinds.Goals, Name = "Goals for this week", Accent = "#9B9A5E" })),
        new(ItemKinds.Counter, "Planning", "Counter", "Tap to count anything", Theme.Glyphs.Counter, "#B47A4E", () => AddNew(new LauncherItem { Kind = ItemKinds.Counter, Name = "Counter", Accent = "#B47A4E" })),
        new(ItemKinds.Media, "Info & media", "Media player", "What's playing, with controls", Theme.Glyphs.Music, "#22c55e", () => AddNew(new LauncherItem { Kind = ItemKinds.Media, Name = "Now playing", Accent = "#22c55e" })),
        new(ItemKinds.Weather, "Info & media", "Weather", "Current weather and forecast", Theme.Glyphs.Sun, "#f59e0b", AddWeather),
        .. Widgets.WidgetRegistry.All.Select(w => new GalleryEntry(w.Kind, w.Section, w.Name, w.Description, w.Glyph, w.Accent, () => AddPlugin(w))),
    ];

    private void AddGroup() => AddNew(new LauncherItem { Kind = ItemKinds.Group, Name = "Group", Accent = "#6c63ff" });

    /// <summary>
    /// A fresh widget of a kind with the same starting content "+ Add" gives it, without asking anything (used by starter layouts). Null for a kind that
    /// doesn't exist.
    /// </summary>
    internal static LauncherItem? NewItemOfKind(string kind)
    {
        switch (kind)
        {
            case ItemKinds.Tasks: return new LauncherItem { Kind = kind, Name = "Today's Tasks", DailyReset = true, LastDate = Planner.Key(Planner.Today()) };
            case ItemKinds.Timer: return new LauncherItem { Kind = kind, Name = "Focus timer" };
            case ItemKinds.Schedule: return new LauncherItem { Kind = kind, Name = "Schedule" };
            case ItemKinds.Media: return new LauncherItem { Kind = kind, Name = "Now playing", Accent = "#22c55e" };
            case ItemKinds.Habits: return new LauncherItem { Kind = kind, Name = "Habits", View = "today" };
            case ItemKinds.Clock: return new LauncherItem { Kind = kind, Name = "Time" };
            case ItemKinds.Weather: return new LauncherItem { Kind = kind, Name = "Weather", Accent = "#f59e0b", Units = WeatherService.DefaultUnits };
        }
        return Widgets.WidgetRegistry.Find(kind) is { } plugin ? new LauncherItem { Kind = plugin.Kind, Name = plugin.Name, Accent = plugin.Accent } : null;
    }

    /// <summary>The size of the deck and the areas the buttons at its corners cover, for placing a starter layout.</summary>
    internal (double Width, double Height, List<TileRect> Reserved) TemplateViewport() => (canvas.ActualWidth, canvas.ActualHeight, Reserved().Concat(IconRects()).ToList());

    /// <summary>The same gallery for the control center's "+ Add widget": adds to this monitor's deck without switching the decks to Edit mode.</summary>
    internal FrameworkElement BuildGalleryForPanel(Action close, double maxHeight) => BuildGallery(close, maxHeight, quiet: true);

    private FrameworkElement BuildGallery(Action? close = null, double? maxHeight = null, bool quiet = false)
    {
        var entries = GalleryEntries().ToList();
        string category = AllTab;
        string needle = "";
        Action? addFirst = null;

        var root = new StackPanel { Width = 3 * 256 + 12 };
        var heading = Theme.Label("Add to " + (hub.Displays.Count > 1 ? Display.Name : "your desktop"), 15);
        heading.FontWeight = FontWeights.SemiBold;
        heading.Margin = new Thickness(6, 2, 0, 8);
        root.Children.Add(heading);

        var (searchHost, search) = PanelKit.SearchBox("Search widgets by name, what they do or section", 240);
        searchHost.Width = double.NaN;
        searchHost.HorizontalAlignment = HorizontalAlignment.Stretch;
        searchHost.Margin = new Thickness(6, 0, 8, 10);
        search.Tag = "search";
        AutomationProperties.SetName(search, "Search widgets");
        root.Children.Add(searchHost);

        var chips = new WrapPanel { Margin = new Thickness(2, 0, 0, 4) };
        root.Children.Add(chips);

        var results = new StackPanel();
        // Tall galleries scroll (mouse wheel) instead of running off the screen.
        var scroller = new ScrollViewer { Content = results, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 4, 0) };
        if (close is null) { scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled; }
        else if (maxHeight is { } room) scroller.Height = Math.Max(160, room - 150);
        else scroller.MaxHeight = Math.Max(300, canvas.ActualHeight * 0.82 - 150);
        root.Children.Add(scroller);

        bool Matches(GalleryEntry entry)
        {
            foreach (var token in needle.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (!(entry.Name + " " + entry.Description + " " + entry.Section).Contains(token, StringComparison.CurrentCultureIgnoreCase)) return false;
            return true;
        }

        FrameworkElement Tile(GalleryEntry entry, Action refresh)
        {
            var icon = PanelKit.IconTile(entry.Glyph, entry.Accent, 36);
            var name = Theme.Label(entry.Name, 13);
            name.FontWeight = FontWeights.SemiBold;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            var description = Theme.Label(entry.Description, 11.5, Theme.Muted);
            description.TextTrimming = TextTrimming.CharacterEllipsis;
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(11, 0, 4, 0) };
            text.Children.Add(name);
            text.Children.Add(description);

            bool favorite = WidgetOrganizer.IsFavorite(settings, entry.Kind);
            var star = Theme.Icon(favorite ? StarFilled : StarOutline, 14, favorite ? Theme.Brush("#F2C14E") : Theme.Dim);
            var starHost = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Child = star };
            Action paintStar = () =>
            {
                bool on = WidgetOrganizer.IsFavorite(settings, entry.Kind);
                star.Text = on ? StarFilled : StarOutline;
                star.Foreground = on ? Theme.Brush("#F2C14E") : Theme.Dim;
                starHost.ToolTip = on ? "Remove from favorites" : "Add to favorites";
                AutomationProperties.SetName(starHost, (on ? "Remove from favorites: " : "Favorite: ") + entry.Name);
            };
            paintStar();
            starHost.MouseEnter += (_, _) => starHost.Background = PanelKit.Hover;
            starHost.MouseLeave += (_, _) => starHost.Background = Brushes.Transparent;
            starHost.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                hub.ToggleFavoriteWidget(entry.Kind);
                if (category == FavoritesTab) refresh(); else paintStar();
            };
            starHost.MouseLeftButtonDown += (_, e) => e.Handled = true;

            var row = new DockPanel();
            DockPanel.SetDock(icon, Dock.Left);
            DockPanel.SetDock(starHost, Dock.Right);
            row.Children.Add(icon);
            row.Children.Add(starHost);
            row.Children.Add(text);
            var tile = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 7, 6, 7), Margin = new Thickness(0, 0, 4, 4), Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = row, Width = 252 };
            tile.MouseEnter += (_, _) => tile.Background = Theme.Surface3;
            tile.MouseLeave += (_, _) => tile.Background = Brushes.Transparent;
            tile.MouseLeftButtonUp += (_, e) => { e.Handled = true; Choose(entry); };
            AutomationProperties.SetName(tile, "Add " + entry.Name);
            return tile;
        }

        void Choose(GalleryEntry entry)
        {
            close?.Invoke();
            int before = settings.Items.Count;
            if (quiet) hub.QuietAdd = true;
            try { if (AskLinkOnAdd(entry.Kind, entry.Name)) entry.Add(); }
            finally { pendingLink = null; if (quiet) hub.QuietAdd = false; }
            if (settings.Items.Count > before) hub.NoteRecentWidget(entry.Kind);
        }

        void Fill()
        {
            results.Children.Clear();
            addFirst = null;
            var pool = category switch
            {
                FavoritesTab => entries.Where(e => WidgetOrganizer.IsFavorite(settings, e.Kind)).ToList(),
                RecentTab => settings.RecentWidgets.Select(k => entries.FirstOrDefault(e => e.Kind == k)).Where(e => e is not null).Select(e => e!).ToList(),
                AllTab => entries,
                _ => entries.Where(e => e.Section == category).ToList(),
            };
            var shown = pool.Where(Matches).ToList();
            if (shown.Count == 0)
            {
                string message = needle.Trim().Length > 0
                    ? $"No widget matches \"{needle.Trim()}\"" + (category == AllTab ? "." : " here. Try the All tab.")
                    : category == FavoritesTab ? "No favorites yet. Click the star on a widget to keep it here."
                    : category == RecentTab ? "Nothing here yet. The widgets you add show up here."
                    : "Nothing here.";
                results.Children.Add(PanelKit.Empty(message));
                return;
            }
            addFirst = () => Choose(shown[0]);
            if (category == AllTab)
            {
                foreach (var section in shown.GroupBy(e => e.Section).OrderBy(g => DisplayOrder(g.Key)))
                {
                    var title = Theme.WidgetTitle(section.Key);
                    title.Margin = new Thickness(6, results.Children.Count == 0 ? 4 : 12, 0, 6);
                    results.Children.Add(title);
                    var grid = new UniformGrid { Columns = 3 };
                    foreach (var entry in section) grid.Children.Add(Tile(entry, Fill));
                    results.Children.Add(grid);
                }
                return;
            }
            var flat = new UniformGrid { Columns = 3, Margin = new Thickness(0, 4, 0, 0) };
            foreach (var entry in shown) flat.Children.Add(Tile(entry, Fill));
            results.Children.Add(flat);
        }

        void DrawChips()
        {
            chips.Children.Clear();
            var tabs = new List<(string Id, string Label)> { (AllTab, "All"), (FavoritesTab, "★ Favorites"), (RecentTab, "Recent") };
            tabs.AddRange(entries.Select(e => e.Section).Distinct().OrderBy(DisplayOrder).Select(s => (s, s)));
            foreach (var (id, label) in tabs)
            {
                bool on = id == category;
                var text = Theme.Label(label, 12, on ? Theme.Text : Theme.Text2);
                text.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
                var chip = new Border
                {
                    CornerRadius = new CornerRadius(11), Padding = new Thickness(10, 3, 10, 4), Margin = new Thickness(4, 0, 0, 6), Cursor = Cursors.Hand, Child = text,
                    Background = on ? Theme.Tint(Theme.PrimaryHex, 0.24) : Theme.Surface3, BorderBrush = on ? Theme.Primary : Theme.Line, BorderThickness = new Thickness(1),
                };
                chip.MouseEnter += (_, _) => { if (id != category) chip.Background = Theme.SurfaceHover; };
                chip.MouseLeave += (_, _) => { if (id != category) chip.Background = Theme.Surface3; };
                string chosen = id;
                chip.MouseLeftButtonUp += (_, e) => { e.Handled = true; category = chosen; DrawChips(); Fill(); scroller.ScrollToTop(); };
                AutomationProperties.SetName(chip, label + " widgets");
                chips.Children.Add(chip);
            }
        }

        search.TextChanged += (_, _) => { needle = search.Text; Fill(); scroller.ScrollToTop(); };
        search.KeyDown += (_, e) => { if (e.Key == Key.Enter && addFirst is not null) { addFirst(); e.Handled = true; } };
        DrawChips();
        Fill();
        // In the control center the keyboard is ready for typing at once.
        if (close is not null && quiet) root.Loaded += (_, _) => search.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => search.Focus()));

        return new Border
        {
            Margin = new Thickness(6, 4, 16, 18), Padding = new Thickness(12, 12, 8, 10), CornerRadius = new CornerRadius(16),
            Background = Theme.Surface, BorderBrush = Theme.LineStrong, BorderThickness = new Thickness(1), Child = root,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Direction = 270, Opacity = 0.5, Color = Colors.Black },
        };
    }
}
