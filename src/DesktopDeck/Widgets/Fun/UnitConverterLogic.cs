using System;
using System.Linq;

namespace DesktopDeck.Widgets;

public static class UnitConverterLogic
{
    public sealed record Unit(string Id, string Category, double Factor, double Offset = 0);
    public static readonly Unit[] Units =
    [
        new("m", "Length", 1), new("ft", "Length", .3048), new("cm", "Length", .01), new("mm", "Length", .001), new("km", "Length", 1000), new("in", "Length", .0254), new("yd", "Length", .9144), new("mi", "Length", 1609.344),
        new("kg", "Mass", 1), new("lb", "Mass", .45359237), new("g", "Mass", .001), new("oz", "Mass", .028349523125), new("tonne", "Mass", 1000),
        new("C", "Temperature", 1), new("F", "Temperature", 5d / 9, -32d * 5 / 9), new("K", "Temperature", 1, -273.15),
        new("L", "Volume", 1), new("US gal", "Volume", 3.785411784), new("mL", "Volume", .001), new("US cup", "Volume", .2365882365), new("imp gal", "Volume", 4.54609), new("m³", "Volume", 1000),
        new("km/h", "Speed", 1d / 3.6), new("mph", "Speed", .44704), new("m/s", "Speed", 1), new("kn", "Speed", 1852d / 3600),
        new("m²", "Area", 1), new("ft²", "Area", .09290304), new("km²", "Area", 1e6), new("ha", "Area", 10000), new("acre", "Area", 4046.8564224),
        new("B", "Data size", 1), new("kB", "Data size", 1000), new("MB", "Data size", 1e6), new("GB", "Data size", 1e9), new("KiB", "Data size", 1024), new("MiB", "Data size", 1048576), new("GiB", "Data size", 1073741824), new("bit", "Data size", .125),
        new("s", "Time", 1), new("min", "Time", 60), new("h", "Time", 3600), new("day", "Time", 86400), new("week", "Time", 604800), new("ms", "Time", .001),
    ];
    public static string[] Categories => Units.Select(u => u.Category).Distinct().ToArray();
    public static Unit[] In(string category) => Units.Where(u => u.Category == category).ToArray();
    public static double Convert(double value, string from, string to)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        var a = Units.FirstOrDefault(u => u.Id == from) ?? throw new ArgumentException("Unknown source unit");
        var b = Units.FirstOrDefault(u => u.Id == to) ?? throw new ArgumentException("Unknown target unit");
        if (a.Category != b.Category) throw new ArgumentException("Units belong to different categories");
        double result = (value * a.Factor + a.Offset - b.Offset) / b.Factor;
        if (!double.IsFinite(result)) throw new ArgumentOutOfRangeException(nameof(value), "Conversion overflow");
        return result;
    }
}
