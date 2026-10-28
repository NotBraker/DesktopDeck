namespace DesktopDeck.Models;

/// <summary>
/// How many days of each history the deck keeps (<see cref="DeckSettings.History"/>). A key the user has not set uses its
/// built-in default from <see cref="HistoryKeys.Defaults"/>; 0 means keep forever.
/// </summary>
public sealed class HistorySettings
{
    /// <summary>How many days of history to keep per kind of history (see <see cref="HistoryKeys"/>). 0 = keep forever. A missing key = the built-in default.</summary>
    public Dictionary<string, int> Days { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The days to keep for one kind of history: the user's value when set and not negative, else the built-in default (0 for an unknown key).</summary>
    public int DaysFor(string key)
    {
        if (Days.TryGetValue(key, out int days) && days >= 0) return days;
        return HistoryKeys.Defaults.TryGetValue(key, out int fallback) ? fallback : 0;
    }

    /// <summary>Repairs a loaded (possibly hand-edited or null) history block: negative values and blank keys are dropped.</summary>
    public static HistorySettings Normalized(HistorySettings? value)
    {
        var history = value ?? new HistorySettings();
        var cleaned = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (key, days) in history.Days ?? new())
        {
            string name = (key ?? "").Trim();
            if (name.Length > 0 && days >= 0) cleaned[name] = days;
        }
        history.Days = cleaned;
        return history;
    }
}

/// <summary>The kinds of history the deck keeps, and the days each one keeps by default (what the app did before this setting existed).</summary>
public static class HistoryKeys
{
    /// <summary>Focus widget: the past days of focus statements (FocusLogic.HistoryLength, trimmed in FocusLogic.cs).</summary>
    public const string Focus = "focus";
    /// <summary>Journal widget: daily entries (JournalLogic.KeepDays, dropped when saving in JournalLogic.cs).</summary>
    public const string Journal = "journal";
    /// <summary>Hydration widget: the daily water log (HydrationLogic.KeepDays, dropped when formatting the log in HydrationLogic.cs).</summary>
    public const string Hydration = "hydration";
    /// <summary>Bedtime widget: the daily sleep ratings (BedtimeLogic.KeepDays, dropped when formatting the ratings in BedtimeLogic.cs).</summary>
    public const string Bedtime = "bedtime";
    /// <summary>Study log widget: finished sessions. Not limited by days; the newest 3000 are kept (StudyLogLogic.MaxSessions in StudyLogLogic.cs).</summary>
    public const string StudyLog = "studylog";
    /// <summary>Homework widget: assignments, including completed ones. Not limited by days; completed ones stay until "Clear completed" (HomeworkWidget.cs), and the list holds at most 400 (HomeworkLogic.MaxItems).</summary>
    public const string Homework = "homework";
    /// <summary>Timer (pomodoro): the completed focus sessions per day (PomodoroState.Log, days older than 400 are removed in Planner.cs).</summary>
    public const string Timer = "timer";
    /// <summary>Habits: the days each habit was done (Trackers.cs, days older than 400 are removed).</summary>
    public const string Habits = "habits";
    /// <summary>Daily settings snapshots (DailySnapshots.Keep, DailySnapshots.cs).</summary>
    public const string Snapshots = "snapshots";
    /// <summary>The recycle bin: removed widgets (TrashBin.RetentionDays, TrashEntry.cs).</summary>
    public const string Trash = "trash";
    /// <summary>Game time widget: the daily log per game (GameTimeLogic.KeepDays, dropped when formatting the log in GameTimeLogic.cs).</summary>
    public const string GameTime = "gametime";

    /// <summary>The built-in days kept for each history key. A key missing here keeps 0 days (forever).</summary>
    public static readonly Dictionary<string, int> Defaults = new(StringComparer.Ordinal)
    {
        [Focus] = 7,
        [Journal] = 3 * 366,
        [Hydration] = 35,
        [Bedtime] = 35,
        [StudyLog] = 0,
        [Homework] = 0,
        [Timer] = 400,
        [Habits] = 400,
        [Snapshots] = 14,
        [Trash] = 30,
        [GameTime] = 60,
    };
}
