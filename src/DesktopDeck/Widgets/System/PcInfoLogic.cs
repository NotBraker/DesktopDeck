using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DesktopDeck.Widgets;

/// <summary>Text for the PC info widget: uptime, Windows name and a tidy CPU name. Pure logic (unit tested).</summary>
public static class PcInfoLogic
{
    /// <summary>Uptime as "short" ("3d 4h 12m"), "long" ("3 days, 4 hours") or "clock" ("76:12" hours:minutes).</summary>
    public static string Uptime(TimeSpan span, string style)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        int days = (int)span.TotalDays, hours = span.Hours, minutes = span.Minutes;
        switch (style)
        {
            case "clock":
                return $"{(int)span.TotalHours}:{minutes:00}";
            case "long":
                static string Unit(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";
                if (days > 0) return hours > 0 ? $"{Unit(days, "day")}, {Unit(hours, "hour")}" : Unit(days, "day");
                if (hours > 0) return minutes > 0 ? $"{Unit(hours, "hour")}, {Unit(minutes, "minute")}" : Unit(hours, "hour");
                return Unit(minutes, "minute");
            default:
                if (days > 0) return $"{days}d {hours}h {minutes}m";
                if (hours > 0) return $"{hours}h {minutes}m";
                return $"{minutes}m";
        }
    }

    /// <summary>
    /// Windows' registry still says "Windows 10" on Windows 11 (build 22000 and later); this fixes the name.
    /// </summary>
    public static string WindowsName(string? productName, int build)
    {
        string name = string.IsNullOrWhiteSpace(productName) ? "Windows" : productName.Trim();
        if (build >= 22000 && name.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase)) name = "Windows 11" + name[10..];
        return name;
    }

    /// <summary>"Windows 11 Pro 23H2".</summary>
    public static string WindowsVersion(string? productName, string? displayVersion, int build)
    {
        string name = WindowsName(productName, build);
        return string.IsNullOrWhiteSpace(displayVersion) ? name : name + " " + displayVersion.Trim();
    }

    /// <summary>"22631.4317" (or just the build when the update revision is unknown).</summary>
    public static string Build(int build, int revision)
        => revision > 0 ? build.ToString(CultureInfo.InvariantCulture) + "." + revision.ToString(CultureInfo.InvariantCulture) : build.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Tidies a processor name: collapses spaces, and when <paramref name="shorten"/> is set drops the (R)/(TM)
    /// marks, "CPU", the "@ 2.60GHz" clock and "with Radeon Graphics" style tails.
    /// </summary>
    public static string CpuName(string? raw, bool shorten)
    {
        string name = Regex.Replace((raw ?? "").Trim(), @"\s+", " ");
        if (name.Length == 0) return "Unknown processor";
        if (!shorten) return name;
        name = Regex.Replace(name, @"\((R|TM|tm|r)\)|®|™", "");
        name = Regex.Replace(name, @"\s*@\s*[\d.]+\s*[GM]Hz", "", RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"\s+with\s+.*$", "", RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"\s+\d+-Core Processor$", "", RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"\s+(CPU|Processor)\b", "", RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"\s+", " ").Trim();
        return name.Length == 0 ? "Unknown processor" : name;
    }

    /// <summary>"8 cores · 16 threads" or "16 threads" when the core count is unknown.</summary>
    public static string Cores(int cores, int threads)
        => cores > 0 && cores != threads ? $"{cores} cores · {threads} threads" : threads == 1 ? "1 thread" : $"{threads} threads";

    /// <summary>"3 screens".</summary>
    public static string Screens(int count) => count == 1 ? "1 screen" : $"{count} screens";
}
