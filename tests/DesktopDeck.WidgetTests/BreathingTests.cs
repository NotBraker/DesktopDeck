using DesktopDeck.Widgets;

public static class BreathingTests
{
    public static void PhaseBoundaries()
    {
        int[] pattern = [4, 4, 4, 4];
        Expect.Equal(0, BreathingLogic.At(0, pattern, 3).Phase, "starts inhaling");
        Expect.Equal(1, BreathingLogic.At(4, pattern, 3).Phase, "hold boundary");
        Expect.Equal(2, BreathingLogic.At(8, pattern, 3).Phase, "exhale boundary");
        Expect.Equal(3, BreathingLogic.At(12, pattern, 3).Phase, "second hold");
        Expect.Equal(2, BreathingLogic.At(16, pattern, 3).Cycle, "second cycle");
        Expect.True(BreathingLogic.At(48, pattern, 3).Complete, "stops at cycle limit");
    }
    public static void ZeroHoldsAndScale()
    {
        int[] pattern = [4, 0, 8, 0];
        Expect.Equal(2, BreathingLogic.At(4, pattern, 1).Phase, "skips zero hold");
        Expect.Near(0.5, BreathingLogic.At(2, pattern, 1).Expansion, 1e-9, "half inhale");
        Expect.Near(0.5, BreathingLogic.At(8, pattern, 1).Expansion, 1e-9, "half exhale");
        Expect.Equal(0, BreathingLogic.At(-1, pattern, 1).Phase, "clock rollback clamped");
        Expect.Throws<ArgumentException>(() => BreathingLogic.At(0, [0, 0, 0, 0], 1), "empty pattern");
    }
}
