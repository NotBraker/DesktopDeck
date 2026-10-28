using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using DesktopDeck.Layout;
using DesktopDeck.Models;
using DesktopDeck.Services;

namespace DesktopDeck;

/// <summary>
/// The "Show desktop icons" checks of --host-test, on the real desktop (never moves the mouse, only asks Windows who is at a point):
/// with icons on, an empty spot belongs to Explorer's icon view and a widget to the deck, on every monitor; Edit layout and turning the
/// switch off give the empty spot to the deck again; the region is back after Reconnect (windows kept and windows recreated).
/// Who is at a point is asked of the desktop window's own children (ChildWindowFromPointEx / RealChildWindowFromPoint), which honour regions
/// and are not fooled by another app's window covering the monitor; WindowFromPoint is also checked where nothing covers the spot.
/// </summary>
internal static class IconsHostTest
{
    public static void Run(DeckHub h, List<string> lines, Action<bool, string> check, Action<int> settle, string? shotsFolder)
    {
        bool before = h.ShowDesktopIcons;
        lines.Add("     -- Show desktop icons --");
        try { RunCore(h, lines, check, settle, shotsFolder); }
        finally
        {
            h.SetShowDesktopIcons(before);
            settle(6);
        }
    }

    private static void RunCore(DeckHub h, List<string> lines, Action<bool, string> check, Action<int> settle, string? shotsFolder)
    {
        h.SetMode("background");
        h.SetEditing(false);
        h.SetShowDesktopIcons(true);
        settle(15);

        var decks = h.Backgrounds.ToList();
        check(decks.Count == h.Displays.Count && decks.All(d => d.HasRegion && DeckRegion.Kind(d.Hwnd) > 1), $"icons on: every desktop deck is cut down to its widgets ({string.Join(", ", decks.Select(d => $"{d.Display.Name} kind={DeckRegion.Kind(d.Hwnd)} shapes={d.RegionShapes.Count} bounds={Fmt(DeckRegion.Bounds(d.Hwnd))}"))})");
        check(decks.All(d => !d.GearShown), "icons on: the control-center gear at the top left is hidden on every deck");
        foreach (var d in decks)
            lines.Add($"     {d.Display.Name}: wallpaper {DescribeWallpaper(d.Display)}");
        check(decks.All(d => WindowsWallpaper.For(d.Display).FromShell), "the Windows wallpaper can be read per monitor (IDesktopWallpaper)");
        if (shotsFolder is not null) Capture(h, shotsFolder, "icons-on");

        foreach (var d in decks) Probe(h, d, regionOn: true, lines, check);

        // The icons themselves are read through the shell; new widgets avoid them.
        var cells = DesktopIcons.Read();
        check(cells is not null, $"the desktop icons can be read through the shell ({(cells is null ? "failed" : cells.Count + " icon cell(s)")})");
        if (cells is { Count: > 0 } && h.Primary is { } main)
        {
            var area = main.Display.WorkArea;
            var occupied = IconSpace.Occupied(cells, area.X, area.Y, area.Width, area.Height, main.Display.Scale);
            var (w, hh) = ItemKinds.DefaultSize(ItemKinds.Clock);
            var spot = DeckLayout.FindSpace(24, 96, w, hh, area.Width / main.Display.Scale, area.Height / main.Display.Scale, occupied, false, DeckLayout.Grid);
            bool clear = spot is { } p && occupied.All(o => !DeckLayout.Overlaps(new TileRect(p.X, p.Y, w, hh), o));
            check(spot is not null && clear, $"a new widget wanted at the top left lands beside the icons ({occupied.Count} icon cell(s) on the main deck{(spot is { } q ? $", spot {q.X:0},{q.Y:0}" : ", no room")})");
            check(main.IconRects().Count == occupied.Count, "the deck's own icon reading agrees with the shell reading");
        }

        // Edit layout: the deck covers the whole monitor again.
        h.SetEditing(true);
        settle(15);
        check(decks.All(d => !d.HasRegion && DeckRegion.Kind(d.Hwnd) == 0), "Edit layout: the region is dropped on every deck");
        foreach (var d in decks) Probe(h, d, regionOn: false, lines, check);
        h.SetEditing(false);
        settle(15);
        check(decks.All(d => d.HasRegion && DeckRegion.Kind(d.Hwnd) > 1), "Done: the region is back on every deck");

        // Reconnect with the windows kept (Explorer did not restart).
        h.Reconnect();
        settle(15);
        check(h.Backgrounds.All(d => d.HasRegion && DeckRegion.Kind(d.Hwnd) > 1), "after Reconnect (windows kept) the region is back on every deck");
        foreach (var d in h.Backgrounds) Probe(h, d, regionOn: true, lines, check);

        // Reconnect when a deck window was destroyed (Explorer restarted): the new deck gets its region once it is attached.
        h.Backgrounds.First().CloseSurface();
        settle(5);
        h.Reconnect();
        settle(30);
        check(h.Backgrounds.Count() == h.Displays.Count && h.Backgrounds.All(d => h.HostFor(d.Display.Id)?.IsAttached == true && d.HasRegion && DeckRegion.Kind(d.Hwnd) > 1), "after Reconnect had to recreate a deck, every deck is attached and has its region");
        foreach (var d in h.Backgrounds) Probe(h, d, regionOn: true, lines, check);
        if (shotsFolder is not null) Capture(h, shotsFolder, "icons-on-after-reconnect");

        // The switch off: the decks cover the monitors again, live.
        h.SetShowDesktopIcons(false);
        settle(15);
        check(h.Backgrounds.All(d => !d.HasRegion && DeckRegion.Kind(d.Hwnd) == 0 && d.GearShown), "icons off: no region, the gear is back");
        foreach (var d in h.Backgrounds) Probe(h, d, regionOn: false, lines, check);
        if (shotsFolder is not null) Capture(h, shotsFolder, "icons-off");
        h.SetShowDesktopIcons(true);
        settle(15);
        check(h.Backgrounds.All(d => d.HasRegion && !d.GearShown), "icons on again: regions and the hidden gear are back, live");
    }

    // ---------- Who is at a point ----------

    private static void Probe(DeckHub h, MainWindow deck, bool regionOn, List<string> lines, Action<bool, string> check)
    {
        var host = h.HostFor(deck.Display.Id);
        if (host is not { IsAttached: true }) { check(false, $"{deck.Display.Name}: the deck is not attached to the desktop"); return; }
        nint container = host.Parent, hwnd = deck.Hwnd;
        var origin = host.ScreenRect();
        var shapes = deck.RegionShapes;
        string tag = $"{deck.Display.Name} [{(regionOn ? "icons on" : "no region")}]";

        // A widget: the middle of the biggest shape. An empty spot: far from every shape.
        var points = new List<(string Name, int X, int Y, bool ExpectDeck)>();
        if (regionOn)
        {
            var widget = shapes.OrderByDescending(s => s.Width * s.Height).FirstOrDefault();
            if (widget.Width > 0) points.Add(("widget", origin.X + widget.X + widget.Width / 2, origin.Y + widget.Y + widget.Height / 2, true));
        }
        foreach (var (x, y) in EmptySpots(origin, regionOn ? shapes : [], 4)) points.Add(("empty spot", x, y, !regionOn));
        if (!points.Any(p => p.Name == "empty spot")) { check(false, $"{tag}: no empty spot to probe"); return; }
        if (regionOn && !points.Any(p => p.Name == "widget")) lines.Add($"     {tag}: no widget on this deck to probe (only empty spots)");

        var wrong = new List<string>();
        foreach (var (name, x, y, expectDeck) in points)
        {
            var client = new NativePoint { X = x, Y = y };
            ScreenToClient(container, ref client);
            nint child = ChildWindowFromPointEx(container, client, 1 /* CWP_SKIPINVISIBLE */), real = RealChildWindowFromPoint(container, client);
            bool childOk = expectDeck ? child == hwnd && real == hwnd : ClassOf(child) == "SHELLDLL_DefView" && ClassOf(real) == "SHELLDLL_DefView";
            string detail = $"{name} ({x},{y}) children-of-desktop: Child={Short(child, hwnd)} Real={Short(real, hwnd)}";
            nint hit = WindowFromPoint(new NativePoint { X = x, Y = y });
            bool covered = GetAncestor(hit, 2) != GetAncestor(container, 2);
            if (covered) detail += $"; WindowFromPoint is covered by another app ({Describe(hit)}), not counted";
            else
            {
                bool hitDeck = hit == hwnd || IsChildWindow(hwnd, hit), hitIcons = ClassOf(hit) is "SysListView32" or "SHELLDLL_DefView";
                bool ok = expectDeck ? hitDeck : hitIcons;
                detail += $"; WindowFromPoint={Describe(hit)} {(ok ? "ok" : "WRONG")}";
                if (!ok) childOk = false;
            }
            if (!childOk) wrong.Add(detail);
            lines.Add("     " + tag + " " + detail);
        }
        check(wrong.Count == 0, $"{tag}: {(regionOn ? "an empty spot is Explorer's icon view and a widget is the deck" : "the whole monitor is the deck")} ({points.Count} point(s)){(wrong.Count > 0 ? ": " + string.Join(" | ", wrong) : "")}");
    }

    /// <summary>Points inside the deck's rectangle that are at least 24 px away from every shape (a coarse grid, spread out).</summary>
    private static List<(int X, int Y)> EmptySpots(PixelRect origin, IReadOnlyList<RegionShape> shapes, int want)
    {
        var found = new List<(int, int)>();
        int stepX = Math.Max(40, origin.Width / 13), stepY = Math.Max(40, origin.Height / 9);
        for (int y = stepY / 2; y < origin.Height; y += stepY)
            for (int x = stepX / 2; x < origin.Width; x += stepX)
            {
                if (shapes.Any(s => x >= s.X - 24 && x < s.Right + 24 && y >= s.Y - 24 && y < s.Bottom + 24)) continue;
                found.Add((origin.X + x, origin.Y + y));
            }
        if (found.Count <= want) return found;
        return Enumerable.Range(0, want).Select(i => found[i * (found.Count - 1) / (want - 1)]).ToList();
    }

    // ---------- Screenshots ----------

    /// <summary>Each monitor's work area as it looks on screen right now (a monitor an app covers shows that app).</summary>
    private static void Capture(DeckHub h, string folder, string name)
    {
        Directory.CreateDirectory(folder);
        foreach (var d in h.Displays)
        {
            using var bitmap = new Bitmap(d.WorkArea.Width, d.WorkArea.Height);
            using (var g = Graphics.FromImage(bitmap)) g.CopyFromScreen(d.WorkArea.X, d.WorkArea.Y, 0, 0, bitmap.Size);
            bitmap.Save(Path.Combine(folder, $"{name}-display{d.Number}{(d.IsPrimary ? "-main" : "")}.png"), ImageFormat.Png);
        }
    }

    // ---------- Helpers ----------

    private static string DescribeWallpaper(DisplayInfo display)
    {
        var look = WindowsWallpaper.For(display);
        return $"{(look.Path.Length > 0 ? Path.GetFileName(look.Path) : "(plain color)")} {look.Position} background {look.Background} fromShell={look.FromShell}";
    }

    private static string Fmt((int Left, int Top, int Right, int Bottom)? b) => b is { } v ? $"({v.Left},{v.Top})-({v.Right},{v.Bottom})" : "none";
    private static string Short(nint window, nint deck) => window == deck ? "DECK" : Describe(window);

    private static string ClassOf(nint hwnd)
    {
        if (hwnd == 0) return "";
        var sb = new StringBuilder(256);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string Describe(nint hwnd) => hwnd == 0 ? "(none)" : $"{ClassOf(hwnd)}[0x{hwnd:X}]";

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern nint ChildWindowFromPointEx(nint parent, NativePoint point, uint flags);
    [DllImport("user32.dll")] private static extern nint RealChildWindowFromPoint(nint parent, NativePoint point);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(nint hwnd, ref NativePoint point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll", EntryPoint = "IsChild")] private static extern bool IsChildWindow(nint parent, nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder text, int count);
}
