using DesktopDeck.Layout;
using DesktopDeck.Models;
using DesktopDeck.Services;

/// <summary>Show desktop icons: the setting (default, file round trip, first run), and the pure parts behind it: the region's shapes, the room the icons take, and the wallpaper maths. No Windows needed.</summary>
public static class DesktopIconsTests
{
    public static void Run(Action<bool, string> check, string folder)
    {
        Setting(check, folder);
        Shapes(check);
        IconRoom(check);
        Wallpaper(check);
    }

    private static void Setting(Action<bool, string> check, string folder)
    {
        var store = new SettingsStore(Path.Combine(folder, "icons"));
        check(!new DeckSettings().ShowDesktopIcons, "Desktop icons: the class default is off, so existing setups behave as before");

        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        File.WriteAllText(store.FilePath, """{ "SchemaVersion": 5, "Items": [], "StartAtLogin": false }""");
        check(!store.Load().ShowDesktopIcons, "Desktop icons: a settings file without the field loads as off");

        var fresh = new DeckSettings();
        fresh.StartWithDesktopIcons();
        check(fresh.ShowDesktopIcons, "Desktop icons: the first-run welcome turns them on");
        store.Save(fresh);
        check(store.Load().ShowDesktopIcons, "Desktop icons: on round-trips");
        fresh.ShowDesktopIcons = false;
        store.Save(fresh);
        check(!store.Load().ShowDesktopIcons, "Desktop icons: off round-trips (a fresh install can switch them off for good)");
        check(!HistoryState.Scope.Contains("ShowDesktopIcons"), "Desktop icons: undo history does not include the switch");

        // The deck's own wallpaper drawing flag is a runtime marker, never saved.
        var style = new BackgroundStyle { Kind = "wallpaper", MatchWindows = true };
        var saved = System.Text.Json.JsonSerializer.Serialize(style);
        check(!saved.Contains("MatchWindows"), "Desktop icons: the match-Windows marker of a background is not saved");
        check(style.Signature != new BackgroundStyle { Kind = "wallpaper" }.Signature, "Desktop icons: the exact Windows wallpaper has its own cache signature");
    }

    private static void Shapes(Action<bool, string> check)
    {
        // 100x50 DIP card at (10, 20) on a 150% monitor, radius 14 DIP: pixels are widened outward and the radius scaled.
        var one = RegionMath.Build([(10, 20, 100, 50, 14)], 1.5, 1.5, 1920, 1080);
        check(one.Count == 1 && one[0] == new RegionShape(15, 30, 150, 75, 21), $"Region: a card becomes whole pixels at the DPI scale ({Describe(one)})");
        var fractional = RegionMath.Build([(10.3, 20.3, 100.4, 50.4, 0)], 1.0, 1.0, 1920, 1080);
        check(fractional[0].X == 10 && fractional[0].Y == 20 && fractional[0].Right == 111 && fractional[0].Bottom == 71, $"Region: fractional edges widen outward, never inside the drawn card ({Describe(fractional)})");
        var clipped = RegionMath.Build([(-20, -20, 60, 60, 8), (1900, 1060, 100, 100, 8), (2000, 10, 50, 50, 0), (5, 5, 0, 10, 0), (5, 5, -3, 10, 0), (double.NaN, 5, 10, 10, 0), (5, 5, 10, double.PositiveInfinity, 0)], 1.0, 1.0, 1920, 1080);
        check(clipped.Count == 2 && clipped[0] == new RegionShape(0, 0, 40, 40, 8) && clipped[1] == new RegionShape(1900, 1060, 20, 20, 8), $"Region: shapes are clipped to the window; empty, off-window and non-finite ones are dropped ({Describe(clipped)})");
        check(RegionMath.Build([], 1, 1, 1920, 1080).Count == 0, "Region: nothing in, nothing out");
        check(RegionMath.Build([(0, 0, 10, 10, double.NaN)], 1, 1, 100, 100)[0].Radius == 0, "Region: a non-finite radius is square");

        var round = new RegionShape(0, 0, 100, 60, 20);
        check(round.Contains(50, 30) && round.Contains(0.5, 30) && !round.Contains(0, 0) && !round.Contains(1, 1) && round.Contains(20, 20) && !round.Contains(100, 30) && !round.Contains(50, 60), "Region: rounded corners cut the corner pixels, edges stay in");
        var pill = new RegionShape(0, 0, 40, 20, 99);
        check(pill.Contains(20, 10) && !pill.Contains(0, 0), "Region: a radius larger than half the height is a pill, not broken");
    }

    private static void IconRoom(Action<bool, string> check)
    {
        // A column of icons at the top left of a 1920x1040 work area (the deck starts at screen 0,0), 76x100 px cells; one icon is on another monitor.
        var icons = Enumerable.Range(0, 8).Select(i => new IconBox(8, 8 + i * 100, 76, 100)).Concat([new IconBox(2500, 40, 76, 100), new IconBox(8, 8, 0, 0)]).ToList();
        var room = IconSpace.Occupied(icons, 0, 0, 1920, 1040, 1.0);
        check(room.Count == 8 && room[0] == new TileRect(8, 8, 76, 100), $"Icon room: icons on the deck become rectangles; other monitors' and empty ones are left out ({room.Count})");

        // 150% monitor whose deck starts at screen (1920, 0): positions are relative to the deck and in DIP.
        var scaled = IconSpace.Occupied([new IconBox(1920 + 30, 60, 150, 120), new IconBox(1900, 0, 40, 40)], 1920, 0, 2880, 1560, 1.5);
        check(scaled.Count == 2 && scaled[0] == new TileRect(20, 40, 100, 80) && scaled[1] == new TileRect(0, 0, 20 / 1.5, 40 / 1.5), $"Icon room: pixels become deck DIP at the monitor's scale, an icon half over the edge is clipped ({string.Join(", ", scaled)})");

        check(IconSpace.Occupied(icons, 0, 0, 0, 0, 1).Count == 0 && IconSpace.Occupied(icons, 0, 0, 100, 100, 0).Count == 0, "Icon room: a deck without a size or scale has no icon room");

        var fallbackMain = IconSpace.Fallback(true, 1920, 1000);
        check(fallbackMain.Count == 1 && fallbackMain[0] == new TileRect(0, 0, 480, 1000), "Icon room: when the icons can't be read the left quarter of the main monitor is kept clear");
        check(IconSpace.Fallback(false, 1920, 1000).Count == 0 && IconSpace.Fallback(true, 0, 0).Count == 0, "Icon room: other monitors keep nothing clear");

        // A new widget wanted on top of the icons lands beside them, and so do starters.
        var (w, h) = (184.0, 84.0);
        var spot = DeckLayout.FindSpace(24, 96, w, h, 1920, 1040, room, false, DeckLayout.Grid);
        check(spot is { } p && room.All(r => !DeckLayout.Overlaps(new TileRect(p.X, p.Y, w, h), r)) && p.X >= 84, $"Icon room: a widget wanted over the icon column is placed beside it ({spot})");
        var slots = Enumerable.Range(0, 6).Select(_ => new TemplateSlot(184, 84, 120, 60)).ToList();
        var placed = TemplatePlacer.Place(slots, 1920, 1040, room, false, DeckLayout.Grid);
        var rects = placed.Where(r => r is not null).Select(r => r!.Value).ToList();
        check(rects.Count == slots.Count && rects.All(r => room.All(i => !DeckLayout.Overlaps(r, i))), $"Icon room: a starter layout keeps clear of the icons ({rects.Count} of {slots.Count} placed)");

        // The whole screen full of icons: placement says there is no room instead of breaking.
        var crowded = Enumerable.Range(0, 26).SelectMany(c => Enumerable.Range(0, 11).Select(r => new IconBox(c * 76, r * 100, 76, 100))).ToList();
        var full = IconSpace.Occupied(crowded, 0, 0, 1920, 1040, 1.0);
        check(DeckLayout.FindSpace(24, 96, w, h, 1920, 1040, full, false, DeckLayout.Grid) is null, "Icon room: a desktop full of icons reports no room instead of breaking placement");
    }

    private static void Wallpaper(Action<bool, string> check)
    {
        // One 1920x1080 monitor at the origin; a 4000x1000 picture unless said otherwise.
        WallpaperPlan P(WallpaperPosition position, double iw = 4000, double ih = 1000, double mx = 0, double vx = 0, double vy = 0, double vw = 1920, double vh = 1080)
            => WallpaperMath.Plan(iw, ih, mx, 0, 1920, 1080, position, vx, vy, vw, vh);
        bool Is(WallpaperPlan p, double x, double y, double w, double h, bool tile = false) => Math.Abs(p.X - x) < 1e-6 && Math.Abs(p.Y - y) < 1e-6 && Math.Abs(p.Width - w) < 1e-6 && Math.Abs(p.Height - h) < 1e-6 && p.Tile == tile;

        var fill = P(WallpaperPosition.Fill);
        check(Is(fill, (1920 - 4320) / 2.0, 0, 4320, 1080), $"Wallpaper: Fill covers the monitor and crops the sides ({fill})");
        var fit = P(WallpaperPosition.Fit);
        check(Is(fit, 0, (1080 - 480) / 2.0, 1920, 480), $"Wallpaper: Fit shows the whole picture with bars ({fit})");
        check(Is(P(WallpaperPosition.Stretch), 0, 0, 1920, 1080), "Wallpaper: Stretch fills the monitor ignoring the picture's shape");
        check(Is(P(WallpaperPosition.Center), (1920 - 4000) / 2.0, (1080 - 1000) / 2.0, 4000, 1000), "Wallpaper: Center keeps the picture's size, cropped by the monitor");
        check(Is(P(WallpaperPosition.Center, 800, 600), 560, 240, 800, 600), "Wallpaper: a small centered picture sits in the middle");
        check(Is(P(WallpaperPosition.Tile, 800, 600), 0, 0, 800, 600, tile: true), "Wallpaper: Tile repeats the picture from the monitor's corner");

        // Two 1920x1080 monitors side by side: the picture spans both; the second one shows its right half.
        var spanLeft = P(WallpaperPosition.Span, 3840, 1080, vw: 3840);
        var spanRight = P(WallpaperPosition.Span, 3840, 1080, mx: 1920, vw: 3840);
        check(Is(spanLeft, 0, 0, 3840, 1080) && Is(spanRight, -1920, 0, 3840, 1080), $"Wallpaper: Span draws one picture across both monitors ({spanLeft} / {spanRight})");
        // The monitor to the left of the main one (negative x), with a square picture.
        var negative = P(WallpaperPosition.Span, 3000, 3000, mx: -1920, vx: -1920, vw: 3840);
        check(Is(negative, 0, (1080 - 3840) / 2.0, 3840, 3840), $"Wallpaper: Span covers the whole screen area even for a monitor on the left ({negative})");
        check(Is(P(WallpaperPosition.Span, vw: 0, vh: 0), (1920 - 4320) / 2.0, 0, 4320, 1080), "Wallpaper: Span without a virtual screen falls back to Fill");

        // Bad numbers never throw: the monitor is simply filled.
        check(Is(P(WallpaperPosition.Fill, 0, 0), 0, 0, 1920, 1080) && Is(P((WallpaperPosition)99), 0, 0, 1920, 1080), "Wallpaper: an empty picture or an unknown position fills the monitor");

        check(WallpaperMath.NeededScale(4000, 1000, fill) == 1 && Math.Abs(WallpaperMath.NeededScale(4000, 2250, P(WallpaperPosition.Fill, 4000, 2250)) - 0.48) < 1e-9, "Wallpaper: a big picture is decoded no larger than the monitor needs");
        check(WallpaperMath.NeededScale(800, 600, P(WallpaperPosition.Tile, 800, 600)) == 1 && WallpaperMath.NeededScale(800, 600, P(WallpaperPosition.Center, 800, 600)) == 1, "Wallpaper: tiles and centered pictures keep their full size");
    }

    private static string Describe(IEnumerable<RegionShape> shapes) => string.Join(", ", shapes.Select(s => $"{s.X},{s.Y} {s.Width}x{s.Height} r{s.Radius}"));
}
