using System.Globalization;

namespace DesktopDeck.Models;

/// <summary>Date, schedule, task, timer and goal rules. Pure and UI-free so they can be tested headlessly.</summary>
public static class Planner
{
    public static readonly string[] DayNames = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    public static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);

    /// <summary>Weeks start on Monday.</summary>
    public static DateOnly Monday(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    public static string DayName(DateOnly day) => DayNames[((int)day.DayOfWeek + 6) % 7];

    public static string Key(DateOnly day) => Trackers.Key(day);

    /// <summary>Minutes after midnight for "HH:mm"; null if the text is not a time.</summary>
    public static int? Minutes(string? time)
    {
        if (string.IsNullOrWhiteSpace(time)) return null;
        var parts = time.Trim().Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out int h) || !int.TryParse(parts[1], out int m)) return null;
        if (h < 0 || h > 24 || m < 0 || m > 59 || (h == 24 && m != 0)) return null;
        return h * 60 + m;
    }

    /// <summary>Accepts "17:30", "5:30 pm", "5pm", "530pm"; returns "HH:mm" or null.</summary>
    public static string? ParseTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string t = text.Trim().ToLowerInvariant().Replace(" ", "").Replace(".", "");
        bool pm = t.EndsWith("pm") || t.EndsWith("p"), am = t.EndsWith("am") || t.EndsWith("a");
        t = t.TrimEnd('m').TrimEnd('a', 'p');
        int h, m = 0;
        if (t.Contains(':'))
        {
            var parts = t.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[0], out h) || !int.TryParse(parts[1], out m)) return null;
        }
        else if (t.Length is 3 or 4 && int.TryParse(t, out int packed)) { h = packed / 100; m = packed % 100; }
        else if (!int.TryParse(t, out h)) return null;
        if (pm || am)
        {
            if (h < 1 || h > 12) return null;
            h = h % 12 + (pm ? 12 : 0);
        }
        if (h < 0 || h > 23 || m < 0 || m > 59) return null;
        return $"{h:00}:{m:00}";
    }

    public static string Format(string time, bool use24Hour)
    {
        if (Minutes(time) is not { } total) return time;
        if (use24Hour) return $"{total / 60:00}:{total % 60:00}";
        int h = total / 60 % 24, m = total % 60;
        return $"{(h % 12 == 0 ? 12 : h % 12)}:{m:00} {(h < 12 ? "AM" : "PM")}";
    }

    /// <summary>The blocks that happen on a date, earliest first.</summary>
    public static List<ScheduleBlock> Occurrences(IEnumerable<ScheduleBlock> blocks, DateOnly day)
    {
        string key = Key(day), name = DayName(day);
        return blocks
            .Where(b => Minutes(b.Start) is not null && Minutes(b.End) is not null)
            .Where(b => string.IsNullOrEmpty(b.Date) ? b.Days.Contains(name) && !b.CancelledDates.Contains(key) : b.Date == key)
            .OrderBy(b => Minutes(b.Start)).ThenBy(b => Minutes(b.End))
            .ToList();
    }

    /// <summary>Starts a new day for a task list: completed tasks are cleared (if enabled) and unfinished ones stay. Returns true if anything changed.</summary>
    public static bool RollOver(LauncherItem list, DateOnly today)
    {
        string key = Key(today);
        if (list.LastDate == key) return false;
        bool firstRun = string.IsNullOrEmpty(list.LastDate);
        list.LastDate = key;
        if (list.DailyReset && !firstRun) ClearCompleted(list);
        if (list.GoogleTasksSync) list.Tasks.RemoveAll(t => t.Hidden && t.Done && t.GoogleUpdated is { } at && DateOnly.FromDateTime(at.LocalDateTime) < today.AddDays(-HiddenKeptDays));
        return true;
    }

    /// <summary>How long a finished task that is hidden here (but still on Google) is remembered before it is forgotten locally.</summary>
    public const int HiddenKeptDays = 7;

    /// <summary>
    /// Clears finished tasks from a list. A task linked to Google Tasks is only hidden here (it stays on Google, and a
    /// sync does not bring it back); every other finished task is removed. Returns how many were cleared.
    /// </summary>
    public static int ClearCompleted(LauncherItem list)
    {
        int cleared = 0;
        foreach (var task in list.Tasks.ToList())
        {
            if (!task.Done || task.Hidden) continue;
            cleared++;
            if (list.GoogleTasksSync && task.IsLinked) task.Hidden = true;
            else list.Tasks.Remove(task);
        }
        return cleared;
    }

    /// <summary>The tasks a Today's Tasks widget shows (hidden ones are left out).</summary>
    public static List<TaskEntry> VisibleTasks(LauncherItem list) => list.Tasks.Where(t => !t.Hidden).ToList();

    // ---------- Pomodoro ----------

    public static int SessionSeconds(PomodoroState p, string mode) => 60 * Math.Max(1, mode switch
    {
        "break" => p.BreakMinutes,
        "long" => p.LongBreakMinutes,
        _ => p.WorkMinutes,
    });

    public static bool IsRunning(PomodoroState p) => p.EndsAt is not null;

    /// <summary>Seconds left in the current session, computed from the clock rather than counted down.</summary>
    public static int Remaining(PomodoroState p, DateTimeOffset now)
    {
        if (p.EndsAt is { } end) return Math.Max(0, (int)Math.Ceiling((end - now).TotalSeconds));
        return p.PausedSeconds ?? SessionSeconds(p, p.Mode);
    }

    public static void Start(PomodoroState p, DateTimeOffset now)
    {
        if (p.EndsAt is not null) return;
        p.EndsAt = now.AddSeconds(p.PausedSeconds ?? SessionSeconds(p, p.Mode));
        p.PausedSeconds = null;
    }

    public static void Pause(PomodoroState p, DateTimeOffset now)
    {
        if (p.EndsAt is null) return;
        p.PausedSeconds = Remaining(p, now);
        p.EndsAt = null;
    }

    public static void Reset(PomodoroState p) { p.EndsAt = null; p.PausedSeconds = null; }

    public static void SetMode(PomodoroState p, string mode) { p.Mode = mode is "break" or "long" ? mode : "focus"; Reset(p); }

    /// <summary>
    /// Ends the current session and moves to the next mode (stopped). A finished focus session is logged.
    /// Returns the mode that just finished.
    /// </summary>
    public static string Advance(PomodoroState p, DateOnly today, bool completed, int keepDays = 400)
    {
        string finished = p.Mode;
        if (finished == "focus")
        {
            if (completed)
            {
                p.Log[Key(today)] = p.Log.GetValueOrDefault(Key(today)) + 1;
                // Keep the days chosen in Settings → History (400 by default; 0 keeps all).
                string cutoff = Key(today.AddDays(-keepDays));
                if (keepDays > 0) foreach (var old in p.Log.Keys.Where(k => string.CompareOrdinal(k, cutoff) < 0).ToList()) p.Log.Remove(old);
            }
            p.Cycle++;
            p.Mode = p.Cycle >= Math.Max(1, p.LongBreakEvery) ? "long" : "break";
            if (p.Mode == "long") p.Cycle = 0;
        }
        else p.Mode = "focus";
        Reset(p);
        return finished;
    }

    /// <summary>True once a running session has reached zero.</summary>
    public static bool IsFinished(PomodoroState p, DateTimeOffset now) => p.EndsAt is { } end && end <= now;

    public static int SessionsOn(PomodoroState p, DateOnly day) => p.Log.GetValueOrDefault(Key(day));

    // ---------- Weekly goals ----------

    public static IEnumerable<WeeklyGoal> GoalsFor(LauncherItem item, DateOnly monday) =>
        item.Goals.Where(g => g.Week == Key(monday));

    /// <summary>0..1. Milestones count when present; otherwise the goal itself.</summary>
    public static double Progress(WeeklyGoal goal)
    {
        if (goal.Done) return 1;
        if (goal.Milestones.Count == 0) return 0;
        return (double)goal.Milestones.Count(m => m.Done) / goal.Milestones.Count;
    }

    /// <summary>Copies last week's unfinished goals into this week. Returns how many were added.</summary>
    public static int CarryOver(LauncherItem item, DateOnly thisMonday)
    {
        var previous = GoalsFor(item, thisMonday.AddDays(-7)).Where(g => !g.Done).ToList();
        var existing = new HashSet<string>(GoalsFor(item, thisMonday).Select(g => g.Text), StringComparer.CurrentCultureIgnoreCase);
        int added = 0;
        foreach (var goal in previous)
        {
            if (!existing.Add(goal.Text)) continue;
            var copy = goal.Copy();
            copy.Id = Guid.NewGuid().ToString("N");
            copy.Week = Key(thisMonday);
            copy.SourceId = "";
            item.Goals.Add(copy);
            added++;
        }
        return added;
    }

    public static string WeekRange(DateOnly monday) =>
        monday.ToString("MMM d", CultureInfo.CurrentCulture) + " – " +
        monday.AddDays(6).ToString(monday.Month == monday.AddDays(6).Month ? "%d" : "MMM d", CultureInfo.CurrentCulture);
}
