using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DesktopDeck;

internal sealed partial class ControlCenter
{
    /// <summary>
    /// The theme changed: draws the whole control center again with the new colors and font (frame and tabs), on the same tab and scroll position.
    /// The dim backdrop stays as it is. Must not run inside one of this window's own click handlers (see <see cref="DeckHub.ApplyThemeSoon"/>).
    /// </summary>
    internal void ApplyTheme()
    {
        if (closed) return;
        string tab = current;
        double offset = scroll.VerticalOffset;
        FontFamily = Theme.UiFont;
        Foreground = Theme.Text;
        // The pieces that are kept between frames must leave their old parents before the frame is built again.
        scroll.Content = null;
        foreach (var kept in new FrameworkElement[] { scroll, title, subtitle })
        {
            if (kept.Parent is Panel parent) parent.Children.Remove(kept);
            else if (kept.Parent is Decorator decorator) decorator.Child = null;
        }
        title.Foreground = Theme.Text;
        title.FontFamily = Theme.UiFont;
        subtitle.Foreground = Theme.Muted;
        subtitle.FontFamily = Theme.UiFont;
        settingsContent?.Dispose();
        settingsContent = null;
        cache.Clear();
        navItems.Clear();
        stale.Clear();
        root.Children.Clear();
        inner.Children.Clear();
        inner.ColumnDefinitions.Clear();
        panel.Child = null;
        BuildFrame();
        current = "";
        ShowTab(tab);
        UpdateLayout();
        scroll.ScrollToVerticalOffset(offset);
    }
}
