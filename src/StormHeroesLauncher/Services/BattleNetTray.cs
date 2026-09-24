using System.Runtime.InteropServices;
using StormHeroesLauncher.WindowSupport;
namespace StormHeroesLauncher.Services;
public static class BattleNetTray
{
    public static bool Candidate(ExternalWindow w,IReadOnlySet<uint> pids,bool transient) =>
        w.Visible && w.Enabled && pids.Contains(w.Pid) && w.ProcessName.Equals("Battle.net",StringComparison.OrdinalIgnoreCase) &&
        w.Owner == IntPtr.Zero && (transient ? w.ClassName == "Qt5151QWindowIcon" : w.ClassName.StartsWith("Chrome_WidgetWin_",StringComparison.Ordinal));
    private static IReadOnlyList<ExternalWindow> Snapshot() => new NativeWindowManager().Snapshot("Battle.net");
    private static HashSet<uint> Owners() => DesktopProcessState.Find("Battle.net");
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<(IntPtr,uint),HideTrayAttempt> timings=new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(IntPtr,uint,string),byte> mainAttempts=new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(IntPtr,uint),DateTimeOffset> detectedAt=new();
    internal static void RecordDetection(ExternalWindow w,DateTimeOffset time) { if(detectedAt.Count<32) detectedAt.TryAdd((w.Handle,w.Pid),time); }
    internal static void ConfirmTiming(ExternalWindow w,bool visible,bool alive)
    {
        if(!visible && timings.TryRemove((w.Handle,w.Pid),out var attempt)) attempt.Observe(visible,alive);
    }
    internal static bool NativeAction(ExternalWindow target,bool hide,AppLogger? logger=null)
    {
        bool Valid(bool visible) => Snapshot().Any(w => w.Handle==target.Handle && w.Pid==target.Pid && w.ClassName==target.ClassName &&
            Candidate(visible ? w : w with {Visible=true},Owners(),hide));
        if(hide) return Valid(true) && ShowWindowAsync(target.Handle,0); // Qt remains Hide only.
        if(mainAttempts.Count>=32 || !mainAttempts.TryAdd((target.Handle,target.Pid,target.ClassName),0)) return false;
        DateTimeOffset detected=detectedAt.TryRemove((target.Handle,target.Pid),out var time) ? time : DateTimeOffset.UtcNow;
        var attempt=HideTrayAttempt.Run($"Battle.net main: PID={target.Pid} HWND=0x{target.Handle.ToInt64():X} Class={target.ClassName}",detected,
            ()=>Valid(true),()=>ShowWindowAsync(target.Handle,0),()=>Valid(false),
            ()=>PostMessage(target.Handle,0x10,IntPtr.Zero,IntPtr.Zero),message=>logger?.Write(message));
        if(timings.Count<32) timings[(target.Handle,target.Pid)]=attempt;
        return attempt.Posted;
    }
    public static void HideTransient(AppLogger logger)
    {
        try
        {
            foreach(var w in Snapshot().Where(w => Candidate(w,Owners(),true)))
            {
                bool accepted = NativeAction(w,true,logger);
                Log(logger,w,true,accepted ? "Requested" : "RejectedOrChanged",Snapshot().Any(n => n.Handle == w.Handle && n.Pid == w.Pid && n.Visible));
            }
        }
        catch(Exception ex) { logger.Write($"Battle.net transient window: Action=Hide Result=Unavailable Exception={ex.GetType().Name}"); }
    }
    public static Task<bool> CloseAsync(AppLogger logger,CancellationToken token) => ObserveCore(logger,Snapshot,Owners,
        w => NativeAction(w,false,logger),w => NativeAction(w,true,logger),t => Task.Delay(500,t),token);
    // Retained entry point for existing offline checks.
    public static Task<bool> CloseCore(AppLogger logger,Func<IReadOnlyList<ExternalWindow>> snapshot,Func<HashSet<uint>> owners,
        Func<ExternalWindow,bool> post,Func<CancellationToken,Task> wait,CancellationToken token) => ObserveCore(logger,snapshot,owners,post,_ => false,wait,token);
    public static async Task<bool> ObserveCore(AppLogger logger,Func<IReadOnlyList<ExternalWindow>> snapshot,Func<HashSet<uint>> owners,
        Func<ExternalWindow,bool> post,Func<ExternalWindow,bool> hide,Func<CancellationToken,Task> wait,CancellationToken token)
    {
        var attempted = new HashSet<(IntPtr,uint,string)>();
        // Six observation intervals, three seconds total. One action per identity per checkpoint.
        for(int sample=0;sample<6;sample++)
        {
            token.ThrowIfCancellationRequested();
            var pids = owners();
            foreach(var target in snapshot().Where(w => Candidate(w,pids,true) || Candidate(w,pids,false)))
            {
                RecordDetection(target,DateTimeOffset.UtcNow);
                var key = (target.Handle,target.Pid,target.ClassName);
                if(attempted.Contains(key)) continue;
                bool transient = target.ClassName == "Qt5151QWindowIcon";
                var fresh = snapshot().FirstOrDefault(w => w.Handle == target.Handle && w.Pid == target.Pid && w.ClassName == target.ClassName && Candidate(w,owners(),transient));
                if(fresh == null) continue; // Next bounded sample discovers its replacement.
                attempted.Add(key);
                bool accepted = transient ? hide(fresh) : post(fresh);
                Log(logger,fresh,transient,accepted ? "Requested" : "RejectedOrChanged",null);
            }
            await wait(token);
        }
        var remaining = snapshot(); var live = owners();
        foreach(var key in attempted)
        {
            var w = new ExternalWindow(key.Item1,key.Item2,"Battle.net",key.Item3,"",true,true,IntPtr.Zero,0,false);
            bool visible = remaining.Any(n => n.Handle == w.Handle && n.Pid == w.Pid && n.ClassName == w.ClassName && n.Visible);
            ConfirmTiming(w,visible,live.Contains(w.Pid));
            Log(logger,w,w.ClassName == "Qt5151QWindowIcon",live.Contains(w.Pid) && !visible ? "HiddenAndProcessRunning" : "NotConfirmed",visible);
        }
        return live.Count > 0 && !remaining.Any(w => Candidate(w,live,true) || Candidate(w,live,false));
    }
    private static void Log(AppLogger logger,ExternalWindow w,bool transient,string result,bool? visible) => logger.Write(
        $"Battle.net {(transient ? "transient" : "main")} window: PID={w.Pid} HWND=0x{w.Handle.ToInt64():X} Class={w.ClassName} Action={(transient ? "Hide" : "WM_CLOSE")} Result={result} VisibleAfter={(visible.HasValue ? visible.Value.ToString() : "Pending")}");
    [DllImport("user32.dll",SetLastError=true)] private static extern bool ShowWindowAsync(IntPtr hwnd,int command);
    [DllImport("user32.dll",EntryPoint="PostMessageW",SetLastError=true)] private static extern bool PostMessage(IntPtr hwnd,uint msg,IntPtr w,IntPtr l);
}
