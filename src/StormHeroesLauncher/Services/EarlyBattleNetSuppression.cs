using System.Diagnostics;
namespace StormHeroesLauncher.Services;
// One short-lived observer per startup; no hooks, worker lifetime or retained HWND authority.
public sealed class EarlyBattleNetSuppression(AppLogger logger,Func<IReadOnlyList<ExternalWindow>> snapshot,
    Func<HashSet<uint>> owners,Func<ExternalWindow,bool,bool> action,Func<long> milliseconds)
{
    private readonly Dictionary<(IntPtr,uint,string),(ExternalWindow Window,long Detected,long Requested,bool Accepted)> handled=new();
    private readonly HashSet<(IntPtr,uint,string)> confirmed=new();
    private long started;
    private DateTimeOffset epoch;
    private bool armed,finished;
    public bool Active => armed && !finished && milliseconds()-started<5000;
    public int PollDelay => milliseconds()-started<2000 ? 10 : 25;
    public void Arm()
    {
        // Allocate matching state before Process.Start. Timestamp is reset immediately before launch.
        handled.Clear(); confirmed.Clear(); armed=true;
        MarkLaunch(); logger.Write("Battle.net suppression: Armed=True BeforeLaunch=True BudgetMs=5000 FastPollMs=10 SlowPollMs=25");
    }
    public void MarkLaunch() { started=milliseconds(); epoch=DateTimeOffset.UtcNow; }
    public void Poll()
    {
        if(!Active) { Finish(); return; }
        try
        {
            var windows=snapshot(); var pids=owners();
            foreach(var pair in handled)
                if(!confirmed.Contains(pair.Key) && !windows.Any(w=>w.Handle==pair.Key.Item1 && w.Pid==pair.Key.Item2 && w.ClassName==pair.Key.Item3 && w.Visible))
                {
                    confirmed.Add(pair.Key); BattleNetTray.ConfirmTiming(pair.Value.Window,false,pids.Contains(pair.Key.Item2)); Log(pair.Value,milliseconds()-started,pids.Contains(pair.Key.Item2) ? "HiddenAndProcessRunning" : "ProcessExitedOrChanged");
                }
            foreach(var w in windows.Where(w=>BattleNetTray.Candidate(w,pids,true)||BattleNetTray.Candidate(w,pids,false)))
            {
                var key=(w.Handle,w.Pid,w.ClassName);
                if(handled.ContainsKey(key) || handled.Count>=32) continue;
                long detected=milliseconds()-started;
                bool transient=w.ClassName=="Qt5151QWindowIcon";
                if(!snapshot().Any(f=>f.Handle==w.Handle && f.Pid==w.Pid && f.ClassName==w.ClassName && BattleNetTray.Candidate(f,owners(),transient))) continue;
                long requested;
                BattleNetTray.RecordDetection(w,epoch.AddMilliseconds(detected));
                bool accepted=action(w,transient); requested=milliseconds()-started;
                var entry=(w,detected,requested,accepted); handled.Add(key,entry);
                Log(entry,null,accepted ? "Requested" : "RejectedOrChanged");
            }
        }
        catch(Exception ex) { logger.Write($"Battle.net suppression: Result=Unavailable Exception={ex.GetType().Name}; normal readiness continues"); finished=true; }
    }
    public void Finish()
    {
        if(finished) return;
        finished=true;
        foreach(var pair in handled.Where(p=>!confirmed.Contains(p.Key))) Log(pair.Value,null,"ConfirmationUnavailable");
        logger.Write("Battle.net suppression: FastObservationEnded=True; bounded normal checkpoints retained");
    }
    private void Log((ExternalWindow Window,long Detected,long Requested,bool Accepted) e,long? confirmedMs,string result)
    {
        var w=e.Window;
        logger.Write($"Battle.net {(w.ClassName=="Qt5151QWindowIcon" ? "transient" : "main")}: PID={w.Pid} HWND=0x{w.Handle.ToInt64():X} Class={w.ClassName} ProcessLaunchT0={epoch:O} WindowFirstDetected={epoch.AddMilliseconds(e.Detected):O} ActionRequested={epoch.AddMilliseconds(e.Requested):O} ActionConfirmed={(confirmedMs.HasValue ? epoch.AddMilliseconds(confirmedMs.Value).ToString("O") : "Unknown")} DetectionLatencyMs={e.Detected} ActionLatencyMs={e.Requested-e.Detected} EstimatedVisibleExposureMs={(confirmedMs.HasValue ? (confirmedMs.Value-e.Detected).ToString() : "Unknown")} ExposureBasis=FirstDetectedToObservedHidden_NotTotalVisibleLifetime Action={(w.ClassName=="Qt5151QWindowIcon" ? "Hide" : "WM_CLOSE")} Result={result}");
    }
}
