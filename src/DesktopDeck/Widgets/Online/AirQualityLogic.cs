using System;
using System.Collections.Generic;
using System.Text.Json;
namespace DesktopDeck.Widgets;
public static class AirQualityLogic
{
    public static Dictionary<string,double> Parse(string json)
    {
        using var doc=JsonDocument.Parse(json); var list=new Dictionary<string,double>();
        if(!doc.RootElement.TryGetProperty("current",out var current)||current.ValueKind!=JsonValueKind.Object) throw new FormatException("Missing current air quality.");
        foreach(var p in current.EnumerateObject()) if(p.Value.ValueKind==JsonValueKind.Number&&p.Value.TryGetDouble(out double v)&&double.IsFinite(v)) list[p.Name]=v;
        if(!list.ContainsKey("us_aqi")&&!list.ContainsKey("european_aqi")) throw new FormatException("AQI is unavailable."); return list;
    }
    public static (string Text,string Color) Category(double value,bool european)
    {
        double[] limits=european?[20,40,60,80,100]:[50,100,150,200,300];
        string[] names=european?["Good","Fair","Moderate","Poor","Very poor","Extremely poor"]:["Good","Moderate","Unhealthy for sensitive groups","Unhealthy","Very unhealthy","Hazardous"];
        string[] colors=["#60c98b","#dfc76a","#e6a05b","#e27676","#b38cce","#c77a95"];
        int i=0;while(i<limits.Length&&value>limits[i])i++;return(names[i],colors[i]);
    }
}
