using System.IO;
using System.Text.Json;
using DesktopDeck.Models;

namespace DesktopDeck.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly string _path;
    private readonly object _gate = new();

    // A custom location is useful for portable tests without changing the user's real settings.
    public SettingsStore(string? directory = null) => _path = Path.Combine(Path.GetFullPath(directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopDeck")), "settings.json");
    public string FilePath => _path;
    /// <summary>Copies of user-picked icons and pictures live here so moving the originals cannot break the deck.</summary>
    public string MediaDirectory => Path.Combine(Path.GetDirectoryName(_path)!, "media");
    public string? LastLoadWarning { get; private set; }
    /// <summary>Raised after settings were written: the settings and the JSON that was saved (the undo history watches this). Not raised if the save failed.</summary>
    public event Action<DeckSettings, string>? Saved;

    /// <summary>The settings as they are saved.</summary>
    public static string Serialize(DeckSettings settings) => JsonSerializer.Serialize(settings, JsonOptions);

    /// <summary>Reads settings from JSON text the way a load does (older files are upgraded, gaps repaired, and the result checked).</summary>
    public static DeckSettings Parse(string json)
    {
        var settings = JsonSerializer.Deserialize<DeckSettings>(json, JsonOptions) ?? throw new InvalidDataException("Settings were empty.");
        Normalize(settings);
        Validate(settings);
        return settings;
    }

    public DeckSettings Load()
    {
        lock (_gate)
        {
            LastLoadWarning = null;
            if (!File.Exists(_path) && !File.Exists(_path + ".bak")) return new DeckSettings();
            Exception? failure = null;
            if (File.Exists(_path))
            {
                try { return Read(_path); }
                catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or UnauthorizedAccessException) { failure = ex; }
            }
            if (File.Exists(_path + ".bak"))
            {
                try
                {
                    var recovered = Read(_path + ".bak");
                    LastLoadWarning = "The saved settings could not be read. The previous backup was recovered; your original file has been preserved.";
                    return recovered;
                }
                catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    throw new InvalidDataException($"Neither the settings file nor its backup could be read. Files were left untouched at {_path}.", new AggregateException(failure ?? new FileNotFoundException(_path), ex));
                }
            }
            throw new InvalidDataException($"The settings file could not be read and has been preserved at {_path}.", failure);
        }
    }

    public void Save(DeckSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Normalize(settings);
        // The active layouts mirror what is on screen, so every save point (drags, hiding, background changes, adding) keeps them current.
        LayoutEngine.CaptureActive(settings);
        Validate(settings);
        string json = JsonSerializer.Serialize(settings, JsonOptions);
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(new System.Text.UTF8Encoding(false).GetBytes(json));
                    stream.Flush(flushToDisk: true);
                }
                if (File.Exists(_path))
                {
                    // Preserve corrupt input separately instead of replacing a good recovery backup with it.
                    bool readable;
                    try { Read(_path); readable = true; }
                    catch (Exception ex) when (ex is JsonException or InvalidDataException) { readable = false; }
                    // The first save of a day keeps the readable file as it was (the day's snapshot); a snapshot problem never fails a save.
                    if (readable) { try { DailySnapshots.TakeIfDue(_path, DateOnly.FromDateTime(DateTime.Now), settings.History.DaysFor(HistoryKeys.Snapshots)); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }
                    string backup = readable ? _path + ".bak" : _path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N");
                    File.Replace(temporary, _path, backup, ignoreMetadataErrors: false);
                }
                else File.Move(temporary, _path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        Saved?.Invoke(settings, json);
    }

    private static DeckSettings Read(string path)
    {
        using var stream = File.OpenRead(path);
        var settings = JsonSerializer.Deserialize<DeckSettings>(stream, JsonOptions) ?? throw new InvalidDataException("Settings were empty.");
        Normalize(settings);
        Validate(settings);
        return settings;
    }

    /// <summary>Fills fields added after version 1 so older files load unchanged. Required fields are left for Validate.</summary>
    private static void Normalize(DeckSettings settings)
    {
        if (settings.Items is null) return;
        foreach (var item in settings.Items)
        {
            if (item is null) continue;
            // Built-in kinds, plus plug-in widget kinds (lowercase ids); anything else loads as a launcher.
            string kind = item.Kind?.Trim().ToLowerInvariant() ?? "";
            item.Kind = ItemKinds.All.Contains(kind) || ItemKinds.IsPluginId(kind) ? kind : ItemKinds.Launcher;
            item.Options = item.Options is null ? new() : new Dictionary<string, string>(item.Options.Where(o => o.Key is not null && o.Value is not null), StringComparer.Ordinal);
            item.IconPath ??= "";
            item.ImagePath ??= "";
            item.ImageFit = item.ImageFit == "fit" ? "fit" : "fill";
            if (!double.IsFinite(item.Width) || item.Width < 0) item.Width = 0;
            if (!double.IsFinite(item.Height) || item.Height < 0) item.Height = 0;
            if (item.Step < 1) item.Step = 1;
            item.Tasks = (item.Tasks ?? new()).Where(t => t is not null).ToList();
            foreach (var task in item.Tasks) { task.Id = string.IsNullOrWhiteSpace(task.Id) ? Guid.NewGuid().ToString("N") : task.Id; task.Text ??= ""; }
            item.Habits = (item.Habits ?? new()).Where(h => h is not null).ToList();
            foreach (var habit in item.Habits)
            {
                habit.Id = string.IsNullOrWhiteSpace(habit.Id) ? Guid.NewGuid().ToString("N") : habit.Id;
                habit.Name ??= "";
                habit.Color ??= "";
                habit.SourceId ??= "";
                habit.Days = (habit.Days ?? new()).Where(d => d is not null).Distinct(StringComparer.Ordinal).ToList();
            }
            foreach (var task in item.Tasks)
            {
                task.SourceId ??= "";
                task.GoogleId ??= "";
                task.GoogleListId ??= "";
                task.GoogleEtag ??= "";
                // A task without a Google id has nothing to be hidden from, and a hidden task must be finished.
                if (task.GoogleId.Length == 0) { task.GoogleListId = ""; task.GoogleEtag = ""; task.GoogleUpdated = null; task.Hidden = false; }
                if (!task.Done) task.Hidden = false;
            }
            item.GoogleTaskListId = string.IsNullOrWhiteSpace(item.GoogleTaskListId) ? "@default" : item.GoogleTaskListId.Trim();
            item.GoogleTaskListName = string.IsNullOrWhiteSpace(item.GoogleTaskListName) ? (item.GoogleTaskListId == "@default" ? "My Tasks" : item.GoogleTaskListId) : item.GoogleTaskListName.Trim();
            item.GoogleTaskDeletes = (item.GoogleTaskDeletes ?? new()).Where(d => d is not null && !string.IsNullOrWhiteSpace(d.TaskId)).GroupBy(d => (d.ListId ?? "") + "|" + d.TaskId, StringComparer.Ordinal).Select(g => g.First()).ToList();
            foreach (var d in item.GoogleTaskDeletes) d.ListId = string.IsNullOrWhiteSpace(d.ListId) ? "@default" : d.ListId;
            item.LastDate ??= "";
            item.View ??= "";
            item.SourceId ??= "";
            if (!ItemKinds.CardStyles.Contains(item.CardStyle)) item.CardStyle = "solid";
            if (item.StartHour is < -1 or > 23) item.StartHour = -1;
            if (item.EndHour is < -1 or > 24) item.EndHour = -1;
            if (item.StartHour >= 0 && item.EndHour >= 0 && item.EndHour <= item.StartHour) item.EndHour = -1;
            if (item.CountGoal < 0) item.CountGoal = 0;
            item.ExternalStyle = item.ExternalStyle is "outline" ? "outline" : "filled";
            item.GoogleCalendarIds = (item.GoogleCalendarIds ?? new()).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList();
            // v0.4 pop-out windows became "keep on top" in place.
            if (item.PopOut is { } pop) { item.OnTop |= pop.OnTop; item.PopOut = null; }
            item.Display ??= "";
            item.FloatDisplay ??= "";
            item.FloatX = double.IsFinite(item.FloatX) ? Math.Clamp(item.FloatX, 0, 1) : 0;
            item.FloatY = double.IsFinite(item.FloatY) ? Math.Clamp(item.FloatY, 0, 1) : 0;
            item.FloatWidth = double.IsFinite(item.FloatWidth) && item.FloatWidth > 0 ? item.FloatWidth : 0;
            item.FloatHeight = double.IsFinite(item.FloatHeight) && item.FloatHeight > 0 ? item.FloatHeight : 0;
            item.FloatIdleOpacity = FloatBehavior.ClampIdleOpacity(item.FloatIdleOpacity);
            item.Text ??= "";
            item.TargetDate ??= "";
            item.TargetTime ??= "";
            item.Location ??= "";
            item.Units = item.Units is "F" or "C" ? item.Units : "";
            item.WeatherJson ??= "";
            if (!double.IsFinite(item.Latitude) || item.Latitude is < -90 or > 90) item.Latitude = 0;
            if (!double.IsFinite(item.Longitude) || item.Longitude is < -180 or > 180) item.Longitude = 0;
            item.Pomodoro ??= new();
            var p = item.Pomodoro;
            p.WorkMinutes = Math.Clamp(p.WorkMinutes, 1, 600);
            p.BreakMinutes = Math.Clamp(p.BreakMinutes, 1, 600);
            p.LongBreakMinutes = Math.Clamp(p.LongBreakMinutes, 1, 600);
            p.LongBreakEvery = Math.Clamp(p.LongBreakEvery, 1, 20);
            if (p.Mode is not ("focus" or "break" or "long")) p.Mode = "focus";
            if (p.PausedSeconds < 0) p.PausedSeconds = null;
            p.Log ??= new();
            item.Blocks = (item.Blocks ?? new()).Where(x => x is not null).ToList();
            foreach (var block in item.Blocks)
            {
                block.Id = string.IsNullOrWhiteSpace(block.Id) ? Guid.NewGuid().ToString("N") : block.Id;
                block.Title ??= "";
                block.Color = string.IsNullOrWhiteSpace(block.Color) ? "#C4684F" : block.Color;
                block.Days = (block.Days ?? new()).Where(d => d is not null).ToList();
                block.Date ??= "";
                block.Start ??= "09:00";
                block.End ??= "10:00";
                block.CancelledDates = (block.CancelledDates ?? new()).Where(d => d is not null).ToList();
                block.Events = (block.Events ?? new()).Where(e => e is not null).ToList();
                foreach (var e in block.Events) { e.Id ??= Guid.NewGuid().ToString("N"); e.Title ??= ""; e.Start ??= ""; e.End ??= ""; e.Color ??= ""; }
                block.SourceId ??= "";
            }
            item.Goals = (item.Goals ?? new()).Where(x => x is not null).ToList();
            foreach (var goal in item.Goals)
            {
                goal.Id = string.IsNullOrWhiteSpace(goal.Id) ? Guid.NewGuid().ToString("N") : goal.Id;
                goal.Text ??= "";
                goal.Week ??= "";
                goal.SourceId ??= "";
                goal.Milestones = (goal.Milestones ?? new()).Where(m => m is not null).ToList();
                foreach (var m in goal.Milestones) { m.Id ??= Guid.NewGuid().ToString("N"); m.Text ??= ""; }
            }
        }
        if (settings.SchemaVersion < 3)
        {
            // Version 3 adopted the dusk palette; items still on the old default mint follow it.
            foreach (var item in settings.Items)
                if (item is not null && string.Equals(item.Accent, "#A7DBC8", StringComparison.OrdinalIgnoreCase)) item.Accent = "#C4684F";
        }
        settings.GoogleEmail ??= "";
        settings.GoogleCalendars = (settings.GoogleCalendars ?? new()).Where(c => c is not null && !string.IsNullOrWhiteSpace(c.Id)).GroupBy(c => c.Id, StringComparer.Ordinal).Select(g => g.First()).ToList();
        foreach (var c in settings.GoogleCalendars)
        {
            c.Name = string.IsNullOrWhiteSpace(c.Name) ? c.Id : c.Name;
            c.Color = CalendarColors.Normalize(c.Color);
            c.AccessRole = c.AccessRole is "owner" or "writer" or "reader" or "freeBusyReader" ? c.AccessRole : "reader";
        }
        settings.GoogleTaskLists = (settings.GoogleTaskLists ?? new()).Where(l => l is not null && !string.IsNullOrWhiteSpace(l.Id)).GroupBy(l => l.Id, StringComparer.Ordinal).Select(g => g.First()).ToList();
        foreach (var l in settings.GoogleTaskLists) l.Title = string.IsNullOrWhiteSpace(l.Title) ? l.Id : l.Title;
        settings.IcsCalendars = (settings.IcsCalendars ?? new()).Where(c => c is not null).ToList();
        var icsIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in settings.IcsCalendars)
        {
            // The Id names the secret entry, so it must be present and unique.
            if (string.IsNullOrWhiteSpace(c.Id) || !icsIds.Add(c.Id)) { c.Id = Guid.NewGuid().ToString("N"); icsIds.Add(c.Id); }
            c.Label ??= "";
            c.Color = CalendarColors.Normalize(c.Color, "#14b8a6");
        }
        settings.Appearance = AppearanceSettings.Normalized(settings.Appearance);
        settings.History = HistorySettings.Normalized(settings.History);
        settings.Reminders = ReminderSettings.Normalized(settings.Reminders);
        settings.GridSize = Layout.DeckLayout.ClampGridSize(settings.GridSize);
        settings.GridGap = Layout.DeckLayout.ClampGap(settings.GridGap);
        settings.Theme = ThemeSettings.Normalized(settings.Theme);
        settings.StylePresets = StylePresetSettings.Normalized(settings.StylePresets);
        settings.OrbDisplay ??= "";
        settings.OrbX = double.IsFinite(settings.OrbX) ? Math.Clamp(settings.OrbX, 0, 1) : DeckSettings.DefaultOrbX;
        settings.OrbY = double.IsFinite(settings.OrbY) ? Math.Clamp(settings.OrbY, 0, 1) : DeckSettings.DefaultOrbY;
        WidgetOrganizer.NormalizeGroups(settings);
        WidgetLinks.Normalize(settings.Items);
        WidgetOrganizer.NormalizeLists(settings);
        LayoutEngine.EnsureDefaults(settings);
        settings.Automation = AutomationSettings.Normalized(settings.Automation);
        NormalizeTrash(settings);
        settings.SchemaVersion = DeckSettings.CurrentSchema;
    }

    /// <summary>Repairs the recycle bin: no empty entries, every removed widget repaired like a live one (the bin is emptied by TrashBin.Purge, not here).</summary>
    private static void NormalizeTrash(DeckSettings settings)
    {
        settings.Trash = (settings.Trash ?? new()).Where(t => t?.Item is not null).ToList();
        if (settings.Trash.Count == 0) return;
        foreach (var entry in settings.Trash)
        {
            if (string.IsNullOrWhiteSpace(entry.Id)) entry.Id = Guid.NewGuid().ToString("N");
            entry.Layer = LayoutLayers.IsValid(entry.Layer) ? entry.Layer : (entry.Item.OnTop ? LayoutLayers.Floating : LayoutLayers.Background);
            entry.Display ??= "";
            entry.LayoutEntries = new Dictionary<string, LayoutEntry>((entry.LayoutEntries ?? new()).Where(e => e.Value is not null), StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(entry.Item.Id)) entry.Item.Id = Guid.NewGuid().ToString("N");
        }
        // Widgets in the bin get the same repairs as live ones.
        Normalize(new DeckSettings { SchemaVersion = DeckSettings.CurrentSchema, Items = settings.Trash.Select(t => t.Item).ToList() });
    }

    private static void Validate(DeckSettings settings)
    {
        if (settings.Items is null) throw new InvalidDataException("Settings must contain an item list.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in settings.Items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id)) throw new InvalidDataException("Launcher item IDs must be present and unique.");
            if (item.Target is null || item.Name is null || item.Arguments is null || item.WorkingDirectory is null || item.Accent is null) throw new InvalidDataException("Launcher item text fields must not be null.");
            if (!double.IsFinite(item.X) || !double.IsFinite(item.Y)) throw new InvalidDataException("Launcher positions must be finite numbers.");
        }
    }
}
