using System;
using System.Collections.Generic;
using System.Linq;

namespace DesktopDeck.Widgets;

/// <summary>Grouping, ranking and CPU math for the Top apps widget. Pure logic (unit tested).</summary>
public static class TopAppsLogic
{
    /// <summary>One running process. Cpu is a percent (-1 when unknown); Path may be null for protected processes.</summary>
    public sealed record Proc(string Name, int Pid, long Memory, double Cpu, string? Path);

    /// <summary>One row: a process name with its instance count and summed memory and CPU.</summary>
    public sealed record App(string Name, int Count, long Memory, double Cpu, string? Path);

    /// <summary>Names hidden by default: kernel bookkeeping that isn't an app.</summary>
    public const string DefaultExclude = "Idle\nSystem\nRegistry\nMemory Compression\nSecure System";

    /// <summary>
    /// The top <paramref name="count"/> rows sorted by "memory" or "cpu", optionally grouping processes with the same name.
    /// Names in <paramref name="exclude"/> (case-insensitive, ".exe" optional) are left out.
    /// </summary>
    public static List<App> Top(IEnumerable<Proc> processes, bool group, string sortBy, int count, IEnumerable<string> exclude)
    {
        var skip = new HashSet<string>(exclude.Select(StripExe).Where(n => n.Length > 0), StringComparer.OrdinalIgnoreCase);
        var list = processes.Where(p => p.Name.Length > 0 && !skip.Contains(StripExe(p.Name)));
        IEnumerable<App> apps = group
            ? list.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Select(g => new App(
                g.First().Name, g.Count(), g.Sum(p => p.Memory), g.Any(p => p.Cpu >= 0) ? g.Where(p => p.Cpu >= 0).Sum(p => p.Cpu) : -1,
                g.Select(p => p.Path).FirstOrDefault(path => !string.IsNullOrEmpty(path))))
            : list.Select(p => new App(p.Name, 1, p.Memory, p.Cpu, p.Path));
        var sorted = sortBy == "cpu"
            ? apps.OrderByDescending(a => a.Cpu).ThenByDescending(a => a.Memory)
            : apps.OrderByDescending(a => a.Memory).ThenByDescending(a => a.Cpu);
        return sorted.ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase).Take(Math.Max(1, count)).ToList();
    }

    /// <summary>
    /// CPU percent from two process-time readings (100 ns units) taken <paramref name="wallSeconds"/> apart.
    /// "total" scales to all logical processors (like Task Manager); "core" makes one busy core 100%.
    /// </summary>
    public static double CpuPercent(long cpuBefore, long cpuAfter, double wallSeconds, int processors, string scale = "total")
    {
        if (wallSeconds <= 0 || cpuAfter < cpuBefore) return 0;
        double busy = (cpuAfter - cpuBefore) / 1e7 / wallSeconds * 100;
        double result = scale == "core" ? busy : busy / Math.Max(1, processors);
        return Math.Clamp(result, 0, scale == "core" ? 100 * Math.Max(1, processors) : 100);
    }

    /// <summary>"msedge.exe" → "msedge".</summary>
    public static string StripExe(string name)
    {
        string n = (name ?? "").Trim();
        return n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n[..^4] : n;
    }

    /// <summary>A friendlier display name: the file description when known, else the process name.</summary>
    public static string Display(string processName, string? description)
        => string.IsNullOrWhiteSpace(description) ? processName : description.Trim();

    /// <summary>Made-up processes for previews.</summary>
    public static List<Proc> FakeProcesses()
    {
        const long MB = 1024 * 1024;
        return
        [
            new("msedge", 1, 612 * MB, 3.2, null), new("msedge", 2, 418 * MB, 1.1, null), new("msedge", 3, 233 * MB, 0.4, null),
            new("Code", 4, 734 * MB, 6.8, null), new("Code", 5, 201 * MB, 0.9, null),
            new("Teams", 6, 512 * MB, 1.7, null), new("explorer", 7, 188 * MB, 0.6, null),
            new("Spotify", 8, 301 * MB, 2.4, null), new("DesktopDeck", 9, 142 * MB, 0.8, null),
            new("OneDrive", 10, 96 * MB, 0.1, null), new("dwm", 11, 154 * MB, 4.1, null),
            new("System", 4, 3 * MB, 0.5, null), new("Memory Compression", 12, 800 * MB, 0, null),
        ];
    }
}
