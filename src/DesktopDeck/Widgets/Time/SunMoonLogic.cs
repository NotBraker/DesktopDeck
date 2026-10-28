using System;
namespace DesktopDeck.Widgets;
public static class SunMoonLogic
{
    public sealed record SolarDay(DateTimeOffset? Sunrise,DateTimeOffset? Sunset,string PolarState);
    // NOAA fractional-year approximation; zenith 90.833 degrees includes refraction.
    public static SolarDay Solar(DateTime date,double latitude,double longitude,TimeSpan offset,double zenith=90.833)
    {
        latitude=Math.Clamp(latitude,-90,90);longitude=Math.Clamp(longitude,-180,180);
        double gamma=2*Math.PI/365*(date.DayOfYear-1);
        double eq=229.18*(.000075+.001868*Math.Cos(gamma)-.032077*Math.Sin(gamma)-.014615*Math.Cos(2*gamma)-.040849*Math.Sin(2*gamma));
        double dec=.006918-.399912*Math.Cos(gamma)+.070257*Math.Sin(gamma)-.006758*Math.Cos(2*gamma)+.000907*Math.Sin(2*gamma)-.002697*Math.Cos(3*gamma)+.00148*Math.Sin(3*gamma);
        double lat=latitude*Math.PI/180;double cos=(Math.Cos(zenith*Math.PI/180)-Math.Sin(lat)*Math.Sin(dec))/(Math.Cos(lat)*Math.Cos(dec));
        if(cos>1)return new(null,null,"Polar night");if(cos< -1)return new(null,null,"Midnight sun");
        double hour=Math.Acos(cos)*180/Math.PI;double noon=720-4*longitude-eq+offset.TotalMinutes;
        var midnight=new DateTimeOffset(date.Date,offset);return new(midnight.AddMinutes(noon-4*hour),midnight.AddMinutes(noon+4*hour),"");
    }
    public static (double Age,double Illumination,string Phase) Moon(DateTimeOffset date)
    {
        const double cycle=29.530588853;double age=((date-new DateTimeOffset(2000,1,6,18,14,0,TimeSpan.Zero)).TotalDays%cycle+cycle)%cycle;
        double illumination=(1-Math.Cos(2*Math.PI*age/cycle))/2;
        string[] names=["New moon","Waxing crescent","First quarter","Waxing gibbous","Full moon","Waning gibbous","Last quarter","Waning crescent"];
        return(age,illumination,names[(int)Math.Floor(age/cycle*8+.5)%8]);
    }
}
