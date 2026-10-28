using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using DesktopDeck.Models;
namespace DesktopDeck.Widgets;
internal sealed class NetworkSpeedWidget:IWidget
{
    public string Kind=>"network-speed";public string Name=>"Network speed";public string Description=>"Download and upload rates with adapter details and history";public string Section=>"System";public string Glyph=>"";public string Accent=>"#38bdf8";
    public (double Width,double Height) DefaultSize=>(324,228);public (double Width,double Height) MinimumSize=>(180,132);
    public IReadOnlyList<OptionSpec> Options{get;}=[OptionSpec.Text("adapter","Network adapter","all","Behavior","all, an adapter name, or part of its description."),OptionSpec.Toggle("virtual","Include VPN and virtual connections",false,"Behavior"),OptionSpec.Number("interval","Update every (seconds)",1,1,10,"Behavior"),OptionSpec.Number("history","History length (readings)",40,10,120,"Behavior"),OptionSpec.Choice("units","Units","auto",["auto","KB","Mbps","bits"],null,"Layout"),OptionSpec.Toggle("upload","Upload speed",true,"What to show"),OptionSpec.Toggle("graph","History graph",true,"What to show"),OptionSpec.Toggle("ip","Local IP address",true,"What to show"),OptionSpec.Toggle("name","Adapter name",true,"What to show"),OptionSpec.Toggle("totals","Totals since start",false,"What to show"),OptionSpec.Color("downColor","Download color","","Colors"),OptionSpec.Color("upColor","Upload color","#a78bfa","Colors")];
    public IEnumerable<Action<LauncherItem>> Samples=>[_=>{},i=>{i.Options["units"]="Mbps";i.Options["totals"]="true";i.Height=276;},i=>{i.Options["data.sample.down"]="0";i.Options["data.sample.up"]="0";i.Options["data.sample.name"]="No connected adapters";}];
    private sealed record Reading(double Down,double Up,long TotalDown,long TotalUp,string Name,string Ip,double[] DownHistory,double[] UpHistory);
    public FrameworkElement Build(WidgetContext ctx)
    {
        var(card,body)=WidgetKit.Frame(ctx);string adapter=ctx.Get("adapter");bool virtuals=ctx.GetBool("virtual");int interval=ctx.GetInt("interval"),history=ctx.GetInt("history");string key=$"network/{ctx.Item.Id}/{adapter}/{virtuals}/{interval}";
        var cache=ctx.IsSnapshot?null:SystemRefresh.Bind(ctx,key,interval,()=>{var n=SystemSamplers.Shared(key+"/sampler",()=>new SystemSamplers.Network(adapter,virtuals,interval));n.Update(DateTimeOffset.Now,history);return new Reading(n.Down,n.Up,n.TotalDown,n.TotalUp,n.Adapter,n.Ip,n.DownHistory.ToArray(),n.UpHistory.ToArray());});
        var r=ctx.IsSnapshot?new Reading(SystemKit.SampleNumber(ctx,"down",3.4*1024*1024),SystemKit.SampleNumber(ctx,"up",240*1024),1200L*1024*1024,83L*1024*1024,SystemKit.SampleText(ctx,"name","Wi-Fi"),"192.0.2.24",NetworkSpeedLogic.FakeSpeeds(history,6*1024*1024,4),NetworkSpeedLogic.FakeSpeeds(history,500*1024,8)):cache?.Value;
        if(r is null){body.Children.Add(WidgetKit.Empty(ctx,"Reading network interfaces…"));return card;}
        var stack=new StackPanel();var speeds=new Grid();speeds.ColumnDefinitions.Add(new ColumnDefinition());if(ctx.GetBool("upload"))speeds.ColumnDefinitions.Add(new ColumnDefinition());
        void Speed(string label,double value,string color,int col){var s=new StackPanel();var parts=NetworkSpeedLogic.RateParts(value,ctx.Get("units"));s.Children.Add(WidgetKit.Muted(ctx,label,11));s.Children.Add(WidgetKit.Text(ctx,parts.Value,ctx.Width<228?24:34,ctx.GetColor(color),true));s.Children.Add(WidgetKit.Muted(ctx,parts.Unit,10));Grid.SetColumn(s,col);speeds.Children.Add(s);}
        Speed("↓ Download",r.Down,"downColor",0);if(ctx.GetBool("upload"))Speed("↑ Upload",r.Up,"upColor",1);stack.Children.Add(speeds);
        if(ctx.Height>=180){if(ctx.GetBool("graph")){var graph=new SystemKit.Sparkline{Height=Math.Min(55,Math.Max(18,ctx.Height-173)),Capacity=history,Stroke=ctx.GetColor("downColor"),SecondStroke=ctx.GetBool("upload")?ctx.GetColor("upColor"):null,AutoMax=NetworkSpeedLogic.GraphMax,Margin=new Thickness(0,5,0,7)};graph.Set(r.DownHistory,ctx.GetBool("upload")?r.UpHistory:null);stack.Children.Add(graph);}if(ctx.GetBool("name"))stack.Children.Add(WidgetKit.Muted(ctx,r.Name.Length==0?"No connected adapters":r.Name,11));if(ctx.GetBool("ip")&&r.Ip.Length>0)stack.Children.Add(WidgetKit.Muted(ctx,r.Ip,10.5));if(ctx.GetBool("totals")&&ctx.Height>=276)stack.Children.Add(WidgetKit.Muted(ctx,$"Session ↓ {SystemStatsLogic.FormatBytes(r.TotalDown)} · ↑ {SystemStatsLogic.FormatBytes(r.TotalUp)}",10.5));}
        body.Children.Add(stack);return card;
    }
}
