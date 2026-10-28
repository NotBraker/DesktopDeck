using DesktopDeck.Models;

namespace DesktopDeck;

/// <summary>The deck-wide look: applying it to every widget for good.</summary>
internal sealed partial class DeckHub
{
    /// <summary>
    /// "Reset every widget to this look": each widget takes the deck's card style and loses its own colors, opacity, corners, text size and spacing
    /// (see <see cref="LookResolver.ResetWidgets"/>). One undo step. Returns how many widgets changed.
    /// </summary>
    public int ResetWidgetLooks()
    {
        FlushAll();
        // The label is picked up by the save below (and dropped again if nothing changed).
        var changed = LookResolver.ResetWidgets(Settings);
        if (changed > 0) nextLabel = "Reset every widget's look";
        RenderAll();
        Save();
        if (changed > 0) Announce(changed == 1 ? "Reset 1 widget to the deck look." : $"Reset {changed} widgets to the deck look.", "Undo", () => Undo());
        else Announce("Every widget already has this look.");
        return changed;
    }
}
