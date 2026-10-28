using DesktopDeck.Models;
using DesktopDeck.Services;

/// <summary>The one look for every widget: which values win, what "own look" does, repairing saved values, and resetting every widget. No UI.</summary>
public static class LookTests
{
    public static void Run(Action<bool, string> check, string folder)
    {
        Resolver(check);
        Reset(check);
        Saved(check, new SettingsStore(Path.Combine(folder, "looks")));
    }

    private static LauncherItem Widget(string style = "outline", params (string Key, string Value)[] options)
    {
        var item = new LauncherItem { Kind = ItemKinds.Clock, CardStyle = style };
        foreach (var (key, value) in options) item.Options[key] = value;
        return item;
    }

    private static AppearanceSettings Deck(bool all) => new()
    {
        ApplyToAll = all, CardStyle = "frosted", CardColor = "#102030", TextColor = "#EEEEEE", BorderColor = "#445566",
        CardOpacity = 70, CornerRadius = 22, TextScale = 120, Padding = 20,
    };

    private static void Resolver(Action<bool, string> check)
    {
        var own = Widget("outline", ("_cardColor", "#AA0000"), ("_textColor", "#00FF00"), ("_borderColor", "#0000FF"), ("_opacity", "40"), ("_radius", "5"), ("_textScale", "150"), ("_padding", "3"));

        var off = LookResolver.Effective(Deck(false), own);
        check(!off.FromDeck && off.CardStyle == "outline" && off.CardColor == "#AA0000" && off.TextColor == "#00FF00" && off.BorderColor == "#0000FF" && off.Opacity == 40 && off.Radius == 5 && off.TextScale == 150 && off.Padding == 3,
            "Look: with the switch off a widget's own values win");

        var on = LookResolver.Effective(Deck(true), own);
        check(on.FromDeck && on.CardStyle == "frosted" && on.CardColor == "#102030" && on.TextColor == "#EEEEEE" && on.BorderColor == "#445566" && on.Opacity == 70 && on.Radius == 22 && on.TextScale == 120 && on.Padding == 20,
            "Look: with the switch on the deck's values win outright");
        check(own.CardStyle == "outline" && own.Options["_radius"] == "5" && own.Options.Count == 7 && !own.OwnLook, "Look: resolving never rewrites the widget");

        own.OwnLook = true;
        var keep = LookResolver.Effective(Deck(true), own);
        check(!keep.FromDeck && keep.CardStyle == "outline" && keep.Radius == 5 && keep.TextScale == 150, "Look: a widget with its own look keeps it while the switch is on");
        own.OwnLook = false;
        check(LookResolver.Effective(Deck(false), own) == off, "Look: turning the switch off brings the widget's own look straight back");

        // Nothing set on the widget: the deck defaults fill in, and the built-in values stand in where the deck has none (text size and spacing are not deck defaults while off).
        var bare = Widget("solid");
        var deckOff = Deck(false);
        var fill = LookResolver.Effective(deckOff, bare);
        check(fill.CardStyle == "solid" && fill.CardColor == "#102030" && fill.TextColor == "#EEEEEE" && fill.BorderColor == "#445566" && fill.Opacity == 70 && fill.Radius == 22 && fill.TextScale == 100 && fill.Padding == 12,
            "Look: with the switch off an empty widget takes the deck's colors, opacity and corners, but keeps the usual text size and spacing");
        var none = LookResolver.Effective(null, bare);
        check(none.CardColor == "" && none.Opacity == 100 && none.Radius == AppearanceSettings.DefaultRadius && none.TextScale == 100 && none.Padding == 12 && !none.FromDeck, "Look: no deck settings at all gives the built-in look");

        // Empty and half-set values: an empty option is "not set", a bad one is ignored, numbers are clamped.
        var odd = Widget("bogus", ("_cardColor", ""), ("_textColor", "nope#"), ("_opacity", ""), ("_radius", "99"), ("_textScale", "5"), ("_padding", "-4"));
        var repaired = LookResolver.Effective(Deck(false), odd);
        check(repaired.CardStyle == "solid" && repaired.CardColor == "#102030" && repaired.TextColor == "#EEEEEE" && repaired.Opacity == 70 && repaired.Radius == 40 && repaired.TextScale == 60 && repaired.Padding == 0,
            "Look: empty or invalid values fall back, numbers are clamped, an unknown card style becomes solid");
        var comma = LookResolver.Effective(null, Widget("glass", ("_radius", "12,5")));
        check(comma.Radius == 12.5 && comma.CardStyle == "glass", "Look: a decimal comma is read");
        var rejected = LookResolver.Effective(Deck(false), Widget("solid", ("_cardColor", "#AA0000")), c => c != "#AA0000");
        check(rejected.CardColor == "#102030", "Look: a color that cannot be drawn falls back to the deck's");

        // The deck's own values are repaired on load, so the resolver never sees nonsense.
        var messy = AppearanceSettings.Normalized(new AppearanceSettings { ApplyToAll = true, CardStyle = " FROSTED ", CardOpacity = double.NaN, CornerRadius = 500, TextScale = 3, Padding = 999, CardColor = "javascript", TextColor = "#12" });
        var fromMessy = LookResolver.Effective(messy, Widget("none"));
        check(fromMessy.FromDeck && fromMessy.CardStyle == "frosted" && fromMessy.Opacity == 100 && fromMessy.Radius == 40 && fromMessy.TextScale == 60 && fromMessy.Padding == 40 && fromMessy.TextColor == "",
            "Look: the deck's own values are cleaned before they are used");
        messy.CardStyle = "not-a-style";
        check(LookResolver.Effective(messy, Widget("none")).CardStyle == "solid", "Look: an unknown deck card style becomes solid");
        check(LookResolver.UsesDeckLook(Deck(true), Widget()) && !LookResolver.UsesDeckLook(Deck(false), Widget()) && !LookResolver.UsesDeckLook(null, Widget()), "Look: UsesDeckLook follows the switch");
        check(ItemKinds.CardStyles.Contains("frosted") && ItemKinds.CardStyles.Contains("glass"), "Look: frosted glass is a card style next to see-through");
    }

    private static void Reset(Action<bool, string> check)
    {
        var settings = new DeckSettings { Appearance = new AppearanceSettings { ApplyToAll = true, CardStyle = "glass", TextScale = 110, Padding = 12 } };
        var tidy = Widget("glass", ("weekColor", "#fff"));
        var busy = Widget("outline", ("_cardColor", "#AA0000"), ("_radius", "5"), ("_textScale", "150"), ("_padding", "3"), ("_opacity", "50"), ("_textColor", "#fff"), ("_borderColor", "#000"), ("keep", "x"));
        busy.OwnLook = true;
        settings.Items.Add(tidy);
        settings.Items.Add(busy);
        int changed = LookResolver.ResetWidgets(settings);
        check(busy.CardStyle == "glass" && !busy.OwnLook && busy.Options.Count == 2 && busy.Options["keep"] == "x" && busy.Options["_textScale"] == "110" && !busy.Options.ContainsKey("_padding"),
            "Reset: widgets take the deck's card style, lose their own look settings and keep their other settings");
        check(changed == 2 && tidy.Options["_textScale"] == "110" && tidy.Options["weekColor"] == "#fff", "Reset: it counts the widgets it changed; a non-default deck text size is kept on the widget so switching off keeps it");
        settings.Appearance.ApplyToAll = false;
        var after = LookResolver.Effective(settings.Appearance, busy);
        check(after.CardStyle == "glass" && after.TextScale == 110 && after.Padding == 12, "Reset: with the switch off the reset widgets still look the way the deck looked");
        check(LookResolver.ResetWidgets(settings) == 0, "Reset: doing it again changes nothing");
    }

    private static void Saved(Action<bool, string> check, SettingsStore store)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        var settings = new DeckSettings { Appearance = Deck(true), Items = [new LauncherItem { Kind = ItemKinds.Clock, CardStyle = "frosted", OwnLook = true }, new LauncherItem { Kind = ItemKinds.Clock, CardStyle = "Frosted " }] };
        store.Save(settings);
        var again = store.Load();
        check(again.Appearance.ApplyToAll && again.Appearance.CardStyle == "frosted" && again.Appearance.TextScale == 120 && again.Appearance.Padding == 20 && !again.Appearance.IsDefault, "Look: the deck look round-trips");
        check(again.Items[0].CardStyle == "frosted" && again.Items[0].OwnLook && !again.Items[1].OwnLook, "Look: a widget's frosted style and own-look switch round-trip");
        check(again.Items[1].CardStyle is "frosted" or "solid", "Look: a loosely typed card style is repaired");
    }
}
