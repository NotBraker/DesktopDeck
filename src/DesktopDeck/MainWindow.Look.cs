using System;
using System.Windows;
using DesktopDeck.Models;

namespace DesktopDeck;

// Frosted glass cards on the deck: each one paints the blurred deck background behind it (see Appearance.Frost.cs).
public sealed partial class MainWindow
{
    /// <summary>
    /// While a widget's content is built, frosted cards can see the blurred deck background under the widget's place. Only the deck behind
    /// the other windows has a background to look through: the layer in front of other windows and floating widgets get the plain tinted card.
    /// </summary>
    private IDisposable? FrostScope(LauncherItem item)
    {
        if (IsFront || buildKey is not null || !rects.TryGetValue(item.Id, out var rect)) return null;
        if (Appearance.Effective(settings.Appearance, item).CardStyle != "frosted") return null;
        var backdrop = Appearance.FrostBackdrop(Appearance.DeckStyle(settings, Display.Id), new Size(canvas.ActualWidth, canvas.ActualHeight), Display);
        return Appearance.PushBackdrop(backdrop, new Rect(rect.X, rect.Y, rect.Width, rect.Height));
    }
}
