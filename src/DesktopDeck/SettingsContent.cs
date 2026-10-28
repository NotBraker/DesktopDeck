using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopDeck.Models;
using DesktopDeck.Services;

namespace DesktopDeck;

/// <summary>
/// The Settings tab of the control center: start-up and layout switches, connected accounts (Google and
/// iCal) and data. It is plain content hosted by <see cref="ControlCenter"/>; dialogs it opens are owned by <paramref name="owner"/> (the
/// control center), so they stay in front of it.
/// </summary>
internal sealed partial class SettingsContent
{
    private readonly DeckHub deck;
    private readonly DeckSettings settings;
    private readonly SecretStore secrets;
    private readonly Window owner;
    private bool disposed;

    /// <summary>The content (a vertical stack of sections).</summary>
    internal StackPanel Root { get; } = new();
    /// <summary>False once the control center has closed: nothing may touch the UI after an await then.</summary>
    private bool Alive => !disposed && owner.IsLoaded;

    /// <param name="addDisplay">Adds the control center's Display section right after General (it needs the control center's own helpers).</param>
    internal static SettingsContent Build(DeckHub hub, Window owner, Action<StackPanel>? addDisplay = null) => new(hub, owner, addDisplay);

    /// <summary>Stops anything still running (a Google sign-in waiting for the browser).</summary>
    internal void Dispose()
    {
        disposed = true;
        connecting?.Cancel();
    }

    private SettingsContent(DeckHub hub, Window owner, Action<StackPanel>? addDisplay)
    {
        deck = hub;
        settings = hub.Settings;
        secrets = hub.Secrets;
        this.owner = owner;
        var root = Root;
        var general = Section(root, "General");
        general.Children.Add(Option("Show advanced features", "Layouts, display modes, widget groups and links, and automation.", settings.AdvancedFeatures, deck.SetAdvanced));
        general.Children.Add(Option("Start when I sign in", "Opens Desktop Deck every time you sign in to Windows.", settings.StartAtLogin, deck.SetStartAtLogin));
        var shortcuts = Option("Keyboard shortcuts", "Ctrl+Alt+S opens the control center. Ctrl+Alt+D shows your widgets in front of your windows.", settings.AppHotkey, deck.SetAppHotkey);
        shortcuts.ToolTip = "Press Esc or Ctrl+Alt+D again to send the widgets back. Opening Desktop Deck from its shortcut does the same.";
        general.Children.Add(shortcuts);

        // How the deck shows, floating widgets, the control button and the grid: the control center adds them here.
        addDisplay?.Invoke(root);

        BuildConnectedAccounts(root);


        BuildHistory(root);

        var data = Collapsed(root, "settings.data", "Data");
        var desktop = Theme.Button("Add my desktop shortcuts as launchers");
        desktop.Click += (_, _) => deck.ImportDesktop();
        var folder = Theme.Button("Open data folder");
        folder.Click += (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Path.GetDirectoryName(secrets.FilePath) + "\"") { UseShellExecute = true });
        var dataRow = new WrapPanel();
        foreach (var b in new[] { desktop, folder }) { b.Margin = new Thickness(0, 0, 8, 8); dataRow.Children.Add(b); }
        data.Children.Add(Hint("Makes a launcher for each shortcut and script on your desktop. Nothing is launched."));
        data.Children.Add(dataRow);

        var exit = Theme.Button("Exit Desktop Deck");
        exit.Foreground = Theme.Danger;
        exit.HorizontalAlignment = HorizontalAlignment.Left;
        exit.Margin = new Thickness(0, 26, 0, 0);
        exit.ToolTip = "Closes the deck and shows your normal desktop until you start it again.";
        exit.Click += (_, _) => { owner.Close(); deck.RequestExit(); };
        root.Children.Add(exit);
    }

    private static StackPanel Section(Panel parent, string name, string? note = null) => PanelKit.Section(parent, name, note);

    /// <summary>A section that starts closed: a header row that opens a card. Returns the card's content. Open or closed is remembered under <paramref name="key"/>.</summary>
    internal static StackPanel Collapsed(Panel parent, string key, string title, string? summary = null)
    {
        bool afterOther = parent.Children.Count > 0 && parent.Children[^1] is Disclosure;
        var section = PanelKit.Collapsible(parent, key, title, summary);
        section.Margin = new Thickness(0, afterOther ? PanelKit.S8 : PanelKit.S24, 0, 0);
        var inner = new StackPanel();
        section.Body.Children.Add(new Border
        {
            Background = Theme.Surface, BorderBrush = Theme.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(PanelKit.S16, PanelKit.S16, PanelKit.S16, PanelKit.S8), Child = inner,
        });
        return inner;
    }

    private static TextBlock Hint(string text) => PanelKit.Hint(text);

    private static Grid Option(string label, string description, bool value, Action<bool> changed) => PanelKit.Option(label, description, value, changed);

}
