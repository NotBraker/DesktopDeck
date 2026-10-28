using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using DesktopDeck.Models;
namespace DesktopDeck.Widgets;
internal sealed class PcInfoWidget:IWidget
{
    public string Kind=>"pc-info";public string Name=>"PC info";public string Description=>"Computer details, Windows build and uptime";public string Section=>"System";public string Glyph=>"";public string Accent=>"#a78bfa";
    public (double Width,double Height) DefaultSize=>(324,324);public (double Width,double Height) MinimumSize=>(180,132);
    public IReadOnlyList<OptionSpec> Options{get;}=[OptionSpec.Toggle("computer","Computer name",true,"What to show"),OptionSpec.Toggle("user","User name",false,"What to show"),OptionSpec.Toggle("uptime","Uptime",true,"What to show"),OptionSpec.Toggle("boot","Boot time",true,"What to show"),OptionSpec.Toggle("windows","Windows version",true,"What to show"),OptionSpec.Toggle("build","Windows build",true,"What to show"),OptionSpec.Toggle("cpu","Processor",true,"What to show"),OptionSpec.Toggle("ram","Installed memory",true,"What to show"),OptionSpec.Toggle("screens","Screen count",true,"What to show"),OptionSpec.Choice("layout","Layout","list",["list","compact"],null,"Layout"),OptionSpec.Choice("uptimeStyle","Uptime format","short",["short","long","clock"],null,"Layout"),OptionSpec.Toggle("shortCpu","Short processor name",true,"Layout"),OptionSpec.Color("valueColor","Value color","","Colors")];
    public IEnumerable<Action<LauncherItem>> Samples=>[_=>{},i=>{i.Options["layout"]="compact";i.Options["user"]="true";i.Options["uptimeStyle"]="long";},i=>{i.Options["windows"]="false";i.Options["build"]="false";i.Options["boot"]="false";i.Height=228;}];
    public FrameworkElement Build(WidgetContext ctx)
    {
        var(card,body)=WidgetKit.Frame(ctx);var cache=ctx.IsSnapshot?null:SystemRefresh.Bind(ctx,$"pc/{ctx.Item.Id}",300,SystemSamplers.PcFacts.ReadNow);
        var f=ctx.IsSnapshot?new SystemSamplers.PcFacts("DESKTOP-STUDIO","Alex","Windows 11 Pro 24H2","26100.2033","AMD Ryzen 7 7800X3D 8-Core Processor",8,16,32d*1024*1024*1024,2,"64-bit (x64)",DateTimeOffset.Now):cache?.Value;
        if(f is null){body.Children.Add(WidgetKit.Empty(ctx,"Reading PC details…"));return card;}
        var uptime=ctx.IsSnapshot?TimeSpan.FromDays(3)+TimeSpan.FromHours(4)+TimeSpan.FromMinutes(12):TimeSpan.FromMilliseconds(Environment.TickCount64);
        var rows=new List<(string Key,string Label,string Value)>{("computer","Computer",f.Computer),("user","User",f.User),("uptime","Uptime",PcInfoLogic.Uptime(uptime,ctx.Get("uptimeStyle"))),("boot","Booted",(DateTimeOffset.Now-uptime).ToString("ddd d MMM, HH:mm")),("windows","Windows",f.Windows),("build","Build",f.Build),("cpu","CPU",PcInfoLogic.CpuName(f.Cpu,ctx.GetBool("shortCpu"))),("ram","Memory",SystemStatsLogic.FormatBytes(f.Ram)),("screens","Displays",PcInfoLogic.Screens(f.Screens))};
        bool compact=ctx.Get("layout")=="compact";var list=new StackPanel();int capacity=Math.Max(1,(int)((ctx.Height-58)/(compact?38:27))),count=0;
        foreach(var r in rows){if(!ctx.GetBool(r.Key)||count++>=capacity)continue;var value=WidgetKit.Text(ctx,r.Value,12,ctx.GetColor("valueColor",ctx.TextBrush));value.ToolTip=r.Value;
            FrameworkElement row;if(compact){var s=new StackPanel();s.Children.Add(WidgetKit.Muted(ctx,r.Label,10));s.Children.Add(value);row=s;}else{var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(ctx.Width<228?58:76)});grid.ColumnDefinitions.Add(new ColumnDefinition());grid.Children.Add(WidgetKit.Muted(ctx,r.Label,11));Grid.SetColumn(value,1);grid.Children.Add(value);row=grid;}row.Margin=new Thickness(0,0,0,9);list.Children.Add(row);}
        if(count==0)list.Children.Add(WidgetKit.Empty(ctx,"Choose details in Customize…"));body.Children.Add(list);
        // Facts are slow; uptime labels are rebuilt at most once a minute, independent of other widgets' ticks.
        var built=DateTimeOffset.Now;ctx.Tick(()=>false,now=>{if((now-built).TotalSeconds>=60)ctx.Rebuild();});return card;
    }
}
