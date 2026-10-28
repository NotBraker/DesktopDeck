using DesktopDeck.Widgets;

public static class RoutineTests
{
    private static readonly TimeSpan Four = TimeSpan.FromHours(4);
    private static readonly List<RoutineLogic.RoutineStep> Steps = RoutineLogic.Steps(["Water", "Stretch | 5 min", "", "Water", "Plan"]);

    public static void StepsParse()
    {
        Expect.Equal(3, Steps.Count, "blanks and repeats dropped");
        Expect.Equal(new RoutineLogic.RoutineStep("Stretch", "5 min"), Steps[1], "title | detail");
    }

    public static void ParseTimeIsForgiving()
    {
        Expect.Equal(new TimeSpan(6, 30, 0), RoutineLogic.ParseTime("6:30", TimeSpan.Zero), "short hour");
        Expect.Equal(Four, RoutineLogic.ParseTime("04:00", TimeSpan.Zero), "padded");
        Expect.Equal(Four, RoutineLogic.ParseTime("nonsense", Four), "fallback");
        Expect.Equal(Four, RoutineLogic.ParseTime("", Four), "empty");
    }

    public static void ResetHappensAtResetTime()
    {
        Expect.Equal(new DateTime(2026, 10, 3), RoutineLogic.RoutineDay(new DateTime(2026, 10, 4, 3, 59, 0), Four), "before 4 am is still yesterday");
        Expect.Equal(new DateTime(2026, 10, 4), RoutineLogic.RoutineDay(new DateTime(2026, 10, 4, 4, 0, 0), Four), "from 4 am it is today");
        var state = new RoutineLogic.RoutineState();
        RoutineLogic.Toggle(state, "Water", Steps, new DateTime(2026, 10, 4, 7, 0, 0), Four);
        Expect.True(RoutineLogic.IsDone(state, "Water"), "ticked");
        Expect.True(!RoutineLogic.Normalize(state, new DateTime(2026, 10, 5, 2, 0, 0), Four), "2 am next day: same routine day");
        Expect.True(RoutineLogic.IsDone(state, "Water"), "still ticked");
        Expect.True(RoutineLogic.Normalize(state, new DateTime(2026, 10, 5, 4, 30, 0), Four), "after reset");
        Expect.True(!RoutineLogic.IsDone(state, "Water"), "cleared");
    }

    public static void CompletionAndStreak()
    {
        var state = new RoutineLogic.RoutineState();
        for (int day = 1; day <= 3; day++)
        {
            var at = new DateTime(2026, 10, day, 8, 0, 0);
            RoutineLogic.SetAll(state, true, Steps, at, Four);
        }
        Expect.Equal(3, state.Completed.Count, "three complete days");
        var today = new DateTime(2026, 10, 4, 8, 0, 0);
        Expect.Equal(3, RoutineLogic.Streak(state, RoutineLogic.RoutineDay(today, Four)), "today not finished: streak from yesterday");
        RoutineLogic.Toggle(state, "Water", Steps, today, Four);
        RoutineLogic.Toggle(state, "Stretch", Steps, today, Four);
        Expect.Equal(3, RoutineLogic.Streak(state, today.Date), "partial day doesn't count");
        RoutineLogic.Toggle(state, "Plan", Steps, today, Four);
        Expect.True(RoutineLogic.IsComplete(state, Steps), "complete");
        Expect.Equal(4, RoutineLogic.Streak(state, today.Date), "four in a row");
        RoutineLogic.Toggle(state, "Plan", Steps, today, Four);
        Expect.Equal(3, RoutineLogic.Streak(state, today.Date), "unticking removes today's completion");
        Expect.Equal(3, RoutineLogic.Best(state), "best run");
    }

    public static void StreakBreaksOnGap()
    {
        var state = new RoutineLogic.RoutineState { Completed = ["2026-09-28", "2026-09-29", "2026-10-02", "2026-10-03"] };
        Expect.Equal(2, RoutineLogic.Streak(state, new DateTime(2026, 10, 4)), "gap ends the streak");
        Expect.Equal(0, RoutineLogic.Streak(state, new DateTime(2026, 10, 6)), "missed yesterday");
        Expect.Equal(2, RoutineLogic.Best(state), "best");
    }

    public static void ActiveWindow()
    {
        var six = TimeSpan.FromHours(6);
        var ten = TimeSpan.FromHours(10);
        Expect.True(RoutineLogic.IsActive(TimeSpan.FromHours(7), six, ten), "inside");
        Expect.True(!RoutineLogic.IsActive(TimeSpan.FromHours(10), six, ten), "end is exclusive");
        Expect.True(!RoutineLogic.IsActive(TimeSpan.FromHours(5), six, ten), "before");
        Expect.True(RoutineLogic.IsActive(TimeSpan.FromHours(23), TimeSpan.FromHours(21), TimeSpan.FromHours(1)), "crosses midnight");
        Expect.True(RoutineLogic.IsActive(TimeSpan.FromMinutes(30), TimeSpan.FromHours(21), TimeSpan.FromHours(1)), "after midnight");
        Expect.True(RoutineLogic.IsActive(TimeSpan.FromHours(15), six, six), "same start and end = always");
    }

    public static void JsonRoundTrip()
    {
        var state = new RoutineLogic.RoutineState { Day = "2026-10-04", Done = ["Water"], Completed = ["2026-10-03"] };
        var back = RoutineLogic.Parse(RoutineLogic.Serialize(state));
        Expect.Equal("2026-10-04", back.Day, "day");
        Expect.Equal("Water", back.Done.Single(), "done");
        Expect.Equal(0, RoutineLogic.Parse("[1,2]").Done.Count, "wrong shape");
    }
}
