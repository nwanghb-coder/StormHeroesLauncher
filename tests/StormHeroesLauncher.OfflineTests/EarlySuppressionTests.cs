using StormHeroesLauncher.Services;
using StormHeroesLauncher.WindowSupport;
public static class EarlySuppressionTests
{
    public static void Run(AppLogger log,Action<bool,string> check)
    {
        long now=0;int hides=0,posts=0,reads=0;
        var qt=new ExternalWindow(new IntPtr(1),20,"Battle.net","Qt5151QWindowIcon","",true,true,IntPtr.Zero,0,false);
        var chrome=qt with {Handle=new IntPtr(2),ClassName="Chrome_WidgetWin_0"};
        var windows=new List<ExternalWindow>{qt,chrome,qt with {Pid=99},chrome with {ProcessName="Other"}};
        var observer=new EarlyBattleNetSuppression(log,()=>{reads++;return windows.ToArray();},()=>new HashSet<uint>{20},(w,hide)=>{if(hide)hides++;else posts++;return true;},()=>now);
        observer.Arm(); check(observer.Active && hides==0 && posts==0,"suppression state armed before fake launch");
        observer.MarkLaunch();observer.Poll();
        check(hides==1 && posts==1,"first poll immediately hides Qt and closes Chrome; unrelated windows ignored");
        observer.Poll(); check(hides==1 && posts==1,"same HWND identities not spammed");
        windows=[chrome with {Handle=new IntPtr(3)}];observer.Poll();check(posts==2,"new Chrome HWND handled without readiness wait");
        check(observer.PollDelay==10,"first two seconds use 10 ms polling"); now=2000;check(observer.PollDelay==25,"remaining startup uses 25 ms polling");
        now=5000;int before=reads;observer.Poll();observer.Poll();check(!observer.Active && reads==before,"no enumeration after absolute five-second budget");
        check(BattleNetService.ReadyCandidate(false,true,true,false,true,"Chrome_WidgetWin_0"),"hidden Chrome can satisfy independent readiness");
        now=0;reads=0;windows=[chrome];posts=0;
        observer=new(log,()=>{reads++;if(reads==2)windows=[chrome with {Handle=new IntPtr(9)}];return windows.ToArray();},()=>new HashSet<uint>{20},(w,_)=>{posts++;check(w.Handle==new IntPtr(9),"stale HWND rejected during revalidation");return false;},()=>now);
        observer.Arm();observer.Poll();observer.Poll();check(posts==1,"replacement acted on once even when action fails");
        observer=new(log,()=>throw new InvalidOperationException(),()=>[],(_,_)=>false,()=>now);observer.Arm();observer.Poll();check(!observer.Active,"observer failure nonfatal; readiness can continue");
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
