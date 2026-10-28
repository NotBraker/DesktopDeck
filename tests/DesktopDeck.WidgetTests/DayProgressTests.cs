using DesktopDeck.Widgets;

public static class DayProgressTests
{
    public static void HalfDay() => Expect.Near(0.5, DayProgressLogic.Fraction("day", new DateTime(2026, 10, 4, 12, 0, 0)), 1e-9, "noon is half the day");

    public static void WeekStartsOnChosenDay()
    {
        var sunday = new DateTime(2026, 10, 4, 0, 0, 0);
        Expect.Equal(new DateTime(2026, 9, 28), DayProgressLogic.Period("week", sunday).Start, "Monday weeks");
        Expect.Equal(new DateTime(2026, 10, 4), DayProgressLogic.Period("week", sunday, DayOfWeek.Sunday).Start, "Sunday weeks");
    }

    public static void MonthAndYear()
    {
        var now = new DateTime(2026, 2, 15);
        Expect.Equal((new DateTime(2026, 2, 1), new DateTime(2026, 3, 1)), DayProgressLogic.Period("month", now), "February");
        Expect.Near(0, DayProgressLogic.Fraction("year", new DateTime(2026, 1, 1)), 1e-9, "year starts at 0");
    }

    public static void RemainingText()
    {
        Expect.Equal("6 h 0 m left", DayProgressLogic.Remaining("day", new DateTime(2026, 10, 4, 18, 0, 0)), "evening");
        Expect.Equal("3 days left", DayProgressLogic.Remaining("week", new DateTime(2026, 10, 1, 12, 0, 0)), "midweek");
    }
}
