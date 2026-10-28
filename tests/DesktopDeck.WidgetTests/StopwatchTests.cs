using System.Globalization;
using DesktopDeck.Widgets;

public static class StopwatchTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 4, 9, 0, 0, TimeSpan.Zero);

    public static void StartPauseAccumulates()
    {
        var s = StopwatchLogic.Start(StopwatchLogic.Empty, T0);
        Expect.True(s.Running, "running after start");
        Expect.Equal(5000L, StopwatchLogic.Elapsed(s, T0.AddSeconds(5)), "5 s in");
        s = StopwatchLogic.Pause(s, T0.AddSeconds(5));
        Expect.True(!s.Running, "paused");
        Expect.Equal(5000L, StopwatchLogic.Elapsed(s, T0.AddHours(3)), "paused time doesn't move");
        s = StopwatchLogic.Start(s, T0.AddMinutes(10));
        Expect.Equal(7500L, StopwatchLogic.Elapsed(s, T0.AddMinutes(10).AddMilliseconds(2500)), "resumes from 5 s");
        Expect.Equal(5000L, StopwatchLogic.Elapsed(s, T0), "clock going back never goes below the saved time");
    }

    public static void SurvivesRoundTrip()
    {
        var s = StopwatchLogic.Lap(StopwatchLogic.Start(new StopwatchLogic.State(1200, null, []), T0), T0.AddSeconds(3));
        var options = new Dictionary<string, string>();
        StopwatchLogic.Write(options, s);
        var back = StopwatchLogic.Read(options);
        Expect.Equal(1200L, back.AccumulatedMs, "accumulated");
        Expect.Equal(T0, back.StartedAt!.Value, "start time");
        Expect.Equal(1, back.Laps.Count, "one lap");
        Expect.Equal(4200L, back.Laps[0], "lap split");
        Expect.True(!StopwatchLogic.Read(new Dictionary<string, string> { [StopwatchLogic.LapsKey] = "x,,5" }).Running, "garbage tolerated");
        Expect.Equal(1, StopwatchLogic.Read(new Dictionary<string, string> { [StopwatchLogic.LapsKey] = "x,,5" }).Laps.Count, "bad laps skipped");
    }

    public static void LapsAndBestWorst()
    {
        var s = new StopwatchLogic.State(0, null, [60_000, 110_000, 190_000]);
        var laps = StopwatchLogic.LapTimes(s);
        Expect.Equal((60_000L, 60_000L), laps[0], "lap 1");
        Expect.Equal((50_000L, 110_000L), laps[1], "lap 2");
        Expect.Equal((80_000L, 190_000L), laps[2], "lap 3");
        Expect.Equal((1, 2), StopwatchLogic.BestWorst(laps), "best and worst");
        Expect.Equal((-1, -1), StopwatchLogic.BestWorst(StopwatchLogic.LapTimes(new StopwatchLogic.State(0, null, [1000]))), "one lap: none");
    }

    public static void Formatting()
    {
        string sep = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        Expect.Equal(("00:05", ""), StopwatchLogic.Format(5999, "seconds"), "truncates seconds");
        Expect.Equal(("12:34", sep + "5"), StopwatchLogic.Format(754_599, "tenths"), "tenths");
        Expect.Equal(("01:02", sep + "07"), StopwatchLogic.Format(62_079, "hundredths"), "hundredths");
        Expect.Equal(("1:23:45", ""), StopwatchLogic.Format(5_025_000, "seconds"), "hours appear");
        Expect.Equal(("0:00:09", ""), StopwatchLogic.Format(9_000, "seconds", forceHours: true), "forced hours");
    }
}
