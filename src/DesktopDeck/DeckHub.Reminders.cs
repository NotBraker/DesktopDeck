using System.Windows.Threading;
using DesktopDeck.Models;
using DesktopDeck.Services;

namespace DesktopDeck;

/// <summary>What <see cref="DeckHub.AddReminder"/> made: the reminder (a copy), or a message saying why not.</summary>
/// <param name="When">How the time was read: "Today at 5:00 PM", "Every Monday at 7:30 AM · next Mon".</param>
/// <param name="Summary">One line for the user, e.g. "Call mom: Today at 5:00 PM".</param>
public sealed record ReminderAddResult(Reminder? Reminder, string? Error, string Summary, string When = "")
{
    public bool Ok => Reminder is not null;
}

// Personal reminders: one service (Services/ReminderService.cs), one timer aimed at the next ring, toasts and the tray balloon.
internal sealed partial class DeckHub
{
    private ReminderService? reminderService;
    private ReminderSettings? reminderServiceFor;
    private DispatcherTimer? reminderTimer;
    private ReminderToasts? toasts;
    /// <summary>Rings held back by quiet hours that the user has not been told about yet.</summary>
    private int heldBack;

    /// <summary>Reminders ring (timer, toasts, sound) only on the real desktop; preview and smoke runs just show the list.</summary>
    private bool RemindersLive => !preview || framed;

    /// <summary>The scheduling service over <see cref="DeckSettings.Reminders"/> (re-made if the settings object is replaced, e.g. by an undo).</summary>
    internal ReminderService Engine
    {
        get
        {
            var settings = Settings.Reminders;
            if (reminderService is null || !ReferenceEquals(reminderServiceFor, settings)) { reminderService = new ReminderService(settings); reminderServiceFor = settings; }
            return reminderService;
        }
    }

    public ReminderSettings ReminderOptions => Settings.Reminders;

    /// <summary>A snapshot (copies) of every reminder, soonest first. Safe to call from any thread.</summary>
    public IReadOnlyList<Reminder> Reminders
    {
        get
        {
            if (!uiDispatcher.CheckAccess()) return uiDispatcher.Invoke(() => Reminders);
            return Engine.Items.OrderBy(r => r.Done).ThenBy(r => ReminderService.RingAt(r) ?? r.Due).Select(r => r.Copy()).ToList();
        }
    }

    /// <summary>Reminders changed (added, rang, done, snoozed, edited, deleted, or a setting). Raised on the UI thread.</summary>
    public event Action? RemindersChanged;

    public ReminderParseOptions ReminderParseSettings => new()
    {
        DefaultTime = Settings.Reminders.DefaultTimeOfDay,
        Use24Hour = Settings.Reminders.Use24Hour,
        DayFirst = DayBeforeMonth(),
    };

    private static bool DayBeforeMonth()
    {
        string pattern = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern;
        int day = pattern.IndexOf('d'), month = pattern.IndexOf('M');
        return day >= 0 && month >= 0 && day < month;
    }

    // ---------- Public API ----------

    /// <summary>
    /// Makes a reminder from a sentence such as "remind me at 5pm to call mom", "in 20 minutes stretch", "tomorrow 8am homework",
    /// "every monday 7:30 trash", "on oct 12 dentist" or "every 2 hours drink water". Returns the reminder, or a message saying what to fix.
    /// Safe to call from any thread.
    /// </summary>
    public ReminderAddResult AddReminder(string naturalText)
    {
        if (!uiDispatcher.CheckAccess()) return uiDispatcher.Invoke(() => AddReminder(naturalText));
        var now = DateTimeOffset.Now;
        var outcome = ReminderParser.Parse(naturalText, now, Engine.Zone, ReminderParseSettings);
        if (outcome.Value is not { } parsed) return new ReminderAddResult(null, outcome.Error ?? "I couldn't understand that.", "");
        var reminder = Engine.Add(parsed.ToReminder(now), now);
        ReminderStateChanged();
        return new ReminderAddResult(reminder.Copy(), null, $"{reminder.Text}: {parsed.When}", parsed.When);
    }

    /// <summary>How a sentence would be read, without adding it (the live preview).</summary>
    public ParseOutcome PreviewReminder(string text, bool requireText = true) =>
        ReminderParser.Parse(text, DateTimeOffset.Now, Engine.Zone, ReminderParseSettings with { RequireText = requireText });

    public bool CompleteReminder(string id)
    {
        if (!uiDispatcher.CheckAccess()) return uiDispatcher.Invoke(() => CompleteReminder(id));
        bool ok = Engine.Complete(id, DateTimeOffset.Now);
        if (ok) ReminderStateChanged();
        return ok;
    }

    public bool SnoozeReminder(string id, SnoozeChoice choice)
    {
        if (!uiDispatcher.CheckAccess()) return uiDispatcher.Invoke(() => SnoozeReminder(id, choice));
        bool ok = Engine.Snooze(id, choice, DateTimeOffset.Now);
        if (ok) ReminderStateChanged();
        return ok;
    }

    public bool DeleteReminder(string id)
    {
        if (!uiDispatcher.CheckAccess()) return uiDispatcher.Invoke(() => DeleteReminder(id));
        bool ok = Engine.Remove(id);
        if (ok) ReminderStateChanged();
        return ok;
    }

    internal bool UpdateReminder(string id, string text, string note, DateTimeOffset due, ReminderRepeat repeat)
    {
        bool ok = Engine.Update(id, text, note, due, repeat);
        if (ok) ReminderStateChanged();
        return ok;
    }

    /// <summary>Removes every finished reminder.</summary>
    internal void ClearDoneReminders()
    {
        if (Engine.Items.Where(r => r.Done).Select(r => r.Id).ToList() is { Count: > 0 } ids)
        {
            foreach (var id in ids) Engine.Remove(id);
            ReminderStateChanged();
        }
    }

    /// <summary>A reminder setting changed (a switch or a time in the Reminders tab).</summary>
    internal void ChangeReminderOptions(Action<ReminderSettings> change)
    {
        change(Settings.Reminders);
        ReminderSettings.Normalized(Settings.Reminders);
        ReminderStateChanged();
        // Do-not-disturb or quiet hours just ended: tell about what came in meanwhile.
        ReminderTick();
    }

    /// <summary>
    /// Asks "what and when?" (tray, control button, widgets) and adds the reminder; a sentence that cannot be read is asked again with the reason.
    /// <paramref name="ask"/> shows the prompt (title, label, text so far) and returns null if cancelled.
    /// </summary>
    internal ReminderAddResult? PromptNewReminder(Func<string, string, string, string?>? ask = null)
    {
        ask ??= (title, label, value) => Primary?.Ask(title, label, value, "Add");
        string label = "What and when? For example " + ReminderParser.Examples + ".";
        string value = "";
        while (true)
        {
            if (ask("New reminder", label, value) is not { } text) return null;
            var result = AddReminder(text);
            if (result.Ok) { SetStatus("Reminder added. " + result.Summary); return result; }
            label = result.Error!;
            value = text;
        }
    }

    /// <summary>The reminders that ring between two days, as calendar events (for the Agenda widget).</summary>
    internal List<CalendarEvent> ReminderEvents(DateOnly from, DateOnly toExclusive)
    {
        var zone = Engine.Zone;
        var start = ReminderSchedule.At(from.ToDateTime(TimeOnly.MinValue), zone);
        var end = ReminderSchedule.At(toExclusive.ToDateTime(TimeOnly.MinValue), zone);
        string color = Settings.Reminders.Color;
        return Engine.Occurrences(start, end).Select(o =>
        {
            var wall = ReminderSchedule.Wall(o.At, zone);
            return new CalendarEvent
            {
                Source = CalendarSource.Reminder, CalendarId = "reminders", CalendarLabel = "Reminders", Color = color, Title = o.Reminder.Text, Description = o.Reminder.Note,
                Start = wall, End = wall, LocalBlockId = o.Reminder.Id,
            };
        }).ToList();
    }

    // ---------- The timer ----------

    private void StartReminders()
    {
        if (!RemindersLive) return;
        reminderTimer = new DispatcherTimer(DispatcherPriority.Normal, uiDispatcher);
        reminderTimer.Tick += (_, _) => ReminderTick();
        // What came due while the app was closed shows once, as missed.
        ReminderTick();
    }

    private void StopReminders()
    {
        reminderTimer?.Stop();
        reminderTimer = null;
        toasts?.CloseAll();
    }

    /// <summary>Rings everything due now (once), saves, then shows it and aims the timer at the next ring.</summary>
    internal void ReminderTick()
    {
        if (closing || !RemindersLive) return;
        reminderTimer?.Stop();
        var now = DateTimeOffset.Now;
        var rings = Engine.Tick(now);
        bool quiet = Engine.IsQuiet(now);
        if (rings.Count > 0)
        {
            if (quiet) { foreach (var ring in rings) Engine.MarkMissed(ring.Reminder.Id); heldBack += rings.Count; }
            // Saved before anything is shown, so a ring can never repeat after a crash.
            Save();
            if (!quiet) ShowRings(rings, now);
            RaiseRemindersChanged();
        }
        else if (heldBack > 0 && !quiet)
        {
            ShowHeldBack();
        }
        ArmReminders();
    }

    private void ArmReminders()
    {
        if (reminderTimer is null) return;
        var now = DateTimeOffset.Now;
        var wake = Engine.NextWake();
        if (heldBack > 0 && Engine.QuietEndsAt(now) is { } quietEnd && (wake is null || quietEnd < wake)) wake = quietEnd;
        if (wake is null) { reminderTimer.Stop(); return; }
        // Real elapsed time (not the wall clock), so a daylight saving change cannot shift it; at most six hours, then it looks again.
        var delay = wake.Value - now + TimeSpan.FromMilliseconds(100);
        reminderTimer.Interval = delay < TimeSpan.FromMilliseconds(200) ? TimeSpan.FromMilliseconds(200) : delay > TimeSpan.FromHours(6) ? TimeSpan.FromHours(6) : delay;
        reminderTimer.Start();
    }

    /// <summary>Something changed: save, aim the timer again and tell the lists.</summary>
    private void ReminderStateChanged()
    {
        Save();
        ArmReminders();
        RaiseRemindersChanged();
    }

    private void RaiseRemindersChanged()
    {
        RemindersChanged?.Invoke();
        RebuildKind("reminders");
        RebuildKind("agenda");
    }

    // ---------- Showing a ring ----------

    private void ShowRings(List<ReminderRing> rings, DateTimeOffset now)
    {
        var options = Settings.Reminders;
        bool h24 = options.Use24Hour;
        var zone = Engine.Zone;
        if (options.Sound) System.Media.SystemSounds.Asterisk.Play();
        if (rings.Count > 3)
        {
            // A pile-up (the computer was asleep): one note instead of a wall of cards.
            string names = string.Join(", ", rings.Take(3).Select(r => r.Reminder.Text)) + $" and {rings.Count - 3} more";
            ShowNote($"{rings.Count} reminders", names, options);
            return;
        }
        foreach (var ring in rings)
        {
            bool missed = ring.Kind == RingKind.Missed;
            string title = missed ? "Missed reminder" : "Reminder";
            string when = ReminderFormat.When(ring.Occurrence, now, zone, h24);
            string detail = missed ? "Was due " + when + (ring.Skipped > 0 ? $" (and {ring.Skipped} earlier)" : "") : ring.Reminder.Note;
            if (options.TrayBalloon) RaiseNotify(title, ring.Reminder.Text + (missed ? " (due " + when + ")" : ""));
            if (options.Toasts) (toasts ??= new ReminderToasts(this)).Show(new ReminderToastInfo(ring.Reminder.Id, title, ring.Reminder.Text, detail, missed));
        }
    }

    private void ShowHeldBack()
    {
        int count = heldBack;
        heldBack = 0;
        ShowNote(count == 1 ? "1 reminder came in while you were away" : $"{count} reminders came in while you were away", "They are under Missed in the control center → Reminders.", Settings.Reminders);
    }

    private void ShowNote(string title, string text, ReminderSettings options)
    {
        if (options.TrayBalloon) RaiseNotify(title, text);
        if (options.Toasts) (toasts ??= new ReminderToasts(this)).Show(new ReminderToastInfo(null, title, text, "", true));
    }

    /// <summary>The toast of a reminder was answered elsewhere (or deleted): close it.</summary>
    internal bool ReminderNeedsAnswer(string id) => Engine.Find(id) is { Done: false, Attention: not ReminderAttention.None };

    /// <summary>Opens the control center on the Reminders tab.</summary>
    internal void OpenReminders() => OpenControlCenter("reminders");
}
