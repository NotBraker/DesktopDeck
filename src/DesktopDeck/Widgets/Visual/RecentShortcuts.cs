using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace DesktopDeck.Widgets;

/// <summary>
/// Reads Windows' "Recent" items: %APPDATA%\Microsoft\Windows\Recent\*.lnk, each resolved to the file or folder it
/// points to through a minimal [ComImport] IShellLinkW / IPersistFile (no packages, no scripting host). Meant for a
/// background thread: it touches the disk. Targets that no longer exist are skipped.
/// </summary>
internal static class RecentShortcuts
{
    internal static string RecentFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Recent");

    /// <summary>The newest recent items (at most <paramref name="maxShortcuts"/> shortcuts are looked at), newest first.</summary>
    internal static List<RecentFilesLogic.Entry> Read(int maxShortcuts, bool includeNetwork)
    {
        var entries = new List<RecentFilesLogic.Entry>();
        string folder = RecentFolder;
        if (!Directory.Exists(folder)) return entries;
        List<FileInfo> shortcuts;
        try
        {
            shortcuts = new DirectoryInfo(folder).EnumerateFiles("*.lnk").OrderByDescending(f => f.LastWriteTimeUtc).Take(Math.Max(1, maxShortcuts)).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return entries; }

        foreach (var shortcut in shortcuts)
        {
            string? target = Target(shortcut.FullName);
            if (string.IsNullOrEmpty(target)) continue;
            if (!includeNetwork && RecentFilesLogic.IsNetworkPath(target)) continue;
            try
            {
                bool isFolder = Directory.Exists(target);
                if (isFolder || File.Exists(target)) entries.Add(new RecentFilesLogic.Entry(target, shortcut.LastWriteTime, isFolder));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        return entries;
    }

    /// <summary>The path a .lnk points to, or null (shell-namespace links such as "This PC" have none).</summary>
    internal static string? Target(string shortcutPath)
    {
        object? link = null;
        try
        {
            link = new ShellLinkClass();
            ((IPersistFile)link).Load(shortcutPath, 0 /* STGM_READ */);
            var path = new StringBuilder(1024);
            int hr = ((IShellLinkW)link).GetPath(path, path.Capacity, IntPtr.Zero, 0);
            return hr == 0 && path.Length > 0 ? path.ToString() : null;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or IOException or UnauthorizedAccessException or ArgumentException) { return null; }
        finally
        {
            if (link is not null && Marshal.IsComObject(link)) Marshal.ReleaseComObject(link);
        }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLinkClass { }

    /// <summary>Only the first method after IUnknown is declared; it is all that is called.</summary>
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        [PreserveSig]
        int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxChars, IntPtr findData, uint flags);
    }
}
