using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DesktopDeck.Widgets;

/// <summary>Month grids, week numbers, weekends and marked dates for the month calendar. Pure logic (unit tested).</summary>
public static class MonthCalendarLogic
{
    /// <summary>One marked date: its label and an optional color ("#RRGGBB", or "" for the widget's mark color).</summary>
    public sealed record Mark(string Label, string Color);

    /// <summary>"auto" uses the culture's first day; otherwise a day name such as "Monday".</summary>
    public static DayOfWeek FirstDay(string choice, CultureInfo culture)
        => Enum.TryParse<DayOfWeek>(choice, ignoreCase: true, out var day) && Enum.IsDefined(day) ? day : culture.DateTimeFormat.FirstDayOfWeek;

    /// <summary>The first cell of the grid: the <paramref name="firstDay"/> on or before the 1st of the month.</summary>
    public static DateTime GridStart(int year, int month, DayOfWeek firstDay)
    {
        var first = new DateTime(year, month, 1);
        return first.AddDays(-(((int)first.DayOfWeek - (int)firstDay + 7) % 7));
    }

    /// <summary>The weeks (rows of 7 days) needed to show the month: 4 to 6, or always 6 with <paramref name="sixRows"/>.</summary>
    public static IReadOnlyList<DateTime[]> Weeks(int year, int month, DayOfWeek firstDay, bool sixRows = false)
    {
        var start = GridStart(year, month, firstDay);
        var end = new DateTime(year, month, 1).AddMonths(1);
        int rows = sixRows ? 6 : (int)Math.Ceiling((end - start).TotalDays / 7);
        return Enumerable.Range(0, rows).Select(r => Enumerable.Range(0, 7).Select(d => start.AddDays(r * 7 + d)).ToArray()).ToList();
    }

    /// <summary>
    /// The week number for a row. Monday weeks use ISO 8601 (week 1 holds the first Thursday); other first days use
    /// the "week 1 holds January 1st" rule common in the US and Middle East, judged by the row's last day.
    /// </summary>
    public static int WeekNumber(DateTime rowStart, DayOfWeek firstDay)
        => firstDay == DayOfWeek.Monday
            ? ISOWeek.GetWeekOfYear(rowStart)
            : CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(rowStart.AddDays(6), CalendarWeekRule.FirstDay, firstDay);

    /// <summary>Weekend days: "sat-sun", "fri-sat", "sun", "fri" or "none".</summary>
    public static bool IsWeekend(DayOfWeek day, string weekend) => weekend switch
    {
        "fri-sat" => day is DayOfWeek.Friday or DayOfWeek.Saturday,
        "sun" => day == DayOfWeek.Sunday,
        "fri" => day == DayOfWeek.Friday,
        "none" => false,
        _ => day is DayOfWeek.Saturday or DayOfWeek.Sunday,
    };

    /// <summary>
    /// Reads marked-date lines between <paramref name="from"/> and <paramref name="to"/> (inclusive). Each line is
    /// "yyyy-MM-dd | label | #color", "MM-dd | label" (every year) or "yyyy-MM-dd..yyyy-MM-dd | label" (a range);
    /// the label and color are optional. Lines that can't be read are returned in Invalid.
    /// </summary>
    public static (Dictionary<DateTime, List<Mark>> Marks, List<string> Invalid) ParseMarks(IEnumerable<string> lines, DateTime from, DateTime to)
    {
        var marks = new Dictionary<DateTime, List<Mark>>();
        var invalid = new List<string>();
        from = from.Date;
        to = to.Date;
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            // Blank lines and comments ("# …", "// …") are skipped.
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("//", StringComparison.Ordinal)) continue;
            var parts = line.Split('|').Select(p => p.Trim()).ToArray();
            string label = parts.Length > 1 ? parts[1] : "";
            string color = parts.Length > 2 && IsColor(parts[2]) ? parts[2] : "";
            var mark = new Mark(label, color);
            string when = parts[0];
            bool ok;
            if (when.Contains("..", StringComparison.Ordinal))
            {
                var ends = when.Split("..", 2);
                ok = TryDate(ends[0].Trim(), out var a) & TryDate(ends[1].Trim(), out var b) && b >= a && (b - a).TotalDays <= 366;
                if (ok)
                    for (var d = a < from ? from : a; d <= b && d <= to; d = d.AddDays(1)) Add(d, mark);
            }
            else if (TryDate(when, out var date))
            {
                ok = true;
                if (date >= from && date <= to) Add(date, mark);
            }
            else if (DateTime.TryParseExact(when, "MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) || when == "02-29")
            {
                ok = true;
                int month = int.Parse(when[..2], CultureInfo.InvariantCulture), day = int.Parse(when[3..], CultureInfo.InvariantCulture);
                for (int year = from.Year; year <= to.Year; year++)
                {
                    if (day > DateTime.DaysInMonth(year, month)) continue;
                    var d = new DateTime(year, month, day);
                    if (d >= from && d <= to) Add(d, mark);
                }
            }
            else ok = false;
            if (!ok) invalid.Add(line);
        }
        return (marks, invalid);

        void Add(DateTime day, Mark mark)
        {
            if (!marks.TryGetValue(day, out var list)) marks[day] = list = [];
            list.Add(mark);
        }
    }

    /// <summary>The next <paramref name="count"/> marked days from <paramref name="today"/> on (within a year).</summary>
    public static IReadOnlyList<(DateTime Day, Mark Mark)> Upcoming(IEnumerable<string> lines, DateTime today, int count)
    {
        if (count <= 0) return [];
        var (marks, _) = ParseMarks(lines, today, today.AddDays(366));
        return marks.OrderBy(m => m.Key).SelectMany(m => m.Value.Select(v => (m.Key, v))).Take(count).ToList();
    }

    /// <summary>"Today", "Tomorrow", "in 5 days", "in 3 weeks".</summary>
    public static string Until(DateTime today, DateTime day)
    {
        int days = (int)(day.Date - today.Date).TotalDays;
        return days switch
        {
            0 => "Today",
            1 => "Tomorrow",
            < 0 => days == -1 ? "Yesterday" : $"{-days} days ago",
            < 14 => $"in {days} days",
            < 60 => $"in {days / 7} weeks",
            _ => $"in {Math.Round(days / 30.4)} months",
        };
    }

    private static bool TryDate(string text, out DateTime date) => DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static bool IsColor(string text) => text.Length is 7 or 9 && text[0] == '#' && text.Skip(1).All(Uri.IsHexDigit);
}
