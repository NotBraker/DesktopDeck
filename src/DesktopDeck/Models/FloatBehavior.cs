namespace DesktopDeck.Models;

/// <summary>
/// The rules for two optional behaviors of floating widgets: fading when idle and letting clicks through. Pure logic (no windows), so it can be tested.
/// The floating layer polls the pointer and feeds these functions; the float window applies the answers.
/// </summary>
public static class FloatBehavior
{
    /// <summary>How see-through an idle widget gets unless chosen otherwise, in percent.</summary>
    public const double DefaultIdleOpacity = 35;
    public const double MinIdleOpacity = 10, MaxIdleOpacity = 90;
    /// <summary>Seconds without the pointer over a widget before it fades.</summary>
    public const double IdleSeconds = 3;

    /// <summary>The choices in the menu, in percent.</summary>
    public static readonly double[] IdleChoices = [20, 35, 50, 70];

    /// <summary>A usable idle opacity (percent): unusable numbers become the default, others are kept between 10 and 90.</summary>
    public static double ClampIdleOpacity(double percent) =>
        double.IsFinite(percent) ? Math.Clamp(percent, MinIdleOpacity, MaxIdleOpacity) : DefaultIdleOpacity;

    /// <summary>
    /// The opacity (0–1) a widget should have now. Full while it is in use (pointer over it, a menu open, being dragged) or when fading is off;
    /// the idle opacity once it has been left alone for <see cref="IdleSeconds"/>.
    /// </summary>
    public static double TargetOpacity(bool fade, double idlePercent, bool inUse, double idleSeconds) =>
        fade && !inUse && idleSeconds >= IdleSeconds ? ClampIdleOpacity(idlePercent) / 100.0 : 1.0;

    /// <summary>
    /// Whether a click-through widget takes clicks right now. Holding Ctrl with the pointer over it turns clicks on, and they stay on until
    /// the pointer leaves (so a right-click after letting go of Ctrl still works). A menu or a drag keeps it on. Widgets that don't pass clicks always take them.
    /// </summary>
    public static bool TakesClicks(bool clickThrough, bool wasTakingClicks, bool inside, bool ctrlHeld, bool busy)
    {
        if (!clickThrough) return true;
        if (busy) return wasTakingClicks || (inside && ctrlHeld);
        if (!inside) return false;
        return wasTakingClicks || ctrlHeld;
    }
}
