using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DesktopDeck.Models;
namespace DesktopDeck.Widgets;
internal sealed class AlarmsWidget : IWidget
{
    public string Kind => "alarms"; public string Name => "Alarms"; public string Description => "Scheduled reminders with snooze and a next-alarm countdown"; public string Section => "Time"; public string Glyph => ""; public string Accent => "#f59e0b";
    public (double Width,double Height) DefaultSize => (324,276); public (double Width,double Height) MinimumSize => (180,132);
    public IReadOnlyList<OptionSpec> Options { get; } = [
        OptionSpec.Lines("alarms","Alarms","09:00 | Mon,Tue,Wed,Thu,Fri | Morning planning\n14:30 | daily | Take a break","What to show","One per line: time | daily, or days like Mon,Tue | label. Times use this PC's clock."),
        OptionSpec.Toggle("enabled","Enable reminders",true,"Behavior"), OptionSpec.Toggle("sound","Play a sound",false,"Behavior"), OptionSpec.Number("snooze","Snooze (minutes)",10,1,120,"Behavior"),
        OptionSpec.Toggle("countdown","Countdown to next alarm",true,"What to show"),OptionSpec.Toggle("past","Show past alarms today",false,"What to show"),OptionSpec.Toggle("days","Show repeat days",true,"What to show"),
        OptionSpec.Choice("clock","Clock","24",["12","24"],null,"Layout"),OptionSpec.Number("rows","Most alarms shown",6,1,20,"Layout"),OptionSpec.Color("nextColor","Next reminder color","","Colors"),OptionSpec.Toggle("dividers","Row dividers",true,"Layout") ];
    public IEnumerable<Action<LauncherItem>> Samples => [ _ => {}, i => { i.Options["past"]="true"; i.Options["clock"]="12"; i.Options["alarms"]="08:00 | daily | Stretch\n12:00 | daily | Lunch\n18:30 | daily | Evening walk"; }, i => {i.Options["enabled"]="false"; i.Options["alarms"]="10:00 | Sat,Sun | Weekend reading";} ];
    public FrameworkElement Build(WidgetContext ctx)
    {
        var now=DateTime.Now; var (alarms,invalid)=AlarmsLogic.Parse(ctx.GetLines("alarms")); bool enabled=ctx.GetBool("enabled");
        var (card,body)=WidgetKit.Frame(ctx,ctx.Width>=228?WidgetKit.Pill(ctx,enabled?"Enabled":"Paused"):null,WidgetKit.IconButton(enabled?Theme.Glyphs.Pause:Theme.Glyphs.Play,"Enable / pause reminders",()=>ctx.Set("enabled",!enabled)));
        var list=new StackPanel(); var next=alarms.OrderBy(a=>AlarmsLogic.Next(a,now)).FirstOrDefault();
        var nextLabel=WidgetKit.Muted(ctx,next?.Label??"No alarms scheduled");
        var countdown=WidgetKit.Text(ctx,"",ctx.Width<228?17:23,ctx.GetColor("nextColor"),true);
        if(ctx.GetBool("countdown")){list.Children.Add(countdown);list.Children.Add(nextLabel);}
        if(ctx.Height>=180) foreach(var a in alarms.OrderBy(a=>AlarmsLogic.Next(a,now)).Take(ctx.GetInt("rows")))
        {
            var at=ctx.GetBool("past")?now.Date+a.Time:AlarmsLogic.Next(a,now); var row=new StackPanel {Margin=new Thickness(0,5,0,3)};
            row.Children.Add(WidgetKit.Text(ctx,at.ToString(ctx.Get("clock")=="12"?"h:mm tt":"HH:mm",CultureInfo.CurrentCulture)+(at.Date!=now.Date?" "+at.ToString("ddd"):"")+"  ·  "+a.Label,12.5));
            if(ctx.GetBool("days"))row.Children.Add(WidgetKit.Muted(ctx,string.Join(", ",a.Days.Select(d=>d.ToString()[..3])),10.5));
            row.ToolTip=a.Label; list.Children.Add(row); if(ctx.GetBool("dividers"))list.Children.Add(WidgetKit.Divider());
        }
        if(invalid.Count>0 && ctx.Height>=180)list.Children.Add(WidgetKit.Text(ctx,$"{invalid.Count} invalid alarm line(s)",11,Theme.Danger));
        if(ctx.Item.Options.ContainsKey("data.lastLabel") && ctx.Height>=180)list.Children.Add(WidgetKit.Button("Snooze last reminder",()=>ctx.Set("data.snoozeAt",DateTimeOffset.Now.AddMinutes(ctx.GetInt("snooze")).ToString("O",CultureInfo.InvariantCulture))));
        body.Children.Add(WidgetKit.Scroll(list));
        void Update(DateTime at){next=alarms.OrderBy(a=>AlarmsLogic.Next(a,at)).FirstOrDefault();nextLabel.Text=next?.Label??"No alarms scheduled";countdown.Text=!enabled?"Reminders paused":next is null?"No alarms":Format(AlarmsLogic.Next(next,at)-at);}
        Update(now);
        // A single shared cursor prevents duplicate delivery on multiple surfaces; register on every build.
        string key="alarms/"+ctx.Item.Id; var cursor=ctx.IsSnapshot?new Cursor {Previous=now}: (Cursor)WidgetContext.Cache.GetOrAdd(key,_=>new Cursor {Previous=now.AddSeconds(-1)});
        if(cursor.Enabled!=enabled){cursor.Previous=now;cursor.Enabled=enabled;}
        ctx.Tick(()=>enabled,at=> {Update(at.LocalDateTime); if(!enabled)return; lock(cursor){var local=at.LocalDateTime;
            foreach(var alarm in alarms){string firedKey="data.fired."+alarm.Id; if(AlarmsLogic.Due(alarm,local,cursor.Previous,ctx.Get(firedKey))){ctx.Set(firedKey,(local.Date+alarm.Time).ToString("O",CultureInfo.InvariantCulture),false);Fire(alarm.Label);}}
            if(DateTimeOffset.TryParse(ctx.Get("data.snoozeAt"),out var snooze)&&at>=snooze){ctx.Set("data.snoozeAt","",false);Fire(ctx.Get("data.lastLabel"));}cursor.Previous=local; }});
        return card;
        void Fire(string label){ctx.Set("data.lastLabel",label,false);ctx.Notify("Reminder",label);if(ctx.GetBool("sound"))System.Media.SystemSounds.Asterisk.Play();}
    }
    private sealed class Cursor { public DateTime Previous; public bool Enabled; }
    private static string Format(TimeSpan t)=>t.TotalDays>=1?$"{(int)t.TotalDays}d {t.Hours}h":$"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
}

