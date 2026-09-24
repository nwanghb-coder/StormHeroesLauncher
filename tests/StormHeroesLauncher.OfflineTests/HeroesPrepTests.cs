using StormHeroesLauncher.Services;
public static class HeroesPrepTests
{
    public static async Task Run(AppLogger log,Action<bool,string> check)
    {
        var w=new HeroesPrepWindow(new IntPtr(1),10,100,"HeroesOfTheStorm_x64.exe","#32770",true,true,IntPtr.Zero,true);
        check(HeroesPreparationWindow.Matches(w),"workflow Heroes preparation dialog selected");
        foreach(var bad in new[]{w with {WorkflowOwned=false},w with {ProcessName="other.exe"},w with {Owner=new IntPtr(9)},w with {ClassName="GameWindow"},w with {TopLevel=false}})
            check(!HeroesPreparationWindow.Matches(bad),"unrelated/wrong-process/owned/non-dialog window ignored");
        int reads=0,actions=0,waits=0; var current=w;
        await HeroesPreparationWindow.ObserveCore(log,()=>{reads++;if(reads==2)current=w with {Hwnd=new IntPtr(2)};return new[]{current};},x=>{actions++;check(x.Hwnd!=w.Hwnd,"stale HWND rejected");return true;},_=>{waits++;return Task.CompletedTask;},default);
        check(actions==1 && waits==50,"same dialog hidden only once; observation bounded");
        actions=0;waits=0;current=w;
        await HeroesPreparationWindow.ObserveCore(log,()=>new[]{current},_=>{actions++;return true;},_=>{waits++;if(waits==1)current=w with {Hwnd=new IntPtr(2)};if(waits==2)current=w with {Hwnd=new IntPtr(3)};return Task.CompletedTask;},default);
        check(actions==2 && waits==50,"only one replacement hidden");
        actions=0;waits=0;
        await HeroesPreparationWindow.ObserveCore(log,()=>new[]{w},_=>{actions++;return false;},_=>{waits++;return Task.CompletedTask;},default);
        check(actions==1 && waits==50,"hide failure nonfatal and not retried");
        waits=0;await HeroesPreparationWindow.ObserveCore(log,()=>Array.Empty<HeroesPrepWindow>(),_=>throw new Exception(),_=>{waits++;return Task.CompletedTask;},default);
        check(waits==50,"no dialog nonfatal and bounded");
        await HeroesPreparationWindow.ObserveCore(log,()=>new[]{w},_=>throw new InvalidOperationException(),_=>Task.CompletedTask,default);
        check(true,"visibility exception nonfatal");
    }
}
