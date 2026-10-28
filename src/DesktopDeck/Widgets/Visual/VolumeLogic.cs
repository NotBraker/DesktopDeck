using System;

namespace DesktopDeck.Widgets;

/// <summary>Volume steps, click positions and the speaker glyph. Pure logic (unit tested).</summary>
public static class VolumeLogic
{
    /// <summary>Segoe Fluent Icons: speaker with an X.</summary>
    public const string MutedGlyph = "";
    /// <summary>Segoe Fluent Icons: plain speaker with waves (used when the icon does not follow the level).</summary>
    public const string SpeakerGlyph = "";

    /// <summary>
    /// One − or + press: moves to the next multiple of <paramref name="stepPercent"/> in <paramref name="direction"/>
    /// (+1 or −1), so 47% goes to 50% up or 45% down; kept within 0 and <paramref name="maxLevel"/>.
    /// </summary>
    public static double Step(double level, double stepPercent, int direction, double maxLevel = 1)
    {
        stepPercent = Math.Clamp(stepPercent, 1, 100);
        double percent = Math.Round(Math.Clamp(level, 0, 1) * 100, 1);
        double next = direction >= 0
            ? Math.Floor(percent / stepPercent + 1e-6) * stepPercent + stepPercent
            : Math.Ceiling(percent / stepPercent - 1e-6) * stepPercent - stepPercent;
        return Math.Clamp(next / 100, 0, Math.Clamp(maxLevel, 0, 1));
    }

    /// <summary>The level for a click <paramref name="position"/> along a bar of <paramref name="length"/>; vertical bars fill upward.</summary>
    public static double FromPosition(double position, double length, bool vertical, double maxLevel = 1)
    {
        if (length <= 0 || !double.IsFinite(position)) return 0;
        double f = Math.Clamp(position / length, 0, 1);
        if (vertical) f = 1 - f;
        // Snap to whole percent so the ends are exact (a click on the last pixel gives 100%).
        f = Math.Round(f * 100) / 100;
        return Math.Clamp(f, 0, Math.Clamp(maxLevel, 0, 1));
    }

    /// <summary>The level for a click on a ring: clockwise from 12 o'clock. <paramref name="dx"/>, <paramref name="dy"/> are from the center (y down).</summary>
    public static double FromAngle(double dx, double dy, double maxLevel = 1)
    {
        if (dx == 0 && dy == 0) return 0;
        double angle = Math.Atan2(dx, -dy);
        if (angle < 0) angle += 2 * Math.PI;
        double f = Math.Round(angle / (2 * Math.PI) * 100) / 100;
        return Math.Clamp(f, 0, Math.Clamp(maxLevel, 0, 1));
    }

    /// <summary>"47%".</summary>
    public static string Percent(double level) => (int)Math.Round(Math.Clamp(level, 0, 1) * 100) + "%";

    /// <summary>The Segoe Fluent speaker glyph for a level: muted, silent, low, medium or high.</summary>
    public static string Glyph(double level, bool muted, bool byLevel = true)
    {
        if (muted) return MutedGlyph;
        if (!byLevel) return SpeakerGlyph;
        return level <= 0.001 ? "" : level < 0.34 ? "" : level < 0.67 ? "" : "";
    }
}
