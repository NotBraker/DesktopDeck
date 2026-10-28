using DesktopDeck.Models;
using DesktopDeck.Services;

/// <summary>Reminders: the natural-language parser, the repeat rules (DST, weekdays, monthly on the 31st), ringing once, catch-up, snooze, quiet hours. No UI.</summary>
public static class ReminderTests
{
    private static Action<bool, string> check = (_, _) => { };
    private static TimeZoneInfo utc = TimeZoneInfo.Utc;
    private static TimeZoneInfo pacific = TimeZoneInfo.Utc;

    // Tuesday, 6 October 2026, 15:00.
    private static DateTimeOffset Now(TimeZoneInfo zone) => ReminderSchedule.At(new DateTime(2026, 10, 6, 15, 0, 0), zone);

    private static DateTimeOffset At(TimeZoneInfo zone, int y, int mo, int d, int h = 0, int mi = 0) => ReminderSchedule.At(new DateTime(y, mo, d, h, mi, 0), zone);

    private static TimeZoneInfo? Zone(params string[] ids)
    {
        foreach (var id in ids) if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var z)) return z;
        return null;
    }

    public static void Run(Action<bool, string> checkFn, string folder)
    {
        check = checkFn;
        var found = Zone("Pacific Standard Time", "America/Los_Angeles");
        if (found is null) Console.WriteLine("SKIP: no Pacific time zone on this machine; the daylight saving tests run in UTC only");
        pacific = found ?? TimeZoneInfo.Utc;
        Parsing();
        ParsingRepeats();
        ParsingDates();
        ParsingTimes();
        ParsingErrors();
        RoundTrip();
        Schedule();
        if (found is not null) DaylightSaving();
        Ringing();
        CatchUp();
        SnoozeAndDone();
        Quiet();
        Settings(folder);
        Groups();
    }

    private static ParseOutcome P(string text, TimeZoneInfo? zone = null, ReminderParseOptions? options = null, DateTimeOffset? now = null)
    {
        zone ??= utc;
        return ReminderParser.Parse(text, now ?? Now(zone), zone, options);
    }

    private static ParsedReminder Ok(string text, TimeZoneInfo? zone = null, ReminderParseOptions? options = null)
    {
        var outcome = P(text, zone, options);
        if (!outcome.Ok) throw new Exception($"FAIL: \"{text}\" should parse but said: {outcome.Error}");
        return outcome.Value!;
    }

    private static void Same(string text, string expectedText, DateTimeOffset due, string description, TimeZoneInfo? zone = null)
    {
        var outcome = P(text, zone);
        bool ok = outcome.Ok && outcome.Value!.Text == expectedText && outcome.Value.Due == due && !outcome.Value.Repeat.IsRepeating;
        check(ok, $"{description} (\"{text}\"){(outcome.Ok ? "" : ": " + outcome.Error)}{(ok || !outcome.Ok ? "" : $" got \"{outcome.Value!.Text}\" at {outcome.Value.Due:u}, wanted {due:u}")}");
    }

    // ---------- The sentence parser ----------

    private static void Parsing()
    {
        var now = Now(utc);
        Same("remind me at 5pm to call mom", "Call mom", At(utc, 2026, 10, 6, 17), "\"remind me at 5pm to call mom\" is today at 5 pm");
        Same("at 5pm call mom", "Call mom", At(utc, 2026, 10, 6, 17), "time first");
        Same("call mom at 5pm", "Call mom", At(utc, 2026, 10, 6, 17), "time last");
        Same("Remind me to call mom at 5 pm", "Call mom", At(utc, 2026, 10, 6, 17), "remind me to ... at 5 pm");
        Same("don't forget to call mom at 5:30pm", "Call mom", At(utc, 2026, 10, 6, 17, 30), "don't forget to");
        Same("reminder: pay rent tomorrow", "Pay rent", At(utc, 2026, 10, 7, 9), "a day with no time uses 9:00");
        Same("remind me about the test tomorrow 7am", "The test", At(utc, 2026, 10, 7, 7), "remind me about");
        Same("in 20 minutes stretch", "Stretch", now.AddMinutes(20), "in 20 minutes");
        Same("remind me in 20 minutes to stretch", "Stretch", now.AddMinutes(20), "remind me in 20 minutes to stretch");
        Same("stretch in 20 min", "Stretch", now.AddMinutes(20), "message first, \"min\"");
        Same("in an hour check the oven", "Check the oven", now.AddHours(1), "in an hour");
        Same("in half an hour take a break", "Take a break", now.AddMinutes(30), "in half an hour");
        Same("in 1 hour and 30 minutes laundry", "Laundry", now.AddMinutes(90), "in 1 hour and 30 minutes");
        Same("in 2h laundry", "Laundry", now.AddHours(2), "in 2h");
        Same("laundry 45 minutes from now", "Laundry", now.AddMinutes(45), "N minutes from now");
        Same("in 3 days renew the pass", "Renew the pass", At(utc, 2026, 10, 9, 15), "in 3 days keeps the time of day");
        Same("in 2 weeks at 8am vote", "Vote", At(utc, 2026, 10, 20, 8), "in 2 weeks at 8am");
        Same("tomorrow 8am homework", "Homework", At(utc, 2026, 10, 7, 8), "tomorrow 8am homework");
        Same("homework tmrw at 8", "Homework", At(utc, 2026, 10, 7, 8), "tmrw at 8 is 8 am (7 to 11 means am)");
        Same("tomorrow morning run", "Run", At(utc, 2026, 10, 7, 9), "tomorrow morning");
        Same("this evening water the plants", "Water the plants", At(utc, 2026, 10, 6, 18), "this evening");
        Same("tonight read", "Read", At(utc, 2026, 10, 6, 20), "tonight is 8 pm");
        Same("tonight at 9 read", "Read", At(utc, 2026, 10, 6, 21), "tonight at 9");
        Same("day after tomorrow 4pm recital", "Recital", At(utc, 2026, 10, 8, 16), "day after tomorrow");
        Same("remind me at 5pm to talk to mom", "Talk to mom", At(utc, 2026, 10, 6, 17), "\"to\" inside the message stays");
        Same("remind me at 5pm to meet at the gym", "Meet at the gym", At(utc, 2026, 10, 6, 17), "\"at\" inside the message stays");
        var note = Ok("call mom at 5pm note: ask about the trip");
        check(note.Text == "Call mom" && note.Note == "ask about the trip", "a note: part becomes the note");
        check(P("  CALL MOM   AT   5PM ").Value?.Due == At(utc, 2026, 10, 6, 17), "case and spacing do not matter");
        var h24 = Ok("at 17:45 pickup");
        check(h24.Due == At(utc, 2026, 10, 6, 17, 45) && h24.Text == "Pickup", "24-hour times");
        check(Ok("pickup at 17:45", null, new ReminderParseOptions { Use24Hour = true }).When == "Today at 17:45", "the interpretation follows the 24-hour setting");
        check(Ok("pickup at 5:45pm").When == "Today at 5:45 PM", "the interpretation of a one-off: " + Ok("pickup at 5:45pm").When);
    }

    private static void ParsingRepeats()
    {
        var now = Now(utc);
        var trash = Ok("every monday 7:30 trash");
        check(trash.Text == "Trash" && trash.Repeat.Kind == RepeatKind.Weekly && trash.Repeat.Days.SequenceEqual([DayOfWeek.Monday]) && trash.Repeat.Time == new TimeSpan(7, 30, 0) && trash.Due == At(utc, 2026, 10, 12, 7, 30), "\"every monday 7:30 trash\": weekly, Monday 7:30 am, first on the 12th");
        check(trash.When == "Every Monday at 7:30 AM · next Mon", "its interpretation: " + trash.When);
        var water = Ok("every 2 hours drink water");
        check(water.Text == "Drink water" && water.Repeat is { Kind: RepeatKind.Interval, Unit: IntervalUnit.Hours, Every: 2 } && water.Due == now.AddHours(2), "\"every 2 hours drink water\" starts two hours from now");
        var half = Ok("every half hour stand up");
        check(half.Repeat is { Kind: RepeatKind.Interval, Unit: IntervalUnit.Minutes, Every: 30 }, "every half hour is every 30 minutes");
        var mins = Ok("every 45 minutes posture");
        check(mins.Repeat is { Unit: IntervalUnit.Minutes, Every: 45 } && mins.Due == now.AddMinutes(45), "every 45 minutes");
        check(Ok("every hour stretch").Repeat is { Kind: RepeatKind.Interval, Unit: IntervalUnit.Hours, Every: 1 }, "every hour");
        check(Ok("hourly stretch").Repeat is { Kind: RepeatKind.Interval, Unit: IntervalUnit.Hours, Every: 1 }, "hourly");
        var daily = Ok("every day at 8pm brush teeth");
        check(daily.Repeat is { Kind: RepeatKind.Daily } && daily.Repeat.Time == new TimeSpan(20, 0, 0) && daily.Due == At(utc, 2026, 10, 6, 20) && daily.Text == "Brush teeth", "every day at 8pm is today 8 pm first");
        var daily9 = Ok("daily 9am vitamins");
        check(daily9.Repeat.Kind == RepeatKind.Daily && daily9.Due == At(utc, 2026, 10, 7, 9), "daily 9am (9 am has gone by today) starts tomorrow");
        check(Ok("every morning stretch").Repeat.Time == new TimeSpan(9, 0, 0), "every morning is 9:00");
        check(Ok("every evening walk").Repeat.Time == new TimeSpan(18, 0, 0), "every evening is 18:00");
        var weekdays = Ok("every weekday 7am gym");
        check(weekdays.Repeat.Kind == RepeatKind.Weekdays && weekdays.Due == At(utc, 2026, 10, 7, 7) && weekdays.Text == "Gym", "every weekday 7am gym");
        var multi = Ok("every mon, wed and fri 6pm run");
        check(multi.Repeat.Days.SequenceEqual([DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday]) && multi.Due == At(utc, 2026, 10, 7, 18) && multi.Text == "Run", "every mon, wed and fri 6pm");
        var weekend = Ok("every weekend sleep in");
        check(weekend.Repeat.Days.OrderBy(d => d).SequenceEqual([DayOfWeek.Sunday, DayOfWeek.Saturday]) && weekend.Due == At(utc, 2026, 10, 10, 9), "every weekend is Saturday and Sunday");
        check(Ok("trash mondays").Repeat.Days.SequenceEqual([DayOfWeek.Monday]), "plural weekday names repeat weekly");
        var rent = Ok("every month on the 15th pay rent");
        check(rent.Repeat is { Kind: RepeatKind.Monthly, DayOfMonth: 15 } && rent.Due == At(utc, 2026, 10, 15, 9) && rent.Text == "Pay rent", "every month on the 15th");
        check(Ok("pay rent on the 1st of every month").Repeat is { Kind: RepeatKind.Monthly, DayOfMonth: 1 }, "on the 1st of every month");
        var last = Ok("every month on the 31st backup");
        check(last.Repeat.DayOfMonth == 31 && last.Due == At(utc, 2026, 10, 31, 9), "every month on the 31st starts on Oct 31");
        var monthly = Ok("monthly report at 4pm");
        check(monthly.Repeat is { Kind: RepeatKind.Monthly, DayOfMonth: 6 } && monthly.Due == At(utc, 2026, 10, 6, 16), "monthly with no day uses today's date: " + monthly.When);
        check(Ok("every 2 weeks water the cactus").Repeat is { Kind: RepeatKind.Interval, Unit: IntervalUnit.Weeks, Every: 2 }, "every 2 weeks");
        check(Ok("every other day floss").Repeat is { Kind: RepeatKind.Interval, Unit: IntervalUnit.Days, Every: 2 }, "every other day");
        check(Ok("every week on thursday trash").Repeat.Days.SequenceEqual([DayOfWeek.Thursday]), "every week on thursday");
        check(Ok("weekly review").Repeat is { Kind: RepeatKind.Weekly } r && r.Days.SequenceEqual([DayOfWeek.Tuesday]), "weekly with no day uses today's weekday");
        var sameDay = P("every tuesday 9am standup", utc, null, Now(utc));
        check(sameDay.Value?.Due == At(utc, 2026, 10, 13, 9), "every tuesday at 9am on a Tuesday afternoon starts next week");
        var morning = P("every tuesday 9am standup", utc, null, At(utc, 2026, 10, 6, 8));
        check(morning.Value?.Due == At(utc, 2026, 10, 6, 9), "...but this morning it starts today");
        check(Ok("every day at 2pm", null, new ReminderParseOptions { RequireText = false }).Text == "", "a repeat with no message is fine when only the time is wanted");
        check(!P("every 0 hours stretch").Ok, "every 0 hours is not a repeat");
        check(P("meeting every 2nd monday").Error?.Contains("aren't supported") == true, "\"every 2nd Monday\" is refused with an explanation instead of guessed");
    }

    private static void ParsingDates()
    {
        Same("on oct 12 dentist", "Dentist", At(utc, 2026, 10, 12, 9), "on oct 12 dentist is 9:00");
        Same("dentist october 12th at 3pm", "Dentist", At(utc, 2026, 10, 12, 15), "october 12th at 3pm");
        Same("dentist 12 oct", "Dentist", At(utc, 2026, 10, 12, 9), "12 oct");
        Same("dentist oct 12 2027", "Dentist", At(utc, 2027, 10, 12, 9), "with a year");
        Same("dentist 2026-10-20 at 4pm", "Dentist", At(utc, 2026, 10, 20, 16), "ISO date");
        Same("exam on oct 1", "Exam", At(utc, 2027, 10, 1, 9), "a month and day that has gone by means next year");
        Same("dentist 10/12", "Dentist", At(utc, 2026, 10, 12, 9), "10/12 is month first by default");
        check(P("dentist 10/12", null, new ReminderParseOptions { DayFirst = true }).Value?.Due == At(utc, 2026, 12, 10, 9), "10/12 is day first when asked");
        Same("monday standup", "Standup", At(utc, 2026, 10, 12, 9), "monday on a Tuesday is the next Monday");
        Same("on wednesday dentist", "Dentist", At(utc, 2026, 10, 7, 9), "on wednesday");
        Same("next friday call", "Call", At(utc, 2026, 10, 9, 9), "next friday");
        Same("tuesday standup", "Standup", At(utc, 2026, 10, 13, 9), "tuesday on a Tuesday after 9 am means next week");
        Same("this tuesday 5pm standup", "Standup", At(utc, 2026, 10, 6, 17), "this tuesday 5pm is today");
        Same("next week call", "Call", At(utc, 2026, 10, 13, 9), "next week is seven days from now");
    }

    private static void ParsingTimes()
    {
        Same("call at 5", "Call", At(utc, 2026, 10, 6, 17), "a bare \"at 5\" is 5 pm");
        Same("call at 8", "Call", At(utc, 2026, 10, 6, 20), "\"at 8\" at 3 pm is 8 pm today (8 am has gone by)");
        var early = ReminderParser.Parse("call at 8", At(utc, 2026, 10, 6, 6), utc);
        check(early.Value?.Due == At(utc, 2026, 10, 6, 8), "\"at 8\" at 6 am is 8 am today");
        Same("call at 7:30", "Call", At(utc, 2026, 10, 6, 19, 30), "a bare 7:30 at 3 pm is 7:30 pm today (7:30 am has gone by)");
        Same("call tomorrow at 7:30", "Call", At(utc, 2026, 10, 7, 7, 30), "...but with a day it is am");
        Same("call at noon", "Call", At(utc, 2026, 10, 7, 12), "noon has gone by, so tomorrow");
        Same("call at midnight", "Call", At(utc, 2026, 10, 7, 0), "midnight is the next midnight");
        Same("call at 2pm", "Call", At(utc, 2026, 10, 7, 14), "a time that has gone by today means tomorrow");
        Same("call at 3pm", "Call", At(utc, 2026, 10, 7, 15), "exactly now has gone by");
        Same("call at 3:01 pm", "Call", At(utc, 2026, 10, 6, 15, 1), "a minute from now is today");
        Same("call at 12am", "Call", At(utc, 2026, 10, 7, 0), "12am is midnight");
        Same("call at 12pm tomorrow", "Call", At(utc, 2026, 10, 7, 12), "12pm is noon");
        Same("call at 5 o'clock", "Call", At(utc, 2026, 10, 6, 17), "5 o'clock");
        Same("call @ 6pm", "Call", At(utc, 2026, 10, 6, 18), "@ 6pm");
        var m = P("call at 5pm today");
        check(m.Ok && m.Value!.Due == At(utc, 2026, 10, 6, 17), "\"today\" is allowed");
    }

    private static void ParsingErrors()
    {
        check(!P("").Ok && P("").Error!.Contains("Type what"), "empty text asks for a reminder");
        var noTime = P("call mom");
        check(!noTime.Ok && noTime.Error!.Contains("When should I remind you"), "no time: a friendly question with examples");
        var noText = P("at 5pm");
        check(!noText.Ok && noText.Error == "What should I remind you about?" && noText.When == "Today at 5:00 PM", "no message: asks, and still shows how the time was read");
        check(P("dentist feb 30").Error == "That date doesn't exist.", "feb 30");
        check(P("dentist 2026-13-01").Error == "That date doesn't exist.", "month 13");
        check(P("dentist on 2026-01-05").Error == "That date is in the past.", "a date with a year that has gone by");
        check(P("call today at 9am").Error == "That time has already passed today.", "today at 9am at 3 pm");
        check(P("call at 25:00").Error == "That time doesn't look right." || !P("call at 25:00").Ok, "25:00 is not a time");
        check(P("call at 13pm").Error == "That time doesn't look right.", "13pm");
        check(P("call at 9:75").Error == "That time doesn't look right.", "9:75");
        check(!P("in 99999 days call").Ok, "too far away");
        check(P("every month on the 40th pay").Error is not null || P("every month on the 40th pay").Value?.Repeat.DayOfMonth != 40, "the 40th of the month is not accepted");
        check(!P("call yesterday at 5pm").Ok || P("call yesterday at 5pm").Value!.Due == At(utc, 2026, 10, 6, 17), "\"yesterday\" is not a date (the time still reads)");
    }

    private static void RoundTrip()
    {
        var now = Now(utc);
        foreach (var text in new[] { "call mom at 5pm", "tomorrow 8am homework", "every monday 7:30 trash", "on oct 12 dentist", "every weekday 7am gym", "every mon, wed and fri 6pm run", "every month on the 31st backup", "every 2 hours drink water", "every day at 8pm brush teeth", "dentist oct 12 2027 at 3pm note: bring the form", "in 20 minutes stretch" })
        {
            var first = Ok(text);
            var reminder = first.ToReminder(now);
            string natural = ReminderFormat.ToNatural(reminder, now, utc);
            var again = P(natural);
            bool same = again.Ok && again.Value!.Text == first.Text && again.Value.Note == first.Note && again.Value.Repeat.Kind == first.Repeat.Kind && again.Value.Repeat.Days.SequenceEqual(first.Repeat.Days)
                && again.Value.Repeat.Every == first.Repeat.Every && again.Value.Repeat.DayOfMonth == first.Repeat.DayOfMonth && (again.Value.Repeat.Kind == RepeatKind.Interval || again.Value.Due == first.Due);
            check(same, $"\"{text}\" reads back from its editing text \"{natural}\"{(again.Ok ? "" : ": " + again.Error)}");
        }
    }

    // ---------- Repeat rules ----------

    private static void Schedule()
    {
        var weekdays = new ReminderRepeat { Kind = RepeatKind.Weekdays, Time = new TimeSpan(7, 0, 0) };
        check(ReminderSchedule.Next(weekdays, At(utc, 2026, 10, 9, 7), utc) == At(utc, 2026, 10, 12, 7), "weekdays: Friday goes to Monday");
        check(ReminderSchedule.Next(weekdays, At(utc, 2026, 10, 6, 7), utc) == At(utc, 2026, 10, 7, 7), "weekdays: Tuesday goes to Wednesday");
        var weekly = new ReminderRepeat { Kind = RepeatKind.Weekly, Days = [DayOfWeek.Monday, DayOfWeek.Wednesday], Time = new TimeSpan(18, 0, 0) };
        check(ReminderSchedule.Next(weekly, At(utc, 2026, 10, 7, 18), utc) == At(utc, 2026, 10, 12, 18), "weekly Mon+Wed: Wednesday goes to the next Monday");
        check(ReminderSchedule.Next(weekly, At(utc, 2026, 10, 5, 18), utc) == At(utc, 2026, 10, 7, 18), "weekly Mon+Wed: Monday goes to Wednesday");
        var lonely = new ReminderRepeat { Kind = RepeatKind.Weekly, Days = [DayOfWeek.Sunday], Time = new TimeSpan(9, 0, 0) };
        check(ReminderSchedule.Next(lonely, At(utc, 2026, 10, 4, 9), utc) == At(utc, 2026, 10, 11, 9), "weekly on one day is seven days later");

        // Monthly on the 31st: short months use their last day, and the 31st comes back.
        var monthly = new ReminderRepeat { Kind = RepeatKind.Monthly, DayOfMonth = 31, Time = new TimeSpan(9, 0, 0) };
        var d = At(utc, 2026, 1, 31, 9);
        var seen = new List<(int, int)>();
        for (int i = 0; i < 6; i++) { d = ReminderSchedule.Next(monthly, d, utc)!.Value; seen.Add((d.Month, d.Day)); }
        check(seen.SequenceEqual([(2, 28), (3, 31), (4, 30), (5, 31), (6, 30), (7, 31)]), "monthly on the 31st: Feb 28, Mar 31, Apr 30, May 31, Jun 30, Jul 31 (got " + string.Join(" ", seen) + ")");
        check(ReminderSchedule.Next(monthly, At(utc, 2028, 1, 31, 9), utc) == At(utc, 2028, 2, 29, 9), "monthly on the 31st in a leap year: Feb 29");
        check(ReminderSchedule.Next(monthly, At(utc, 2026, 12, 31, 9), utc) == At(utc, 2027, 1, 31, 9), "monthly rolls over the year");
        var daily = new ReminderRepeat { Kind = RepeatKind.Daily, Time = new TimeSpan(9, 0, 0) };
        check(ReminderSchedule.Next(daily, At(utc, 2026, 12, 31, 9), utc) == At(utc, 2027, 1, 1, 9), "daily rolls over the year");
        var every = new ReminderRepeat { Kind = RepeatKind.Interval, Unit = IntervalUnit.Hours, Every = 2 };
        check(ReminderSchedule.Next(every, At(utc, 2026, 10, 6, 23), utc) == At(utc, 2026, 10, 7, 1), "every 2 hours crosses midnight");
        var days3 = new ReminderRepeat { Kind = RepeatKind.Interval, Unit = IntervalUnit.Days, Every = 3, Time = new TimeSpan(8, 0, 0) };
        check(ReminderSchedule.Next(days3, At(utc, 2026, 10, 30, 8), utc) == At(utc, 2026, 11, 2, 8), "every 3 days");
        check(ReminderSchedule.Next(new ReminderRepeat(), At(utc, 2026, 10, 6, 8), utc) is null, "a one-off has no next time");
        // First occurrence.
        check(ReminderSchedule.First(weekly, new TimeSpan(18, 0, 0), At(utc, 2026, 10, 5, 18), utc) == At(utc, 2026, 10, 7, 18), "first(): on Monday at the time itself, the next ring is Wednesday");
        check(ReminderSchedule.First(weekly, new TimeSpan(18, 0, 0), At(utc, 2026, 10, 5, 12), utc) == At(utc, 2026, 10, 5, 18), "first(): on Monday before the time, it is today");
        check(ReminderSchedule.First(monthly, new TimeSpan(9, 0, 0), At(utc, 2026, 2, 1, 0), utc) == At(utc, 2026, 2, 28, 9), "first(): the 31st in February is the 28th");
    }

    private static void DaylightSaving()
    {
        var z = pacific;
        // Spring forward 2026-03-08: 02:00 -> 03:00. A 02:30 reminder rings at 03:00 that day and is back at 02:30 the next.
        var daily = new ReminderRepeat { Kind = RepeatKind.Daily, Time = new TimeSpan(2, 30, 0) };
        var before = At(z, 2026, 3, 7, 2, 30);
        var skipped = ReminderSchedule.Next(daily, before, z)!.Value;
        check(skipped.Hour == 3 && skipped.Minute == 0 && skipped.Day == 8 && skipped.Offset == TimeSpan.FromHours(-7), "a time the clocks skip rings at the first moment after the gap (3:00 PDT)");
        var after = ReminderSchedule.Next(daily, skipped, z)!.Value;
        check(after.Hour == 2 && after.Minute == 30 && after.Day == 9 && after.Offset == TimeSpan.FromHours(-7), "...and returns to 2:30 the next day (no drift)");
        // A 9:00 reminder keeps its wall-clock time across the change (23 hours elapse).
        var nine = new ReminderRepeat { Kind = RepeatKind.Daily, Time = new TimeSpan(9, 0, 0) };
        var d1 = At(z, 2026, 3, 7, 9);
        var d2 = ReminderSchedule.Next(nine, d1, z)!.Value;
        check(d2.Hour == 9 && d2 - d1 == TimeSpan.FromHours(23), "daily 9:00 across spring forward stays at 9:00 (23 hours later)");
        var n1 = At(z, 2026, 10, 31, 9);
        var n2 = ReminderSchedule.Next(nine, n1, z)!.Value;
        check(n2.Hour == 9 && n2 - n1 == TimeSpan.FromHours(25), "daily 9:00 across fall back stays at 9:00 (25 hours later)");
        // Every 2 hours counts real time.
        var every = new ReminderRepeat { Kind = RepeatKind.Interval, Unit = IntervalUnit.Hours, Every = 2 };
        var e1 = At(z, 2026, 3, 8, 0);
        var e2 = ReminderSchedule.Next(every, e1, z)!.Value;
        check(e2 - e1 == TimeSpan.FromHours(2) && e2.Hour == 3, "every 2 hours from 00:00 on the spring forward day is 03:00 (two real hours)");
        // Fall back 2026-11-01: 02:00 -> 01:00, so 01:30 happens twice. A 01:30 reminder rings once, the first time.
        var ambiguous = new ReminderRepeat { Kind = RepeatKind.Daily, Time = new TimeSpan(1, 30, 0) };
        var oct31 = At(z, 2026, 10, 31, 1, 30);
        var nov1 = ReminderSchedule.Next(ambiguous, oct31, z)!.Value;
        check(nov1.Offset == TimeSpan.FromHours(-7) && nov1.Hour == 1 && nov1.Minute == 30, "01:30 on the repeated hour means the first one (PDT)");
        var settings = new ReminderSettings();
        var service = new ReminderService(settings, z);
        service.Add(new Reminder { Text = "Late snack", Due = nov1, Repeat = ambiguous }, nov1.AddDays(-5));
        var rings1 = service.Tick(nov1);
        var rings2 = service.Tick(nov1.AddHours(1)); // 01:30 PST, the second 01:30
        var rings3 = service.Tick(nov1.AddHours(2));
        check(rings1.Count == 1 && rings2.Count == 0 && rings3.Count == 0, "fall back: the repeated 01:30 does not ring twice");
        var next = settings.Items[0].Due;
        check(next.Day == 2 && next.Hour == 1 && next.Minute == 30 && next.Offset == TimeSpan.FromHours(-8), "...and the next ring is 01:30 PST the next day");
        // Parser in the zone.
        var now = At(z, 2026, 3, 8, 1, 30);
        var parsed = ReminderParser.Parse("at 2:30am check the oven", now, z);
        check(parsed.Ok && parsed.Value!.Due.Hour == 3 && parsed.Value.Due.Offset == TimeSpan.FromHours(-7), "the parser maps a skipped time to 3:00");
        var inTwo = ReminderParser.Parse("in 2 hours stretch", now, z);
        check(inTwo.Ok && inTwo.Value!.Due - now == TimeSpan.FromHours(2) && inTwo.Value.Due.Hour == 4, "\"in 2 hours\" across spring forward is two real hours");
    }

    // ---------- The service ----------

    private static ReminderService Service(ReminderSettings? settings = null, TimeZoneInfo? zone = null) => new(settings ?? new ReminderSettings(), zone ?? utc);

    private static Reminder OneOff(ReminderService s, string text, DateTimeOffset due) => s.Add(new Reminder { Text = text, Due = due }, due.AddHours(-1));

    private static void Ringing()
    {
        var s = Service();
        var due = At(utc, 2026, 10, 6, 17);
        var r = OneOff(s, "Call mom", due);
        check(s.NextWake() == due, "the next wake-up is the soonest reminder");
        check(s.Tick(due.AddSeconds(-1)).Count == 0, "nothing rings before its time");
        var rings = s.Tick(due.AddSeconds(20));
        check(rings.Count == 1 && rings[0].Kind == RingKind.Rang && rings[0].Reminder == r && rings[0].Occurrence == due, "a reminder rings at its time");
        check(r.Attention == ReminderAttention.Rang && r.LastFired == due && !r.Done, "a one-off that rang waits for you");
        check(s.Tick(due.AddMinutes(1)).Count == 0 && s.Tick(due.AddHours(5)).Count == 0, "it rings exactly once");
        check(s.NextWake() is null, "nothing is waiting");
        var second = OneOff(s, "Later", due.AddHours(3));
        OneOff(s, "Sooner", due.AddHours(2));
        check(s.NextWake() == due.AddHours(2), "the soonest of several");
        var order = s.Tick(due.AddHours(3).AddSeconds(1)).Select(x => x.Reminder.Text).ToList();
        check(order.SequenceEqual(["Later", "Sooner"]) || order.SequenceEqual(["Sooner", "Later"]), "two reminders due together both ring once");
        check(s.Tick(due.AddHours(3).AddSeconds(2)).Count == 0 && second.Attention == ReminderAttention.Rang, "...and not again");

        var daily = s.Add(new Reminder { Text = "Vitamins", Due = At(utc, 2026, 10, 7, 9), Repeat = new ReminderRepeat { Kind = RepeatKind.Daily, Time = new TimeSpan(9, 0, 0) } }, due);
        var ring = s.Tick(At(utc, 2026, 10, 7, 9, 0)).Single(x => x.Reminder == daily);
        check(ring.Kind == RingKind.Rang && ring.Skipped == 0 && daily.Due == At(utc, 2026, 10, 8, 9), "a repeating reminder rings and moves to its next time");
        check(s.Tick(At(utc, 2026, 10, 7, 9, 0)).All(x => x.Reminder != daily) && s.Tick(At(utc, 2026, 10, 7, 9, 1)).All(x => x.Reminder != daily), "...and the same occurrence cannot ring twice");
        s.Complete(daily.Id, At(utc, 2026, 10, 7, 9, 5));
        check(daily.Attention == ReminderAttention.None && daily.Due == At(utc, 2026, 10, 8, 9) && !daily.Done, "done on a repeating reminder that rang just answers it");
        s.Complete(daily.Id, At(utc, 2026, 10, 7, 9, 6));
        check(daily.Due == At(utc, 2026, 10, 9, 9), "done on an upcoming repeating reminder skips that occurrence");
    }

    private static void CatchUp()
    {
        var s = Service();
        var daily = s.Add(new Reminder { Text = "Vitamins", Due = At(utc, 2026, 10, 1, 9), Repeat = new ReminderRepeat { Kind = RepeatKind.Daily, Time = new TimeSpan(9, 0, 0) } }, At(utc, 2026, 9, 1));
        var wake = At(utc, 2026, 10, 4, 10); // the computer slept for three days
        var rings = s.Tick(wake);
        check(rings.Count == 1 && rings[0].Kind == RingKind.Missed && rings[0].Occurrence == At(utc, 2026, 10, 4, 9) && rings[0].Skipped == 3, "after sleep a repeating reminder shows once as missed (the latest), with the others counted");
        check(daily.Due == At(utc, 2026, 10, 5, 9) && daily.Attention == ReminderAttention.Missed, "...and moves to the next time that is still ahead");
        check(s.Tick(wake.AddSeconds(5)).Count == 0, "...and does not show again");

        var s2 = Service();
        var one = OneOff(s2, "Dentist", At(utc, 2026, 10, 5, 9));
        var r = s2.Tick(At(utc, 2026, 10, 6, 8)).Single();
        check(r.Kind == RingKind.Missed && one.Attention == ReminderAttention.Missed && s2.NextWake() is null, "a one-off whose time passed while the app was closed is missed, once");
        var s3 = Service();
        OneOff(s3, "Tea", At(utc, 2026, 10, 5, 9));
        check(s3.Tick(At(utc, 2026, 10, 5, 9, 1)).Single().Kind == RingKind.Rang, "a minute late is still on time");
        OneOff(s3, "Tea 2", At(utc, 2026, 10, 5, 12));
        check(s3.Tick(At(utc, 2026, 10, 5, 12, 3)).Single().Kind == RingKind.Missed, "three minutes late is missed");

        var s4 = Service();
        var water = s4.Add(new Reminder { Text = "Water", Due = At(utc, 2026, 10, 6, 8), Repeat = new ReminderRepeat { Kind = RepeatKind.Interval, Unit = IntervalUnit.Minutes, Every = 30 } }, At(utc, 2026, 10, 6, 7));
        var water2 = s4.Tick(At(utc, 2026, 10, 6, 16, 10)).Single();
        check(water2.Kind == RingKind.Missed && water2.Skipped == 16 && water2.Occurrence == At(utc, 2026, 10, 6, 16) && water.Due == At(utc, 2026, 10, 6, 16, 30), "every 30 minutes over eight hours asleep: one missed ring, 16 skipped, next at 16:30");
    }

    private static void SnoozeAndDone()
    {
        var s = Service();
        var due = At(utc, 2026, 10, 6, 17);
        var r = OneOff(s, "Call mom", due);
        s.Tick(due);
        check(s.Snooze(r.Id, SnoozeChoice.Minutes10, due.AddSeconds(30)) && r.Attention == ReminderAttention.None && r.SnoozedUntil == due.AddMinutes(10).AddSeconds(30), "snooze 10 minutes");
        check(s.NextWake() == r.SnoozedUntil, "a snoozed reminder wakes the timer at the snooze time");
        check(s.Tick(due.AddMinutes(10)).Count == 0, "it is quiet until the snooze ends");
        var again = s.Tick(due.AddMinutes(10).AddSeconds(31));
        check(again.Count == 1 && again[0].Kind == RingKind.Rang && r.SnoozedUntil is null, "it rings again when the snooze ends");
        foreach (var (choice, minutes) in new[] { (SnoozeChoice.Minutes5, 5), (SnoozeChoice.Minutes30, 30), (SnoozeChoice.Minutes60, 60) })
            check(s.SnoozeTime(choice, due) == due.AddMinutes(minutes), $"snooze {minutes} minutes");
        var settings = new ReminderSettings { TomorrowAt = "08:15" };
        var s2 = Service(settings);
        check(s2.SnoozeTime(SnoozeChoice.Tomorrow, At(utc, 2026, 10, 6, 23, 50)) == At(utc, 2026, 10, 7, 8, 15), "snooze until tomorrow is tomorrow at the set time (even just before midnight)");
        check(s2.Snooze("nope", SnoozeChoice.Minutes5, due) == false && s2.Complete("nope", due) == false, "unknown ids are ignored");

        // Snooze must not swallow the next regular occurrence of a repeating reminder.
        var s3 = Service();
        var daily = s3.Add(new Reminder { Text = "Pills", Due = At(utc, 2026, 10, 6, 9), Repeat = new ReminderRepeat { Kind = RepeatKind.Daily, Time = new TimeSpan(9, 0, 0) } }, At(utc, 2026, 10, 1));
        s3.Tick(At(utc, 2026, 10, 6, 9));
        s3.Snooze(daily.Id, SnoozeChoice.Minutes30, At(utc, 2026, 10, 6, 9, 1));
        check(daily.Due == At(utc, 2026, 10, 7, 9) && s3.NextWake() == At(utc, 2026, 10, 6, 9, 31), "snoozing a repeating reminder keeps tomorrow's time");
        check(s3.Tick(At(utc, 2026, 10, 6, 9, 31)).Single().Kind == RingKind.Rang && daily.Due == At(utc, 2026, 10, 7, 9), "the snooze rings, tomorrow is untouched");
        check(s3.Tick(At(utc, 2026, 10, 7, 9)).Single().Reminder == daily && daily.Due == At(utc, 2026, 10, 8, 9), "...and tomorrow still rings");
        // A snooze and the regular time both due at once is one ring.
        s3.Snooze(daily.Id, SnoozeChoice.Minutes60, At(utc, 2026, 10, 7, 9, 5));
        var both = s3.Tick(At(utc, 2026, 10, 8, 9, 0));
        check(both.Count == 1 && daily.SnoozedUntil is null && daily.Due == At(utc, 2026, 10, 9, 9), "a snooze and a regular time due together ring once");

        // Done.
        var s4 = Service();
        var task = OneOff(s4, "Homework", due);
        s4.Tick(due);
        check(s4.Complete(task.Id, due.AddMinutes(2)) && task.Done && task.DoneAt == due.AddMinutes(2) && task.Attention == ReminderAttention.None && s4.NextWake() is null, "done finishes a one-off");
        check(s4.Snooze(task.Id, SnoozeChoice.Minutes5, due) == false, "a finished reminder cannot be snoozed");
        var edited = s4.Update(task.Id, "Homework (math)", "pages 4-5", due.AddDays(1), new ReminderRepeat());
        check(edited && !task.Done && task.Text == "Homework (math)" && task.Note == "pages 4-5" && s4.NextWake() == due.AddDays(1), "editing brings it back with the new time");
        check(s4.Remove(task.Id) && s4.Items.Count == 0 && !s4.Remove(task.Id), "delete");
        // Occurrences for the agenda.
        var s5 = Service();
        s5.Add(new Reminder { Text = "Trash", Due = At(utc, 2026, 10, 12, 7, 30), Repeat = new ReminderRepeat { Kind = RepeatKind.Weekly, Days = [DayOfWeek.Monday], Time = new TimeSpan(7, 30, 0) } }, due);
        s5.Add(new Reminder { Text = "Dentist", Due = At(utc, 2026, 10, 14, 9) }, due);
        var list = s5.Occurrences(At(utc, 2026, 10, 6), At(utc, 2026, 10, 27));
        check(list.Select(x => x.Reminder.Text + x.At.Day).SequenceEqual(["Trash12", "Dentist14", "Trash19", "Trash26"]), "occurrences list repeats one by one, in time order: " + string.Join(",", list.Select(x => x.Reminder.Text + x.At.Day)));
    }

    private static void Quiet()
    {
        var s = new ReminderSettings { QuietHours = true, QuietFrom = "22:00", QuietTo = "07:00" };
        check(ReminderService.InQuietHours(s, new TimeSpan(23, 0, 0)) && ReminderService.InQuietHours(s, new TimeSpan(3, 0, 0)) && ReminderService.InQuietHours(s, new TimeSpan(22, 0, 0)) && ReminderService.InQuietHours(s, new TimeSpan(6, 59, 0)), "quiet hours that cross midnight cover the late evening and the early morning");
        check(!ReminderService.InQuietHours(s, new TimeSpan(7, 0, 0)) && !ReminderService.InQuietHours(s, new TimeSpan(21, 59, 0)) && !ReminderService.InQuietHours(s, new TimeSpan(12, 0, 0)), "...and end exactly at the end time");
        var day = new ReminderSettings { QuietHours = true, QuietFrom = "13:00", QuietTo = "14:30" };
        check(ReminderService.InQuietHours(day, new TimeSpan(13, 30, 0)) && !ReminderService.InQuietHours(day, new TimeSpan(14, 30, 0)) && !ReminderService.InQuietHours(day, new TimeSpan(2, 0, 0)), "quiet hours within one day");
        check(!ReminderService.InQuietHours(new ReminderSettings { QuietHours = false }, new TimeSpan(23, 0, 0)), "quiet hours off");
        check(!ReminderService.InQuietHours(new ReminderSettings { QuietHours = true, QuietFrom = "08:00", QuietTo = "08:00" }, new TimeSpan(8, 0, 0)), "quiet hours of zero length are off");
        var svc = new ReminderService(s, utc);
        check(svc.IsQuiet(At(utc, 2026, 10, 6, 23, 15)) && !svc.IsQuiet(At(utc, 2026, 10, 6, 15)), "IsQuiet follows the clock");
        check(svc.QuietEndsAt(At(utc, 2026, 10, 6, 23, 15)) == At(utc, 2026, 10, 7, 7) && svc.QuietEndsAt(At(utc, 2026, 10, 7, 3)) == At(utc, 2026, 10, 7, 7) && svc.QuietEndsAt(At(utc, 2026, 10, 7, 15)) is null, "quiet hours end at the end time, tonight or this morning");
        s.DoNotDisturb = true;
        check(svc.IsQuiet(At(utc, 2026, 10, 6, 15)) && svc.QuietEndsAt(At(utc, 2026, 10, 6, 23)) is null, "do not disturb is quiet at any time, with no end");
        var r = svc.Add(new Reminder { Text = "x", Due = At(utc, 2026, 10, 6, 23) }, At(utc, 2026, 10, 6, 20));
        svc.Tick(At(utc, 2026, 10, 6, 23));
        svc.MarkMissed(r.Id);
        check(r.Attention == ReminderAttention.Missed, "a ring held back by quiet hours shows as missed");
    }

    // ---------- Settings, saving, lists ----------

    private static void Settings(string folder)
    {
        var settings = new DeckSettings();
        var rem = settings.Reminders;
        rem.Items.Add(new Reminder { Id = "a", Text = "  Trash  ", Due = At(utc, 2026, 10, 12, 7, 30), Repeat = new ReminderRepeat { Kind = RepeatKind.Weekly, Days = [DayOfWeek.Wednesday, DayOfWeek.Monday, DayOfWeek.Monday], Time = new TimeSpan(7, 30, 0) }, SnoozedUntil = At(utc, 2026, 10, 12, 8) });
        rem.Items.Add(new Reminder { Id = "a", Text = "Duplicate id", Due = At(utc, 2026, 10, 7, 7), Repeat = new ReminderRepeat { Kind = RepeatKind.Monthly, DayOfMonth = 99, Every = -4 } });
        rem.Items.Add(new Reminder { Id = "", Text = "   ", Due = At(utc, 2026, 10, 7, 7) });
        rem.Items.Add(null!);
        rem.Items.Add(new Reminder { Id = "c", Text = "Weekly with no days", Due = At(utc, 2026, 10, 7, 7), Repeat = new ReminderRepeat { Kind = RepeatKind.Weekly } });
        for (int i = 0; i < 70; i++) rem.Items.Add(new Reminder { Id = "d" + i, Text = "Done " + i, Due = At(utc, 2026, 9, 1), Done = true, DoneAt = At(utc, 2026, 9, 1).AddMinutes(i) });
        rem.ToastSeconds = 999;
        rem.QuietFrom = "nonsense";
        rem.Color = "pink";
        var store = new SettingsStore(Path.Combine(folder, "reminders"));
        store.Save(settings);
        var loaded = store.Load().Reminders;
        var first = loaded.Items.First(r => r.Text == "Trash");
        check(loaded.Items.Count(r => !r.Done) == 3, "blank and null reminders are dropped, the rest stay");
        check(first.Repeat.Days.SequenceEqual([DayOfWeek.Monday, DayOfWeek.Wednesday]) && first.Repeat.Time == new TimeSpan(7, 30, 0) && first.Due == At(utc, 2026, 10, 12, 7, 30) && first.SnoozedUntil == At(utc, 2026, 10, 12, 8), "a reminder survives saving: weekdays sorted and distinct, times exact");
        check(loaded.Items.Select(r => r.Id).Distinct().Count() == loaded.Items.Count, "duplicate ids are repaired");
        var monthly = loaded.Items.First(r => r.Text == "Duplicate id");
        check(monthly.Repeat.DayOfMonth == 31 && monthly.Repeat.Every == 1, "out-of-range repeat numbers are clamped");
        check(loaded.Items.First(r => r.Text == "Weekly with no days").Repeat.Days.Count == 1, "a weekly reminder with no days gets one");
        check(loaded.Items.Count(r => r.Done) == ReminderSettings.MaxDone && loaded.Items.Any(r => r.Text == "Done 69") && loaded.Items.All(r => r.Text != "Done 0"), "only the newest finished reminders are kept");
        check(loaded.ToastSeconds == 60 && loaded.QuietFrom == "22:00" && loaded.Color == "#ec4899", "toast seconds, quiet hours text and color are repaired");
        var json = File.ReadAllText(store.FilePath);
        check(json.Contains("\"Weekly\"") && json.Contains("\"Kind\""), "repeat kinds are saved as words");
        var older = new SettingsStore(Path.Combine(folder, "reminders-old"));
        Directory.CreateDirectory(Path.GetDirectoryName(older.FilePath)!);
        File.WriteAllText(older.FilePath, """{ "SchemaVersion": 5, "Items": [] }""");
        check(older.Load().Reminders.Items.Count == 0 && older.Load().Reminders.Toasts, "settings from before reminders load with defaults");
        File.WriteAllText(older.FilePath, """{ "SchemaVersion": 5, "Items": [], "Reminders": null }""");
        check(older.Load().Reminders is not null, "a null reminders section loads as defaults");
        check(ReminderSettings.ParseTime("7:30", TimeSpan.Zero) == new TimeSpan(7, 30, 0) && ReminderSettings.ParseTime("7:30 pm", TimeSpan.Zero) == new TimeSpan(19, 30, 0) && ReminderSettings.ParseTime("zzz", TimeSpan.FromHours(3)) == TimeSpan.FromHours(3), "times of day in settings");
    }

    private static void Groups()
    {
        var now = Now(utc);
        var items = ReminderSamples.Make(now, utc);
        var g = ReminderGroups.Build(items, now, utc);
        check(g.Missed.Select(r => r.Text).SequenceEqual(["Submit lab report"]), "missed: the one that was missed");
        check(g.Today.Select(r => r.Text).SequenceEqual(["Stretch", "Call Mom", "Finish chemistry homework"]), "today, by time: " + string.Join(", ", g.Today.Select(r => r.Text)));
        check(g.Tomorrow.Select(r => r.Text).SequenceEqual(["Soccer practice bag"]), "tomorrow");
        check(g.Later.Select(r => r.Text).SequenceEqual(["Dentist"]), "later");
        check(g.Repeating.Count == 4 && g.Repeating[0].Text == "Drink water", "repeating, soonest first: " + string.Join(", ", g.Repeating.Select(r => r.Text)));
        check(g.Done.Select(r => r.Text).SequenceEqual(["Return library books"]) && !g.IsEmpty && g.Upcoming == 9, "done list and counts");
        var snoozed = items.First(r => r.Text == "Dentist");
        snoozed.SnoozedUntil = now.AddMinutes(10);
        check(ReminderGroups.Build(items, now, utc).Today.Any(r => r.Text == "Dentist"), "a snoozed reminder is listed under the day it rings");
        check(ReminderGroups.Build([], now, utc).IsEmpty, "no reminders");
        check(ReminderFormat.Relative(now.AddMinutes(20), now) == "in 20 min" && ReminderFormat.Relative(now.AddHours(-3), now) == "3 h ago" && ReminderFormat.Relative(now.AddDays(3), now) == "in 3 days" && ReminderFormat.Relative(now.AddSeconds(10), now) == "now", "relative wording");
        check(ReminderFormat.RepeatText(new ReminderRepeat { Kind = RepeatKind.Weekly, Days = [DayOfWeek.Friday, DayOfWeek.Monday, DayOfWeek.Wednesday] }) == "Every Mon, Wed and Fri" && ReminderFormat.RepeatText(new ReminderRepeat { Kind = RepeatKind.Monthly, DayOfMonth = 22 }) == "Every month on the 22nd" && ReminderFormat.RepeatText(new ReminderRepeat { Kind = RepeatKind.Interval, Unit = IntervalUnit.Minutes, Every = 90 }) == "Every 90 minutes", "repeat wording");
        check(ReminderFormat.When(now.AddDays(10), now, utc, false) == "Fri, Oct 16 3:00 PM" && ReminderFormat.When(now.AddDays(400), now, utc, true) == "Nov 10, 2027 15:00" && ReminderFormat.When(now.AddDays(3), now, utc, false) == "Fri 3:00 PM", "date wording");
    }
}
