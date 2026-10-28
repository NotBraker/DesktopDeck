using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DesktopDeck.Widgets;

/// <summary>Drive list filtering, sorting and text for the Drives widget. Pure logic (unit tested).</summary>
public static class DiskSpaceLogic
{
    /// <summary>One drive: Name like "C:", Kind "fixed", "removable" or "network", sizes in bytes.</summary>
    public sealed record Drive(string Name, string Label, string Kind, long Total, long Free)
    {
        public long Used => Math.Max(0, Total - Free);
        public double UsedFraction => DiskSpaceLogic.UsedFraction(Total, Free);
    }

    /// <summary>0–1 of the drive in use.</summary>
    public static double UsedFraction(long total, long free)
        => total <= 0 ? 0 : Math.Clamp((double)(total - Math.Clamp(free, 0, total)) / total, 0, 1);

    /// <summary>"c", "C:", "c:\" all become "C:"; anything else is trimmed.</summary>
    public static string NormalizeName(string? name)
    {
        string text = (name ?? "").Trim().TrimEnd('\\', '/');
        if (text.Length == 1 && char.IsLetter(text[0])) text += ":";
        return text.Length == 2 && text[1] == ':' ? text.ToUpperInvariant() : text;
    }

    /// <summary>True when the drive's letter (or its label) is in the hidden list.</summary>
    public static bool IsHidden(Drive drive, IEnumerable<string> hidden)
    {
        string name = NormalizeName(drive.Name);
        return hidden.Any(h => string.Equals(NormalizeName(h), name, StringComparison.OrdinalIgnoreCase)
            || (drive.Label.Length > 0 && string.Equals(h.Trim(), drive.Label, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Which drives to show, in which order: "name", "fullest", "emptiest" (most free space first) or "largest".</summary>
    public static List<Drive> Arrange(IEnumerable<Drive> drives, string order, IEnumerable<string> hidden, bool removable, bool network)
    {
        var hiddenList = hidden.ToList();
        var shown = drives.Where(d => d.Total > 0 && (d.Kind == "fixed" || (d.Kind == "removable" && removable) || (d.Kind == "network" && network)) && !IsHidden(d, hiddenList));
        return (order switch
        {
            "fullest" => shown.OrderByDescending(d => d.UsedFraction).ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase),
            "emptiest" => shown.OrderByDescending(d => d.Free).ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase),
            "largest" => shown.OrderByDescending(d => d.Total).ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase),
            _ => shown.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase),
        }).ToList();
    }

    /// <summary>The detail text: "free" → "120 GB free", "used" → "392 GB used", "both" → "392 / 512 GB", "percent" → "77% used".</summary>
    public static string Detail(Drive drive, string mode, IFormatProvider? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return mode switch
        {
            "used" => SystemStatsLogic.FormatBytes(drive.Used, culture) + " used",
            "both" => SystemStatsLogic.UsedOfTotal(drive.Used, drive.Total, culture),
            "percent" => SystemStatsLogic.Percent(drive.UsedFraction * 100, 0, culture) + " used",
            "freeof" => SystemStatsLogic.FormatBytes(drive.Free, culture) + " free of " + SystemStatsLogic.FormatBytes(drive.Total, culture),
            "none" => "",
            _ => SystemStatsLogic.FormatBytes(drive.Free, culture) + " free",
        };
    }

    /// <summary>"C:" or "Windows (C:)" like Explorer.</summary>
    public static string Title(Drive drive, bool withLabel)
        => withLabel && drive.Label.Trim().Length > 0 ? $"{drive.Label.Trim()} ({drive.Name})" : drive.Name;

    /// <summary>Made-up drives for previews.</summary>
    public static List<Drive> FakeDrives() =>
    [
        new("C:", "Windows", "fixed", 511L * 1024 * 1024 * 1024, 87L * 1024 * 1024 * 1024),
        new("D:", "Data", "fixed", 1863L * 1024 * 1024 * 1024, 1120L * 1024 * 1024 * 1024),
        new("E:", "Games", "fixed", 931L * 1024 * 1024 * 1024, 41L * 1024 * 1024 * 1024),
        new("F:", "USB stick", "removable", 58L * 1024 * 1024 * 1024, 31L * 1024 * 1024 * 1024),
        new("Z:", "NAS", "network", 3725L * 1024 * 1024 * 1024, 2410L * 1024 * 1024 * 1024),
    ];
}
