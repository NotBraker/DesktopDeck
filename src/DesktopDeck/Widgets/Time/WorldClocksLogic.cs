using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DesktopDeck.Widgets;

/// <summary>Reading "Label | time zone" lines and describing other places' time. Pure logic (unit tested).</summary>
public static class WorldClocksLogic
{
    /// <summary>One line of the list. <see cref="Zone"/> is null when the time zone couldn't be found.</summary>
    public sealed record Place(string Label, TimeZoneInfo? Zone, string ZoneText);

    private static readonly Lazy<IReadOnlyList<TimeZoneInfo>> zones = new(() => TimeZoneInfo.GetSystemTimeZones());

    /// <summary>
    /// Reads "Label | zone". The zone may be a Windows id ("Tokyo Standard Time"), an IANA id ("Asia/Tokyo"),
    /// "Local", "UTC", or a city from Windows' zone list ("Tokyo"). Without a label, one is made from the zone.
    /// </summary>
    public static Place Parse(string line)
    {
        var parts = line.Split('|', 2).Select(p => p.Trim()).ToArray();
        string label = parts.Length == 2 ? parts[0] : "";
        string zoneText = parts.Length == 2 ? parts[1] : parts[0];
        var zone = Resolve(zoneText);
        if (label.Length == 0) label = DefaultLabel(zoneText, zone);
        return new Place(label, zone, zoneText);
    }

    public static TimeZoneInfo? Resolve(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return null;
        if (text.Equals("local", StringComparison.OrdinalIgnoreCase)) return TimeZoneInfo.Local;
        if (text.Equals("utc", StringComparison.OrdinalIgnoreCase) || text.Equals("gmt", StringComparison.OrdinalIgnoreCase)) return TimeZoneInfo.Utc;
        if (TimeZoneInfo.TryFindSystemTimeZoneById(text, out var found)) return found;
        if (text.Length < 3) return null;
        // A city named in a zone's display name: "(UTC+09:00) Osaka, Sapporo, Tokyo".
        foreach (var zone in zones.Value)
        {
            if (zone.StandardName.Equals(text, StringComparison.OrdinalIgnoreCase)) return zone;
            string name = zone.DisplayName;
            int close = name.IndexOf(')');
            if (close >= 0) name = name[(close + 1)..];
            if (name.Split(',').Any(city => city.Trim().Equals(text, StringComparison.OrdinalIgnoreCase))) return zone;
        }
        return null;
    }

    private static string DefaultLabel(string zoneText, TimeZoneInfo? zone)
    {
        if (zoneText.Contains('/')) return zoneText[(zoneText.LastIndexOf('/') + 1)..].Replace('_', ' ');
        if (zone is not null && zoneText.EndsWith(" Standard Time", StringComparison.OrdinalIgnoreCase)) return zoneText[..^" Standard Time".Length];
        if (zone == TimeZoneInfo.Local && zoneText.Equals("local", StringComparison.OrdinalIgnoreCase)) return "Here";
        return zoneText;
    }

    /// <summary>The time difference from here: "+7 h", "−4 h 30 m", or "Same time".</summary>
    public static string Relative(TimeSpan difference)
    {
        if (difference == TimeSpan.Zero) return "Same time";
        string sign = difference < TimeSpan.Zero ? "−" : "+";
        var d = difference.Duration();
        return d.Minutes == 0 ? $"{sign}{(int)d.TotalHours} h" : $"{sign}{(int)d.TotalHours} h {d.Minutes} m";
    }

    /// <summary>"UTC+9", "UTC−3:30", "UTC".</summary>
    public static string Utc(TimeSpan offset)
    {
        if (offset == TimeSpan.Zero) return "UTC";
        string sign = offset < TimeSpan.Zero ? "−" : "+";
        var d = offset.Duration();
        return d.Minutes == 0 ? $"UTC{sign}{(int)d.TotalHours}" : $"UTC{sign}{(int)d.TotalHours}:{d.Minutes:00}";
    }

    /// <summary>The place's offset from local time at <paramref name="utcNow"/>.</summary>
    public static TimeSpan Difference(TimeZoneInfo zone, DateTimeOffset utcNow, TimeZoneInfo local)
        => zone.GetUtcOffset(utcNow) - local.GetUtcOffset(utcNow);

    /// <summary>"Tomorrow", "Yesterday" or "Today", comparing the place's date with the local date.</summary>
    public static string DayWord(DateTime localNow, DateTime thereNow) => (thereNow.Date - localNow.Date).Days switch
    {
        > 0 => "Tomorrow",
        < 0 => "Yesterday",
        _ => "Today",
    };

    /// <summary>Daytime between <paramref name="dayStart"/> (inclusive) and <paramref name="dayEnd"/> (exclusive), in hours.</summary>
    public static bool IsDaytime(DateTime thereNow, double dayStart, double dayEnd)
    {
        double hour = thereNow.TimeOfDay.TotalHours;
        return dayStart <= dayEnd ? hour >= dayStart && hour < dayEnd : hour >= dayStart || hour < dayEnd;
    }

    /// <summary>"auto" follows the region's short-time pattern.</summary>
    public static bool Use24Hour(string choice, CultureInfo culture) => choice switch
    {
        "24" => true,
        "12" => false,
        _ => culture.DateTimeFormat.ShortTimePattern.Contains('H'),
    };

    /// <summary>The time and an AM/PM part ("" in 24-hour mode).</summary>
    public static (string Time, string Suffix) Format(DateTime time, bool use24, bool seconds)
    {
        if (use24) return (time.ToString(seconds ? "HH:mm:ss" : "HH:mm", CultureInfo.InvariantCulture), "");
        return (time.ToString(seconds ? "h:mm:ss" : "h:mm", CultureInfo.InvariantCulture), time.ToString("tt", CultureInfo.InvariantCulture));
    }
}
