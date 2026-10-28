using System.Runtime.InteropServices;

namespace DesktopDeck.Services;

/// <summary>The newest things in a folder, for the Folder widget.</summary>
public static class FolderListing
{
    public sealed record Entry(string Name, string Path, bool IsFolder, DateTime Modified);

    /// <summary>Partial downloads and system clutter the widget never shows.</summary>
    private static readonly string[] Skipped = [".crdownload", ".part", ".partial", ".download", ".tmp", ".opdownload"];

    /// <summary>The user's Downloads folder (wherever it was moved to).</summary>
    public static string Downloads
    {
        get
        {
            try
            {
                var id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
                if (SHGetKnownFolderPath(ref id, 0, 0, out var path) == 0 && path != 0)
                {
                    try { return Marshal.PtrToStringUni(path) ?? Fallback(); }
                    finally { Marshal.FreeCoTaskMem(path); }
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
            return Fallback();
            static string Fallback() => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }

    /// <summary>Up to <paramref name="max"/> files and folders, newest first, skipping hidden, system and partial files.</summary>
    public static List<Entry> Recent(string folder, int max)
    {
        var list = new List<Entry>();
        if (max <= 0 || string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return list;
        try
        {
            var entries = new DirectoryInfo(folder).EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System });
            foreach (var info in entries.OrderByDescending(i => i.LastWriteTimeUtc))
            {
                if (info is FileInfo file && (Skipped.Contains(file.Extension, StringComparer.OrdinalIgnoreCase) || file.Name.StartsWith("~$", StringComparison.Ordinal) || file.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))) continue;
                list.Add(new Entry(info.Name, info.FullName, info is DirectoryInfo, info.LastWriteTime));
                if (list.Count >= max) break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        return list;
    }

    [DllImport("shell32.dll")] private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, nint token, out nint path);
}
