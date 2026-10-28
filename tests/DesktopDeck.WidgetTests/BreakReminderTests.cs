using DesktopDeck.Widgets;

public static class BreakReminderTests
{
    private static readonly TimeSpan Nine = TimeSpan.FromHours(9), Six = TimeSpan.FromHours(18);
    // Monday 5 October 2026.
    private static readonly DateTime Monday = new(2026, 10, 5);

    private static BreakReminderSettings Cfg(bool activeOnly = false, int stretchEvery = 0, bool weekdays = false)
        => new(20, 20, stretchEvery, 120, activeOnly, Nine, Six, weekdays);

    private static BreakReminderState Start(DateTime now, BreakReminderSettings cfg)
        => BreakReminderLogic.Step(BreakReminderLogic.Initial(now), now, cfg, out _);

    // ---------- HealthLogic: times and windows ----------

    public static void ParseTimes()
    {
        Expect.Equal(new TimeSpan(18, 30, 0), HealthLogic.ParseTime("18:30", TimeSpan.Zero), "24-hour");
        Expect.Equal(new TimeSpan(18, 30, 0), HealthLogic.ParseTime("6:30 pm", TimeSpan.Zero), "12-hour");
        Expect.Equal(new TimeSpan(6, 5, 0), HealthLogic.ParseTime(" 6:05 ", TimeSpan.Zero), "no leading zero");
        Expect.Equal(new TimeSpan(7, 0, 0), HealthLogic.ParseTime("nonsense", new TimeSpan(7, 0, 0)), "falls back");
        Expect.Equal(new TimeSpan(7, 0, 0), HealthLogic.ParseTime("", new TimeSpan(7, 0, 0)), "empty falls back");
        Expect.Equal(new TimeSpan(7, 0, 0), HealthLogic.ParseTime("25:99", new TimeSpan(7, 0, 0)), "out of range falls back");
    }

    public static void WindowsAndWeekdays()
    {
        Expect.True(HealthLogic.IsActive(Monday.AddHours(10), Nine, Six, false), "inside");
        Expect.True(!HealthLogic.IsActive(Monday.AddHours(8), Nine, Six, false), "before");
        Expect.True(!HealthLogic.IsActive(Monday.AddHours(18), Nine, Six, false), "end is exclusive");
        Expect.True(!HealthLogic.IsActive(Monday.AddDays(5).AddHours(10), Nine, Six, true), "Saturday with weekdays only");
        Expect.True(HealthLogic.IsActive(Monday.AddHours(3), TimeSpan.FromHours(22), TimeSpan.FromHours(6), false), "window past midnight");
        Expect.True(HealthLogic.IsActive(Monday.AddHours(12), TimeSpan.FromHours(9), TimeSpan.FromHours(9), false), "equal times mean all day");
    }

    public static void NextActiveFindsTheNextStart()
    {
        Expect.Equal(Monday.AddHours(10), HealthLogic.NextActive(Monday.AddHours(10), Nine, Six, false), "already active");
        Expect.Equal(Monday.AddHours(9), HealthLogic.NextActive(Monday.AddHours(7), Nine, Six, false), "later today");
        Expect.Equal(Monday.AddDays(1).AddHours(9), HealthLogic.NextActive(Monday.AddHours(20), Nine, Six, false), "tomorrow");
        var friday = Monday.AddDays(4);
        Expect.Equal(Monday.AddDays(7).AddHours(9), HealthLogic.NextActive(friday.AddHours(20), Nine, Six, true), "Monday after a weekend");
    }

    public static void SchedulesOnWholeMinutes()
    {
        var at = new DateTime(2026, 10, 5, 10, 0, 30);
        Expect.Equal(new DateTime(2026, 10, 5, 10, 21, 0), HealthLogic.ScheduleAfter(at, TimeSpan.FromMinutes(20), false, Nine, Six, false), "rounded up to the minute");
        Expect.Equal(new DateTime(2026, 10, 5, 10, 20, 0), HealthLogic.ScheduleAfter(new DateTime(2026, 10, 5, 10, 0, 0), TimeSpan.FromMinutes(20), false, Nine, Six, false), "exact stays");
    }

    public static void OutsideActiveHoursWaitsForTheWindowPlusAnInterval()
    {
        var evening = Monday.AddHours(17).AddMinutes(50);
        Expect.Equal(Monday.AddDays(1).AddHours(9).AddMinutes(20), HealthLogic.ScheduleAfter(evening, TimeSpan.FromMinutes(20), true, Nine, Six, false), "next morning + interval");
        var night = Monday.AddHours(23);
        Expect.Equal(Monday.AddDays(1).AddHours(9).AddMinutes(20), HealthLogic.ScheduleAfter(night, TimeSpan.FromMinutes(20), true, Nine, Six, false), "after hours");
        Expect.Equal(Monday.AddHours(10).AddMinutes(20), HealthLogic.ScheduleAfter(Monday.AddHours(10), TimeSpan.FromMinutes(20), true, Nine, Six, false), "inside hours is plain");
    }

    public static void SpanText()
    {
        Expect.Equal("12 m", HealthLogic.FormatSpan(TimeSpan.FromMinutes(12)), "minutes");
        Expect.Equal("1 h 05 m", HealthLogic.FormatSpan(TimeSpan.FromMinutes(65)), "hours");
        Expect.Equal("1 m", HealthLogic.FormatSpan(TimeSpan.FromSeconds(20)), "under a minute rounds up");
        Expect.Equal("0:34", HealthLogic.FormatSpan(TimeSpan.FromSeconds(34), true), "seconds");
        Expect.Equal("0 m", HealthLogic.FormatSpan(TimeSpan.FromSeconds(-3)), "negative");
    }

    // ---------- The state machine ----------

    public static void FirstStepSchedulesTheFirstBreak()
    {
        var now = Monday.AddHours(10);
        var s = Start(now, Cfg());
        Expect.Equal(now.AddMinutes(20), s.NextDue, "one interval away");
    }

    public static void BreakStartsEndsAndCounts()
    {
        var cfg = Cfg();
        var now = Monday.AddHours(10);
        var s = Start(now, cfg);
        s = BreakReminderLogic.Step(s, now.AddMinutes(19), cfg, out var ev);
        Expect.Equal(BreakReminderEvent.None, ev, "nothing before it is due");
        s = BreakReminderLogic.Step(s, now.AddMinutes(20), cfg, out ev);
        Expect.Equal(BreakReminderEvent.BreakStarted, ev, "due");
        Expect.True(s.NextDue is null && s.BreakEnd == now.AddMinutes(20).AddSeconds(20), "break is 20 seconds");
        s = BreakReminderLogic.Step(s, now.AddMinutes(20).AddSeconds(10), cfg, out ev);
        Expect.Equal(BreakReminderEvent.None, ev, "break still on");
        s = BreakReminderLogic.Step(s, now.AddMinutes(20).AddSeconds(21), cfg, out ev);
        Expect.Equal(BreakReminderEvent.BreakEnded, ev, "break over");
        Expect.Equal(1, s.Taken, "counted");
        Expect.Equal(now.AddMinutes(41), s.NextDue, "next one interval after the break ended, on a minute");
    }

    public static void EveryThirdBreakIsAStretch()
    {
        var cfg = Cfg(stretchEvery: 3);
        var now = Monday.AddHours(10);
        var s = Start(now, cfg);
        var kinds = new List<bool>();
        for (int i = 0; i < 3; i++)
        {
            now = s.NextDue!.Value;
            s = BreakReminderLogic.Step(s, now, cfg, out _);
            kinds.Add(s.BreakIsStretch);
            now = s.BreakEnd!.Value;
            s = BreakReminderLogic.Step(s, now, cfg, out _);
        }
        Expect.Equal("False,False,True", string.Join(",", kinds), "third is the stretch");
        Expect.Equal(3, s.Taken, "all counted");
        Expect.Equal(120, BreakReminderLogic.BreakLengthSeconds(true, cfg), "stretch length");
    }

    public static void LongGapDoesNotBurstOrNotify()
    {
        var cfg = Cfg();
        var now = Monday.AddHours(10);
        var s = Start(now, cfg);
        var wake = now.AddHours(5);
        s = BreakReminderLogic.Step(s, wake, cfg, out var ev);
        Expect.Equal(BreakReminderEvent.Missed, ev, "a reminder 5 hours late is dropped");
        Expect.Equal(wake.AddMinutes(20), s.NextDue, "rescheduled from now, once");
        s = BreakReminderLogic.Step(s, wake.AddSeconds(5), cfg, out ev);
        Expect.Equal(BreakReminderEvent.None, ev, "no second event");
        // A break that was running when the PC slept is not counted.
        var cfg2 = Cfg();
        var started = BreakReminderLogic.BreakNow(Start(now, cfg2), now, cfg2);
        var after = BreakReminderLogic.Step(started, now.AddHours(3), cfg2, out ev);
        Expect.True(ev == BreakReminderEvent.Missed && after.Taken == 0, "stale break not counted");
    }

    public static void SlightlyLateStillFires()
    {
        var cfg = Cfg();
        var now = Monday.AddHours(10);
        var s = Start(now, cfg);
        BreakReminderLogic.Step(s, now.AddMinutes(23), cfg, out var ev);
        Expect.Equal(BreakReminderEvent.BreakStarted, ev, "3 minutes late is fine");
    }

    public static void ActiveHoursAreRespected()
    {
        var cfg = Cfg(activeOnly: true);
        var evening = Monday.AddHours(17).AddMinutes(45);
        var s = Start(evening, cfg);
        Expect.Equal(Monday.AddDays(1).AddHours(9).AddMinutes(20), s.NextDue, "first break after the window opens");
        // Due exactly when the window has closed: skipped.
        var late = new BreakReminderState(Monday.AddHours(18), null, false, null, "2026-10-05", 0);
        BreakReminderLogic.Step(late, Monday.AddHours(18), cfg, out var ev);
        Expect.Equal(BreakReminderEvent.Missed, ev, "no break outside active hours");
    }

    public static void PauseResumeKeepsTheRemainingTime()
    {
        var cfg = Cfg();
        var now = Monday.AddHours(10);
        var s = Start(now, cfg);
        s = BreakReminderLogic.Pause(s, now.AddMinutes(8), cfg);
        Expect.Equal(BreakReminderPhaseOf(s), BreakReminderLogic.Phase.Paused, "paused");
        Expect.Near(12 * 60, s.PausedLeftSeconds!.Value, 1e-6, "12 minutes left");
        s = BreakReminderLogic.Step(s, now.AddHours(2), cfg, out var ev);
        Expect.Equal(BreakReminderEvent.None, ev, "nothing fires while paused");
        var back = now.AddHours(2);
        s = BreakReminderLogic.Resume(s, back, cfg);
        Expect.Equal(back.AddMinutes(12), s.NextDue, "resumes with 12 minutes");
        Expect.True(s.PausedLeftSeconds is null, "no longer paused");
    }

    private static BreakReminderLogic.Phase BreakReminderPhaseOf(BreakReminderState s) => BreakReminderLogic.PhaseOf(s);

    public static void SnoozeSkipAndFinish()
    {
        var cfg = Cfg();
        var now = Monday.AddHours(10);
        var s = Start(now, cfg);
        var snoozed = BreakReminderLogic.Snooze(s, now.AddMinutes(19), 5);
        Expect.Equal(now.AddMinutes(24), snoozed.NextDue, "snooze moves it");
        var inBreak = BreakReminderLogic.BreakNow(s, now.AddMinutes(5), cfg);
        Expect.True(inBreak.BreakEnd is not null && inBreak.NextDue is null, "break now");
        var skipped = BreakReminderLogic.Skip(inBreak, now.AddMinutes(5).AddSeconds(3), cfg);
        Expect.True(skipped.Taken == 0 && skipped.NextDue is not null && skipped.BreakEnd is null, "skip does not count");
        var finished = BreakReminderLogic.Finish(inBreak, now.AddMinutes(5).AddSeconds(8), cfg);
        Expect.True(finished.Taken == 1 && finished.NextDue is not null && finished.BreakEnd is null, "finish counts");
        Expect.Equal(skipped, BreakReminderLogic.Finish(skipped, now, cfg), "finish outside a break does nothing");
    }

    public static void CountResetsAtMidnight()
    {
        var cfg = Cfg();
        var s = new BreakReminderState(Monday.AddHours(23).AddMinutes(50), null, false, null, "2026-10-05", 7);
        s = BreakReminderLogic.Step(s, Monday.AddDays(1).AddMinutes(1), cfg, out _);
        Expect.Equal(0, s.Taken, "new day starts at zero");
        Expect.Equal("2026-10-06", s.Day, "day updated");
    }

    public static void DisplayValues()
    {
        var cfg = Cfg();
        var now = Monday.AddHours(10);
        var s = Start(now, cfg);
        Expect.Near(0.5, BreakReminderLogic.Progress(s, now.AddMinutes(10), cfg), 1e-9, "halfway");
        Expect.Equal(TimeSpan.FromMinutes(12), BreakReminderLogic.Remaining(s, now.AddMinutes(8)), "remaining");
        var br = BreakReminderLogic.BreakNow(s, now, cfg);
        Expect.True(BreakReminderLogic.NeedsSeconds(br) && !BreakReminderLogic.NeedsSeconds(s), "seconds only during a break");
        Expect.Near(0.5, BreakReminderLogic.Progress(br, now.AddSeconds(10), cfg), 1e-9, "break progress");
    }

    public static void StampsRoundTrip()
    {
        var at = new DateTime(2026, 10, 5, 14, 20, 0);
        Expect.Equal(at, HealthLogic.ParseStamp(HealthLogic.FormatStamp(at))!.Value, "round trip");
        Expect.True(HealthLogic.ParseStamp("junk") is null, "junk");
    }
}
