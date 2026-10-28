using System.Globalization;
using DesktopDeck.Models;
using DesktopDeck.Widgets;

/// <summary>Classroom widget logic (ClassroomLogic.cs) and "Import into Homework", including the Homework data format.</summary>
public static class ClassroomTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 10, 0, 0);
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    private static ClassroomWork W(string id, string course, string title, DateTime? due, bool time = false, SubmissionState state = SubmissionState.Open, bool late = false) =>
        new() { Id = id, CourseId = "c-" + course, CourseName = course, Title = title, Due = due, HasDueTime = time && due is not null, State = state, Late = late };

    private static ClassroomOptions Options(int days = 14, int overdue = 7, bool hideTurnedIn = true, bool noDue = false, int max = 50, string group = "date", params string[] courses) =>
        new(days, overdue, hideTurnedIn, noDue, max, group, courses);

    private static List<ClassroomWork> Sample() =>
    [
        W("late", "Math", "Late set", Now.Date.AddDays(-1).AddHours(23).AddMinutes(59), true),
        W("old", "Math", "Ancient", Now.Date.AddDays(-30), false),
        W("today", "Chem", "Lab", Now.Date.AddHours(15), true),
        W("tomorrow", "Hist", "Reading", Now.Date.AddDays(1)),
        W("done", "Math", "Quiz", Now.Date.AddDays(2), false, SubmissionState.TurnedIn),
        W("lateTurnedIn", "Chem", "Pre-lab", Now.Date.AddDays(-2).AddHours(12), true, SubmissionState.TurnedIn, late: true),
        W("far", "Eng", "Essay", Now.Date.AddDays(40)),
        W("nodue", "Eng", "Extra", null),
        W("unknown", "Hist", "Mystery", Now.Date.AddDays(-1).AddHours(12), true, SubmissionState.Unknown),
    ];

    public static void CacheRoundTrips()
    {
        var courses = new[] { new ClassroomCourse { Id = "c1", Name = "Math", Section = "P2", Link = "https://classroom.google.com/c/1" } };
        string json = ClassroomLogic.ToCache(courses, Sample(), "note");
        var back = ClassroomLogic.FromCache(json);
        Expect.Equal(1, back.Courses.Count, "courses");
        Expect.Equal(Sample().Count, back.Work.Count, "work");
        Expect.True(back.Work[0].Due == Sample()[0].Due && back.Work[0].HasDueTime && back.Work[4].State == SubmissionState.TurnedIn && back.Work[5].Late, "due moment, state and late flag survive");
        Expect.Equal("note", back.SubmissionsProblem, "problem note");
        Expect.Equal(0, ClassroomLogic.FromCache("{oops").Work.Count, "bad json");
        Expect.Equal(0, ClassroomLogic.FromCache(null).Courses.Count, "null");
    }

    public static void MissingMeansPastDueAndNotHandedIn()
    {
        var w = Sample();
        Expect.True(ClassroomLogic.IsMissing(w[0], Now), "late and open is missing");
        Expect.True(!ClassroomLogic.IsMissing(w[2], Now), "due this afternoon is not missing yet");
        Expect.True(ClassroomLogic.IsMissing(w[2], Now.AddHours(6)), "missing once the time passes");
        Expect.True(!ClassroomLogic.IsMissing(w[3], Now.AddHours(34)), "a date-only assignment is due until the end of that day");
        Expect.True(ClassroomLogic.IsMissing(w[3], Now.AddDays(2)), "and missing the day after");
        Expect.True(!ClassroomLogic.IsMissing(w[5], Now), "turned in is never missing");
        Expect.True(!ClassroomLogic.IsMissing(w[8], Now), "unknown state is not called missing");
        Expect.True(!ClassroomLogic.IsMissing(w[7], Now), "no due date is never missing");
    }

    public static void StatusWording()
    {
        var w = Sample();
        Expect.Equal(ClassroomLogic.StatusKind.Missing, ClassroomLogic.StatusOf(w[0], Now).Kind, "missing");
        Expect.Equal(ClassroomLogic.StatusKind.DueToday, ClassroomLogic.StatusOf(w[2], Now).Kind, "due today");
        var soon = ClassroomLogic.StatusOf(w[3], Now);
        Expect.True(soon.Kind == ClassroomLogic.StatusKind.Soon && soon.Text == "Due tomorrow", "due tomorrow");
        Expect.Equal(ClassroomLogic.StatusKind.None, ClassroomLogic.StatusOf(w[3], Now, 0).Kind, "soon can be switched off");
        Expect.Equal(ClassroomLogic.StatusKind.TurnedIn, ClassroomLogic.StatusOf(w[4], Now).Kind, "turned in");
        var late = ClassroomLogic.StatusOf(w[5], Now);
        Expect.True(late.Kind == ClassroomLogic.StatusKind.TurnedInLate && late.Text == "Turned in late", "turned in late");
        Expect.Equal(ClassroomLogic.StatusKind.Returned, ClassroomLogic.StatusOf(w[0] with { State = SubmissionState.Returned }, Now).Kind, "returned");
        Expect.Equal(ClassroomLogic.StatusKind.None, ClassroomLogic.StatusOf(w[6], Now).Kind, "far away");
    }

    public static void DueTextShowsDayAndTime()
    {
        var w = Sample();
        Expect.Equal("Today 3 PM", ClassroomLogic.DueText(w[2], Now, false, Culture), "today with a time");
        Expect.Equal("Today 15:00", ClassroomLogic.DueText(w[2], Now, true, Culture), "24-hour");
        Expect.Equal("Tomorrow", ClassroomLogic.DueText(w[3], Now, false, Culture), "date only");
        Expect.Equal("Yesterday 11:59 PM", ClassroomLogic.DueText(w[0], Now, false, Culture), "yesterday with a time");
        Expect.Equal("Oct 15", ClassroomLogic.DueText(W("x", "A", "t", Now.Date.AddDays(10)), Now, false, Culture), "a date further away");
        Expect.Equal("Fri", ClassroomLogic.DueText(W("x", "A", "t", Now.Date.AddDays(4)), Now, false, Culture), "weekday within the week");
        Expect.Equal("No due date", ClassroomLogic.DueText(w[7], Now, false, Culture), "none");
    }

    public static void WindowAndFiltersChooseWhatToShow()
    {
        var ids = (IEnumerable<ClassroomWork> list) => list.Select(x => x.Id).ToList();
        var shown = ids(ClassroomLogic.Candidates(Sample(), Now, Options(), includeTurnedIn: false));
        Expect.True(shown.SequenceEqual(["unknown", "late", "today", "tomorrow"]), "open work from the last week to two weeks ahead (no ancient, far, turned-in or undated work): " + string.Join(",", shown));
        var withDone = ids(ClassroomLogic.Candidates(Sample(), Now, Options(hideTurnedIn: false), includeTurnedIn: true));
        Expect.True(withDone.Contains("done") && !withDone.Contains("lateTurnedIn"), "turned-in work shows when it is due ahead, but not once it is past");
        Expect.True(ids(ClassroomLogic.Candidates(Sample(), Now, Options(noDue: true), false)).Contains("nodue"), "undated work on request");
        Expect.True(!ids(ClassroomLogic.Candidates(Sample(), Now, Options(overdue: 0), false)).Contains("late"), "overdue can be hidden");
        Expect.True(ids(ClassroomLogic.Candidates(Sample(), Now, Options(days: 60), false)).Contains("far"), "a longer window");
        Expect.True(ids(ClassroomLogic.Candidates(Sample(), Now, Options(days: 1, hideTurnedIn: false), true)).SequenceEqual(["unknown", "late", "today", "tomorrow"]), "a one-day window stops after tomorrow");
        var chem = ids(ClassroomLogic.Candidates(Sample(), Now, Options(courses: "chem"), false));
        Expect.True(chem.SequenceEqual(["today"]), "course filter by part of the name");
    }

    public static void ArrangesByDateCourseOrNothing()
    {
        var byDate = ClassroomLogic.Arrange(Sample(), Now, Options(), Culture);
        Expect.True(byDate.Sections.Select(s => s.Title).SequenceEqual(["Overdue", "Today", "Tomorrow"]), "date groups: " + string.Join(",", byDate.Sections.Select(s => s.Title)));
        Expect.Equal(2, byDate.Sections[0].Items.Count, "overdue holds the missing and the unknown-state late work");
        var byCourse = ClassroomLogic.Arrange(Sample(), Now, Options(group: "course"), Culture);
        Expect.True(byCourse.Sections.Select(s => s.Title).SequenceEqual(["Chem", "Hist", "Math"]), "course groups in name order");
        var flat = ClassroomLogic.Arrange(Sample(), Now, Options(group: "none"), Culture);
        Expect.True(flat.Sections.Count == 1 && flat.Sections[0].Title == "", "one flat group");
        var limited = ClassroomLogic.Arrange(Sample(), Now, Options(max: 2), Culture);
        Expect.True(limited.Sections.Sum(s => s.Items.Count) == 2 && limited.More == 2 && limited.Total == 4, "the item limit leaves a '+2 more'");
        var later = ClassroomLogic.Arrange([W("a", "Math", "Far-ish", Now.Date.AddDays(5))], Now, Options(), Culture);
        Expect.Equal("Saturday, Oct 10", later.Sections[0].Title, "later days are named");
        Expect.Equal(0, ClassroomLogic.Arrange([], Now, Options(), Culture).Sections.Count, "nothing to show");
        Expect.Equal(4, ClassroomLogic.OpenCount(Sample(), Now, Options()), "open count for the badge");
    }

    public static void OrderIsByDueMomentThenCourseThenTitle()
    {
        var list = new List<ClassroomWork> { W("3", "B", "z", Now.Date.AddDays(2)), W("1", "A", "b", Now.Date.AddDays(1).AddHours(9), true), W("2", "A", "a", Now.Date.AddDays(1).AddHours(9), true), W("4", "A", "nodue", null) };
        var order = ClassroomLogic.Candidates(list, Now, Options(noDue: true), false).Select(w => w.Id).ToList();
        Expect.True(order.SequenceEqual(["2", "1", "3", "4"]), "by due moment, then title, with undated last: " + string.Join(",", order));
    }

    public static void CourseColorsAreStableAndOverridable()
    {
        string a = ClassroomLogic.ColorFor("c-1", "Math", []), b = ClassroomLogic.ColorFor("c-1", "Math", []);
        Expect.Equal(a, b, "stable");
        Expect.True(ClassroomLogic.Palette.Contains(a), "from the palette");
        var overrides = ClassroomLogic.ParseCourseColors(["chem | #ec4899", "bad", "x | nope"]);
        Expect.Equal(1, overrides.Count, "valid override lines only");
        Expect.Equal("#ec4899", ClassroomLogic.ColorFor("c-2", "AP Chemistry", overrides), "override matches part of the name");
        Expect.Equal("#ec4899", ClassroomLogic.ColorFor("c-2", "Other", ClassroomLogic.ParseCourseColors(["c-2 | #ec4899"])), "or the course id");
    }

    // ---------- Import into Homework ----------

    public static void ImportCopiesOpenWorkIntoHomeworkWithoutDoubles()
    {
        var candidates = ClassroomLogic.ImportCandidates(Sample(), Now, Options(hideTurnedIn: false));
        Expect.True(candidates.All(w => !ClassroomLogic.IsDone(w)), "turned-in work is not imported");
        var list = new List<HomeworkLogic.Assignment> { HomeworkLogic.Create("Typed myself", "Math", Now.Date) };
        var first = ClassroomLogic.AddTo(list, candidates);
        Expect.Equal(4, first.Added, "four open assignments added");
        Expect.Equal(0, first.Duplicates, "none yet imported");
        var lab = list.Single(a => a.ClassroomId == "today");
        Expect.True(lab.Title == "Lab" && lab.Subject == "Chem" && lab.Due == "2026-10-05" && lab.Time == "15:00" && !lab.Done, "title, course as subject, local date and time");
        var reading = list.Single(a => a.ClassroomId == "tomorrow");
        Expect.True(reading.Due == "2026-10-06" && reading.Time == "", "a date-only assignment has no time");
        list.Single(a => a.ClassroomId == "today").Done = true;
        list.Single(a => a.ClassroomId == "late").Title = "Renamed by me";
        var second = ClassroomLogic.AddTo(list, candidates);
        Expect.True(second.Added == 0 && second.Duplicates == 4, "importing again adds nothing, even for finished or renamed ones");
        Expect.Equal(5, list.Count, "list unchanged");
        Expect.Equal(4, ClassroomLogic.KnownIds(list).Count, "known ids");
    }

    public static void ImportRespectsHomeworksSizeLimit()
    {
        var list = Enumerable.Range(0, HomeworkLogic.MaxItems - 1).Select(i => HomeworkLogic.Create("t" + i, "", null)).ToList();
        var result = ClassroomLogic.AddTo(list, [W("a", "A", "one", Now), W("b", "A", "two", Now), W("c", "A", "three", Now)]);
        Expect.True(result.Added == 1 && result.NoRoom == 2 && list.Count == HomeworkLogic.MaxItems, "only what fits is added");
    }

    public static void HomeworkKeepsTheClassroomIdThroughSaving()
    {
        var a = ClassroomLogic.ToAssignment(W("abc123", "Math", "Set 4", Now.Date.AddDays(3)));
        var back = HomeworkLogic.Parse(HomeworkLogic.Serialize([a, HomeworkLogic.Create("Typed", "", null)]));
        Expect.Equal("abc123", back[0].ClassroomId, "id kept");
        Expect.Equal("", back[1].ClassroomId, "typed assignments have none");
        var old = HomeworkLogic.Parse("""[{"id":"x","title":"Old file","subject":"","due":"","time":"","priority":1,"done":false,"doneAt":""}]""");
        Expect.Equal("", old[0].ClassroomId, "older data loads with an empty id");
    }
}
