using DesktopDeck.Layout;
using DesktopDeck.Models;
using DesktopDeck.Services;

/// <summary>The configurable grid size and gap: defaults, repair of bad values on load, and what the layout does with them. No UI.</summary>
public static class GridSettingsTests
{
    public static void Run(Action<bool, string> check, string folder)
    {
        var store = new SettingsStore(Path.Combine(folder, "grid"));
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);

        File.WriteAllText(store.FilePath, """{ "SchemaVersion": 5, "Items": [] }""");
        var old = store.Load();
        check(old.GridSize == 24 && old.GridGap == 12, "Grid: a file without the new settings gets 24 and 12");

        foreach (var (size, gap, expectedSize, expectedGap) in new[] { (3, -5, 8, 0), (100, 99, 64, 32), (0, 0, 8, 0), (8, 32, 8, 32), (64, 0, 64, 0), (30, 7, 30, 7) })
        {
            File.WriteAllText(store.FilePath, $$"""{ "SchemaVersion": 5, "GridSize": {{size}}, "GridGap": {{gap}}, "Items": [] }""");
            var loaded = store.Load();
            check(loaded.GridSize == expectedSize && loaded.GridGap == expectedGap, $"Grid: size {size} and gap {gap} load as {expectedSize} and {expectedGap}");
        }

        var saved = new DeckSettings { GridSize = 40, GridGap = 20 };
        store.Save(saved);
        var again = store.Load();
        check(again.GridSize == 40 && again.GridGap == 20, "Grid: size and gap round-trip");

        check(DeckLayout.ClampGridSize(double.NaN) == 24 && DeckLayout.ClampGap(double.PositiveInfinity) == 12 && DeckLayout.ClampGridSize(15.6) == 16, "Grid: unusable numbers fall back to the defaults, fractions are rounded");
        try
        {
            DeckLayout.Configure(16, 8);
            check(DeckLayout.Grid == 16 && DeckLayout.Gap == 8, "Grid: the layout takes the configured size and gap");
            DeckLayout.Configure(1000, -3);
            check(DeckLayout.Grid == 64 && DeckLayout.Gap == 0, "Grid: the layout clamps what it is given");
        }
        finally { DeckLayout.Configure(DeckLayout.DefaultGrid, DeckLayout.DefaultGap); }
        check(DeckLayout.Grid == 24 && DeckLayout.Gap == 12, "Grid: the defaults are restored");
    }
}
