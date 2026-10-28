using System.Globalization;
using DesktopDeck.Widgets;

/// <summary>Tests for the pure logic of the Visual widgets: slideshow, text banner, volume, color palette, recent files, sticker.</summary>
public static class VisualTests
{
    // ---------- Volume ----------

    public static void VolumeStepsAndPositions()
    {
        Expect.Near(.5, VolumeLogic.Step(.47, 5, 1), 1e-9, "step up");
        Expect.Near(.45, VolumeLogic.Step(.47, 5, -1), 1e-9, "step down");
        Expect.Near(1, VolumeLogic.Step(.99, 5, 1), 1e-9, "clamp upper");
        Expect.Near(0, VolumeLogic.Step(.02, 5, -1), 1e-9, "clamp lower");
        Expect.Near(.6, VolumeLogic.Step(.55, 5, 1, .6), 1e-9, "volume limit");
        Expect.Near(.75, VolumeLogic.FromPosition(25, 100, true), 1e-9, "vertical upward fill");
        Expect.Near(.4, VolumeLogic.FromPosition(40, 100, false), 1e-9, "horizontal");
        Expect.Near(.5, VolumeLogic.FromPosition(90, 100, false, .5), 1e-9, "limit applies to clicks");
        Expect.Near(0, VolumeLogic.FromPosition(5, 0, false), 1e-9, "zero width");
        Expect.Near(0, VolumeLogic.FromPosition(double.NaN, 100, false), 1e-9, "not a number");
    }

    public static void VolumeGlyphsAndText()
    {
        Expect.Equal(VolumeLogic.MutedGlyph, VolumeLogic.Glyph(.8, true), "muted glyph wins");
        Expect.Equal(VolumeLogic.SpeakerGlyph, VolumeLogic.Glyph(.2, false, false), "fixed speaker");
        Expect.True(VolumeLogic.Glyph(0, false) != VolumeLogic.Glyph(.2, false), "silent differs from low");
        Expect.True(VolumeLogic.Glyph(.2, false) != VolumeLogic.Glyph(.5, false), "low differs from medium");
        Expect.True(VolumeLogic.Glyph(.5, false) != VolumeLogic.Glyph(.9, false), "medium differs from high");
        Expect.Equal("47%", VolumeLogic.Percent(.47), "percent text");
        Expect.Equal("100%", VolumeLogic.Percent(1.5), "percent clamped");
        Expect.Near(.25, VolumeLogic.FromAngle(1, 0), 1e-9, "ring: 3 o'clock is a quarter");
        Expect.Near(.5, VolumeLogic.FromAngle(0, 1), 1e-9, "ring: 6 o'clock is half");
    }

    // ---------- Color palette ----------

    public static void PaletteParsing()
    {
        var c = ColorPaletteLogic.Parse("#abc | Mist")!;
        Expect.Equal("#AABBCC", c.Hex, "short hex expanded");
        Expect.Equal(170, c.R, "red channel");
        Expect.Equal("Mist", c.Name, "name retained");
        Expect.True(ColorPaletteLogic.Parse("#ZZZZZZ") is null, "bad hex skipped");
        Expect.True(ColorPaletteLogic.Parse("  ") is null, "blank line skipped");
        Expect.Equal("#D8AF68", ColorPaletteLogic.Parse("d8af68")!.Hex, "# is optional");
        Expect.Equal("", ColorPaletteLogic.Parse("#112233")!.Name, "name is optional");
        Expect.Equal(2, ColorPaletteLogic.ParseAll("#111111 | a\nnope\n#222222", 10).Count, "bad lines skipped in a list");
        Expect.Equal(1, ColorPaletteLogic.ParseAll("#111111\n#222222", 1).Count, "list limit");
    }

    public static void PalettePresetsAndFormats()
    {
        foreach (var name in ColorPaletteLogic.PresetNames)
        {
            var swatches = ColorPaletteLogic.ParseAll(ColorPaletteLogic.Preset(name));
            Expect.True(swatches.Count >= 6, name + " has at least six colors");
            Expect.Equal(swatches.Count, ColorPaletteLogic.Preset(name).Split('\n').Length, name + ": every preset line parses");
        }
        var gold = ColorPaletteLogic.Parse("#D8AF68 | Gold")!;
        Expect.Equal("#D8AF68", ColorPaletteLogic.Format(gold, "hex"), "hex");
        Expect.Equal("#d8af68", ColorPaletteLogic.Format(gold, "hex", false), "lowercase hex");
        Expect.Equal("D8AF68", ColorPaletteLogic.Format(gold, "hex-bare"), "hex without #");
        Expect.Equal("rgb(216, 175, 104)", ColorPaletteLogic.Format(gold, "rgb"), "rgb()");
        Expect.Equal("hsl(38, 59%, 63%)", ColorPaletteLogic.Format(gold, "hsl"), "hsl()");
        Expect.Equal((0, 0, 0), ColorPaletteLogic.Hsl(0, 0, 0), "black");
        Expect.Equal((0, 100, 50), ColorPaletteLogic.Hsl(255, 0, 0), "red");
        Expect.Equal((240, 100, 50), ColorPaletteLogic.Hsl(0, 0, 255), "blue");
        Expect.True(ColorPaletteLogic.IsLight(ColorPaletteLogic.Parse("#F5F5F5")!) && !ColorPaletteLogic.IsLight(ColorPaletteLogic.Parse("#222222")!), "light and dark");
        Expect.Equal("Gold: #D8AF68", ColorPaletteLogic.AllAsText([gold], "hex"), "copy all");
    }

    // ---------- Recent files ----------

    public static void RecentFiltersAndDuplicates()
    {
        var files = new[]
        {
            new RecentFilesLogic.Entry("C:/a.PDF", new DateTime(2026, 10, 1), false),
            new RecentFilesLogic.Entry("C:/a.PDF", new DateTime(2026, 10, 4), false),
            new RecentFilesLogic.Entry("C:/b.jpg", new DateTime(2026, 10, 3), false),
            new RecentFilesLogic.Entry("C:/folder", new DateTime(2026, 10, 4), true),
        };
        var docs = RecentFilesLogic.Filter(files, "documents", [], true, 10);
        Expect.Equal(1, docs.Count, "documents filtered and deduplicated");
        Expect.Equal(new DateTime(2026, 10, 4), docs[0].Modified, "latest shortcut retained");
        Expect.Equal(1, RecentFilesLogic.Filter(files, "all", ["jpg"], false, 10).Count, "custom extension");
        Expect.Equal(1, RecentFilesLogic.Filter(files, "documents", ["*.JPG"], false, 10).Count, "extensions replace the type and ignore * and case");
        Expect.Equal(1, RecentFilesLogic.Filter(files, "images", [], true, 10).Count, "images only");
        Expect.Equal(3, RecentFilesLogic.Filter(files, "all", [], false, 10).Count, "all with folders");
        Expect.Equal(2, RecentFilesLogic.Filter(files, "all", [], true, 10).Count, "all without folders");
        Expect.Equal("C:/b.jpg", RecentFilesLogic.Filter(files, "all", [], false, 10)[2].Path, "oldest last");
        Expect.Equal(1, RecentFilesLogic.Filter(files, "all", [], false, 1).Count, "count limit");
    }

    public static void RecentWording()
    {
        var now = new DateTime(2026, 10, 5, 15, 0, 0);
        Expect.Equal("just now", RecentFilesLogic.Ago(now.AddSeconds(-20), now, CultureInfo.InvariantCulture), "seconds");
        Expect.Equal("12 min ago", RecentFilesLogic.Ago(now.AddMinutes(-12), now, CultureInfo.InvariantCulture), "minutes");
        Expect.Equal("3 h ago", RecentFilesLogic.Ago(now.AddHours(-3), now, CultureInfo.InvariantCulture), "hours");
        Expect.Equal("Yesterday", RecentFilesLogic.Ago(now.AddHours(-20), now, CultureInfo.InvariantCulture), "yesterday evening");
        Expect.Equal("3 days ago", RecentFilesLogic.Ago(now.AddDays(-3), now, CultureInfo.InvariantCulture), "days");
        Expect.Equal("1 Aug", RecentFilesLogic.Ago(new DateTime(2026, 8, 1), now, CultureInfo.InvariantCulture), "older, same year");
        Expect.Equal("Documents", RecentFilesLogic.ParentName(@"C:\Users\me\Documents\a.pdf"), "parent folder name");
        Expect.Equal(@"C:\", RecentFilesLogic.ParentName(@"C:\a.pdf"), "drive root");
        Expect.Equal("image", RecentFilesLogic.Category(new RecentFilesLogic.Entry("x.PNG", now, false)), "image category");
        Expect.Equal("folder", RecentFilesLogic.Category(new RecentFilesLogic.Entry("x", now, true)), "folder category");
        var entry = new RecentFilesLogic.Entry(@"C:\Users\me\Documents\a.pdf", now.AddMinutes(-5), false);
        Expect.Equal("Documents  ·  5 min ago", RecentFilesLogic.Subtitle(entry, "both", now, CultureInfo.InvariantCulture), "folder and age");
        Expect.Equal("", RecentFilesLogic.Subtitle(entry, "none", now), "no subtitle");
        Expect.True(RecentFilesLogic.IsNetworkPath(@"\\server\share\a.txt") && !RecentFilesLogic.IsNetworkPath(@"C:\a.txt") && !RecentFilesLogic.IsNetworkPath(@"\\?\C:\a.txt"), "network paths");
    }

    // ---------- Sticker ----------

    public static void StickerGlyphsAndSizes()
    {
        var glyphs = StickerLogic.Glyphs;
        Expect.True(glyphs.Length >= 30, "about thirty glyphs");
        Expect.Equal(glyphs.Length, glyphs.Select(g => g.Name).Distinct().Count(), "names are unique");
        Expect.Equal(glyphs.Length, glyphs.Select(g => g.Glyph).Distinct().Count(), "glyphs are unique");
        Expect.True(glyphs.All(g => g.Glyph.Length == 1 && g.Glyph[0] >= '\uE000'), "every glyph is one private-use character");
        Expect.Equal(StickerLogic.Glyphs[0].Glyph, StickerLogic.GlyphFor("nonsense"), "unknown name gives the first glyph");
        Expect.Equal("\uE719", StickerLogic.GlyphFor("Gift"), "named glyph");
        Expect.Near(76, StickerLogic.SymbolSize(76, 276, 276, 16, 0, false), 1e-9, "fits as asked");
        Expect.True(StickerLogic.SymbolSize(240, 180, 180, 16, 30, true) < 100, "big symbols shrink to fit the card");
        Expect.True(StickerLogic.SymbolSize(76, 20, 20, 16, 30, true) >= 16, "never below 16");
    }

    public static void StickerTargetsAndEmoji()
    {
        Expect.Equal("https://example.com", StickerLogic.NormalizeTarget(" example.com "), "bare domain gets https");
        Expect.Equal("https://example.com/a?b=1", StickerLogic.NormalizeTarget("https://example.com/a?b=1"), "full address kept");
        Expect.Equal(@"C:\Users\me\a.pdf", StickerLogic.NormalizeTarget("\"C:\\Users\\me\\a.pdf\""), "path unquoted");
        Expect.Equal("ms-settings:sound", StickerLogic.NormalizeTarget("ms-settings:sound"), "settings uri kept");
        Expect.Equal("%USERPROFILE%\\Desktop", StickerLogic.NormalizeTarget("%USERPROFILE%\\Desktop"), "environment variable kept");
        Expect.Equal("", StickerLogic.NormalizeTarget("  "), "empty stays empty");
        Expect.True(StickerLogic.LooksLikeEmoji("🌿") && StickerLogic.LooksLikeEmoji("❤️") && !StickerLogic.LooksLikeEmoji("Hello"), "emoji detection");
    }

    // ---------- Slideshow ----------

    public static void SlideshowOrderAndWrap()
    {
        var pictures = new[] { new SlideshowPicture("IMG_10.jpg", DateTime.MinValue), new SlideshowPicture("IMG_2.jpg", DateTime.MinValue) };
        Expect.Equal("IMG_2.jpg", SlideshowLogic.Order(pictures, "name", false, 0)[0].Path, "natural order");
        Expect.Equal("IMG_10.jpg", SlideshowLogic.Order(pictures, "name-desc", false, 0)[0].Path, "descending");
        Expect.Equal(2, SlideshowLogic.Step(0, -1, 3), "previous wraps");
        Expect.Equal(0, SlideshowLogic.Step(2, 1, 3), "next wraps");
        Expect.Equal(120d, SlideshowLogic.IntervalSeconds(2, "minutes"), "minutes conversion");
        Expect.Equal(2d, SlideshowLogic.IntervalSeconds(0, "seconds"), "interval floor");
    }

    public static void SlideshowShuffleIsStableAndComplete()
    {
        var pictures = Enumerable.Range(0, 20).Select(i => new SlideshowPicture($"p{i}.jpg", DateTime.MinValue.AddDays(i))).ToList();
        var a = SlideshowLogic.Order(pictures, "name", true, 7);
        var b = SlideshowLogic.Order(pictures.AsEnumerable().Reverse(), "name", true, 7);
        Expect.True(a.Select(p => p.Path).SequenceEqual(b.Select(p => p.Path)), "same seed gives the same order whatever the disk order");
        Expect.Equal(20, a.Select(p => p.Path).Distinct().Count(), "every picture appears once");
        Expect.True(!a.Select(p => p.Path).SequenceEqual(SlideshowLogic.Order(pictures, "name", false, 0).Select(p => p.Path)), "shuffle changes the order");
        Expect.Equal("p19.jpg", SlideshowLogic.Order(pictures, "newest", false, 0)[0].Path, "newest first");
    }

    public static void SlideshowCaptionsAndOrientation()
    {
        var date = new DateTime(2026, 6, 14);
        Expect.Equal("Lake at dusk", SlideshowLogic.Caption("name", @"C:\p\Lake at dusk.jpg", date, CultureInfo.InvariantCulture), "name without extension");
        Expect.Equal("14 Jun 2026", SlideshowLogic.Caption("date", "x.jpg", date, CultureInfo.InvariantCulture), "date");
        Expect.Equal("", SlideshowLogic.Caption("none", "x.jpg", date, CultureInfo.InvariantCulture), "none");
        Expect.Equal("3 / 24", SlideshowLogic.Counter(2, 24), "counter");
        Expect.Equal(90, SlideshowLogic.OrientationAngle(6), "EXIF 6 is a quarter turn");
        Expect.Equal(0, SlideshowLogic.OrientationAngle(1), "EXIF 1 is upright");
        Expect.True(SlideshowLogic.IsPicture("a.JPG") && !SlideshowLogic.IsPicture("a.txt"), "picture extensions");
    }

    public static void DecodeSizingAndStaleness()
    {
        Expect.Equal(800, SlideshowLogic.DecodeWidth(1600, 800, 200, 200, true), "fill covers short side at 2x");
        Expect.Equal(400, SlideshowLogic.DecodeWidth(1600, 800, 200, 200, false), "fit preserves long side");
        Expect.Equal(300, SlideshowLogic.DecodeWidth(300, 200, 800, 800, true), "never larger than the picture");
        var now = DateTime.Now;
        Expect.True(!SlideshowLogic.ListingStale(now.AddSeconds(-59), now), "enumeration throttled");
        Expect.True(SlideshowLogic.ListingStale(now.AddSeconds(-60), now), "minute refresh");
        Expect.True(SlideshowLogic.ListingStale(null, now), "never listed is stale");
    }

    // ---------- Text banner ----------

    public static void BannerTokensAndCase()
    {
        var now = new DateTime(2026, 10, 4, 14, 25, 0);
        Expect.Equal("SUNDAY 14:25", TextBannerLogic.Render("{weekday} {time}", "upper", now, CultureInfo.InvariantCulture, true), "tokens then case");
        Expect.Equal("A\nB", TextBannerLogic.Render("A\r\nB", "none", now, CultureInfo.InvariantCulture), "newlines normalized");
        Expect.Equal("Good afternoon", TextBannerLogic.Greeting(now), "greeting");
        Expect.Equal("Hello World", TextBannerLogic.ApplyCase("hELLO wORLD", "title", CultureInfo.InvariantCulture), "title case");
        Expect.Equal("2:25 PM", TextBannerLogic.ReplaceTokens("{TIME}", now, CultureInfo.InvariantCulture), "tokens ignore letter case, 12-hour clock");
        Expect.Equal("{unknown}", TextBannerLogic.ReplaceTokens("{unknown}", now, CultureInfo.InvariantCulture), "unknown tokens stay");
        Expect.True(TextBannerLogic.HasTokens("It is {time}") && !TextBannerLogic.HasTokens("plain"), "plain text needs no ticker");
    }
}
