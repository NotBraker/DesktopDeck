using DesktopDeck.Models;
using DesktopDeck.Services;

/// <summary>Themes and style presets: repairing saved settings, the palettes' readability and the preset rules. No UI.</summary>
public static class ThemeTests
{
    public static void Run(Action<bool, string> check, string folder)
    {
        var store = new SettingsStore(Path.Combine(folder, "themes"));
        Theme(check, store);
        Palettes(check);
        Presets(check, store);
    }

    private static void Theme(Action<bool, string> check, SettingsStore store)
    {
        var fresh = new DeckSettings();
        check(fresh.Theme.Name == ThemeSettings.IdDusk && fresh.Theme.Accent == "" && fresh.Theme.Font == "inter" && fresh.Theme.IsDefault, "Themes: the default is Dusk with Inter and no accent override");
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        File.WriteAllText(store.FilePath, """{ "Items": [], "Theme": { "Name": " LIGHT ", "Accent": "#5b8def", "Font": "Georgia", "Custom": { "Background": "red", "Surface": "#12", "Text": "#FFF", "Muted": null, "Accent": "#2EC4A0" } } }""");
        var loaded = store.Load();
        check(loaded.Theme.Name == "light" && loaded.Theme.Accent == "#5B8DEF" && loaded.Theme.Font == "georgia", "Themes: names, accents and fonts are cleaned on load");
        check(loaded.Theme.Custom.Background == "#1A1611" && loaded.Theme.Custom.Surface == "#221C15" && loaded.Theme.Custom.Text == "#FFFFFF" && loaded.Theme.Custom.Muted == "#8E8270" && loaded.Theme.Custom.Accent == "#2EC4A0", "Themes: bad custom colors fall back to Dusk's, short hex is written in full");
        File.WriteAllText(store.FilePath, """{ "Items": [], "Theme": { "Name": "neon", "Accent": "orange", "Font": "Comic", "Custom": null } }""");
        var odd = store.Load();
        check(odd.Theme.Name == "dusk" && odd.Theme.Accent == "" && odd.Theme.Font == "inter" && odd.Theme.Custom.Accent == "#C4684F", "Themes: an unknown theme, accent, font or missing custom block is repaired");
        File.WriteAllText(store.FilePath, """{ "Items": [], "Theme": null, "StylePresets": null }""");
        var none = store.Load();
        check(none.Theme.IsDefault && none.StylePresets.Items.Count == 0, "Themes: null theme and presets become the defaults (files from before themes load unchanged)");
        var saved = new DeckSettings { Theme = new ThemeSettings { Name = ThemeSettings.IdCustom, Accent = "#FFD400", Font = "bahnschrift", Custom = new CustomPalette { Background = "#102030" } } };
        store.Save(saved);
        var again = store.Load();
        check(again.Theme.Name == "custom" && again.Theme.Accent == "#FFD400" && again.Theme.Font == "bahnschrift" && again.Theme.Custom.Background == "#102030", "Themes: the theme round-trips");
    }

    private static void Palettes(Action<bool, string> check)
    {
        foreach (var p in ThemePalette.BuiltIn)
        {
            check(HexColor.Contrast(p.Text, p.Background) >= 7 && HexColor.Contrast(p.Text, p.Surface) >= 7, $"Palette {p.Label}: text is very readable on the background and on cards");
            check(HexColor.Contrast(p.Text2, p.Surface) >= 4.5 && HexColor.Contrast(p.Muted, p.Surface) >= 4.0 && HexColor.Contrast(p.Muted, p.Background) >= 4.0, $"Palette {p.Label}: secondary and muted text stay readable");
            check(HexColor.Contrast(p.OnPrimary, p.Primary) >= 3.0 && HexColor.Contrast(p.Danger, p.Surface) >= 3.0 && HexColor.Contrast(p.Olive, p.Surface) >= 3.0 && HexColor.Contrast(p.Primary, p.Surface) >= 3.0, $"Palette {p.Label}: the accent, its text, danger and olive are readable");
            check(HexColor.Clean(p.Line).Length > 0 && HexColor.Clean(p.LineStrong).Length > 0 && p.Surface != p.Background, $"Palette {p.Label}: all colors are valid and cards stand out from the background");
        }
        check(!ThemePalette.Light.IsDark && ThemePalette.Dusk.IsDark && HexColor.Luminance(ThemePalette.Light.Background) > 0.5, "Palette: Light is light, the others dark");
        var yellow = ThemePalette.Dusk.WithAccent("#FFD400");
        check(yellow.Primary == "#FFD400" && yellow.OnPrimary == "#1A1611", "Palette: a bright accent gets dark text on it");
        var terracotta = ThemePalette.Light.WithAccent("#B5523A");
        check(terracotta.OnPrimary == "#FFFFFF", "Palette: a darker accent keeps light text");
        var custom = ThemePalette.For(new ThemeSettings { Name = ThemeSettings.IdCustom, Custom = new CustomPalette { Background = "#F8F8F8", Surface = "#FFFFFF", Text = "#111111", Muted = "#666666", Accent = "#0057B8" } });
        check(!custom.IsDark && custom.Primary == "#0057B8" && custom.Surface3 != custom.Surface && HexColor.Contrast(custom.Text2, custom.Surface) >= 4.5, "Palette: a custom light palette derives its other colors");
        check(ThemePalette.For(new ThemeSettings { Name = ThemeSettings.IdCustom, Accent = "#22C55E" }).Primary == "#22C55E" && ThemePalette.For(null) == ThemePalette.Dusk, "Palette: the accent override wins over the custom accent, no theme means Dusk");
        check(HexColor.Clean("#abc") == "#AABBCC" && HexColor.Clean("#8fab") == "#88FFAABB" && HexColor.Clean("#8Fabc") == "" && HexColor.Clean("blue") == "" && HexColor.Clean("#12345") == "", "Colors: hex text is validated and written in full");
        check(ThemeSettings.FamilyOf("cascadia").StartsWith("Cascadia") && ThemeSettings.FamilyOf("nope").StartsWith("Inter") && ThemeSettings.Fonts.All(f => f.Family.EndsWith("Segoe UI") || f.Family.Contains("Consolas")), "Fonts: every choice has a fallback and unknown ones use Inter");
    }

    private static void Presets(Action<bool, string> check, SettingsStore store)
    {
        var item = new LauncherItem { Name = "Clock", CardStyle = "glass", Accent = "#5E9BA6", Options = new Dictionary<string, string> { ["_radius"] = "20", ["_cardColor"] = "#102030", ["_opacity"] = "", ["weekColor"] = "#fff", ["_textScale"] = "120" } };
        var presets = new StylePresetSettings();
        var warm = presets.Add(item, "  Warm   glass ");
        check(warm is { Name: "Warm glass", CardStyle: "glass", Accent: "#5E9BA6" } && warm.Options.Count == 3 && !warm.Options.ContainsKey("weekColor") && !warm.Options.ContainsKey("_opacity"), "Presets: a look keeps the appearance options (not widget settings or empty ones), card style and accent");
        var second = presets.Add(item, "warm glass");
        check(second is not null && second.Name == "warm glass 2" && presets.Rename(second.Id, "Cool") && second.Name == "Cool" && !presets.Rename(second.Id, "   ") && presets.Delete(second.Id) && presets.Items.Count == 1, "Presets: names stay unique, rename and delete work");

        var target = new LauncherItem { CardStyle = "solid", Accent = "#C4684F", Options = new Dictionary<string, string> { ["_borderColor"] = "#fff", ["_radius"] = "2", ["weekColor"] = "#abc" } };
        warm!.ApplyTo(target);
        check(target.CardStyle == "glass" && target.Accent == "#5E9BA6" && target.Options["_radius"] == "20" && target.Options["_cardColor"] == "#102030" && !target.Options.ContainsKey("_borderColor") && target.Options["weekColor"] == "#abc", "Presets: applying replaces the appearance options, card style and accent but leaves widget settings alone");
        var noAccent = new StylePreset { Name = "Plain", CardStyle = "bogus", Accent = "" };
        var keep = new LauncherItem { Accent = "#112233" };
        noAccent.ApplyTo(keep);
        check(keep.Accent == "#112233" && keep.CardStyle == "solid", "Presets: a look without an accent keeps the widget's accent, an unknown style becomes solid");

        File.WriteAllText(store.FilePath, """{ "Items": [], "StylePresets": { "Items": [ null, { "Id": "a", "Name": "  ", "CardStyle": "OUTLINE", "Accent": "red", "Options": { "_radius": "8", "plain": "x", "_x": "", "_y": " 5 " } }, { "Id": "a", "Name": "Same", "CardStyle": "weird", "Accent": "#abc", "Options": null }, { "Id": "", "Name": "Same", "CardStyle": null } ] } }""");
        var loaded = store.Load().StylePresets.Items;
        check(loaded.Count == 3 && loaded.Select(p => p.Id).Distinct().Count() == 3 && loaded.All(p => p.Id.Length > 0), "Presets: nulls are dropped and ids are made unique");
        check(loaded[0].Name == "My look" && loaded[0].CardStyle == "outline" && loaded[0].Accent == "" && loaded[0].Options.Keys.OrderBy(k => k).SequenceEqual(["_radius", "_y"]) && loaded[0].Options["_y"] == "5", "Presets: blank names, bad accents and non-appearance or empty options are cleaned");
        check(loaded[1].Name == "Same" && loaded[2].Name == "Same 2" && loaded[1].CardStyle == "solid" && loaded[1].Accent == "#AABBCC" && loaded[2].Options.Count == 0, "Presets: duplicate names get a number, unknown styles become solid");

        var many = new StylePresetSettings();
        for (int i = 0; i < StylePresetSettings.MaxPresets + 5; i++) many.Add(item, "P");
        check(many.Items.Count == StylePresetSettings.MaxPresets && many.Add(item, "More") is null, "Presets: the list is limited");
        var longName = StylePresetSettings.CleanName(new string('x', 100));
        check(longName.Length == StylePresetSettings.MaxNameLength, "Presets: long names are shortened");

        var s = new DeckSettings();
        s.StylePresets.Add(item, "Saved");
        store.Save(s);
        var back = store.Load().StylePresets.Items;
        check(back.Count == 1 && back[0].Name == "Saved" && back[0].Options["_radius"] == "20" && back[0].CardStyle == "glass", "Presets: saved looks round-trip");
    }
}
