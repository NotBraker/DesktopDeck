using System;
using System.Globalization;

namespace DesktopDeck.Widgets;

/// <summary>Battery state text and estimates for the Battery widget. Pure logic (unit tested).</summary>
public static class BatteryLogic
{
    /// <summary>
    /// One power reading. Percent is 0–100 (or -1 if unknown); SecondsLeft is -1 when Windows can't estimate it.
    /// TimeToFull, Health (0–1) and RateWatts (positive = charging) come from the battery report when available.
    /// </summary>
    public sealed record Reading(bool HasBattery, bool PluggedIn, bool Charging, int Percent, int SecondsLeft, bool Saver, string Plan,
        double? TimeToFull = null, double? Health = null, double? RateWatts = null);

    /// <summary>"Charging", "Fully charged", "Plugged in", "On battery" or "AC power" (no battery).</summary>
    public static string State(Reading r)
    {
        if (!r.HasBattery) return "AC power";
        if (r.Charging) return "Charging";
        if (r.PluggedIn) return r.Percent >= 95 ? "Fully charged" : "Plugged in";
        return "On battery";
    }

    /// <summary>"3 h 12 m", "45 m", "" when unknown.</summary>
    public static string Duration(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) return "";
        var span = TimeSpan.FromSeconds(seconds);
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours} h {span.Minutes} m";
        return $"{Math.Max(1, (int)Math.Round(span.TotalMinutes))} m";
    }

    /// <summary>The time line: "2 h 14 m left", "1 h 5 m until full", or "" when there's nothing to say.</summary>
    public static string TimeText(Reading r)
    {
        if (!r.HasBattery) return "";
        if (r.Charging) return r.TimeToFull is { } full && full > 0 ? Duration(full) + " until full" : "";
        if (r.PluggedIn) return "";
        return r.SecondsLeft >= 0 ? Duration(r.SecondsLeft) + " left" : "Estimating…";
    }

    /// <summary>Seconds until full from the battery report (null when it isn't charging or values are missing).</summary>
    public static double? TimeToFull(int? remainingMWh, int? fullMWh, int? rateMW)
    {
        if (remainingMWh is not { } remaining || fullMWh is not { } full || rateMW is not { } rate || rate <= 0 || full <= 0) return null;
        double missing = Math.Max(0, full - remaining);
        return missing / rate * 3600;
    }

    /// <summary>Full-charge capacity as a fraction of the design capacity (null when unknown).</summary>
    public static double? Health(int? fullMWh, int? designMWh)
        => fullMWh is { } full && designMWh is { } design && design > 0 && full > 0 ? Math.Clamp((double)full / design, 0, 1.2) : null;

    /// <summary>0 = fine, 1 = low, 2 = critical (lower charge is worse). Never low while charging.</summary>
    public static int Level(int percent, double low, double critical, bool charging)
    {
        if (charging || percent < 0) return 0;
        if (critical > low) (low, critical) = (critical, low);
        return percent <= critical ? 2 : percent <= low ? 1 : 0;
    }

    /// <summary>True the moment the charge drops to or below the low threshold while on battery (for a one-time notice).</summary>
    public static bool CrossedLow(int previousPercent, Reading now, double low)
        => now.HasBattery && !now.PluggedIn && previousPercent > low && now.Percent >= 0 && now.Percent <= low;

    /// <summary>"12.5 W" for a charge or discharge rate.</summary>
    public static string Watts(double watts, IFormatProvider? culture = null)
        => Math.Abs(watts).ToString(Math.Abs(watts) < 10 ? "F1" : "F0", culture ?? CultureInfo.CurrentCulture) + " W";
}
