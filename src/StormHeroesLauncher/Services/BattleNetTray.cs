using System.Diagnostics;
namespace StormHeroesLauncher.Services;
public static class BattleNetTray
{
    internal static EarlyBattleNetSuppression? ColdObserver { get; set; }
    public static bool Candidate(ExternalWindow w,IReadOnlySet<uint> pids,bool transient) =>
        w.Visible && w.Enabled && pids.Contains(w.Pid) && (w.ProcessName.Equals("Battle.net",StringComparison.OrdinalIgnoreCase) || transient && w.ProcessName.Equals("Battle.net Launcher",StringComparison.OrdinalIgnoreCase)) &&
        w.Owner == IntPtr.Zero && (transient ? w.ClassName == "Qt5151QWindowIcon" : w.ClassName.StartsWith("Chrome_WidgetWin_",StringComparison.Ordinal));
    private static IReadOnlyList<ExternalWindow> Snapshot() => new NativeWindowManager().Snapshot("Battle.net");
    private static HashSet<uint> Owners() => DesktopProcessState.Find("Battle.net");
    internal static bool NativeAction(ExternalWindow target,bool hide,AppLogger? logger=null)
    {
        var clock = Stopwatch.StartNew();
        var result = ColdObserver?.ActAtCheckpoint(target, hide) ??
            BattleNetNativeWindows.Act(target, hide, () => clock.Elapsed.TotalMilliseconds);
        logger?.Write($"Battle.net window: PID={target.Pid} HWND=0x{target.Handle.ToInt64():X} Class={target.ClassName} HideAccepted={result.HideAccepted} WMClosePosted={result.ClosePosted} ProcessRunningAfter={result.ProcessAlive} Result={result.Result}");
        return hide ? result.HideAccepted : result.ClosePosted;
    }
    public static void HideTransient(AppLogger logger)
    {
        try
        {
            foreach(var w in Snapshot().Where(w => Candidate(w,Owners(),true)))
            {
                bool accepted = NativeAction(w,true,logger);
                var sample = Snapshot().FirstOrDefault(n => n.Handle == w.Handle && n.Pid == w.Pid && n.ClassName == w.ClassName);
                ColdObserver?.ConfirmAtCheckpoint(w,sample,Owners().Contains(w.Pid));
                Log(logger,w,true,accepted ? "Requested" : "RejectedOrChanged",sample?.Visible ?? false);
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
            ColdObserver?.ConfirmAtCheckpoint(w,remaining.FirstOrDefault(n => n.Handle == w.Handle && n.Pid == w.Pid && n.ClassName == w.ClassName),live.Contains(w.Pid));
            Log(logger,w,w.ClassName == "Qt5151QWindowIcon",live.Contains(w.Pid) && !visible ? "HiddenAndProcessRunning" : "NotConfirmed",visible);
        }
        return live.Count > 0 && !remaining.Any(w => Candidate(w,live,true) || Candidate(w,live,false));
    }
    private static void Log(AppLogger logger,ExternalWindow w,bool transient,string result,bool? visible) => logger.Write(
        $"Battle.net {(transient ? "transient" : "main")} window: PID={w.Pid} HWND=0x{w.Handle.ToInt64():X} Class={w.ClassName} Action={(transient ? "Hide" : "WM_CLOSE")} Result={result} VisibleAfter={(visible.HasValue ? visible.Value.ToString() : "Pending")}");
}
