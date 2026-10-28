using DesktopDeck.Models;

namespace DesktopDeck.Services;

/// <summary>The reminders sorted for the lists: what needs an answer, then today, tomorrow, later, repeating and done.</summary>
public sealed record ReminderGroups(List<Reminder> Missed, List<Reminder> Today, List<Reminder> Tomorrow, List<Reminder> Later, List<Reminder> Repeating, List<Reminder> Done)
{
    public int Upcoming => Today.Count + Tomorrow.Count + Later.Count + Repeating.Count;
    public bool IsEmpty => Missed.Count + Upcoming + Done.Count == 0;

    /// <summary>
    /// Missed: rang or missed and not answered yet (one-off or repeating). Today, Tomorrow, Later: one-off reminders by when they ring next
    /// (a snooze counts). Repeating: every repeating reminder, by its next time. Done: finished one-offs, newest first.
    /// </summary>
    public static ReminderGroups Build(IEnumerable<Reminder> items, DateTimeOffset now, TimeZoneInfo zone)
    {
        var today = ReminderSchedule.Wall(now, zone).Date;
        var all = items.ToList();
        static DateTimeOffset Key(Reminder r) => ReminderService.RingAt(r) ?? r.Due;
        var missed = all.Where(r => !r.Done && r.Attention != ReminderAttention.None).OrderBy(r => r.LastFired ?? r.Due).ToList();
        var pending = all.Where(r => !r.Done && !r.IsRepeating && r.Attention == ReminderAttention.None).OrderBy(Key).ToList();
        int DayOffset(Reminder r) => (int)(ReminderSchedule.Wall(Key(r), zone).Date - today).TotalDays;
        return new ReminderGroups(
            missed,
            pending.Where(r => DayOffset(r) <= 0).ToList(),
            pending.Where(r => DayOffset(r) == 1).ToList(),
            pending.Where(r => DayOffset(r) >= 2).ToList(),
            all.Where(r => !r.Done && r.IsRepeating).OrderBy(Key).ToList(),
            all.Where(r => r.Done).OrderByDescending(r => r.DoneAt ?? r.Due).ToList());
    }
}

/// <summary>Made-up reminders for smoke renders and the widget's samples (never the user's own).</summary>
public static class ReminderSamples
{
    public static List<Reminder> Make(DateTimeOffset now, TimeZoneInfo zone)
    {
        var wall = ReminderSchedule.Wall(now, zone);
        DateTimeOffset At(int days, int hour, int minute = 0) => ReminderSchedule.At(wall.Date.AddDays(days).AddHours(hour).AddMinutes(minute), zone);
        var weekly = new ReminderRepeat { Kind = RepeatKind.Weekly, Days = [DayOfWeek.Monday], Time = new TimeSpan(7, 30, 0) };
        var daily = new ReminderRepeat { Kind = RepeatKind.Daily, Time = new TimeSpan(21, 0, 0) };
        var water = new ReminderRepeat { Kind = RepeatKind.Interval, Unit = IntervalUnit.Hours, Every = 2 };
        var monthly = new ReminderRepeat { Kind = RepeatKind.Monthly, DayOfMonth = 1, Time = new TimeSpan(9, 0, 0) };
        var list = new List<Reminder>
        {
            new() { Id = "s1", Text = "Take out the trash", Due = ReminderSchedule.First(weekly, weekly.Time!.Value, now, zone)!.Value, Repeat = weekly, Created = now.AddDays(-20), LastFired = now.AddDays(-3) },
            new() { Id = "s2", Text = "Submit lab report", Note = "Upload to the school portal", Due = now.AddMinutes(-25), Created = now.AddDays(-2), LastFired = now.AddMinutes(-25), Attention = ReminderAttention.Missed },
            new() { Id = "s3", Text = "Call Mom", Due = now.AddMinutes(42), Created = now.AddHours(-3) },
            new() { Id = "s4", Text = "Stretch", Due = now.AddMinutes(20), Created = now.AddMinutes(-5) },
            new() { Id = "s5", Text = "Finish chemistry homework", Due = At(0, 20), Created = now.AddDays(-1) },
            new() { Id = "s6", Text = "Soccer practice bag", Due = At(1, 7, 30), Created = now.AddHours(-6) },
            new() { Id = "s7", Text = "Dentist", Note = "Main Street Dental", Due = At(9, 9, 30), Created = now.AddDays(-12) },
            new() { Id = "s8", Text = "Drink water", Due = now.AddMinutes(75), Repeat = water, Created = now.AddDays(-4) },
            new() { Id = "s9", Text = "Practice piano", Due = At(0, 21) > now ? At(0, 21) : At(1, 21), Repeat = daily, Created = now.AddDays(-9) },
            new() { Id = "s10", Text = "Pay allowance money", Due = ReminderSchedule.First(monthly, monthly.Time!.Value, now, zone)!.Value, Repeat = monthly, Created = now.AddDays(-40) },
            new() { Id = "s11", Text = "Return library books", Due = now.AddHours(-30), Created = now.AddDays(-5), Done = true, DoneAt = now.AddHours(-29) },
        };
        return list;
    }
}
