using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
namespace DesktopDeck.Widgets;
public static class AlarmsLogic
{
    public sealed record Alarm(string Id, TimeSpan Time, IReadOnlyList<DayOfWeek> Days, string Label);
    public static (List<Alarm> Alarms, List<string> Invalid) Parse(IEnumerable<string> lines)
    {
        var alarms = new List<Alarm>(); var invalid = new List<string>(); int index = 0;
        foreach (var line in lines)
        {
            var p = line.Split('|').Select(x => x.Trim()).ToArray();
            if (p.Length < 2 || !TimeSpan.TryParseExact(p[0], @"hh\:mm", CultureInfo.InvariantCulture, out var time) || time.TotalHours >= 24) { invalid.Add(line); continue; }
            var days = new List<DayOfWeek>(); bool valid = true;
            if (p[1].Equals("daily", StringComparison.OrdinalIgnoreCase)) days.AddRange(Enum.GetValues<DayOfWeek>());
            else foreach (var d in p[1].Split(',')) { var found = Enum.GetValues<DayOfWeek>().Where(v => v.ToString().StartsWith(d.Trim(), StringComparison.OrdinalIgnoreCase) && d.Trim().Length >= 3).ToArray(); if (found.Length != 1) valid = false; else days.Add(found[0]); }
            if (!valid || days.Count == 0) { invalid.Add(line); continue; }
            // Stable across ordering; duplicate lines represent one alarm.
            string id = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(line))).Substring(0, 16);
            if (!alarms.Any(a => a.Id == id)) alarms.Add(new Alarm(id, time, days, p.Length > 2 && p[2].Length > 0 ? p[2] : "Reminder " + (++index)));
        }
        return (alarms, invalid);
    }
    public static DateTime Next(Alarm alarm, DateTime now)
    {
        for (int i = 0; i < 8; i++) { var date = now.Date.AddDays(i); var at = date + alarm.Time; if (alarm.Days.Contains(date.DayOfWeek) && at >= now) return at; }
        return DateTime.MaxValue;
    }
    public static bool Due(Alarm alarm, DateTime now, DateTime previous, string lastFired)
    {
        var at = now.Date + alarm.Time;
        return alarm.Days.Contains(now.DayOfWeek) && at <= now && at > previous && lastFired != at.ToString("O", CultureInfo.InvariantCulture);
    }
}
