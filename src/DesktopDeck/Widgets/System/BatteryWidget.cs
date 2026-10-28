using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using DesktopDeck.Models;
namespace DesktopDeck.Widgets;
internal sealed class BatteryWidget:IWidget
{
    public string Kind=>"battery";public string Name=>"Battery & power";public string Description=>"Battery charge, remaining time and AC power status";public string Section=>"System";public string Glyph=>"";public string Accent=>"#22c55e";
    public (double Width,double Height) DefaultSize=>(228,276);public (double Width,double Height) MinimumSize=>(180,132);
    public IReadOnlyList<OptionSpec> Options{get;}=[OptionSpec.Choice("style","Style","ring",["ring","bar"],null,"Layout"),OptionSpec.Toggle("time","Estimated time left",true,"What to show"),OptionSpec.Toggle("state","Charging status",true,"What to show"),OptionSpec.Toggle("plan","Power plan",true,"What to show"),OptionSpec.Toggle("saver","Battery saver",true,"What to show"),OptionSpec.Toggle("health","Battery health",false,"What to show"),OptionSpec.Number("low","Low charge (%)",25,1,100,"Behavior"),OptionSpec.Number("critical","Critical charge (%)",10,1,100,"Behavior"),OptionSpec.Number("interval","Update every (seconds)",10,3,60,"Behavior"),OptionSpec.Color("chargeColor","Charge color","","Colors"),OptionSpec.Color("warnColor","Low color","#f59e0b","Colors"),OptionSpec.Color("critColor","Critical color","#ef4444","Colors")];
    public IEnumerable<Action<LauncherItem>> Samples=>[_=>{},i=>{i.Options["data.sample.percent"]="18";i.Options["style"]="bar";},i=>{i.Options["data.sample.desktop"]="true";}];
    public FrameworkElement Build(WidgetContext ctx)
    {
        var(card,body)=WidgetKit.Frame(ctx);var cache=ctx.IsSnapshot?null:SystemRefresh.Bind(ctx,$"power/{ctx.Item.Id}/{ctx.GetInt("interval")}/{ctx.GetBool("health")}",ctx.GetInt("interval"),()=>SystemSamplers.Power.ReadNow(ctx.GetBool("health")));
        var r=ctx.IsSnapshot?new BatteryLogic.Reading(SystemKit.SampleText(ctx,"desktop","")!="true",false,false,(int)SystemKit.SampleNumber(ctx,"percent",76),12540,false,"Balanced",Health:.91):cache?.Value;
        if(r is null){body.Children.Add(WidgetKit.Empty(ctx,"Reading power status…"));return card;}
        var stack=new StackPanel{VerticalAlignment=VerticalAlignment.Center};var color=SystemKit.LevelBrush(ctx,BatteryLogic.Level(r.Percent,ctx.GetNumber("low"),ctx.GetNumber("critical"),r.Charging),ctx.GetColor("chargeColor"));
        if(!r.HasBattery){stack.Children.Add(WidgetKit.Text(ctx,"AC power",ctx.Width<228?24:32,color,true));stack.Children.Add(WidgetKit.Muted(ctx,"No battery installed",12));}
        else if(ctx.Get("style")=="ring"&&ctx.Height>=228){var center=WidgetKit.Text(ctx,r.Percent<0?"—":r.Percent+"%",27,bold:true);center.HorizontalAlignment=HorizontalAlignment.Center;double size=Math.Min(ctx.Width-42,ctx.Height-145);var ring=WidgetKit.Ring(Math.Max(0,r.Percent)/100d,color,size,8,center);ring.HorizontalAlignment=HorizontalAlignment.Center;stack.Children.Add(ring);}else{stack.Children.Add(WidgetKit.Text(ctx,r.Percent<0?"—":r.Percent+"%",ctx.Height<180?25:42,color,true));var bar=WidgetKit.Bar(Math.Max(0,r.Percent)/100d,color,8);bar.Margin=new Thickness(0,5,0,8);stack.Children.Add(bar);}
        if(ctx.GetBool("state")){var state=WidgetKit.Text(ctx,BatteryLogic.State(r),12);state.Margin=new Thickness(0,6,0,0);stack.Children.Add(state);}
        if(ctx.Height>=228){if(ctx.GetBool("time")&&BatteryLogic.TimeText(r).Length>0)stack.Children.Add(WidgetKit.Muted(ctx,BatteryLogic.TimeText(r)));if(ctx.GetBool("plan")&&r.Plan.Length>0)stack.Children.Add(WidgetKit.Muted(ctx,r.Plan));if(ctx.GetBool("saver")&&r.Saver)stack.Children.Add(WidgetKit.Pill(ctx,"Battery saver"));if(ctx.GetBool("health")&&r.Health is {} h)stack.Children.Add(WidgetKit.Muted(ctx,$"Battery health {h:P0}"));}
        body.Children.Add(stack);return card;
    }
}
