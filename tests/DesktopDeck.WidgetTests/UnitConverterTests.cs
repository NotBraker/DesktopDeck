using DesktopDeck.Widgets;

public static class UnitConverterTests
{
    public static void TemperatureOffsets()
    {
        Expect.Near(32, UnitConverterLogic.Convert(0, "C", "F"), 1e-10, "freezing");
        Expect.Near(100, UnitConverterLogic.Convert(212, "F", "C"), 1e-10, "boiling");
        Expect.Near(-273.15, UnitConverterLogic.Convert(0, "K", "C"), 1e-10, "absolute zero");
        Expect.Near(-40, UnitConverterLogic.Convert(-40, "F", "C"), 1e-10, "negative temperature");
    }
    public static void PhysicalUnits()
    {
        Expect.Near(1609.344, UnitConverterLogic.Convert(1, "mi", "m"), 1e-9, "mile");
        Expect.Near(0.45359237, UnitConverterLogic.Convert(1, "lb", "kg"), 1e-10, "pound");
        Expect.Near(3.785411784, UnitConverterLogic.Convert(1, "US gal", "L"), 1e-9, "US gallon");
        Expect.Near(1, UnitConverterLogic.Convert(3.6, "km/h", "m/s"), 1e-9, "speed");
        Expect.Near(10000, UnitConverterLogic.Convert(1, "ha", "m²"), 1e-9, "hectare");
        Expect.Near(1024, UnitConverterLogic.Convert(1, "KiB", "B"), 0, "binary data");
        Expect.Near(1000, UnitConverterLogic.Convert(1, "kB", "B"), 0, "decimal data");
        Expect.Near(86400, UnitConverterLogic.Convert(1, "day", "s"), 0, "day");
    }
    public static void RejectInvalidConversions()
    {
        Expect.Throws<ArgumentException>(() => UnitConverterLogic.Convert(1, "m", "kg"), "category mismatch");
        Expect.Throws<ArgumentException>(() => UnitConverterLogic.Convert(1, "bogus", "kg"), "unknown unit");
        Expect.Throws<ArgumentOutOfRangeException>(() => UnitConverterLogic.Convert(double.NaN, "m", "ft"), "finite input");
    }
    public static void EveryUnitRoundTrips()
    {
        foreach (var group in UnitConverterLogic.Units.GroupBy(u => u.Category))
            foreach (var from in group)
                foreach (var to in group)
                    Expect.Near(123.456, UnitConverterLogic.Convert(UnitConverterLogic.Convert(123.456, from.Id, to.Id), to.Id, from.Id), 1e-7, from.Id + " / " + to.Id);
    }
}
