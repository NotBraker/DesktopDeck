using System;
using System.Collections.Generic;
using System.Text.Json;
namespace DesktopDeck.Widgets;
public static class OnThisDayLogic
{
    public sealed record Entry(int? Year,string Text,string Url);
    public static List<Entry> Parse(string json,string type)
    {
        using var doc=JsonDocument.Parse(json);var list=new List<Entry>();
        if(doc.RootElement.ValueKind!=JsonValueKind.Object)throw new FormatException("Invalid history response.");
        foreach(var group in doc.RootElement.EnumerateObject())
        {
            if(type!="all"&&group.Name!=type||group.Value.ValueKind!=JsonValueKind.Array)continue;
            foreach(var e in group.Value.EnumerateArray())
            {
                if(!e.TryGetProperty("text",out var t)||t.ValueKind!=JsonValueKind.String)continue;
                int? year=e.TryGetProperty("year",out var y)&&y.TryGetInt32(out int n)?n:null;string url="";
                if(e.TryGetProperty("pages",out var pages)&&pages.ValueKind==JsonValueKind.Array)
                    foreach(var page in pages.EnumerateArray()) if(page.TryGetProperty("content_urls",out var urls)&&urls.TryGetProperty("desktop",out var desktop)&&desktop.TryGetProperty("page",out var link)){url=link.GetString()??"";break;}
                list.Add(new Entry(year,OnlineLogic.StripHtml(t.GetString()),url));
            }
        }
        if(list.Count==0)throw new FormatException("No history entries available.");return list;
    }
}
