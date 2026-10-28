using DesktopDeck.Layout;
using DesktopDeck.Models;
using DesktopDeck.Services;

/// <summary>Floating extras: fade when idle, click-through and the quick sizes. The rules only (no windows).</summary>
public static class FloatExtrasTests
{
    public static void Run(Action<bool, string> check, string folder)
    {
        Settings(check, folder);
        Fade(check);
        Clicks(check);
        Sizes(check);
        Links(check);
        History(check);
    }

    private static void Settings(Action<bool, string> check, string folder)
    {
        var store = new SettingsStore(Path.Combine(folder, "float-extras"));
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        File.WriteAllText(store.FilePath, """{ "SchemaVersion": 5, "Items": [ { "Id": "a", "Kind": "note", "OnTop": true }, { "Id": "b", "Kind": "note", "OnTop": true, "FloatIdleFade": true, "FloatIdleOpacity": 5, "FloatClickThrough": true }, { "Id": "c", "Kind": "note", "FloatIdleOpacity": 500 }, { "Id": "d", "Kind": "note", "FloatIdleOpacity": 50 } ] }""");
        var items = store.Load().Items;
        check(!items[0].FloatIdleFade && !items[0].FloatClickThrough && items[0].FloatIdleOpacity == 35, "Float extras: a widget from before them has both off and fades to 35% if turned on");
        check(items[1].FloatIdleFade && items[1].FloatClickThrough && items[1].FloatIdleOpacity == 10, "Float extras: the on-off choices load; an opacity below 10 becomes 10");
        check(items[2].FloatIdleOpacity == 90 && items[3].FloatIdleOpacity == 50, "Float extras: an opacity above 90 becomes 90; a good one is kept");
        check(FloatBehavior.ClampIdleOpacity(double.NaN) == 35 && FloatBehavior.ClampIdleOpacity(double.PositiveInfinity) == 35, "Float extras: unusable opacity numbers fall back to 35");
        var saved = new DeckSettings { Items = [new LauncherItem { Id = "x", Kind = ItemKinds.Note, OnTop = true, FloatIdleFade = true, FloatIdleOpacity = 20, FloatClickThrough = true }], ClickThroughHintShown = true };
        store.Save(saved);
        var again = store.Load();
        check(again.Items[0].FloatIdleFade && again.Items[0].FloatIdleOpacity == 20 && again.Items[0].FloatClickThrough && again.ClickThroughHintShown, "Float extras: the choices and the hint flag round-trip");
        var copy = saved.Items[0].Copy();
        check(copy.FloatIdleFade && copy.FloatIdleOpacity == 20 && copy.FloatClickThrough, "Float extras: copying a widget (Customize works on a copy) keeps them");
        check(FloatBehavior.IdleChoices.All(c => c is >= FloatBehavior.MinIdleOpacity and <= FloatBehavior.MaxIdleOpacity), "Float extras: the menu choices are all valid");
    }

    private static void Fade(Action<bool, string> check)
    {
        double s = FloatBehavior.IdleSeconds;
        check(FloatBehavior.TargetOpacity(true, 35, inUse: false, idleSeconds: s + 1) == 0.35, "Fade: left alone long enough, it drops to the chosen opacity");
        check(FloatBehavior.TargetOpacity(true, 35, inUse: false, idleSeconds: s - 0.5) == 1, "Fade: not yet");
        check(FloatBehavior.TargetOpacity(true, 35, inUse: true, idleSeconds: 60) == 1, "Fade: in use (pointer over it, a menu, a drag) it is fully visible");
        check(FloatBehavior.TargetOpacity(false, 35, inUse: false, idleSeconds: 60) == 1, "Fade: off means always fully visible");
        check(FloatBehavior.TargetOpacity(true, 2, inUse: false, idleSeconds: 60) == 0.1 && FloatBehavior.TargetOpacity(true, 400, inUse: false, idleSeconds: 60) == 0.9, "Fade: the opacity is kept between 10% and 90%");
    }

    private static void Clicks(Action<bool, string> check)
    {
        check(FloatBehavior.TakesClicks(false, true, false, false, false) && FloatBehavior.TakesClicks(false, false, false, false, false), "Click-through: a normal widget always takes clicks");
        check(!FloatBehavior.TakesClicks(true, false, inside: true, ctrlHeld: false, busy: false), "Click-through: pointer over it without Ctrl, clicks pass through");
        check(FloatBehavior.TakesClicks(true, false, inside: true, ctrlHeld: true, busy: false), "Click-through: Ctrl with the pointer over it turns clicks on");
        check(!FloatBehavior.TakesClicks(true, false, inside: false, ctrlHeld: true, busy: false), "Click-through: Ctrl elsewhere does nothing");
        check(FloatBehavior.TakesClicks(true, true, inside: true, ctrlHeld: false, busy: false), "Click-through: after Ctrl is let go it stays usable while the pointer is over it (so a right-click works)");
        check(!FloatBehavior.TakesClicks(true, true, inside: false, ctrlHeld: false, busy: false), "Click-through: it passes clicks again once the pointer leaves");
        check(FloatBehavior.TakesClicks(true, true, inside: false, ctrlHeld: false, busy: true), "Click-through: an open menu or a drag keeps it usable even if the pointer is outside");
        check(!FloatBehavior.TakesClicks(true, false, inside: false, ctrlHeld: false, busy: true), "Click-through: a menu does not turn on a widget that was not in use");
    }

    private static void Sizes(Action<bool, string> check)
    {
        var clock = (Min: ItemKinds.MinimumSize(ItemKinds.Clock), Normal: ItemKinds.DefaultSize(ItemKinds.Clock));
        var small = SizePresets.For(SizePreset.Small, clock.Min, clock.Normal, 4000, 4000);
        var medium = SizePresets.For(SizePreset.Medium, clock.Min, clock.Normal, 4000, 4000);
        var large = SizePresets.For(SizePreset.Large, clock.Min, clock.Normal, 4000, 4000);
        var normal = SizePresets.For(SizePreset.Default, clock.Min, clock.Normal, 4000, 4000);
        check(small == (228, 108) && medium == (372, 156) && large == (564, 228) && normal == medium, $"Sizes: Clock is {small}, {medium}, {large} (small, medium, large) and Default is its normal size");

        var kinds = ItemKinds.All.Where(k => k != ItemKinds.Group).Append(ItemKinds.Group);
        bool onGrid = true, ordered = true, aboveMinimum = true;
        foreach (var kind in kinds)
        {
            var min = ItemKinds.MinimumSize(kind);
            var def = ItemKinds.DefaultSize(kind);
            var sizes = SizePresets.All.ToDictionary(p => p, p => SizePresets.For(p, min, def, 100000, 100000));
            foreach (var (w, h) in sizes.Values)
            {
                onGrid &= Math.Abs((w + DeckLayout.Gap) % DeckLayout.Grid) < 0.001 && Math.Abs((h + DeckLayout.Gap) % DeckLayout.Grid) < 0.001;
                aboveMinimum &= w >= min.Width && h >= min.Height;
            }
            ordered &= sizes[SizePreset.Small].Width <= sizes[SizePreset.Medium].Width && sizes[SizePreset.Medium].Width <= sizes[SizePreset.Large].Width
                && sizes[SizePreset.Small].Height <= sizes[SizePreset.Medium].Height && sizes[SizePreset.Medium].Height <= sizes[SizePreset.Large].Height;
        }
        check(onGrid, "Sizes: every preset of every kind sits on the grid (n x grid - gap)");
        check(aboveMinimum, "Sizes: no preset is below the kind's minimum");
        check(ordered, "Sizes: Small is no bigger than Medium, Medium no bigger than Large");

        var tight = SizePresets.For(SizePreset.Large, clock.Min, clock.Normal, 500, 200);
        check(tight.Width <= 500 && tight.Height <= 200 && tight.Width >= clock.Min.Width && tight.Height >= clock.Min.Height && Math.Abs((tight.Width + 12) % 24) < 0.001, $"Sizes: Large is cut down to fit the room there is ({tight})");
        var tiny = SizePresets.For(SizePreset.Large, clock.Min, clock.Normal, 50, 50);
        check(tiny == clock.Min || (tiny.Width >= clock.Min.Width && tiny.Height >= clock.Min.Height), "Sizes: a tiny display never takes it below the minimum");
        try
        {
            DeckLayout.Configure(32, 8);
            var custom = SizePresets.For(SizePreset.Large, clock.Min, clock.Normal, 4000, 4000);
            check(Math.Abs((custom.Width + 8) % 32) < 0.001 && Math.Abs((custom.Height + 8) % 32) < 0.001, $"Sizes: they follow a changed grid size and gap ({custom})");
        }
        finally { DeckLayout.Configure(DeckLayout.DefaultGrid, DeckLayout.DefaultGap); }
        check(SizePresets.Label(SizePreset.Small) == "Small" && SizePresets.Label(SizePreset.Default) == "Default", "Sizes: labels");
    }

    private static void Links(Action<bool, string> check)
    {
        var a = new LauncherItem { Kind = ItemKinds.Note, Text = "hello", OnTop = true, FloatIdleFade = true, FloatIdleOpacity = 20, FloatClickThrough = true };
        var b = new LauncherItem { Kind = ItemKinds.Note, Text = "", OnTop = true, FloatIdleFade = false, FloatIdleOpacity = 60, FloatClickThrough = false };
        check(WidgetLinks.CopyData(a, b) && b.Text == "hello", "Float extras and links: the note's text is shared");
        check(!b.FloatIdleFade && b.FloatIdleOpacity == 60 && !b.FloatClickThrough && a.FloatIdleFade && a.FloatIdleOpacity == 20 && a.FloatClickThrough, "Float extras and links: fade and click-through stay per widget");
    }

    private static void History(Action<bool, string> check)
    {
        static string Json(DeckSettings s) => SettingsStore.Serialize(s);
        DeckSettings Sample() => new() { Items = [new LauncherItem { Id = "n", Kind = ItemKinds.Note, Name = "Notes", OnTop = true }] };
        var s = Sample();
        string before = Json(s);
        s.Items[0].FloatClickThrough = true;
        string after = Json(s);
        check(HistoryState.Key(HistoryState.Capture(before)) != HistoryState.Key(HistoryState.Capture(after)), "Float extras and undo: turning click-through on is an undo step");
        check(HistoryState.Describe(HistoryState.Capture(before), HistoryState.Capture(after)) == "Edited Notes", "Float extras and undo: and is described as an edit");
        var history = new UndoHistory();
        var h = Sample();
        history.Reset(Json(h));
        h.Items[0].FloatIdleFade = true;
        h.Items[0].FloatIdleOpacity = 20;
        history.Observe(Json(h), new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc), null);
        check(history.CanUndo, "Float extras and undo: fade and its opacity are covered by undo");
        var hint = Sample();
        string withoutHint = Json(hint);
        hint.ClickThroughHintShown = true;
        check(HistoryState.Key(HistoryState.Capture(withoutHint)) == HistoryState.Key(HistoryState.Capture(Json(hint))), "Float extras and undo: showing the hint once is not an undo step");
        var resized = Sample();
        string r0 = Json(resized);
        (resized.Items[0].FloatWidth, resized.Items[0].FloatHeight) = SizePresets.For(SizePreset.Large, ItemKinds.MinimumSize(ItemKinds.Note), ItemKinds.DefaultSize(ItemKinds.Note), 4000, 4000);
        check(HistoryState.Describe(HistoryState.Capture(r0), HistoryState.Capture(Json(resized))) == "Resized Notes", "Float extras and undo: a quick size is an undoable \"Resized\" step");
    }
}
