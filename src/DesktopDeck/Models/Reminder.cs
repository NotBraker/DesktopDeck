using System.Text.Json.Serialization;

namespace DesktopDeck.Models;

/// <summary>How a reminder repeats.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RepeatKind
{
    /// <summary>Rings once.</summary>
    None,
    Daily,
    /// <summary>Monday to Friday.</summary>
    Weekdays,
    /// <summary>On the chosen days of the week (<see cref="ReminderRepeat.Days"/>).</summary>
    Weekly,
    /// <summary>On one day of the month (<see cref="ReminderRepeat.DayOfMonth"/>); short months use their last day.</summary>
    Monthly,
    /// <summary>Every N minutes, hours, days or weeks (<see cref="ReminderRepeat.Every"/> and <see cref="ReminderRepeat.Unit"/>).</summary>
    Interval,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IntervalUnit { Minutes, Hours, Days, Weeks }

/// <summary>Whether the last ring of a reminder still waits for the user (Done or Snooze).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReminderAttention
{
    None,
    /// <summary>It rang on time and was not marked done yet.</summary>
    Rang,
    /// <summary>It was due while the app was asleep, closed or in quiet hours.</summary>
    Missed,
}

/// <summary>The repeat rule of a reminder. Times of day are kept here so a time that was skipped by daylight saving comes back the next day.</summary>
public sealed class ReminderRepeat
{
    public RepeatKind Kind { get; set; }
    /// <summary>Weekly: the days it rings on.</summary>
    public List<DayOfWeek> Days { get; set; } = new();
    /// <summary>Interval: how many units between two rings.</summary>
    public int Every { get; set; } = 1;
    public IntervalUnit Unit { get; set; } = IntervalUnit.Hours;
    /// <summary>Monthly: the day of the month it was meant for (31 stays 31 even in a month that has only 30 days).</summary>
    public int DayOfMonth { get; set; }
    /// <summary>The local time of day of every ring (daily, weekdays, weekly, monthly and day or week intervals).</summary>
    public TimeSpan? Time { get; set; }

    [JsonIgnore]
    public bool IsRepeating => Kind != RepeatKind.None;

    public ReminderRepeat Copy() => new() { Kind = Kind, Days = [.. Days], Every = Every, Unit = Unit, DayOfMonth = DayOfMonth, Time = Time };
}

/// <summary>
/// One personal reminder, app-wide (not tied to a widget). Times are exact instants with the local offset they were made in, so a
/// repeated hour at the end of daylight saving time is never confused with the first one.
/// </summary>
public sealed class Reminder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public string Note { get; set; } = "";
    /// <summary>When it rings next (or rang, for a one-off that is waiting for the user).</summary>
    public DateTimeOffset Due { get; set; }
    public ReminderRepeat Repeat { get; set; } = new();
    public DateTimeOffset Created { get; set; }
    /// <summary>The occurrence that rang last (so one occurrence can never ring twice).</summary>
    public DateTimeOffset? LastFired { get; set; }
    /// <summary>Snoozed: it rings again then (a repeating reminder still rings at its next regular time too).</summary>
    public DateTimeOffset? SnoozedUntil { get; set; }
    /// <summary>A one-off that was finished. Repeating reminders are never done: marking one done skips to its next occurrence.</summary>
    public bool Done { get; set; }
    public DateTimeOffset? DoneAt { get; set; }
    public ReminderAttention Attention { get; set; }

    [JsonIgnore]
    public bool IsRepeating => Repeat.IsRepeating;

    public Reminder Copy()
    {
        var copy = (Reminder)MemberwiseClone();
        copy.Repeat = Repeat.Copy();
        return copy;
    }
}
