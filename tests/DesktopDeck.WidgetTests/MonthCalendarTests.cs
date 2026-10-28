using System.Globalization;
using DesktopDeck.Widgets;

public static class MonthCalendarTests
{
    public static void GridStartsOnChosenDay()
    {
        // October 2026 starts on a Thursday.
        Expect.Equal(new DateTime(2026, 9, 28), MonthCalendarLogic.GridStart(2026, 10, DayOfWeek.Monday), "Monday grid");
        Expect.Equal(new DateTime(2026, 9, 27), MonthCalendarLogic.GridStart(2026, 10, DayOfWeek.Sunday), "Sunday grid");
        Expect.Equal(new DateTime(2026, 9, 26), MonthCalendarLogic.GridStart(2026, 10, DayOfWeek.Saturday), "Saturday grid");
        Expect.Equal(new DateTime(2026, 6, 1), MonthCalendarLogic.GridStart(2026, 6, DayOfWeek.Monday), "month starting on the first day");
    }

    public static void RowCount()
    {
        Expect.Equal(5, MonthCalendarLogic.Weeks(2026, 10, DayOfWeek.Monday).Count, "October 2026, Monday weeks");
        Expect.Equal(4, MonthCalendarLogic.Weeks(2026, 2, DayOfWeek.Sunday).Count, "February 2026 fits four Sunday rows");
        Expect.Equal(6, MonthCalendarLogic.Weeks(2026, 8, DayOfWeek.Monday).Count, "August 2026 needs six rows");
        Expect.Equal(6, MonthCalendarLogic.Weeks(2026, 2, DayOfWeek.Sunday, sixRows: true).Count, "six rows on request");
        var weeks = MonthCalendarLogic.Weeks(2026, 10, DayOfWeek.Monday);
        Expect.Equal(new DateTime(2026, 11, 1), weeks[^1][6], "last cell");
    }

    public static void WeekNumbers()
    {
        Expect.Equal(53, MonthCalendarLogic.WeekNumber(new DateTime(2026, 12, 28), DayOfWeek.Monday), "ISO week 53 of 2026");
        Expect.Equal(1, MonthCalendarLogic.WeekNumber(new DateTime(2025, 12, 29), DayOfWeek.Monday), "ISO week 1 of 2026 starts in December");
        Expect.Equal(1, MonthCalendarLogic.WeekNumber(new DateTime(2026, 12, 27), DayOfWeek.Sunday), "US week holding Jan 1 is week 1");
        Expect.Equal(41, MonthCalendarLogic.WeekNumber(new DateTime(2026, 10, 4), DayOfWeek.Sunday), "US week of Oct 4 2026");
    }

    public static void FirstDayAndWeekends()
    {
        Expect.Equal(DayOfWeek.Sunday, MonthCalendarLogic.FirstDay("auto", CultureInfo.GetCultureInfo("en-US")), "US starts Sunday");
        Expect.Equal(DayOfWeek.Monday, MonthCalendarLogic.FirstDay("auto", CultureInfo.GetCultureInfo("de-DE")), "Germany starts Monday");
        Expect.Equal(DayOfWeek.Saturday, MonthCalendarLogic.FirstDay("Saturday", CultureInfo.GetCultureInfo("en-US")), "explicit");
        Expect.True(MonthCalendarLogic.IsWeekend(DayOfWeek.Sunday, "sat-sun") && !MonthCalendarLogic.IsWeekend(DayOfWeek.Friday, "sat-sun"), "sat-sun");
        Expect.True(MonthCalendarLogic.IsWeekend(DayOfWeek.Friday, "fri-sat") && !MonthCalendarLogic.IsWeekend(DayOfWeek.Sunday, "fri-sat"), "fri-sat");
    }

    public static void Marks()
    {
        string[] lines =
        [
            "2026-10-12 | Dentist",
            "10-31 | Halloween | #f97316",
            "2026-10-20..2026-10-22 | Trip",
            "# a comment",
            "2026-10-12",
            "not a date | nope",
            "2026-13-01 | bad month",
        ];
        var (marks, invalid) = MonthCalendarLogic.ParseMarks(lines, new DateTime(2026, 9, 28), new DateTime(2026, 11, 1));
        Expect.Equal(2, marks[new DateTime(2026, 10, 12)].Count, "two marks on the 12th");
        Expect.Equal("Dentist", marks[new DateTime(2026, 10, 12)][0].Label, "label");
        Expect.Equal("#f97316", marks[new DateTime(2026, 10, 31)][0].Color, "yearly mark with color");
        Expect.True(marks.ContainsKey(new DateTime(2026, 10, 21)) && !marks.ContainsKey(new DateTime(2026, 10, 23)), "range");
        Expect.Equal(2, invalid.Count, "two unreadable lines");
        var leap = MonthCalendarLogic.ParseMarks(["02-29 | Leap day"], new DateTime(2027, 1, 1), new DateTime(2028, 12, 31)).Marks;
        Expect.True(leap.Count == 1 && leap.ContainsKey(new DateTime(2028, 2, 29)), "Feb 29 only in leap years");
    }

    public static void UpcomingAndUntil()
    {
        var today = new DateTime(2026, 10, 4);
        var next = MonthCalendarLogic.Upcoming(["2026-10-01 | Past", "12-25 | Christmas", "2026-10-06 | Soon"], today, 5);
        Expect.Equal(2, next.Count, "past dates skipped");
        Expect.Equal("Soon", next[0].Mark.Label, "nearest first");
        Expect.Equal("Tomorrow", MonthCalendarLogic.Until(today, today.AddDays(1)), "tomorrow");
        Expect.Equal("in 3 weeks", MonthCalendarLogic.Until(today, today.AddDays(21)), "weeks");
    }
}
