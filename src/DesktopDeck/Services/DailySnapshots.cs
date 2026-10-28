using System.Globalization;
using System.Text.RegularExpressions;

namespace DesktopDeck.Services;

/// <summary>A saved copy of the settings from one day.</summary>
public sealed record SnapshotInfo(DateOnly Day, string Path, long Bytes);

/// <summary>
/// Daily snapshots: the first time settings are saved on a day, the settings file as it was (the end of the previous session) is copied to
/// <c>&lt;data folder&gt;/snapshots/settings-YYYY-MM-DD.json</c>; the last <see cref="Keep"/> days are kept. Only settings.json is ever copied, never secrets.json.
/// Restoring first keeps a copy of the current settings in the same folder (<c>before-restore-*.json</c>, the last <see cref="KeepRestoreBackups"/>).
/// </summary>
public static class DailySnapshots
{
    public const int Keep = 14;
    public const int KeepRestoreBackups = 5;
    public const string FolderName = "snapshots";

    private static readonly Regex DayFile = new(@"^settings-(\d{4}-\d{2}-\d{2})\.json$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex RestoreFile = new(@"^before-restore-\d{8}-\d{6}\.json$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string FolderFor(string settingsPath) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsPath))!, FolderName);

    public static string PathFor(string settingsPath, DateOnly day) => Path.Combine(FolderFor(settingsPath), "settings-" + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".json");

    /// <summary>Copies the settings file to today's snapshot unless there already is one, then drops the oldest beyond <paramref name="keep"/> (0 = keep all). Returns the new file, or null.</summary>
    public static string? TakeIfDue(string settingsPath, DateOnly today, int keep = Keep)
    {
        string target = PathFor(settingsPath, today);
        if (!File.Exists(settingsPath) || File.Exists(target)) return null;
        Directory.CreateDirectory(FolderFor(settingsPath));
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(settingsPath, temporary);
            File.Move(temporary, target);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        if (keep > 0) Rotate(settingsPath, keep);
        return target;
    }

    /// <summary>The snapshots there are, newest day first. Files that don't match the naming are ignored.</summary>
    public static IReadOnlyList<SnapshotInfo> List(string settingsPath)
    {
        string folder = FolderFor(settingsPath);
        if (!Directory.Exists(folder)) return [];
        var found = new List<SnapshotInfo>();
        foreach (var path in Directory.EnumerateFiles(folder))
        {
            var match = DayFile.Match(System.IO.Path.GetFileName(path));
            if (!match.Success || !DateOnly.TryParseExact(match.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) continue;
            found.Add(new SnapshotInfo(day, path, new FileInfo(path).Length));
        }
        return found.OrderByDescending(s => s.Day).ToList();
    }

    /// <summary>Deletes the oldest daily snapshots beyond <paramref name="keep"/>, going by the date in the file name.</summary>
    public static void Rotate(string settingsPath, int keep = Keep)
    {
        if (keep <= 0) return;
        foreach (var old in List(settingsPath).Skip(Math.Max(0, keep)))
        {
            try { File.Delete(old.Path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Copies the current settings file aside before a restore replaces the state. Returns the copy, or null when there is no settings file.</summary>
    public static string? BackupBeforeRestore(string settingsPath, DateTime now)
    {
        if (!File.Exists(settingsPath)) return null;
        string folder = FolderFor(settingsPath);
        Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, "before-restore-" + now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".json");
        File.Copy(settingsPath, target, overwrite: true);
        foreach (var old in Directory.EnumerateFiles(folder).Where(p => RestoreFile.IsMatch(System.IO.Path.GetFileName(p))).OrderByDescending(p => p, StringComparer.Ordinal).Skip(KeepRestoreBackups))
        {
            try { File.Delete(old); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return target;
    }

    /// <summary>The text of every snapshot file (daily and before-restore), so media they mention is not deleted.</summary>
    public static IEnumerable<string> Texts(string settingsPath)
    {
        string folder = FolderFor(settingsPath);
        if (!Directory.Exists(folder)) yield break;
        foreach (var path in Directory.EnumerateFiles(folder, "*.json"))
        {
            string name = System.IO.Path.GetFileName(path);
            if (!DayFile.IsMatch(name) && !RestoreFile.IsMatch(name)) continue;
            string text;
            try { text = File.ReadAllText(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            yield return text;
        }
    }
}

/// <summary>
/// Deleting copies of pictures and icons (the <c>media</c> folder) that nothing refers to any more. "Refers to" is decided by looking for the file name in text
/// the caller supplies: the live settings, the recycle bin, every state undo can reach and the daily snapshots.
/// </summary>
public static class MediaSweeper
{
    /// <summary>Files younger than this are left alone: they may have just been imported for something not saved yet.</summary>
    public static readonly TimeSpan DefaultMinimumAge = TimeSpan.FromMinutes(10);

    public static bool IsReferenced(string fileName, IEnumerable<string> texts) => texts.Any(t => t.Contains(fileName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Deletes every file of the media folder that no text mentions and that is at least <paramref name="minimumAge"/> old. Returns how many were deleted.</summary>
    public static int Sweep(string mediaDirectory, IReadOnlyCollection<string> texts, DateTime nowUtc, TimeSpan minimumAge)
    {
        if (!Directory.Exists(mediaDirectory)) return 0;
        int deleted = 0;
        foreach (var path in Directory.EnumerateFiles(mediaDirectory))
        {
            try
            {
                if (nowUtc - new FileInfo(path).CreationTimeUtc < minimumAge || IsReferenced(System.IO.Path.GetFileName(path), texts)) continue;
                File.Delete(path);
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return deleted;
    }
}
