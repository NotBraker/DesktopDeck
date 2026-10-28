using DesktopDeck.Models;

namespace DesktopDeck.Services;

/// <summary>How a reminder rang: on time, or late (the app was asleep or closed, or it was quiet).</summary>
public enum RingKind { Rang, Missed }

/// <summary>One reminder that rang during <see cref="ReminderService.Tick"/>.</summary>
/// <param name="Occurrence">The occurrence that rang (for several missed ones, the latest).</param>
/// <param name="Skipped">How many earlier occurrences of a repeating reminder were passed over in the same catch-up.</param>
public sealed record ReminderRing(Reminder Reminder, RingKind Kind, DateTimeOffset Occurrence, int Skipped);

/// <summary>Snooze choices of the toast and the lists.</summary>
public enum SnoozeChoice { Minutes5, Minutes10, Minutes30, Minutes60, Tomorrow }

/// <summary>
/// The reminders and when they ring. Pure scheduling with no clock, timer or window: callers pass "now" and a time zone, ask when to
/// wake up next (<see cref="NextWake"/>) and ring everything due (<see cref="Tick"/>). An occurrence rings exactly once: ringing moves the
/// reminder on (a repeating one to its next time, a one-off to "waiting for you") before anything is shown, so a second call finds nothing.
/// </summary>
public sealed class ReminderService
{
    /// <summary>A reminder that rings later than this after its time is "missed" rather than "rang".</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(2);

    private readonly ReminderSettings settings;
    private readonly TimeZoneInfo zone;

    public ReminderService(ReminderSettings settings, TimeZoneInfo? zone = null)
    {
        this.settings = settings;
        this.zone = zone ?? TimeZoneInfo.Local;
    }

    public TimeZoneInfo Zone => zone;
    public IReadOnlyList<Reminder> Items => settings.Items;
    public Reminder? Find(string id) => settings.Items.FirstOrDefault(r => r.Id == id);

    // ---------- Changing the list ----------

    /// <summary>Adds a reminder (a new id and creation time are set). Returns it.</summary>
    public Reminder Add(Reminder reminder, DateTimeOffset now)
    {
        reminder.Id = string.IsNullOrWhiteSpace(reminder.Id) || Find(reminder.Id) is not null ? Guid.NewGuid().ToString("N") : reminder.Id;
        reminder.Text = reminder.Text.Trim();
        reminder.Created = now;
        reminder.Done = false;
        reminder.DoneAt = null;
        reminder.Attention = ReminderAttention.None;
        reminder.SnoozedUntil = null;
        reminder.LastFired = null;
        settings.Items.Add(reminder);
        return reminder;
    }

    public bool Remove(string id) => settings.Items.RemoveAll(r => r.Id == id) > 0;

    /// <summary>Replaces the text, time and repeat of a reminder (an edit): it is waiting for its new time again.</summary>
    public bool Update(string id, string text, string note, DateTimeOffset due, ReminderRepeat repeat)
    {
        if (Find(id) is not { } r) return false;
        r.Text = text.Trim();
        r.Note = note.Trim();
        r.Due = due;
        r.Repeat = repeat.Copy();
        r.Done = false;
        r.DoneAt = null;
        r.Attention = ReminderAttention.None;
        r.SnoozedUntil = null;
        return true;
    }

    // ---------- When things ring ----------

    /// <summary>When a reminder rings next, or null when it is finished or waiting for the user.</summary>
    public static DateTimeOffset? RingAt(Reminder r)
    {
        if (r.Done) return null;
        if (!r.IsRepeating) return r.SnoozedUntil ?? (r.Attention == ReminderAttention.None ? r.Due : null);
        return r.SnoozedUntil is { } snooze && snooze < r.Due ? snooze : r.Due;
    }

    /// <summary>The soonest moment any reminder rings, or null when nothing is waiting.</summary>
    public DateTimeOffset? NextWake()
    {
        DateTimeOffset? soonest = null;
        foreach (var r in settings.Items)
            if (RingAt(r) is { } at && (soonest is null || at < soonest)) soonest = at;
        return soonest;
    }

    /// <summary>
    /// Rings everything due at <paramref name="now"/>. Each reminder rings once per call, however many occurrences it missed: a repeating
    /// one reports the latest and counts the others as skipped, then moves on to its next time after now.
    /// </summary>
    public List<ReminderRing> Tick(DateTimeOffset now)
    {
        var rings = new List<ReminderRing>();
        foreach (var r in settings.Items.ToList())
        {
            if (RingAt(r) is not { } at || at > now) continue;
            var occurrence = at;
            int skipped = 0;
            if (r.IsRepeating)
            {
                // Both a snooze and the regular time may be due: that is still one ring, for the latest of them.
                DateTimeOffset? viaSnooze = null, viaDue = null;
                if (r.SnoozedUntil is { } snooze && snooze <= now) { viaSnooze = snooze; r.SnoozedUntil = null; }
                if (r.Due <= now)
                {
                    var (latest, count, next) = PassOccurrences(r, now);
                    viaDue = latest;
                    skipped = Math.Max(0, count - 1);
                    r.Due = next;
                }
                occurrence = new[] { viaSnooze, viaDue }.Where(t => t is not null).Max()!.Value;
            }
            else r.SnoozedUntil = null;
            r.LastFired = occurrence;
            var kind = now - occurrence > Grace ? RingKind.Missed : RingKind.Rang;
            r.Attention = kind == RingKind.Missed ? ReminderAttention.Missed : ReminderAttention.Rang;
            rings.Add(new ReminderRing(r, kind, occurrence, skipped));
        }
        return rings;
    }

    /// <summary>Walks a repeating reminder past <paramref name="now"/>: the latest occurrence at or before now, how many there were, and the first after it.</summary>
    private (DateTimeOffset Latest, int Count, DateTimeOffset Next) PassOccurrences(Reminder r, DateTimeOffset now)
    {
        if (ReminderSchedule.Elapsed(r.Repeat) is { } span)
        {
            // Real elapsed time: jump straight there.
            long steps = (long)Math.Floor((now - r.Due) / span);
            var latest = ReminderSchedule.Normalize(r.Due + steps * span, zone);
            return (latest, (int)Math.Min(int.MaxValue, steps + 1), ReminderSchedule.Normalize(latest + span, zone));
        }
        var at = r.Due;
        var last = at;
        int count = 0;
        for (int guard = 0; at <= now && guard < 40_000; guard++)
        {
            last = at;
            count++;
            var next = ReminderSchedule.Next(r.Repeat, at, zone);
            if (next is null || next <= at) { next = ReminderSchedule.First(r.Repeat, r.Repeat.Time ?? ReminderSchedule.Wall(at, zone).TimeOfDay, now, zone) ?? at.AddDays(1); at = next.Value; break; }
            at = next.Value;
        }
        return (last, count, at);
    }

    // ---------- What the user does with a ring ----------

    /// <summary>
    /// Done. A one-off is finished. A repeating reminder that rang (or is snoozed) is just acknowledged; one that is only upcoming skips
    /// to its next occurrence.
    /// </summary>
    public bool Complete(string id, DateTimeOffset now)
    {
        if (Find(id) is not { } r) return false;
        if (!r.IsRepeating)
        {
            r.Done = true;
            r.DoneAt = now;
            r.Attention = ReminderAttention.None;
            r.SnoozedUntil = null;
            return true;
        }
        if (r.Attention != ReminderAttention.None || r.SnoozedUntil is not null)
        {
            r.Attention = ReminderAttention.None;
            r.SnoozedUntil = null;
            return true;
        }
        if (ReminderSchedule.Next(r.Repeat, r.Due, zone) is { } next) r.Due = next;
        return true;
    }

    /// <summary>Snooze: it rings again later. Never replaces the next regular occurrence of a repeating reminder.</summary>
    public bool Snooze(string id, SnoozeChoice choice, DateTimeOffset now)
    {
        if (Find(id) is not { } r || r.Done) return false;
        r.SnoozedUntil = SnoozeTime(choice, now);
        r.Attention = ReminderAttention.None;
        return true;
    }

    /// <summary>The moment a snooze rings: in 5, 10, 30 or 60 minutes, or tomorrow at the time set in the settings.</summary>
    public DateTimeOffset SnoozeTime(SnoozeChoice choice, DateTimeOffset now) => choice switch
    {
        SnoozeChoice.Minutes5 => now.AddMinutes(5),
        SnoozeChoice.Minutes10 => now.AddMinutes(10),
        SnoozeChoice.Minutes30 => now.AddMinutes(30),
        SnoozeChoice.Minutes60 => now.AddMinutes(60),
        _ => ReminderSchedule.At(ReminderSchedule.Wall(now, zone).Date.AddDays(1) + settings.TomorrowTimeOfDay, zone),
    };

    /// <summary>A ring that was held back (quiet hours) shows as missed.</summary>
    public bool MarkMissed(string id)
    {
        if (Find(id) is not { } r || r.Done) return false;
        r.Attention = ReminderAttention.Missed;
        return true;
    }

    /// <summary>Clears "rang / missed" without finishing anything (dismissing a toast does not call this: the reminder stays in the list until you answer it).</summary>
    public bool Acknowledge(string id)
    {
        if (Find(id) is not { } r) return false;
        r.Attention = ReminderAttention.None;
        return true;
    }

    // ---------- Quiet hours ----------

    /// <summary>True while "do not disturb" is on, or inside the daily quiet hours (which may cross midnight).</summary>
    public bool IsQuiet(DateTimeOffset now) => settings.DoNotDisturb || InQuietHours(settings, ReminderSchedule.Wall(now, zone).TimeOfDay);

    public static bool InQuietHours(ReminderSettings s, TimeSpan timeOfDay)
    {
        if (!s.QuietHours) return false;
        TimeSpan from = s.QuietStart, to = s.QuietEnd;
        if (from == to) return false;
        return from < to ? timeOfDay >= from && timeOfDay < to : timeOfDay >= from || timeOfDay < to;
    }

    /// <summary>When the current quiet hours end (null when it is not quiet or do-not-disturb has no end).</summary>
    public DateTimeOffset? QuietEndsAt(DateTimeOffset now)
    {
        if (settings.DoNotDisturb || !InQuietHours(settings, ReminderSchedule.Wall(now, zone).TimeOfDay)) return null;
        var wall = ReminderSchedule.Wall(now, zone);
        var end = wall.Date + settings.QuietEnd;
        if (end <= wall) end = end.AddDays(1);
        return ReminderSchedule.At(end, zone);
    }

    // ---------- Looking ahead ----------

    /// <summary>
    /// Every time a pending reminder will ring in [from, to), in time order (repeats listed occurrence by occurrence, at most
    /// <paramref name="perReminder"/> each). For the Agenda widget.
    /// </summary>
    public List<(Reminder Reminder, DateTimeOffset At)> Occurrences(DateTimeOffset from, DateTimeOffset to, int perReminder = 60)
    {
        var list = new List<(Reminder, DateTimeOffset)>();
        foreach (var r in settings.Items.Where(r => !r.Done))
        {
            if (r.SnoozedUntil is { } snooze && snooze >= from && snooze < to) list.Add((r, snooze));
            if (!r.IsRepeating && r.SnoozedUntil is not null) continue;
            if (!r.IsRepeating && r.Attention != ReminderAttention.None) continue;
            var at = r.Due;
            for (int i = 0; i < perReminder && at < to; i++)
            {
                if (at >= from) list.Add((r, at));
                if (!r.IsRepeating) break;
                if (ReminderSchedule.Next(r.Repeat, at, zone) is not { } next || next <= at) break;
                at = next;
            }
        }
        return list.OrderBy(x => x.Item2).ToList();
    }
}
