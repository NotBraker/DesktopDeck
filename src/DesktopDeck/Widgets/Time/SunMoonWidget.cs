using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using DesktopDeck.Models;
namespace DesktopDeck.Widgets;
internal sealed class SunMoonWidget:IWidget
{
    public string Kind=>"sun-moon";public string Name=>"Sun & moon";public string Description=>"Offline sunrise, sunset, golden hour and lunar phase";public string Section=>"Time";public string Glyph=>Theme.Glyphs.Sun;public string Accent=>"#f59e0b";
    public (double Width,double Height) DefaultSize=>(276,276);public (double Width,double Height) MinimumSize=>(180,132);
    public IReadOnlyList<OptionSpec> Options{get;}=[OptionSpec.Number("latitude","Latitude",40.71,-90,90,"Location"),OptionSpec.Number("longitude","Longitude",-74.01,-180,180,"Location"),OptionSpec.Text("location","Location label","New York","Location"),OptionSpec.Text("zone","Time zone","Eastern Standard Time","Location","Must match the location for local sunrise times."),OptionSpec.Toggle("golden","Golden hour",true,"What to show"),OptionSpec.Toggle("moon","Moon phase",true,"What to show"),OptionSpec.Toggle("length","Day length",true,"What to show"),OptionSpec.Choice("clock","Clock","12",["12","24"],null,"Layout"),OptionSpec.Choice("layout","Layout","list",["list","grid"],null,"Layout"),OptionSpec.Color("sunColor","Sun color","","Colors"),OptionSpec.Color("moonColor","Moon color","#8b9cf6","Colors")];
    public IEnumerable<Action<LauncherItem>> Samples=>[_=>{},i=>{i.Options["latitude"]="51.5";i.Options["longitude"]="-0.12";i.Options["zone"]="GMT Standard Time";i.Options["location"]="London";i.Options["clock"]="24";i.Options["layout"]="grid";i.Width=324;i.Height=276;},i=>{i.Options["latitude"]="78.22";i.Options["longitude"]="15.65";i.Options["zone"]="W. Europe Standard Time";i.Options["location"]="Longyearbyen";}];
    public FrameworkElement Build(WidgetContext ctx)
    {
        var(card,body)=WidgetKit.Frame(ctx);var zone=WorldClocksLogic.Resolve(ctx.Get("zone"));if(zone is null){body.Children.Add(WidgetKit.Empty(ctx,"Unknown time zone"));return card;}
        var now=TimeZoneInfo.ConvertTime(DateTimeOffset.Now,zone);double lat=ctx.GetNumber("latitude"),lon=ctx.GetNumber("longitude");var solar=SunMoonLogic.Solar(now.Date,lat,lon,now.Offset);var moon=SunMoonLogic.Moon(now);
        var stack=new StackPanel();if(ctx.Height>=180)stack.Children.Add(WidgetKit.Muted(ctx,ctx.Get("location"),11));string format=ctx.Get("clock")=="24"?"HH:mm":"h:mm tt";
        string Time(DateTimeOffset? at)=>at?.ToString(format,CultureInfo.CurrentCulture)??"—";
        var times=new Grid{Margin=new Thickness(0,9,0,7)};times.ColumnDefinitions.Add(new ColumnDefinition());times.ColumnDefinitions.Add(new ColumnDefinition());
        void TimeColumn(string label,string value,int col){var s=new StackPanel();s.Children.Add(WidgetKit.Muted(ctx,label,11));s.Children.Add(WidgetKit.Text(ctx,value,ctx.Width<228?17:24,ctx.GetColor("sunColor"),true));Grid.SetColumn(s,col);times.Children.Add(s);}
        TimeColumn("Sunrise",Time(solar.Sunrise),0);TimeColumn("Sunset",Time(solar.Sunset),1);stack.Children.Add(times);
        if(ctx.Height>=180){if(solar.PolarState.Length>0)stack.Children.Add(WidgetKit.Text(ctx,solar.PolarState,13,ctx.GetColor("sunColor")));
            else if(ctx.GetBool("length")){var length=solar.Sunset!.Value-solar.Sunrise!.Value;stack.Children.Add(WidgetKit.Muted(ctx,$"Daylight  {(int)length.TotalHours}h {length.Minutes}m"));}
            if(ctx.GetBool("golden")&&ctx.Height>=228){var golden=SunMoonLogic.Solar(now.Date,lat,lon,now.Offset,84);if(golden.Sunrise is not null&&solar.Sunrise is not null){stack.Children.Add(WidgetKit.Divider());stack.Children.Add(WidgetKit.Muted(ctx,"Golden hour · sun below 6°",10.5));stack.Children.Add(WidgetKit.Text(ctx,Time(solar.Sunrise)+" – "+Time(golden.Sunrise),12));stack.Children.Add(WidgetKit.Text(ctx,Time(golden.Sunset)+" – "+Time(solar.Sunset),12));}}
            if(ctx.GetBool("moon")){stack.Children.Add(WidgetKit.Divider());var m=new StackPanel();m.Children.Add(WidgetKit.Text(ctx,moon.Phase,13,ctx.GetColor("moonColor"),true));m.Children.Add(WidgetKit.Muted(ctx,$"{moon.Illumination:P0} illuminated · day {moon.Age:F1}",11));if(ctx.Get("layout")=="grid"){var row=new DockPanel();var ring=WidgetKit.Ring(moon.Illumination,ctx.GetColor("moonColor"),36,4);ring.Margin=new Thickness(0,0,10,0);row.Children.Add(ring);row.Children.Add(m);stack.Children.Add(row);}else stack.Children.Add(m);}}
        body.Children.Add(WidgetKit.Scroll(stack));var builtDay=now.Date;ctx.Tick(()=>false,at=>{if(TimeZoneInfo.ConvertTime(at,zone).Date!=builtDay)ctx.Rebuild();});return card;
    }
}

