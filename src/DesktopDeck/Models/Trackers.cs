using System.Globalization;

namespace DesktopDeck.Models;

/// <summary>Pure tracker rules, kept free of UI types so they can be tested headlessly.</summary>
public static class Trackers
{
    public static string Key(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static bool IsDone(HabitEntry habit, DateOnly day) => habit.Days.Contains(Key(day));

    /// <summary>Flips completion for one day and returns the new state.</summary>
    /// <remarks>Days older than <paramref name="keepDays"/> before <paramref name="day"/> are dropped (0 = keep all).</remarks>
    public static bool Toggle(HabitEntry habit, DateOnly day, int keepDays = 400)
    {
        string key = Key(day);
        if (habit.Days.Remove(key)) return false;
        habit.Days.Add(key);
        habit.Days.Sort(StringComparer.Ordinal);
        // Old history is trimmed to keep the file small (400 days by default, from Settings → History).
        string cutoff = Key(day.AddDays(-keepDays));
        if (keepDays > 0) habit.Days.RemoveAll(d => string.CompareOrdinal(d, cutoff) < 0);
        return true;
    }

    /// <summary>Consecutive completed days ending today, or ending yesterday if today is still open.</summary>
    public static int Streak(HabitEntry habit, DateOnly today)
    {
        var done = new HashSet<string>(habit.Days, StringComparer.Ordinal);
        var day = done.Contains(Key(today)) ? today : today.AddDays(-1);
        int streak = 0;
        while (done.Contains(Key(day))) { streak++; day = day.AddDays(-1); }
        return streak;
    }

    public static long Bump(LauncherItem counter, int direction)
    {
        long step = Math.Max(1, (long)counter.Step);
        counter.Count = direction >= 0
            ? (counter.Count > long.MaxValue - step ? long.MaxValue : counter.Count + step)
            : (counter.Count < long.MinValue + step ? long.MinValue : counter.Count - step);
        return counter.Count;
    }
}
