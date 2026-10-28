using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopDeck.Models;
using Shapes=System.Windows.Shapes;
namespace DesktopDeck.Widgets;
internal sealed class AnalogClockWidget:IWidget
{
    public string Kind=>"analog-clock"; public string Name=>"Analog clock";public string Description=>"A customizable clock face with local or world time";public string Section=>"Time";public string Glyph=>"";public string Accent=>"#6c63ff";
    public (double Width,double Height) DefaultSize=>(228,276);public (double Width,double Height) MinimumSize=>(180,180);
    public IReadOnlyList<OptionSpec> Options {get;}=[OptionSpec.Choice("style","Face style","ticks",["minimal","classic","ticks","roman"],null,"Layout"),OptionSpec.Toggle("seconds","Second hand",true,"What to show"),OptionSpec.Toggle("smooth","Smooth hands",true,"Behavior","Hands move between ticks instead of jumping."),OptionSpec.Toggle("digital","Digital time",true,"What to show"),OptionSpec.Toggle("date","Date",true,"What to show"),OptionSpec.Text("zone","Time zone","Local","Behavior","Local, UTC or a time zone name such as Pacific Standard Time."),OptionSpec.Choice("clock","Digital clock","24",["12","24"],null,"Layout"),OptionSpec.Color("faceColor","Face color","","Colors"),OptionSpec.Color("handColor","Hands color","","Colors"),OptionSpec.Color("secondColor","Second hand color","","Colors"),OptionSpec.Color("markColor","Marks color","","Colors")];
    public IEnumerable<Action<LauncherItem>> Samples=>[_=>{},i=>{i.Options["style"]="roman";i.Options["zone"]="Tokyo Standard Time";i.Options["clock"]="12";i.Accent="#C4684F";},i=>{i.Options["style"]="minimal";i.Options["seconds"]="false";i.Options["digital"]="false";i.Options["date"]="false";i.Width=180;i.Height=180;}];
    public FrameworkElement Build(WidgetContext ctx)
    {
        var(card,body)=WidgetKit.Frame(ctx);var zone=WorldClocksLogic.Resolve(ctx.Get("zone"));if(zone is null){body.Children.Add(WidgetKit.Empty(ctx,"Unknown time zone"));return card;}
        double available=ctx.Height-2*ctx.GetNumber("_padding")-(ctx.Item.ShowHeader?34:0);bool digital=ctx.GetBool("digital")&&available>=160,date=ctx.GetBool("date")&&available>=185;
        double size=Math.Max(60,Math.Min(ctx.Width-2*ctx.GetNumber("_padding")-4,available-(digital?29:0)-(date?20:0)));
        var stack=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};var face=new Canvas{Width=size,Height=size};double c=size/2;
        face.Children.Add(new Shapes.Ellipse{Width=size,Height=size,Fill=ctx.GetColor("faceColor",Theme.Surface3),Stroke=Theme.LineStrong,StrokeThickness=1});
        string style=ctx.Get("style");string[] roman=["XII","I","II","III","IV","V","VI","VII","VIII","IX","X","XI"];
        for(int i=0;i<60;i++){double a=i*Math.PI/30;bool major=i%5==0;if(style is "classic" or "roman"){if(!major)continue;int n=i/5;var label=WidgetKit.Text(ctx,style=="roman"?roman[n]: (n==0?12:n).ToString(CultureInfo.InvariantCulture),size*.075,ctx.GetColor("markColor",Theme.Muted));label.Width=size*.2;label.TextAlignment=TextAlignment.Center;Canvas.SetLeft(label,c+Math.Sin(a)*size*.36-size*.1);Canvas.SetTop(label,c-Math.Cos(a)*size*.36-size*.055);face.Children.Add(label);}else if(style!="minimal"||major){double outer=size*.44,inner=outer-size*(major?.055:.022);face.Children.Add(new Shapes.Line{X1=c+Math.Sin(a)*inner,Y1=c-Math.Cos(a)*inner,X2=c+Math.Sin(a)*outer,Y2=c-Math.Cos(a)*outer,Stroke=ctx.GetColor("markColor",Theme.Muted),StrokeThickness=major?2:1});}}
        Shapes.Line Hand(double length,double thickness,Brush brush){var hand=new Shapes.Line{X1=c,Y1=c+size*.06,X2=c,Y2=c-length,Stroke=brush,StrokeThickness=thickness,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,RenderTransform=new RotateTransform(0,c,c)};face.Children.Add(hand);return hand;}
        var hour=Hand(size*.24,Math.Max(3,size*.025),ctx.GetColor("handColor",ctx.TextBrush));var minute=Hand(size*.35,Math.Max(2,size*.018),ctx.GetColor("handColor",ctx.TextBrush));var second=Hand(size*.39,1.5,ctx.GetColor("secondColor"));second.Visibility=ctx.GetBool("seconds")?Visibility.Visible:Visibility.Collapsed;
        var hub=new Shapes.Ellipse{Width=7,Height=7,Fill=ctx.GetColor("secondColor")};Canvas.SetLeft(hub,c-3.5);Canvas.SetTop(hub,c-3.5);face.Children.Add(hub);stack.Children.Add(face);
        var time=WidgetKit.Text(ctx,"",18,bold:true);time.HorizontalAlignment=HorizontalAlignment.Center;time.Margin=new Thickness(0,7,0,0);if(digital)stack.Children.Add(time);var day=WidgetKit.Muted(ctx,"",11);day.HorizontalAlignment=HorizontalAlignment.Center;if(date)stack.Children.Add(day);body.Children.Add(stack);
        void Update(DateTimeOffset now){var t=TimeZoneInfo.ConvertTime(now,zone).DateTime;var angles=AnalogClockLogic.Angles(t,ctx.GetBool("smooth"));((RotateTransform)hour.RenderTransform).Angle=angles.Hour;((RotateTransform)minute.RenderTransform).Angle=angles.Minute;((RotateTransform)second.RenderTransform).Angle=angles.Second;time.Text=t.ToString(ctx.Get("clock")=="12"?"h:mm tt":"HH:mm",CultureInfo.CurrentCulture);day.Text=t.ToString("ddd, d MMM",CultureInfo.CurrentCulture);}
        Update(DateTimeOffset.Now);ctx.Tick(()=>ctx.GetBool("seconds"),Update);return card;
    }
}
