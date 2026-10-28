using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace DesktopDeck.Widgets;

/// <summary>One picture found in the slideshow folder.</summary>
public sealed record SlideshowPicture(string Path, DateTime Modified);

/// <summary>Ordering, captions and decode sizes for the photo slideshow. Pure logic (unit tested).</summary>
public static class SlideshowLogic
{
    /// <summary>Picture types WPF can decode out of the box.</summary>
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".jfif" };

    public static bool IsPicture(string path) => Extensions.Contains(System.IO.Path.GetExtension(path));

    /// <summary>
    /// The pictures in display order. <paramref name="order"/> is "name", "name-desc", "newest" or "oldest";
    /// <paramref name="shuffle"/> replaces it with a stable random order for <paramref name="seed"/> (every picture once per round).
    /// </summary>
    public static List<SlideshowPicture> Order(IEnumerable<SlideshowPicture> pictures, string order, bool shuffle, int seed)
    {
        var list = pictures.ToList();
        if (shuffle)
        {
            // Sort first so the shuffle is the same for the same files whatever order the disk listed them in.
            list.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Path, b.Path));
            var random = new Random(seed);
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            return list;
        }
        IEnumerable<SlideshowPicture> sorted = order switch
        {
            "name-desc" => list.OrderByDescending(p => System.IO.Path.GetFileName(p.Path), NaturalComparer.Instance),
            "newest" => list.OrderByDescending(p => p.Modified).ThenBy(p => p.Path, StringComparer.OrdinalIgnoreCase),
            "oldest" => list.OrderBy(p => p.Modified).ThenBy(p => p.Path, StringComparer.OrdinalIgnoreCase),
            _ => list.OrderBy(p => System.IO.Path.GetFileName(p.Path), NaturalComparer.Instance),
        };
        return sorted.ToList();
    }

    /// <summary>Steps through <paramref name="count"/> pictures, wrapping both ways.</summary>
    public static int Step(int index, int delta, int count) => count <= 0 ? 0 : ((index + delta) % count + count) % count;

    /// <summary>Seconds between pictures for an interval and unit ("seconds" or "minutes"); at least 2 s.</summary>
    public static double IntervalSeconds(double value, string unit) => Math.Max(2, unit == "minutes" ? value * 60 : value);

    /// <summary>The caption: "name" (file name without extension), "date", "both" or "none".</summary>
    public static string Caption(string mode, string path, DateTime modified, CultureInfo culture)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(path);
        string date = modified.ToString("d MMM yyyy", culture);
        return mode switch
        {
            "name" => name,
            "date" => date,
            "both" => name + "  ·  " + date,
            _ => "",
        };
    }

    /// <summary>
    /// The width to decode a <paramref name="sourceWidth"/> × <paramref name="sourceHeight"/> picture at so it stays sharp
    /// in a <paramref name="boxWidth"/> × <paramref name="boxHeight"/> frame (DIP) at <paramref name="scale"/> pixels per DIP.
    /// "fill" covers the frame, so the shorter side decides; "fit" shows all of it, so the longer side does.
    /// Never larger than the picture itself.
    /// </summary>
    public static int DecodeWidth(int sourceWidth, int sourceHeight, double boxWidth, double boxHeight, bool fill, double scale = 2)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || boxWidth <= 0 || boxHeight <= 0) return Math.Max(1, sourceWidth);
        double factor = fill ? Math.Max(boxWidth / sourceWidth, boxHeight / sourceHeight) : Math.Min(boxWidth / sourceWidth, boxHeight / sourceHeight);
        int wanted = (int)Math.Ceiling(sourceWidth * factor * scale);
        return Math.Clamp(wanted, 1, sourceWidth);
    }

    /// <summary>Clockwise rotation (0, 90, 180, 270) for an EXIF orientation value; mirrored values use their rotation.</summary>
    public static int OrientationAngle(int orientation) => orientation switch
    {
        3 or 4 => 180,
        5 or 6 => 90,
        7 or 8 => 270,
        _ => 0,
    };

    /// <summary>Whether the folder listing should be read again (at most once a minute).</summary>
    public static bool ListingStale(DateTime? listedAt, DateTime now) => listedAt is not { } at || now - at >= TimeSpan.FromMinutes(1) || now < at;

    /// <summary>"3 / 24".</summary>
    public static string Counter(int index, int count) => count <= 0 ? "" : $"{index + 1} / {count}";

    /// <summary>Compares names with numbers in numeric order ("IMG_2" before "IMG_10").</summary>
    public sealed class NaturalComparer : IComparer<string>
    {
        public static readonly NaturalComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            int i = 0, j = 0;
            while (i < x.Length && j < y.Length)
            {
                if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
                {
                    int si = i, sj = j;
                    while (i < x.Length && char.IsDigit(x[i])) i++;
                    while (j < y.Length && char.IsDigit(y[j])) j++;
                    string a = x[si..i].TrimStart('0'), b = y[sj..j].TrimStart('0');
                    if (a.Length != b.Length) return a.Length.CompareTo(b.Length);
                    int c = string.CompareOrdinal(a, b);
                    if (c != 0) return c;
                }
                else
                {
                    int c = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                    if (c != 0) return c;
                    i++;
                    j++;
                }
            }
            return (x.Length - i).CompareTo(y.Length - j);
        }
    }
}
