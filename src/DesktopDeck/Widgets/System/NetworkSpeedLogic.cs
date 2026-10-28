using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DesktopDeck.Widgets;

/// <summary>Speed math, units and adapter filtering for the Network widget. Pure logic (unit tested).</summary>
public static class NetworkSpeedLogic
{
    /// <summary>Bytes per second between two counter readings; a counter that went backwards (adapter reset) gives 0.</summary>
    public static double Rate(long before, long after, double seconds)
        => seconds <= 0 || after < before ? 0 : (after - before) / seconds;

    /// <summary>
    /// Splits a speed into number and unit. Units: "auto" (B/s, KB/s, MB/s, GB/s), "bits" (Kb/s, Mb/s, Gb/s),
    /// "KB" (always KB/s), "MB" (always MB/s), "Mbps" (always Mb/s).
    /// </summary>
    public static (string Value, string Unit) RateParts(double bytesPerSecond, string units, int decimals = -1, IFormatProvider? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        double b = Math.Max(0, double.IsFinite(bytesPerSecond) ? bytesPerSecond : 0);
        double value;
        string unit;
        switch (units)
        {
            case "KB": value = b / 1024; unit = "KB/s"; break;
            case "MB": value = b / (1024 * 1024); unit = "MB/s"; break;
            case "Mbps": value = b * 8 / 1_000_000; unit = "Mb/s"; break;
            case "bits":
                double bits = b * 8;
                (value, unit) = bits >= 1e9 ? (bits / 1e9, "Gb/s") : bits >= 1e6 ? (bits / 1e6, "Mb/s") : (bits / 1e3, "Kb/s");
                break;
            default:
                (value, unit) = b >= 1000L * 1024 * 1024 ? (b / (1024d * 1024 * 1024), "GB/s") : b >= 1000 * 1024 ? (b / (1024d * 1024), "MB/s") : b >= 1000 ? (b / 1024, "KB/s") : (b, "B/s");
                break;
        }
        int places = decimals >= 0 ? decimals : unit == "B/s" ? 0 : value < 10 ? 1 : 0;
        return (value.ToString("F" + places, culture), unit);
    }

    /// <summary>"12.4 MB/s".</summary>
    public static string FormatRate(double bytesPerSecond, string units, int decimals = -1, IFormatProvider? culture = null)
    {
        var (value, unit) = RateParts(bytesPerSecond, units, decimals, culture);
        return value + " " + unit;
    }

    private static readonly string[] VirtualHints = ["virtual", "vethernet", "hyper-v", "vmware", "virtualbox", "loopback", "pseudo", "tap-", "tunnel", "wan miniport", "teredo", "isatap", "npcap", "wsl", "docker", "vpn", "wireguard", "tailscale", "zerotier"];

    /// <summary>Virtual, VPN and tunnel adapters (counted twice otherwise, since their traffic also crosses the real adapter).</summary>
    public static bool IsVirtual(string name, string description)
    {
        string text = (name + " " + description).ToLowerInvariant();
        return VirtualHints.Any(text.Contains);
    }

    /// <summary>True when the adapter matches the chosen filter (empty or "all" = every adapter).</summary>
    public static bool Matches(string name, string description, string? filter)
    {
        string f = (filter ?? "").Trim();
        if (f.Length == 0 || f.Equals("all", StringComparison.OrdinalIgnoreCase)) return true;
        return name.Equals(f, StringComparison.OrdinalIgnoreCase)
            || name.Contains(f, StringComparison.OrdinalIgnoreCase)
            || description.Contains(f, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A round top for the speed graph so it doesn't jump with every sample (at least 8 KB/s).</summary>
    public static double GraphMax(IEnumerable<double> values)
    {
        double max = values.DefaultIfEmpty(0).Max();
        double floor = 8 * 1024;
        if (max <= floor) return floor;
        double magnitude = Math.Pow(2, Math.Ceiling(Math.Log2(max)));
        return magnitude * (max <= magnitude * 0.75 ? 0.75 : 1);
    }

    /// <summary>Made-up speeds (bytes/s) for previews.</summary>
    public static double[] FakeSpeeds(int count, double peak, int seed)
    {
        var values = new double[Math.Max(0, count)];
        for (int i = 0; i < values.Length; i++)
        {
            double burst = Math.Max(0, Math.Sin((i + seed) * 0.32)) * 0.75 + Math.Abs(Math.Sin((i + seed) * 1.7)) * 0.25;
            values[i] = peak * burst;
        }
        return values;
    }
}
