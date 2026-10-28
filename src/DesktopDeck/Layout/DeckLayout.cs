using System;
using System.Collections.Generic;
using System.Linq;

namespace DesktopDeck.Layout;

public readonly record struct TilePosition(double X, double Y);

public readonly record struct TileRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

/// <summary>
/// Places variable-size items inside a DIP viewport without overlap. Optionally keeps the
/// central 60% x 60% clear and optionally aligns positions to a grid.
/// </summary>
public static class DeckLayout
{
    public const double DefaultGap = 12;
    public const double DefaultGrid = 24;
    public const int MinGridSize = 8, MaxGridSize = 64, MaxGridGap = 32;

    /// <summary>The minimum space between items (DIP). Set once for the whole deck with <see cref="Configure"/>.</summary>
    public static double Gap { get; private set; } = DefaultGap;
    /// <summary>The grid cell size (DIP) positions snap to.</summary>
    public static double Grid { get; private set; } = DefaultGrid;

    /// <summary>Clamps a grid size to 8-64 (anything unusable becomes the default).</summary>
    public static int ClampGridSize(double value) => double.IsFinite(value) ? (int)Math.Clamp(Math.Round(value), MinGridSize, MaxGridSize) : (int)DefaultGrid;

    /// <summary>Clamps a gap to 0-32 (anything unusable becomes the default).</summary>
    public static int ClampGap(double value) => double.IsFinite(value) ? (int)Math.Clamp(Math.Round(value), 0, MaxGridGap) : (int)DefaultGap;

    /// <summary>Sets the grid cell size and the gap between items; out-of-range values are clamped. The defaults are 24 and 12.</summary>
    public static void Configure(double gridSize, double gap)
    {
        Grid = ClampGridSize(gridSize);
        Gap = ClampGap(gap);
    }

    private const double Epsilon = 0.0000001;

    public static bool IsAllowed(TileRect rect, double width, double height, bool keepCenterClear)
    {
        if (!double.IsFinite(rect.X) || !double.IsFinite(rect.Y)) return false;
        return Regions(rect.Width, rect.Height, width, height, keepCenterClear, 0).Any(region => region.Contains(rect.X, rect.Y));
    }

    /// <summary>True if the rectangles overlap or are separated by less than Gap.</summary>
    public static bool Overlaps(TileRect a, TileRect b) =>
        a.X < b.Right + Gap - Epsilon && b.X < a.Right + Gap - Epsilon &&
        a.Y < b.Bottom + Gap - Epsilon && b.Y < a.Bottom + Gap - Epsilon;

    /// <summary>Rounds a size so that size + Gap spans whole grid cells.</summary>
    public static double SnapSize(double size, double minimum) =>
        Math.Max(Math.Ceiling((minimum + Gap) / Grid), Math.Round((size + Gap) / Grid)) * Grid - Gap;

    /// <summary>
    /// Finds the legal, non-overlapping top-left position nearest to (x, y) for a w x h item,
    /// or null if none exists. With grid &gt; 0, only multiples of grid are returned.
    /// </summary>
    public static TilePosition? FindSpace(double x, double y, double w, double h, double width, double height,
        IEnumerable<TileRect> occupied, bool keepCenterClear, double grid = 0)
    {
        ArgumentNullException.ThrowIfNull(occupied);
        x = FiniteOrZero(x);
        y = FiniteOrZero(y);
        grid = double.IsFinite(grid) && grid > 0 ? grid : 0;
        var regions = Regions(w, h, width, height, keepCenterClear, grid);
        if (regions.Count == 0) return null;
        var obstacles = occupied.Where(r => double.IsFinite(r.X) && double.IsFinite(r.Y) &&
            double.IsFinite(r.Width) && double.IsFinite(r.Height)).ToArray();
        TilePosition? best = null;
        double bestDistance = double.PositiveInfinity;

        foreach (var region in regions)
        {
            var projected = new TilePosition(
                SnapNearest(Math.Clamp(x, region.MinX, region.MaxX), grid, region.MinX, region.MaxX),
                SnapNearest(Math.Clamp(y, region.MinY, region.MaxY), grid, region.MinY, region.MaxY));
            if (!obstacles.Any(o => Overlaps(new TileRect(projected.X, projected.Y, w, h), o)))
            {
                Consider(projected, x, y, ref best, ref bestDistance);
                continue;
            }

            // Obstacles partition each region into axis-aligned cells. On each axis the nearest
            // free coordinate is the (grid-rounded) target or a (grid-rounded) cell boundary, so
            // the Cartesian product of those values contains the optimum.
            var xs = new HashSet<double> { region.MinX, region.MaxX };
            var ys = new HashSet<double> { region.MinY, region.MaxY };
            AddTarget(xs, x, grid, region.MinX, region.MaxX);
            AddTarget(ys, y, grid, region.MinY, region.MaxY);
            foreach (var o in obstacles)
            {
                AddWithin(xs, Down(o.X - w - Gap, grid), region.MinX, region.MaxX);
                AddWithin(xs, Up(o.Right + Gap, grid), region.MinX, region.MaxX);
                AddWithin(ys, Down(o.Y - h - Gap, grid), region.MinY, region.MaxY);
                AddWithin(ys, Up(o.Bottom + Gap, grid), region.MinY, region.MaxY);
            }

            foreach (var candidateX in xs)
            foreach (var candidateY in ys)
            {
                var candidate = new TilePosition(candidateX, candidateY);
                if (DistanceSquared(candidate, x, y) > bestDistance) continue;
                var rect = new TileRect(candidateX, candidateY, w, h);
                if (obstacles.Any(o => Overlaps(rect, o))) continue;
                Consider(candidate, x, y, ref best, ref bestDistance);
            }
        }
        return best;
    }

    private static List<Region> Regions(double w, double h, double width, double height, bool keepCenterClear, double grid)
    {
        var result = new List<Region>(4);
        if (!double.IsFinite(width) || !double.IsFinite(height) || !double.IsFinite(w) || !double.IsFinite(h) ||
            w <= 0 || h <= 0 || width < w || height < h) return result;

        double maxX = width - w;
        double maxY = height - h;
        if (!keepCenterClear) Add(result, 0, 0, maxX, maxY, grid);
        else
        {
            double centerLeft = width * 0.2, centerRight = width * 0.8;
            double centerTop = height * 0.2, centerBottom = height * 0.8;
            if (centerTop >= h) Add(result, 0, 0, maxX, centerTop - h, grid);
            if (centerBottom <= maxY) Add(result, 0, centerBottom, maxX, maxY, grid);
            if (centerLeft >= w) Add(result, 0, 0, centerLeft - w, maxY, grid);
            if (centerRight <= maxX) Add(result, centerRight, 0, maxX, maxY, grid);
        }
        return result;
    }

    private static void Add(List<Region> regions, double minX, double minY, double maxX, double maxY, double grid)
    {
        minX = Up(minX, grid); minY = Up(minY, grid);
        maxX = Down(maxX, grid); maxY = Down(maxY, grid);
        if (minX <= maxX + Epsilon && minY <= maxY + Epsilon) regions.Add(new Region(minX, minY, maxX, maxY));
    }

    private static double Up(double value, double grid) => grid > 0 ? Math.Ceiling(value / grid - Epsilon) * grid : value;
    private static double Down(double value, double grid) => grid > 0 ? Math.Floor(value / grid + Epsilon) * grid : value;

    private static double SnapNearest(double value, double grid, double min, double max) =>
        grid > 0 ? Math.Clamp(Math.Round(value / grid) * grid, min, max) : value;

    private static void AddTarget(HashSet<double> values, double target, double grid, double min, double max)
    {
        double clamped = Math.Clamp(target, min, max);
        if (grid <= 0) { values.Add(clamped); return; }
        values.Add(Math.Clamp(Down(clamped, grid), min, max));
        values.Add(Math.Clamp(Up(clamped, grid), min, max));
    }

    private static void AddWithin(HashSet<double> values, double value, double min, double max)
    {
        if (value >= min - Epsilon && value <= max + Epsilon) values.Add(Math.Clamp(value, min, max));
    }

    private static void Consider(TilePosition candidate, double x, double y,
        ref TilePosition? best, ref double bestDistance)
    {
        double distance = DistanceSquared(candidate, x, y);
        if (best is null || distance < bestDistance ||
            (distance == bestDistance && (candidate.Y < best.Value.Y ||
             (candidate.Y == best.Value.Y && candidate.X < best.Value.X))))
        {
            best = candidate;
            bestDistance = distance;
        }
    }

    private static double DistanceSquared(TilePosition point, double x, double y) =>
        (point.X - x) * (point.X - x) + (point.Y - y) * (point.Y - y);

    private static double FiniteOrZero(double value) => double.IsFinite(value) ? value : 0;

    private readonly record struct Region(double MinX, double MinY, double MaxX, double MaxY)
    {
        public bool Contains(double x, double y) =>
            x >= MinX - Epsilon && x <= MaxX + Epsilon &&
            y >= MinY - Epsilon && y <= MaxY + Epsilon;
    }
}
