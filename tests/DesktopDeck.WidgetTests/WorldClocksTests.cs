using System.Globalization;
using DesktopDeck.Widgets;

public static class WorldClocksTests
{
    public static void ParsesLabelAndWindowsId()
    {
        var place = WorldClocksLogic.Parse("Tokyo | Tokyo Standard Time");
        Expect.Equal("Tokyo", place.Label, "label");
        Expect.True(place.Zone is not null && place.Zone.BaseUtcOffset == TimeSpan.FromHours(9), "Tokyo is UTC+9");
        Expect.Equal("Tokyo", WorldClocksLogic.Parse("Tokyo Standard Time").Label, "label made from the id");
    }

    public static void UnknownZone()
    {
        var place = WorldClocksLogic.Parse("Atlantis | Atlantis Standard Time");
        Expect.True(place.Zone is null, "unknown zone has no TimeZoneInfo");
        Expect.Equal("Atlantis", place.Label, "label kept");
        Expect.True(WorldClocksLogic.Resolve("") is null && WorldClocksLogic.Resolve("xy") is null, "blank and tiny text");
    }

    public static void SpecialAndIanaIds()
    {
        Expect.True(WorldClocksLogic.Resolve("UTC") == TimeZoneInfo.Utc, "UTC");
        Expect.True(WorldClocksLogic.Resolve("local") == TimeZoneInfo.Local, "local");
        var iana = WorldClocksLogic.Resolve("Asia/Tokyo");
        Expect.True(iana is not null && iana.BaseUtcOffset == TimeSpan.FromHours(9), "IANA id");
        Expect.Equal("New York", WorldClocksLogic.Parse("America/New_York").Label, "IANA label");
    }

    public static void CityFromDisplayName()
    {
        // Windows lists "(UTC+09:00) Osaka, Sapporo, Tokyo" (English display names).
        var zone = WorldClocksLogic.Resolve("Sapporo");
        Expect.True(zone is not null && zone.BaseUtcOffset == TimeSpan.FromHours(9), "city found");
    }

    public static void OffsetTexts()
    {
        Expect.Equal("+7 h", WorldClocksLogic.Relative(TimeSpan.FromHours(7)), "ahead");
        Expect.Equal("−4 h 30 m", WorldClocksLogic.Relative(TimeSpan.FromHours(-4.5)), "behind");
        Expect.Equal("Same time", WorldClocksLogic.Relative(TimeSpan.Zero), "same");
        Expect.Equal("UTC+5:45", WorldClocksLogic.Utc(new TimeSpan(5, 45, 0)), "Nepal");
        Expect.Equal("UTC−3", WorldClocksLogic.Utc(TimeSpan.FromHours(-3)), "minus");
        var utcNow = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var tokyo = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
        var london = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
        Expect.Equal(TimeSpan.FromHours(9), WorldClocksLogic.Difference(tokyo, utcNow, london), "Tokyo vs London in winter");
        Expect.Equal(TimeSpan.FromHours(8), WorldClocksLogic.Difference(tokyo, utcNow.AddMonths(6), london), "Tokyo vs London in summer");
    }

    public static void DaysAndDaytime()
    {
        var here = new DateTime(2026, 10, 4, 22, 0, 0);
        Expect.Equal("Tomorrow", WorldClocksLogic.DayWord(here, here.AddHours(7)), "ahead past midnight");
        Expect.Equal("Yesterday", WorldClocksLogic.DayWord(here.AddHours(-20), here.AddHours(-23)), "behind past midnight");
        Expect.True(WorldClocksLogic.IsDaytime(new DateTime(2026, 1, 1, 12, 0, 0), 7, 19), "noon is day");
        Expect.True(!WorldClocksLogic.IsDaytime(new DateTime(2026, 1, 1, 19, 0, 0), 7, 19), "7 pm is night");
        Expect.True(WorldClocksLogic.IsDaytime(new DateTime(2026, 1, 1, 1, 0, 0), 20, 4), "wrapping range");
    }

    public static void TimeFormats()
    {
        var t = new DateTime(2026, 1, 1, 15, 4, 9);
        Expect.Equal(("15:04", ""), WorldClocksLogic.Format(t, true, false), "24 h");
        Expect.Equal(("3:04:09", "PM"), WorldClocksLogic.Format(t, false, true), "12 h with seconds");
        Expect.True(WorldClocksLogic.Use24Hour("auto", CultureInfo.GetCultureInfo("de-DE")) && !WorldClocksLogic.Use24Hour("auto", CultureInfo.GetCultureInfo("en-US")), "auto");
    }
}
