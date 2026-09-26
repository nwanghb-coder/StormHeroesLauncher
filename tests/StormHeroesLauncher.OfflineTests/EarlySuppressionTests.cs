using StormHeroesLauncher.Services;
using StormHeroesLauncher.WindowSupport;
public static class EarlySuppressionTests
{
    public static async Task Run(AppLogger log, Action<bool,string> check)
    {
        double now = 0; int hides = 0, posts = 0, reads = 0;
        var qt = new ExternalWindow(new IntPtr(1),20,"Battle.net","Qt5151QWindowIcon","",true,true,IntPtr.Zero,0,false);
        var chrome = qt with {Handle=new IntPtr(2),ClassName="Chrome_WidgetWin_0"};
        var windows = new List<ExternalWindow>{qt,chrome,qt with {Pid=99},chrome with {ProcessName="Other"}};
        BattleNetVisualResult Act(ExternalWindow w, bool transient)
        {
            return BattleNetVisualAction.Run(transient, _ => windows.Contains(w),
                () => { hides++; return true; }, () => { posts++; return true; }, () => true, () => now);
        }
        var observer = new EarlyBattleNetSuppression(log,()=>{reads++;return windows.ToArray();},()=>[20],Act,()=>now);
        observer.Arm(); check(observer.Active && hides==0 && posts==0,"suppression state armed before fake launch");
        observer.MarkLaunch(); observer.Poll();
        check(hides==2 && posts==1,"first eligible Qt and Chrome detection hides immediately; only Chrome closes; unrelated ignored");
        observer.Poll(); check(hides==2 && posts==1,"same HWND identities not spammed");
        windows=[chrome with {Handle=new IntPtr(3)}]; observer.Poll();
        check(hides==3 && posts==2,"replacement Chrome hidden and closed once without readiness wait");
        check(observer.PollDelay==5,"first 500 ms use 5 ms delay");
        now=500; check(observer.PollDelay==10,"500 ms through two seconds use 10 ms delay");
        now=2000; check(observer.PollDelay==25,"two through five seconds use 25 ms delay");
        now=5000; int before=reads; observer.Poll(); observer.Poll();
        check(!observer.Active && reads==before,"no enumeration after absolute five-second budget");
        check(BattleNetService.ReadyCandidate(false,true,true,false,true,"Chrome_WidgetWin_0"),"hidden Chrome satisfies independent readiness");

        // A genuinely pending delay ensures the real pre-launch wrapper enters observation first.
        now=0; reads=0; hides=0; posts=0; windows=[];
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var detected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        observer=new(log,()=>{reads++;return windows.ToArray();},()=>[20],(w,h)=>{var r=Act(w,h);detected.TrySetResult();return r;},()=>now);
        int waits=0;
        var observation=observer.StartBeforeLaunch(()=>
        {
            check(reads==1 && waits==1 && observer.Active,"production launch wrapper starts observation before Process.Start delegate");
            windows=[chrome]; now=5; release.SetResult();
            check(detected.Task.Wait(TimeSpan.FromSeconds(5)),"suppression continues while fake Process.Start has not returned");
        },default,(_,_)=>++waits==1 ? release.Task : Task.Delay(1));
        check(!observation.IsCompleted,"launch and readiness do not await fast observation budget");
        now=5000; await observation;
        check(hides==1 && posts==1,"prelaunch background loop handles candidate only once");

        now=0; int delays=0; reads=0; windows=[];
        observer=new(log,()=>{reads++;return windows;},()=>[],Act,()=>now); observer.Arm(); observer.MarkLaunch();
        await observer.ObserveAsync(default,(ms,_)=>{delays++;now+=ms;return Task.CompletedTask;});
        check(now==5000 && delays==370 && reads==370,"fake-clock loop is bounded to 370 delays / five seconds, without busy spin");

        now=0; hides=0; posts=0;
        windows=Enumerable.Range(1,40).Select(i=>chrome with {Handle=new IntPtr(i)}).ToList();
        observer=new(log,()=>windows,()=>[20],Act,()=>now); observer.Arm();observer.MarkLaunch();observer.Poll();observer.Poll();observer.Finish();
        check(hides==32 && posts==32,"identity storage and actions bounded to 32 windows");

        now=0;
        observer=new(log,()=>throw new InvalidOperationException(),()=>[],(_,_)=>throw new Exception(),()=>now);
        observer.Arm();observer.Poll();check(!observer.Active,"observer failure nonfatal; readiness continues");
        using(var cancel=new CancellationTokenSource())
        {
            cancel.Cancel(); int launches=0;
            observer=new(log,()=>[],()=>[],Act,()=>now);
            try { _ = observer.StartBeforeLaunch(()=>launches++,cancel.Token); } catch(OperationCanceledException) { }
            check(launches==0,"cancelled prelaunch wrapper never launches");
        }
        // No visibility-confirmation callback exists between these native action delegates.
        var steps=new List<string>(); now=10;
        var result=BattleNetVisualAction.Run(false,visible=>{steps.Add(visible?"validate visible":"validate identity");return true;},
            ()=>{steps.Add("hide");now+=0.25;return false;},()=>{steps.Add("close");return true;},
            ()=>{steps.Add("alive");return true;},()=>now);
        check(steps.SequenceEqual(new[]{"validate visible","hide","validate identity","close","alive"}) && result.ClosePosted,
            "hide failure still closes immediately, without confirmation wait");
        check(result.HideRequested==10 && result.CloseRequested==10.25,"request timestamps surround actual calls with fractional precision");
        int calls=0;
        result=BattleNetVisualAction.Run(false,v=>v,()=>true,()=>{calls++;return true;},()=>true,()=>now);
        check(calls==0 && result.CloseRequested==null,"changed post-hide identity blocks close");
        result=BattleNetVisualAction.Run(false,_=>true,()=>throw new InvalidOperationException(),()=>{calls++;return false;},()=>true,()=>now);
        check(calls==1 && !result.ClosePosted && result.ProcessAlive,"hide exception and close failure remain nonfatal");
        result=BattleNetVisualAction.Run(true,_=>true,()=>true,()=>throw new Exception("Qt close"),()=>true,()=>now);
        check(result.HideAccepted && result.CloseRequested==null,"Qt never receives WM_CLOSE");
        var nativeType=typeof(BattleNetService).Assembly.GetType("StormHeroesLauncher.Services.BattleNetNativeWindows")!;
        var imports=nativeType.GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            .Where(m=>m.GetCustomAttributes(typeof(System.Runtime.InteropServices.DllImportAttribute),false).Length>0)
            .Select(m=>m.Name).ToHashSet();
        check(imports.SetEquals(new[]{"EnumWindows","IsWindow","GetWindowThreadProcessId","GetAncestor","GetWindow",
            "IsWindowVisible","IsWindowEnabled","GetClassName","ShowWindowAsync","PostMessage"}),
            "native suppression exposes only inspected read APIs and Hide/Close; no foreground, focus, hook or synchronous ShowWindow API");
        check(BattleNetTray.Candidate(qt with {ProcessName="Battle.net Launcher"},new HashSet<uint>{20},true) &&
            !BattleNetTray.Candidate(chrome with {ProcessName="Battle.net Launcher"},new HashSet<uint>{20},false),
            "verified launcher-family Qt can hide; Chrome still requires Battle.net.exe");
        check(!BattleNetTray.Candidate(chrome with {Owner=new IntPtr(50)},new HashSet<uint>{20},false) &&
            !BattleNetTray.Candidate(qt with {Enabled=false},new HashSet<uint>{20},true),"owned and disabled candidates ignored");

        // Metrics use sampled hidden state, distinguish absence, and give replacements independent records.
        var messages=new List<string>(); void Capture(string line)=>messages.Add(line);
        log.MessageLogged+=Capture;
        now=0;windows=[chrome];
        observer=new(log,()=>windows.ToArray(),()=>[20],Act,()=>now);observer.Arm();
        now=2;observer.MarkLaunch();now=10;observer.Poll();
        now=15;windows=[chrome with {Visible=false},chrome with {Handle=new IntPtr(30)}];observer.Poll();
        now=20;windows=[];observer.Poll();observer.Finish();
        log.MessageLogged-=Capture;
        check(messages.Any(s=>s.Contains("HWND=0x2 ") && s.Contains("ObserverLeadMs=2.000") && s.Contains("DetectionLatencyFromLaunchMs=8.000") && s.Contains("FirstDetectedToHiddenConfirmMs=5.000")),
            "per-window timing records prelaunch lead, detection latency and sampled hidden delay");
        check(messages.Any(s=>s.Contains("HWND=0x1E ") && s.Contains("FirstConfirmedHiddenT=Unknown") && !s.Contains("WindowAbsentT=Unknown")),
            "replacement has independent timings; destroyed HWND not falsely logged as confirmed hidden");
        log.MessageLogged+=Capture;
        now=6000;var late=chrome with {Handle=new IntPtr(40)};windows=[late];
        int previousPosts=posts;
        observer.ActAtCheckpoint(late,false);observer.ActAtCheckpoint(late,false);
        now=6500;observer.ConfirmAtCheckpoint(late,late with {Visible=false},true);
        log.MessageLogged-=Capture;
        check(posts==previousPosts+1 && !observer.Active,"late checkpoint handles a new identity once without restarting fast polling");
        check(messages.Any(s=>s.Contains("HWND=0x28 ") && s.Contains("DetectionLatencyFromLaunchMs=5998.000") &&
            s.Contains("FirstDetectedToHiddenConfirmMs=500.000")),"late-window checkpoint retains original launch clock and confirmation metrics");

        var host=new FastUuHost();var report=UuElevatedOperation.Run(true,"fake",null,host);
        check(host.ArmedBeforeStart && host.Delays==0 && host.Closes==1 && report.ExitCode==0,"UU armed before launch and closes on first eligible detection");
        host=new FastUuHost {NeverWindow=true};report=UuElevatedOperation.Run(true,"fake",null,host);
        check(report.ExitCode==21 && host.Delays==320 && host.Fast==200 && host.Slow==120 && host.Closes==0,"UU timeout bounded to 320 samples and safe failure; no close retries");
    }
}
sealed class FastUuHost : IUuTrayHost
{
    public bool NeverWindow,ArmedBeforeStart,Alive; private bool armed;
    public int Delays,Fast,Slow,Closes;
    public void PrepareStartup()=>armed=true;
    public bool Running()=>Alive;
    public bool ValidateLauncher(string path)=>true;
    public bool Start(string path){ArmedBeforeStart=armed;Alive=true;return true;}
    public UuTrayTarget? FindMain()=>NeverWindow ? null : new(1,2,3);
    public UuTrayReport Close(UuTrayTarget target){Closes++;return new(true,true,2,1,"UUMAINFORMV40","Posted",false,true,0);}
    public void Delay()=>throw new InvalidOperationException("Legacy delay used");
    public void StartupDelay(int ms){Delays++;if(ms==10)Fast++;if(ms==25)Slow++;}
}
