using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using DesktopDeck.Models;

namespace DesktopDeck.Widgets;

/// <summary>What the Classroom widget shows (read from its settings).</summary>
public sealed record ClassroomOptions(int Days, int OverdueDays, bool HideTurnedIn, bool ShowNoDue, int MaxItems, string GroupBy, IReadOnlyList<string> Courses);

/// <summary>
/// Classroom widget logic (no WPF, no network): the cache, which assignments to show and in what order, due and status
/// wording (overdue / missing / turned in late), course colors, and copying assignments into the Homework widget without
/// doubles. Everything takes "now" so tests can fix the time. Unit tested.
/// </summary>
public static class ClassroomLogic
{
    public enum StatusKind { None, Missing, DueToday, Soon, TurnedIn, TurnedInLate, Returned }

    public sealed record Status(StatusKind Kind, string Text);
    public sealed record Section(string Key, string Title, List<ClassroomWork> Items);
    public sealed record Layout(List<Section> Sections, int More, int Total);

    /// <summary>What is saved between runs.</summary>
    public sealed class CacheData
    {
        public List<ClassroomCourse> Courses { get; set; } = new();
        public List<ClassroomWork> Work { get; set; } = new();
        /// <summary>Set when turned-in state could not be read (shown as a quiet note).</summary>
        public string SubmissionsProblem { get; set; } = "";
    }

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>A fixed palette: Classroom's API does not give a course color, so each course gets a stable one (or the user's own).</summary>
    public static readonly string[] Palette = ["#4285f4", "#34a853", "#fbbc04", "#ea4335", "#8e5ea2", "#12b5cb", "#f7770f", "#ec407a", "#7cb342", "#5c6bc0", "#26a69a", "#a142f4"];

    // ---------- Cache ----------

    public static string ToCache(IEnumerable<ClassroomCourse> courses, IEnumerable<ClassroomWork> work, string? submissionsProblem = null) =>
        JsonSerializer.Serialize(new CacheData { Courses = courses.ToList(), Work = work.ToList(), SubmissionsProblem = submissionsProblem ?? "" }, Json);

    public static CacheData FromCache(string? json)
    {
        try
        {
            var data = string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<CacheData>(json, Json);
            if (data is null) return new CacheData();
            data.Courses = (data.Courses ?? new()).Where(c => c is not null && c.Id.Length > 0).ToList();
            data.Work = (data.Work ?? new()).Where(w => w is not null && w.Id.Length > 0).ToList();
            data.SubmissionsProblem ??= "";
            return data;
        }
        catch (JsonException) { return new CacheData(); }
    }

    // ---------- Course colors ----------

    /// <summary>"Course name | #color" lines (part of the name is enough). Lines without a valid color are skipped.</summary>
    public static List<(string Match, string Color)> ParseCourseColors(IEnumerable<string> lines)
    {
        var result = new List<(string, string)>();
        foreach (string raw in lines)
        {
            int bar = raw.LastIndexOf('|');
            if (bar <= 0) continue;
            string match = raw[..bar].Trim(), color = raw[(bar + 1)..].Trim();
            if (match.Length > 0 && color.Length is 4 or 7 or 9 && color[0] == '#' && color.Skip(1).All(Uri.IsHexDigit)) result.Add((match, color));
        }
        return result;
    }

    /// <summary>The user's color for the course if a line matches its name or id, else a stable palette color from its id.</summary>
    public static string ColorFor(string courseId, string courseName, IReadOnlyList<(string Match, string Color)> overrides)
    {
        foreach (var (match, color) in overrides)
            if (courseId == match || courseName.Contains(match, StringComparison.CurrentCultureIgnoreCase)) return color;
        return Palette[ConnectedLogic.Bucket(courseId.Length > 0 ? courseId : courseName, Palette.Length)];
    }

    // ---------- Due moments and status ----------

    /// <summary>
    /// The moment an assignment is late after. With a due time that is the time itself; with only a date it is the end of that day
    /// (Classroom treats "due Oct 12" as the whole day).
    /// </summary>
    public static DateTime? DeadlineOf(ClassroomWork w) => w.Due is { } due ? (w.HasDueTime ? due : due.Date.AddDays(1)) : null;

    public static bool IsMissing(ClassroomWork w, DateTime now) =>
        w.State == SubmissionState.Open && DeadlineOf(w) is { } deadline && deadline <= now;

    public static bool IsDone(ClassroomWork w) => w.State is SubmissionState.TurnedIn or SubmissionState.Returned;

    /// <summary>Overdue (not handed in and past due), due today, due soon, turned in (on time or late) or returned.</summary>
    public static Status StatusOf(ClassroomWork w, DateTime now, int soonDays = 2)
    {
        if (w.State == SubmissionState.Returned) return new Status(StatusKind.Returned, "Returned");
        if (w.State == SubmissionState.TurnedIn) return w.Late ? new Status(StatusKind.TurnedInLate, "Turned in late") : new Status(StatusKind.TurnedIn, "Turned in");
        if (IsMissing(w, now)) return new Status(StatusKind.Missing, "Missing");
        if (w.Due is not { } due) return new Status(StatusKind.None, "");
        int days = (int)Math.Round((due.Date - now.Date).TotalDays);
        if (days == 0) return new Status(StatusKind.DueToday, "Due today");
        if (days <= soonDays) return new Status(StatusKind.Soon, days == 1 ? "Due tomorrow" : $"Due in {days} days");
        return new Status(StatusKind.None, "");
    }

    /// <summary>"Today 3:30 PM", "Tomorrow", "Fri 11:59 PM", "Oct 12", "Yesterday"; empty without a due date.</summary>
    public static string DueText(ClassroomWork w, DateTime now, bool use24, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.InvariantCulture;
        if (w.Due is not { } due) return "No due date";
        int days = (int)Math.Round((due.Date - now.Date).TotalDays);
        string day = days switch
        {
            0 => "Today",
            1 => "Tomorrow",
            -1 => "Yesterday",
            > 1 and <= 6 => due.ToString("ddd", culture),
            _ => due.ToString("MMM d", culture),
        };
        if (!w.HasDueTime) return day;
        string time = use24 ? due.ToString("HH:mm", culture) : due.ToString("h:mm tt", culture).Replace(":00 ", " ");
        return day + " " + time;
    }

    // ---------- Choosing and grouping ----------

    /// <summary>The assignments in the window: not yet due-and-handed-in as the options say, from <c>overdueDays</c> ago to <c>days</c> ahead, sorted by due moment.</summary>
    public static List<ClassroomWork> Candidates(IEnumerable<ClassroomWork> work, DateTime now, ClassroomOptions o, bool includeTurnedIn)
    {
        var from = now.Date.AddDays(-Math.Max(0, o.OverdueDays));
        var until = now.Date.AddDays(Math.Max(1, o.Days) + 1);
        var list = new List<ClassroomWork>();
        foreach (var w in work)
        {
            if (!CourseAllowed(w, o.Courses)) continue;
            if (!includeTurnedIn && IsDone(w) && o.HideTurnedIn) continue;
            if (w.Due is not { } due)
            {
                if (o.ShowNoDue && !IsDone(w)) list.Add(w);
                continue;
            }
            // Overdue work only counts while it is still open; finished work only shows when it is due ahead.
            if (due.Date < now.Date && (IsDone(w) || due.Date < from)) continue;
            if (due.Date >= until) continue;
            list.Add(w);
        }
        return list.OrderBy(w => w.Due is null ? 1 : 0).ThenBy(w => w.Due).ThenBy(w => w.CourseName, StringComparer.CurrentCultureIgnoreCase).ThenBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Matches the "courses to include" lines against the course name and id stored on the work (no lines = every course).</summary>
    private static bool CourseAllowed(ClassroomWork w, IReadOnlyList<string> lines)
    {
        var wanted = lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        return wanted.Count == 0 || wanted.Any(l => w.CourseId == l || w.CourseName.Contains(l, StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>The sections to draw: grouped by "date" (Overdue, Today, Tomorrow, then each day), by "course" or not at all.</summary>
    public static Layout Arrange(IEnumerable<ClassroomWork> work, DateTime now, ClassroomOptions o, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var all = Candidates(work, now, o, includeTurnedIn: !o.HideTurnedIn);
        var shown = all.Take(Math.Max(1, o.MaxItems)).ToList();
        var sections = new List<Section>();
        switch (o.GroupBy)
        {
            case "course":
                foreach (var g in shown.GroupBy(w => w.CourseId).OrderBy(g => g.First().CourseName, StringComparer.CurrentCultureIgnoreCase))
                    sections.Add(new Section("course:" + g.Key, g.First().CourseName, g.ToList()));
                break;
            case "none":
                if (shown.Count > 0) sections.Add(new Section("all", "", shown));
                break;
            default:
                foreach (var g in shown.GroupBy(w => DateKey(w, now)).OrderBy(g => g.Key.Order))
                    sections.Add(new Section(g.Key.Key, g.Key.Title.Length > 0 ? g.Key.Title : DayTitle(g.First(), now, culture), g.ToList()));
                break;
        }
        return new Layout(sections, all.Count - shown.Count, all.Count);
    }

    private static (int Order, string Key, string Title) DateKey(ClassroomWork w, DateTime now)
    {
        if (w.Due is not { } due) return (int.MaxValue, "nodate", "No due date");
        if (IsMissing(w, now) || (due.Date < now.Date && !IsDone(w))) return (-1, "overdue", "Overdue");
        int days = (int)Math.Round((due.Date - now.Date).TotalDays);
        return (days, "d" + due.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture), days switch { 0 => "Today", 1 => "Tomorrow", _ => "" });
    }

    private static string DayTitle(ClassroomWork w, DateTime now, CultureInfo culture) =>
        w.Due is { } due ? (due.Year == now.Year ? due.ToString("dddd, MMM d", culture) : due.ToString("ddd, MMM d, yyyy", culture)) : "";

    /// <summary>"Open assignments" count for the header badge: open work in the window that is not turned in.</summary>
    public static int OpenCount(IEnumerable<ClassroomWork> work, DateTime now, ClassroomOptions o) =>
        Candidates(work, now, o with { HideTurnedIn = true }, includeTurnedIn: false).Count;

    // ---------- Import into Homework ----------

    /// <summary>The assignments "Import into Homework" copies: everything open in the window (the item limit and the turned-in setting do not apply).</summary>
    public static List<ClassroomWork> ImportCandidates(IEnumerable<ClassroomWork> work, DateTime now, ClassroomOptions o) =>
        Candidates(work, now, o with { HideTurnedIn = true }, includeTurnedIn: false);

    /// <summary>The Classroom ids Homework already has.</summary>
    public static HashSet<string> KnownIds(IEnumerable<HomeworkLogic.Assignment> list) =>
        list.Where(a => a.ClassroomId.Length > 0).Select(a => a.ClassroomId).ToHashSet(StringComparer.Ordinal);

    /// <summary>One Classroom assignment as a Homework assignment (course name as the subject, local due date and time).</summary>
    public static HomeworkLogic.Assignment ToAssignment(ClassroomWork w)
    {
        var a = HomeworkLogic.Create(w.Title, w.CourseName, w.Due?.Date, w.HasDueTime && w.Due is { } due ? due.ToString("HH:mm", CultureInfo.InvariantCulture) : "");
        a.ClassroomId = w.Id;
        return a;
    }

    /// <summary>
    /// Adds assignments to a Homework list. Anything whose Classroom id is already there is skipped (even if it was finished or
    /// edited), as is anything past Homework's size limit. Returns (added, already there, no room).
    /// </summary>
    public static (int Added, int Duplicates, int NoRoom) AddTo(List<HomeworkLogic.Assignment> list, IEnumerable<ClassroomWork> work)
    {
        var known = KnownIds(list);
        int added = 0, duplicates = 0, noRoom = 0;
        foreach (var w in work)
        {
            if (w.Id.Length == 0) continue;
            if (!known.Add(w.Id)) { duplicates++; continue; }
            if (list.Count >= HomeworkLogic.MaxItems) { noRoom++; continue; }
            list.Add(ToAssignment(w));
            added++;
        }
        return (added, duplicates, noRoom);
    }
}
