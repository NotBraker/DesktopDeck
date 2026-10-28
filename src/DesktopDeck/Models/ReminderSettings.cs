using System.Globalization;

namespace DesktopDeck.Models;

/// <summary>Reminders and how they notify. Saved in settings.json as <see cref="DeckSettings.Reminders"/>.</summary>
public sealed class ReminderSettings
{
    public const int MaxDone = 60;

    public List<Reminder> Items { get; set; } = new();
    /// <summary>Show the toast card (top-right of the main monitor) when a reminder rings.</summary>
    public bool Toasts { get; set; } = true;
    /// <summary>Also show a tray balloon.</summary>
    public bool TrayBalloon { get; set; } = true;
    public bool Sound { get; set; }
    /// <summary>How long a toast stays (seconds); it stays while the pointer is on it.</summary>
    public int ToastSeconds { get; set; } = 6;
    /// <summary>"Do not disturb": nothing rings or pops up until it is turned off. What was due is shown as missed.</summary>
    public bool DoNotDisturb { get; set; }
    /// <summary>Quiet hours: the same, every day between <see cref="QuietFrom"/> and <see cref="QuietTo"/> (may cross midnight).</summary>
    public bool QuietHours { get; set; }
    public string QuietFrom { get; set; } = "22:00";
    public string QuietTo { get; set; } = "07:00";
    public bool Use24Hour { get; set; }
    /// <summary>The time used when only a day is given ("on oct 12 dentist", "every monday trash").</summary>
    public string DefaultTime { get; set; } = "09:00";
    /// <summary>"Snooze until tomorrow" rings at this time.</summary>
    public string TomorrowAt { get; set; } = "09:00";
    /// <summary>The color reminders have in the Agenda widget.</summary>
    public string Color { get; set; } = "#ec4899";

    [System.Text.Json.Serialization.JsonIgnore]
    public TimeSpan DefaultTimeOfDay => ParseTime(DefaultTime, new TimeSpan(9, 0, 0));
    [System.Text.Json.Serialization.JsonIgnore]
    public TimeSpan TomorrowTimeOfDay => ParseTime(TomorrowAt, new TimeSpan(9, 0, 0));
    [System.Text.Json.Serialization.JsonIgnore]
    public TimeSpan QuietStart => ParseTime(QuietFrom, new TimeSpan(22, 0, 0));
    [System.Text.Json.Serialization.JsonIgnore]
    public TimeSpan QuietEnd => ParseTime(QuietTo, new TimeSpan(7, 0, 0));

    /// <summary>"HH:mm" (or "h:mm tt") to a time of day; the fallback when it is not one.</summary>
    public static TimeSpan ParseTime(string? text, TimeSpan fallback)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        string t = text.Trim();
        if (DateTime.TryParseExact(t, ["H:mm", "h:mm tt", "h:mmtt", "h tt", "htt"], CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var d)) return d.TimeOfDay;
        return fallback;
    }

    public static string FormatTime(TimeSpan time) => $"{(int)time.TotalHours:00}:{time.Minutes:00}";

    /// <summary>Repairs what an old or hand-edited file may hold, so nothing downstream has to check.</summary>
    public static ReminderSettings Normalized(ReminderSettings? settings)
    {
        settings ??= new ReminderSettings();
        settings.Items = (settings.Items ?? new()).Where(r => r is not null && !string.IsNullOrWhiteSpace(r.Text)).ToList();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in settings.Items)
        {
            if (string.IsNullOrWhiteSpace(r.Id) || !ids.Add(r.Id)) { r.Id = Guid.NewGuid().ToString("N"); ids.Add(r.Id); }
            r.Text = Clip(r.Text.Trim(), 300);
            r.Note = Clip((r.Note ?? "").Trim(), 2000);
            r.Repeat ??= new ReminderRepeat();
            var rep = r.Repeat;
            if (!Enum.IsDefined(rep.Kind)) rep.Kind = RepeatKind.None;
            if (!Enum.IsDefined(rep.Unit)) rep.Unit = IntervalUnit.Hours;
            rep.Days = (rep.Days ?? new()).Where(d => Enum.IsDefined(d)).Distinct().OrderBy(d => ((int)d + 6) % 7).ToList();
            if (rep.Kind == RepeatKind.Weekly && rep.Days.Count == 0) rep.Days.Add(r.Due.DayOfWeek);
            rep.Every = Math.Clamp(rep.Every, 1, 10_000);
            rep.DayOfMonth = rep.Kind == RepeatKind.Monthly ? Math.Clamp(rep.DayOfMonth <= 0 ? r.Due.Day : rep.DayOfMonth, 1, 31) : 0;
            if (rep.Time is { } t && (t < TimeSpan.Zero || t >= TimeSpan.FromDays(1))) rep.Time = null;
            if (r.Created == default) r.Created = r.Due;
            if (!Enum.IsDefined(r.Attention)) r.Attention = ReminderAttention.None;
            // Repeating reminders are never done.
            if (rep.IsRepeating) { r.Done = false; r.DoneAt = null; }
            if (r.Done) { r.Attention = ReminderAttention.None; r.SnoozedUntil = null; r.DoneAt ??= r.LastFired ?? r.Due; }
        }
        // Old finished ones are forgotten: the newest few stay for "Done".
        var keep = settings.Items.Where(r => r.Done).OrderByDescending(r => r.DoneAt).Take(MaxDone).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        settings.Items = settings.Items.Where(r => !r.Done || keep.Contains(r.Id)).ToList();
        settings.ToastSeconds = Math.Clamp(settings.ToastSeconds, 2, 60);
        settings.QuietFrom = FormatTime(settings.QuietStart);
        settings.QuietTo = FormatTime(settings.QuietEnd);
        settings.DefaultTime = FormatTime(settings.DefaultTimeOfDay);
        settings.TomorrowAt = FormatTime(settings.TomorrowTimeOfDay);
        settings.Color = CalendarColors.Normalize(settings.Color, "#ec4899");
        return settings;
    }

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..max];
}
