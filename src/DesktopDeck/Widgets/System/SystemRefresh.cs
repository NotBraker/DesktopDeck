using System;
using System.Threading.Tasks;
namespace DesktopDeck.Widgets;
// One asynchronous read shared by every surface; timestamps survive rebuilds.
internal static class SystemRefresh
{
    internal sealed class Entry<T> where T:class {public T? Value;public DateTimeOffset Attempt;public bool Busy;public int Version;public string Error="";}
    public static Entry<T> Bind<T>(WidgetContext ctx,string key,int interval,Func<T> read) where T:class
    {
        var entry=SystemSamplers.Shared(key,()=>new Entry<T>());
        void Refresh(DateTimeOffset now)
        {
            lock(entry){if(entry.Busy||(now-entry.Attempt).TotalSeconds<interval)return;entry.Busy=true;entry.Attempt=now;}
            ctx.RunOnce("read/"+key,async()=>{
                T? value=null;string error="";try{value=await Task.Run(read);}catch(Exception ex){error=ex.Message;}
                lock(entry){if(value is not null)entry.Value=value;entry.Error=error;entry.Busy=false;entry.Version++;}
                await ctx.Dispatcher.InvokeAsync(()=>ctx.Rebuild());
            });
        }
        int version=entry.Version;
        ctx.Tick(()=>interval<60,now=>{Refresh(now);if(version!=entry.Version){version=entry.Version;ctx.Rebuild();}});
        Refresh(DateTimeOffset.Now);return entry;
    }
}

