using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using DesktopDeck.Models;

namespace DesktopDeck.Widgets;

/// <summary>
/// Photo slideshow: pictures from a folder, one at a time. The folder is listed off the UI thread at most once a
/// minute and only the current picture is decoded (at display size, frozen). Ordering and sizes live in SlideshowLogic.
/// </summary>
internal sealed class SlideshowWidget : IWidget
{
    public string Kind => "slideshow";
    public string Name => "Photo slideshow";
    public string Description => "Pictures from a folder, one after another";
    public string Section => "Visual";
    public string Glyph => "";
    public string Accent => "#8b5cf6";
    public (double Width, double Height) DefaultSize => (372, 276);
    public (double Width, double Height) MinimumSize => (180, 132);

    public IReadOnlyList<OptionSpec> Options { get; } =
    [
        new OptionSpec("folder", "Folder", OptionType.Folder, Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)) { Group = "What to show", Hint = "Every picture in it is shown in turn." },
        OptionSpec.Toggle("subfolders", "Include subfolders", false, "What to show"),
        OptionSpec.Choice("caption", "Caption", "name", ["name", "date", "both", "none"], ["File name", "Date (file time)", "Name and date", "None"], "What to show"),
        OptionSpec.Toggle("counter", "Show position (3 / 24)", false, "What to show"),
        OptionSpec.Number("interval", "Change every", 15, 1, 999, "Behavior"),
        OptionSpec.Choice("unit", "Interval unit", "seconds", ["seconds", "minutes"], ["Seconds", "Minutes"], "Behavior"),
        OptionSpec.Choice("order", "Order", "name", ["name", "name-desc", "newest", "oldest"], ["Name A–Z", "Name Z–A", "Newest first", "Oldest first"], "Behavior"),
        OptionSpec.Toggle("shuffle", "Shuffle", false, "Behavior", "Random order; every picture once per round."),
        OptionSpec.Toggle("autoplay", "Play automatically", true, "Behavior", "Off = only the arrow buttons change the picture."),
        OptionSpec.Choice("click", "Clicking the picture", "open", ["open", "next", "none"], ["Opens it", "Shows the next one", "Does nothing"], "Behavior"),
        OptionSpec.Choice("fit", "Picture fit", "fill", ["fill", "fit"], ["Fill (crop)", "Fit (whole picture)"], "Layout"),
        OptionSpec.Choice("transition", "Transition", "fade", ["fade", "none"], ["Fade", "None"], "Layout"),
        OptionSpec.Number("fadeMs", "Fade length (ms)", 450, 100, 2000, "Layout"),
        OptionSpec.Choice("controls", "Buttons", "hover", ["hover", "always", "never"], ["On hover", "Always", "Hidden"], "Layout"),
        OptionSpec.Choice("captionPosition", "Caption position", "bottom", ["bottom", "top"], ["Bottom", "Top"], "Layout"),
        OptionSpec.Toggle("rounded", "Rounded corners", true, "Layout", "Uses the corner roundness setting."),
        OptionSpec.Color("background", "Background", "", "Colors", "Shows around pictures in Fit mode. Empty = card color."),
        OptionSpec.Color("captionColor", "Caption color", "#FFFFFF", "Colors"),
        OptionSpec.Toggle("shade", "Shade behind caption", true, "Colors"),
    ];

    public IEnumerable<Action<LauncherItem>> Samples =>
    [
        item => { item.Options["data.sample"] = "0"; item.Options["controls"] = "always"; item.Options["counter"] = "true"; },
        item => { item.Options["data.sample"] = "1"; item.Options["fit"] = "fit"; item.Options["caption"] = "both"; item.Options["captionPosition"] = "top"; item.Options["background"] = "#101418"; item.Width = 276; item.Height = 276; },
        item => { item.Options["data.sample"] = "2"; item.Options["caption"] = "none"; item.Options["rounded"] = "false"; item.Width = 468; item.Height = 228; },
    ];

    private sealed record Listing(string Folder, bool Subfolders, DateTime At, bool Missing, List<SlideshowPicture> Pictures)
    {
        public string OrderKey = "";
        public List<SlideshowPicture> Ordered = [];
    }

    private sealed record Decoded(string Path, double Width, double Height, bool Fill, ImageSource? Image);

    private static string ListKey(LauncherItem item) => "slideshow:list:" + item.Id;
    private static string PicKey(LauncherItem item) => "slideshow:pic:" + item.Id;
    private static string PrevKey(LauncherItem item) => "slideshow:prev:" + item.Id;

    public FrameworkElement Build(WidgetContext ctx)
    {
        var card = WidgetKit.Card(ctx);
        bool rounded = ctx.GetBool("rounded");
        double radius = rounded ? ctx.GetNumber("_radius") : 0;
        card.CornerRadius = new CornerRadius(radius);
        if (ctx.Get("background").Trim().Length > 0) card.Background = ctx.GetColor("background");
        bool fill = ctx.Get("fit") != "fit";

        // ---- Which pictures, which one now ----
        List<SlideshowPicture> pictures;
        bool missing = false, loading = false;
        string folder = ctx.Get("folder").Trim();
        int sample = -1;
        if (ctx.IsSnapshot)
        {
            sample = int.TryParse(ctx.Get("data.sample"), out var s) ? s : 0;
            pictures = SampleNames.Select((n, i) => new SlideshowPicture(n, new DateTime(2026, 6, 14).AddDays(-i * 9))).ToList();
        }
        else
        {
            var listing = WidgetContext.Cache.TryGetValue(ListKey(ctx.Item), out var cached) ? cached as Listing : null;
            bool sub = ctx.GetBool("subfolders");
            if (listing is null || !string.Equals(listing.Folder, folder, StringComparison.OrdinalIgnoreCase) || listing.Subfolders != sub)
            {
                listing = null;
                loading = folder.Length > 0;
                if (loading) RefreshListing(ctx, folder, sub);
            }
            else if (SlideshowLogic.ListingStale(listing.At, DateTime.Now)) RefreshListing(ctx, folder, sub);
            missing = listing?.Missing ?? folder.Length == 0;
            pictures = listing is null ? [] : Ordered(ctx, listing);
        }

        var root = new Grid { ClipToBounds = false };
        card.Child = root;
        if (pictures.Count == 0)
        {
            root.Children.Add(EmptyState(ctx, loading ? "Loading pictures…" : missing ? (folder.Length == 0 ? "Choose a folder of pictures." : "Folder not found.") : "No pictures in this folder.", !loading));
            ctx.Tick(() => false, _ => { if (!loading) RefreshListing(ctx, folder, ctx.GetBool("subfolders")); });
            return card;
        }

        string wanted = ctx.State("path", "");
        int index = pictures.FindIndex(p => string.Equals(p.Path, wanted, StringComparison.OrdinalIgnoreCase));
        if (ctx.IsSnapshot) index = Math.Clamp(sample, 0, pictures.Count - 1);
        else if (index < 0) { index = 0; wanted = pictures[0].Path; ctx.SetState("path", wanted, false); }
        var current = pictures[index];
        if (ctx.State<DateTime?>("last", null) is null) ctx.SetState<DateTime?>("last", DateTime.Now, false);

        // ---- The picture (current, or the previous one while the next decodes) ----
        ImageSource? image;
        ImageSource? previous = null;
        string shownPath = current.Path;
        bool failed = false, fade = false;
        if (sample >= 0) image = SampleScene(sample);
        else
        {
            var decoded = WidgetContext.Cache.TryGetValue(PicKey(ctx.Item), out var d) ? d as Decoded : null;
            bool matches = decoded is not null && string.Equals(decoded.Path, current.Path, StringComparison.OrdinalIgnoreCase) && decoded.Fill == fill
                && Math.Abs(decoded.Width - ctx.Width) < 1 && Math.Abs(decoded.Height - ctx.Height) < 1;
            if (!matches) StartDecode(ctx, current.Path, fill);
            image = decoded?.Image;
            if (decoded is not null) shownPath = decoded.Path;
            failed = matches && decoded!.Image is null;
            if (matches && ctx.State("fade", false))
            {
                ctx.SetState("fade", false, false);
                fade = ctx.Get("transition") == "fade";
                previous = WidgetContext.Cache.TryGetValue(PrevKey(ctx.Item), out var p) ? (p as Decoded)?.Image : null;
            }
            WidgetContext.Cache.TryRemove(PrevKey(ctx.Item), out _);
        }

        double inner = Math.Max(0, radius - 1);
        if (previous is not null) root.Children.Add(PictureLayer(previous, fill, inner));
        if (image is not null)
        {
            var layer = PictureLayer(image, fill, inner);
            if (fade)
            {
                var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ctx.GetNumber("fadeMs"))) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                layer.BeginAnimation(UIElement.OpacityProperty, animation);
            }
            root.Children.Add(layer);
        }
        else if (failed) root.Children.Add(EmptyState(ctx, "Can't show " + Path.GetFileName(current.Path), false));

        // Click the picture: open it or move on.
        string click = ctx.Get("click");
        if (click != "none")
        {
            var hit = new Border { Background = Brushes.Transparent };
            WidgetKit.Clickable(hit, () =>
            {
                if (ctx.IsSnapshot) return;
                if (click == "next") Advance(ctx, 1);
                else ctx.Open(current.Path);
            }, click == "open" ? "Open " + Path.GetFileName(current.Path) : "Next picture");
            root.Children.Add(hit);
        }

        // ---- Caption ----
        bool top = ctx.Get("captionPosition") == "top";
        var shownPicture = pictures.FirstOrDefault(p => string.Equals(p.Path, shownPath, StringComparison.OrdinalIgnoreCase)) ?? current;
        string caption = SlideshowLogic.Caption(ctx.Get("caption"), shownPicture.Path, shownPicture.Modified, CultureInfo.CurrentCulture);
        bool narrow = ctx.Width < 240;
        if (caption.Length > 0 && image is not null)
        {
            var text = WidgetKit.Text(ctx, caption, narrow ? 12 : 13, ctx.GetColor("captionColor", Brushes.White), bold: true);
            Brush? shade = null;
            if (ctx.GetBool("shade"))
            {
                var g = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(175, 0, 0, 0), top ? 270 : 90);
                g.Freeze();
                shade = g;
            }
            root.Children.Add(new Border
            {
                VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom,
                CornerRadius = top ? new CornerRadius(inner, inner, 0, 0) : new CornerRadius(0, 0, inner, inner),
                Background = shade,
                Padding = top ? new Thickness(14, 10, 14, 22) : new Thickness(14, 22, 14, 10),
                Child = text,
                IsHitTestVisible = false,
            });
        }

        // ---- Buttons: ‹ › at the sides, pause and position in the corner away from the caption ----
        string controls = ctx.Get("controls");
        bool playing = !ctx.State("paused", !ctx.GetBool("autoplay"));
        if (controls != "never" || ctx.GetBool("counter"))
        {
            var overlay = new Grid();
            if (controls != "never")
            {
                double size = narrow ? 26 : 30;
                var prev = RoundButton("", size, "Previous picture", () => Advance(ctx, -1));
                prev.HorizontalAlignment = HorizontalAlignment.Left;
                prev.Margin = new Thickness(8, 0, 0, 0);
                var next = RoundButton("", size, "Next picture", () => Advance(ctx, 1));
                next.HorizontalAlignment = HorizontalAlignment.Right;
                next.Margin = new Thickness(0, 0, 8, 0);
                var pause = RoundButton(playing ? "" : "", size, playing ? "Pause" : "Play", () =>
                {
                    bool paused = ctx.State("paused", !ctx.GetBool("autoplay"));
                    ctx.SetState("last", (DateTime?)DateTime.Now, false);
                    ctx.SetState("paused", !paused);
                });
                pause.HorizontalAlignment = HorizontalAlignment.Right;
                pause.VerticalAlignment = top ? VerticalAlignment.Bottom : VerticalAlignment.Top;
                pause.Margin = new Thickness(0, 8, 8, 8);
                overlay.Children.Add(prev);
                overlay.Children.Add(next);
                overlay.Children.Add(pause);
            }
            if (ctx.GetBool("counter"))
            {
                var label = WidgetKit.Text(ctx, SlideshowLogic.Counter(index, pictures.Count), 11, Brushes.White, bold: true);
                var pill = new Border
                {
                    Background = Glass, CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 2, 9, 3), Child = label,
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = top ? VerticalAlignment.Bottom : VerticalAlignment.Top,
                    Margin = new Thickness(10), IsHitTestVisible = false,
                };
                overlay.Children.Add(pill);
            }
            if (controls == "hover")
            {
                overlay.Opacity = 0;
                root.MouseEnter += (_, _) => overlay.Opacity = 1;
                root.MouseLeave += (_, _) => overlay.Opacity = 0;
            }
            root.Children.Add(overlay);
        }
        root.ToolTip = sample >= 0 ? null : current.Path;

        // ---- Timing: every second for short intervals, else every minute ----
        double interval = SlideshowLogic.IntervalSeconds(ctx.GetNumber("interval"), ctx.Get("unit"));
        ctx.Tick(() => playing && interval < 120, now =>
        {
            if (WidgetContext.Cache.TryGetValue(ListKey(ctx.Item), out var l) && l is Listing listing && SlideshowLogic.ListingStale(listing.At, DateTime.Now))
                RefreshListing(ctx, listing.Folder, listing.Subfolders);
            if (!playing) return;
            var last = ctx.State<DateTime?>("last", null) ?? DateTime.Now;
            if ((DateTime.Now - last).TotalSeconds >= interval - 0.5) Advance(ctx, 1);
        });
        return card;
    }

    public IEnumerable<WidgetAction> Actions(WidgetContext ctx) =>
    [
        new("Next picture", "", () => Advance(ctx, 1)),
        new("Previous picture", "", () => Advance(ctx, -1)),
        new("Open this picture", "", () => { if (Current(ctx) is { } path) ctx.Open(path); }),
        new("Reload the folder", "", () => { WidgetContext.Cache.TryRemove(ListKey(ctx.Item), out _); ctx.Rebuild(); }),
    ];

    // ---------- Moving between pictures ----------

    private static string? Current(WidgetContext ctx)
    {
        string path = ctx.State("path", "");
        return path.Length > 0 ? path : null;
    }

    private static void Advance(WidgetContext ctx, int delta)
    {
        if (ctx.IsSnapshot) return;
        if (!WidgetContext.Cache.TryGetValue(ListKey(ctx.Item), out var l) || l is not Listing listing) return;
        var pictures = Ordered(ctx, listing);
        if (pictures.Count == 0) return;
        string wanted = ctx.State("path", "");
        int index = pictures.FindIndex(p => string.Equals(p.Path, wanted, StringComparison.OrdinalIgnoreCase));
        int next = SlideshowLogic.Step(Math.Max(0, index), index < 0 ? 0 : delta, pictures.Count);
        ctx.SetState("last", (DateTime?)DateTime.Now, false);
        ctx.SetState("path", pictures[next].Path);
    }

    private static List<SlideshowPicture> Ordered(WidgetContext ctx, Listing listing)
    {
        int seed = ctx.State("seed", 0);
        if (seed == 0) { seed = Environment.TickCount | 1; ctx.SetState("seed", seed, false); }
        bool shuffle = ctx.GetBool("shuffle");
        string key = ctx.Get("order") + "|" + shuffle + "|" + seed;
        lock (listing)
        {
            if (listing.OrderKey != key)
            {
                listing.Ordered = SlideshowLogic.Order(listing.Pictures, ctx.Get("order"), shuffle, seed);
                listing.OrderKey = key;
            }
            return listing.Ordered;
        }
    }

    // ---------- Background work ----------

    private static void RefreshListing(WidgetContext ctx, string folder, bool subfolders)
    {
        if (folder.Length == 0) return;
        var item = ctx.Item;
        if (WidgetContext.Cache.TryGetValue(ListKey(item), out var value) && value is Listing current && current.Folder == folder && current.Subfolders == subfolders && !SlideshowLogic.ListingStale(current.At, DateTime.Now)) return;
        ctx.RunOnce("slideshow:list:" + item.Id, async () =>
        {
            var (pictures, missing) = await Task.Run(() => List(folder, subfolders));
            var old = WidgetContext.Cache.TryGetValue(ListKey(item), out var o) ? o as Listing : null;
            var listing = new Listing(folder, subfolders, DateTime.Now, missing, pictures);
            WidgetContext.Cache[ListKey(item)] = listing;
            bool same = old is not null && old.Folder == folder && old.Subfolders == subfolders && old.Missing == missing && old.Pictures.SequenceEqual(pictures);
            if (same) { listing.Ordered = old!.Ordered; listing.OrderKey = old.OrderKey; }
            else ctx.Rebuild();
        });
    }

    private static (List<SlideshowPicture> Pictures, bool Missing) List(string folder, bool subfolders)
    {
        var found = new List<SlideshowPicture>();
        try
        {
            var dir = new DirectoryInfo(Environment.ExpandEnvironmentVariables(folder));
            if (!dir.Exists) return (found, true);
            var options = new EnumerationOptions { RecurseSubdirectories = subfolders, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
            foreach (var file in dir.EnumerateFiles("*", options))
            {
                if (!SlideshowLogic.IsPicture(file.Name)) continue;
                found.Add(new SlideshowPicture(file.FullName, file.LastWriteTime));
                if (found.Count >= 20000) break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException) { }
        return (found, false);
    }

    private static void StartDecode(WidgetContext ctx, string path, bool fill)
    {
        var item = ctx.Item;
        double width = ctx.Width, height = ctx.Height;
        ctx.RunOnce("slideshow:decode:" + item.Id, async () =>
        {
            var image = await Task.Run(() => Decode(path, width, height, fill));
            // Only keep it if it is still the picture wanted (quick clicks may have moved on); either way redraw,
            // which starts the next decode if needed.
            if (string.Equals(ctx.State("path", ""), path, StringComparison.OrdinalIgnoreCase))
            {
                if (WidgetContext.Cache.TryGetValue(PicKey(item), out var old) && old is Decoded { Image: not null } oldPicture && oldPicture.Path != path)
                    WidgetContext.Cache[PrevKey(item)] = oldPicture;
                WidgetContext.Cache[PicKey(item)] = new Decoded(path, width, height, fill, image);
                ctx.SetState("fade", true, false);
            }
            ctx.Rebuild();
        });
    }

    /// <summary>Decodes one picture at display size (off the UI thread), turned upright, frozen.</summary>
    private static ImageSource? Decode(string path, double boxWidth, double boxHeight, bool fill)
    {
        try
        {
            int rawWidth, rawHeight, angle = 0;
            using (var stream = File.OpenRead(path))
            {
                var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None).Frames[0];
                rawWidth = frame.PixelWidth;
                rawHeight = frame.PixelHeight;
                try
                {
                    if (frame.Metadata is BitmapMetadata metadata && metadata.GetQuery("System.Photo.Orientation") is ushort orientation)
                        angle = SlideshowLogic.OrientationAngle(orientation);
                }
                catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException or COMException) { }
            }
            bool turned = angle is 90 or 270;
            int shownWidth = turned ? rawHeight : rawWidth, shownHeight = turned ? rawWidth : rawHeight;
            int decodeShown = SlideshowLogic.DecodeWidth(shownWidth, shownHeight, boxWidth, boxHeight, fill);
            double scale = shownWidth > 0 ? (double)decodeShown / shownWidth : 1;
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (scale < 0.999) image.DecodePixelWidth = Math.Max(1, (int)Math.Round(rawWidth * scale));
            image.EndInit();
            image.Freeze();
            if (angle == 0) return image;
            var upright = new TransformedBitmap(image, new RotateTransform(angle));
            upright.Freeze();
            return upright;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or FileFormatException or COMException or OverflowException) { return null; }
    }

    // ---------- Drawing ----------

    private static readonly Brush Glass = Theme.Brush("#73000000");

    private static Border PictureLayer(ImageSource image, bool fill, double radius)
    {
        var brush = new ImageBrush(image) { Stretch = fill ? Stretch.UniformToFill : Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
        brush.Freeze();
        return new Border { CornerRadius = new CornerRadius(radius), Background = brush };
    }

    private static Border RoundButton(string glyph, double size, string tooltip, Action click)
    {
        var button = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = Glass,
            BorderBrush = Theme.Brush("#33FFFFFF"), BorderThickness = new Thickness(1),
            Child = WidgetKit.Icon(glyph, size * 0.4, Brushes.White), VerticalAlignment = VerticalAlignment.Center,
        };
        button.MouseEnter += (_, _) => button.Background = Theme.Brush("#A6000000");
        button.MouseLeave += (_, _) => button.Background = Glass;
        return WidgetKit.Clickable(button, click, tooltip);
    }

    private static FrameworkElement EmptyState(WidgetContext ctx, string text, bool offerFolder)
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(12) };
        var icon = WidgetKit.Icon("", ctx.Height < 160 ? 22 : 30, Theme.Muted);
        stack.Children.Add(icon);
        var note = WidgetKit.Muted(ctx, text, 12);
        note.TextWrapping = TextWrapping.Wrap;
        note.TextAlignment = TextAlignment.Center;
        note.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(note);
        if (offerFolder && ctx.Height >= 150)
        {
            var button = WidgetKit.Button("Choose folder…", ctx.Customize);
            button.Margin = new Thickness(0, 10, 0, 0);
            button.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(button);
        }
        return stack;
    }

    private static readonly string[] SampleNames =
    [
        "Lake at dusk.jpg", "Pine ridge.jpg", "Harbor morning.jpg", "Dunes.jpg", "Old town.jpg", "Glacier.jpg", "Garden.jpg", "Night market.jpg",
        "Waterfall.jpg", "Lighthouse.jpg", "Canyon.jpg", "Bridge.jpg",
    ];

    /// <summary>A drawn landscape for smoke renders (no files are read).</summary>
    private static ImageSource SampleScene(int which)
    {
        var (skyTop, skyBottom, sun, far, near) = (which % 3) switch
        {
            1 => ("#0f3b2e", "#9fd3a8", "#fff3c4", "#2f6b4f", "#173d2c"),
            2 => ("#0b2a4a", "#7cc4e8", "#ffffff", "#2d5f86", "#123651"),
            _ => ("#2b1840", "#f2996e", "#ffd9a0", "#6b3a5a", "#2a1a2e"),
        };
        Brush B(string hex) => Theme.Brush(hex);
        var group = new DrawingGroup();
        var sky = new LinearGradientBrush(((SolidColorBrush)B(skyTop)).Color, ((SolidColorBrush)B(skyBottom)).Color, 90);
        sky.Freeze();
        group.Children.Add(new GeometryDrawing(sky, null, new RectangleGeometry(new Rect(0, 0, 600, 400))));
        group.Children.Add(new GeometryDrawing(B(sun), null, new EllipseGeometry(which % 3 == 2 ? new Point(150, 120) : new Point(410, 190), 46, 46)));
        group.Children.Add(new GeometryDrawing(B(far), null, Geometry.Parse("M0,260 L90,200 L170,240 L260,170 L360,235 L450,190 L600,250 L600,400 L0,400 Z")));
        group.Children.Add(new GeometryDrawing(B(near), null, Geometry.Parse("M0,310 C120,270 220,330 330,300 C430,275 520,300 600,285 L600,400 L0,400 Z")));
        var water = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255));
        water.Freeze();
        group.Children.Add(new GeometryDrawing(water, null, Geometry.Parse("M0,330 C150,320 300,340 600,325 L600,333 C300,348 150,328 0,338 Z")));
        group.Freeze();
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }
}
