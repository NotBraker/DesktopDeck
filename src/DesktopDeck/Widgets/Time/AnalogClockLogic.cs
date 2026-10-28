using System;
namespace DesktopDeck.Widgets;
public static class AnalogClockLogic
{
    public static (double Hour,double Minute,double Second) Angles(DateTime time,bool smooth)
    {
        double seconds=time.Second+(smooth?time.Millisecond/1000.0:0); double minutes=time.Minute+(smooth?seconds/60:0);
        return ((time.Hour%12+minutes/60)*30,minutes*6,seconds*6);
    }
}
