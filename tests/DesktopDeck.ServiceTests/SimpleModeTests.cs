using DesktopDeck.Models;
using DesktopDeck.Services;

/// <summary>The simple view's switch (Settings → Show advanced features): its default, its file round trip and the fresh-install rule. No UI; the menus are checked by the smoke renders.</summary>
public static class SimpleModeTests
{
    public static void Run(Action<bool, string> check, string folder)
    {
        var store = new SettingsStore(Path.Combine(folder, "simple"));
        check(new DeckSettings().AdvancedFeatures, "Simple mode: the class default is advanced, so existing setups keep everything");

        // A settings file written before the switch existed has no value for it and loads as advanced.
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        File.WriteAllText(store.FilePath, """{ "SchemaVersion": 5, "Items": [], "StartAtLogin": false }""");
        check(store.Load().AdvancedFeatures, "Simple mode: a settings file without the field loads as advanced");

        // A fresh install (the first-run welcome calls StartSimple) saves the simple view and gets it back; turning the switch on survives too.
        var fresh = new DeckSettings();
        fresh.StartSimple();
        check(!fresh.AdvancedFeatures, "Simple mode: the welcome starts a fresh install in the simple view");
        store.Save(fresh);
        check(!store.Load().AdvancedFeatures, "Simple mode: the simple view round-trips");
        fresh.AdvancedFeatures = true;
        store.Save(fresh);
        check(store.Load().AdvancedFeatures, "Simple mode: switching advanced features on round-trips");

        // Neither view is part of undo, so undoing never flips the switch.
        check(!HistoryState.Scope.Contains("AdvancedFeatures"), "Simple mode: undo history does not include the switch");
    }
}
