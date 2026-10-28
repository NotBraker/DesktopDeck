using System;
using System.Linq;

namespace DesktopDeck.Widgets;

public static class BreathingLogic
{
    public sealed record Position(int Phase, int Cycle, double Remaining, double Expansion, bool Complete);
    public static Position At(double elapsed, int[] seconds, int cycles)
    {
        if (seconds.Length != 4 || seconds.Any(s => s < 0) || seconds.Sum() == 0 || cycles < 1)
            throw new ArgumentException("Use four nonnegative phases and at least one cycle");
        elapsed = double.IsFinite(elapsed) ? Math.Max(0, elapsed) : 0;
        double duration = seconds.Sum();
        if (elapsed >= duration * cycles) return new(3, cycles, 0, 0, true);
        int cycle = (int)(elapsed / duration) + 1;
        double offset = elapsed % duration;
        for (int phase = 0; phase < 4; phase++)
        {
            if (offset >= seconds[phase]) { offset -= seconds[phase]; continue; }
            double fraction = offset / seconds[phase];
            return new(phase, cycle, seconds[phase] - offset, phase switch { 0 => fraction, 1 => 1, 2 => 1 - fraction, _ => 0 }, false);
        }
        return new(3, cycle, 0, 0, true);
    }
}
