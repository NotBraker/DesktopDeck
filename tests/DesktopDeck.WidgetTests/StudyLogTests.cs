using DesktopDeck.Widgets;

public static class StudyLogTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 20, 0, 0); // a Monday evening
    private static readonly List<StudySubjectsLogic.Subject> Subjects = StudySubjectsLogic.Parse(StudySubjectsLogic.DefaultSubjects.Split('\n'));

    private static StudyLogLogic.Session S(DateTime start, double minutes, string subject = "Math") => new()
    {
        Start = StudyLogLogic.FormatStamp(start),
        End = StudyLogLogic.FormatStamp(start.AddMinutes(minutes)),
        Subject = subject,
    };

    public static void SessionsRoundTripAndRepair()
    {
        var list = new List<StudyLogLogic.Session> { S(Now.AddHours(-3), 45, "Latin"), S(Now.AddHours(-1), 30) };
        var back = StudyLogLogic.ParseSessions(StudyLogLogic.SerializeSessions(list));
        Expect.Equal(2, back.Count, "count");
        Expect.Equal("Latin", back[0].Subject, "subject");
        Expect.Near(45, back[0].Length.TotalMinutes, 1e-9, "length");
        Expect.Equal(0, StudyLogLogic.ParseSessions("nope").Count, "garbage");
        var bad = StudyLogLogic.ParseSessions("[{\"s\":\"2026-10-05T10:00:00\",\"e\":\"2026-10-05T09:00:00\",\"u\":\"x\"},{\"s\":\"junk\",\"e\":\"junk\"},{\"s\":\"2026-10-01T10:00:00\",\"e\":\"2026-10-05T10:00:00\",\"u\":\"days\"}]");
        Expect.Equal(0, bad.Count, "backwards, unparseable and day-long sessions are dropped");
        var many = Enumerable.Range(0, StudyLogLogic.MaxSessions + 50).Select(i => S(new DateTime(2026, 1, 1).AddMinutes(i * 90), 30)).ToList();
        Expect.Equal(StudyLogLogic.MaxSessions, StudyLogLogic.ParseSessions(StudyLogLogic.SerializeSessions(many)).Count, "history is capped");
    }

    public static void RunSurvivesRestart()
    {
        var options = new Dictionary<string, string>();
        Expect.True(StudyLogLogic.ReadRun(options) is null, "nothing running");
        StudyLogLogic.WriteRun(options, new StudyLogLogic.Run(Now.AddMinutes(-20), "Chem"));
        var run = StudyLogLogic.ReadRun(options)!;
        Expect.Equal("Chem", run.Subject, "subject");
        Expect.Equal(Now.AddMinutes(-20), run.Start, "start");
        StudyLogLogic.WriteRun(options, null);
        Expect.True(StudyLogLogic.ReadRun(options) is null, "cleared");
    }

    public static void FinishingMakesASession()
    {
        var run = new StudyLogLogic.Run(Now.AddMinutes(-25), "Latin");
        var session = StudyLogLogic.Finish(run, Now)!;
        Expect.Near(25, session.Length.TotalMinutes, 1e-9, "25 minutes");
        Expect.Equal("Latin", session.Subject, "subject kept");
        Expect.True(StudyLogLogic.Finish(new StudyLogLogic.Run(Now.AddSeconds(-30), "x"), Now, 60) is null, "too short is dropped");
        Expect.True(StudyLogLogic.Finish(new StudyLogLogic.Run(Now.AddSeconds(-30), "x"), Now, 10) is not null, "a lower minimum keeps it");
    }

    public static void ForgottenSessionsAreCutOff()
    {
        var run = new StudyLogLogic.Run(Now.AddHours(-9), "Math");
        Expect.Near(4, StudyLogLogic.Finish(run, Now, 60, 4)!.Length.TotalHours, 1e-9, "cut at four hours");
        Expect.Near(9, StudyLogLogic.Finish(run, Now, 60, 0)!.Length.TotalHours, 1e-9, "no limit");
        Expect.True(StudyLogLogic.IsOverLimit(run, Now, 4), "over the limit");
        Expect.True(!StudyLogLogic.IsOverLimit(run, Now, 0), "no limit never trips");
        var totals = StudyLogLogic.TotalsBySubject([], run, Now, Now.Date, Now.Date.AddDays(1), 4);
        Expect.Near(4, totals["Math"].TotalHours, 1e-9, "a running session counts only up to the limit");
    }

    public static void TotalsPerSubjectIncludeTheRunningSession()
    {
        var sessions = new List<StudyLogLogic.Session> { S(Now.AddHours(-5), 60, "Math"), S(Now.AddHours(-3), 30, "Latin"), S(Now.AddHours(-2), 15, "math") };
        var run = new StudyLogLogic.Run(Now.AddMinutes(-20), "Chem");
        var totals = StudyLogLogic.TotalsBySubject(sessions, run, Now, Now.Date, Now.Date.AddDays(1));
        Expect.Near(75, totals["Math"].TotalMinutes, 1e-9, "subjects match ignoring case");
        Expect.Near(30, totals["Latin"].TotalMinutes, 1e-9, "latin");
        Expect.Near(20, totals["Chem"].TotalMinutes, 1e-9, "the running session");
        Expect.Near(125, StudyLogLogic.Total(sessions, run, Now, Now.Date, Now.Date.AddDays(1)).TotalMinutes, 1e-9, "total");
    }

    public static void SessionsSplitAtMidnight()
    {
        var late = S(new DateTime(2026, 10, 4, 23, 30, 0), 90, "Math");
        var yesterday = StudyLogLogic.Total([late], null, Now, new DateTime(2026, 10, 4), new DateTime(2026, 10, 5));
        var today = StudyLogLogic.Total([late], null, Now, new DateTime(2026, 10, 5), new DateTime(2026, 10, 6));
        Expect.Near(30, yesterday.TotalMinutes, 1e-9, "30 minutes before midnight");
        Expect.Near(60, today.TotalMinutes, 1e-9, "60 minutes after");
        var perDay = StudyLogLogic.PerDay([late], null, Now, new DateTime(2026, 10, 3), 3);
        Expect.Equal("0,30,60", string.Join(',', perDay.Select(d => (int)d.TotalMinutes)), "per day");
    }

    public static void WeekStartsWhereTheUserSays()
    {
        Expect.Equal(new DateTime(2026, 10, 5), StudyLogLogic.WeekStart(Now, DayOfWeek.Monday), "monday weeks, on a monday");
        Expect.Equal(new DateTime(2026, 10, 4), StudyLogLogic.WeekStart(Now, DayOfWeek.Sunday), "sunday weeks");
        Expect.Equal(new DateTime(2026, 10, 3), StudyLogLogic.WeekStart(Now, DayOfWeek.Saturday), "saturday weeks");
        Expect.Equal(new DateTime(2026, 10, 5), StudyLogLogic.WeekStart(new DateTime(2026, 10, 11, 23, 0, 0), DayOfWeek.Monday), "the end of a week");
        Expect.Equal(DayOfWeek.Sunday, StudyLogLogic.WeekStartOption("sunday"), "option");
        Expect.Equal(DayOfWeek.Monday, StudyLogLogic.WeekStartOption("whatever"), "default");
    }

    public static void StreakCountsDaysInARow()
    {
        var sessions = new List<StudyLogLogic.Session>
        {
            S(Now.Date.AddDays(-3).AddHours(10), 30), S(Now.Date.AddDays(-2).AddHours(10), 30), S(Now.Date.AddDays(-1).AddHours(10), 30),
        };
        var noToday = StudyLogLogic.Streak(sessions, null, Now, 10);
        Expect.Equal(3, noToday.Current, "yesterday and before still count while today is open");
        sessions.Add(S(Now.Date.AddHours(9), 12));
        Expect.Equal(4, StudyLogLogic.Streak(sessions, null, Now, 10).Current, "today joins in");
        Expect.Equal(3, StudyLogLogic.Streak(sessions, null, Now, 20).Current, "a higher minimum leaves today out until it is reached");
    }

    public static void StreakBreaksOnAMissedDayAndTracksTheBest()
    {
        var sessions = new List<StudyLogLogic.Session>();
        foreach (int back in new[] { 20, 19, 18, 17, 16, 2, 1 }) sessions.Add(S(Now.Date.AddDays(-back).AddHours(10), 30));
        var (current, best) = StudyLogLogic.Streak(sessions, null, Now, 10);
        Expect.Equal(2, current, "two days in a row");
        Expect.Equal(5, best, "best run");
        Expect.Equal(0, StudyLogLogic.Streak([], null, Now, 10).Current, "no history");
        var skipped = StudyLogLogic.Streak([S(Now.Date.AddDays(-2).AddHours(10), 30)], null, Now, 10);
        Expect.Equal(0, skipped.Current, "a missed yesterday ends it");
    }

    public static void ShortStudyDoesNotCountForTheStreak()
    {
        var sessions = new List<StudyLogLogic.Session> { S(Now.Date.AddDays(-1).AddHours(10), 4) };
        Expect.Equal(0, StudyLogLogic.Streak(sessions, null, Now, 10).Current, "4 minutes is under the minimum");
        Expect.Equal(1, StudyLogLogic.Streak(sessions, null, Now, 3).Current, "but counts with a lower one");
    }

    public static void RunningTimeCountsForTodaysStreak()
    {
        var run = new StudyLogLogic.Run(Now.AddMinutes(-15), "Math");
        Expect.Equal(1, StudyLogLogic.Streak([], run, Now, 10).Current, "fifteen live minutes make today count");
    }

    public static void GoalFraction()
    {
        Expect.Near(0.5, StudyLogLogic.GoalFraction(TimeSpan.FromHours(5), 10), 1e-9, "half");
        Expect.Near(1, StudyLogLogic.GoalFraction(TimeSpan.FromHours(12), 10), 1e-9, "capped");
        Expect.Near(0, StudyLogLogic.GoalFraction(TimeSpan.FromHours(3), 0), 1e-9, "no goal");
    }

    public static void DurationsFormatPlainly()
    {
        Expect.Equal("0m", StudyLogLogic.FormatDuration(TimeSpan.FromSeconds(50)), "under a minute");
        Expect.Equal("45m", StudyLogLogic.FormatDuration(TimeSpan.FromMinutes(45)), "minutes");
        Expect.Equal("1h", StudyLogLogic.FormatDuration(TimeSpan.FromMinutes(60)), "round hour");
        Expect.Equal("2h 05m", StudyLogLogic.FormatDuration(TimeSpan.FromMinutes(125)), "hours and minutes");
        Expect.Equal("2h 10m", StudyLogLogic.FormatDuration(TimeSpan.FromMinutes(128), 5), "rounded to five");
        Expect.Equal("2h 15m", StudyLogLogic.FormatDuration(TimeSpan.FromMinutes(128), 15), "rounded to fifteen");
        Expect.Equal("0m", StudyLogLogic.FormatDuration(TimeSpan.FromMinutes(-3)), "never negative");
        Expect.Equal("1:05:09", StudyLogLogic.FormatClock(TimeSpan.FromSeconds(3909)), "clock");
        Expect.Equal("6.5", StudyLogLogic.FormatHours(TimeSpan.FromHours(6.5)), "hours");
    }

    public static void ParsesTypedLengths()
    {
        Expect.Equal(45, StudyLogLogic.ParseMinutes("45"), "bare minutes");
        Expect.Equal(45, StudyLogLogic.ParseMinutes("45m"), "m");
        Expect.Equal(90, StudyLogLogic.ParseMinutes("1h30"), "1h30");
        Expect.Equal(90, StudyLogLogic.ParseMinutes("1h 30m"), "1h 30m");
        Expect.Equal(90, StudyLogLogic.ParseMinutes("1.5h"), "1.5h");
        Expect.Equal(120, StudyLogLogic.ParseMinutes("2 hours"), "2 hours");
        Expect.Equal(30, StudyLogLogic.ParseMinutes("30 min"), "30 min");
        Expect.True(StudyLogLogic.ParseMinutes("math") is null, "a subject is not a length");
        Expect.True(StudyLogLogic.ParseMinutes("0") is null, "zero");
        Expect.True(StudyLogLogic.ParseMinutes("5000") is null, "more than a day");
        Expect.True(StudyLogLogic.ParseMinutes("") is null, "empty");
    }

    public static void ParsesTypedSessions()
    {
        var a = StudyLogLogic.ParseManual("45 math", Subjects)!.Value;
        Expect.Equal(("Math", 45), (a.Subject, a.Minutes), "length first");
        var b = StudyLogLogic.ParseManual("latin 1h30", Subjects)!.Value;
        Expect.Equal(("Latin", 90), (b.Subject, b.Minutes), "subject first");
        var c = StudyLogLogic.ParseManual("history 1h 30m", Subjects)!.Value;
        Expect.Equal(("History", 90), (c.Subject, c.Minutes), "spaced length");
        var d = StudyLogLogic.ParseManual("30 min art history", Subjects)!.Value;
        Expect.Equal(("art history", 30), (d.Subject, d.Minutes), "unknown subjects are kept as typed");
        var e = StudyLogLogic.ParseManual("20", Subjects)!.Value;
        Expect.Equal(("", 20), (e.Subject, e.Minutes), "no subject");
        Expect.True(StudyLogLogic.ParseManual("chem", Subjects) is null, "no length");
        Expect.True(StudyLogLogic.ParseManual("", Subjects) is null, "empty");
    }

    public static void ManualSessionEndsNow()
    {
        var s = StudyLogLogic.Manual(" Chem ", 40, Now);
        Expect.Equal(Now, s.EndTime, "ends now");
        Expect.Near(40, s.Length.TotalMinutes, 1e-9, "length");
        Expect.Equal("Chem", s.Subject, "trimmed");
    }

    public static void HistoryKeepsTheChosenDays()
    {
        var sessions = new List<StudyLogLogic.Session>
        {
            new() { Start = "2026-10-04T10:00:00", End = "2026-10-04T11:00:00", Subject = "Math" },
            new() { Start = "2026-08-01T10:00:00", End = "2026-08-01T11:00:00", Subject = "Math" },
        };
        var today = new DateTime(2026, 10, 5);
        Expect.Equal(2, StudyLogLogic.ParseSessions(StudyLogLogic.SerializeSessions(sessions)).Count, "no day limit keeps all");
        Expect.Equal(2, StudyLogLogic.ParseSessions(StudyLogLogic.SerializeSessions(sessions, today, 0)).Count, "0 days keeps all");
        Expect.Equal(1, StudyLogLogic.ParseSessions(StudyLogLogic.SerializeSessions(sessions, today, 30)).Count, "a 30-day limit drops the older session");
    }
}
