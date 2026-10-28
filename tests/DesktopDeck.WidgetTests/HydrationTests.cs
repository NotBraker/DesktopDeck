using System.Globalization;
using DesktopDeck.Widgets;

public static class HydrationTests
{
    private static readonly CultureInfo C = CultureInfo.InvariantCulture;
    private static readonly DateTime Today = new(2026, 10, 5);
    private static readonly TimeSpan Eight = TimeSpan.FromHours(8), Nine = TimeSpan.FromHours(21);

    public static void UnitConversions()
    {
        Expect.Near(236.588, HydrationLogic.ToMl(1, "cups"), 1e-6, "a cup");
        Expect.Near(29.5735, HydrationLogic.ToMl(1, "oz"), 1e-6, "an ounce");
        Expect.Near(250, HydrationLogic.ToMl(250, "ml"), 1e-9, "ml unchanged");
        Expect.Near(8, HydrationLogic.FromMl(HydrationLogic.ToMl(8, "cups"), "cups"), 1e-9, "round trip");
    }

    public static void OversizedAmountsAreReadAsMl()
    {
        Expect.Near(2000, HydrationLogic.EnteredToMl(2000, "oz"), 1e-9, "2000 oz is really 2000 ml");
        Expect.Near(HydrationLogic.ToMl(64, "oz"), HydrationLogic.EnteredToMl(64, "oz"), 1e-9, "64 oz is 64 oz");
        Expect.Near(HydrationLogic.ToMl(8, "cups"), HydrationLogic.EnteredToMl(8, "cups"), 1e-9, "8 cups is 8 cups");
        Expect.Near(5000, HydrationLogic.EnteredToMl(5000, "ml"), 1e-9, "ml is never rescaled");
    }

    public static void AmountText()
    {
        Expect.Equal("1,250 ml", HydrationLogic.Format(1250, "ml", C), "ml");
        Expect.Equal("42.3 oz", HydrationLogic.Format(1250, "oz", C), "oz");
        Expect.Equal("5.3 cups", HydrationLogic.Format(1250, "cups", C), "cups");
        Expect.Equal("0", HydrationLogic.Format(-5, "ml", C, withUnit: false), "never negative");
    }

    public static void ButtonSizes()
    {
        Expect.Equal("0.5,1,2", string.Join(",", HydrationLogic.ParseMultipliers("0.5, 1, 2").Select(d => d.ToString(C))), "plain");
        Expect.Equal("0.5,0.25", string.Join(",", HydrationLogic.ParseMultipliers("½ ¼").Select(d => d.ToString(C))), "fractions");
        Expect.Equal(2, HydrationLogic.ParseMultipliers("1, junk, -3, 1, 99, 2").Count, "junk, negatives, duplicates and huge values are dropped");
        Expect.Equal(6, HydrationLogic.ParseMultipliers("1,2,3,4,5,6,7,8").Count, "at most six");
        Expect.Equal("+½", HydrationLogic.ButtonLabel(0.5, "cups", C), "half cup");
        Expect.Equal("+250", HydrationLogic.ButtonLabel(250, "ml", C), "ml");
        Expect.Equal("+4", HydrationLogic.ButtonLabel(4, "oz", C), "oz");
        Expect.Equal("+1.5", HydrationLogic.ButtonLabel(1.5, "cups", C), "cups");
    }

    public static void LogAddUndoAndPruning()
    {
        var log = new Dictionary<string, double>();
        HydrationLogic.Add(log, Today, 250);
        HydrationLogic.Add(log, Today, 500);
        HydrationLogic.Add(log, Today.AddDays(-100), 999);
        Expect.Near(750, HydrationLogic.Total(log, Today), 1e-9, "adds up");
        HydrationLogic.Add(log, Today, -500);
        Expect.Near(250, HydrationLogic.Total(log, Today), 1e-9, "undo");
        HydrationLogic.Add(log, Today, -9999);
        Expect.Near(0, HydrationLogic.Total(log, Today), 1e-9, "never below zero");
        HydrationLogic.Add(log, Today, 300);
        string text = HydrationLogic.FormatLog(log, Today);
        Expect.True(!text.Contains("2026-06"), "old days pruned");
        Expect.Near(300, HydrationLogic.Total(HydrationLogic.ParseLog(text), Today), 1e-9, "round trip");
        Expect.Equal(0, HydrationLogic.ParseLog("junk\n2026-99-99|5\n2026-10-05|abc\n2026-10-05|-4").Count, "damaged lines ignored");
    }

    public static void RecentDaysAreOldestFirstEndingToday()
    {
        var log = new Dictionary<string, double>();
        HydrationLogic.Add(log, Today, 100);
        HydrationLogic.Add(log, Today.AddDays(-6), 600);
        var days = HydrationLogic.Recent(log, Today);
        Expect.Equal(7, days.Count, "seven days");
        Expect.Equal(Today.AddDays(-6), days[0].Day, "oldest first");
        Expect.Near(600, days[0].Ml, 1e-9, "oldest total");
        Expect.Equal(Today, days[6].Day, "today last");
        Expect.Equal(1, HydrationLogic.GoalDays(days, 500), "one day met 500");
        Expect.Near(0.5, HydrationLogic.Fraction(1000, 2000), 1e-9, "fraction");
        Expect.Near(1, HydrationLogic.Fraction(5000, 2000), 1e-9, "capped at 1");
        Expect.Near(0, HydrationLogic.Fraction(5, 0), 1e-9, "no goal");
    }

    public static void MidnightStartsAFreshDay()
    {
        var log = new Dictionary<string, double>();
        HydrationLogic.Add(log, Today, 1800);
        Expect.Near(0, HydrationLogic.Total(log, Today.AddDays(1)), 1e-9, "new date, new total");
    }

    public static void ReminderFiresOnceThenReschedules()
    {
        var noon = Today.AddHours(12);
        var interval = TimeSpan.FromMinutes(60);
        var (due, fire) = HydrationLogic.StepReminder(null, noon, interval, true, Eight, Nine, false);
        Expect.True(!fire && due == noon.AddHours(1), "first check only schedules");
        (due, fire) = HydrationLogic.StepReminder(due, noon.AddMinutes(59), interval, true, Eight, Nine, false);
        Expect.True(!fire && due == noon.AddHours(1), "not yet");
        (due, fire) = HydrationLogic.StepReminder(due, noon.AddHours(1), interval, true, Eight, Nine, false);
        Expect.True(fire && due == noon.AddHours(2), "due: fire and schedule the next");
    }

    public static void ReminderSkipsGapsGoalAndNight()
    {
        var interval = TimeSpan.FromMinutes(60);
        var noon = Today.AddHours(12);
        var (due, fire) = HydrationLogic.StepReminder(noon, noon.AddHours(4), interval, true, Eight, Nine, false);
        Expect.True(!fire && due == noon.AddHours(5), "four hours late: no burst, reschedule from now");
        (due, fire) = HydrationLogic.StepReminder(noon, noon, interval, true, Eight, Nine, true);
        Expect.True(!fire, "goal met: no reminder");
        var night = Today.AddHours(22);
        (due, fire) = HydrationLogic.StepReminder(night, night, interval, true, Eight, Nine, false);
        Expect.True(!fire && due == Today.AddDays(1).AddHours(9), "outside active hours waits for morning + interval");
    }

    public static void GoalAnnouncedOncePerDay()
    {
        Expect.True(HydrationLogic.ReachedGoal(1800, 2050, 2000, null, Today), "crossing the goal");
        Expect.True(!HydrationLogic.ReachedGoal(2100, 2350, 2000, null, Today), "already above");
        Expect.True(!HydrationLogic.ReachedGoal(1800, 2050, 2000, "2026-10-05", Today), "already announced today");
        Expect.True(HydrationLogic.ReachedGoal(1800, 2050, 2000, "2026-10-04", Today), "yesterday's announcement doesn't count");
        Expect.True(!HydrationLogic.ReachedGoal(0, 100, 0, null, Today), "no goal");
    }

    public static void HistoryKeepsTheChosenDays()
    {
        var today = new DateTime(2026, 10, 5);
        var log = new Dictionary<string, double> { ["2026-10-04"] = 500, ["2026-09-01"] = 700, ["2025-01-01"] = 900 };
        Expect.Equal(2, HistoryLines(HydrationLogic.FormatLog(log, today)), "the default keeps 35 days");
        Expect.Equal(1, HistoryLines(HydrationLogic.FormatLog(log, today, 10)), "a shorter setting trims more");
        Expect.Equal(3, HistoryLines(HydrationLogic.FormatLog(log, today, 0)), "0 keeps everything");
    }

    private static int HistoryLines(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
}
