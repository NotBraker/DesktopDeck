using DesktopDeck.Layout;

namespace DesktopDeck.Models;

/// <summary>The quick sizes in a widget's right-click menu: Small, Medium, Large and Default.</summary>
public enum SizePreset { Small, Medium, Large, Default }

/// <summary>
/// Works out the sizes (DIP) behind the quick sizes from a kind's default and minimum size. Every size lands on the deck grid
/// (n × grid − gap) and never goes below the minimum. No UI, so it can be tested.
/// </summary>
public static class SizePresets
{
    /// <summary>Small sits a quarter of the way from the minimum to the default; Medium is the default; Large is half as big again.</summary>
    public const double SmallShare = 0.25, LargeFactor = 1.5;

    public static readonly SizePreset[] All = [SizePreset.Small, SizePreset.Medium, SizePreset.Large, SizePreset.Default];

    /// <summary>
    /// The size for a preset. <paramref name="maxWidth"/> and <paramref name="maxHeight"/> are the room there is (the display or deck, in DIP);
    /// the result is cut down to whole grid steps that fit, but never below the minimum.
    /// </summary>
    public static (double Width, double Height) For(SizePreset preset, (double Width, double Height) minimum, (double Width, double Height) normal, double maxWidth, double maxHeight)
    {
        double w = Pick(preset, minimum.Width, normal.Width), h = Pick(preset, minimum.Height, normal.Height);
        return (Fit(w, minimum.Width, maxWidth), Fit(h, minimum.Height, maxHeight));
    }

    private static double Pick(SizePreset preset, double min, double normal) => preset switch
    {
        SizePreset.Small => min + SmallShare * Math.Max(0, normal - min),
        SizePreset.Large => normal * LargeFactor,
        _ => normal,
    };

    private static double Fit(double wanted, double minimum, double room)
    {
        double size = DeckLayout.SnapSize(wanted, minimum);
        if (double.IsFinite(room) && size > room)
        {
            // The largest whole number of grid steps that fits (size + gap spans whole cells), still no smaller than the minimum.
            double steps = Math.Floor((room + DeckLayout.Gap) / DeckLayout.Grid);
            size = DeckLayout.SnapSize(Math.Max(minimum, steps * DeckLayout.Grid - DeckLayout.Gap), minimum);
        }
        return size;
    }

    public static string Label(SizePreset preset) => preset switch
    {
        SizePreset.Small => "Small", SizePreset.Medium => "Medium", SizePreset.Large => "Large", _ => "Default",
    };
}
