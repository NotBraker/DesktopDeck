using System;
using System.Collections.Generic;
using System.Text.Json;
namespace DesktopDeck.Widgets;
public static class CurrencyLogic
{
    public static Dictionary<string,double> Parse(string json)
    {
        using var doc=JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("rates",out var rates) || rates.ValueKind!=JsonValueKind.Object) throw new FormatException("Missing exchange rates.");
        var result=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
        foreach(var p in rates.EnumerateObject()) if(p.Value.ValueKind==JsonValueKind.Number&&p.Value.TryGetDouble(out double v)&&double.IsFinite(v)&&v>0) result[p.Name]=v;
        if(result.Count==0) throw new FormatException("No valid exchange rates.");
        return result;
    }
    public static double Convert(double amount,double rate,bool inverse)=> inverse ? amount/rate : amount*rate;
}

