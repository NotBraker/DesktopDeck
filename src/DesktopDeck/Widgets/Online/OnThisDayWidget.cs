using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using DesktopDeck.Models;
namespace DesktopDeck.Widgets;
internal sealed class OnThisDayWidget:IWidget
{
    public string Kind=>"on-this-day";public string Name=>"On this day";public string Description=>"Events, births and discoveries from this date in history";public string Section=>"Online";public string Glyph=>"\uE787";public string Accent=>"#b398cd";
    public (double Width,double Height) DefaultSize=>(324,324);public (double Width,double Height) MinimumSize=>(180,180);
    public IReadOnlyList<OptionSpec> Options{get;}=[OptionSpec.Choice("type","Entries","events",["all","events","births","deaths","holidays"],null,"What to show"),OptionSpec.Number("count","How many entries",5,1,30,"What to show"),OptionSpec.Toggle("yearsAgo","Show years ago",true,"What to show"),OptionSpec.Choice("language","Language","en",["en","de","fr","es","nl","it","pt"],null,"What to show"),OptionSpec.Choice("sort","Sort","oldest",["oldest","newest","source"],null,"Layout"),OptionSpec.Toggle("rotate","Rotate one entry",false,"Layout"),OptionSpec.Number("rotation","Rotate every (seconds)",30,10,300,"Behavior"),OptionSpec.Number("textSize","Entry text size",13,10,22,"Layout"),OptionSpec.Color("yearColor","Year color","","Colors"),OptionSpec.Toggle("dividers","Entry dividers",true,"Layout"),OptionSpec.Toggle("showUpdated","Show update time",false,"Behavior")];
    static string Signature(WidgetContext c)=>c.Get("language")+"/"+c.Get("type")+"/"+DateTime.Now.ToString("MM/dd",CultureInfo.InvariantCulture);
    static void Fill(LauncherItem i)=>OnlineWidget.Fill(i,"{\"events\":[{\"year\":1957,\"text\":\"Sputnik 1 becomes the first artificial satellite to orbit Earth.\",\"pages\":[{\"content_urls\":{\"desktop\":{\"page\":\"https://en.wikipedia.org/wiki/Sputnik_1\"}}}]},{\"year\":1883,\"text\":\"The Orient Express begins its first journey from Paris.\"},{\"year\":1582,\"text\":\"The Gregorian calendar is adopted in several European countries.\"}]}","en/events/"+DateTime.Now.ToString("MM/dd",CultureInfo.InvariantCulture));
    public IEnumerable<Action<LauncherItem>> Samples=>[i=>Fill(i),i=>{Fill(i);i.Options["rotate"]="true";i.Options["sort"]="newest";},i=>{Fill(i);i.Options[OnlineWidget.CacheError]="Couldn't connect.";}];
    static OnlineSource Source(WidgetContext c){string type=c.Get("type");return new(Signature(c),TimeSpan.FromDays(1),async()=>{var entries=OnThisDayLogic.Parse(await OnlineWidget.GetAsync("https://api.wikimedia.org/feed/v1/wikipedia/"+Uri.EscapeDataString(c.Get("language"))+"/onthisday/"+Uri.EscapeDataString(type)+"/"+DateTime.Now.ToString("MM/dd",CultureInfo.InvariantCulture)),type).Take(100).Select(e=>new{year=e.Year,text=e.Text,pages=new[]{new{content_urls=new{desktop=new{page=e.Url}}}}});return JsonSerializer.Serialize(new Dictionary<string,object>{{type=="all"?"events":type,entries}});});}
    public IEnumerable<WidgetAction> Actions(WidgetContext c)=>[OnlineWidget.RefreshAction(c,Source(c))];
    public FrameworkElement Build(WidgetContext c)
    {
        var (card,body)=WidgetKit.Frame(c);OnlineWidget.Keep(c,Source(c),now=>{if(c.GetBool("rotate")){var last=c.State("history.last",DateTimeOffset.MinValue);if((now-last).TotalSeconds>=c.GetNumber("rotation")){c.SetState("history.last",now,false);c.SetState("history.index",c.State("history.index",0)+1);}}},()=>c.GetBool("rotate"));OnlineWidget.AddFooter(c,body,c.GetBool("showUpdated"));
        List<OnThisDayLogic.Entry> entries;try{entries=OnThisDayLogic.Parse(OnlineWidget.Cached(c),c.Get("type"));}catch(Exception e)when(e is JsonException or FormatException){body.Children.Add(OnlineWidget.Waiting(c));return card;}
        entries=(c.Get("sort")switch{"newest"=>entries.OrderByDescending(e=>e.Year),"oldest"=>entries.OrderBy(e=>e.Year),_=>entries.AsEnumerable()}).Take(c.GetInt("count")).ToList();var stack=new StackPanel();stack.Children.Add(WidgetKit.Muted(c,DateTime.Now.ToString("MMMM d",CultureInfo.CurrentCulture)));if(c.GetBool("rotate")&&entries.Count>0)entries=[entries[c.State("history.index",0)%entries.Count]];
        foreach(var entry in entries){var row=new StackPanel{Margin=new Thickness(0,8,0,2)};if(entry.Year is {} year)row.Children.Add(WidgetKit.Text(c,year.ToString(CultureInfo.CurrentCulture)+(c.GetBool("yearsAgo")?" · "+(DateTime.Now.Year-year)+" years ago":""),11,c.GetColor("yearColor"),true));var text=WidgetKit.Text(c,entry.Text,c.GetNumber("textSize"));text.TextWrapping=TextWrapping.Wrap;row.Children.Add(text);stack.Children.Add(WidgetKit.Clickable(WidgetKit.Row(row),()=>OnlineWidget.OpenLink(c,entry.Url),entry.Text));if(c.GetBool("dividers"))stack.Children.Add(WidgetKit.Divider());}body.Children.Add(WidgetKit.Scroll(stack));return card;
    }
}
