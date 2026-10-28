using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using DesktopDeck.Layout;
using DesktopDeck.Models;
using DesktopDeck.Services;

namespace DesktopDeck;

/// <summary>How one widget's card looks once its own settings and the deck-wide defaults are combined.</summary>
internal readonly record struct CardLook(Brush Fill, Brush Hover, Brush Border, double BorderThickness, double Radius, double Opacity, bool CustomColor)
{
    internal CornerRadius Corners => new(Radius);
}

/// <summary>
/// The deck's look: the card style every widget shares (a widget's own "_cardColor", "_opacity", "_radius",
/// "_borderColor" and "_textColor" options win over the deck-wide defaults in <see cref="AppearanceSettings"/>,
/// which win over the theme, unless the deck look is set to apply to every widget), and the deck background per display.
/// Every value comes from <see cref="Effective"/>, the one place that decides it (see <see cref="LookResolver"/>).
/// </summary>
internal static partial class Appearance
{
    private static readonly System.Globalization.CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static Color GlassColor => Theme.GlassColor;

    /// <summary>The theme changed: cached background pictures were drawn on the old base color.</summary>
    internal static void ThemeChanged() { BrushCache.Clear(); BackdropCache.Clear(); }

    // ---------- Reading options ----------

    /// <summary>A number the item saved under <paramref name="key"/>, or null when it has none.</summary>
    internal static double? OptionNumber(LauncherItem item, string key)
        => item.Options.TryGetValue(key, out var text) && double.TryParse(text.Replace(',', '.'), NumberStyles.Float, Invariant, out var value) && double.IsFinite(value) ? value : null;

    /// <summary>A valid color the item saved under <paramref name="key"/>, or null when it has none.</summary>
    internal static Color? OptionColor(LauncherItem item, string key)
        => item.Options.TryGetValue(key, out var text) ? ParseColor(text) : null;

    internal static Color? ParseColor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { return (Color)ColorConverter.ConvertFromString(text.Trim()); }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException) { return null; }
    }

    internal static SolidColorBrush Solid(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Color WithAlpha(Color color, double factor) => Color.FromArgb((byte)Math.Round(Math.Clamp(color.A * factor, 0, 255)), color.R, color.G, color.B);

    // ---------- Cards ----------

    /// <summary>Widgets being drawn without their own card (the selected tab of a group): they take the deck's text look but no card.</summary>
    private static readonly HashSet<string> Flat = new(StringComparer.Ordinal);

    /// <summary>Runs <paramref name="draw"/> with the widget's card switched off (its own look is untouched).</summary>
    internal static T WithoutCard<T>(LauncherItem item, Func<T> draw)
    {
        bool added = Flat.Add(item.Id);
        try { return draw(); }
        finally { if (added) Flat.Remove(item.Id); }
    }

    /// <summary>True when the deck-wide look (Appearance → one look for every widget) decides this widget's appearance.</summary>
    internal static bool UsesDeckLook(AppearanceSettings? defaults, LauncherItem item) => LookResolver.UsesDeckLook(defaults, item);

    /// <summary>
    /// The widget's look: card style, colors, opacity, corners, text size and spacing. With "one look for every widget" on, the deck-wide values
    /// win (nothing saved on the widget is changed); otherwise the widget's own values win over the deck defaults.
    /// </summary>
    internal static EffectiveLook Effective(AppearanceSettings? defaults, LauncherItem item)
    {
        var look = LookResolver.Effective(defaults, item, text => ParseColor(text) is not null);
        return Flat.Contains(item.Id) ? look with { CardStyle = "none" } : look;
    }

    internal static double Radius(AppearanceSettings? defaults, LauncherItem item) => Effective(defaults, item).Radius;

    internal static double Opacity(AppearanceSettings? defaults, LauncherItem item) => Effective(defaults, item).Opacity / 100.0;

    /// <summary>The deck's value for a shared "_" number option (radius, opacity, text size, spacing) when the deck look decides this widget, else null.</summary>
    internal static double? DeckNumber(AppearanceSettings? defaults, LauncherItem item, string key)
    {
        if (key is not ("_radius" or "_opacity" or "_textScale" or "_padding") || !LookResolver.UsesDeckLook(defaults, item)) return null;
        var look = Effective(defaults, item);
        return key switch { "_radius" => look.Radius, "_opacity" => look.Opacity, "_textScale" => look.TextScale, _ => look.Padding };
    }

    /// <summary>The inner spacing of a framed widget.</summary>
    internal static double Padding(AppearanceSettings? defaults, LauncherItem item) => Effective(defaults, item).Padding;

    /// <summary>The text color the widget should use: its own, else the deck default, else null (the theme's).</summary>
    internal static Color? TextColor(AppearanceSettings? defaults, LauncherItem item)
    {
        var look = Effective(defaults, item);
        if (ParseColor(look.TextColor) is { } chosen) return chosen;
        // A card color the user picked that hides the theme's text (a dark card on the Light theme, a pale one on a dark theme) gets readable text.
        if (look.CardStyle is "solid" && ParseColor(look.CardColor) is { A: > 200 } card)
        {
            string fill = HexColor.Format(255, card.R, card.G, card.B);
            if (HexColor.Contrast(Theme.Palette.Text, fill) >= 3.0) return null;
            string light = Theme.IsDark ? Theme.Palette.Text : "#FFF6EC", dark = Theme.IsDark ? "#1A1611" : Theme.Palette.Text;
            return ParseColor(HexColor.Contrast(light, fill) >= HexColor.Contrast(dark, fill) ? light : dark);
        }
        return null;
    }

    /// <summary>The small uppercase widget heading's color: muted, or the custom text color softened.</summary>
    internal static Brush TitleBrush(AppearanceSettings? defaults, LauncherItem item)
        => TextColor(defaults, item) is { } text ? Solid(WithAlpha(text, 0.7)) : Theme.Muted;

    /// <param name="solidOpaque">The look as a solid, fully opaque card (what shows behind a letterboxed picture).</param>
    internal static CardLook Look(AppearanceSettings? defaults, LauncherItem item, bool solidOpaque = false)
    {
        var eff = Effective(defaults, item);
        string style = solidOpaque ? "solid" : eff.CardStyle;
        double opacity = solidOpaque ? 1 : eff.Opacity / 100.0;
        var custom = style is "solid" or "glass" or "frosted" ? ParseColor(eff.CardColor) : null;
        var borderColor = ParseColor(eff.BorderColor);

        if (style == "frosted") return FrostedLook(custom, borderColor, eff.Radius, opacity);

        Color fillColor = style switch
        {
            "outline" or "none" => Colors.Transparent,
            "glass" => custom is { } g ? WithAlpha(g, GlassColor.A / 255.0 * (g.A / 255.0)) : GlassColor,
            _ => custom ?? ((SolidColorBrush)Theme.Surface).Color,
        };
        var fill = Solid(WithAlpha(fillColor, opacity));

        Brush hover;
        if (custom is { } c)
        {
            // A custom card color lightens on hover (or darkens, when it is already light).
            double luminance = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;
            byte Blend(byte x) => (byte)Math.Round(x + ((luminance > 0.62 ? 0 : 255) - x) * (luminance > 0.62 ? 0.12 : 0.1));
            hover = Solid(WithAlpha(Color.FromArgb(fillColor.A, Blend(c.R), Blend(c.G), Blend(c.B)), opacity));
        }
        else hover = Solid(WithAlpha(((SolidColorBrush)Theme.SurfaceHover).Color, opacity));

        Brush border = style switch
        {
            "none" => Brushes.Transparent,
            "outline" => borderColor is { } oc ? Solid(oc) : Theme.LineStrong,
            _ => borderColor is { } bc ? Solid(bc) : Theme.Line,
        };
        return new CardLook(fill, hover, border, style == "outline" ? 1.5 : 1, eff.Radius, opacity, custom is not null);
    }

    internal static Border Card(CardLook look) => new()
    {
        Background = look.Fill,
        BorderBrush = look.Border,
        BorderThickness = new Thickness(look.BorderThickness),
        CornerRadius = look.Corners,
    };

    internal static Border Card(AppearanceSettings? defaults, LauncherItem? item)
        => item is null ? Card(Look(defaults, new LauncherItem())) : Card(Look(defaults, item));

    /// <summary>
    /// Applies a widget's text color and text size to a finished built-in widget: labels in the theme's text colors take the
    /// custom one, and every label is scaled by the "Text size" setting. Plug-in widgets do this themselves through WidgetContext.
    /// </summary>
    internal static void StyleText(DependencyObject root, AppearanceSettings? defaults, LauncherItem item)
    {
        var text = TextColor(defaults, item);
        double scale = Math.Clamp(OptionNumber(item, "_textScale") ?? 100, 60, 250) / 100.0;
        if (text is null && Math.Abs(scale - 1) < 0.005) return;
        Brush? primary = text is { } t ? Solid(t) : null;
        Brush? secondary = text is { } t2 ? Solid(WithAlpha(t2, 0.8)) : null;
        string iconFont = Theme.IconFont.Source ?? "";
        void Walk(DependencyObject node)
        {
            if (node is TextBlock block)
            {
                if (!string.Equals(block.FontFamily.Source, iconFont, StringComparison.Ordinal))
                {
                    if (primary is not null && ReferenceEquals(block.Foreground, Theme.Text)) block.Foreground = primary;
                    else if (secondary is not null && ReferenceEquals(block.Foreground, Theme.Text2)) block.Foreground = secondary;
                    if (Math.Abs(scale - 1) >= 0.005) block.FontSize = Math.Max(6, block.FontSize * scale);
                }
                return;
            }
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) Walk(child);
        }
        Walk(root);
    }

    // ---------- Deck background ----------

    private static readonly Dictionary<string, Brush> BrushCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, (BitmapSource Image, double NaturalWidth, double NaturalHeight)?> DecodedCache = new(StringComparer.Ordinal);

    /// <summary>The brush for a deck background: this display's own setting, else the all-displays one, else the theme color.</summary>
    /// <param name="size">The deck's size in DIP.</param>
    /// <param name="scale">Device pixels per DIP (the display's DPI scale), so pictures are decoded at their real size.</param>
    internal static Brush DeckBackground(DeckSettings s, DisplayInfo display, Size size, double scale = 1)
        => BuildBrush(DeckStyle(s, display.Id), size, scale, display);

    /// <summary>What Windows' own wallpaper looks like on a deck: with desktop icons shown the deck always draws exactly that (the saved background choice is kept, just not used).</summary>
    private static readonly BackgroundStyle WindowsLook = new() { Kind = "wallpaper", MatchWindows = true };

    /// <summary>The background that applies to a display's deck: the Windows wallpaper while desktop icons are shown, else its own, else the all-displays one, else null (theme).</summary>
    internal static BackgroundStyle? DeckStyle(DeckSettings s, string? displayId) => s.ShowDesktopIcons ? WindowsLook : s.Appearance?.BackgroundFor(displayId);

    /// <summary>The brush for any background style (null = the theme color). Pictures are decoded once and cached.</summary>
    /// <param name="display">The monitor the deck is on (the wallpaper is read for it); null for a preview.</param>
    internal static Brush BuildBrush(BackgroundStyle? style, Size size, double scale = 1, DisplayInfo? display = null)
    {
        if (style is null || style.Kind == "theme") return Dimmed(Theme.Background, style?.Dim ?? 0);
        try
        {
            switch (style.Kind)
            {
                case "solid":
                    return Dimmed(ParseColor(style.Color) is { } solid ? Solid(solid) : Theme.Background, style.Dim);
                case "gradient":
                    return GradientBrush(style);
                default:
                    return ImageBackground(style, size, scale, display) ?? Theme.Background;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException or FileFormatException or COMException or OutOfMemoryException)
        {
            App.Log(ex);
            return Theme.Background;
        }
    }

    private static Brush Dimmed(Brush brush, double dim)
    {
        if (dim <= 0 || brush is not SolidColorBrush solid) return brush;
        return Solid(Darken(solid.Color, dim));
    }

    private static Color Darken(Color c, double dim)
    {
        double keep = 1 - Math.Clamp(dim, 0, 100) / 100.0;
        return Color.FromArgb(c.A, (byte)Math.Round(c.R * keep), (byte)Math.Round(c.G * keep), (byte)Math.Round(c.B * keep));
    }

    private static Brush GradientBrush(BackgroundStyle style)
    {
        var from = Darken(ParseColor(style.Color) ?? ((SolidColorBrush)Theme.Background).Color, style.Dim);
        var to = Darken(ParseColor(style.Color2) ?? from, style.Dim);
        // CSS-style angle: the gradient line runs corner to corner so both colors reach the edges of any rectangle.
        double radians = style.Angle * Math.PI / 180, dx = Math.Sin(radians), dy = -Math.Cos(radians);
        double k = 1 / Math.Max(Math.Abs(dx), Math.Abs(dy));
        var brush = new LinearGradientBrush(from, to, new Point(0.5 - dx * k / 2, 0.5 - dy * k / 2), new Point(0.5 + dx * k / 2, 0.5 + dy * k / 2));
        brush.Freeze();
        return brush;
    }

    private static Brush? ImageBackground(BackgroundStyle style, Size size, double scale, DisplayInfo? display)
    {
        string path;
        if (style.Kind == "wallpaper")
        {
            var look = WindowsWallpaper.For(display);
            if (style.MatchWindows) return WindowsWallpaperBrush(look, size, scale, display);
            path = look.Path;
        }
        else path = Environment.ExpandEnvironmentVariables(style.ImagePath ?? "");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        if (size.Width < 1 || size.Height < 1) size = new Size(1920, 1080);
        scale = scale > 0 ? scale : 1;
        string key = string.Join("|", style.Kind, path, File.GetLastWriteTimeUtc(path).Ticks, style.Fit, style.Dim.ToString("0.#", Invariant), style.Blur.ToString("0.#", Invariant),
            Math.Round(size.Width), Math.Round(size.Height), scale.ToString("0.##", Invariant));
        if (BrushCache.TryGetValue(key, out var cached)) return cached;
        if (BrushCache.Count >= 10) BrushCache.Clear();

        double w = Math.Round(size.Width), h = Math.Round(size.Height);
        var loaded = LoadImage(path, style.Blur, (nw, nh) => style.Fit switch
        {
            "fill" => Math.Max(w * scale / nw, h * scale / nh),
            "fit" => Math.Min(w * scale / nw, h * scale / nh),
            _ => 1,
        }, 4096, w * scale);
        if (loaded is not { } picture) return null;

        var group = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(0, 0, w, h)) };
        RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.HighQuality);
        // The base color shows around a "fit" or centered picture, and fixes the drawing's bounds to the deck.
        group.Children.Add(new GeometryDrawing(Theme.Background, null, new RectangleGeometry(new Rect(0, 0, w, h))));
        double ratio = picture.NaturalWidth / picture.NaturalHeight;
        switch (style.Fit)
        {
            case "tile":
            {
                var tile = new ImageBrush(picture.Image) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, picture.NaturalWidth / scale, picture.NaturalHeight / scale), Stretch = Stretch.Fill };
                RenderOptions.SetBitmapScalingMode(tile, BitmapScalingMode.HighQuality);
                tile.Freeze();
                group.Children.Add(new GeometryDrawing(tile, null, new RectangleGeometry(new Rect(0, 0, w, h))));
                break;
            }
            case "center":
            {
                double iw = picture.NaturalWidth / scale, ih = picture.NaturalHeight / scale;
                group.Children.Add(new ImageDrawing(picture.Image, new Rect((w - iw) / 2, (h - ih) / 2, iw, ih)));
                break;
            }
            default:
            {
                double iw, ih;
                if (style.Fit == "fit") { iw = Math.Min(w, h * ratio); ih = iw / ratio; }
                else { iw = Math.Max(w, h * ratio); ih = iw / ratio; }
                group.Children.Add(new ImageDrawing(picture.Image, new Rect((w - iw) / 2, (h - ih) / 2, iw, ih)));
                break;
            }
        }
        if (style.Dim > 0) group.Children.Add(new GeometryDrawing(Solid(Color.FromArgb((byte)Math.Round(style.Dim / 100 * 255), 0, 0, 0)), null, new RectangleGeometry(new Rect(0, 0, w, h))));
        group.Freeze();
        var brush = new DrawingBrush(group) { Stretch = Stretch.Fill };
        brush.Freeze();
        BrushCache[key] = brush;
        return brush;
    }

    /// <summary>
    /// The Windows wallpaper drawn the way Windows draws it on this monitor (center, tile, stretch, fit, fill or span; a solid color when there is no picture),
    /// then cut to the deck's part of the monitor (the work area), so the deck lines up with the real desktop around its widgets.
    /// </summary>
    private static Brush WindowsWallpaperBrush(WallpaperLook look, Size size, double scale, DisplayInfo? display)
    {
        scale = scale > 0 ? scale : 1;
        if (size.Width < 1 || size.Height < 1) size = new Size(1920, 1080);
        double w = Math.Round(size.Width), h = Math.Round(size.Height);
        string path = look.Path;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return look.FromShell ? Solid(look.Background) : Theme.Background;

        var monitor = display?.Bounds ?? new PixelRect(0, 0, (int)Math.Round(w * scale), (int)Math.Round(h * scale));
        int offsetX = display is null ? 0 : display.WorkArea.X - display.Bounds.X, offsetY = display is null ? 0 : display.WorkArea.Y - display.Bounds.Y;
        var virtualScreen = look.Position == WallpaperPosition.Span ? WindowsWallpaper.VirtualScreen(DesktopDeck.Displays.Current()) : monitor;
        string key = string.Join("|", "windows", path, File.GetLastWriteTimeUtc(path).Ticks, look.Position, look.Background, monitor, offsetX, offsetY, virtualScreen, w, h,
            scale.ToString("0.##", Invariant), WindowsWallpaper.Version);
        if (BrushCache.TryGetValue(key, out var cached)) return cached;
        if (BrushCache.Count >= 10) BrushCache.Clear();

        WallpaperPlan PlanFor(double nw, double nh) => WallpaperMath.Plan(nw, nh, monitor.X, monitor.Y, monitor.Width, monitor.Height, look.Position,
            virtualScreen.X, virtualScreen.Y, virtualScreen.Width, virtualScreen.Height);
        var loaded = LoadImage(path, 0, (nw, nh) => WallpaperMath.NeededScale(nw, nh, PlanFor(nw, nh)), 8192, w * scale);
        if (loaded is not { } picture) return look.FromShell ? Solid(look.Background) : Theme.Background;

        var plan = PlanFor(picture.NaturalWidth, picture.NaturalHeight);
        var dest = new Rect((plan.X - offsetX) / scale, (plan.Y - offsetY) / scale, plan.Width / scale, plan.Height / scale);
        var bounds = new Rect(0, 0, w, h);
        var group = new DrawingGroup { ClipGeometry = new RectangleGeometry(bounds) };
        RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.HighQuality);
        // The wallpaper color shows around a fitted or centered picture, as it does on the desktop.
        group.Children.Add(new GeometryDrawing(Solid(look.Background), null, new RectangleGeometry(bounds)));
        if (plan.Tile)
        {
            var tile = new ImageBrush(picture.Image) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = dest, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(tile, BitmapScalingMode.HighQuality);
            tile.Freeze();
            group.Children.Add(new GeometryDrawing(tile, null, new RectangleGeometry(bounds)));
        }
        else group.Children.Add(new ImageDrawing(picture.Image, dest));
        group.Freeze();
        var brush = new DrawingBrush(group) { Stretch = Stretch.Fill };
        brush.Freeze();
        BrushCache[key] = brush;
        return brush;
    }

    /// <summary>Decodes the picture once at the size the deck needs (never larger), blurring it when asked. needFor gives the share of the full size that is needed from the picture's pixel size.</summary>
    private static (BitmapSource Image, double NaturalWidth, double NaturalHeight)? LoadImage(string path, double blur, Func<int, int, double> needFor, int longestSide, double pixelsWide)
    {
        int naturalWidth, naturalHeight;
        using (var stream = File.OpenRead(path))
        {
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None).Frames[0];
            naturalWidth = frame.PixelWidth;
            naturalHeight = frame.PixelHeight;
        }
        if (naturalWidth < 1 || naturalHeight < 1) return null;
        // The scale that makes the picture just cover (fill) or just fit the deck's pixels; tiles and centered pictures keep their size.
        double need = needFor(naturalWidth, naturalHeight);
        double factor = Math.Min(1, need);
        // Very large originals are bounded even when they are shown at full size.
        factor = Math.Min(factor, (double)longestSide / Math.Max(naturalWidth, naturalHeight));
        // A blurred picture has no fine detail to keep.
        if (blur > 0) factor = Math.Min(factor, 1600.0 / Math.Max(naturalWidth, naturalHeight));
        string key = string.Join("|", path, File.GetLastWriteTimeUtc(path).Ticks, factor.ToString("0.####", Invariant), blur.ToString("0.#", Invariant));
        if (DecodedCache.TryGetValue(key, out var hit)) return hit;
        if (DecodedCache.Count >= 6) DecodedCache.Clear();

        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(path);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        if (factor < 0.999) image.DecodePixelWidth = Math.Max(1, (int)Math.Round(naturalWidth * factor));
        image.EndInit();
        image.Freeze();
        BitmapSource result = image;
        if (blur > 0) result = Blur(image, blur * image.PixelWidth / Math.Max(1.0, pixelsWide));
        var entry = (result, (double)naturalWidth, (double)naturalHeight);
        DecodedCache[key] = entry;
        return entry;
    }

    /// <summary>The picture, blurred. It is drawn a little larger than the frame and clipped, so the edges do not fade out.</summary>
    private static BitmapSource Blur(BitmapSource source, double radius)
    {
        radius = Math.Clamp(radius, 0.5, 80);
        double w = source.PixelWidth, h = source.PixelHeight, pad = radius * 1.5;
        var picture = new Image
        {
            Source = source, Stretch = Stretch.Fill, Width = w + 2 * pad, Height = h + 2 * pad,
            Margin = new Thickness(-pad), Effect = new BlurEffect { Radius = radius, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance },
        };
        var host = new Grid { Width = w, Height = h, ClipToBounds = true, Background = Brushes.Transparent };
        host.Children.Add(picture);
        host.Measure(new Size(w, h));
        host.Arrange(new Rect(0, 0, w, h));
        var target = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
        target.Render(host);
        target.Freeze();
        return target;
    }

    /// <summary>The current Windows desktop wallpaper file (read only), or "" when there is none (a solid-color desktop).</summary>
    internal static string WallpaperPath()
    {
        try
        {
            var path = new StringBuilder(520);
            return SystemParametersInfo(0x0073, (uint)path.Capacity, path, 0) ? path.ToString() : "";
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return ""; }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SystemParametersInfoW")]
    private static extern bool SystemParametersInfo(uint action, uint param, StringBuilder value, uint winIni);

    // ---------- Background pictures in the media folder ----------

    private static bool IsManaged(SettingsStore store, string path)
        => !string.IsNullOrWhiteSpace(path) && Path.GetFullPath(path).StartsWith(Path.GetFullPath(store.MediaDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>Copies a picked picture into the deck's media folder (like widget pictures) so the original can move freely.</summary>
    internal static string ImportPicture(SettingsStore store, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || IsManaged(store, path) || !File.Exists(path)) return path;
        Directory.CreateDirectory(store.MediaDirectory);
        string destination = Path.Combine(store.MediaDirectory, Guid.NewGuid().ToString("N") + Path.GetExtension(path).ToLowerInvariant());
        File.Copy(path, destination);
        return destination;
    }

    /// <summary>Deletes a managed background picture that nothing in the settings uses any more.</summary>
    internal static void ReleasePicture(DeckSettings settings, SettingsStore store, string path)
    {
        if (!IsManaged(store, path)) return;
        bool used = LayoutEngine.PictureInUse(settings, path)
            || settings.Items.Any(i => string.Equals(i.IconPath, path, StringComparison.OrdinalIgnoreCase) || string.Equals(i.ImagePath, path, StringComparison.OrdinalIgnoreCase));
        // Undo, the recycle bin and the daily snapshots may still need it; the media sweep removes it once they don't.
        if (used || HistoryMedia.IsHeld(path)) return;
        try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
