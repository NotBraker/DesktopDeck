using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using DesktopDeck.Models;
namespace DesktopDeck.Widgets;
internal sealed class AirQualityWidget:IWidget
{
    public string Kind=>"air-quality";public string Name=>"Air quality";public string Description=>"Current air quality, pollutants and UV index at your location";public string Section=>"Online";public string Glyph=>"\uE9CA";public string Accent=>"#73bda7";
    public (double Width,double Height) DefaultSize=>(276,276);public (double Width,double Height) MinimumSize=>(180,180);
    public IReadOnlyList<OptionSpec> Options{get;}=[OptionSpec.Text("location","Location name","New York","Location"),OptionSpec.Number("latitude","Latitude",40.7,-90,90,"Location"),OptionSpec.Number("longitude","Longitude",-74,-180,180,"Location"),OptionSpec.Choice("scale","Air quality scale","us",["us","european"],["US","European"],"What to show"),OptionSpec.Toggle("pm2_5","PM2.5",true,"What to show"),OptionSpec.Toggle("pm10","PM10",true,"What to show"),OptionSpec.Toggle("ozone","Ozone",false,"What to show"),OptionSpec.Toggle("nitrogen_dioxide","Nitrogen dioxide",false,"What to show"),OptionSpec.Toggle("uv_index","UV index",true,"What to show"),OptionSpec.Toggle("category","Show category",true,"Layout"),OptionSpec.Number("decimals","Decimal places",1,0,3,"Layout"),OptionSpec.Color("pollutantColor","Pollutant values","","Colors"),OptionSpec.Number("refresh","Refresh every (minutes)",30,30,360,"Behavior"),OptionSpec.Toggle("showUpdated","Show update time",true,"Behavior")];
    static string Signature(WidgetContext c)=>FormattableString.Invariant($"{c.GetNumber("latitude"):0.####},{c.GetNumber("longitude"):0.####}");
    static void Fill(LauncherItem i)=>OnlineWidget.Fill(i,"{\"current\":{\"us_aqi\":54,\"european_aqi\":50,\"pm2_5\":15.3,\"pm10\":17.5,\"ozone\":18,\"nitrogen_dioxide\":41.9,\"uv_index\":2.1}}","40.7,-74");
    public IEnumerable<Action<LauncherItem>> Samples=>[i=>Fill(i),i=>{Fill(i);i.Options["scale"]="european";i.Options["ozone"]="true";i.Options["nitrogen_dioxide"]="true";i.Height=324;},i=>{Fill(i);i.Options[OnlineWidget.CacheError]="Couldn't connect.";}];
    static OnlineSource Source(WidgetContext c){string coords=FormattableString.Invariant($"latitude={c.GetNumber("latitude"):0.####}&longitude={c.GetNumber("longitude"):0.####}");return new(Signature(c),OnlineLogic.Interval(c.GetNumber("refresh"),30),async()=>{var current=AirQualityLogic.Parse(await OnlineWidget.GetAsync("https://air-quality-api.open-meteo.com/v1/air-quality?"+coords+"&current=us_aqi,european_aqi,pm2_5,pm10,ozone,nitrogen_dioxide,uv_index"));return JsonSerializer.Serialize(new{current});});}
    public IEnumerable<WidgetAction> Actions(WidgetContext c)=>[OnlineWidget.RefreshAction(c,Source(c))];
    public FrameworkElement Build(WidgetContext c)
    {
        OnlineWidget.Keep(c,Source(c));var (card,body)=WidgetKit.Frame(c);OnlineWidget.AddFooter(c,body,c.GetBool("showUpdated"));Dictionary<string,double> data;try{data=AirQualityLogic.Parse(OnlineWidget.Cached(c));}catch(Exception e)when(e is JsonException or FormatException){body.Children.Add(OnlineWidget.Waiting(c));return card;}
        bool eu=c.Get("scale")=="european";var stack=new StackPanel();stack.Children.Add(WidgetKit.Muted(c,c.Get("location")));bool available=data.TryGetValue(eu?"european_aqi":"us_aqi",out double aqi);var category=AirQualityLogic.Category(aqi,eu);stack.Children.Add(WidgetKit.Text(c,available?aqi.ToString("0",CultureInfo.CurrentCulture)+" AQI":"AQI unavailable",32,available?WidgetKit.Color(category.Color):Theme.Muted,true));
        if(c.GetBool("category")&&available){var text=WidgetKit.Text(c,category.Text,12,WidgetKit.Color(category.Color));text.TextWrapping=TextWrapping.Wrap;stack.Children.Add(text);}stack.Children.Add(WidgetKit.Divider());
        foreach(var (key,label) in new[]{("pm2_5","PM2.5"),("pm10","PM10"),("ozone","Ozone"),("nitrogen_dioxide","NO₂"),("uv_index","UV index")})if(c.GetBool(key)){var row=new Grid{Margin=new Thickness(0,3,0,3)};row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.Children.Add(WidgetKit.Muted(c,label));var value=WidgetKit.Text(c,data.TryGetValue(key,out var n)?n.ToString("F"+c.GetInt("decimals"),CultureInfo.CurrentCulture)+(key=="uv_index"?"":" µg/m³"):"—",12,c.GetColor("pollutantColor"));Grid.SetColumn(value,1);row.Children.Add(value);stack.Children.Add(row);}body.Children.Add(WidgetKit.Scroll(stack));return card;
    }
}
