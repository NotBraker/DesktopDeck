using System;

namespace DesktopDeck.Widgets;

/// <summary>How the break reminder is configured.</summary>
public sealed record BreakReminderSettings(int IntervalMinutes, int BreakSeconds, int StretchEvery, int StretchSeconds, bool ActiveOnly, TimeSpan From, TimeSpan To, bool WeekdaysOnly);

/// <summary>
/// Everything the reminder remembers. Exactly one of NextDue (counting down to a break), BreakEnd (a break is on)
/// or PausedLeftSeconds (paused) is set once the state has been started.
/// </summary>
public sealed record BreakReminderState(DateTime? NextDue, DateTime? BreakEnd, bool BreakIsStretch, double? PausedLeftSeconds, string Day, int Taken);

public enum BreakReminderEvent { None, BreakStarted, BreakEnded, Missed }

/// <summary>
/// The 20-20-20 break schedule as a pure state machine: <see cref="Step"/> moves time forward, the other methods are
/// the buttons. Nothing here reads a clock or settings; the widget passes both in.
/// </summary>
public static class BreakReminderLogic
{
    /// <summary>A reminder this late (the PC slept, the deck was closed) is dropped and rescheduled rather than shown.</summary>
    public static readonly TimeSpan MissGrace = TimeSpan.FromMinutes(5);

    public static BreakReminderState Initial(DateTime now) => new(null, null, false, null, HealthLogic.DayKey(now), 0);

    private static TimeSpan Interval(BreakReminderSettings cfg) => TimeSpan.FromMinutes(Math.Max(1, cfg.IntervalMinutes));

    public static DateTime Schedule(DateTime from, BreakReminderSettings cfg)
        => HealthLogic.ScheduleAfter(from, Interval(cfg), cfg.ActiveOnly, cfg.From, cfg.To, cfg.WeekdaysOnly);

    /// <summary>Whether the next break (the Taken+1-th today) is a longer stretch/stand break.</summary>
    public static bool IsStretchNext(int taken, BreakReminderSettings cfg) => cfg.StretchEvery > 0 && (taken + 1) % cfg.StretchEvery == 0;

    public static int BreakLengthSeconds(bool stretch, BreakReminderSettings cfg) => Math.Max(1, stretch ? cfg.StretchSeconds : cfg.BreakSeconds);

    /// <summary>Moves the state forward to <paramref name="now"/>. Fires at most one event per call, however long the gap.</summary>
    public static BreakReminderState Step(BreakReminderState state, DateTime now, BreakReminderSettings cfg, out BreakReminderEvent ev)
    {
        ev = BreakReminderEvent.None;
        string today = HealthLogic.DayKey(now);
        if (state.Day != today) state = state with { Day = today, Taken = 0 };
        if (state.PausedLeftSeconds is not null) return state;

        if (state.BreakEnd is { } end)
        {
            if (now < end) return state;
            // A break that ended long ago (nobody was looking) is not counted.
            bool counted = now - end <= MissGrace;
            ev = counted ? BreakReminderEvent.BreakEnded : BreakReminderEvent.Missed;
            return state with { BreakEnd = null, NextDue = Schedule(now, cfg), Taken = state.Taken + (counted ? 1 : 0) };
        }

        if (state.NextDue is not { } due) return state with { NextDue = Schedule(now, cfg) };
        if (now < due) return state;

        if (now - due > MissGrace || cfg.ActiveOnly && !HealthLogic.IsActive(now, cfg.From, cfg.To, cfg.WeekdaysOnly))
        {
            ev = BreakReminderEvent.Missed;
            return state with { NextDue = Schedule(now, cfg) };
        }
        return StartBreak(state, now, cfg, out ev);
    }

    private static BreakReminderState StartBreak(BreakReminderState state, DateTime now, BreakReminderSettings cfg, out BreakReminderEvent ev)
    {
        bool stretch = IsStretchNext(state.Taken, cfg);
        ev = BreakReminderEvent.BreakStarted;
        return state with { NextDue = null, PausedLeftSeconds = null, BreakEnd = now.AddSeconds(BreakLengthSeconds(stretch, cfg)), BreakIsStretch = stretch };
    }

    // ---------- Buttons ----------

    /// <summary>Starts a break right now.</summary>
    public static BreakReminderState BreakNow(BreakReminderState state, DateTime now, BreakReminderSettings cfg) => StartBreak(state, now, cfg, out _);

    /// <summary>Pauses, remembering how long was left; a break in progress is cancelled.</summary>
    public static BreakReminderState Pause(BreakReminderState state, DateTime now, BreakReminderSettings cfg)
    {
        if (state.PausedLeftSeconds is not null) return state;
        double left = state.NextDue is { } due && state.BreakEnd is null ? Math.Max(0, (due - now).TotalSeconds) : Interval(cfg).TotalSeconds;
        return state with { NextDue = null, BreakEnd = null, PausedLeftSeconds = left };
    }

    /// <summary>Resumes with the time that was left (moved into active hours if need be).</summary>
    public static BreakReminderState Resume(BreakReminderState state, DateTime now, BreakReminderSettings cfg)
    {
        if (state.PausedLeftSeconds is not { } left) return state;
        var due = HealthLogic.CeilMinute(now.AddSeconds(left));
        if (cfg.ActiveOnly && !HealthLogic.IsActive(due, cfg.From, cfg.To, cfg.WeekdaysOnly)) due = Schedule(now, cfg);
        return state with { NextDue = due, PausedLeftSeconds = null };
    }

    /// <summary>Pushes the next break back by <paramref name="minutes"/> from now.</summary>
    public static BreakReminderState Snooze(BreakReminderState state, DateTime now, int minutes)
        => state with { NextDue = HealthLogic.CeilMinute(now.AddMinutes(Math.Max(1, minutes))), BreakEnd = null, PausedLeftSeconds = null };

    /// <summary>Gives up the current break (not counted) and starts the countdown again.</summary>
    public static BreakReminderState Skip(BreakReminderState state, DateTime now, BreakReminderSettings cfg)
        => state with { NextDue = Schedule(now, cfg), BreakEnd = null, PausedLeftSeconds = null };

    /// <summary>"Done" during a break: counts it and starts the countdown again.</summary>
    public static BreakReminderState Finish(BreakReminderState state, DateTime now, BreakReminderSettings cfg)
        => state.BreakEnd is null ? state : state with { NextDue = Schedule(now, cfg), BreakEnd = null, Taken = state.Taken + 1 };

    // ---------- Display ----------

    public enum Phase { Waiting, Break, Paused }

    public static Phase PhaseOf(BreakReminderState state) => state.PausedLeftSeconds is not null ? Phase.Paused : state.BreakEnd is not null ? Phase.Break : Phase.Waiting;

    /// <summary>The time on the dial: until the next break, until the current one ends, or what was left when paused.</summary>
    public static TimeSpan Remaining(BreakReminderState state, DateTime now)
        => state.PausedLeftSeconds is { } left ? TimeSpan.FromSeconds(left)
         : state.BreakEnd is { } end ? end - now
         : state.NextDue is { } due ? due - now : TimeSpan.Zero;

    /// <summary>How far through the current phase we are (0–1), for the ring.</summary>
    public static double Progress(BreakReminderState state, DateTime now, BreakReminderSettings cfg)
    {
        double total, left = Remaining(state, now).TotalSeconds;
        total = PhaseOf(state) == Phase.Break ? BreakLengthSeconds(state.BreakIsStretch, cfg) : Interval(cfg).TotalSeconds;
        return total <= 0 ? 0 : Math.Clamp(1 - left / total, 0, 1);
    }

    /// <summary>The ticker should run every second only while a break countdown is on screen.</summary>
    public static bool NeedsSeconds(BreakReminderState state) => state.BreakEnd is not null;

    // ---------- Saving ----------

    public static string Describe(bool stretch, string eyeMessage, string stretchMessage) => stretch ? stretchMessage : eyeMessage;
}
