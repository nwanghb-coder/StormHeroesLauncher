using StormHeroesLauncher.Services;
public static class BattleNetWindowTests
{
    public static async Task Run(AppLogger logger,Action<bool,string> check)
    {
        ExternalWindow Window(int handle,string cls,string process="Battle.net",uint pid=20) => new(new IntPtr(handle),pid,process,cls,"",true,true,IntPtr.Zero,0,false);
        var qt=Window(1,"Qt5151QWindowIcon"); var chrome=Window(2,"Chrome_WidgetWin_0");
        var owners=new HashSet<uint>{20};
        check(BattleNetTray.Candidate(qt,owners,true) && !BattleNetTray.Candidate(qt,owners,false),"Qt selected only as transient");
        check(!BattleNetTray.Candidate(qt with {ProcessName="Other"},owners,true) && !BattleNetTray.Candidate(qt with {Pid=99},owners,true),"unrelated Qt ignored");
        check(BattleNetTray.Candidate(chrome,owners,false) && !BattleNetTray.Candidate(chrome with {ProcessName="Other"},owners,false),"title-free Chrome identity; unrelated Chrome ignored");
        check(!BattleNetService.ReadyCandidate(true,true,true,false,false,qt.ClassName),"Qt loading window cannot satisfy readiness");
        var windows=new List<ExternalWindow>{qt}; int hides=0,posts=0,waits=0;
        bool result=await BattleNetTray.ObserveCore(logger,()=>windows.ToArray(),()=>owners,
            w=>{posts++; check(w.ClassName.StartsWith("Chrome_WidgetWin_"),"only Chrome receives WM_CLOSE"); windows.Remove(w); return true;},
            w=>{hides++; check(w.ClassName=="Qt5151QWindowIcon","only Qt receives Hide"); windows.Remove(w); return true;},
            _=>{waits++; if(waits==1) windows.Add(chrome); return Task.CompletedTask;},default);
        check(result && hides==1 && posts==1 && waits==6,"Qt-to-Chrome replacement discovered within bounded checkpoint");
        int reads=0; posts=0; waits=0; windows=[chrome];
        result=await BattleNetTray.ObserveCore(logger,()=>{reads++; if(reads==2) windows=[chrome with {Handle=new IntPtr(3)}]; return windows.ToArray();},()=>owners,
            w=>{posts++; check(w.Handle==new IntPtr(3),"stale HWND not acted on; fresh replacement selected"); windows.Remove(w); return true;},_=>false,
            _=>{waits++;return Task.CompletedTask;},default);
        check(result && posts==1 && waits==6,"replacement main window closes once");
        windows=[]; posts=0; waits=0;
        result=await BattleNetTray.ObserveCore(logger,()=>windows.ToArray(),()=>owners,w=>{posts++;windows.Remove(w);return true;},_=>false,
            _=>{waits++;if(waits==2)windows.Add(chrome);return Task.CompletedTask;},default);
        check(result && posts==1 && waits==6,"new visible window after readiness discovered by recheck");
        windows=[chrome];posts=0;waits=0;
        result=await BattleNetTray.ObserveCore(logger,()=>windows.ToArray(),()=>owners,_=>{posts++;return false;},_=>false,
            _=>{waits++;return Task.CompletedTask;},default);
        check(!result && posts==1 && waits==6,"final recheck bounded; failed close not repeated");
    }
}
