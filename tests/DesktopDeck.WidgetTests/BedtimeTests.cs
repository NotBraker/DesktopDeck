using DesktopDeck.Widgets;

public static class BedtimeTests
{
    private static readonly TimeSpan Bed = new(23, 0, 0), Wake = new(7, 0, 0);
    // Monday 5 October 2026.
    private static readonly DateTime Monday = new(2026, 10, 5);

    private static BedtimeNight At(DateTime now, int windDown = 30, bool weekend = false)
        => BedtimeLogic.Current(now, Bed, Wake, weekend, TimeSpan.Zero, new TimeSpan(9, 0, 0), windDown);

    public static void SleepDurationCrossesMidnight()
    {
        Expect.Equal(TimeSpan.FromHours(8), BedtimeLogic.SleepDuration(Bed, Wake), "23:00 to 07:00");
        Expect.Equal(TimeSpan.FromMinutes(390), BedtimeLogic.SleepDuration(new TimeSpan(0, 30, 0), Wake), "00:30 to 07:00 is 6.5 h");
        Expect.Equal(TimeSpan.FromHours(24), BedtimeLogic.SleepDuration(Wake, Wake), "equal times mean a full day");
    }

    public static void DaytimeCountsDownToTonight()
    {
        var n = At(Monday.AddHours(14));
        Expect.Equal(BedtimePhase.Day, n.Phase, "afternoon");
        Expect.Equal(Monday.AddHours(23), n.Bed, "bed tonight");
        Expect.Equal(Monday.AddDays(1).AddHours(7), n.Wake, "wake tomorrow");
    }

    public static void WindDownThenAsleepThenNextDay()
    {
        Expect.Equal(BedtimePhase.Day, At(Monday.AddHours(22).AddMinutes(29)).Phase, "before wind-down");
        Expect.Equal(BedtimePhase.WindDown, At(Monday.AddHours(22).AddMinutes(30)).Phase, "wind-down starts");
        Expect.Equal(BedtimePhase.Asleep, At(Monday.AddHours(23)).Phase, "bedtime");
        var small = At(Monday.AddDays(1).AddHours(2));
        Expect.Equal(BedtimePhase.Asleep, small.Phase, "after midnight still the same night");
        Expect.Equal(Monday.AddHours(23), small.Bed, "same bedtime");
        Expect.Equal(BedtimePhase.Day, At(Monday.AddDays(1).AddHours(7)).Phase, "at wake-up the next night begins");
    }

    public static void BedtimeAfterMidnight()
    {
        var n = BedtimeLogic.Current(Monday.AddHours(23), new TimeSpan(0, 30, 0), Wake, false, TimeSpan.Zero, Wake, 30);
        Expect.Equal(Monday.AddDays(1).AddMinutes(30), n.Bed, "bedtime 00:30 is after midnight");
        Expect.Equal(BedtimePhase.Day, n.Phase, "23:00 is before the wind-down");
        var wind = BedtimeLogic.Current(Monday.AddDays(1).AddMinutes(10), new TimeSpan(0, 30, 0), Wake, false, TimeSpan.Zero, Wake, 30);
        Expect.Equal(BedtimePhase.WindDown, wind.Phase, "00:10 is inside the wind-down");
        var asleep = BedtimeLogic.Current(Monday.AddDays(1).AddHours(1), new TimeSpan(0, 30, 0), Wake, false, TimeSpan.Zero, Wake, 30);
        Expect.Equal(BedtimePhase.Asleep, asleep.Phase, "01:00 is asleep");
    }

    public static void WeekendTimesApplyToWeekendMornings()
    {
        var friday = new DateTime(2026, 10, 9);
        var n = At(friday.AddHours(15), weekend: true);
        Expect.Equal(friday.AddDays(1), n.Bed, "night ending Saturday uses the weekend bedtime 00:00");
        Expect.Equal(friday.AddDays(1).AddHours(9), n.Wake, "Saturday wake-up 09:00");
        var weekday = At(friday.AddHours(15), weekend: false);
        Expect.Equal(friday.AddHours(23), weekday.Bed, "without the option Friday night is a normal night");
    }

    public static void NoticesFireOncePerNight()
    {
        var wind = At(Monday.AddHours(22).AddMinutes(40));
        var (w, b, wm, bm) = BedtimeLogic.Due(wind, Monday.AddHours(22).AddMinutes(40), null, null);
        Expect.True(w && !b, "wind-down fires");
        (w, b, wm, bm) = BedtimeLogic.Due(wind, Monday.AddHours(22).AddMinutes(41), wm, bm);
        Expect.True(!w && !b, "not again");
        var asleep = At(Monday.AddHours(23).AddMinutes(1));
        (w, b, wm, bm) = BedtimeLogic.Due(asleep, Monday.AddHours(23).AddMinutes(1), wm, bm);
        Expect.True(!w && b, "bedtime fires once");
        (w, b, _, _) = BedtimeLogic.Due(asleep, Monday.AddHours(23).AddMinutes(2), wm, bm);
        Expect.True(!w && !b, "and not again");
    }

    public static void LateStartIsNotAnnounced()
    {
        var now = Monday.AddHours(23).AddMinutes(50);
        var (w, b, wm, bm) = BedtimeLogic.Due(At(now), now, null, null);
        Expect.True(!w && !b, "deck opened 50 minutes after bedtime: no stale notices");
        Expect.True(wm.Length > 0 && bm.Length > 0, "but both are marked as passed");
    }

    public static void NewNightNewNotice()
    {
        var tomorrow = Monday.AddDays(1).AddHours(22).AddMinutes(45);
        var (w, _, _, _) = BedtimeLogic.Due(At(tomorrow), tomorrow, At(Monday.AddHours(22).AddMinutes(45)).Key, null);
        Expect.True(w, "yesterday's marker doesn't silence today");
    }

    public static void SleepCycles()
    {
        Expect.Near(5.0, BedtimeLogic.Cycles(TimeSpan.FromMinutes(465), 15), 1e-9, "7 h 45 m with 15 to fall asleep is 5 cycles");
        Expect.Equal("5.3", BedtimeLogic.CycleText(5.26, System.Globalization.CultureInfo.InvariantCulture), "rounded to a tenth");
        var beds = BedtimeLogic.BedtimesFor(Wake, 15);
        Expect.Equal(3, beds.Count, "6, 5 and 4 cycles");
        Expect.Equal(new TimeSpan(21, 45, 0), beds[0].Bed, "6 cycles: 07:00 - 9 h - 15 m = 21:45");
        Expect.Equal(new TimeSpan(23, 15, 0), beds[1].Bed, "5 cycles");
        Expect.Equal(new TimeSpan(0, 45, 0), beds[2].Bed, "4 cycles wraps past midnight");
        var wakes = BedtimeLogic.WakeTimesFrom(Monday.AddHours(23), 15);
        Expect.Equal(Monday.AddDays(1).AddHours(5).AddMinutes(15), wakes[0].At, "4 cycles from 23:00");
        Expect.Equal(6, wakes[2].Cycles, "six cycles last");
    }

    public static void RatingsLogToggleAndStrip()
    {
        var ratings = new Dictionary<string, int>();
        Expect.Equal(4, BedtimeLogic.Toggle(ratings, Monday, 4), "set");
        Expect.Equal(5, BedtimeLogic.Toggle(ratings, Monday, 5), "change");
        Expect.Equal(0, BedtimeLogic.Toggle(ratings, Monday, 5), "same again clears");
        Expect.Equal(0, BedtimeLogic.Toggle(ratings, Monday, 9), "out of range clears");
        BedtimeLogic.Toggle(ratings, Monday, 3);
        BedtimeLogic.Toggle(ratings, Monday.AddDays(-2), 5);
        BedtimeLogic.Toggle(ratings, Monday.AddDays(-200), 1);
        var back = BedtimeLogic.ParseRatings(BedtimeLogic.FormatRatings(ratings, Monday));
        Expect.Equal(2, back.Count, "old entries pruned");
        var week = BedtimeLogic.Recent(back, Monday);
        Expect.Equal(7, week.Count, "seven days");
        Expect.Equal(Monday, week[6].Day, "today last");
        Expect.Equal(5, week[4].Rating, "two days ago");
        Expect.Near(4, BedtimeLogic.Average(week), 1e-9, "average of logged nights");
        Expect.Equal(0, BedtimeLogic.ParseRatings("junk\n2026-10-05|9\n2026-10-05|x").Count, "damaged lines ignored");
    }

    public static void AskForARatingAfterWakeUp()
    {
        var ratings = new Dictionary<string, int>();
        var wake = Monday.AddHours(7);
        Expect.True(!BedtimeLogic.ShouldAskRating(Monday.AddHours(6), wake, ratings), "still asleep");
        Expect.True(BedtimeLogic.ShouldAskRating(Monday.AddHours(8), wake, ratings), "awake and not logged");
        ratings["2026-10-05"] = 4;
        Expect.True(!BedtimeLogic.ShouldAskRating(Monday.AddHours(8), wake, ratings), "already logged");
    }

    public static void HistoryKeepsTheChosenDays()
    {
        var today = new DateTime(2026, 10, 5);
        var ratings = new Dictionary<string, int> { ["2026-10-04"] = 4, ["2026-09-01"] = 3, ["2025-01-01"] = 5 };
        Expect.Equal(2, BedtimeHistoryLines(BedtimeLogic.FormatRatings(ratings, today)), "the default keeps 35 days");
        Expect.Equal(1, BedtimeHistoryLines(BedtimeLogic.FormatRatings(ratings, today, 10)), "a shorter setting trims more");
        Expect.Equal(3, BedtimeHistoryLines(BedtimeLogic.FormatRatings(ratings, today, 0)), "0 keeps everything");
    }

    private static int BedtimeHistoryLines(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
}
