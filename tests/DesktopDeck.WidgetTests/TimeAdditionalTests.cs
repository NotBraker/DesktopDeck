using DesktopDeck.Widgets;
public static class AlarmsTests
{
    public static void InvalidDaysAndDuplicateLines(){var a=AlarmsLogic.Parse(["09:00 | daily | Work","09:00 | daily | Work","25:00 | daily | Bad","10:00 | nonsense | Bad"]);Expect.Equal(1,a.Alarms.Count,"duplicates fire once");Expect.Equal(2,a.Invalid.Count,"bad lines are visible");}
    public static void RepeatAndOnce(){var a=AlarmsLogic.Parse(["09:00 | Mon,Wed | Work"]).Alarms[0];var now=new DateTime(2026,10,5,9,0,1);Expect.True(AlarmsLogic.Due(a,now,now.AddSeconds(-2),""),"crossing time fires");Expect.True(!AlarmsLogic.Due(a,now,now.AddSeconds(-2),now.Date.AddHours(9).ToString("O")),"persistent key prevents refire");Expect.Equal(new DateTime(2026,10,7,9,0,0),AlarmsLogic.Next(a,now),"next weekday");}
}
public static class AnalogClockTests
{
    public static void NoonAndHalfHour(){var a=AnalogClockLogic.Angles(new DateTime(2026,1,1,12,30,0),true);Expect.Near(15,a.Hour,.001,"hour hand advances");Expect.Near(180,a.Minute,.001,"half hour");}
    public static void FractionalSeconds(){var a=AnalogClockLogic.Angles(new DateTime(2026,1,1,0,0,10,500),true);Expect.Near(63,a.Second,.001,"fraction included");Expect.Near(60,AnalogClockLogic.Angles(new DateTime(2026,1,1,0,0,10,500),false).Second,.001,"tick position");}
}
public static class SunMoonTests
{
    public static void EquinoxAtEquator(){var s=SunMoonLogic.Solar(new DateTime(2026,3,20),0,0,TimeSpan.Zero);Expect.Near(12,(s.Sunset!.Value-s.Sunrise!.Value).TotalHours,.2,"equinox day length");Expect.Near(6,s.Sunrise.Value.Hour+s.Sunrise.Value.Minute/60d,.3,"equatorial sunrise");}
    public static void PolarSeasons(){Expect.Equal("Midnight sun",SunMoonLogic.Solar(new DateTime(2026,6,21),80,0,TimeSpan.Zero).PolarState,"polar summer");Expect.Equal("Polar night",SunMoonLogic.Solar(new DateTime(2026,12,21),80,0,TimeSpan.Zero).PolarState,"polar winter");}
    public static void DatelineDaylight(){foreach(double longitude in new[]{-170d,170d}){var s=SunMoonLogic.Solar(new DateTime(2026,3,20),20,longitude,TimeSpan.Zero);Expect.True(s.Sunset>s.Sunrise,"solar interval ordered across UTC dates");Expect.Near(12,(s.Sunset!.Value-s.Sunrise!.Value).TotalHours,.4,"dateline daylight");}}
    public static void LunarEpoch(){var m=SunMoonLogic.Moon(new DateTimeOffset(2000,1,6,18,14,0,TimeSpan.Zero));Expect.Near(0,m.Illumination,1e-8,"new moon");Expect.Equal("New moon",m.Phase,"phase");Expect.Near(1,SunMoonLogic.Moon(new DateTimeOffset(2000,1,6,18,14,0,TimeSpan.Zero).AddDays(29.530588853/2)).Illumination,1e-6,"full moon");}
}

