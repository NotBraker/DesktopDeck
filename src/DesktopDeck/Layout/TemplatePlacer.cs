namespace DesktopDeck.Layout;

/// <summary>One widget to place: its size, and the smallest it may shrink to when the screen is crowded.</summary>
public readonly record struct TemplateSlot(double Width, double Height, double MinWidth, double MinHeight);

/// <summary>
/// Lays a set of widgets out on a screen for a starter layout: biggest first, left to right in rows from the top-left corner, each as close to its
/// row position as <see cref="DeckLayout.FindSpace"/> allows (so nothing overlaps, nothing sits under the reserved areas and the grid is kept).
/// </summary>
public static class TemplatePlacer
{
    public const double Margin = 24;

    /// <summary>One result per slot, in the same order: where it goes (maybe smaller than asked), or null when there is no room even at the minimum size.</summary>
    public static List<TileRect?> Place(IReadOnlyList<TemplateSlot> slots, double width, double height, IEnumerable<TileRect> reserved, bool keepCenterClear, double grid)
    {
        // As asked first; on a small screen everything is shrunk together (85%, 70%, 55% of the asked size, never below a widget's minimum) so as many as possible still fit.
        var occupied = reserved.ToList();
        List<TileRect?>? best = null;
        foreach (double factor in new[] { 1, 0.85, 0.7, 0.55 })
        {
            var scaled = factor == 1 ? slots : slots.Select(s => s with { Width = Math.Max(s.MinWidth, Fit(s.Width * factor)), Height = Math.Max(s.MinHeight, Fit(s.Height * factor)) }).ToList();
            var result = PlaceOnce(scaled, width, height, occupied, keepCenterClear, grid);
            if (best is null || result.Count(r => r is not null) > best.Count(r => r is not null)) best = result;
            if (result.All(r => r is not null)) break;
        }
        return best!;
    }

    /// <summary>A size on the 24 DIP grid with the 12 DIP gap (n * 24 - 12) nearest to <paramref name="size"/>.</summary>
    private static double Fit(double size) => Math.Round((size + 12) / 24) * 24 - 12;

    private static List<TileRect?> PlaceOnce(IReadOnlyList<TemplateSlot> slots, double width, double height, IEnumerable<TileRect> reserved, bool keepCenterClear, double grid)
    {
        var result = new List<TileRect?>(Enumerable.Repeat<TileRect?>(null, slots.Count));
        var occupied = reserved.ToList();
        double cx = Margin, cy = Margin, rowBottom = Margin;
        foreach (int index in Enumerable.Range(0, slots.Count).OrderByDescending(i => slots[i].Width * slots[i].Height).ThenBy(i => i))
        {
            var slot = slots[index];
            if (cx > Margin && cx + slot.Width > width - Margin) { cx = Margin; cy = rowBottom + DeckLayout.Gap; }
            TileRect? placed = null;
            foreach (var (w, h) in new[] { (slot.Width, slot.Height), (slot.MinWidth, slot.MinHeight) })
            {
                if (DeckLayout.FindSpace(cx, cy, w, h, width, height, occupied, keepCenterClear, grid) is { } spot) { placed = new TileRect(spot.X, spot.Y, w, h); break; }
            }
            if (placed is not { } rect) continue;
            result[index] = rect;
            occupied.Add(rect);
            cx = rect.Right + DeckLayout.Gap;
            rowBottom = Math.Max(rowBottom, rect.Bottom);
        }
        return result;
    }
}
