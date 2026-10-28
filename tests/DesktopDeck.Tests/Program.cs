using DesktopDeck.Layout;

const double TW = 184, TH = 84;

var tests = new (string Name, Action Run)[]
{
    ("Exact ring boundaries and full tile extents", Boundaries),
    ("Nearest projection on all strips", Projection),
    ("Normalized positions survive resolution changes", NormalizedPositions),
    ("Collision gap and closest automatic placement", CollisionAndPlacement),
    ("Automatic placement and saturation", Saturation),
    ("Random projection and nearest-space validation", RandomGeometry),
    ("Tiny viewports and non-finite inputs", TinyViewports),
    ("Free placement may use the center", FreePlacement),
    ("Variable sizes never overlap", VariableSizes),
    ("Grid placement is aligned and optimal on the lattice", GridPlacement),
    ("Grid size snapping", SizeSnapping),
    ("Configurable grid size and gap", ConfigurableGrid),
    ("Items resolve to their monitor or fall back to the main one", DisplayResolution),
};

int failures = 0;
foreach (var (name, run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception error) { failures++; Console.Error.WriteLine($"FAIL {name}: {error.Message}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
return failures == 0 ? 0 : 1;

static TileRect R(double x, double y, double w = TW, double h = TH) => new(x, y, w, h);
static TilePosition Ring(double x, double y, double w, double h) => DeckLayout.FindSpace(x, y, TW, TH, w, h, [], true)!.Value;

static void Boundaries()
{
    const double w = 1920, h = 1080;
    foreach (var p in new[] { R(200, 400), R(1536, 400), R(700, 132), R(700, 864), R(0, 0), R(w - 184, h - 84) })
        Allowed(p, w, h, true);
    foreach (var p in new[] { R(200.01, 400), R(1535.99, 400), R(700, 132.01), R(700, 863.99),
        R(-0.01, 0), R(w - 183.99, 0), R(0, h - 83.99), R(0, -0.01) })
        Check(!DeckLayout.IsAllowed(p, w, h, true), $"Invalid position accepted: {p}");
    Allowed(R(300, 0), 800, 420, true);
    Allowed(R(0, 100), 920, 300, true);
}

static void Projection()
{
    Equal(Ring(300, 400, 1920, 1080), new(200, 400));
    Equal(Ring(1450, 400, 1920, 1080), new(1536, 400));
    Equal(Ring(700, 300, 1920, 1080), new(700, 132));
    Equal(Ring(700, 750, 1920, 1080), new(700, 864));
    Equal(Ring(-300, -200, 1920, 1080), new(0, 0));
    Equal(Ring(9999, 9999, 1920, 1080), new(1736, 996));
}

static void NormalizedPositions()
{
    var sizes = new[] { (1920d, 1080d), (1366d, 728d), (2560d, 1392d), (1280d, 720d), (800d, 1280d), (3440d, 1400d) };
    var normalized = new[] { (0d, 0d), (.13, .52), (.87, .35), (.5, .92), (.49, .12) };
    foreach (var (w, h) in sizes)
    foreach (var (nx, ny) in normalized)
    {
        var p = Ring(nx * w, ny * h, w, h);
        Allowed(R(p.X, p.Y), w, h, true);
        var saved = (X: p.X / w, Y: p.Y / h);
        Equal(Ring(saved.X * w, saved.Y * h, w, h), p);
        foreach (var (nextW, nextH) in sizes)
        {
            var moved = Ring(saved.X * nextW, saved.Y * nextH, nextW, nextH);
            Allowed(R(moved.X, moved.Y), nextW, nextH, true);
        }
    }
}

static void CollisionAndPlacement()
{
    var origin = R(400, 0);
    Check(DeckLayout.Overlaps(origin, R(595.99, 0)), "Horizontal gap must be 12 DIP");
    Check(!DeckLayout.Overlaps(origin, R(596, 0)), "Exact horizontal gap is valid");
    Check(DeckLayout.Overlaps(origin, R(400, 95.99)), "Vertical gap must be 12 DIP");
    Check(!DeckLayout.Overlaps(origin, R(400, 96)), "Exact vertical gap is valid");
    Equal(DeckLayout.FindSpace(400, 0, TW, TH, 1920, 1080, [origin], true)!.Value, new(400, 96));
    Equal(DeckLayout.FindSpace(400, 0, TW, TH, 1920, 1080, [], true)!.Value, new(400, 0));
    var fractional = R(400.375, 0.125);
    Equal(DeckLayout.FindSpace(fractional.X, fractional.Y, TW, TH, 1920, 1080, [fractional], true)!.Value, new(400.375, 96.125));
}

static void Saturation()
{
    foreach (var keep in new[] { true, false })
    foreach (var (w, h) in new[] { (1000d, 420d), (1920d, 1040d), (800d, 600d), (920d, 300d) })
    {
        var occupied = new List<TileRect>();
        for (int i = 0; i < 400; i++)
        {
            var next = DeckLayout.FindSpace(w / 2, h / 2, TW, TH, w, h, occupied, keep);
            if (next is null) break;
            var rect = R(next.Value.X, next.Value.Y);
            Allowed(rect, w, h, keep);
            Check(occupied.All(p => !DeckLayout.Overlaps(p, rect)), "New tile overlaps existing tile");
            occupied.Add(rect);
        }
        Check(occupied.Count > 0 && occupied.Count < 400, "Layout must reach finite saturation");
        Check(DeckLayout.FindSpace(0, 0, TW, TH, w, h, occupied, keep) is null, "Saturation must not depend on desired position");
        for (double x = 0; x <= w - TW; x += 8)
        for (double y = 0; y <= h - TH; y += 8)
            Check(!DeckLayout.IsAllowed(R(x, y), w, h, keep) || occupied.Any(p => DeckLayout.Overlaps(p, R(x, y))),
                "Saturated layout still has an available sampled position");
    }
}

static void RandomGeometry()
{
    var random = new Random(241031);
    for (int i = 0; i < 4000; i++)
    {
        double w = random.Next(920, 5000), h = random.Next(420, 2500);
        double x = random.NextDouble() * w * 3 - w, y = random.NextDouble() * h * 3 - h;
        var projected = Ring(x, y, w, h);
        Allowed(R(projected.X, projected.Y), w, h, true);
        Equal(Ring(projected.X, projected.Y, w, h), projected);
    }
    foreach (var keep in new[] { true, false })
        Oracle(random, keep, 0, 12);
}

static void TinyViewports()
{
    foreach (var (w, h) in new[] { (0d, 0d), (100d, 100d), (800d, 400d), (919d, 419d), (-100d, 500d), (double.NaN, 1080d), (1920d, double.PositiveInfinity) })
        Check(DeckLayout.FindSpace(0, 0, TW, TH, w, h, [], true) is null, "Impossible viewport must return no space");
    Check(DeckLayout.FindSpace(0, 0, TW, TH, 100, 100, [], false) is null, "Item larger than viewport has no space");
    Check(!DeckLayout.IsAllowed(R(double.NaN, 0), 1920, 1080, true), "NaN cannot be allowed");
    Allowed(R(Ring(double.NaN, double.PositiveInfinity, 1920, 1080).X, Ring(double.NaN, double.PositiveInfinity, 1920, 1080).Y), 1920, 1080, true);
}

static void FreePlacement()
{
    var p = DeckLayout.FindSpace(900, 500, TW, TH, 1920, 1080, [], false)!.Value;
    Equal(p, new(900, 500));
    Check(DeckLayout.IsAllowed(R(900, 500), 1920, 1080, false), "Center is allowed when not reserved");
    Check(!DeckLayout.IsAllowed(R(900, 500), 1920, 1080, true), "Center is rejected when reserved");
    Check(!DeckLayout.IsAllowed(R(1800, 500), 1920, 1080, false), "Viewport bounds still apply");
}

static void VariableSizes()
{
    var random = new Random(77);
    foreach (var keep in new[] { true, false })
    for (int trial = 0; trial < 30; trial++)
    {
        const double w = 2560, h = 1392;
        var placed = new List<TileRect>();
        for (int i = 0; i < 40; i++)
        {
            double iw = 60 + random.Next(0, 400), ih = 60 + random.Next(0, 300);
            var p = DeckLayout.FindSpace(random.NextDouble() * w, random.NextDouble() * h, iw, ih, w, h, placed, keep);
            if (p is null) continue;
            var rect = new TileRect(p.Value.X, p.Value.Y, iw, ih);
            Allowed(rect, w, h, keep);
            Check(placed.All(o => !DeckLayout.Overlaps(o, rect)), "Variable-size placement collides");
            placed.Add(rect);
        }
    }
}

static void GridPlacement()
{
    var random = new Random(5150);
    foreach (var keep in new[] { true, false })
        Oracle(random, keep, DeckLayout.Grid, DeckLayout.Grid);
    var first = DeckLayout.FindSpace(13, 13, 180, 84, 1920, 1080, [], false, DeckLayout.Grid)!.Value;
    Equal(first, new(24, 24));
    var second = DeckLayout.FindSpace(30, 30, 180, 84, 1920, 1080, [R(24, 24, 180, 84)], false, DeckLayout.Grid)!.Value;
    Check(second.X % 24 == 0 && second.Y % 24 == 0, "Grid placement must be aligned");
    Check(!DeckLayout.Overlaps(R(24, 24, 180, 84), R(second.X, second.Y, 180, 84)), "Grid placement collides");
    // 180 + 12 = 8 cells, so grid neighbours sit exactly one gap apart.
    Equal(DeckLayout.FindSpace(205, 24, 180, 84, 1920, 1080, [R(24, 24, 180, 84)], false, DeckLayout.Grid)!.Value, new(216, 24));
}

static void SizeSnapping()
{
    Check(DeckLayout.SnapSize(184, 60) == 180, "184 snaps to 180");
    Check(DeckLayout.SnapSize(90, 60) == 84, "90 snaps to 84");
    Check(DeckLayout.SnapSize(10, 60) == 60, "Minimum is respected and aligned");
    Check(DeckLayout.SnapSize(10, 61) == 84, "Minimum rounds up to the next cell");
}

static void ConfigurableGrid()
{
    try
    {
        Check(DeckLayout.Grid == 24 && DeckLayout.Gap == 12, "The defaults are a 24 grid and a 12 gap");
        foreach (var (grid, gap) in new[] { (8d, 0d), (16d, 8d), (32d, 16d), (48d, 32d), (64d, 12d), (24d, 0d), (10d, 5d) })
        {
            DeckLayout.Configure(grid, gap);
            Check(DeckLayout.Grid == grid && DeckLayout.Gap == gap, $"Configure({grid}, {gap}) is kept");
            var origin = R(0, 0, 100, 50);
            Check(DeckLayout.Overlaps(origin, new TileRect(100 + gap - 0.01, 0, 100, 50)),$"Items closer than the gap ({gap}) overlap");
            Check(!DeckLayout.Overlaps(origin, new TileRect(100 + gap, 0, 100, 50)), $"Items exactly {gap} apart are fine");
            Check(!DeckLayout.Overlaps(origin, new TileRect(0, 50 + gap, 100, 50)), $"Items exactly {gap} apart vertically are fine");
            // Sizes: size + gap spans whole cells, and the minimum is respected.
            foreach (double size in new[] { 10d, 90d, 184d, 301d })
            {
                double snapped = DeckLayout.SnapSize(size, 40);
                double cells = (snapped + gap) / grid;
                Check(Math.Abs(cells - Math.Round(cells)) < 1e-9 && snapped >= 40, $"SnapSize({size}) is {snapped} on a {grid}/{gap} grid");
            }
            // Placement: grid-aligned, never within the gap of a neighbour.
            var first = DeckLayout.FindSpace(grid / 3, grid / 3, 100, 50, 1920, 1080, [], false, grid)!.Value;
            Check(first.X % grid == 0 && first.Y % grid == 0, "First placement is on the grid");
            var firstRect = new TileRect(first.X, first.Y, 100, 50);
            var next = DeckLayout.FindSpace(first.X + 101, first.Y, 100, 50, 1920, 1080, [firstRect], false, grid)!.Value;
            Check(next.X % grid == 0 && next.Y % grid == 0 && !DeckLayout.Overlaps(firstRect, new TileRect(next.X, next.Y, 100, 50)), "A neighbour is aligned and keeps the gap");
            var random = new Random((int)(grid * 100 + gap));
            Oracle(random, true, grid, grid);
            Oracle(random, false, 0, 7);
            if (gap == 0)
            {
                // With no gap, touching items are allowed.
                var touching = DeckLayout.FindSpace(first.X + 100, first.Y, 100, 50, 1920, 1080, [firstRect], false, 0)!.Value;
                Equal(touching, new(first.X + 100, first.Y));
            }
        }
        DeckLayout.Configure(24, 12);
        Check(DeckLayout.Overlaps(R(0, 0), R(190, 0)), "Back to the default gap: 6 DIP apart overlaps");
    }
    finally { DeckLayout.Configure(DeckLayout.DefaultGrid, DeckLayout.DefaultGap); }
}

// Brute-force check: no sampled legal position is nearer than the search result.
static void Oracle(Random random, bool keep, double grid, double step)
{
    for (int trial = 0; trial < 25; trial++)
    {
        const double w = 1280, h = 720;
        var occupied = new List<TileRect>();
        for (int i = 0; i < 8; i++)
        {
            double iw = 60 + random.Next(0, 200), ih = 60 + random.Next(0, 120);
            var p = DeckLayout.FindSpace(random.NextDouble() * w, random.NextDouble() * h, iw, ih, w, h, occupied, keep, grid);
            if (p is not null) occupied.Add(new TileRect(p.Value.X, p.Value.Y, iw, ih));
        }
        double targetX = random.NextDouble() * w, targetY = random.NextDouble() * h;
        var result = DeckLayout.FindSpace(targetX, targetY, TW, TH, w, h, occupied, keep, grid);
        if (result is not null)
        {
            Allowed(R(result.Value.X, result.Value.Y), w, h, keep);
            Check(occupied.All(p => !DeckLayout.Overlaps(p, R(result.Value.X, result.Value.Y))), "Random placement collides");
            if (grid > 0) Check(result.Value.X % grid == 0 && result.Value.Y % grid == 0, "Grid result must be aligned");
        }
        for (double x = 0; x <= w - TW; x += step)
        for (double y = 0; y <= h - TH; y += step)
        {
            var candidate = R(x, y);
            if (!DeckLayout.IsAllowed(candidate, w, h, keep) || occupied.Any(p => DeckLayout.Overlaps(p, candidate))) continue;
            Check(result is not null, "Search incorrectly reports full layout");
            double candidateDistance = Math.Pow(x - targetX, 2) + Math.Pow(y - targetY, 2);
            double resultDistance = Math.Pow(result!.Value.X - targetX, 2) + Math.Pow(result.Value.Y - targetY, 2);
            Check(resultDistance <= candidateDistance + 0.00001, "Search did not minimize distance");
        }
    }
}

static void Allowed(TileRect p, double w, double h, bool keep)
{
    const double epsilon = .000001;
    Check(DeckLayout.IsAllowed(p, w, h, keep), $"Position is not allowed: {p} in {w}x{h}");
    Check(p.X >= -epsilon && p.Y >= -epsilon && p.Right <= w + epsilon && p.Bottom <= h + epsilon, "Tile exceeds viewport bounds");
    if (keep)
        Check(p.Right <= .2 * w + epsilon || p.X >= .8 * w - epsilon || p.Bottom <= .2 * h + epsilon || p.Y >= .8 * h - epsilon,
            "Tile intrudes on center rectangle");
}

static void Equal(TilePosition actual, TilePosition expected) =>
    Check(Math.Abs(actual.X - expected.X) < .000001 && Math.Abs(actual.Y - expected.Y) < .000001, $"Expected {expected}, got {actual}");

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void DisplayResolution()
{
    var main = new DisplayInfo("main", 1, true, new PixelRect(0, 0, 2560, 1440), new PixelRect(0, 0, 2560, 1400), 1);
    var left = new DisplayInfo(@"\?\DISPLAY#ABC#1", 2, false, new PixelRect(-1080, -179, 1080, 1920), new PixelRect(-1080, -179, 1080, 1920), 1.25);
    var displays = new[] { left, main };
    if (DisplayMap.Resolve("", displays) != main) throw new Exception("empty display should mean the main one");
    if (DisplayMap.Resolve(@"\?\display#abc#1", displays) != left) throw new Exception("ids compare without case");
    if (DisplayMap.Resolve("unplugged", displays) != main) throw new Exception("unplugged display should fall back to the main one");
    if (DisplayMap.At(-500, 900, displays) != left || DisplayMap.At(100, 100, displays) != main) throw new Exception("point lookup");
    if (DisplayMap.At(-2000, 0, displays) != left || DisplayMap.At(9000, 500, displays) != main) throw new Exception("points outside every monitor use the nearest");
    if (!main.Name.Contains("main") || !left.Name.StartsWith("Display 2")) throw new Exception("names");
}
