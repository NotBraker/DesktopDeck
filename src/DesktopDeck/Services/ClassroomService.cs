using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using DesktopDeck.Models;

namespace DesktopDeck.Services;

/// <summary>The outcome of planning an import: what to add, how many were already there, how many did not fit.</summary>
public sealed record ImportPlan(List<ClassroomWork> ToAdd, int Duplicates, int NoRoom);

/// <summary>What one refresh of the Classroom widget downloads. <see cref="SubmissionsProblem"/> is set when turned-in states could not be read.</summary>
public sealed record ClassroomFetch(List<ClassroomCourse> Courses, List<ClassroomWork> Work, string? SubmissionsProblem);

/// <summary>
/// Google Classroom API v1, read-only, for the signed-in student: active courses, their assignments (courseWork) and the
/// student's own submissions (turned in or not). JSON reading and the due-date conversion are static and network-free so tests
/// cover them. School Google Workspace admins often block third-party apps: a refusal that is not about scopes or the API being
/// off becomes the "your school blocks this app" message.
/// </summary>
public sealed class ClassroomService
{
    public const string BaseUrl = "https://classroom.googleapis.com/v1";
    public const int MaxCourses = 12, PagesPerList = 5, ParallelCourses = 3;
    public const string BlockedMessage = "Your school blocks this app — ask your admin, or use the iCal link of your Classroom calendar instead.";
    public static readonly GoogleApiInfo Info = new("Google Classroom", "Google Classroom API", "Classroom", BlockedMessage);

    private readonly GoogleApiClient api;

    public ClassroomService(GoogleAuth auth, HttpClient? http = null) => api = new GoogleApiClient(auth, Info, http);

    // ---------- Network ----------

    public async Task<List<ClassroomCourse>> ListCoursesAsync(CancellationToken cancel = default)
    {
        var all = new List<ClassroomCourse>();
        string? page = null;
        for (int i = 0; i < PagesPerList; i++)
        {
            all.AddRange(ParseCourses(await api.SendAsync(HttpMethod.Get, CoursesUrl(page), null, cancel).ConfigureAwait(false), out page));
            if (page is null) break;
        }
        return all;
    }

    /// <summary>
    /// The courses (limited to those <paramref name="include"/> accepts, at most 12) with their published assignments and the
    /// student's turned-in state. When only the submissions call fails, the assignments are still returned with an unknown state.
    /// </summary>
    public async Task<ClassroomFetch> FetchAsync(Func<ClassroomCourse, bool>? include, TimeZoneInfo? zone = null, CancellationToken cancel = default)
    {
        var courses = (await ListCoursesAsync(cancel).ConfigureAwait(false)).Where(c => include is null || include(c)).Take(MaxCourses).ToList();
        var perCourse = new List<ClassroomWork>?[courses.Count];
        string? submissionsProblem = null;
        using var gate = new SemaphoreSlim(ParallelCourses);
        await Task.WhenAll(courses.Select(async (course, i) =>
        {
            await gate.WaitAsync(cancel).ConfigureAwait(false);
            try
            {
                var work = new List<ClassroomWork>();
                string? page = null;
                for (int p = 0; p < PagesPerList; p++)
                {
                    work.AddRange(ParseCourseWork(await api.SendAsync(HttpMethod.Get, CourseWorkUrl(course.Id, page), null, cancel).ConfigureAwait(false), course, zone, out page));
                    if (page is null) break;
                }
                try
                {
                    var states = new Dictionary<string, (SubmissionState, bool)>(StringComparer.Ordinal);
                    page = null;
                    for (int p = 0; p < PagesPerList; p++)
                    {
                        foreach (var (id, state) in ParseSubmissions(await api.SendAsync(HttpMethod.Get, SubmissionsUrl(course.Id, page), null, cancel).ConfigureAwait(false), out page)) states[id] = state;
                        if (page is null) break;
                    }
                    work = ApplySubmissions(work, states);
                }
                catch (GoogleApiException ex) when (ex.Problem is GoogleApiProblem.ScopeMissing or GoogleApiProblem.Other or GoogleApiProblem.Blocked)
                {
                    submissionsProblem ??= ex.Problem == GoogleApiProblem.ScopeMissing ? "Turned-in state needs one more permission: press Enable for Classroom in Settings → Connected accounts." : ex.Message;
                }
                perCourse[i] = work;
            }
            finally { gate.Release(); }
        })).ConfigureAwait(false);
        return new ClassroomFetch(courses, perCourse.Where(w => w is not null).SelectMany(w => w!).ToList(), submissionsProblem);
    }

    // ---------- Pure: addresses ----------

    public static string CoursesUrl(string? pageToken = null) =>
        $"{BaseUrl}/courses?studentId=me&courseStates=ACTIVE&pageSize=50" + (pageToken is null ? "" : "&pageToken=" + Uri.EscapeDataString(pageToken));

    public static string CourseWorkUrl(string courseId, string? pageToken = null) =>
        $"{BaseUrl}/courses/{Uri.EscapeDataString(courseId)}/courseWork?courseWorkStates=PUBLISHED&pageSize=100" + (pageToken is null ? "" : "&pageToken=" + Uri.EscapeDataString(pageToken));

    /// <summary>All of the student's submissions in a course ("-" means every assignment).</summary>
    public static string SubmissionsUrl(string courseId, string? pageToken = null) =>
        $"{BaseUrl}/courses/{Uri.EscapeDataString(courseId)}/courseWork/-/studentSubmissions?userId=me&pageSize=100" + (pageToken is null ? "" : "&pageToken=" + Uri.EscapeDataString(pageToken));

    // ---------- Pure: reading ----------

    public static List<ClassroomCourse> ParseCourses(string json, out string? nextPageToken)
    {
        var result = new List<ClassroomCourse>();
        using var doc = JsonDocument.Parse(json);
        nextPageToken = GoogleApiClient.NextPage(doc.RootElement);
        if (!doc.RootElement.TryGetProperty("courses", out var courses) || courses.ValueKind != JsonValueKind.Array) return result;
        foreach (var c in courses.EnumerateArray())
        {
            string id = GoogleApiClient.Str(c, "id"), state = GoogleApiClient.Str(c, "courseState");
            if (id.Length == 0 || (state.Length > 0 && state != "ACTIVE")) continue;
            string name = GoogleApiClient.Str(c, "name").Trim();
            result.Add(new ClassroomCourse { Id = id, Name = name.Length > 0 ? name : "Course", Section = GoogleApiClient.Str(c, "section").Trim(), Link = GoogleApiClient.Str(c, "alternateLink") });
        }
        return result;
    }

    public static List<ClassroomWork> ParseCourseWork(string json, ClassroomCourse course, TimeZoneInfo? zone, out string? nextPageToken)
    {
        var result = new List<ClassroomWork>();
        using var doc = JsonDocument.Parse(json);
        nextPageToken = GoogleApiClient.NextPage(doc.RootElement);
        if (!doc.RootElement.TryGetProperty("courseWork", out var items) || items.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in items.EnumerateArray())
            if (ParseWork(item, course, zone) is { } work) result.Add(work);
        return result;
    }

    /// <summary>One courseWork resource; null for materials (they have nothing to hand in), drafts and deleted work.</summary>
    public static ClassroomWork? ParseWork(JsonElement e, ClassroomCourse course, TimeZoneInfo? zone = null)
    {
        string id = GoogleApiClient.Str(e, "id"), type = GoogleApiClient.Str(e, "workType"), state = GoogleApiClient.Str(e, "state");
        if (id.Length == 0 || type == "MATERIAL" || state is "DRAFT" or "DELETED") return null;
        var due = ToLocalDue(e, zone, out bool hasTime);
        string title = GoogleApiClient.Str(e, "title").Trim();
        return new ClassroomWork
        {
            Id = id,
            CourseId = course.Id,
            CourseName = course.Name,
            Title = title.Length > 0 ? title : "(Untitled)",
            Description = Clip(Squash(GoogleApiClient.Str(e, "description")), 300),
            Link = GoogleApiClient.Str(e, "alternateLink"),
            Due = due,
            HasDueTime = hasTime,
            MaxPoints = e.TryGetProperty("maxPoints", out var points) && points.ValueKind == JsonValueKind.Number && points.TryGetDouble(out double p) ? p : null,
            WorkType = type,
        };
    }

    private static string Squash(string text) => Regex.Replace(text, @"\s+", " ").Trim();
    private static string Clip(string text, int max) => text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";

    /// <summary>
    /// Classroom gives the due moment as a UTC date (dueDate) and an optional UTC time of day (dueTime). With a time, the UTC
    /// moment is converted to local time (a 23:59 deadline in California arrives as the next UTC day). With only a date, that date is
    /// the due day at local midnight and <paramref name="hasTime"/> is false. Zero fields are omitted by Google's JSON, so a missing
    /// "minutes" is 0 and an empty dueTime object is 00:00. Null when there is no usable dueDate.
    /// </summary>
    public static DateTime? ToLocalDue(JsonElement work, TimeZoneInfo? zone, out bool hasTime)
    {
        hasTime = false;
        if (work.ValueKind != JsonValueKind.Object || !work.TryGetProperty("dueDate", out var date) || date.ValueKind != JsonValueKind.Object) return null;
        int year = GoogleApiClient.Int(date, "year"), month = GoogleApiClient.Int(date, "month"), day = GoogleApiClient.Int(date, "day");
        if (year < 1 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(Math.Min(year, 9999), month)) return null;
        if (!work.TryGetProperty("dueTime", out var time) || time.ValueKind != JsonValueKind.Object) return new DateTime(year, month, day);
        int hours = Math.Clamp(GoogleApiClient.Int(time, "hours"), 0, 23), minutes = Math.Clamp(GoogleApiClient.Int(time, "minutes"), 0, 59), seconds = Math.Clamp(GoogleApiClient.Int(time, "seconds"), 0, 59);
        var utc = new DateTime(year, month, day, hours, minutes, seconds, DateTimeKind.Utc);
        hasTime = true;
        return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(utc, zone ?? TimeZoneInfo.Local), DateTimeKind.Unspecified);
    }

    /// <summary>The student's submissions as courseWorkId → (state, late).</summary>
    public static List<(string WorkId, (SubmissionState State, bool Late) Value)> ParseSubmissions(string json, out string? nextPageToken)
    {
        var result = new List<(string, (SubmissionState, bool))>();
        using var doc = JsonDocument.Parse(json);
        nextPageToken = GoogleApiClient.NextPage(doc.RootElement);
        if (!doc.RootElement.TryGetProperty("studentSubmissions", out var items) || items.ValueKind != JsonValueKind.Array) return result;
        foreach (var s in items.EnumerateArray())
        {
            string workId = GoogleApiClient.Str(s, "courseWorkId");
            if (workId.Length == 0) continue;
            var state = GoogleApiClient.Str(s, "state") switch
            {
                "TURNED_IN" => SubmissionState.TurnedIn,
                "RETURNED" => SubmissionState.Returned,
                "NEW" or "CREATED" or "RECLAIMED_BY_STUDENT" => SubmissionState.Open,
                _ => SubmissionState.Unknown,
            };
            result.Add((workId, (state, GoogleApiClient.Bool(s, "late"))));
        }
        return result;
    }

    public static List<ClassroomWork> ApplySubmissions(IEnumerable<ClassroomWork> work, IReadOnlyDictionary<string, (SubmissionState State, bool Late)> states) =>
        work.Select(w => states.TryGetValue(w.Id, out var s) ? w with { State = s.State, Late = s.Late } : w).ToList();

    // ---------- Pure: choosing and importing ----------

    /// <summary>"Courses to include" lines: empty means every course; otherwise a line matches a course id, or part of its name or section (any letter case).</summary>
    public static bool CourseMatches(ClassroomCourse course, IReadOnlyCollection<string> lines)
    {
        if (lines.Count == 0) return true;
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            if (course.Id == line || course.Name.Contains(line, StringComparison.CurrentCultureIgnoreCase) || (course.Section.Length > 0 && (course.Name + " " + course.Section).Contains(line, StringComparison.CurrentCultureIgnoreCase))) return true;
        }
        return false;
    }

    /// <summary>
    /// What "Import into Homework" does with a list of assignments: the ones whose Classroom id is not in <paramref name="knownIds"/> yet
    /// (each id once, in the order given) up to <paramref name="room"/> of them (Homework holds a limited number), and how many were skipped.
    /// </summary>
    public static ImportPlan PlanImport(IEnumerable<ClassroomWork> work, IReadOnlySet<string> knownIds, int room)
    {
        var seen = new HashSet<string>(knownIds, StringComparer.Ordinal);
        var add = new List<ClassroomWork>();
        int duplicates = 0, noRoom = 0;
        foreach (var w in work)
        {
            if (w.Id.Length == 0) continue;
            if (!seen.Add(w.Id)) { duplicates++; continue; }
            if (add.Count >= Math.Max(0, room)) { noRoom++; continue; }
            add.Add(w);
        }
        return new ImportPlan(add, duplicates, noRoom);
    }
}
