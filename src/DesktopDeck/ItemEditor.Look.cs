using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DesktopDeck;

/// <summary>Customize while "one look for every widget" is on: the widget's own appearance controls give way to a note and a switch.</summary>
internal sealed partial class ItemEditor
{
    /// <summary>
    /// When the deck look applies to every widget, adds a short note and the switch "Give this widget its own look" to <paramref name="root"/>, and shows
    /// the panels in <paramref name="ownLook"/> (the card controls that the deck look replaces) only while the switch is on. Does nothing when the deck look is off.
    /// </summary>
    private void AddDeckLookNotice(Panel root, params Panel[] ownLook)
    {
        if (deckAppearance is not { ApplyToAll: true }) return;
        var note = Theme.Label("This widget uses the deck look.", 13.5, Theme.Text);
        note.FontWeight = FontWeights.SemiBold;
        var detail = Theme.Label("Change it in the control center, under Appearance.", 12, Theme.Muted);
        detail.TextWrapping = TextWrapping.Wrap;
        var text = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        text.Children.Add(note);
        text.Children.Add(detail);
        var box = new Border
        {
            Background = Theme.Tint(Theme.PrimaryHex, 0.10), BorderBrush = Theme.Primary, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 12, 14, 0), Margin = new Thickness(0, 6, 0, 12),
        };
        var inside = new StackPanel();
        inside.Children.Add(text);
        void Sync()
        {
            text.Visibility = draft.OwnLook ? Visibility.Collapsed : Visibility.Visible;
            foreach (var panel in ownLook) panel.Visibility = draft.OwnLook ? Visibility.Visible : Visibility.Collapsed;
        }
        inside.Children.Add(SwitchRow("Give this widget its own look", draft.OwnLook, on => { draft.OwnLook = on; Sync(); }));
        box.Child = inside;
        root.Children.Add(box);
        Sync();
    }
}
