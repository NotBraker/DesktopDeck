using System.IO;
using DesktopDeck.Models;

namespace DesktopDeck.Services;

public static class DesktopImporter
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".bat", ".cmd", ".lnk", ".url" };

    public static List<LauncherItem> Discover()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) continue;
            // Enumeration only: do not resolve shortcuts or launch scripts while importing.
            foreach (string path in Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly))
                if (Extensions.Contains(Path.GetExtension(path))) paths.Add(path);
        }
        return paths.OrderBy(Path.GetFileNameWithoutExtension, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select((path, index) => new LauncherItem
            {
                Name = Path.GetFileNameWithoutExtension(path),
                Target = path,
                WorkingDirectory = Path.GetDirectoryName(path) ?? "",
                X = 0.04 + (index % 4) * 0.23,
                Y = 0.05 + ((index / 4) % 5) * 0.18
            }).ToList();
    }
}
