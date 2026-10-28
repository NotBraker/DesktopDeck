using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using DesktopDeck.Models;
namespace DesktopDeck.Widgets;
internal sealed class TopAppsWidget:IWidget
{
    public string Kind=>"top-apps";public string Name=>"Top apps";public string Description=>"Largest running apps by memory or processor use";public string Section=>"System";public string Glyph=>"";public string Accent=>"#f472b6";
    public (double Width,double Height) DefaultSize=>(324,276);public (double Width,double Height) MinimumSize=>(180,132);
    public IReadOnlyList<OptionSpec> Options{get;}=[OptionSpec.Number("count","Top apps shown",5,1,20,"What to show"),OptionSpec.Toggle("cpu","Show CPU percentage",true,"What to show"),OptionSpec.Toggle("instances","Show number of processes",true,"What to show"),OptionSpec.Toggle("bars","Memory bars",true,"What to show"),OptionSpec.Toggle("icons","App icons when available",true,"What to show"),OptionSpec.Lines("exclude","Excluded processes",TopAppsLogic.DefaultExclude,"What to show"),OptionSpec.Choice("sort","Rank by","memory",["memory","cpu"],null,"Behavior"),OptionSpec.Toggle("group","Combine apps with the same name",true,"Behavior"),OptionSpec.Toggle("private","Count only private memory",false,"Behavior"),OptionSpec.Number("interval","Refresh every (seconds)",5,3,30,"Behavior"),OptionSpec.Choice("cpuScale","CPU scale","total",["total","core"],null,"Behavior"),OptionSpec.Color("barColor","Memory bar color","","Colors")];
    public IEnumerable<Action<LauncherItem>> Samples=>[_=>{},i=>{i.Options["sort"]="cpu";i.Options["bars"]="false";i.Options["count"]="7";i.Height=324;},i=>{i.Options["group"]="false";i.Options["cpu"]="false";}];
    public FrameworkElement Build(WidgetContext ctx)
    {
        var(card,body)=WidgetKit.Frame(ctx);int interval=ctx.GetInt("interval");string key=$"apps/{ctx.Item.Id}/{interval}/{ctx.GetBool("private")}/{ctx.Get("cpuScale")}";
        var cache=ctx.IsSnapshot?null:SystemRefresh.Bind(ctx,key,interval,()=>{var sampler=SystemSamplers.Shared(key+"/sampler",()=>new SystemSamplers.Processes());return sampler.Sample(ctx.GetBool("private"),ctx.Get("cpuScale"),false);});
        var data=ctx.IsSnapshot?TopAppsLogic.FakeProcesses():cache?.Value;if(data is null){body.Children.Add(WidgetKit.Empty(ctx,"Sampling running apps…"));return card;}
        var apps=TopAppsLogic.Top(data,ctx.GetBool("group"),ctx.Get("sort"),ctx.GetInt("count"),ctx.GetLines("exclude"));
        if(apps.Count==0){body.Children.Add(WidgetKit.Empty(ctx,"No matching apps"));return card;}var list=new StackPanel();int cap=Math.Max(1,(int)((ctx.Height-58)/(ctx.GetBool("bars")?40:29)));long largest=System.Linq.Enumerable.Max(apps,a=>a.Memory);foreach(var a in apps.GetRange(0,Math.Min(cap,apps.Count)))
        {
            var texts=new StackPanel();string count=ctx.GetBool("instances")&&a.Count>1?$" ({a.Count})":"";var name=WidgetKit.Text(ctx,a.Name+count,12,bold:true);string suffix=ctx.GetBool("cpu")&&ctx.Width>=228?(a.Cpu<0?" · …":$" · {a.Cpu:F1}%"):"";var value=WidgetKit.Muted(ctx,SystemStatsLogic.FormatBytes(a.Memory)+suffix,11);var row=SystemKit.Split(name,value);texts.Children.Add(row);
            if(ctx.GetBool("bars")){var bar=WidgetKit.Bar(largest>0?(double)a.Memory/largest:0,ctx.GetColor("barColor"),3);bar.Margin=new Thickness(0,5,0,0);texts.Children.Add(bar);}texts.Margin=new Thickness(0,0,0,10);
            string detail=$"{a.Name} · {a.Count} process(es)\nMemory: {SystemStatsLogic.FormatBytes(a.Memory)}\nCPU: {(a.Cpu<0?"Waiting for next sample":a.Cpu.ToString("F1")+"%")}";texts.ToolTip=detail;
            // Shell icon retrieval is dispatched separately; Build only uses cached images.
            if(ctx.GetBool("icons")&&!ctx.IsSnapshot&&a.Path is {} path){string iconKey="app-icon/"+path;if(WidgetContext.Cache.TryGetValue(iconKey,out var cached)&&cached is System.Windows.Media.ImageSource icon){var dock=new DockPanel();var img=new Image{Source=icon,Width=20,Height=20,Margin=new Thickness(0,0,8,0)};dock.Children.Add(img);dock.Children.Add(texts);dock.ToolTip=detail;list.Children.Add(dock);}else{list.Children.Add(texts);if(!WidgetContext.Cache.ContainsKey(iconKey))ctx.RunOnce(iconKey,async()=>{var icon=await System.Threading.Tasks.Task.Run(()=>ImageCache.ShellIcon(path,32));WidgetContext.Cache[iconKey]=icon is null?new object():icon;await ctx.Dispatcher.InvokeAsync(()=>ctx.Rebuild());});}}else list.Children.Add(texts);
            texts.MouseLeave+=(_,_)=>{if(texts.ToolTip is ToolTip open){open.IsOpen=false;texts.ToolTip=detail;}};
            texts.MouseLeftButtonUp+=(_,e)=>{e.Handled=true;if(texts.ToolTip is string tip){var tooltip=new ToolTip{Content=tip,PlacementTarget=texts,IsOpen=true};texts.ToolTip=tooltip;}};
        }body.Children.Add(list);return card;
    }
}

