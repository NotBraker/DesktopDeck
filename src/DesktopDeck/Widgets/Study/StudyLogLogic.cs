using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopDeck.Widgets;

/// <summary>
/// Study time log: finished sessions (JSON in Item.Options["data.sessions"]) and the session in progress (start time and
/// subject, so it keeps counting across restarts). Totals per subject, per day and per week split sessions at midnight.
/// Every function takes "now" so it can be tested.
/// </summary>
public static class StudyLogLogic
{
    public const string SessionsKey = "data.sessions", RunStartKey = "data.runStart", RunSubjectKey = "data.runSubject";
    public const int MaxSessions = 3000;
    private const string Stamp = "yyyy-MM-ddTHH:mm:ss";

    /// <summary>A finished session in local time.</summary>
    public sealed class Session
    {
        [JsonPropertyName("s")] public string Start { get; set; } = "";
        [JsonPropertyName("e")] public string End { get; set; } = "";
        [JsonPropertyName("u")] public string Subject { get; set; } = "";

        [JsonIgnore] public DateTime StartTime => ParseStamp(Start) ?? DateTime.MinValue;
        [JsonIgnore] public DateTime EndTime => ParseStamp(End) ?? DateTime.MinValue;
        [JsonIgnore] public TimeSpan Length => EndTime - StartTime;
    }

    /// <summary>A session in progress.</summary>
    public sealed record Run(DateTime Start, string Subject);

    private static readonly JsonSerializerOptions Json = new();

    public static string FormatStamp(DateTime time) => time.ToString(Stamp, CultureInfo.InvariantCulture);
    public static DateTime? ParseStamp(string? text) =>
        DateTime.TryParseExact(text, Stamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;

    // ---------- Storage ----------

    public static List<Session> ParseSessions(string? json)
    {
        try
        {
            var list = JsonSerializer.Deserialize<List<Session>>(string.IsNullOrWhiteSpace(json) ? "[]" : json, Json) ?? [];
            return list.Where(s => s is not null && ParseStamp(s.Start) is { } a && ParseStamp(s.End) is { } b && b > a && (b - a).TotalHours <= 24).Select(s => { s.Subject = (s.Subject ?? "").Trim(); return s; }).ToList();
        }
        catch (JsonException) { return []; }
    }

    /// <summary>
    /// The sessions as JSON, oldest first, at most <see cref="MaxSessions"/>. With <paramref name="keepDays"/> above 0 (and <paramref name="today"/>),
    /// sessions that started more than that many days before today are dropped too (0 = keep all, up to the count).
    /// </summary>
    public static string SerializeSessions(IEnumerable<Session> sessions, DateTime? today = null, int keepDays = 0)
    {
        string? oldest = today is { } t && keepDays > 0 ? FormatStamp(t.Date.AddDays(-keepDays)) : null;
        return JsonSerializer.Serialize(sessions.Where(s => oldest is null || string.CompareOrdinal(s.Start, oldest) >= 0)
            .OrderBy(s => s.Start, StringComparer.Ordinal).TakeLast(MaxSessions).ToList(), Json);
    }

    public static Run? ReadRun(IReadOnlyDictionary<string, string> options) =>
        options.TryGetValue(RunStartKey, out var start) && ParseStamp(start) is { } at ? new Run(at, options.TryGetValue(RunSubjectKey, out var s) ? s : "") : null;

    public static void WriteRun(IDictionary<string, string> options, Run? run)
    {
        options[RunStartKey] = run is null ? "" : FormatStamp(run.Start);
        options[RunSubjectKey] = run?.Subject ?? "";
    }

    // ---------- Starting and stopping ----------

    /// <summary>
    /// Ends a run: the finished session, or null if it was shorter than <paramref name="minSeconds"/>. A session left running
    /// is cut off after <paramref name="maxHours"/> (0 = no limit).
    /// </summary>
    public static Session? Finish(Run run, DateTime now, int minSeconds = 60, double maxHours = 0)
    {
        var end = EffectiveEnd(run, now, maxHours);
        if ((end - run.Start).TotalSeconds < Math.Max(1, minSeconds)) return null;
        return new Session { Start = FormatStamp(run.Start), End = FormatStamp(end), Subject = run.Subject };
    }

    public static DateTime EffectiveEnd(Run run, DateTime now, double maxHours)
    {
        var end = now < run.Start ? run.Start : now;
        return maxHours > 0 && (end - run.Start).TotalHours > maxHours ? run.Start.AddHours(maxHours) : end;
    }

    public static bool IsOverLimit(Run run, DateTime now, double maxHours) => maxHours > 0 && (now - run.Start).TotalHours >= maxHours;

    /// <summary>A session typed in by hand that ended at <paramref name="now"/>.</summary>
    public static Session Manual(string subject, int minutes, DateTime now) => new()
    {
        Start = FormatStamp(now.AddMinutes(-minutes)),
        End = FormatStamp(now),
        Subject = subject.Trim(),
    };

    // ---------- Totals ----------

    private static TimeSpan Overlap(DateTime start, DateTime end, DateTime from, DateTime to)
    {
        var a = start > from ? start : from;
        var b = end < to ? end : to;
        return b > a ? b - a : TimeSpan.Zero;
    }

    /// <summary>Every finished session plus the one in progress, as (start, end, subject).</summary>
    private static IEnumerable<(DateTime Start, DateTime End, string Subject)> Spans(IEnumerable<Session> sessions, Run? run, DateTime now, double maxHours)
    {
        foreach (var s in sessions) yield return (s.StartTime, s.EndTime, s.Subject);
        if (run is not null) yield return (run.Start, EffectiveEnd(run, now, maxHours), run.Subject);
    }

    /// <summary>Time per subject between <paramref name="from"/> (inclusive) and <paramref name="to"/> (exclusive).</summary>
    public static Dictionary<string, TimeSpan> TotalsBySubject(IEnumerable<Session> sessions, Run? run, DateTime now, DateTime from, DateTime to, double maxHours = 0)
    {
        var totals = new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase);
        foreach (var (start, end, subject) in Spans(sessions, run, now, maxHours))
        {
            var part = Overlap(start, end, from, to);
            if (part <= TimeSpan.Zero) continue;
            totals[subject] = totals.TryGetValue(subject, out var old) ? old + part : part;
        }
        return totals;
    }

    public static TimeSpan Total(IEnumerable<Session> sessions, Run? run, DateTime now, DateTime from, DateTime to, double maxHours = 0) =>
        TotalsBySubject(sessions, run, now, from, to, maxHours).Values.Aggregate(TimeSpan.Zero, (a, b) => a + b);

    /// <summary>Time on each of <paramref name="days"/> days starting at <paramref name="first"/>.</summary>
    public static TimeSpan[] PerDay(IEnumerable<Session> sessions, Run? run, DateTime now, DateTime first, int days, double maxHours = 0)
    {
        var list = Spans(sessions, run, now, maxHours).ToList();
        var result = new TimeSpan[days];
        for (int i = 0; i < days; i++)
        {
            var from = first.Date.AddDays(i);
            var to = from.AddDays(1);
            foreach (var (start, end, _) in list) result[i] += Overlap(start, end, from, to);
        }
        return result;
    }

    /// <summary>The first day of the week containing <paramref name="date"/>.</summary>
    public static DateTime WeekStart(DateTime date, DayOfWeek first)
    {
        int back = ((int)date.DayOfWeek - (int)first + 7) % 7;
        return date.Date.AddDays(-back);
    }

    public static DayOfWeek WeekStartOption(string? text) => text switch { "sunday" => DayOfWeek.Sunday, "saturday" => DayOfWeek.Saturday, _ => DayOfWeek.Monday };

    /// <summary>
    /// Days in a row with at least <paramref name="minMinutes"/> of study. Today doesn't break the streak until it is over:
    /// without time yet today, the streak counts back from yesterday. <c>Best</c> is the longest run in the history.
    /// </summary>
    public static (int Current, int Best) Streak(IEnumerable<Session> sessions, Run? run, DateTime now, int minMinutes = 10, double maxHours = 0, int lookbackDays = 400)
    {
        var list = sessions.ToList();
        var first = now.Date.AddDays(-(lookbackDays - 1));
        var days = PerDay(list, run, now, first, lookbackDays, maxHours);
        bool Studied(int i) => days[i].TotalMinutes + 1e-9 >= Math.Max(1, minMinutes);
        int last = lookbackDays - 1, current = 0;
        int i2 = Studied(last) ? last : last - 1;
        while (i2 >= 0 && Studied(i2)) { current++; i2--; }
        int best = 0, runLength = 0;
        for (int i = 0; i < lookbackDays; i++)
        {
            runLength = Studied(i) ? runLength + 1 : 0;
            best = Math.Max(best, runLength);
        }
        return (current, best);
    }

    /// <summary>Progress toward the weekly goal, 0 to 1 (can't exceed 1; 0 when there is no goal).</summary>
    public static double GoalFraction(TimeSpan weekTotal, double goalHours) => goalHours <= 0 ? 0 : Math.Clamp(weekTotal.TotalHours / goalHours, 0, 1);

    // ---------- Text ----------

    /// <summary>"1h 20m", "45m" or "0m", with minutes rounded to the nearest <paramref name="roundTo"/> (0 = exact to the minute).</summary>
    public static string FormatDuration(TimeSpan span, int roundTo = 0)
    {
        double minutes = Math.Max(0, span.TotalMinutes);
        long total = roundTo > 1 ? (long)(Math.Round(minutes / roundTo, MidpointRounding.AwayFromZero) * roundTo) : (long)Math.Floor(minutes + 1e-9);
        long h = total / 60, m = total % 60;
        return h > 0 ? (m == 0 ? $"{h}h" : $"{h}h {m:00}m") : $"{m}m";
    }

    public static string FormatClock(TimeSpan span)
    {
        long seconds = (long)Math.Max(0, Math.Floor(span.TotalSeconds));
        return $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}";
    }

    /// <summary>Hours as one decimal for the goal ring, e.g. "6.5".</summary>
    public static string FormatHours(TimeSpan span) => span.TotalHours.ToString("0.#", CultureInfo.InvariantCulture);

    // ---------- Typing a session ----------

    /// <summary>"45", "45m", "1h", "1h30", "1h 30m", "1.5h", "90 min" as minutes; null if it isn't a length of time (1–1440 minutes).</summary>
    public static int? ParseMinutes(string? text)
    {
        string t = (text ?? "").Trim().ToLowerInvariant().Replace("hours", "h").Replace("hour", "h").Replace("hrs", "h").Replace("hr", "h").Replace("minutes", "m").Replace("minute", "m").Replace("mins", "m").Replace("min", "m").Replace(" ", "").Replace(',', '.');
        if (t.Length == 0) return null;
        double minutes;
        int h = t.IndexOf('h');
        if (h >= 0)
        {
            if (!double.TryParse(t[..h], NumberStyles.Float, CultureInfo.InvariantCulture, out double hours)) return null;
            string rest = t[(h + 1)..].TrimEnd('m');
            double extra = 0;
            if (rest.Length > 0 && !double.TryParse(rest, NumberStyles.Float, CultureInfo.InvariantCulture, out extra)) return null;
            minutes = hours * 60 + extra;
        }
        else
        {
            if (!double.TryParse(t.TrimEnd('m'), NumberStyles.Float, CultureInfo.InvariantCulture, out minutes)) return null;
        }
        int rounded = (int)Math.Round(minutes);
        return rounded is >= 1 and <= 1440 && double.IsFinite(minutes) ? rounded : null;
    }

    /// <summary>"45 math", "math 1h30", "1.5h latin": a length of time and a subject (any text; the subject may be left out and comes back empty).</summary>
    public static (string Subject, int Minutes)? ParseManual(string? text, IReadOnlyList<StudySubjectsLogic.Subject> subjects)
    {
        var tokens = (text ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        for (int count = Math.Min(3, tokens.Count); count >= 1; count--)
        {
            foreach (int at in new[] { 0, tokens.Count - count })
            {
                if (at < 0 || at + count > tokens.Count) continue;
                if (ParseMinutes(string.Join(' ', tokens.Skip(at).Take(count))) is not { } minutes) continue;
                string subject = string.Join(' ', tokens.Where((_, i) => i < at || i >= at + count)).Trim();
                if (StudySubjectsLogic.Find(subjects, subject) is { } known) subject = known.Name;
                return (subject, minutes);
            }
        }
        return null;
    }
}
