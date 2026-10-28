using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using DesktopDeck.Layout;
using DesktopDeck.Models;

namespace DesktopDeck;

/// <summary>The deck background of one display, blurred once into a small picture that every frosted card on that display paints a window of.</summary>
/// <param name="Image">The blurred background, at <paramref name="Scale"/> pixels per DIP.</param>
/// <param name="Scale">Pixels of <paramref name="Image"/> per DIP of the deck.</param>
/// <param name="Deck">The deck's size in DIP.</param>
internal sealed record Backdrop(BitmapSource Image, double Scale, Size Deck);

/// <summary>
/// "Frosted glass" cards, like the widgets on macOS and iOS: the background behind the card is blurred, with a light tint over it and a thin light
/// edge. The blur is done once per display (a small blurred copy of the deck background, cached and redone only when the background or the size
/// changes); each card then just paints its own window of that picture, so many cards cost almost nothing. Where there is no deck background
/// to look through (floating widgets, the layer in front of other windows) the card is a more opaque tint instead.
/// </summary>
internal static partial class Appearance
{
    private const double BlurDip = 40, BackdropScale = 0.3, BackdropLongSide = 640;
    private static readonly Dictionary<string, Backdrop> BackdropCache = new(StringComparer.Ordinal);
    private static (Backdrop Source, Rect Card)? current;

    // ---------- The blurred background ----------

    /// <summary>
    /// The blurred copy of a deck background for a deck of this size, or null when the size is unknown. Cached by what the background looks like
    /// (style, picture file and its date, size, theme) so redrawing the deck does not blur again.
    /// </summary>
    internal static Backdrop? FrostBackdrop(BackgroundStyle? style, Size size, DisplayInfo? display = null)
    {
        if (size.Width < 8 || size.Height < 8) return null;
        string path = style is { UsesImage: true } ? (style.Kind == "wallpaper" ? WindowsWallpaper.For(display).Path : Environment.ExpandEnvironmentVariables(style.ImagePath ?? "")) : "";
        long stamp = path.Length > 0 && File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0;
        string key = string.Join("|", style?.Signature ?? "theme", path, stamp, display?.Id, WindowsWallpaper.Version, Math.Round(size.Width), Math.Round(size.Height), ((SolidColorBrush)Theme.Background).Color);
        if (BackdropCache.TryGetValue(key, out var hit)) return hit;
        if (BackdropCache.Count >= 6) BackdropCache.Clear();

        double scale = Math.Min(BackdropScale, BackdropLongSide / Math.Max(size.Width, size.Height));
        int width = Math.Max(8, (int)Math.Round(size.Width * scale)), height = Math.Max(8, (int)Math.Round(size.Height * scale));
        try
        {
            var brush = BuildBrush(style, new Size(size.Width, size.Height), scale, display);
            var image = BlurBrush(brush, width, height, BlurDip * scale);
            var backdrop = new Backdrop(image, width / size.Width, size);
            BackdropCache[key] = backdrop;
            return backdrop;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or OutOfMemoryException or System.Runtime.InteropServices.COMException)
        {
            App.Log(ex);
            return null;
        }
    }

    /// <summary>A brush drawn into a small picture and blurred. The edges are mirrored first, so the picture does not fade out at its border.</summary>
    private static BitmapSource BlurBrush(Brush brush, int width, int height, double radius)
    {
        radius = Math.Clamp(radius, 2, 60);
        double pad = Math.Ceiling(radius * 2);
        var tile = new DrawingBrush(new GeometryDrawing(brush, null, new RectangleGeometry(new Rect(0, 0, width, height))))
        {
            TileMode = TileMode.FlipXY, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, width, height),
            ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(pad, pad, width, height), Stretch = Stretch.Fill,
        };
        var surface = new System.Windows.Shapes.Rectangle
        {
            Width = width + 2 * pad, Height = height + 2 * pad, Margin = new Thickness(-pad), Fill = tile,
            Effect = new BlurEffect { Radius = radius, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality },
        };
        var host = new Grid { Width = width, Height = height, ClipToBounds = true, Background = Brushes.Transparent };
        host.Children.Add(surface);
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(host);
        target.Freeze();
        return target;
    }

    // ---------- Telling the cards where they are ----------

    /// <summary>
    /// Until the returned scope is disposed, frosted cards paint the part of <paramref name="backdrop"/> under <paramref name="card"/> (the card's place
    /// on the deck, in DIP). The deck wraps each widget's build in one. Null scopes are fine to dispose.
    /// </summary>
    internal static IDisposable? PushBackdrop(Backdrop? backdrop, Rect card)
    {
        var before = current;
        current = backdrop is null ? null : (backdrop, card);
        return new Restore(before);
    }

    private sealed class Restore((Backdrop Source, Rect Card)? before) : IDisposable
    {
        public void Dispose() => current = before;
    }

    // ---------- The card ----------

    /// <summary>The frosted card's fill, hover fill and edge. <paramref name="custom"/> is the card color the user picked, if any.</summary>
    private static CardLook FrostedLook(Color? custom, Color? borderColor, double radius, double opacity)
    {
        bool dark = Theme.IsDark;
        var source = current;
        Color tint = custom ?? ((SolidColorBrush)Theme.Surface).Color;
        // Over a blurred background the tint can be light; with nothing to blur it has to carry the card by itself.
        double tintAlpha = source is null ? 0.7 : dark ? 0.56 : 0.55;
        var fill = FrostedFill(source, tint, tintAlpha, 0, opacity, dark);
        var hover = FrostedFill(source, tint, tintAlpha, 0.08, opacity, dark);
        Brush edge;
        if (borderColor is { } bc) edge = Solid(bc);
        else
        {
            var gradient = dark
                ? new LinearGradientBrush(Color.FromArgb(96, 255, 255, 255), Color.FromArgb(22, 255, 255, 255), 90)
                : new LinearGradientBrush(Color.FromArgb(150, 255, 255, 255), Color.FromArgb(40, 0, 0, 0), 90);
            gradient.Freeze();
            edge = gradient;
        }
        return new CardLook(fill, hover, edge, 1, radius, opacity, custom is not null);
    }

    private static Brush FrostedFill((Backdrop Source, Rect Card)? source, Color tint, double tintAlpha, double extraLight, double opacity, bool dark)
    {
        var whole = new RectangleGeometry(new Rect(0, 0, 1, 1));
        var group = new DrawingGroup();
        if (source is { } s)
        {
            double k = s.Source.Scale;
            var picture = new ImageBrush(s.Source.Image)
            {
                ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(s.Card.X * k, s.Card.Y * k, Math.Max(1, s.Card.Width * k), Math.Max(1, s.Card.Height * k)),
                Stretch = Stretch.Fill, Opacity = opacity,
            };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.Fant);
            group.Children.Add(new GeometryDrawing(picture, null, whole));
        }
        group.Children.Add(new GeometryDrawing(Solid(WithAlpha(tint, tintAlpha * opacity)), null, whole));
        // A soft sheen from the top, like light catching the glass.
        byte top = (byte)Math.Round((dark ? 40 : 90) * opacity + 255 * extraLight * opacity), bottom = (byte)Math.Round((dark ? 6 : 14) * opacity + 255 * extraLight * opacity);
        var sheen = new LinearGradientBrush(Color.FromArgb(top, 255, 255, 255), Color.FromArgb(bottom, 255, 255, 255), 90);
        group.Children.Add(new GeometryDrawing(sheen, null, whole));
        group.Freeze();
        var brush = new DrawingBrush(group) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 1, 1) };
        brush.Freeze();
        return brush;
    }
}
