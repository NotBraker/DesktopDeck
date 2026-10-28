using DesktopDeck.Models;

namespace DesktopDeck;

/// <summary>Made-up layouts and automation rules for the sample deck, so the Layouts tab (its automation rules) has something to show in snapshots.</summary>
internal static class AutomationSamples
{
    public static void Add(DeckSettings settings)
    {
        LayoutEngine.EnsureDefaults(settings);
        string floatingInUse = settings.ActiveFloatingLayout, backgroundInUse = settings.ActiveBackgroundLayout;
        var coding = LayoutEngine.CreateFromCurrent(settings, LayoutLayers.Floating, "Vibe coding");
        coding.Icon = "💻";
        var studying = LayoutEngine.CreateFromCurrent(settings, LayoutLayers.Floating, "Studying");
        studying.Icon = "📚";
        var focus = LayoutEngine.CreateFromCurrent(settings, LayoutLayers.Background, "Focus");
        settings.ActiveFloatingLayout = floatingInUse;
        settings.ActiveBackgroundLayout = backgroundInUse;
        settings.Automation.Rules =
        [
            new AutomationRule { Trigger = AutomationTriggers.App, Process = "Code.exe", Layer = LayoutLayers.Floating, LayoutId = coding.Id, SwitchBack = true },
            new AutomationRule { Trigger = AutomationTriggers.Time, Days = [1, 2, 3, 4, 5], Start = "18:00", End = "21:30", Layer = LayoutLayers.Floating, LayoutId = studying.Id },
            new AutomationRule { Trigger = AutomationTriggers.Displays, DisplayCount = 1, Layer = LayoutLayers.Background, LayoutId = focus.Id, SwitchBack = true },
            new AutomationRule { Enabled = false, Trigger = AutomationTriggers.Time, Days = [5, 6], Start = "22:00", End = "06:00", Layer = LayoutLayers.Background, LayoutId = focus.Id },
        ];
        settings.Automation.FullscreenIgnore = ["vlc"];
    }
}
