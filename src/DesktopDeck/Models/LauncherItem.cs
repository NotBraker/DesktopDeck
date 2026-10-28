namespace DesktopDeck.Models;

/// <summary>Element kinds stored in <see cref="LauncherItem.Kind"/>. Older settings omit Kind and load as launchers.</summary>
public static class ItemKinds
{
    public const string Launcher = "launcher";
    public const string Image = "image";
    public const string Tasks = "tasks";
    public const string Habits = "habits";
    public const string Counter = "counter";
    public const string Clock = "clock";
    public const string Timer = "timer";
    public const string Schedule = "schedule";
    public const string Goals = "goals";
    public const string Note = "note";
    public const string Countdown = "countdown";
    public const string Media = "media";
    public const string Folder = "folder";
    public const string Weather = "weather";
    /// <summary>A card that holds other widgets as tabs (see <see cref="WidgetOrganizer"/>). Its children point at it with <see cref="LauncherItem.GroupId"/>.</summary>
    public const string Group = "group";

    public static readonly string[] All = [Launcher, Image, Tasks, Habits, Counter, Clock, Timer, Schedule, Goals, Note, Countdown, Media, Folder, Weather, Group];
    public static readonly string[] CardStyles = ["solid", "glass", "frosted", "outline", "none"];

    /// <summary>Sizes for plug-in widgets (see Widgets/IWidget.cs), set when the widget registry loads.</summary>
    public static Func<string, (double Width, double Height)?>? PluginDefaultSize, PluginMinimumSize;

    /// <summary>A plug-in widget kind: 2–40 characters of a–z, 0–9 and "-".</summary>
    public static bool IsPluginId(string? kind) =>
        kind is { Length: >= 2 and <= 40 } && kind.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');

    // Sizes are n * 24 - 12 so items tile a 24 DIP grid with a 12 DIP gap.
    public static (double Width, double Height) DefaultSize(string kind) => kind switch
    {
        Image => (276, 180),
        Tasks => (276, 276),
        Habits => (372, 228),
        Counter => (180, 132),
        Clock => (372, 156),
        Timer => (276, 324),
        Schedule => (756, 564),
        Goals => (372, 324),
        Note => (276, 228),
        Countdown => (276, 156),
        Media => (372, 156),
        Folder => (324, 324),
        Weather => (372, 228),
        Group => (372, 324),
        _ when PluginDefaultSize?.Invoke(kind) is { } size => size,
        _ => (180, 84),
    };

    public static (double Width, double Height) MinimumSize(string kind) => kind switch
    {
        Image => (60, 60),
        Tasks => (180, 132),
        Habits => (276, 108),
        Counter => (132, 108),
        Clock => (180, 84),
        Timer => (228, 252),
        Schedule => (276, 228),
        Goals => (228, 180),
        Note => (132, 84),
        Countdown => (156, 84),
        Media => (228, 84),
        Folder => (180, 132),
        Weather => (180, 108),
        Group => (228, 156),
        _ when PluginMinimumSize?.Invoke(kind) is { } size => size,
        _ => (60, 60),
    };
}

/// <summary>One element on the deck. The class keeps its original name so existing settings files deserialize unchanged.</summary>
public sealed class LauncherItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = ItemKinds.Launcher;
    public string Name { get; set; } = "New launcher";
    public string Target { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public string Accent { get; set; } = "#C4684F";
    /// <summary>Normalized position (fraction of the deck width/height).</summary>
    public double X { get; set; }
    public double Y { get; set; }
    /// <summary>Size in DIP. Zero means the default for the kind.</summary>
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Locked { get; set; }
    /// <summary>Custom launcher icon, copied into the deck's media folder.</summary>
    public string IconPath { get; set; } = "";
    /// <summary>Picture shown by image elements, copied into the deck's media folder.</summary>
    public string ImagePath { get; set; } = "";
    /// <summary>"fill" crops to the frame; "fit" letterboxes the whole picture.</summary>
    public string ImageFit { get; set; } = "fill";
    public long Count { get; set; }
    public int Step { get; set; } = 1;
    public List<TaskEntry> Tasks { get; set; } = new();
    public List<HabitEntry> Habits { get; set; } = new();
    /// <summary>Task lists: clear completed tasks when a new day starts (unfinished ones carry over).</summary>
    public bool DailyReset { get; set; }
    /// <summary>Task lists: the local date (yyyy-MM-dd) the list was last rolled over.</summary>
    public string LastDate { get; set; } = "";
    /// <summary>Habits: "today" or "week". Schedule: "auto", "week" or "day".</summary>
    public string View { get; set; } = "";
    public bool ShowSeconds { get; set; } = true;
    public bool Use24Hour { get; set; }
    public PomodoroState Pomodoro { get; set; } = new();
    public List<ScheduleBlock> Blocks { get; set; } = new();
    public List<WeeklyGoal> Goals { get; set; } = new();
    /// <summary>Identifies data brought in from another app, so importing twice adds nothing twice.</summary>
    public string SourceId { get; set; } = "";

    // ---------- Appearance (all widgets) ----------
    /// <summary>"solid", "glass" (see-through), "frosted" (blurred background behind a light tint), "outline" or "none".</summary>
    public string CardStyle { get; set; } = "solid";
    /// <summary>Show the small uppercase title on framed widgets.</summary>
    public bool ShowHeader { get; set; } = true;
    /// <summary>Widgets with the same non-empty LinkId share their content (list, timer, notes…); each keeps its own look, size and place. Empty = not linked.</summary>
    public string LinkId { get; set; } = "";
    /// <summary>Keep this widget's own appearance even when the deck-wide look is set to apply to every widget (AppearanceSettings.ApplyToAll).</summary>
    public bool OwnLook { get; set; }

    // ---------- Per-widget options ----------
    public bool ShowDate { get; set; } = true;
    public bool TimerSound { get; set; } = true;
    /// <summary>Timer: start the next session automatically when one ends.</summary>
    public bool AutoNext { get; set; }
    /// <summary>Schedule: first and last hour shown; -1 means fit the blocks automatically.</summary>
    public int StartHour { get; set; } = -1;
    public int EndHour { get; set; } = -1;
    public bool HideWeekends { get; set; }
    /// <summary>Schedule: show events from the connected Google account (see Settings → Connected accounts).</summary>
    public bool ShowGoogle { get; set; } = true;
    /// <summary>Schedule: which Google calendars to show, by id. Empty means every calendar that is switched on in Settings.</summary>
    public List<string> GoogleCalendarIds { get; set; } = new();
    /// <summary>Schedule: show events from the read-only iCal links.</summary>
    public bool ShowIcs { get; set; } = true;
    /// <summary>Schedule: how Google and iCal events are drawn: "filled" or "outline".</summary>
    public string ExternalStyle { get; set; } = "filled";
    /// <summary>Schedule: a strip of all-day events under the day headers.</summary>
    public bool ShowAllDay { get; set; } = true;
    /// <summary>Tasks: hide ticked-off tasks.</summary>
    public bool HideCompleted { get; set; }
    /// <summary>Tasks: keep this list in step with a Google Tasks list (two-way). Needs the Tasks permission (Settings → Connected accounts).</summary>
    public bool GoogleTasksSync { get; set; }
    /// <summary>Tasks: the Google Tasks list id; "@default" is the account's main list, "My Tasks".</summary>
    public string GoogleTaskListId { get; set; } = "@default";
    /// <summary>Tasks: the name of that list (for the picker and the tooltip).</summary>
    public string GoogleTaskListName { get; set; } = "My Tasks";
    /// <summary>Tasks: the newest "updated" time Google reported, so the next sync only asks for what changed since. Null = ask for everything.</summary>
    public DateTimeOffset? GoogleTasksWatermark { get; set; }
    /// <summary>Tasks: Google tasks deleted here that Google has not been told about yet (sent on the next sync).</summary>
    public List<GoogleTaskTombstone> GoogleTaskDeletes { get; set; } = new();
    /// <summary>Counter: a goal to count toward (0 = none), shown as "5 / 8".</summary>
    public long CountGoal { get; set; }

    /// <summary>Which monitor the item is on (see Displays). Empty means the primary monitor.</summary>
    public string Display { get; set; } = "";
    /// <summary>Float above other windows, in place, while the deck is the desktop background.</summary>
    public bool OnTop { get; set; }
    /// <summary>Not shown anywhere right now (neither on a deck nor floating); Settings or the tray brings it back.</summary>
    public bool Hidden { get; set; }
    /// <summary>Floating widgets (<see cref="OnTop"/>): the monitor the widget floats on (empty = the primary monitor).</summary>
    public string FloatDisplay { get; set; } = "";
    /// <summary>Floating widgets: top-left corner as a fraction (0–1) of that monitor's work area.</summary>
    public double FloatX { get; set; }
    public double FloatY { get; set; }
    /// <summary>Floating widgets: size in DIP; zero means the same size as on the deck (<see cref="Width"/>, <see cref="Height"/>, or the kind's default).</summary>
    public double FloatWidth { get; set; }
    public double FloatHeight { get; set; }
    /// <summary>Floating widgets: fade to <see cref="FloatIdleOpacity"/> after a few seconds without the pointer over it; it comes back to full as soon as the pointer returns.</summary>
    public bool FloatIdleFade { get; set; }
    /// <summary>Floating widgets: how see-through the widget gets when idle, in percent (10–90). Used only with <see cref="FloatIdleFade"/>.</summary>
    public double FloatIdleOpacity { get; set; } = FloatBehavior.DefaultIdleOpacity;
    /// <summary>Floating widgets: clicks go to the window underneath. Holding Ctrl with the pointer over the widget makes it usable again until the pointer leaves.</summary>
    public bool FloatClickThrough { get; set; }
    /// <summary>Read only to migrate v0.4 pop-out windows to <see cref="OnTop"/>; always null after loading.</summary>
    public PopOut? PopOut { get; set; }

    // ---------- Sticky note ----------
    public string Text { get; set; } = "";

    // ---------- Countdown ----------
    /// <summary>yyyy-MM-dd.</summary>
    public string TargetDate { get; set; } = "";
    /// <summary>HH:mm, or empty for the start of the day.</summary>
    public string TargetTime { get; set; } = "";
    public bool RepeatYearly { get; set; }

    // ---------- Weather ----------
    public string Location { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    /// <summary>"F" or "C".</summary>
    public string Units { get; set; } = "";
    /// <summary>The last forecast response, so the widget shows something immediately and offline.</summary>
    public string WeatherJson { get; set; } = "";
    public DateTimeOffset? WeatherFetched { get; set; }

    /// <summary>The id of the group card this widget is a tab of; empty = on the deck by itself. Grouped widgets are drawn only inside their group.</summary>
    public string GroupId { get; set; } = "";

    /// <summary>Plug-in widget settings (see Widgets/IWidget.cs), as text keyed by option name.</summary>
    public Dictionary<string, string> Options { get; set; } = new(StringComparer.Ordinal);

    /// <summary>A deep copy, so editors can work on a draft without touching the live item.</summary>
    public LauncherItem Copy()
    {
        var copy = (LauncherItem)MemberwiseClone();
        copy.Tasks = Tasks.Select(t => t.Copy()).ToList();
        copy.GoogleTaskDeletes = GoogleTaskDeletes.Select(d => new GoogleTaskTombstone { ListId = d.ListId, TaskId = d.TaskId }).ToList();
        copy.Habits = Habits.Select(h => new HabitEntry { Id = h.Id, Name = h.Name, Color = h.Color, SourceId = h.SourceId, Days = h.Days.ToList() }).ToList();
        copy.Pomodoro = Pomodoro.Copy();
        copy.Blocks = Blocks.Select(b => b.Copy()).ToList();
        copy.GoogleCalendarIds = GoogleCalendarIds.ToList();
        copy.Goals = Goals.Select(g => g.Copy()).ToList();
        copy.PopOut = PopOut is null ? null : PopOut with { };
        copy.Options = new Dictionary<string, string>(Options, StringComparer.Ordinal);
        return copy;
    }
}

public sealed class TaskEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public bool Done { get; set; }
    public string SourceId { get; set; } = "";

    // ---------- Google Tasks sync (all empty/null for a task that only lives on this PC) ----------
    /// <summary>The Google task this one is linked to; empty until it has been created there.</summary>
    public string GoogleId { get; set; } = "";
    /// <summary>The Google list the linked task lives in.</summary>
    public string GoogleListId { get; set; } = "";
    public string GoogleEtag { get; set; } = "";
    /// <summary>Google's "updated" time for the task as of the last sync.</summary>
    public DateTimeOffset? GoogleUpdated { get; set; }
    /// <summary>When it was last changed here and not yet sent to Google (UTC). Null = nothing waiting. Last change wins by comparing this with Google's updated time.</summary>
    public DateTimeOffset? Modified { get; set; }
    /// <summary>A finished task cleared locally (midnight or "Clear completed") while it stays on Google. It comes back only if Google reopens it.</summary>
    public bool Hidden { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsLinked => GoogleId.Length > 0;

    public TaskEntry Copy() => (TaskEntry)MemberwiseClone();
}

/// <summary>A linked task deleted on this PC; the next sync deletes it on Google too.</summary>
public sealed class GoogleTaskTombstone
{
    public string ListId { get; set; } = "";
    public string TaskId { get; set; } = "";
}

public sealed class HabitEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
    public string SourceId { get; set; } = "";
    /// <summary>Completed local dates as yyyy-MM-dd.</summary>
    public List<string> Days { get; set; } = new();
}


/// <summary>v0.4: where a popped-out widget's own window sat, in screen DIP, and whether it stays above other windows.</summary>
public sealed record PopOut
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool OnTop { get; set; } = true;
}

/// <summary>Pomodoro configuration and run state. A running timer stores its end time, so it survives sleep and restarts without drifting.</summary>
public sealed class PomodoroState
{
    public int WorkMinutes { get; set; } = 25;
    public int BreakMinutes { get; set; } = 5;
    public int LongBreakMinutes { get; set; } = 20;
    public int LongBreakEvery { get; set; } = 4;
    /// <summary>"focus", "break" or "long".</summary>
    public string Mode { get; set; } = "focus";
    /// <summary>When the running session ends; null when stopped or paused.</summary>
    public DateTimeOffset? EndsAt { get; set; }
    /// <summary>Seconds left while paused; null when the current session has not been started.</summary>
    public int? PausedSeconds { get; set; }
    /// <summary>Focus sessions completed since the last long break.</summary>
    public int Cycle { get; set; }
    /// <summary>Completed focus sessions per local date.</summary>
    public Dictionary<string, int> Log { get; set; } = new();

    public PomodoroState Copy()
    {
        var copy = (PomodoroState)MemberwiseClone();
        copy.Log = new Dictionary<string, int>(Log);
        return copy;
    }
}

/// <summary>A recurring (or one-off) block on the weekly schedule.</summary>
public sealed class ScheduleBlock
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Color { get; set; } = "#C4684F";
    /// <summary>Mon, Tue, Wed, Thu, Fri, Sat, Sun.</summary>
    public List<string> Days { get; set; } = new();
    /// <summary>One-off date (yyyy-MM-dd). When set, Days is ignored.</summary>
    public string Date { get; set; } = "";
    public string Start { get; set; } = "09:00";
    public string End { get; set; } = "10:00";
    public List<string> CancelledDates { get; set; } = new();
    public List<ScheduleEvent> Events { get; set; } = new();
    public string SourceId { get; set; } = "";

    public ScheduleBlock Copy()
    {
        var copy = (ScheduleBlock)MemberwiseClone();
        copy.Days = Days.ToList();
        copy.CancelledDates = CancelledDates.ToList();
        copy.Events = Events.Select(e => new ScheduleEvent { Id = e.Id, Title = e.Title, Start = e.Start, End = e.End, Color = e.Color }).ToList();
        return copy;
    }
}

/// <summary>A named part inside a block, such as one class period inside "School".</summary>
public sealed class ScheduleEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Start { get; set; } = "";
    public string End { get; set; } = "";
    public string Color { get; set; } = "";
}

public sealed class WeeklyGoal
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public bool Done { get; set; }
    /// <summary>Monday of the goal's week (yyyy-MM-dd).</summary>
    public string Week { get; set; } = "";
    public List<Milestone> Milestones { get; set; } = new();
    public string SourceId { get; set; } = "";

    public WeeklyGoal Copy()
    {
        var copy = (WeeklyGoal)MemberwiseClone();
        copy.Milestones = Milestones.Select(m => new Milestone { Id = m.Id, Text = m.Text, Done = m.Done }).ToList();
        return copy;
    }
}

public sealed class Milestone
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public bool Done { get; set; }
}

public sealed class DeckSettings
{
    public const int CurrentSchema = 5;
    /// <summary>Files written before version 2 have no value here, so the default means "legacy".</summary>
    public int SchemaVersion { get; set; } = 1;
    public List<LauncherItem> Items { get; set; } = new();
    public bool StartAtLogin { get; set; } = true;
    public bool SnapToGrid { get; set; } = true;
    /// <summary>When on, nothing may enter the central 60% x 60% of the deck.</summary>
    public bool KeepCenterClear { get; set; }
    /// <summary>The grid cell size in DIP (8-64, default 24). Positions and sizes snap to it.</summary>
    public int GridSize { get; set; } = 24;
    /// <summary>The space kept between widgets in DIP (0-32, default 12).</summary>
    public int GridGap { get; set; } = 12;
    public bool ShowTitle { get; set; } = true;
    /// <summary>Ctrl+Alt+D brings the deck in front as an app window.</summary>
    public bool AppHotkey { get; set; } = true;
    /// <summary>
    /// Settings → "Show advanced features": off is the simple view (fewer menu entries, tabs and buttons; nothing is removed). The class default is
    /// true so every settings file that already exists, which has no value for this, keeps showing everything; a fresh install turns it off
    /// in <see cref="StartSimple"/>.
    /// </summary>
    public bool AdvancedFeatures { get; set; } = true;
    /// <summary>A fresh install (the first-run welcome) starts in the simple view.</summary>
    public void StartSimple() => AdvancedFeatures = false;
    /// <summary>
    /// Settings → Display → "Show desktop icons": Windows' own icons, wallpaper and desktop menu stay usable everywhere except under a widget. The class default is
    /// false so every settings file that already exists, which has no value for this, behaves as before (the deck covers the desktop);
    /// a fresh install turns it on in <see cref="StartWithDesktopIcons"/>.
    /// </summary>
    public bool ShowDesktopIcons { get; set; }
    /// <summary>A fresh install (the first-run welcome) keeps the desktop icons visible.</summary>
    public void StartWithDesktopIcons() => ShowDesktopIcons = true;
    /// <summary>The connected Google account's email (shown in Settings). The tokens live encrypted in secrets.json.</summary>
    public string GoogleEmail { get; set; } = "";
    /// <summary>The connected account's calendars and which are switched on.</summary>
    public List<GoogleCalendarChoice> GoogleCalendars { get; set; } = new();
    /// <summary>The account's Google Tasks lists, for the picker in a Today's Tasks widget's Customize window.</summary>
    public List<GoogleTaskListChoice> GoogleTaskLists { get; set; } = new();
    /// <summary>Read-only iCal links (labels and colors; the addresses are secrets).</summary>
    public List<IcsCalendarInfo> IcsCalendars { get; set; } = new();
    /// <summary>"Hide floating widgets": the floating layer is switched off until it is shown again.</summary>
    public bool FloatsHidden { get; set; }
    /// <summary>The hint "Hold Ctrl to use this widget" has been shown once (the first time a floating widget lets clicks through). Not part of undo.</summary>
    public bool ClickThroughHintShown { get; set; }
    /// <summary>The deck-wide look: default card style and the deck background per display (see AppearanceSettings).</summary>
    public AppearanceSettings Appearance { get; set; } = new();
    /// <summary>The color theme, accent and font (see ThemeSettings), and the saved widget looks (see StylePresets).</summary>
    public ThemeSettings Theme { get; set; } = new();
    public StylePresetSettings StylePresets { get; set; } = new();

    // ---------- Layouts (see LayoutEngine) ----------
    /// <summary>Saved arrangements of the floating widgets and of the background. Each layer always has at least one.</summary>
    public List<LayoutProfile> Layouts { get; set; } = new();
    /// <summary>The id of the floating layout in use; changes you make are written into it automatically.</summary>
    public string ActiveFloatingLayout { get; set; } = "";
    /// <summary>The id of the background layout in use.</summary>
    public string ActiveBackgroundLayout { get; set; } = "";

    // ---------- The control button (the "orb") ----------
    /// <summary>Show the small floating button that opens the control center.</summary>
    public bool ShowOrb { get; set; } = true;
    /// <summary>The monitor the button is on (empty = the primary monitor).</summary>
    public string OrbDisplay { get; set; } = "";
    /// <summary>Its top-left corner as a fraction (0–1) of that monitor's work area. The default is near the bottom-right corner.</summary>
    public double OrbX { get; set; } = DefaultOrbX;
    public double OrbY { get; set; } = DefaultOrbY;
    public const double DefaultOrbX = 0.965, DefaultOrbY = 0.9;
    /// <summary>Ctrl+Alt+1…9 switch the floating layouts (off by default: on layouts where Alt Gr types characters they would take those keys away).</summary>
    public bool LayoutHotkeys { get; set; }
    /// <summary>Personal reminders and how they notify (see ReminderService).</summary>
    public ReminderSettings Reminders { get; set; } = new();
    /// <summary>Automatic layout switching rules and the fullscreen option (see AutomationRules).</summary>
    public AutomationSettings Automation { get; set; } = new();
    /// <summary>The recycle bin: removed widgets, restorable for 30 days (see TrashBin).</summary>
    public List<TrashEntry> Trash { get; set; } = new();
    /// <summary>How many days of each history to keep, per kind of history (see HistorySettings and HistoryKeys).</summary>
    public HistorySettings History { get; set; } = new();

    // ---------- The widget gallery (see WidgetOrganizer) ----------
    /// <summary>Widget kinds starred in the "+ Add" gallery.</summary>
    public List<string> FavoriteWidgets { get; set; } = new();
    /// <summary>The kinds most recently added from the gallery, newest first (at most <see cref="WidgetOrganizer.MaxRecent"/>).</summary>
    public List<string> RecentWidgets { get; set; } = new();
}

