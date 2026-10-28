using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DesktopDeck.Widgets;

/// <summary>Filtering and wording for the recent-files list. Pure logic (unit tested).</summary>
public static class RecentFilesLogic
{
    /// <summary>One recent item: the file or folder a Recent shortcut points to, and when it was last opened.</summary>
    public sealed record Entry(string Path, DateTime Modified, bool Folder);

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".doc", ".docx", ".rtf", ".odt", ".txt", ".md", ".xls", ".xlsx", ".csv", ".ods", ".ppt", ".pptx", ".odp", ".epub" };

    /// <summary>The extensions typed in a Lines option, normalized to ".pdf" form ("*.PDF", "pdf" and ".pdf" are the same).</summary>
    public static HashSet<string> NormalizeExtensions(IEnumerable<string> lines) =>
        new(lines.Select(l => l.Trim().TrimStart('*', '.').Trim()).Where(l => l.Length > 0).Select(l => "." + l), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Keeps what should be listed, newest first, one row per path. <paramref name="type"/> is "all", "documents" or
    /// "images"; when <paramref name="extensions"/> has entries only those extensions are listed instead of the type.
    /// </summary>
    public static List<Entry> Filter(IEnumerable<Entry> entries, string type, IEnumerable<string> extensions, bool hideFolders, int count)
    {
        var custom = NormalizeExtensions(extensions);
        bool Matches(Entry e)
        {
            if (e.Folder) return !hideFolders && custom.Count == 0 && type == "all";
            string extension = System.IO.Path.GetExtension(e.Path);
            if (custom.Count > 0) return custom.Contains(extension);
            return type switch
            {
                "documents" => DocumentExtensions.Contains(extension),
                "images" => SlideshowLogic.IsPicture(e.Path) || extension.Equals(".ico", StringComparison.OrdinalIgnoreCase) || extension.Equals(".svg", StringComparison.OrdinalIgnoreCase),
                _ => true,
            };
        }
        return entries.Where(Matches)
            .GroupBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(e => e.Modified).First())
            .OrderByDescending(e => e.Modified)
            .Take(Math.Clamp(count, 1, 100))
            .ToList();
    }

    /// <summary>"just now", "5 min ago", "3 h ago", "Yesterday", "4 days ago", then "12 Mar" (with the year if not this year).</summary>
    public static string Ago(DateTime when, DateTime now, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var span = now - when;
        if (span < TimeSpan.FromMinutes(1)) return "just now";
        if (span < TimeSpan.FromHours(1)) return (int)span.TotalMinutes + " min ago";
        if (span < TimeSpan.FromHours(24) && when.Date == now.Date) return (int)span.TotalHours + " h ago";
        int days = (now.Date - when.Date).Days;
        if (days == 1) return "Yesterday";
        if (days is > 1 and < 7) return days + " days ago";
        return when.ToString(when.Year == now.Year ? "d MMM" : "d MMM yyyy", culture);
    }

    /// <summary>The name of the folder a path is in ("C:\Users\me\Documents\a.pdf" gives "Documents"; a drive root gives the drive).</summary>
    public static string ParentName(string path)
    {
        string? parent = System.IO.Path.GetDirectoryName(path.TrimEnd('\\', '/'));
        if (string.IsNullOrEmpty(parent)) return "";
        string name = System.IO.Path.GetFileName(parent);
        return name.Length > 0 ? name : parent;
    }

    /// <summary>The icon family for a row: "folder", "image", "document", "audio", "video" or "file".</summary>
    public static string Category(Entry entry)
    {
        if (entry.Folder) return "folder";
        string extension = System.IO.Path.GetExtension(entry.Path).ToLowerInvariant();
        if (SlideshowLogic.IsPicture(entry.Path) || extension is ".svg" or ".ico") return "image";
        if (DocumentExtensions.Contains(extension)) return "document";
        if (extension is ".mp3" or ".wav" or ".flac" or ".m4a" or ".ogg") return "audio";
        if (extension is ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm") return "video";
        return "file";
    }

    /// <summary>The second line of a row: the folder, the age, both or nothing ("folder", "date", "both", "none").</summary>
    public static string Subtitle(Entry entry, string mode, DateTime now, CultureInfo? culture = null)
    {
        string folder = ParentName(entry.Path), age = Ago(entry.Modified, now, culture);
        return mode switch
        {
            "folder" => folder,
            "date" => age,
            "none" => "",
            _ => folder.Length > 0 ? folder + "  ·  " + age : age,
        };
    }

    /// <summary>True for network paths (\\server\share), which are skipped unless asked for because they can be slow to reach.</summary>
    public static bool IsNetworkPath(string path) => path.StartsWith("\\\\", StringComparison.Ordinal) && !path.StartsWith("\\\\?\\", StringComparison.Ordinal);
}
