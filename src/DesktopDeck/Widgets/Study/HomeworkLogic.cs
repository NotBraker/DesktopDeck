using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace DesktopDeck.Widgets;

/// <summary>
/// Homework tracker logic: the assignment list (stored as JSON in Item.Options["data.items"]), grouping by due date,
/// the colored due pills and the forgiving date parser behind "type a date". Everything takes "now" so it can be tested.
/// </summary>
public static class HomeworkLogic
{
    public const string DataKey = "data.items";
    public const int MaxItems = 400;

    public sealed class Assignment
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Subject { get; set; } = "";
        /// <summary>yyyy-MM-dd, or empty for "no due date".</summary>
        public string Due { get; set; } = "";
        /// <summary>HH:mm, or empty.</summary>
        public string Time { get; set; } = "";
        /// <summary>0 = low, 1 = normal, 2 = high.</summary>
        public int Priority { get; set; } = 1;
        public bool Done { get; set; }
        /// <summary>yyyy-MM-dd the day it was completed.</summary>
        public string DoneAt { get; set; } = "";
        /// <summary>The Google Classroom coursework id this came from ("Import into Homework" skips ids it already has); empty for assignments typed in.</summary>
        public string ClassroomId { get; set; } = "";
    }

    public enum Group { Overdue, Today, Tomorrow, Week, Later, NoDate, Done }
    public enum PillKind { Overdue, Today, Soon, Normal, None, Done }

    public sealed record Section(string Key, string Title, List<Assignment> Items);
    public sealed record Pill(string Text, PillKind Kind);
    public sealed record DueInput(DateTime? Date, string Time, bool Ok);

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private const string DateFormat = "yyyy-MM-dd";

    // ---------- Storage ----------

    public static List<Assignment> Parse(string? json)
    {
        try
        {
            var items = JsonSerializer.Deserialize<List<Assignment>>(string.IsNullOrWhiteSpace(json) ? "[]" : json, Json) ?? [];
            var result = new List<Assignment>();
            foreach (var item in items)
            {
                if (item is null || string.IsNullOrWhiteSpace(item.Title)) continue;
                item.Id = string.IsNullOrEmpty(item.Id) ? Guid.NewGuid().ToString("N") : item.Id;
                item.Title = Clip(item.Title, 200);
                item.Subject = Clip(item.Subject ?? "", 40);
                item.Due = ParseStoredDate(item.Due) is { } d ? d.ToString(DateFormat, CultureInfo.InvariantCulture) : "";
                item.Time = ParseTime(item.Time ?? "") ?? "";
                item.Priority = Math.Clamp(item.Priority, 0, 2);
                item.DoneAt = ParseStoredDate(item.DoneAt) is { } e ? e.ToString(DateFormat, CultureInfo.InvariantCulture) : "";
                item.ClassroomId = Clip(item.ClassroomId ?? "", 100);
                result.Add(item);
                if (result.Count >= MaxItems) break;
            }
            return result;
        }
        catch (JsonException) { return []; }
    }

    public static string Serialize(IEnumerable<Assignment> items) => JsonSerializer.Serialize(items.Take(MaxItems), Json);

    /// <summary>
    /// Keeps completed assignments for <paramref name="keepDays"/> days after the day they were done (0 = until cleared by hand, nothing is removed).
    /// A completed assignment with no done date (from before dates were kept) counts as done on <paramref name="today"/>. Returns how many were removed.
    /// </summary>
    public static int PruneDone(List<Assignment> items, DateTime today, int keepDays)
    {
        if (keepDays <= 0) return 0;
        foreach (var a in items.Where(a => a.Done && ParseStoredDate(a.DoneAt) is null)) a.DoneAt = Format(today.Date);
        return items.RemoveAll(a => a.Done && ParseStoredDate(a.DoneAt) is { } at && (today.Date - at).TotalDays > keepDays);
    }

    private static string Clip(string text, int max) { text = text.Trim(); return text.Length <= max ? text : text[..max]; }

    public static string Format(DateTime date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);

    private static DateTime? ParseStoredDate(string? text) =>
        DateTime.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.Date : null;

    public static DateTime? DueDate(Assignment a) => ParseStoredDate(a.Due);

    public static Assignment Create(string title, string subject, DateTime? due, string time = "", int priority = 1) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Title = Clip(title, 200),
        Subject = Clip(subject, 40),
        Due = due is { } d ? Format(d) : "",
        Time = due is null ? "" : time,
        Priority = Math.Clamp(priority, 0, 2),
    };

    // ---------- Grouping and sorting ----------

    /// <summary>Days from today to the due date (negative = late); null without a due date.</summary>
    public static int? DaysUntil(Assignment a, DateTime today) => DueDate(a) is { } d ? (int)Math.Round((d - today.Date).TotalDays) : null;

    public static Group GroupOf(Assignment a, DateTime today)
    {
        if (a.Done) return Group.Done;
        return DaysUntil(a, today) switch
        {
            null => Group.NoDate,
            < 0 => Group.Overdue,
            0 => Group.Today,
            1 => Group.Tomorrow,
            <= 6 => Group.Week,
            _ => Group.Later,
        };
    }

    public static string GroupTitle(Group group) => group switch
    {
        Group.Overdue => "Overdue", Group.Today => "Today", Group.Tomorrow => "Tomorrow", Group.Week => "This week",
        Group.Later => "Later", Group.NoDate => "No date", _ => "Done",
    };

    /// <summary>Open items first (earliest due first, then time, then priority, then title); done items last.</summary>
    public static int Compare(Assignment x, Assignment y)
    {
        if (x.Done != y.Done) return x.Done ? 1 : -1;
        if (x.Done) return string.CompareOrdinal(y.DoneAt, x.DoneAt) is var byDone and not 0 ? byDone : string.Compare(x.Title, y.Title, StringComparison.CurrentCultureIgnoreCase);
        int byDue = string.CompareOrdinal(x.Due.Length == 0 ? "9999" : x.Due, y.Due.Length == 0 ? "9999" : y.Due);
        if (byDue != 0) return byDue;
        int byTime = string.CompareOrdinal(x.Time.Length == 0 ? "24:00" : x.Time, y.Time.Length == 0 ? "24:00" : y.Time);
        if (byTime != 0) return byTime;
        if (x.Priority != y.Priority) return y.Priority.CompareTo(x.Priority);
        return string.Compare(x.Title, y.Title, StringComparison.CurrentCultureIgnoreCase);
    }

    public static bool Visible(Assignment a, DateTime today, bool showDone, int hideDoneAfterDays)
    {
        if (!a.Done) return true;
        if (!showDone) return false;
        if (hideDoneAfterDays <= 0) return true;
        return ParseStoredDate(a.DoneAt) is not { } at || (today.Date - at).TotalDays < hideDoneAfterDays;
    }

    /// <summary>
    /// The sections to draw. <paramref name="mode"/>: "due" (Overdue / Today / Tomorrow / This week / Later), "subject",
    /// "priority" or "none" (one flat list). <paramref name="max"/> caps the open items shown (0 = no cap).
    /// </summary>
    public static List<Section> Layout(IEnumerable<Assignment> items, string mode, DateTime now, bool showDone, int hideDoneAfterDays, int max, IReadOnlyList<string>? subjectOrder = null)
    {
        var today = now.Date;
        var list = items.Where(a => Visible(a, today, showDone, hideDoneAfterDays)).OrderBy(a => a, Comparer<Assignment>.Create(Compare)).ToList();
        if (max > 0)
        {
            int open = 0;
            list = list.Where(a => a.Done || ++open <= max).ToList();
        }
        var sections = new List<Section>();
        var done = list.Where(a => a.Done).ToList();
        var open2 = list.Where(a => !a.Done).ToList();
        switch (mode)
        {
            case "subject":
                var order = (subjectOrder ?? []).ToList();
                foreach (var g in open2.GroupBy(a => a.Subject.Trim(), StringComparer.OrdinalIgnoreCase).OrderBy(g => { int i = order.FindIndex(n => string.Equals(n, g.Key, StringComparison.OrdinalIgnoreCase)); return g.Key.Length == 0 ? int.MaxValue : i < 0 ? int.MaxValue - 1 : i; }).ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
                    sections.Add(new Section("subject:" + g.Key, g.Key.Length == 0 ? "No subject" : g.Key, g.ToList()));
                break;
            case "priority":
                foreach (int p in new[] { 2, 1, 0 })
                    if (open2.Where(a => a.Priority == p).ToList() is { Count: > 0 } ps) sections.Add(new Section("priority:" + p, p == 2 ? "High priority" : p == 1 ? "Normal" : "Low priority", ps));
                break;
            case "none":
                if (open2.Count > 0) sections.Add(new Section("all", "", open2));
                break;
            default:
                foreach (var group in new[] { Group.Overdue, Group.Today, Group.Tomorrow, Group.Week, Group.Later, Group.NoDate })
                    if (open2.Where(a => GroupOf(a, today) == group).ToList() is { Count: > 0 } gs) sections.Add(new Section(group.ToString(), GroupTitle(group), gs));
                break;
        }
        if (done.Count > 0) sections.Add(new Section("Done", mode == "none" && sections.Count == 0 ? "" : "Done", done));
        return sections;
    }

    // ---------- Pills ----------

    public static string TimeText(string hhmm, bool use24)
    {
        if (!DateTime.TryParseExact(hhmm, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) return "";
        return use24 ? t.ToString("HH:mm", CultureInfo.InvariantCulture) : t.ToString("h:mm tt", CultureInfo.InvariantCulture).Replace(":00 ", " ");
    }

    /// <summary>The due pill: text and its urgency. <paramref name="warningDays"/> is how soon counts as "soon".</summary>
    public static Pill PillFor(Assignment a, DateTime now, int warningDays = 2, bool use24 = true, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (a.Done) return new Pill("Done", PillKind.Done);
        if (DueDate(a) is not { } due) return new Pill("No date", PillKind.None);
        int days = (int)Math.Round((due - now.Date).TotalDays);
        string time = a.Time.Length > 0 ? " · " + TimeText(a.Time, use24) : "";
        bool late = days < 0 || (days == 0 && a.Time.Length > 0 && DateTime.TryParseExact(a.Time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) && now.TimeOfDay > t.TimeOfDay);
        if (days < 0) return new Pill(days == -1 ? "Yesterday" : $"{-days}d late", PillKind.Overdue);
        if (days == 0) return new Pill("Today" + time, late ? PillKind.Overdue : PillKind.Today);
        if (days == 1) return new Pill("Tomorrow" + time, warningDays >= 1 ? PillKind.Soon : PillKind.Normal);
        string text = days <= 6 ? due.ToString("ddd", culture) : due.ToString("MMM d", culture);
        return new Pill(text + time, days <= warningDays ? PillKind.Soon : PillKind.Normal);
    }

    public static (int Open, int Overdue, int DueToday, int Done) Summary(IEnumerable<Assignment> items, DateTime today)
    {
        int open = 0, overdue = 0, dueToday = 0, done = 0;
        foreach (var a in items)
        {
            if (a.Done) { done++; continue; }
            open++;
            var g = GroupOf(a, today);
            if (g == Group.Overdue) overdue++;
            else if (g == Group.Today) dueToday++;
        }
        return (open, overdue, dueToday, done);
    }

    // ---------- Changes ----------

    public static void Complete(Assignment a, bool done, DateTime now)
    {
        a.Done = done;
        a.DoneAt = done ? Format(now.Date) : "";
    }

    /// <summary>Moves the due date forward; late or undated assignments start from today.</summary>
    public static void Postpone(Assignment a, DateTime now, int days)
    {
        var from = DueDate(a) is { } d && d >= now.Date ? d : now.Date;
        a.Due = Format(from.AddDays(days));
    }

    /// <summary>"Today", "Tomorrow", then the next days by weekday name: the quick date choices.</summary>
    public static List<(string Label, DateTime Date)> QuickDates(DateTime today, CultureInfo? culture = null, int count = 7)
    {
        culture ??= CultureInfo.CurrentCulture;
        var list = new List<(string, DateTime)> { ("Today", today.Date), ("Tomorrow", today.Date.AddDays(1)) };
        for (int i = 2; i <= count; i++)
        {
            var d = today.Date.AddDays(i);
            list.Add((d.ToString("dddd", culture) + (i == 7 ? " (next week)" : ""), d));
        }
        return list;
    }

    // ---------- Typing a date ----------

    private static readonly string[] Months = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];
    private static readonly string[] Weekdays = ["sun", "mon", "tue", "wed", "thu", "fri", "sat"];

    /// <summary>
    /// "9:30", "2:30pm", "3 pm", "noon" to HH:mm; null if it isn't a time. Bare numbers are not times.
    /// </summary>
    public static string? ParseTime(string text)
    {
        text = text.Trim().ToLowerInvariant().Replace(".", "");
        if (text.Length == 0) return null;
        if (text == "noon") return "12:00";
        if (text == "midnight") return "23:59";
        bool pm = false, am = false;
        if (text.EndsWith("pm", StringComparison.Ordinal)) { pm = true; text = text[..^2].Trim(); }
        else if (text.EndsWith("am", StringComparison.Ordinal)) { am = true; text = text[..^2].Trim(); }
        int hour, minute = 0;
        int colon = text.IndexOf(':');
        if (colon >= 0)
        {
            if (!int.TryParse(text[..colon], NumberStyles.None, CultureInfo.InvariantCulture, out hour) || !int.TryParse(text[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out minute)) return null;
        }
        else
        {
            if (!pm && !am) return null;
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out hour)) return null;
        }
        if (minute is < 0 or > 59) return null;
        if (pm || am)
        {
            if (hour is < 1 or > 12) return null;
            hour = hour % 12 + (pm ? 12 : 0);
        }
        if (hour is < 0 or > 23) return null;
        return $"{hour:00}:{minute:00}";
    }

    /// <summary>
    /// Understands: today, tomorrow, mon…sunday (the next one), +3 / 3d, "in 3 days", next week, 2026-10-12, 10/12 (or 12/10 when
    /// <paramref name="dayFirst"/>), 10/12/26, "oct 12", "12 oct", and a bare day of the month ("12").
    /// </summary>
    public static DateTime? ParseDate(string text, DateTime today, bool dayFirst = false)
    {
        text = text.Trim().ToLowerInvariant();
        today = today.Date;
        if (text.Length == 0) return null;
        text = text.Replace(",", " ");
        text = string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (text is "today" or "tod" or "now") return today;
        if (text is "tomorrow" or "tom" or "tmr" or "tmrw" or "tmw") return today.AddDays(1);
        if (text is "next week" or "nextweek") return today.AddDays(7);
        if (text.StartsWith("next ", StringComparison.Ordinal)) text = text[5..];
        if (text.StartsWith("in ", StringComparison.Ordinal)) text = text[3..];

        // +3, 3d, 3 days, 2 weeks.
        string digits = new(text.TakeWhile(c => char.IsDigit(c) || c == '+').ToArray());
        string rest = text[digits.Length..].Trim();
        if (digits.Length > 0 && int.TryParse(digits.TrimStart('+'), NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n <= 3650)
        {
            if (digits.StartsWith('+') && rest.Length == 0) return today.AddDays(n);
            if (rest is "d" or "day" or "days") return today.AddDays(n);
            if (rest is "w" or "wk" or "week" or "weeks") return today.AddDays(7 * n);
        }

        // Weekday names: the next one coming (a week away if it is today).
        string word = text.Length >= 3 ? text[..3] : "";
        for (int i = 0; i < 7; i++)
        {
            if (word != Weekdays[i] || !IsWeekdayWord(text, i)) continue;
            int ahead = ((i - (int)today.DayOfWeek) + 7) % 7;
            return today.AddDays(ahead == 0 ? 7 : ahead);
        }

        // ISO date.
        if (DateTime.TryParseExact(text, ["yyyy-M-d", "yyyy/M/d"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso)) return iso.Date;

        // M/d, M/d/yyyy, d/M (day first), also with - or .
        var parts = text.Split(['/', '-', '.'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 2 or 3 && parts.All(p => p.All(char.IsDigit)))
        {
            int a = int.Parse(parts[0], CultureInfo.InvariantCulture), b = int.Parse(parts[1], CultureInfo.InvariantCulture);
            int month = dayFirst ? b : a, day = dayFirst ? a : b;
            if (month > 12 && day <= 12) (month, day) = (day, month);
            int? year = parts.Length == 3 ? int.Parse(parts[2], CultureInfo.InvariantCulture) : null;
            if (year is < 100) year += 2000;
            return Build(today, year, month, day);
        }

        // "oct 12", "12 oct", "october 12th".
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w.TrimEnd('.')).ToList();
        if (words.Count is 2 or 3)
        {
            int monthIndex = words.FindIndex(w => w.Length >= 3 && Months.Contains(w[..3]) && w.All(char.IsLetter));
            if (monthIndex >= 0)
            {
                var nums = words.Where((_, i) => i != monthIndex).Select(w => new string(w.TakeWhile(char.IsDigit).ToArray())).ToList();
                if (nums.All(x => x.Length > 0) && int.TryParse(nums[0], NumberStyles.None, CultureInfo.InvariantCulture, out int day))
                {
                    int? year = nums.Count > 1 && int.TryParse(nums[1], NumberStyles.None, CultureInfo.InvariantCulture, out int y) ? (y < 100 ? y + 2000 : y) : null;
                    return Build(today, year, Array.IndexOf(Months, words[monthIndex][..3]) + 1, day);
                }
            }
        }

        // A bare day of the month: the next time it comes round.
        if (text.All(char.IsDigit) && text.Length <= 2 && int.Parse(text, CultureInfo.InvariantCulture) is >= 1 and <= 31 and var dom)
        {
            for (int add = 0; add < 3; add++)
            {
                var month = today.AddMonths(add);
                if (dom <= DateTime.DaysInMonth(month.Year, month.Month) && new DateTime(month.Year, month.Month, dom) is var candidate && candidate >= today) return candidate;
            }
        }
        return null;
    }

    private static bool IsWeekdayWord(string text, int index)
    {
        string[] full = ["sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday"];
        return text == Weekdays[index] || text == full[index] || (index == 2 && text == "tues") || (index == 4 && text is "thur" or "thurs");
    }

    /// <summary>A date typed without a year that already passed more than a month ago means next year.</summary>
    private static DateTime? Build(DateTime today, int? year, int month, int day)
    {
        if (month is < 1 or > 12 || day < 1) return null;
        int y = year ?? today.Year;
        if (y < 1900 || y > 2200 || day > DateTime.DaysInMonth(y, month)) return null;
        var date = new DateTime(y, month, day);
        if (year is null && date < today.AddDays(-30))
        {
            y++;
            if (day > DateTime.DaysInMonth(y, month)) return null;
            date = new DateTime(y, month, day);
        }
        return date;
    }

    /// <summary>
    /// What the "type a date" box accepts: a date, a time, or both ("fri 3pm", "oct 12 14:30", "tomorrow noon").
    /// Empty text means "no due date" (Ok, no date).
    /// </summary>
    public static DueInput ParseDue(string? text, DateTime today, bool dayFirst = false)
    {
        string source = (text ?? "").Trim().ToLowerInvariant();
        if (source.Length == 0) return new DueInput(null, "", true);
        var tokens = source.Replace(",", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => t is not ("at" or "by" or "@" or "due")).ToList();
        string time = "";
        for (int i = 0; i < tokens.Count && time.Length == 0; i++)
        {
            string? parsed = null;
            int used = 1;
            if (tokens[i] is "noon" or "midnight" || tokens[i].Contains(':') || tokens[i].EndsWith("am", StringComparison.Ordinal) || tokens[i].EndsWith("pm", StringComparison.Ordinal)) parsed = ParseTime(tokens[i]);
            if (parsed is null && i + 1 < tokens.Count && tokens[i + 1] is "am" or "pm" or "a.m." or "p.m." && tokens[i].All(c => char.IsDigit(c) || c == ':')) { parsed = ParseTime(tokens[i] + tokens[i + 1]); used = 2; }
            if (parsed is null) continue;
            time = parsed;
            tokens.RemoveRange(i, used);
        }
        if (tokens.Count == 0) return time.Length > 0 ? new DueInput(today.Date, time, true) : new DueInput(null, "", false);
        var date = ParseDate(string.Join(' ', tokens), today, dayFirst);
        return date is null ? new DueInput(null, "", false) : new DueInput(date, time, true);
    }
}
