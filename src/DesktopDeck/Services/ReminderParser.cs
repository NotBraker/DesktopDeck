using System.Globalization;
using System.Text.RegularExpressions;
using DesktopDeck.Models;

namespace DesktopDeck.Services;

/// <summary>What the parser needs to know about the user's settings.</summary>
public sealed record ReminderParseOptions
{
    /// <summary>The time used when only a day is given.</summary>
    public TimeSpan DefaultTime { get; init; } = new(9, 0, 0);
    public bool Use24Hour { get; init; }
    /// <summary>"10/12" means 10 December (day first) instead of 12 October.</summary>
    public bool DayFirst { get; init; }
    /// <summary>False when only the time part is wanted (editing "when").</summary>
    public bool RequireText { get; init; } = true;
}

/// <summary>A reminder read from a sentence. <see cref="When"/> says how the time was understood ("Today at 5:00 PM").</summary>
public sealed record ParsedReminder(string Text, string Note, DateTimeOffset Due, ReminderRepeat Repeat, string When)
{
    public Reminder ToReminder(DateTimeOffset now) => new() { Text = Text, Note = Note, Due = Due, Repeat = Repeat.Copy(), Created = now };
}

/// <summary>The result of reading a sentence: a reminder, or a friendly message (with whatever time part was understood).</summary>
public sealed record ParseOutcome(ParsedReminder? Value, string? Error, string When = "")
{
    public bool Ok => Value is not null;
}

/// <summary>
/// Reads natural sentences into reminders: "remind me at 5pm to call mom", "in 20 minutes stretch", "tomorrow 8am homework",
/// "every monday 7:30 trash", "on oct 12 dentist", "every 2 hours drink water". The time, date and repeat can be anywhere in the sentence.
/// Pure (no UI, no clock: <c>now</c> and the zone are passed in) and culture-independent, so tests pin every rule.
/// </summary>
/// <remarks>
/// Rules: a time that has already gone by today means tomorrow ("at 9am" at 3 pm); "at 5" means 5 pm, "7:30" means 7:30 am (but "at 8" at
/// 7 pm today means 8 pm); a day with no time uses the default time (9:00); a month and day that has gone by means next year; a weekday
/// name means the next one (today only if its time is still ahead); a repeat starts at its next occurrence; "every N hours" starts N hours from now.
/// </remarks>
public static class ReminderParser
{
    public const string Examples = "\"at 5pm call mom\", \"in 20 minutes stretch\", \"tomorrow 8am homework\", \"every monday 7:30 trash\"";

    private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private const string Day = "(?:mon(?:day)?|tue(?:s(?:day)?)?|wed(?:nesday)?|thu(?:r(?:s(?:day)?)?)?|fri(?:day)?|sat(?:urday)?|sun(?:day)?)";
    private const string DayFull = "(?:monday|tuesday|wednesday|thursday|friday|saturday|sunday)";
    private const string DayPlural = "(?:mondays|tuesdays|wednesdays|thursdays|fridays|saturdays|sundays)";
    private const string Month = "(?:jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|may|june?|july?|aug(?:ust)?|sept?(?:ember)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?)";
    private const string Word = "(?:one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|fifteen|twenty|thirty|forty-five|forty)";
    private const string Count = @"(?:\d+(?:\.\d+)?|an?|half\s+an?|half|(?:a\s+)?couple\s+of|" + Word + ")";
    private const string Unit = @"(?:seconds?|secs?|minutes?|mins?|hours?|hrs?|days?|weeks?|wks?|months?|[hm])";
    private const string Span = "(?:" + Count + @"\s*" + Unit + @"\b\s*(?:(?:,|and)\s*)?)+";

    /// <summary>Left where a time, date or repeat phrase was cut out, so words that belonged to it ("at", "on") can go with it.</summary>
    private const char Marker = '\u0001';

    private static Regex Rx(string pattern) => new(pattern, Opt);

    private static readonly Regex Note = Rx(@"(?:^|\s)(?:notes?|note to self)\s*:\s*(?<n>.+)$");
    private static readonly Regex Lead = Rx(@"^\s*(?:please\s+)?(?:(?:set\s+(?:a\s+)?|add\s+(?:a\s+)?|make\s+(?:a\s+)?|new\s+)?reminders?(?:\s+me)?|remind\s+me|remind|remember|don'?t\s+forget|dont\s+forget)\b\s*(?:to\b|that\b|about\b|:|-)?\s*");
    private static readonly Regex MonthlyDay = Rx(@"\b(?:(?:every|each)\s+month|monthly)\s+(?:on\s+)?(?:the\s+(?<d>\d{1,2})(?:st|nd|rd|th)?|(?<d>\d{1,2})(?:st|nd|rd|th))\b(?!\s*(?::|am\b|pm\b))|\b(?:on\s+)?the\s+(?<d>\d{1,2})(?:st|nd|rd|th)?\s+of\s+(?:every|each)\s+month\b|\b(?:every|each)\s+(?<d>\d{1,2})(?:st|nd|rd|th)\b");
    private static readonly Regex NthWeekday = Rx(@"\b(?:every|each)\s+(?:\d{1,2}(?:st|nd|rd|th)|first|second|third|fourth|last)\s+" + Day + @"\b");
    private static readonly Regex Every = Rx(@"\b(?:every|each)\s+(?:(?<n>\d+(?:\.\d+)?|other|half\s+an?|half|(?:a\s+)?couple\s+of|" + Word + @")\s+)?(?<u>minutes?|mins?|hours?|hrs?|days?|weeks?)\b");
    private static readonly Regex EveryPeriod = Rx(@"\b(?:every|each)\s+(?<p>morning|afternoon|evening|night)\b");
    private static readonly Regex WeekdaysWord = Rx(@"\b(?:(?:every|each|on)\s+)?(?:weekdays|week\s+days|workdays)\b|\b(?:every|each)\s+(?:weekday|workday|week\s+day)\b");
    private static readonly Regex WeekendWord = Rx(@"\b(?:(?:every|each|on)\s+)?weekends\b|\b(?:every|each)\s+weekend\b");
    private static readonly Regex DayList = Rx(@"\b(?:every|each)\s+(?<list>" + Day + @"(?:\s*(?:,|and|&)\s*" + Day + @")*)\b|\b(?:on\s+)?(?<list>" + DayPlural + @"(?:\s*(?:,|and|&)\s*" + DayPlural + @")*)\b");
    private static readonly Regex SimpleRepeat = Rx(@"\b(?<w>daily|everyday|hourly|weekly|monthly|nightly)\b");
    private static readonly Regex EveryMonth = Rx(@"\b(?:every|each)\s+month\b");
    private static readonly Regex Relative1 = Rx(@"\bin\s+(?<spec>" + Span + ")");
    private static readonly Regex Relative2 = Rx(@"\b(?<spec>" + Span + @")\s*from\s+now\b");
    private static readonly Regex Piece = Rx(@"(?<n>" + Count + @")\s*(?<u>" + Unit + @")\b");
    private static readonly Regex Iso = Rx(@"\b(?<y>\d{4})-(?<m>\d{1,2})-(?<d>\d{1,2})\b");
    private static readonly Regex Slash = Rx(@"(?<![\w/])(?<a>\d{1,2})/(?<b>\d{1,2})(?:/(?<y>\d{4}|\d{2}))?(?![\w/])");
    private static readonly Regex MonthDay = Rx(@"\b(?:on\s+)?(?<mon>" + Month + @")\.?\s+(?:the\s+)?(?<d>\d{1,2})(?:st|nd|rd|th)?\b(?:\s*,?\s+(?<y>20\d{2})\b)?");
    private static readonly Regex DayMonth = Rx(@"\b(?:on\s+)?(?:the\s+)?(?<d>\d{1,2})(?:st|nd|rd|th)?\s+(?:of\s+)?(?<mon>" + Month + @")\b\.?(?:\s*,?\s+(?<y>20\d{2})\b)?");
    private static readonly Regex AfterTomorrow = Rx(@"\bday\s+after\s+tomorrow\b");
    private static readonly Regex Tomorrow = Rx(@"\b(?:tomorrow|tmrw|tmr)\b");
    private static readonly Regex Today = Rx(@"\btoday\b");
    private static readonly Regex Tonight = Rx(@"\btonight\b");
    private static readonly Regex NextWeek = Rx(@"\bnext\s+week\b");
    private static readonly Regex NextMonth = Rx(@"\bnext\s+month\b");
    private static readonly Regex Weekday = Rx(@"\b(?:(?<pre>on|next|this|coming|upcoming)\s+)?(?<day>" + DayFull + @")\b|\b(?<pre>on|next|this)\s+(?<day>mon|tues?|wed|thu(?:rs?)?|fri|sat|sun)\b");
    private static readonly Regex ClockColon = Rx(@"(?<![\w:./-])(?:at\s+|@\s*)?(?<h>\d{1,2}):(?<m>\d{2})(?::\d{2})?\s*(?<ap>[ap]\.?m\.?)?(?![\w:])");
    private static readonly Regex ClockAmPm = Rx(@"(?<![\w:./-])(?:at\s+|@\s*)?(?<h>\d{1,2})\s*(?<ap>[ap]\.?m\.?)(?!\w)");
    private static readonly Regex ClockOClock = Rx(@"(?<![\w:./-])(?:at\s+)?(?<h>\d{1,2})\s*o'?clock\b");
    private static readonly Regex ClockAt = Rx(@"\bat\s+(?<h>\d{1,2})\b(?!\s*[:/\d-])");
    private static readonly Regex Noon = Rx(@"\b(?:at\s+)?(?<w>noon|midday|midnight)\b");
    private static readonly Regex Period = Rx(@"(?:\b(?<pre>this|in\s+the|the)\s+|(?<=\u0001\s*))(?<p>morning|afternoon|evening|night)\b");

    private enum DateKind { None, Today, Tomorrow, Weekday, MonthDay, Exact, NextWeek }

    private sealed class Work
    {
        public string Text = "";
        public string Note = "";
        public DateOnly? Date;
        public DateKind Kind;
        public bool HasYear;
        public DayOfWeek? Weekday;
        public TimeSpan? Time;
        /// <summary>A bare 7 to 11 ("at 8"): am, unless that has gone by today and pm has not.</summary>
        public bool Flexible;
        public ReminderRepeat Repeat = new();
        public (int Months, int Days, TimeSpan Span)? Relative;
        public string? Error;
    }

    /// <summary>Reads <paramref name="input"/>. <paramref name="now"/> is the current time and <paramref name="zone"/> the zone times mean.</summary>
    public static ParseOutcome Parse(string? input, DateTimeOffset now, TimeZoneInfo zone, ReminderParseOptions? options = null)
    {
        options ??= new ReminderParseOptions();
        string text = Regex.Replace((input ?? "").Replace('’', '\'').Replace('“', '"').Replace('”', '"').Trim(), @"\s+", " ");
        if (text.Length == 0) return new ParseOutcome(null, $"Type what to remind you of and when, like {Examples}.");
        var w = new Work();
        now -= TimeSpan.FromTicks(now.Ticks % TimeSpan.TicksPerSecond);
        var nowWall = ReminderSchedule.Wall(now, zone);

        if (Note.Match(text) is { Success: true } noteMatch) { w.Note = noteMatch.Groups["n"].Value.Trim(); text = text[..noteMatch.Index].Trim(); }
        w.Text = Lead.Replace(text, "");

        ExtractRepeat(w, options);
        if (w.Error is null) ExtractRelative(w);
        if (w.Error is null && w.Relative is null) ExtractDate(w, options, nowWall);
        if (w.Error is null) ExtractTime(w, options, nowWall);

        string? error = w.Error;
        string when = "";
        ParsedReminder? parsed = null;
        if (error is null)
        {
            string message = Clean(w.Text);
            if (Compose(w, now, nowWall, zone, options, out error) is { } composed)
            {
                when = Describe(composed.Due, composed.Repeat, now, zone, options);
                if (message.Length == 0 && options.RequireText) error = "What should I remind you about?";
                else parsed = new ParsedReminder(message, w.Note, composed.Due, composed.Repeat, when);
            }
        }
        return new ParseOutcome(parsed, parsed is null ? error ?? "I couldn't work out when. Try " + Examples + "." : null, when);
    }

    // ---------- Repeat ----------

    private static void ExtractRepeat(Work w, ReminderParseOptions options)
    {
        var r = w.Repeat;
        if (NthWeekday.IsMatch(w.Text)) { w.Error = "Repeats like \"every 2nd Monday\" aren't supported. Try \"every monday\" or \"every month on the 15th\"."; return; }
        if (Take(w, MonthlyDay) is { } monthly)
        {
            r.Kind = RepeatKind.Monthly;
            r.DayOfMonth = int.Parse(monthly.Groups["d"].Value, CultureInfo.InvariantCulture);
            if (r.DayOfMonth is < 1 or > 31) w.Error = "A month has no " + ReminderFormat.Ordinal(r.DayOfMonth) + " day.";
            return;
        }
        if (Take(w, Every) is { } every)
        {
            string n = every.Groups["n"].Value.ToLowerInvariant(), unit = every.Groups["u"].Value.ToLowerInvariant();
            double count = n.Length == 0 ? 1 : n == "other" ? 2 : CountOf(n);
            bool day = unit.StartsWith('d'), week = unit.StartsWith('w'), hour = unit.StartsWith('h');
            if (day || week)
            {
                if (count < 1 || count != Math.Floor(count)) { w.Error = "Repeats of days and weeks need a whole number."; return; }
                if (count == 1) { r.Kind = day ? RepeatKind.Daily : RepeatKind.Weekly; return; }
                r.Kind = RepeatKind.Interval;
                r.Unit = day ? IntervalUnit.Days : IntervalUnit.Weeks;
                r.Every = (int)count;
                return;
            }
            double minutes = count * (hour ? 60 : 1);
            if (minutes < 1 || Math.Abs(minutes - Math.Round(minutes)) > 1e-9) { w.Error = "The shortest repeat is every minute."; return; }
            r.Kind = RepeatKind.Interval;
            if (hour && Math.Abs(count - Math.Round(count)) < 1e-9) { r.Unit = IntervalUnit.Hours; r.Every = (int)Math.Round(count); }
            else { r.Unit = IntervalUnit.Minutes; r.Every = (int)Math.Round(minutes); }
            if (r.Every > 10_000) w.Error = "That repeat is too long.";
            return;
        }
        if (Take(w, EveryPeriod) is { } period) { r.Kind = RepeatKind.Daily; w.Time = PeriodTime(period.Groups["p"].Value, options); return; }
        if (Take(w, WeekdaysWord) is not null) { r.Kind = RepeatKind.Weekdays; return; }
        if (Take(w, WeekendWord) is not null) { r.Kind = RepeatKind.Weekly; r.Days = [DayOfWeek.Saturday, DayOfWeek.Sunday]; return; }
        if (Take(w, DayList) is { } list)
        {
            r.Kind = RepeatKind.Weekly;
            foreach (Match m in Regex.Matches(list.Groups["list"].Value, Day + "|" + DayPlural, Opt))
                if (DayOf(m.Value) is { } d && !r.Days.Contains(d)) r.Days.Add(d);
            return;
        }
        if (Take(w, SimpleRepeat) is { } simple)
        {
            switch (simple.Groups["w"].Value.ToLowerInvariant())
            {
                case "daily" or "everyday": r.Kind = RepeatKind.Daily; break;
                case "nightly": r.Kind = RepeatKind.Daily; w.Time = PeriodTime("night", options); break;
                case "hourly": r.Kind = RepeatKind.Interval; r.Unit = IntervalUnit.Hours; r.Every = 1; break;
                case "weekly": r.Kind = RepeatKind.Weekly; break;
                default: r.Kind = RepeatKind.Monthly; break;
            }
            return;
        }
        if (Take(w, EveryMonth) is not null) r.Kind = RepeatKind.Monthly;
    }

    // ---------- Relative ("in 20 minutes") ----------

    private static void ExtractRelative(Work w)
    {
        if (w.Repeat.Kind == RepeatKind.Interval && ReminderSchedule.Elapsed(w.Repeat) is not null) return;
        var m = Take(w, Relative1) ?? Take(w, Relative2);
        if (m is null) return;
        int months = 0, days = 0;
        var span = TimeSpan.Zero;
        foreach (Match piece in Piece.Matches(m.Groups["spec"].Value))
        {
            double n = CountOf(piece.Groups["n"].Value.ToLowerInvariant());
            string u = piece.Groups["u"].Value.ToLowerInvariant();
            if (u.StartsWith("mo")) months += (int)Math.Round(n);
            else if (u.StartsWith('d')) days += (int)Math.Round(n);
            else if (u.StartsWith('w')) days += (int)Math.Round(n * 7);
            else if (u.StartsWith('h')) span += TimeSpan.FromHours(n);
            else if (u.StartsWith('s')) span += TimeSpan.FromSeconds(n);
            else span += TimeSpan.FromMinutes(n);
        }
        if (months == 0 && days == 0 && span <= TimeSpan.Zero) { w.Error = "How long from now? Try \"in 20 minutes\"."; return; }
        if (span > TimeSpan.FromDays(366 * 5) || days > 366 * 5 || months > 12 * 5) { w.Error = "That is too far away."; return; }
        w.Relative = (months, days, span);
    }

    // ---------- Date ----------

    private static void ExtractDate(Work w, ReminderParseOptions options, DateTime nowWall)
    {
        var today = DateOnly.FromDateTime(nowWall);
        if (Take(w, Iso) is { } iso)
        {
            if (!TryDate(int.Parse(iso.Groups["y"].Value, CultureInfo.InvariantCulture), int.Parse(iso.Groups["m"].Value, CultureInfo.InvariantCulture), int.Parse(iso.Groups["d"].Value, CultureInfo.InvariantCulture), out var d)) { w.Error = "That date doesn't exist."; return; }
            w.Date = d; w.Kind = DateKind.Exact; w.HasYear = true; return;
        }
        if (Take(w, Slash) is { } slash)
        {
            int a = int.Parse(slash.Groups["a"].Value, CultureInfo.InvariantCulture), b = int.Parse(slash.Groups["b"].Value, CultureInfo.InvariantCulture);
            int month = options.DayFirst ? b : a, dom = options.DayFirst ? a : b;
            if (month > 12 && dom <= 12) (month, dom) = (dom, month);
            int year = today.Year;
            bool hasYear = slash.Groups["y"].Success;
            if (hasYear) { year = int.Parse(slash.Groups["y"].Value, CultureInfo.InvariantCulture); if (year < 100) year += 2000; }
            if (!TryDate(year, month, dom, out var d)) { w.Error = "That date doesn't exist."; return; }
            w.Date = d; w.HasYear = hasYear; w.Kind = hasYear ? DateKind.Exact : DateKind.MonthDay; return;
        }
        var md = Take(w, MonthDay) ?? Take(w, DayMonth);
        if (md is not null)
        {
            int month = MonthOf(md.Groups["mon"].Value), dom = int.Parse(md.Groups["d"].Value, CultureInfo.InvariantCulture);
            bool hasYear = md.Groups["y"].Success;
            int year = hasYear ? int.Parse(md.Groups["y"].Value, CultureInfo.InvariantCulture) : today.Year;
            if (!TryDate(year, month, dom, out var d)) { w.Error = "That date doesn't exist."; return; }
            w.Date = d; w.HasYear = hasYear; w.Kind = hasYear ? DateKind.Exact : DateKind.MonthDay; return;
        }
        if (Take(w, AfterTomorrow) is not null) { w.Date = today.AddDays(2); w.Kind = DateKind.Exact; return; }
        if (Take(w, Tomorrow) is not null) { w.Date = today.AddDays(1); w.Kind = DateKind.Tomorrow; return; }
        if (Take(w, Tonight) is not null) { w.Date = today; w.Kind = DateKind.Today; w.Time ??= new TimeSpan(20, 0, 0); return; }
        if (Take(w, Today) is not null) { w.Date = today; w.Kind = DateKind.Today; return; }
        if (Take(w, NextWeek) is not null) { w.Date = today.AddDays(7); w.Kind = DateKind.NextWeek; return; }
        if (Take(w, NextMonth) is not null) { w.Date = today.AddMonths(1); w.Kind = DateKind.NextWeek; return; }
        if (Take(w, Weekday) is { } wd)
        {
            var dow = DayOf(wd.Groups["day"].Value)!.Value;
            string pre = wd.Groups["pre"].Success ? wd.Groups["pre"].Value.ToLowerInvariant() : "";
            int days = ((int)dow - (int)today.DayOfWeek + 7) % 7;
            if (pre == "next" && days == 0) days = 7;
            w.Date = today.AddDays(days);
            w.Kind = DateKind.Weekday;
            w.Weekday = dow;
        }
    }

    // ---------- Time of day ----------

    private static void ExtractTime(Work w, ReminderParseOptions options, DateTime nowWall)
    {
        Match? m;
        if ((m = Take(w, ClockColon)) is not null) SetClock(w, m, int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture));
        else if ((m = Take(w, ClockAmPm)) is not null) SetClock(w, m, 0);
        else if ((m = Take(w, ClockOClock)) is not null) SetClock(w, m, 0);
        else if ((m = Take(w, ClockAt)) is not null) SetClock(w, m, 0);
        else if ((m = Take(w, Noon)) is not null) { w.Time = m.Groups["w"].Value.Equals("midnight", StringComparison.OrdinalIgnoreCase) ? TimeSpan.Zero : new TimeSpan(12, 0, 0); w.Flexible = false; }
        if (w.Error is not null) return;
        // "tomorrow morning", "this evening", "in the afternoon": a part of the day gives the time when no clock time was said.
        if (Take(w, Period) is { } period && w.Time is null)
        {
            w.Time = PeriodTime(period.Groups["p"].Value, options);
            if (period.Groups["pre"].Value.Equals("this", StringComparison.OrdinalIgnoreCase) && w.Date is null) { w.Date = DateOnly.FromDateTime(nowWall); w.Kind = DateKind.Today; }
        }
    }

    private static void SetClock(Work w, Match m, int minute)
    {
        int hour = int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture);
        string ap = m.Groups["ap"].Success ? m.Groups["ap"].Value.ToLowerInvariant() : "";
        if (minute > 59 || hour > 23 || ap.Length > 0 && hour is < 1 or > 12) { w.Error = "That time doesn't look right."; return; }
        w.Flexible = false;
        if (ap.StartsWith('p')) hour = hour % 12 + 12;
        else if (ap.StartsWith('a')) hour %= 12;
        else if (hour is >= 1 and <= 6) hour += 12;
        else if (hour is >= 7 and <= 11) w.Flexible = true;
        w.Time = new TimeSpan(hour, minute, 0);
    }

    private static TimeSpan PeriodTime(string period, ReminderParseOptions options) => period.ToLowerInvariant() switch
    {
        "morning" => options.DefaultTime,
        "afternoon" => new TimeSpan(15, 0, 0),
        "evening" => new TimeSpan(18, 0, 0),
        _ => new TimeSpan(21, 0, 0),
    };

    // ---------- Putting it together ----------

    private static (DateTimeOffset Due, ReminderRepeat Repeat)? Compose(Work w, DateTimeOffset now, DateTime nowWall, TimeZoneInfo zone, ReminderParseOptions options, out string? error)
    {
        error = null;
        var repeat = w.Repeat.Copy();
        var today = DateOnly.FromDateTime(nowWall);
        TimeSpan? time = w.Time;

        // Every N minutes or hours: counted in real time from now (or from the time given).
        if (repeat.Kind == RepeatKind.Interval && ReminderSchedule.Elapsed(repeat) is { } span)
        {
            if (w.Date is null && time is null) return (ReminderSchedule.Normalize(now + span, zone), repeat);
            var start = OneOff(w, now, nowWall, zone, options, out error);
            return start is null ? null : (start.Value, repeat);
        }

        if (w.Relative is { } rel)
        {
            if (rel.Months == 0 && rel.Days == 0 && time is null) return (ReminderSchedule.Normalize(now + rel.Span, zone), new ReminderRepeat());
            var baseWall = nowWall.AddMonths(rel.Months).AddDays(rel.Days);
            var due = time is { } t ? ReminderSchedule.At(baseWall.Date + t, zone) : ReminderSchedule.Normalize(ReminderSchedule.At(baseWall, zone) + rel.Span, zone);
            if (due <= now) { error = "That time has already passed."; return null; }
            return (due, new ReminderRepeat());
        }

        if (repeat.IsRepeating)
        {
            var tod = time ?? options.DefaultTime;
            repeat.Time = tod;
            if (repeat.Kind == RepeatKind.Weekly && repeat.Days.Count == 0) repeat.Days.Add(w.Weekday ?? w.Date?.DayOfWeek ?? today.DayOfWeek);
            if (repeat.Kind == RepeatKind.Monthly && repeat.DayOfMonth == 0)
            {
                if (w.Date is { } d) repeat.DayOfMonth = d.Day;
                else repeat.DayOfMonth = (ReminderSchedule.At(nowWall.Date + tod, zone) > now ? today : today.AddDays(1)).Day;
            }
            var after = now;
            if (w.Date is { } startDay && startDay > today) after = ReminderSchedule.At(startDay.ToDateTime(TimeOnly.MinValue), zone).AddTicks(-1);
            var ring = ReminderSchedule.First(repeat, tod, after, zone);
            if (ring is null) { error = "I couldn't work out when that repeats."; return null; }
            return (ring.Value, repeat);
        }

        var one = OneOff(w, now, nowWall, zone, options, out error);
        return one is null ? null : (one.Value, new ReminderRepeat());
    }

    private static DateTimeOffset? OneOff(Work w, DateTimeOffset now, DateTime nowWall, TimeZoneInfo zone, ReminderParseOptions options, out string? error)
    {
        error = null;
        var today = DateOnly.FromDateTime(nowWall);
        if (w.Time is null && w.Date is null) { error = "When should I remind you? Add a time like " + Examples + "."; return null; }
        var tod = w.Time ?? options.DefaultTime;
        if (w.Date is null || w.Kind == DateKind.Today)
        {
            var day = (w.Date ?? today).ToDateTime(TimeOnly.MinValue);
            var at = ReminderSchedule.At(day + tod, zone);
            if (at > now) return at;
            // "at 8" may mean 8 pm when 8 am has gone by.
            if (w.Flexible && ReminderSchedule.At(day + tod + TimeSpan.FromHours(12), zone) is var pm && pm > now) return pm;
            if (w.Kind == DateKind.Today) { error = "That time has already passed today."; return null; }
            return ReminderSchedule.At(day.AddDays(1) + tod, zone);
        }
        var due = ReminderSchedule.At(w.Date.Value.ToDateTime(TimeOnly.MinValue) + tod, zone);
        if (due > now) return due;
        switch (w.Kind)
        {
            case DateKind.Weekday: return ReminderSchedule.At(w.Date.Value.AddDays(7).ToDateTime(TimeOnly.MinValue) + tod, zone);
            case DateKind.MonthDay when !w.HasYear: return ReminderSchedule.At(w.Date.Value.AddYears(1).ToDateTime(TimeOnly.MinValue) + tod, zone);
            default: error = w.Date == today ? "That time has already passed today." : "That date is in the past."; return null;
        }
    }

    private static string Describe(DateTimeOffset due, ReminderRepeat repeat, DateTimeOffset now, TimeZoneInfo zone, ReminderParseOptions options)
    {
        bool h24 = options.Use24Hour;
        if (!repeat.IsRepeating) return ReminderFormat.When(due, now, zone, h24, " at ");
        string next = ReminderFormat.Day(ReminderSchedule.Wall(due, zone), ReminderSchedule.Wall(now, zone));
        if (next is "Today" or "Tomorrow") next = next.ToLowerInvariant();
        if (repeat.Kind == RepeatKind.Interval && repeat.Unit is IntervalUnit.Minutes or IntervalUnit.Hours) next += " at " + ReminderFormat.Time(ReminderSchedule.Wall(due, zone), h24);
        return ReminderFormat.RepeatWithTime(repeat, h24) + " · next " + next;
    }

    // ---------- Helpers ----------

    /// <summary>Finds the first match, cuts it out of the sentence (leaving a marker) and returns it.</summary>
    private static Match? Take(Work w, Regex regex)
    {
        var m = regex.Match(w.Text);
        if (!m.Success) return null;
        w.Text = w.Text[..m.Index] + Marker + w.Text[(m.Index + m.Length)..];
        return m;
    }

    private static readonly Regex Dangling = new(@"(?:\b(?:at|on|in|by|for|from|and|then|until|till)\s+|[,;:]\s*)*\u0001+(?:\s*(?:and|then|,|;)\s*(?=\u0001))?", Opt);

    /// <summary>What is left of the sentence: the words that went with a cut-out time are gone too, no dangling "to" at the ends, first letter capital.</summary>
    private static string Clean(string text)
    {
        text = Dangling.Replace(text, " ").Replace(Marker, ' ');
        for (int i = 0; i < 6; i++)
        {
            string before = text;
            text = Regex.Replace(text, @"\s+", " ").Trim();
            text = Lead.Replace(text, "");
            text = Regex.Replace(text, @"^(?:(?:to|that|about|and|then)\b\s*|[,;:\-]\s*)", "", Opt).Trim();
            text = Regex.Replace(text, @"\s*(?:\b(?:and|then|every|each)|[,;:\-])$", "", Opt).Trim();
            if (text == before) break;
        }
        text = text.Trim('"', ' ');
        return text.Length > 0 && char.IsLower(text[0]) ? char.ToUpperInvariant(text[0]) + text[1..] : text;
    }

    private static double CountOf(string token) => token switch
    {
        "a" or "an" or "one" => 1,
        "half" or "half a" or "half an" => 0.5,
        "couple of" or "a couple of" or "couple" or "two" => 2,
        "three" => 3, "four" => 4, "five" => 5, "six" => 6, "seven" => 7, "eight" => 8, "nine" => 9, "ten" => 10, "eleven" => 11, "twelve" => 12,
        "fifteen" => 15, "twenty" => 20, "thirty" => 30, "forty" => 40, "forty-five" => 45,
        _ => double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : Regex.IsMatch(token, @"^half\s+an?$") ? 0.5 : 1,
    };

    private static DayOfWeek? DayOf(string name) => name.Trim().ToLowerInvariant() switch
    {
        var s when s.StartsWith("mon") => DayOfWeek.Monday,
        var s when s.StartsWith("tu") => DayOfWeek.Tuesday,
        var s when s.StartsWith("we") => DayOfWeek.Wednesday,
        var s when s.StartsWith("th") => DayOfWeek.Thursday,
        var s when s.StartsWith("fr") => DayOfWeek.Friday,
        var s when s.StartsWith("sa") => DayOfWeek.Saturday,
        var s when s.StartsWith("su") => DayOfWeek.Sunday,
        _ => null,
    };

    private static int MonthOf(string name) => name.ToLowerInvariant()[..3] switch
    {
        "jan" => 1, "feb" => 2, "mar" => 3, "apr" => 4, "may" => 5, "jun" => 6, "jul" => 7, "aug" => 8, "sep" => 9, "oct" => 10, "nov" => 11, _ => 12,
    };

    private static bool TryDate(int year, int month, int day, out DateOnly date)
    {
        date = default;
        if (year is < 2000 or > 2200 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)) return false;
        date = new DateOnly(year, month, day);
        return true;
    }
}
