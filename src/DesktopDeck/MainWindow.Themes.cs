using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopDeck.Models;

namespace DesktopDeck;

/// <summary>The theme and "look" parts of a deck surface: recoloring the chrome when the theme changes, and Copy look / Paste look.</summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// The theme changed: the chrome (gear, buttons, toast, hints, grid) was built once, so it takes the new colors and font here;
    /// the widgets are redrawn by <see cref="DeckHub.RenderAll"/> right after.
    /// </summary>
    internal void RestyleChrome()
    {
        if (closingSurface) return;
        FontFamily = Theme.UiFont;
        if (!IsFront) Background = Theme.Background;
        foreach (var button in new[] { roomButton, backButton, gridButton, modeButton, editButton }) Skin(button, false);
        Skin(addButton, true);
        Skin(settingsButton, false);
        settingsButton.Background = Theme.Surface3;
        settingsButton.BorderBrush = Brushes.Transparent;
        if (settingsButton.Content is TextBlock gear) gear.Foreground = Theme.Text;
        roomButton.BorderBrush = Theme.Primary;
        toast.Background = Theme.Surface3;
        toast.BorderBrush = Theme.LineStrong;
        toastText.Foreground = Theme.Text;
        toastText.FontFamily = Theme.UiFont;
        emptyHint.Foreground = Theme.Muted;
        emptyHint.FontFamily = Theme.UiFont;
        centerGuide.Stroke = Theme.LineStrong;
        gridLayer.Fill = GridBrush();
        UpdateEditChrome();
        SetPresentation(displayMode, peek);
    }

    private static void Skin(Button button, bool prominent)
    {
        button.FontFamily = Theme.UiFont;
        button.Foreground = prominent ? Theme.OnPrimary : Theme.Text;
        button.Background = prominent ? Theme.Primary : Theme.Surface;
        button.BorderBrush = prominent ? Theme.Primary : Theme.LineStrong;
    }

    // ---------- Copy look / Paste look (the right-click menu of a widget) ----------

    /// <summary>Remembers this widget's look (appearance options, card style, accent) in memory, to paste onto another widget.</summary>
    private void CopyLook(LauncherItem item)
    {
        hub.CopiedLook = StylePreset.From(item, ItemName(item));
        SetStatus($"Copied the look of {ItemName(item)}. Right-click another widget → Paste look.");
    }

    private void PasteLook(LauncherItem item)
    {
        if (hub.CopiedLook is not { } look) return;
        look.ApplyTo(item);
        hub.RenderAll();
        SaveNow();
        SetStatus($"Pasted the look onto {ItemName(item)}.");
    }

    /// <summary>
    /// "Look ›" with "Copy look" and "Paste look" (Paste is greyed out until something was copied). Hidden while the deck-wide look decides how this
    /// widget looks, since copying and pasting would change nothing.
    /// </summary>
    private void AddLookItems(ContextMenu menu, LauncherItem item)
    {
        var look = new MenuItem { Header = "Look", Icon = Theme.Icon("", 14, Theme.Text2) };
        AddMenuItem(look, "Copy look", () => CopyLook(item)).ToolTip = "Remember this widget's card style, colors, corners and accent, to paste onto another widget.";
        var paste = AddMenuItem(look, "Paste look", () => PasteLook(item));
        menu.Items.Add(look);
        void Sync()
        {
            look.Visibility = LookResolver.UsesDeckLook(settings.Appearance, item) ? Visibility.Collapsed : Visibility.Visible;
            paste.IsEnabled = hub.CopiedLook is not null;
            paste.ToolTip = hub.CopiedLook is null ? "Nothing copied yet. Open Look → Copy look on a widget first." : "Give this widget the look you copied.";
        }
        Sync();
        menu.Opened += (_, _) => Sync();
    }
}
