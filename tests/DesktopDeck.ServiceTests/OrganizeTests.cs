using DesktopDeck.Layout;
using DesktopDeck.Models;
using DesktopDeck.Services;

/// <summary>Organizing widgets: gallery favorites and recents, widget groups (tabs in one card), and the starter layout placement. No UI.</summary>
public static class OrganizeTests
{
    public static void Run(Action<bool, string> check, string folder)
    {
        var store = new SettingsStore(Path.Combine(folder, "organize"));
        FavoritesAndRecents(check, store);
        Groups(check, store);
        GroupsAndLayouts(check, store);
        Templates(check);
        Starters(check);
    }

    private static void FavoritesAndRecents(Action<bool, string> check, SettingsStore store)
    {
        var s = new DeckSettings();
        check(WidgetOrganizer.ToggleFavorite(s, "github") && WidgetOrganizer.ToggleFavorite(s, "timer") && s.FavoriteWidgets.SequenceEqual(["timer", "github"]), "Organize: starring puts the newest favorite first");
        check(!WidgetOrganizer.ToggleFavorite(s, "timer") && s.FavoriteWidgets.SequenceEqual(["github"]) && !WidgetOrganizer.IsFavorite(s, "timer"), "Organize: starring again removes the favorite");
        for (int i = 1; i <= 12; i++) WidgetOrganizer.NoteRecent(s, "kind-" + i);
        check(s.RecentWidgets.Count == WidgetOrganizer.MaxRecent && s.RecentWidgets[0] == "kind-12" && s.RecentWidgets[^1] == "kind-5", "Organize: recents keep the last 8, newest first");
        WidgetOrganizer.NoteRecent(s, "kind-6");
        check(s.RecentWidgets[0] == "kind-6" && s.RecentWidgets.Count == WidgetOrganizer.MaxRecent && s.RecentWidgets.Distinct().Count() == s.RecentWidgets.Count, "Organize: adding a kind again moves it to the front without repeating it");

        // A hand-edited or damaged file is repaired on load.
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        File.WriteAllText(store.FilePath, """
            { "SchemaVersion": 5, "Items": [], "FavoriteWidgets": ["a", " b ", "a", "", null, "c"], "RecentWidgets": ["1","2","3","4","5","6","7","8","9","10","1", "  "] }
            """);
        var loaded = store.Load();
        check(loaded.FavoriteWidgets.SequenceEqual(["a", "b", "c"]), "Organize: favorites are trimmed and de-duplicated on load, empty entries dropped");
        check(loaded.RecentWidgets.SequenceEqual(["1", "2", "3", "4", "5", "6", "7", "8"]), "Organize: recents are capped to 8 on load");
        File.WriteAllText(store.FilePath, """{ "SchemaVersion": 5, "Items": [], "FavoriteWidgets": null, "RecentWidgets": null }""");
        var empty = store.Load();
        check(empty.FavoriteWidgets.Count == 0 && empty.RecentWidgets.Count == 0, "Organize: missing lists load empty (files from before the gallery had none)");
        store.Save(s);
        var back = store.Load();
        check(back.FavoriteWidgets.SequenceEqual(s.FavoriteWidgets) && back.RecentWidgets.SequenceEqual(s.RecentWidgets), "Organize: favorites and recents round-trip");
    }

    private static DeckSettings Deck() => new()
    {
        Items =
        [
            new LauncherItem { Id = "gmail", Kind = "gmail", Name = "Gmail", X = 0.1, Y = 0.1 },
            new LauncherItem { Id = "github", Kind = "github", Name = "GitHub", X = 0.4, Y = 0.1, Width = 276, Height = 276 },
            new LauncherItem { Id = "notion", Kind = "notion", Name = "Notion", X = 0.7, Y = 0.1 },
            new LauncherItem { Id = "clock", Kind = ItemKinds.Clock, Name = "Clock", X = 0.1, Y = 0.6 },
        ],
    };

    private static void Groups(Action<bool, string> check, SettingsStore store)
    {
        var s = Deck();
        var gmail = s.Items[0];
        var group = WidgetOrganizer.NewGroupAround(s, gmail, "Inbox")!;
        var (gw, gh) = ItemKinds.DefaultSize("gmail");
        check(group.Kind == ItemKinds.Group && s.Items[0] == group && s.Items[1] == gmail && gmail.GroupId == group.Id, "Groups: a new group sits where its first widget was and holds it as a tab");
        check(group.Width == gw && group.Height == gh + WidgetOrganizer.TabStripHeight && group.X == 0.1 && group.Y == 0.1, "Groups: the card is the widget's size plus a tab strip, at its place");
        check(WidgetOrganizer.AddToGroup(s, s.Items.Single(i => i.Id == "notion"), group) && WidgetOrganizer.AddToGroup(s, s.Items.Single(i => i.Id == "github"), group), "Groups: widgets can be added");
        check(WidgetOrganizer.Children(s, group.Id).Select(c => c.Id).SequenceEqual(["gmail", "notion", "github"]), "Groups: tabs are in the order the widgets joined");
        check(!WidgetOrganizer.AddToGroup(s, group, group) && WidgetOrganizer.NewGroupAround(s, group, "Nested") is null && !WidgetOrganizer.CanJoin(group, group), "Groups: a group cannot hold itself or another group");
        check(WidgetOrganizer.Selected(s, group)!.Id == "github", "Groups: the tab that was added last is selected");
        WidgetOrganizer.Select(group, "notion");

        store.Save(s);
        var loaded = store.Load();
        var lg = loaded.Items.Single(i => i.Kind == ItemKinds.Group);
        check(lg.Name == "Inbox" && lg.Id == group.Id && WidgetOrganizer.Children(loaded, lg.Id).Select(c => c.Id).SequenceEqual(["gmail", "notion", "github"]), "Groups: a group and its tabs round-trip");
        check(WidgetOrganizer.Selected(loaded, lg)!.Id == "notion" && lg.Options[WidgetOrganizer.TabKey] == "notion", "Groups: the selected tab is remembered (data.tab)");
        check(loaded.Items.Single(i => i.Id == "clock").GroupId == "", "Groups: widgets outside the group stay outside");
        var copy = lg.Copy();
        copy.Options[WidgetOrganizer.TabKey] = "gmail";
        check(lg.Options[WidgetOrganizer.TabKey] == "notion", "Groups: copies don't share the remembered tab");

        // Taking one out puts it back by itself, next to the group; if it was the shown tab, another one shows.
        var notion = loaded.Items.Single(i => i.Id == "notion");
        check(WidgetOrganizer.Release(loaded, notion) && notion.GroupId == "" && notion.X > lg.X && notion.Width == 0 && WidgetOrganizer.Selected(loaded, lg)!.Id == "gmail", "Groups: Remove from group returns the widget to the deck");
        check(!WidgetOrganizer.Release(loaded, notion), "Groups: a widget that is not in a group can't be released");

        // Removing the group returns every child, shown.
        loaded.Items.Single(i => i.Id == "gmail").Hidden = true;
        var released = WidgetOrganizer.Dissolve(loaded, lg);
        check(released.Count == 2 && !loaded.Items.Contains(lg) && loaded.Items.All(i => i.GroupId == "") && released.All(c => !c.Hidden), "Groups: removing a group returns its widgets to the deck, visible");

        // Repair: a missing group, nesting and a stale tab.
        File.WriteAllText(store.FilePath, """
            { "SchemaVersion": 5, "Items": [
              { "Id": "g", "Kind": "group", "Name": "G", "Target": "", "Arguments": "", "WorkingDirectory": "", "Accent": "#fff", "X": 0, "Y": 0, "Options": { "data.tab": "gone" } },
              { "Id": "a", "Kind": "clock", "Name": "A", "Target": "", "Arguments": "", "WorkingDirectory": "", "Accent": "#fff", "X": 0, "Y": 0, "GroupId": "g" },
              { "Id": "b", "Kind": "clock", "Name": "B", "Target": "", "Arguments": "", "WorkingDirectory": "", "Accent": "#fff", "X": 0, "Y": 0, "GroupId": "missing" },
              { "Id": "g2", "Kind": "group", "Name": "G2", "Target": "", "Arguments": "", "WorkingDirectory": "", "Accent": "#fff", "X": 0, "Y": 0, "GroupId": "g" }
            ] }
            """);
        var repaired = store.Load();
        check(repaired.Items.Single(i => i.Id == "a").GroupId == "g" && repaired.Items.Single(i => i.Id == "b").GroupId == "" && repaired.Items.Single(i => i.Id == "g2").GroupId == "", "Groups: orphans return to the deck and groups can't be nested on load");
        check(!repaired.Items.Single(i => i.Id == "g").Options.ContainsKey(WidgetOrganizer.TabKey) && WidgetOrganizer.Selected(repaired, repaired.Items.Single(i => i.Id == "g"))!.Id == "a", "Groups: a remembered tab that is gone is forgotten");
        var old = new SettingsStore(Path.Combine(Path.GetDirectoryName(store.FilePath)!, "old"));
        Directory.CreateDirectory(Path.GetDirectoryName(old.FilePath)!);
        File.WriteAllText(old.FilePath, """{ "SchemaVersion": 5, "Items": [ { "Id": "x", "Name": "X", "Target": "t", "Arguments": "", "WorkingDirectory": "", "Accent": "#fff", "X": 0.2, "Y": 0.3 } ] }""");
        check(old.Load().Items[0].GroupId == "", "Groups: files from before groups load with no groups");
    }

    private static void GroupsAndLayouts(Action<bool, string> check, SettingsStore store)
    {
        var s = Deck();
        LayoutEngine.EnsureDefaults(s);
        var home = LayoutEngine.Active(s, LayoutLayers.Background)!;
        var group = WidgetOrganizer.NewGroupAround(s, s.Items[0], "Inbox")!;
        WidgetOrganizer.AddToGroup(s, s.Items.Single(i => i.Id == "github"), group);
        store.Save(s);
        check(LayoutEngine.VisibleCount(s, home) == 3, "Groups: a layout counts the group and the widgets outside it, not the tabs");
        group.Hidden = true;
        store.Save(s);
        check(LayoutEngine.VisibleCount(s, home) == 2 && !home.Entries[group.Id].Visible, "Groups: hiding the group hides what is in it (the layout records the group hidden)");

        var other = LayoutEngine.CreateFromCurrent(s, LayoutLayers.Background, "Other");
        group.Hidden = false;
        store.Save(s);
        LayoutEngine.Switch(s, LayoutLayers.Background, home.Id);
        check(group.Hidden && LayoutEngine.VisibleCount(s, other) == 3, "Groups: a layout switch hides and shows the group like any widget");
        var loaded = store.Load();
        check(loaded.Items.Count(i => i.GroupId == group.Id) == 2 && LayoutEngine.Of(loaded, LayoutLayers.Background).Count() == 2, "Groups: groups and layouts survive a restart together");
    }

    private static void Templates(Action<bool, string> check)
    {
        check(WidgetTemplates.All.Select(t => t.Id).Distinct().Count() == 5 && WidgetTemplates.All.Select(t => t.Name).SequenceEqual(["Student", "Vibe coding", "Focus", "Developer", "Minimal"]), "Templates: Student, Vibe coding, Focus, Developer and Minimal");
        check(WidgetTemplates.All.All(t => t.Kinds.Length >= 4 && t.Kinds.Distinct().Count() == t.Kinds.Length && t.Kinds.All(k => ItemKinds.All.Contains(k) || ItemKinds.IsPluginId(k))), "Templates: every template lists distinct, valid widget kinds");

        // Placing on a typical 1920x1080 screen at 125% (1536x864 DIP) with the corner buttons reserved.
        double width = 1536, height = 864;
        var reserved = new List<TileRect> { new(-100000, -100000, 100000, 300000), new(width, -100000, 100000, 300000), new(-100000, -100000, 300000, 100000), new(-100000, height, 300000, 100000), new(-100000, -100000, 100000 + 120, 100000 + 60), new(width - 330, -100000, 400000, 100000 + 60) };
        var sizes = new Dictionary<string, (double W, double H)>
        {
            ["homework"] = (372, 324), ["schedule"] = (756, 564), ["flashcards"] = (372, 324), ["study-log"] = (372, 276), ["timer"] = (276, 324),
        };
        var slots = WidgetTemplates.All[0].Kinds.Select(k => { var (w, h) = sizes[k]; return new TemplateSlot(w, h, 276, 228); }).ToList();
        var placed = TemplatePlacer.Place(slots, width, height, reserved, keepCenterClear: false, grid: DeckLayout.Grid);
        check(placed.Count == 5 && placed.All(p => p is not null), "Templates: every widget of the Student layout finds a place on a normal screen");
        var rects = placed.Select(p => p!.Value).ToList();
        bool clean = true;
        for (int i = 0; i < rects.Count; i++)
        {
            clean &= rects[i].X >= 0 && rects[i].Y >= 0 && rects[i].Right <= width && rects[i].Bottom <= height && DeckLayout.IsAllowed(rects[i], width, height, false);
            clean &= !reserved.Any(r => DeckLayout.Overlaps(rects[i], r));
            for (int j = i + 1; j < rects.Count; j++) clean &= !DeckLayout.Overlaps(rects[i], rects[j]);
        }
        check(clean, "Templates: placed widgets stay on screen, clear of the corner buttons and never overlap");
        check(rects.All(r => Math.Abs((r.X / DeckLayout.Grid) - Math.Round(r.X / DeckLayout.Grid)) < 1e-9 && Math.Abs((r.Y / DeckLayout.Grid) - Math.Round(r.Y / DeckLayout.Grid)) < 1e-9), "Templates: widgets are placed on the grid");

        // A tiny screen: the biggest widgets shrink or are left out rather than overlapping.
        var tiny = TemplatePlacer.Place(slots, 700, 400, [], keepCenterClear: false, grid: DeckLayout.Grid);
        var kept = tiny.Where(p => p is not null).Select(p => p!.Value).ToList();
        bool apart = true;
        for (int i = 0; i < kept.Count; i++)
            for (int j = i + 1; j < kept.Count; j++) apart &= !DeckLayout.Overlaps(kept[i], kept[j]);
        check(kept.Count < 5 && kept.Count >= 1 && apart && kept.All(r => r.Right <= 700 && r.Bottom <= 400), "Templates: on a small screen widgets that don't fit are left out, the rest don't overlap");
    }

    /// <summary>The first-run welcome's starters: they exist, use only widgets the public edition has, and fit 1920x1080 and 1366x768 without overlapping.</summary>
    private static void Starters(Action<bool, string> check)
    {
        string[] ids = ["student", "developer", "minimal"];
        var starters = ids.Select(WidgetTemplates.Find).ToList();
        check(starters.All(t => t is not null), "Welcome: Student, Developer and Minimal starters exist");
        // Widgets the public edition keeps (the personal ones are left out of it).
        string[] publicKinds = ["homework", "schedule", "flashcards", "study-log", "timer", "clock", "github", "system-stats", "quick-links", "tasks", "weather", "day-progress"];
        check(starters.All(t => t!.Kinds.All(publicKinds.Contains)), "Welcome: every starter uses only widgets the public edition has");
        check(WidgetTemplates.Find("developer")!.Kinds.SequenceEqual([ItemKinds.Clock, "github", "system-stats", ItemKinds.Timer, "quick-links", ItemKinds.Tasks]), "Welcome: Developer is clock, GitHub, system stats, focus timer, quick links and tasks");
        check(WidgetTemplates.Find("minimal")!.Kinds.SequenceEqual([ItemKinds.Clock, ItemKinds.Weather, ItemKinds.Tasks, "day-progress"]), "Welcome: Minimal is clock, weather, today's tasks and day progress");

        // Default and smallest sizes of those widgets (n * 24 - 12 DIP).
        var sizes = new Dictionary<string, (double W, double H, double MinW, double MinH)>
        {
            ["homework"] = (372, 324, 276, 228), ["schedule"] = (756, 564, 276, 228), ["flashcards"] = (372, 324, 276, 228), ["study-log"] = (372, 276, 276, 228), ["timer"] = (276, 324, 228, 252),
            ["clock"] = (372, 156, 180, 84), ["github"] = (324, 372, 228, 228), ["system-stats"] = (276, 228, 180, 84), ["quick-links"] = (276, 276, 180, 180),
            ["tasks"] = (276, 276, 180, 132), ["weather"] = (372, 228, 180, 108), ["day-progress"] = (276, 228, 180, 132),
        };
        foreach (var (name, w, h) in new[] { ("1920x1080 at 100%", 1920.0, 1080.0), ("1920x1080 at 125%", 1536.0, 864.0), ("1366x768", 1366.0, 768.0) })
        {
            // The corner buttons at the top left and top right stay clear, as on the real deck.
            var reserved = new List<TileRect> { new(-100000, -100000, 100000 + 120, 100000 + 60), new(w - 330, -100000, 400000, 100000 + 60) };
            foreach (var template in starters)
            {
                var slots = template!.Kinds.Select(k => { var z = sizes[k]; return new TemplateSlot(z.W, z.H, z.MinW, z.MinH); }).ToList();
                var placed = TemplatePlacer.Place(slots, w, h, reserved, keepCenterClear: false, grid: DeckLayout.Grid);
                var rects = placed.Where(p => p is not null).Select(p => p!.Value).ToList();
                bool apart = true;
                for (int i = 0; i < rects.Count; i++)
                {
                    apart &= rects[i].X >= 0 && rects[i].Y >= 0 && rects[i].Right <= w && rects[i].Bottom <= h && !reserved.Any(r => DeckLayout.Overlaps(rects[i], r));
                    for (int j = i + 1; j < rects.Count; j++) apart &= !DeckLayout.Overlaps(rects[i], rects[j]);
                }
                check(rects.Count == template.Kinds.Length && apart, $"Welcome: the {template.Name} starter fits {name} without overlapping ({rects.Count} of {template.Kinds.Length} placed)");
            }
        }
    }
}
