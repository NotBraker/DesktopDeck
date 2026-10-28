using System.Globalization;
using System.Net.Http;
using System.Text;
using DesktopDeck.Models;

namespace DesktopDeck.Services;

/// <summary>
/// Reads iCalendar (.ics) text: the events of a private "secret address" feed from Google, Outlook, Apple or a school.
/// Handles folded lines, UTC / TZID / floating / all-day times, RRULE (DAILY, WEEKLY, MONTHLY, YEARLY with INTERVAL,
/// COUNT, UNTIL, BYDAY, BYMONTHDAY, BYMONTH, BYSETPOS), EXDATE, RDATE and RECURRENCE-ID overrides. Recurring events are
/// only expanded inside the range that is asked for. No WPF and no network in the parsing, so tests cover it.
/// </summary>
public sealed class IcsCalendar
{
    private readonly List<Vevent> events = [];
    /// <summary>The feed's own name (X-WR-CALNAME), if it has one.</summary>
    public string Name { get; private set; } = "";
    public int EventCount => events.Count;

    // ---------- Parsing ----------

    public static IcsCalendar Parse(string text)
    {
        var calendar = new IcsCalendar();
        Vevent? current = null;
        int nested = 0;
        foreach (var line in Unfold(text))
        {
            var prop = Property.Parse(line);
            if (prop is null) continue;
            if (prop.Name == "BEGIN")
            {
                if (prop.Value.Equals("VEVENT", StringComparison.OrdinalIgnoreCase) && current is null) current = new Vevent();
                else if (current is not null) nested++; // VALARM and friends inside an event
                continue;
            }
            if (prop.Name == "END")
            {
                if (current is null) continue;
                if (nested > 0) { nested--; continue; }
                if (current.Start is not null) calendar.events.Add(current);
                current = null;
                continue;
            }
            if (current is null)
            {
                if (prop.Name == "X-WR-CALNAME" && calendar.Name.Length == 0) calendar.Name = Unescape(prop.Value);
                continue;
            }
            if (nested > 0) continue;
            switch (prop.Name)
            {
                case "UID": current.Uid = prop.Value.Trim(); break;
                case "SUMMARY": current.Summary = Unescape(prop.Value); break;
                case "LOCATION": current.Location = Unescape(prop.Value); break;
                case "DESCRIPTION": current.Description = Unescape(prop.Value); break;
                case "STATUS": current.Cancelled = prop.Value.Trim().Equals("CANCELLED", StringComparison.OrdinalIgnoreCase); break;
                case "DTSTART": current.Start = ParseTime(prop); break;
                case "DTEND": current.End = ParseTime(prop); break;
                case "DURATION": current.Duration = ParseDuration(prop.Value); break;
                case "RRULE": current.Rule = Rule.Parse(prop.Value); break;
                case "EXDATE": foreach (var t in ParseTimeList(prop)) current.ExDates.Add(t); break;
                case "RDATE": foreach (var t in ParseTimeList(prop)) current.RDates.Add(t); break;
                case "RECURRENCE-ID": current.RecurrenceId = ParseTime(prop); break;
            }
        }
        return calendar;
    }

    /// <summary>Joins folded lines (a line break followed by a space or tab continues the previous line).</summary>
    public static IEnumerable<string> Unfold(string text)
    {
        var builder = new StringBuilder();
        bool any = false;
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t') && any) { builder.Append(line, 1, line.Length - 1); continue; }
            if (any) yield return builder.ToString();
            builder.Clear();
            builder.Append(line);
            any = true;
        }
        if (any) yield return builder.ToString();
    }

    /// <summary>Undoes iCalendar text escaping (\n, \, \; and \\).</summary>
    public static string Unescape(string value)
    {
        if (value.IndexOf('\\') < 0) return value;
        var builder = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                char next = value[++i];
                builder.Append(next is 'n' or 'N' ? '\n' : next);
            }
            else builder.Append(value[i]);
        }
        return builder.ToString();
    }

    private sealed class Property
    {
        public string Name = "";
        public Dictionary<string, string> Params = new(StringComparer.OrdinalIgnoreCase);
        public string Value = "";

        public static Property? Parse(string line)
        {
            if (line.Length == 0) return null;
            bool quoted = false;
            int colon = -1;
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '"') quoted = !quoted;
                else if (line[i] == ':' && !quoted) { colon = i; break; }
            }
            if (colon <= 0) return null;
            var head = SplitOutsideQuotes(line[..colon], ';');
            var prop = new Property { Name = head[0].Trim().ToUpperInvariant(), Value = line[(colon + 1)..] };
            for (int i = 1; i < head.Count; i++)
            {
                int eq = head[i].IndexOf('=');
                if (eq > 0) prop.Params[head[i][..eq].Trim()] = head[i][(eq + 1)..].Trim().Trim('"');
            }
            return prop;
        }
    }

    private static List<string> SplitOutsideQuotes(string text, char separator)
    {
        var parts = new List<string>();
        var builder = new StringBuilder();
        bool quoted = false;
        foreach (char c in text)
        {
            if (c == '"') quoted = !quoted;
            if (c == separator && !quoted) { parts.Add(builder.ToString()); builder.Clear(); }
            else builder.Append(c);
        }
        parts.Add(builder.ToString());
        return parts;
    }

    // ---------- Times ----------

    /// <summary>A date or date-time as written in the feed: all-day, UTC, in a named zone, or floating (the viewer's own clock).</summary>
    internal readonly record struct IcsTime(DateTime Wall, TimeZoneInfo? Zone, bool Utc, bool DateOnly)
    {
        /// <summary>The same moment in the viewer's local time.</summary>
        public DateTime ToLocal()
        {
            if (DateOnly) return Wall.Date;
            if (Utc) return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(Wall, DateTimeKind.Utc), TimeZoneInfo.Local);
            if (Zone is null) return Wall;
            return TimeZoneInfo.ConvertTimeFromUtc(ToUtc(Wall, Zone), TimeZoneInfo.Local);
        }

        /// <summary>The moment itself, for comparing times written in different zones (all-day dates count as local midnight).</summary>
        public DateTime ToInstant()
        {
            if (Utc) return Wall;
            return ToUtc(Wall, DateOnly || Zone is null ? TimeZoneInfo.Local : Zone);
        }

        /// <summary>A key that is equal for the same moment (or the same all-day date) however it was written.</summary>
        public string Key => DateOnly ? "d" + Wall.ToString("yyyyMMdd", CultureInfo.InvariantCulture) : "t" + ToInstant().Ticks.ToString(CultureInfo.InvariantCulture);

        public IcsTime At(DateTime wall) => this with { Wall = wall };
    }

    /// <summary>Wall-clock time in a zone to UTC. Times inside a spring-forward gap don't exist, so they move forward an hour.</summary>
    internal static DateTime ToUtc(DateTime wall, TimeZoneInfo zone)
    {
        wall = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(wall)) wall = wall.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(wall, zone);
    }

    private static readonly Dictionary<string, TimeZoneInfo?> ZoneCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object ZoneGate = new();

    /// <summary>Finds a zone by Windows name, IANA name ("America/Chicago") or a common feed spelling; null if unknown.</summary>
    internal static TimeZoneInfo? ResolveZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        id = id.Trim().Trim('"');
        lock (ZoneGate)
        {
            if (ZoneCache.TryGetValue(id, out var cached)) return cached;
            var zone = FindZone(id);
            ZoneCache[id] = zone;
            return zone;
        }
    }

    private static TimeZoneInfo? FindZone(string id)
    {
        if (id is "UTC" or "GMT" or "Z" or "Etc/UTC" or "tzone://Microsoft/Utc") return TimeZoneInfo.Utc;
        if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone)) return zone;
        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windows) && windows is not null && TimeZoneInfo.TryFindSystemTimeZoneById(windows, out zone)) return zone;
        // "/mozilla.org/20050126_1/America/New_York": use the trailing Area/City.
        var parts = id.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            string tail = parts[^2] + "/" + parts[^1];
            if (TimeZoneInfo.TryFindSystemTimeZoneById(tail, out zone)) return zone;
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(tail, out windows) && windows is not null && TimeZoneInfo.TryFindSystemTimeZoneById(windows, out zone)) return zone;
        }
        return null;
    }

    private static IcsTime? ParseTime(Property prop)
    {
        string value = prop.Value.Trim();
        bool dateOnly = (prop.Params.TryGetValue("VALUE", out var kind) && kind.Equals("DATE", StringComparison.OrdinalIgnoreCase)) || (value.Length == 8 && !value.Contains('T'));
        if (dateOnly)
            return DateTime.TryParseExact(value[..Math.Min(8, value.Length)], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? new IcsTime(day, null, false, true) : null;
        bool utc = value.EndsWith('Z') || value.EndsWith('z');
        if (utc) value = value[..^1];
        if (!DateTime.TryParseExact(value, ["yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var wall)) return null;
        if (utc) return new IcsTime(wall, TimeZoneInfo.Utc, true, false);
        TimeZoneInfo? zone = prop.Params.TryGetValue("TZID", out var tzid) ? ResolveZone(tzid) : null;
        return new IcsTime(wall, zone, false, false);
    }

    private static IEnumerable<IcsTime> ParseTimeList(Property prop)
    {
        foreach (var piece in prop.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // PERIOD values (start/end) are not supported; use the start.
            string single = piece.Split('/')[0];
            var one = new Property { Name = prop.Name, Params = prop.Params, Value = single };
            if (ParseTime(one) is { } t) yield return t;
        }
    }

    /// <summary>"P1DT2H30M", "PT45M", "P2W".</summary>
    internal static TimeSpan? ParseDuration(string value)
    {
        value = value.Trim();
        if (value.Length < 2) return null;
        bool negative = value[0] == '-';
        int i = value[0] is '-' or '+' ? 1 : 0;
        if (i >= value.Length || value[i] != 'P') return null;
        i++;
        double days = 0, seconds = 0;
        bool time = false;
        var number = new StringBuilder();
        for (; i < value.Length; i++)
        {
            char c = value[i];
            if (c == 'T') { time = true; continue; }
            if (char.IsDigit(c)) { number.Append(c); continue; }
            if (number.Length == 0 || !double.TryParse(number.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double n)) return null;
            number.Clear();
            switch (c)
            {
                case 'W': days += 7 * n; break;
                case 'D': days += n; break;
                case 'H' when time: seconds += n * 3600; break;
                case 'M' when time: seconds += n * 60; break;
                case 'S' when time: seconds += n; break;
                default: return null;
            }
        }
        var span = TimeSpan.FromDays(days) + TimeSpan.FromSeconds(seconds);
        return negative ? -span : span;
    }

    // ---------- Events and expansion ----------

    private sealed class Vevent
    {
        public string Uid = "", Summary = "", Location = "", Description = "";
        public bool Cancelled;
        public IcsTime? Start, End, RecurrenceId;
        public TimeSpan? Duration;
        public Rule? Rule;
        public List<IcsTime> ExDates = [], RDates = [];
    }

    /// <summary>
    /// The events that touch [from, to) (local time), with repeating events expanded and overridden occurrences replaced.
    /// </summary>
    public List<CalendarEvent> Expand(DateTime from, DateTime to, string calendarId, string label, string color)
    {
        var result = new List<CalendarEvent>();
        var overridden = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var e in events.Where(e => e.RecurrenceId is not null))
        {
            if (!overridden.TryGetValue(e.Uid, out var set)) overridden[e.Uid] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(e.RecurrenceId!.Value.Key);
        }
        foreach (var e in events)
        {
            if (e.Cancelled && e.RecurrenceId is null) continue;
            if (e.RecurrenceId is not null)
            {
                // An override stands alone (a cancelled one just removes the occurrence it replaces).
                if (!e.Cancelled) AddOccurrence(e, e.Start!.Value, from, to, calendarId, label, color, result);
                continue;
            }
            overridden.TryGetValue(e.Uid, out var skip);
            foreach (var start in Occurrences(e, to))
            {
                if (skip is not null && skip.Contains(start.Key)) continue;
                AddOccurrence(e, start, from, to, calendarId, label, color, result);
            }
        }
        result.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : string.CompareOrdinal(a.Title, b.Title));
        return result;
    }

    private static void AddOccurrence(Vevent e, IcsTime start, DateTime from, DateTime to, string calendarId, string label, string color, List<CalendarEvent> result)
    {
        var master = e.Start!.Value;
        DateTime localStart = start.ToLocal(), localEnd;
        if (master.DateOnly)
        {
            int days = e.End is { } end && end.DateOnly ? Math.Max(1, (int)(end.Wall.Date - master.Wall.Date).TotalDays) : e.Duration is { } d ? Math.Max(1, (int)Math.Ceiling(d.TotalDays)) : 1;
            localEnd = localStart.AddDays(days);
        }
        else
        {
            TimeSpan length = e.End is { } end ? (end.DateOnly ? end.Wall.Date - master.Wall.Date : end.ToInstant() - master.ToInstant()) : e.Duration ?? TimeSpan.Zero;
            if (length < TimeSpan.Zero) length = TimeSpan.Zero;
            localEnd = localStart + length;
        }
        var occurrence = new CalendarEvent
        {
            Source = CalendarSource.Ics, CalendarId = calendarId, CalendarLabel = label, Color = color,
            Title = e.Summary.Trim().Length == 0 ? "(No title)" : e.Summary.Trim(), Start = localStart, End = localEnd, AllDay = master.DateOnly,
            Location = e.Location.Trim(), Description = e.Description.Trim(), Editable = false,
            GoogleId = "", LocalBlockId = e.Uid,
        };
        if (occurrence.Overlaps(from, to)) result.Add(occurrence);
    }

    /// <summary>Every start of an event up to about <paramref name="to"/>: its own, plus its repeats.</summary>
    private static IEnumerable<IcsTime> Occurrences(Vevent e, DateTime to)
    {
        var start = e.Start!.Value;
        var exceptions = new HashSet<string>(e.ExDates.Select(x => x.Key), StringComparer.Ordinal);
        // The moments are generated on the event's own wall clock, which can differ from local time by up to a day.
        var limit = to.AddDays(2);
        if (e.Rule is null || !e.Rule.Supported)
        {
            if (!exceptions.Contains(start.Key)) yield return start;
        }
        else
        {
            int count = 0;
            foreach (var wall in e.Rule.Generate(start.Wall, limit))
            {
                var occurrence = start.At(wall);
                if (e.Rule.Until is { } until && IsAfter(occurrence, until)) break;
                count++;
                if (e.Rule.Count is { } max && count > max) break;
                // EXDATE removes an occurrence but it still used up a slot of COUNT.
                if (exceptions.Contains(occurrence.Key)) continue;
                yield return occurrence;
            }
        }
        foreach (var extra in e.RDates)
            if (!exceptions.Contains(extra.Key) && extra.Key != start.Key) yield return extra;
    }

    private static bool IsAfter(IcsTime occurrence, IcsTime until)
    {
        if (until.DateOnly) return occurrence.Wall.Date > until.Wall.Date;
        if (occurrence.DateOnly) return occurrence.Wall.Date > until.Wall.Date;
        if (until.Utc || until.Zone is not null) return occurrence.ToInstant() > until.ToInstant();
        return occurrence.Wall > until.Wall;
    }

    // ---------- Repeat rules ----------

    private sealed class Rule
    {
        public string Freq = "";
        public int Interval = 1;
        public int? Count;
        public IcsTime? Until;
        public List<(int Ordinal, DayOfWeek Day)> ByDay = [];
        public List<int> ByMonthDay = [], ByMonth = [], BySetPos = [];
        public DayOfWeek WeekStart = DayOfWeek.Monday;
        public bool Supported => Freq is "DAILY" or "WEEKLY" or "MONTHLY" or "YEARLY";

        public static Rule? Parse(string text)
        {
            var rule = new Rule();
            foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string key = part[..eq].Trim().ToUpperInvariant(), value = part[(eq + 1)..].Trim();
                switch (key)
                {
                    case "FREQ": rule.Freq = value.ToUpperInvariant(); break;
                    case "INTERVAL": if (int.TryParse(value, out int interval) && interval >= 1) rule.Interval = interval; break;
                    case "COUNT": if (int.TryParse(value, out int count) && count >= 1) rule.Count = count; break;
                    case "UNTIL": rule.Until = ParseTime(new Property { Name = "UNTIL", Value = value }); break;
                    case "WKST": if (DayOf(value) is { } wk) rule.WeekStart = wk; break;
                    case "BYDAY":
                        foreach (var d in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            if (d.Length < 2 || DayOf(d[^2..]) is not { } day) continue;
                            string ordinal = d[..^2];
                            rule.ByDay.Add((ordinal.Length == 0 ? 0 : int.TryParse(ordinal, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int n) ? n : 0, day));
                        }
                        break;
                    case "BYMONTHDAY": rule.ByMonthDay = Ints(value); break;
                    case "BYMONTH": rule.ByMonth = Ints(value); break;
                    case "BYSETPOS": rule.BySetPos = Ints(value); break;
                }
            }
            return rule.Freq.Length == 0 ? null : rule;
        }

        private static List<int> Ints(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => int.TryParse(v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int n) ? n : 0).Where(n => n != 0).ToList();

        private static DayOfWeek? DayOf(string code) => code.ToUpperInvariant() switch
        {
            "MO" => DayOfWeek.Monday, "TU" => DayOfWeek.Tuesday, "WE" => DayOfWeek.Wednesday, "TH" => DayOfWeek.Thursday,
            "FR" => DayOfWeek.Friday, "SA" => DayOfWeek.Saturday, "SU" => DayOfWeek.Sunday, _ => null,
        };

        /// <summary>The wall-clock starts of the series in order, beginning at <paramref name="start"/>, until past <paramref name="limit"/> (or a safety cap).</summary>
        public IEnumerable<DateTime> Generate(DateTime start, DateTime limit)
        {
            var time = start.TimeOfDay;
            var first = DateOnly.FromDateTime(start);
            var last = DateOnly.FromDateTime(limit);
            int guard = 0;
            for (long period = 0; guard < 200_000; period++)
            {
                var days = PeriodDays(first, period * Interval);
                if (days.start > last) yield break;
                foreach (var day in days.candidates)
                {
                    guard++;
                    if (day < first) continue;
                    if (day > last) yield break;
                    yield return day.ToDateTime(TimeOnly.MinValue) + time;
                }
                guard++;
            }
        }

        private (DateOnly start, List<DateOnly> candidates) PeriodDays(DateOnly first, long step)
        {
            var candidates = new List<DateOnly>();
            DateOnly periodStart;
            switch (Freq)
            {
                case "DAILY":
                {
                    periodStart = first.AddDays((int)step);
                    if (MatchesFilters(periodStart)) candidates.Add(periodStart);
                    break;
                }
                case "WEEKLY":
                {
                    int offset = ((int)first.DayOfWeek - (int)WeekStart + 7) % 7;
                    periodStart = first.AddDays(-offset + (int)(step * 7));
                    var wanted = ByDay.Count > 0 ? ByDay.Select(d => d.Day).Distinct().ToList() : [first.DayOfWeek];
                    for (int i = 0; i < 7; i++)
                    {
                        var day = periodStart.AddDays(i);
                        if (wanted.Contains(day.DayOfWeek) && (ByMonth.Count == 0 || ByMonth.Contains(day.Month))) candidates.Add(day);
                    }
                    break;
                }
                case "MONTHLY":
                {
                    var month = new DateOnly(first.Year, first.Month, 1).AddMonths((int)step);
                    periodStart = month;
                    if (ByMonth.Count == 0 || ByMonth.Contains(month.Month)) candidates.AddRange(DaysIn(month, first.Day, month: true));
                    break;
                }
                default: // YEARLY
                {
                    int year = first.Year + (int)step;
                    periodStart = new DateOnly(Math.Min(year, 9999), 1, 1);
                    if (year > 9999) break;
                    var months = ByMonth.Count > 0 ? ByMonth : (ByDay.Count > 0 && ByMonthDay.Count == 0 ? [] : [first.Month]);
                    if (months.Count == 0) candidates.AddRange(DaysIn(new DateOnly(year, 1, 1), first.Day, month: false));
                    else foreach (int m in months.OrderBy(m => m)) if (m is >= 1 and <= 12) candidates.AddRange(DaysIn(new DateOnly(year, m, 1), first.Day, month: true));
                    break;
                }
            }
            candidates.Sort();
            if (BySetPos.Count > 0 && candidates.Count > 0)
            {
                var picked = new List<DateOnly>();
                foreach (int pos in BySetPos) { int index = pos > 0 ? pos - 1 : candidates.Count + pos; if (index >= 0 && index < candidates.Count) picked.Add(candidates[index]); }
                candidates = picked.Distinct().OrderBy(d => d).ToList();
            }
            return (periodStart, candidates);
        }

        private bool MatchesFilters(DateOnly day)
        {
            if (ByMonth.Count > 0 && !ByMonth.Contains(day.Month)) return false;
            if (ByDay.Count > 0 && !ByDay.Any(d => d.Day == day.DayOfWeek)) return false;
            if (ByMonthDay.Count > 0 && !ByMonthDay.Any(n => Resolve(n, DateTime.DaysInMonth(day.Year, day.Month)) == day.Day)) return false;
            return true;
        }

        private static int Resolve(int n, int daysInMonth) => n > 0 ? n : daysInMonth + n + 1;

        /// <summary>The matching days of a month (or of the whole year when <paramref name="month"/> is false, for BYDAY with an ordinal and no BYMONTH).</summary>
        private IEnumerable<DateOnly> DaysIn(DateOnly anchor, int defaultDay, bool month)
        {
            var found = new List<DateOnly>();
            int year = anchor.Year;
            int firstMonth = month ? anchor.Month : 1, lastMonth = month ? anchor.Month : 12;
            if (!month && ByDay.Any(d => d.Ordinal != 0))
            {
                // Ordinals count within the year: the 20th Monday of the year.
                foreach (var (ordinal, weekday) in ByDay)
                {
                    var all = new List<DateOnly>();
                    for (var d = new DateOnly(year, 1, 1); d.Year == year; d = d.AddDays(1)) if (d.DayOfWeek == weekday) all.Add(d);
                    if (ordinal == 0) found.AddRange(all);
                    else { int index = ordinal > 0 ? ordinal - 1 : all.Count + ordinal; if (index >= 0 && index < all.Count) found.Add(all[index]); }
                }
                return found;
            }
            for (int m = firstMonth; m <= lastMonth; m++)
            {
                int dim = DateTime.DaysInMonth(year, m);
                IEnumerable<int> dayNumbers;
                if (ByDay.Count > 0)
                {
                    var set = new HashSet<int>();
                    foreach (var (ordinal, weekday) in ByDay)
                    {
                        var matches = Enumerable.Range(1, dim).Where(d => new DateOnly(year, m, d).DayOfWeek == weekday).ToList();
                        if (ordinal == 0) foreach (int d in matches) set.Add(d);
                        else { int index = ordinal > 0 ? ordinal - 1 : matches.Count + ordinal; if (index >= 0 && index < matches.Count) set.Add(matches[index]); }
                    }
                    if (ByMonthDay.Count > 0) set.IntersectWith(ByMonthDay.Select(n => Resolve(n, dim)));
                    dayNumbers = set;
                }
                else if (ByMonthDay.Count > 0) dayNumbers = ByMonthDay.Select(n => Resolve(n, dim)).Where(d => d >= 1 && d <= dim).Distinct();
                else dayNumbers = defaultDay <= dim ? [defaultDay] : [];
                foreach (int d in dayNumbers) found.Add(new DateOnly(year, m, d));
            }
            return found;
        }
    }

    // ---------- Download ----------

    /// <summary>Makes a pasted calendar address usable: "webcal://" becomes "https://". Null if it isn't a web address.</summary>
    public static string? NormalizeAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        string url = address.Trim();
        if (url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)) url = "https://" + url[9..];
        else if (url.StartsWith("webcals://", StringComparison.OrdinalIgnoreCase)) url = "https://" + url[10..];
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.Host.Length > 0 ? uri.AbsoluteUri : null;
    }

    private const long MaxBytes = 25L * 1024 * 1024;

    /// <summary>
    /// Downloads a calendar feed. Errors never mention the address, because for private feeds the address is the password.
    /// </summary>
    public static async Task<string> FetchAsync(HttpClient http, string address, string label, CancellationToken cancel = default)
    {
        string url = NormalizeAddress(address) ?? throw new CalendarException($"The address for \"{label}\" isn't a web address (it should start with https:// or webcal://).");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Accept", "text/calendar, text/plain, */*");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new CalendarException(response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Gone
                    ? $"\"{label}\" can't be read any more (HTTP {(int)response.StatusCode}). Its private address may have been reset."
                    : $"\"{label}\" returned HTTP {(int)response.StatusCode}.");
            if (response.Content.Headers.ContentLength > MaxBytes) throw new CalendarException($"\"{label}\" is too large to load.");
            string text = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
            if (text.Length > MaxBytes) throw new CalendarException($"\"{label}\" is too large to load.");
            if (!text.Contains("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase)) throw new CalendarException($"\"{label}\" isn't a calendar feed (no iCalendar data was found at that address).");
            return text;
        }
        catch (HttpRequestException) { throw new CalendarException($"Could not reach \"{label}\"."); }
        catch (TaskCanceledException) when (!cancel.IsCancellationRequested) { throw new CalendarException($"\"{label}\" took too long to answer."); }
    }
}
