using System.Windows;
using System.Windows.Controls;
using DesktopDeck.Models;

namespace DesktopDeck;

// The display part of the Settings tab: how the deck shows, the pinned widgets' switch, the control button, and (collapsed) grid and placement.
// It lives in its own panel so a click here redraws only this part, never the whole Settings tab (a Google sign-in in progress survives).
internal sealed partial class ControlCenter
{
    private StackPanel? displayHost;
    private bool displayDirty;

    /// <summary>Called by the Settings content right after "General": adds the Display section there.</summary>
    private void AddDisplaySections(StackPanel root)
    {
        displayHost = new StackPanel { Margin = new Thickness(0, PanelKit.S24, 0, 0) };
        root.Children.Add(displayHost);
        FillDisplay();
    }

    /// <summary>Redraws the display part now (after something in it changed), keeping the scroll position.</summary>
    private void RefillDisplay()
    {
        displayDirty = false;
        if (displayHost is null) return;
        double offset = scroll.VerticalOffset;
        FillDisplay();
        scroll.UpdateLayout();
        scroll.ScrollToVerticalOffset(offset);
    }

    /// <summary>Redraws the display part when the deck changed outside this tab (the tray's Hide pinned widgets, a mode change from the deck).</summary>
    private void RefreshDisplayIfStale()
    {
        if (displayDirty) RefillDisplay();
    }

    private void FillDisplay()
    {
        if (displayHost is null) return;
        displayHost.Children.Clear();
        var card = PanelKit.Section(displayHost, "Display");

        // ---------- How the deck shows ----------
        // The simple view leaves out Window (an Alt+Tab overlay), unless that is the mode in use, so the choice shown is always the real one.
        string[] modes = hub.Advanced || hub.Mode == "window" ? ["background", "window", "ontop"] : ["background", "ontop"];
        string[] labels = modes.Select(m => m switch { "window" => "Over my apps (Alt+Tab)", "ontop" => "Always on top of my apps", _ => "On the desktop" }).ToArray();
        var heading = Theme.Label("How the deck shows", 13.5);
        heading.FontWeight = FontWeights.SemiBold;
        heading.Margin = new Thickness(0, 0, 0, PanelKit.S8);
        card.Children.Add(heading);
        card.Children.Add(Theme.Segmented(labels, Math.Max(0, Array.IndexOf(modes, hub.Mode)), index => ChooseMode(modes[index]), 30));
        var modeNote = PanelKit.Hint(hub.Mode switch
        {
            "window" => "An overlay on every monitor that you can Alt+Tab to. Other apps can cover it; empty space is click-through.",
            "ontop" => "The same overlay, always on top of your other windows.",
            _ => "Part of your desktop, behind every window, one deck per monitor. Win+D shows it.",
        });
        modeNote.Margin = new Thickness(0, PanelKit.S8, 0, PanelKit.S16);
        card.Children.Add(modeNote);

        // ---------- Pinned widgets and the control button ----------
        card.Children.Add(PanelKit.Option("Hide pinned widgets", "Turns the pinned widgets off until you switch them back on.", !hub.FloatsVisible, on => Quietly(() => hub.SetFloatsVisible(!on))));
        card.Children.Add(PanelKit.Option("Show the control button", "The round button that opens this window. Drag it anywhere. Ctrl+Alt+S and the tray icon open it too.", hub.Settings.ShowOrb, on => Quietly(() => hub.SetShowOrb(on))));

        // ---------- Desktop icons ----------
        card.Children.Add(PanelKit.Option("Show desktop icons", "Your files and shortcuts stay on the desktop. Widgets sit on top of them; the empty space is the normal Windows desktop.", hub.ShowDesktopIcons, on => Quietly(() => hub.SetShowDesktopIcons(on))));

        // ---------- Grid and placement ----------
        var more = PanelKit.Collapsible(card, "settings.grid", "Grid and placement", nested: true);
        BuildGridControls(more.Body);
    }

    private void ChooseMode(string mode)
    {
        if (hub.Mode == mode) return;
        Quietly(() => hub.SetMode(mode));
        RefillDisplay();
        // The widgets overlay (when topmost) opens in front of everything: bring this panel back above it.
        Bring();
    }

    /// <summary>Puts the backdrop and then the panel back on top of every other topmost window.</summary>
    internal void Bring()
    {
        if (closed || smoke) return;
        if (backdrop is not null) NativeWindow.Raise(new System.Windows.Interop.WindowInteropHelper(backdrop).Handle);
        NativeWindow.Raise(handle);
        Activate();
    }
}
