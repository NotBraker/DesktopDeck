using DesktopDeck.Layout;
using DesktopDeck.Models;

namespace DesktopDeck;

// First run: what the welcome window's choice puts on the main screen.
internal sealed partial class DeckHub
{
    /// <summary>
    /// Puts a starter's widgets on the main screen's deck (visible right away, in the layout in use) and, when given, desktop shortcuts as launchers in whatever room
    /// is left. A null starter means "start empty". Returns how many widgets and launchers were added. Nothing is read from the desktop here: the caller passes the shortcuts.
    /// </summary>
    public int ApplyWelcome(WidgetTemplate? starter, IReadOnlyList<LauncherItem>? shortcuts)
    {
        if (Displays.Count == 0) return 0;
        var (display, width, height, items, spots) = PlanTemplate(starter ?? new WidgetTemplate("empty", "Empty", "", "", "#C4684F", []), floating: false);
        var occupied = new List<TileRect>();
        string displayId = display.IsPrimary ? "" : display.Id;
        int added = 0;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] is not { } item || spots[i] is not { } spot) continue;
            var (dw, dh) = ItemKinds.DefaultSize(item.Kind);
            item.Display = displayId;
            item.X = Math.Clamp(spot.X / width, 0, 1);
            item.Y = Math.Clamp(spot.Y / height, 0, 1);
            item.Width = Math.Abs(spot.Width - dw) < 0.5 ? 0 : spot.Width;
            item.Height = Math.Abs(spot.Height - dh) < 0.5 ? 0 : spot.Height;
            Settings.Items.Add(item);
            occupied.Add(spot);
            added++;
        }
        if (shortcuts is { Count: > 0 } && Primary is { } deck)
        {
            var (viewW, viewH, areas) = deck.TemplateViewport();
            if (viewW >= 100 && viewH >= 100)
            {
                occupied.AddRange(areas);
                foreach (var launcher in shortcuts)
                {
                    var (dw, dh) = ItemKinds.DefaultSize(launcher.Kind);
                    if (DeckLayout.FindSpace(launcher.X * viewW, launcher.Y * viewH, dw, dh, viewW, viewH, occupied, Settings.KeepCenterClear, Settings.SnapToGrid ? DeckLayout.Grid : 0) is not { } spot) continue;
                    occupied.Add(new TileRect(spot.X, spot.Y, dw, dh));
                    launcher.Display = displayId;
                    launcher.X = Math.Clamp(spot.X / viewW, 0, 1);
                    launcher.Y = Math.Clamp(spot.Y / viewH, 0, 1);
                    Settings.Items.Add(launcher);
                    added++;
                }
            }
        }
        RenderAll();
        Save();
        return added;
    }
}
