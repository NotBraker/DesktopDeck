using DesktopDeck.Models;
using DesktopDeck.Services;

/// <summary>Layouts: the rules for switching, live updating and repairing the floating and background layouts. No UI.</summary>
public static class LayoutTests
{
    public static void Run(Action<bool, string> check, string folder)
    {
        var store = new SettingsStore(Path.Combine(folder, "layouts"));
        Upgrade(check, store);
        SwitchRoundTrip(check, store);
        FloatingLayer(check, store);
        Management(check, store);
        Backgrounds(check, store);
        Repair(check, store);
    }

    private static DeckSettings Sample() => new()
    {
        Items =
        [
            new LauncherItem { Id = "clock", Kind = ItemKinds.Clock, Name = "Clock", X = 0.1, Y = 0.1 },
            new LauncherItem { Id = "notes", Kind = ItemKinds.Note, Name = "Notes", X = 0.5, Y = 0.2, Width = 276, Height = 228 },
            new LauncherItem { Id = "timer", Kind = ItemKinds.Timer, Name = "Timer", OnTop = true, FloatDisplay = "m1", FloatX = 0.7, FloatY = 0.6, FloatWidth = 300, FloatHeight = 330 },
            new LauncherItem { Id = "mic", Kind = ItemKinds.Counter, Name = "Mic", OnTop = true, FloatX = 0.1, FloatY = 0.9 },
        ],
    };

    private static void Upgrade(Action<bool, string> check, SettingsStore store)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        File.WriteAllText(store.FilePath, """{ "SchemaVersion": 4, "Items": [ { "Id": "a", "Name": "A", "Target": "x", "Arguments": "", "WorkingDirectory": "", "Accent": "#fff", "X": 0.2, "Y": 0.3 }, { "Id": "f", "Kind": "timer", "Name": "F", "Target": "", "Arguments": "", "WorkingDirectory": "", "Accent": "#fff", "OnTop": true, "FloatX": 0.4, "Hidden": true } ] }""");
        var old = store.Load();
        var floating = LayoutEngine.Active(old, LayoutLayers.Floating);
        var background = LayoutEngine.Active(old, LayoutLayers.Background);
        check(old.Layouts.Count == 2 && floating is { Name: "Default" } && background is { Name: "Default" } && floating.Id != background.Id, "Layouts: a file from before layouts gets a Default for each layer");
        check(floating!.Entries.Keys.SequenceEqual(["f"]) && !floating.Entries["f"].Visible && floating.Entries["f"].X == 0.4 && background!.Entries.Keys.SequenceEqual(["a"]) && background.Entries["a"].Visible && background.Entries["a"].X == 0.2, "Layouts: each Default is made from the current state of its own layer (visibility and places)");
        check(old.ShowOrb && old.OrbDisplay == "" && old.OrbX == DeckSettings.DefaultOrbX && old.OrbY == DeckSettings.DefaultOrbY, "Layouts: the control button defaults (shown, near the bottom-right) apply to older files");
        check(old.Items[0].X == 0.2 && old.Items[1].Hidden, "Layouts: upgrading changes nothing you can see");
        store.Save(old);
        var again = store.Load();
        check(again.Layouts.Count == 2 && again.ActiveFloatingLayout == floating.Id && again.ActiveBackgroundLayout == background.Id, "Layouts: the defaults and the active ids are saved once, not made again");
    }

    private static void SwitchRoundTrip(Action<bool, string> check, SettingsStore store)
    {
        var s = Sample();
        LayoutEngine.EnsureDefaults(s);
        var home = LayoutEngine.Active(s, LayoutLayers.Background)!;
        // A second background layout made from the current one, then arranged differently while it is active (live).
        var focus = LayoutEngine.CreateFromCurrent(s, LayoutLayers.Background, "Focus");
        check(LayoutEngine.ActiveId(s, LayoutLayers.Background) == focus.Id && focus.Entries.Count == 2 && focus.Entries["notes"].Visible, "Layouts: New layout from current is saved and becomes the active one");
        s.Items[0].Hidden = true;
        s.Items[1].X = 0.8; s.Items[1].Y = 0.7; s.Items[1].Width = 400; s.Items[1].Display = "m2";
        store.Save(s);
        check(focus.Entries["clock"].Visible == false && focus.Entries["notes"].X == 0.8 && focus.Entries["notes"].Y == 0.7 && focus.Entries["notes"].Width == 400 && focus.Entries["notes"].Display == "m2", "Layouts: changes made while a layout is active are written into it when saved (live)");
        check(home.Entries["clock"].Visible && home.Entries["notes"].X == 0.5, "Layouts: the other layout is not touched by those changes");
        // Switching back restores the first arrangement; switching forward restores the second.
        check(LayoutEngine.Switch(s, LayoutLayers.Background, home.Id) && s.ActiveBackgroundLayout == home.Id, "Layouts: switching makes the other layout active");
        check(!s.Items[0].Hidden && s.Items[1].X == 0.5 && s.Items[1].Y == 0.2 && s.Items[1].Width == 276 && s.Items[1].Display == "", "Layouts: switching back restores places, sizes, monitors and visibility");
        check(LayoutEngine.Switch(s, LayoutLayers.Background, focus.Id) && s.Items[0].Hidden && s.Items[1].X == 0.8 && s.Items[1].Display == "m2", "Layouts: switching forward restores the second arrangement (round trip)");
        check(!LayoutEngine.Switch(s, LayoutLayers.Background, focus.Id) && !LayoutEngine.Switch(s, LayoutLayers.Background, "nope") && !LayoutEngine.Switch(s, LayoutLayers.Floating, focus.Id), "Layouts: switching to the active, a missing or another layer's layout does nothing");

        // An item a layout has no entry for is hidden in it; a new item is visible only in the active layout.
        s.Items.Add(new LauncherItem { Id = "fresh", Name = "Fresh", X = 0.3, Y = 0.5 });
        store.Save(s);
        check(focus.Entries.ContainsKey("fresh") && focus.Entries["fresh"].Visible && !home.Entries.ContainsKey("fresh"), "Layouts: a new item is added to the active layout only");
        LayoutEngine.Switch(s, LayoutLayers.Background, home.Id);
        check(s.Items.Single(i => i.Id == "fresh").Hidden && !s.Items[0].Hidden, "Layouts: an item the layout does not list is hidden");
        s.Items.Single(i => i.Id == "fresh").Hidden = false;
        store.Save(s);
        check(home.Entries["fresh"].Visible, "Layouts: showing it in this layout adds it there");
        LayoutEngine.Switch(s, LayoutLayers.Background, focus.Id);
        check(!s.Items.Single(i => i.Id == "fresh").Hidden && s.Items[0].Hidden, "Layouts: switching back to the first layout restores what it showed");

        // Removing an item removes it from every layout.
        s.Items.Remove(s.Items.Single(i => i.Id == "fresh"));
        store.Save(s);
        check(!focus.Entries.ContainsKey("fresh") && !home.Entries.ContainsKey("fresh"), "Layouts: removing a widget removes it from all layouts");
        var loaded = store.Load();
        check(loaded.Layouts.Count == 3 && loaded.ActiveBackgroundLayout == focus.Id && LayoutEngine.Active(loaded, LayoutLayers.Background)!.Entries["notes"].X == 0.8, "Layouts: layouts and the active ids survive a save and load");
    }

    private static void FloatingLayer(Action<bool, string> check, SettingsStore store)
    {
        var s = Sample();
        LayoutEngine.EnsureDefaults(s);
        var vibe = LayoutEngine.Active(s, LayoutLayers.Floating)!;
        check(vibe.Entries.Keys.OrderBy(k => k).SequenceEqual(["mic", "timer"]) && vibe.Entries["timer"].Display == "m1" && vibe.Entries["timer"].X == 0.7 && vibe.Entries["timer"].Width == 300, "Layouts: a floating layout holds the kept-on-top widgets with their float places");
        var study = LayoutEngine.CreateFromCurrent(s, LayoutLayers.Floating, "Study");
        var timer = s.Items.Single(i => i.Id == "timer");
        timer.FloatX = 0.1; timer.FloatY = 0.1; timer.FloatDisplay = ""; timer.FloatWidth = 0;
        s.Items.Single(i => i.Id == "mic").Hidden = true;
        store.Save(s);
        check(study.Entries["timer"].X == 0.1 && !study.Entries["mic"].Visible && vibe.Entries["timer"].X == 0.7 && vibe.Entries["mic"].Visible, "Layouts: moving or hiding a floating widget updates the active floating layout only");
        // Switching background layouts never touches a floating widget, and floating layouts never move deck items.
        var focus = LayoutEngine.CreateFromCurrent(s, LayoutLayers.Background, "Quiet");
        s.Items.Single(i => i.Id == "clock").Hidden = true;
        LayoutEngine.Switch(s, LayoutLayers.Background, LayoutEngine.Of(s, LayoutLayers.Background).First(l => l.Id != focus.Id).Id);
        check(timer.OnTop && timer.FloatX == 0.1 && s.Items.Single(i => i.Id == "mic").Hidden, "Layouts: switching the background leaves the floating layer as it is");
        double deckX = s.Items.Single(i => i.Id == "notes").X;
        LayoutEngine.Switch(s, LayoutLayers.Floating, vibe.Id);
        check(timer.FloatX == 0.7 && timer.FloatDisplay == "m1" && timer.FloatWidth == 300 && !s.Items.Single(i => i.Id == "mic").Hidden && s.Items.Single(i => i.Id == "notes").X == deckX, "Layouts: switching the floating layout moves the floats and not the deck");

        // Keep on top moves an item between layers: the layer it left ignores its entry, the layer it joined adds it.
        var notes = s.Items.Single(i => i.Id == "notes");
        notes.OnTop = true; notes.FloatX = 0.3; notes.FloatY = 0.3;
        store.Save(s);
        check(LayoutEngine.Active(s, LayoutLayers.Floating)!.Entries["notes"] is { Visible: true, X: 0.3 } && LayoutEngine.VisibleCount(s, LayoutEngine.Active(s, LayoutLayers.Floating)!) == 3, "Layouts: an item that starts floating is added to the active floating layout");
        LayoutEngine.Switch(s, LayoutLayers.Background, LayoutEngine.Of(s, LayoutLayers.Background).Last().Id);
        check(notes.OnTop && notes.FloatX == 0.3 && !notes.Hidden, "Layouts: a floated item is not pulled back to the deck by a background switch");
        notes.OnTop = false;
        store.Save(s);
        check(LayoutEngine.Active(s, LayoutLayers.Background)!.Entries.ContainsKey("notes"), "Layouts: an item that goes back to the deck is added to the active background layout");
    }

    private static void Management(Action<bool, string> check, SettingsStore store)
    {
        var s = Sample();
        LayoutEngine.EnsureDefaults(s);
        var a = LayoutEngine.Active(s, LayoutLayers.Floating)!;
        check(LayoutEngine.Delete(s, a.Id) is null && s.Layouts.Count == 2, "Layouts: the last layout of a layer cannot be deleted");
        var b = LayoutEngine.CreateFromCurrent(s, LayoutLayers.Floating, "Default");
        var c = LayoutEngine.CreateFromCurrent(s, LayoutLayers.Floating, "  Vibe coding  ");
        check(b.Name == "Default 2" && c.Name == "Vibe coding", "Layouts: names are trimmed and kept unique within a layer");
        check(LayoutEngine.Rename(s, c.Id, "Focus") && c.Name == "Focus" && !LayoutEngine.Rename(s, c.Id, "  ") && LayoutEngine.Rename(s, c.Id, "default") && c.Name == "default 3", "Layouts: rename keeps names unique and ignores a blank name");
        LayoutEngine.Rename(s, c.Id, "Focus");
        LayoutEngine.SetIcon(s, c.Id, "🎯");
        var copy = LayoutEngine.Duplicate(s, c.Id)!;
        check(copy.Id != c.Id && copy.Name == "Focus copy" && copy.Icon == "🎯" && copy.Entries.Count == c.Entries.Count && !ReferenceEquals(copy.Entries["timer"], c.Entries["timer"]) && s.ActiveFloatingLayout == c.Id, "Layouts: duplicate is an independent copy placed after the original and does not become active");
        check(LayoutEngine.Of(s, LayoutLayers.Floating).Select(l => l.Name).SequenceEqual(["Default", "Default 2", "Focus", "Focus copy"]), "Layouts: the list order is the quick-switch order");
        check(LayoutEngine.Move(s, copy.Id, -1) && LayoutEngine.Of(s, LayoutLayers.Floating).Select(l => l.Name).SequenceEqual(["Default", "Default 2", "Focus copy", "Focus"]) && !LayoutEngine.Move(s, a.Id, -1) && LayoutEngine.Move(s, a.Id, 1), "Layouts: layouts can be moved up and down within their layer");
        // Deleting a layout that is not active leaves the screen alone; deleting the active one applies the next.
        var timer = s.Items.Single(i => i.Id == "timer");
        timer.FloatX = 0.05;
        store.Save(s);
        check(LayoutEngine.Delete(s, a.Id) == a && timer.FloatX == 0.05 && s.ActiveFloatingLayout == c.Id, "Layouts: deleting an inactive layout changes nothing on screen");
        var deleted = LayoutEngine.Delete(s, c.Id);
        check(deleted == c && s.ActiveFloatingLayout != c.Id && LayoutEngine.Active(s, LayoutLayers.Floating) is { } now && now.Layer == LayoutLayers.Floating && timer.FloatX == now.Entries["timer"].X, "Layouts: deleting the active layout applies a neighbour and makes it active");
        check(s.Layouts.Count(l => l.Layer == LayoutLayers.Background) == 1 && s.Layouts.All(l => LayoutLayers.IsValid(l.Layer)), "Layouts: the other layer is untouched by deletes");
    }

    private static void Backgrounds(Action<bool, string> check, SettingsStore store)
    {
        var s = Sample();
        s.Appearance.Backgrounds[""] = new BackgroundStyle { Kind = "image", ImagePath = Path.Combine(folderOf(store), "media", "a.png") };
        LayoutEngine.EnsureDefaults(s);
        var home = LayoutEngine.Active(s, LayoutLayers.Background)!;
        check(home.Backgrounds[""].Kind == "image" && !ReferenceEquals(home.Backgrounds[""], s.Appearance.Backgrounds[""]), "Layouts: a background layout holds its own copy of the deck background");
        var productive = LayoutEngine.CreateFromCurrent(s, LayoutLayers.Background, "Productivity");
        s.Appearance.Backgrounds[""] = new BackgroundStyle { Kind = "gradient", Color = "#112233", Color2 = "#445566" };
        s.Appearance.Backgrounds["m2"] = new BackgroundStyle { Kind = "solid", Color = "#010203" };
        store.Save(s);
        check(productive.Backgrounds[""].Kind == "gradient" && productive.Backgrounds.ContainsKey("M2") && home.Backgrounds[""].Kind == "image", "Layouts: background changes while a layout is active are written into it, in a case-insensitive map");
        LayoutEngine.Switch(s, LayoutLayers.Background, home.Id);
        check(s.Appearance.Backgrounds[""].Kind == "image" && !s.Appearance.Backgrounds.ContainsKey("m2") && !ReferenceEquals(s.Appearance.Backgrounds[""], home.Backgrounds[""]), "Layouts: switching a background layout swaps the deck background (as a copy)");
        s.Appearance.Backgrounds[""].Dim = 40;
        check(home.Backgrounds[""].Dim == 0, "Layouts: editing the current background does not change a layout until it is saved");
        store.Save(s);
        check(home.Backgrounds[""].Dim == 40 && productive.Backgrounds[""].Dim == 0, "Layouts: ... and then only the active layout follows it");
        LayoutEngine.Switch(s, LayoutLayers.Background, productive.Id);
        check(s.Appearance.Backgrounds[""].Kind == "gradient" && s.Appearance.Backgrounds["m2"].Color == "#010203", "Layouts: the background round-trips back");

        // A picture is in use while the current background or any inactive background layout still references it.
        string picture = Path.Combine(folderOf(store), "media", "a.png");
        check(LayoutEngine.PictureInUse(s, picture), "Layouts: a picture only an inactive layout uses is still in use");
        s.Appearance.Backgrounds[""].ImagePath = picture;
        check(LayoutEngine.PictureInUse(s, picture), "Layouts: the current background's picture is in use");
        s.Appearance.Backgrounds.Clear();
        LayoutEngine.Delete(s, LayoutEngine.Of(s, LayoutLayers.Background).First(l => l.Id != productive.Id).Id);
        check(!LayoutEngine.PictureInUse(s, picture), "Layouts: a picture nothing references is free to delete (the active layout only mirrors the current background)");
        var round = new DeckSettings { Items = [], Appearance = { Backgrounds = { ["Display\\One"] = new BackgroundStyle { Kind = "solid", Color = "#ABCDEF" } } } };
        LayoutEngine.EnsureDefaults(round);
        store.Save(round);
        var reloaded = store.Load();
        check(LayoutEngine.Active(reloaded, LayoutLayers.Background)!.Backgrounds["display\\one"].Color == "#ABCDEF", "Layouts: a layout's backgrounds round-trip through the file and stay case-insensitive");
    }

    private static string folderOf(SettingsStore store) => Path.GetDirectoryName(store.FilePath)!;

    private static void Repair(Action<bool, string> check, SettingsStore store)
    {
        File.WriteAllText(store.FilePath, """
            { "Items": [ { "Id": "a", "Name": "A", "Target": "", "Arguments": "", "WorkingDirectory": "", "Accent": "#fff" } ],
              "ActiveFloatingLayout": "gone", "ActiveBackgroundLayout": "b1", "OrbX": 9,
              "Layouts": [
                { "Id": "b1", "Name": "  ", "Layer": "background", "Icon": "way too long for an icon", "Entries": { "a": { "Visible": true, "X": 5, "Y": -1, "Width": -3, "Height": 100000 }, "ghost": { "Visible": true }, "x": null, "": {} }, "Backgrounds": { "": { "Kind": "SOLID", "Color": "zz" }, "bad": null } },
                { "Id": "b1", "Name": "Dup", "Layer": "background" },
                { "Id": "z", "Name": "Weird", "Layer": "sideways" },
                null,
                { "Id": "f1", "Name": "Float", "Layer": "floating", "Entries": null, "Backgrounds": { "": { "Kind": "solid" } } }
              ] }
            """);
        var s = store.Load();
        var b1 = s.Layouts.First(l => l.Name == "Layout");
        check(s.Layouts.Count == 3 && s.Layouts.All(l => LayoutLayers.IsValid(l.Layer)) && s.Layouts.Select(l => l.Id).Distinct().Count() == 3, "Layouts: invalid layers and null entries are dropped and duplicate ids repaired");
        check(b1.Icon == "" && b1.Entries.Keys.SequenceEqual(["a"]) && b1.Entries["a"].X == 1 && b1.Entries["a"].Y == 0 && b1.Entries["a"].Width == 0 && b1.Entries["a"].Height == 100000 && b1.Backgrounds[""].Kind == "solid" && b1.Backgrounds[""].Color == "#1A1611" && b1.Backgrounds.Count == 1, "Layouts: a layout's numbers, icon, entries of missing items and backgrounds are repaired");
        check(s.ActiveBackgroundLayout == b1.Id && LayoutEngine.Active(s, LayoutLayers.Floating)?.Id == "f1" && LayoutEngine.Active(s, LayoutLayers.Floating)!.Backgrounds.Count == 0 && LayoutEngine.Active(s, LayoutLayers.Floating)!.Entries.Count == 0, "Layouts: a missing active id falls back to the first layout of the layer; a floating layout keeps no backgrounds");
        check(s.OrbX == 1 && s.OrbY == DeckSettings.DefaultOrbY, "Layouts: the control button position is repaired");
        File.WriteAllText(store.FilePath, """{ "Items": [], "Layouts": null }""");
        check(store.Load().Layouts.Count == 2, "Layouts: a null list loads as the two defaults");
    }
}
