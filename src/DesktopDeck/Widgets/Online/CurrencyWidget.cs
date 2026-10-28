using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using DesktopDeck.Models;
namespace DesktopDeck.Widgets;
internal sealed class CurrencyWidget:IWidget
{
    public string Kind=>"currency"; public string Name=>"Exchange rates"; public string Description=>"Convert an amount with daily exchange rates";public string Section=>"Online";public string Glyph=>"\uE8D4";public string Accent=>"#73bda7";
    public (double Width,double Height) DefaultSize=>(276,276);public (double Width,double Height) MinimumSize=>(180,180);
    public IReadOnlyList<OptionSpec> Options {get;}=[OptionSpec.Text("base","Base currency","USD","What to show"),OptionSpec.Lines("targets","Target currencies","EUR\nGBP\nJPY\nCAD","What to show"),OptionSpec.Number("amount","Amount",100,0,1000000000,"What to show"),OptionSpec.Toggle("inverse","Show the reverse rate",false,"What to show"),OptionSpec.Number("decimals","Decimal places",2,0,6,"Layout"),OptionSpec.Toggle("symbols","Show currency codes",true,"Layout"),OptionSpec.Toggle("dividers","Row dividers",true,"Layout"),OptionSpec.Color("valueColor","Converted amount color","","Colors"),OptionSpec.Number("refresh","Refresh every (hours)",6,6,168,"Behavior"),OptionSpec.Toggle("showUpdated","Show update time",false,"Behavior")];
    public IEnumerable<Action<LauncherItem>> Samples=>[item=>Fill(item),item=>{Fill(item);item.Options["amount"]="1";item.Options["inverse"]="true";item.Options["decimals"]="4";},item=>{Fill(item);item.Options[OnlineWidget.CacheError]="Couldn't connect.";}];
    static string Code(string s)=>s.Trim().ToUpperInvariant();
    static void Fill(LauncherItem i)=>OnlineWidget.Fill(i,"{\"rates\":{\"USD\":1,\"EUR\":0.92,\"GBP\":0.77,\"JPY\":147.3,\"CAD\":1.37}}","USD");
    static OnlineSource Source(WidgetContext c){string code=Code(c.Get("base"));return new(code,TimeSpan.FromHours(Math.Max(6,c.GetNumber("refresh"))),async()=>{if(code.Length!=3||!code.All(char.IsAsciiLetter))throw new FormatException("Use a three-letter currency code.");var rates=CurrencyLogic.Parse(await OnlineWidget.GetAsync("https://open.er-api.com/v6/latest/"+code));return JsonSerializer.Serialize(new{rates});});}
    public IEnumerable<WidgetAction> Actions(WidgetContext c)=>[OnlineWidget.RefreshAction(c,Source(c))];
    public FrameworkElement Build(WidgetContext c)
    {
        OnlineWidget.Keep(c,Source(c));var (card,body)=WidgetKit.Frame(c);OnlineWidget.AddFooter(c,body,c.GetBool("showUpdated"),"Rates by Exchange Rate API","https://www.exchangerate-api.com");
        Dictionary<string,double> rates;try{rates=CurrencyLogic.Parse(OnlineWidget.Cached(c));}catch(Exception e)when(e is JsonException or FormatException){body.Children.Add(OnlineWidget.Waiting(c));return card;}
        var stack=new StackPanel();var header=WidgetKit.Text(c,OnlineLogic.Number(c.GetNumber("amount"),c.GetInt("decimals"),false)+" "+Code(c.Get("base")),22,bold:true);header.Margin=new Thickness(0,0,0,10);stack.Children.Add(header);
        foreach(var target in c.GetLines("targets").Select(Code).Distinct().Take(30))
        {
            var row=new Grid{Margin=new Thickness(0,5,0,5)};row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.Children.Add(WidgetKit.Muted(c,target));
            string value=rates.TryGetValue(target,out double rate)?OnlineLogic.Number(CurrencyLogic.Convert(c.GetNumber("amount"),rate,c.GetBool("inverse")),c.GetInt("decimals"),false):"Unavailable";
            var text=WidgetKit.Text(c,value+(c.GetBool("symbols")?" "+(c.GetBool("inverse")?Code(c.Get("base")):target):""),14,c.GetColor("valueColor"),true);Grid.SetColumn(text,1);row.Children.Add(text);stack.Children.Add(row);if(c.GetBool("dividers"))stack.Children.Add(WidgetKit.Divider());
        }
        if(c.GetBool("inverse"))stack.Children.Add(WidgetKit.Muted(c,"Inverse: target → "+Code(c.Get("base")),10));body.Children.Add(WidgetKit.Scroll(stack));return card;
    }
}
