using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using DesktopDeck.Models;
namespace DesktopDeck.Widgets;
internal sealed class DiskSpaceWidget:IWidget
{
    public string Kind=>"disk-space";public string Name=>"Drives";public string Description=>"Drive capacity and free space with fullness alerts";public string Section=>"System";public string Glyph=>"";public string Accent=>"#38bdf8";
    public (double Width,double Height) DefaultSize=>(324,276);public (double Width,double Height) MinimumSize=>(180,132);
    public IReadOnlyList<OptionSpec> Options{get;}=[OptionSpec.Toggle("removable","Include removable drives",false,"What to show"),OptionSpec.Toggle("network","Include network drives",false,"What to show"),OptionSpec.Lines("hidden","Hide drive letters or labels","","What to show"),OptionSpec.Toggle("label","Volume labels",true,"What to show"),OptionSpec.Choice("detail","Amounts","freeof",["free","used","both","percent","freeof","none"],null,"What to show"),OptionSpec.Choice("sort","Order","name",["name","fullest","emptiest","largest"],null,"Layout"),OptionSpec.Choice("style","Style","bars",["bars","rings"],null,"Layout"),OptionSpec.Number("thickness","Bar thickness",7,3,16,"Layout"),OptionSpec.Number("warn","Warning when fuller than (%)",80,10,100,"Behavior"),OptionSpec.Number("critical","Critical when fuller than (%)",95,10,100,"Behavior"),OptionSpec.Color("fillColor","Normal color","","Colors"),OptionSpec.Color("warnColor","Warning color","#f59e0b","Colors"),OptionSpec.Color("critColor","Critical color","#ef4444","Colors")];
    public IEnumerable<Action<LauncherItem>> Samples=>[_=>{},i=>{i.Options["style"]="rings";i.Options["detail"]="both";},i=>{i.Options["removable"]="true";i.Options["network"]="true";i.Options["hidden"]="E";i.Options["sort"]="fullest";i.Height=372;}];
    public FrameworkElement Build(WidgetContext ctx)
    {
        var(card,body)=WidgetKit.Frame(ctx);bool removable=ctx.GetBool("removable"),network=ctx.GetBool("network");
        var cache=ctx.IsSnapshot?null:SystemRefresh.Bind(ctx,$"drives/{ctx.Item.Id}/{removable}/{network}",60,()=>SystemSamplers.Drives.ReadAll(removable,network));
        var data=ctx.IsSnapshot?DiskSpaceLogic.FakeDrives():cache?.Value;
        if(data is null){body.Children.Add(WidgetKit.Empty(ctx,cache?.Error.Length>0?"Unable to read drives":"Reading drives…"));return card;}
        var shown=DiskSpaceLogic.Arrange(data,ctx.Get("sort"),ctx.GetLines("hidden"),removable,network);if(shown.Count==0){body.Children.Add(WidgetKit.Empty(ctx,"No drives match your settings"));return card;}
        var list=new StackPanel();int rows=Math.Max(1,(int)((ctx.Height-58)/(ctx.Get("style")=="rings"?68:61)));
        foreach(var d in shown.GetRange(0,Math.Min(rows,shown.Count)))
        {
            var fill=SystemKit.LevelBrush(ctx,SystemStatsLogic.Level(d.UsedFraction*100,ctx.GetNumber("warn"),ctx.GetNumber("critical")),ctx.GetColor("fillColor"));
            var texts=new StackPanel();texts.Children.Add(WidgetKit.Text(ctx,DiskSpaceLogic.Title(d,ctx.GetBool("label")),13,bold:true));texts.Children.Add(WidgetKit.Muted(ctx,DiskSpaceLogic.Detail(d,ctx.Get("detail")),ctx.Width<228?10:11));
            FrameworkElement row;if(ctx.Get("style")=="rings"){var dock=new DockPanel();var center=WidgetKit.Text(ctx,$"{d.UsedFraction:P0}",11);center.HorizontalAlignment=HorizontalAlignment.Center;var ring=WidgetKit.Ring(d.UsedFraction,fill,49,5,center);ring.Margin=new Thickness(0,0,10,0);dock.Children.Add(ring);dock.Children.Add(texts);row=dock;}else{var bar=WidgetKit.Bar(d.UsedFraction,fill,ctx.GetNumber("thickness"));bar.Margin=new Thickness(0,6,0,0);texts.Children.Add(bar);row=texts;}
            row.Margin=new Thickness(0,0,0,12);list.Children.Add(WidgetKit.Clickable(row,()=>ctx.Open(d.Name+"\\"),$"{d.Name} · {SystemStatsLogic.FormatBytes(d.Used)} used; {SystemStatsLogic.FormatBytes(d.Free)} free; {SystemStatsLogic.FormatBytes(d.Total)} total"));
        }
        if(shown.Count>rows&&ctx.Height>=180)list.Children.Add(WidgetKit.Muted(ctx,$"+{shown.Count-rows} more · enlarge to see all",10));body.Children.Add(list);return card;
    }
}
