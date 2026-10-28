using System.Windows;
using System.Windows.Controls;
using DesktopDeck.Models;
using DesktopDeck.Widgets;

namespace DesktopDeck;

// A saved widget whose code isn't available (removed, or from another version): a small card instead of a crash.
public sealed partial class MainWindow
{
    /// <summary>True for a saved item whose kind is not one of the app's own and is not a registered plug-in widget.</summary>
    private static bool IsUnavailable(LauncherItem item) => !ItemKinds.All.Contains(item.Kind) && WidgetRegistry.Find(item.Kind) is null;

    /// <summary>
    /// Says which widget is missing and why. The item keeps all its saved data (its options and content are never touched here);
    /// right-click still offers Customize and Remove, and the widget comes back by itself when its code is available again.
    /// </summary>
    private FrameworkElement BuildUnavailable(LauncherItem item, double w, double h)
    {
        var card = Card(item);
        var stack = new StackPanel { Margin = new Thickness(14), VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(Theme.Icon(Theme.Glyphs.App, 18, Theme.Muted));
        string name = string.IsNullOrWhiteSpace(item.Name) ? "This widget" : item.Name;
        const string installed = "it isn't installed (it was removed, or it comes from another version of Desktop Deck)";
        string reason = installed;
        var note = Theme.Label($"{name} isn't available: {reason}.", 12, Theme.Text2);
        note.TextWrapping = TextWrapping.Wrap;
        note.TextTrimming = TextTrimming.CharacterEllipsis;
        note.Margin = new Thickness(0, 8, 0, 0);
        note.ToolTip = "Your saved settings and data for this widget are kept. It comes back when the widget is available again, or you can remove it.";
        stack.Children.Add(note);
        card.Child = stack;
        return card;
    }
}
