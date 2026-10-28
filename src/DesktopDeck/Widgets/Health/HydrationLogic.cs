using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DesktopDeck.Widgets;

/// <summary>Pure parts of the water tracker: units, the daily log, button sizes, history and reminder timing.</summary>
public static class HydrationLogic
{
    public const double MlPerOz = 29.5735, MlPerCup = 236.588;
    public const int KeepDays = 35;

    // ---------- Units ----------

    public static double ToMl(double amount, string unit) => unit switch { "oz" => amount * MlPerOz, "cups" => amount * MlPerCup, _ => amount };
    public static double FromMl(double ml, string unit) => unit switch { "oz" => ml / MlPerOz, "cups" => ml / MlPerCup, _ => ml };

    /// <summary>
    /// An amount typed in the chosen unit, as millilitres. A number far too big for the unit (2000 "oz") was clearly
    /// meant as millilitres, so it is read that way instead of producing absurd totals.
    /// </summary>
    public static double EnteredToMl(double amount, string unit)
    {
        double limit = unit switch { "oz" => 400, "cups" => 40, _ => double.MaxValue };
        return amount > limit ? amount : ToMl(amount, unit);
    }

    public static string UnitName(string unit) => unit switch { "oz" => "oz", "cups" => "cups", _ => "ml" };

    /// <summary>"1,250 ml", "42.3 oz", "5.3 cups" (decimals only where they matter).</summary>
    public static string Format(double ml, string unit, CultureInfo? culture = null, bool withUnit = true)
    {
        culture ??= CultureInfo.CurrentCulture;
        double v = FromMl(Math.Max(0, ml), unit);
        string number = unit == "ml" ? Math.Round(v).ToString("N0", culture) : v.ToString(v >= 100 ? "N0" : "0.#", culture);
        return withUnit ? number + " " + UnitName(unit) : number;
    }

    // ---------- Buttons ----------

    /// <summary>Button sizes as multiples of the cup: "0.5, 1, 2" (also ½ ¼ ¾). Invalid or out-of-range entries are dropped; at most six.</summary>
    public static List<double> ParseMultipliers(string? text)
    {
        var sizes = new List<double>();
        foreach (string part in (text ?? "").Split([',', ';', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string p = part.Trim().Replace("½", "0.5").Replace("¼", "0.25").Replace("¾", "0.75").Replace("x", "", StringComparison.OrdinalIgnoreCase);
            if (double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v is > 0 and <= 20 && !sizes.Contains(v) && sizes.Count < 6) sizes.Add(v);
        }
        return sizes;
    }

    /// <summary>The label on an add button: "+250", "+½", "+2".</summary>
    public static string ButtonLabel(double amountInUnit, string unit, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (unit == "cups")
        {
            if (Math.Abs(amountInUnit - 0.5) < 0.01) return "+½";
            if (Math.Abs(amountInUnit - 0.25) < 0.01) return "+¼";
            if (Math.Abs(amountInUnit - 0.75) < 0.01) return "+¾";
        }
        return "+" + (amountInUnit >= 10 ? Math.Round(amountInUnit).ToString("0", culture) : Math.Round(amountInUnit, 1).ToString("0.#", culture));
    }

    // ---------- Log ("yyyy-MM-dd|ml" per line) ----------

    public static Dictionary<string, double> ParseLog(string? text)
    {
        var log = new Dictionary<string, double>();
        if (string.IsNullOrEmpty(text)) return log;
        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split('|');
            if (parts.Length == 2 && DateTime.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double ml) && double.IsFinite(ml) && ml > 0)
                log[parts[0]] = log.GetValueOrDefault(parts[0]) + ml;
        }
        return log;
    }

    /// <summary>The log as text, oldest day first, without days older than <paramref name="keepDays"/> before <paramref name="today"/> (0 = keep all).</summary>
    public static string FormatLog(Dictionary<string, double> log, DateTime today, int keepDays = KeepDays)
    {
        string oldest = HealthLogic.DayKey(today.AddDays(-keepDays));
        var sb = new StringBuilder();
        foreach (var (day, ml) in log.OrderBy(e => e.Key, StringComparer.Ordinal))
            if ((keepDays <= 0 || string.CompareOrdinal(day, oldest) >= 0) && ml > 0) sb.Append(day).Append('|').Append(Math.Round(ml, 1).ToString("0.#", CultureInfo.InvariantCulture)).Append('\n');
        return sb.ToString().TrimEnd('\n');
    }

    public static double Total(Dictionary<string, double> log, DateTime day) => log.GetValueOrDefault(HealthLogic.DayKey(day));

    /// <summary>Adds (or, with a negative amount, takes away) water; never below zero.</summary>
    public static void Add(Dictionary<string, double> log, DateTime day, double ml)
    {
        string key = HealthLogic.DayKey(day);
        double next = Math.Max(0, log.GetValueOrDefault(key) + ml);
        if (next <= 0) log.Remove(key); else log[key] = next;
    }

    /// <summary>The last <paramref name="days"/> days ending today, oldest first.</summary>
    public static List<(DateTime Day, double Ml)> Recent(Dictionary<string, double> log, DateTime today, int days = 7)
        => Enumerable.Range(0, days).Select(i => today.Date.AddDays(i - days + 1)).Select(d => (d, Total(log, d))).ToList();

    public static double Fraction(double ml, double goalMl) => goalMl <= 0 ? 0 : Math.Clamp(ml / goalMl, 0, 1);

    public static int GoalDays(IEnumerable<(DateTime Day, double Ml)> days, double goalMl) => goalMl <= 0 ? 0 : days.Count(d => d.Ml >= goalMl);

    // ---------- Reminders ----------

    /// <summary>
    /// What a reminder check should do: <c>Fire</c> when a reminder is due (and not far overdue), and the next due time.
    /// Once the goal is met reminders stop for the day. A reminder that is overdue by more than
    /// <paramref name="grace"/> (PC asleep, deck closed) is dropped and rescheduled instead of fired.
    /// </summary>
    public static (DateTime? Due, bool Fire) StepReminder(DateTime? due, DateTime now, TimeSpan interval, bool activeOnly, TimeSpan from, TimeSpan to, bool goalMet, TimeSpan? grace = null)
    {
        DateTime Next() => HealthLogic.ScheduleAfter(now, interval, activeOnly, from, to, false);
        if (due is not { } d) return (Next(), false);
        if (now < d) return (d, false);
        if (goalMet || now - d > (grace ?? TimeSpan.FromMinutes(10)) || activeOnly && !HealthLogic.IsActive(now, from, to, false)) return (Next(), false);
        return (Next(), true);
    }

    /// <summary>First time today the total reaches the goal (and it wasn't announced already).</summary>
    public static bool ReachedGoal(double before, double after, double goalMl, string? announcedDay, DateTime today)
        => goalMl > 0 && before < goalMl && after >= goalMl && announcedDay != HealthLogic.DayKey(today);

    public static string Reminder(double remainingMl, string unit)
        => remainingMl > 0 ? $"A glass of water? {Format(remainingMl, unit)} to go today." : "A glass of water?";
}
