using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DesktopDeck.Widgets;

/// <summary>CPU and memory math and formatting shared by the System widgets. Pure logic (unit tested).</summary>
public static class SystemStatsLogic
{
    /// <summary>
    /// CPU busy percent (0–100) between two GetSystemTimes readings. Kernel time includes idle time, so
    /// busy = 1 − idle / (kernel + user). Counters that went backwards (or no time passed) give 0.
    /// </summary>
    public static double CpuPercent(long idle0, long kernel0, long user0, long idle1, long kernel1, long user1)
    {
        long idle = idle1 - idle0, total = (kernel1 - kernel0) + (user1 - user0);
        if (idle < 0 || total <= 0) return 0;
        return Math.Clamp((1 - (double)idle / total) * 100, 0, 100);
    }

    /// <summary>0 = normal, 1 = warning, 2 = critical (higher values are worse).</summary>
    public static int Level(double value, double warn, double critical)
    {
        if (!double.IsFinite(value)) return 0;
        if (critical < warn) (warn, critical) = (critical, warn);
        return value >= critical ? 2 : value >= warn ? 1 : 0;
    }

    /// <summary>Adds a sample and drops the oldest ones beyond <paramref name="max"/>.</summary>
    public static void Push(List<double> history, double value, int max)
    {
        history.Add(double.IsFinite(value) ? value : 0);
        int extra = history.Count - Math.Max(2, max);
        if (extra > 0) history.RemoveRange(0, extra);
    }

    /// <summary>The average of the last <paramref name="count"/> values (0 when empty).</summary>
    public static double Average(IReadOnlyList<double> values, int count)
    {
        if (values.Count == 0) return 0;
        int take = Math.Clamp(count, 1, values.Count);
        double sum = 0;
        for (int i = values.Count - take; i < values.Count; i++) sum += values[i];
        return sum / take;
    }

    /// <summary>"512 MB", "7.8 GB", "1.2 TB" (binary units, as Windows shows them). Fewer decimals for bigger numbers.</summary>
    public static string FormatBytes(double bytes, IFormatProvider? culture = null, int? decimals = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double value = Math.Max(0, double.IsFinite(bytes) ? bytes : 0);
        int unit = 0;
        while (value >= 1000 && unit < units.Length - 1) { value /= 1024; unit++; }
        int places = decimals ?? (unit == 0 ? 0 : value < 10 ? 1 : 0);
        return value.ToString("F" + places, culture) + " " + units[unit];
    }

    /// <summary>"used / total GB" with a shared unit, e.g. "7.8 / 15.9 GB".</summary>
    public static string UsedOfTotal(double used, double total, IFormatProvider? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        string totalText = FormatBytes(total, culture);
        int space = totalText.LastIndexOf(' ');
        string unit = space > 0 ? totalText[(space + 1)..] : "";
        double scale = unit switch { "KB" => 1024d, "MB" => 1024d * 1024, "GB" => 1024d * 1024 * 1024, "TB" => 1024d * 1024 * 1024 * 1024, "PB" => Math.Pow(1024, 5), _ => 1 };
        double usedScaled = used / scale;
        return usedScaled.ToString(usedScaled < 10 ? "F1" : "F0", culture) + " / " + totalText;
    }

    /// <summary>"23%" or "23.4%".</summary>
    public static string Percent(double value, int decimals = 0, IFormatProvider? culture = null)
        => (double.IsFinite(value) ? value : 0).ToString("F" + Math.Clamp(decimals, 0, 3), culture ?? CultureInfo.CurrentCulture) + "%";

    /// <summary>Reads "12,30.5,7" (invariant numbers, comma separated) into numbers; bad parts are skipped.</summary>
    public static double[] ParseSeries(string? text)
        => (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? (double?)v : null)
            .Where(v => v is not null).Select(v => v!.Value).ToArray();

    /// <summary>A gentle made-up series for previews: <paramref name="count"/> values around <paramref name="center"/>.</summary>
    public static double[] FakeSeries(int count, double center, double swing, int seed)
    {
        var values = new double[Math.Max(0, count)];
        for (int i = 0; i < values.Length; i++)
        {
            double wave = Math.Sin((i + seed) * 0.45) * 0.6 + Math.Sin((i + seed * 3) * 1.3) * 0.4;
            values[i] = Math.Max(0, center + wave * swing);
        }
        return values;
    }
}
