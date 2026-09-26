using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using StormHeroesLauncher.Observer;
using StormHeroesLauncher.Services;

public static class BattleNetStartProbeTests
{
    public static async Task Run(Action<bool,string> check)
    {
        const string path = @"C:\Fixture\Battle.net.exe", root = @"C:\Fixture", agent = @"C:\ProgramData\Battle.net\Agent";
        var baseline=BattleNetProbePolicy.StartInfo(path,BattleNetProbeStrategy.Baseline);
        var hidden=BattleNetProbePolicy.StartInfo(path,BattleNetProbeStrategy.Hidden);
        check(!baseline.UseShellExecute && baseline.WindowStyle==ProcessWindowStyle.Normal && baseline.ArgumentList.Count==0 && baseline.Arguments=="","baseline is normal non-shell launch with no arguments");
        check(!hidden.UseShellExecute && hidden.WindowStyle==ProcessWindowStyle.Hidden && !hidden.CreateNoWindow && hidden.Verb=="","Hidden changes only show-state hint, with no shell/elevation");
        check(await DeveloperBattleNetStartProbe.RunAsync(["--test-battlenet-start","99",path,@"C:\test.jsonl"])==2,"unknown strategy rejected without execution");
        var parent=new HeroesProcessIdentity(1,0,100,2,path);
        var child=new HeroesProcessIdentity(3,1,200,2,path);
        bool Own(HeroesProcessIdentity c,long exit=0,IReadOnlySet<uint>? before=null)=>BattleNetProbePolicy.Owns(c,parent,exit,150,before??new HashSet<uint>(),root,agent);
        check(Own(child),"test descendant with creation/path/session/parent proof accepted");
        check(BattleNetProbePolicy.CleanupOrder(new[]{child,parent},parent.Pid).SequenceEqual(new[]{parent,child}),
            "cleanup stops the known root before workers and never adds an unproven process");
        check(!Own(child with {ParentPid=99}) && !Own(child with {Session=4}) && !Own(child with {Created=90}),"unrelated parent/session or old process rejected");
        check(!Own(child,190) && !Own(child,before:new HashSet<uint>{3}),"parent PID reuse and preexisting PID rejected");
        check(!Own(child with {Path=@"C:\FixtureOther\Battle.net.exe"}) && !Own(child with {Path=@"C:\Fixture\HeroesOfTheStorm_x64.exe"}),"sibling-root collision and Heroes always excluded");
        check(Own(child with {Path=agent+@"\Agent.1\Agent.exe"}) && !Own(child with {Path=@"C:\Elsewhere\Agent.exe"}),"Agent requires both ancestry and approved agent root");
        var qt=new ProbeWindow(1,100,10,"Qt5151QWindowIcon",true,false,true,false,true);
        var chrome=qt with {Hwnd=20,ClassName="Chrome_WidgetWin_0"};
        var m=new BattleNetProbeMeasurements();
        m.Sample(10,[qt]);m.Sample(110,[chrome]);m.Sample(210,[chrome,chrome with {Hwnd=30}]);m.Sample(310,[chrome with {Visible=false}]);
        check(m.FirstQtVisibleMs==10 && m.FirstChromeVisibleMs==110 && m.FirstHiddenOrMinimizedMs==310,"first class/hidden timestamps measured independently");
        check(m.QtVisibleDurationMs==100 && m.ChromeVisibleDurationMs==200 && !m.ChromeRightCensored,"visible durations are sampled class unions, not multiplied across HWNDs");
        m.Sample(1810,[chrome with {Visible=false}]);check(m.StableChromiumProxy,"hidden nonhung Chromium qualifies only as bootstrap proxy");
        var replacement=new BattleNetProbeMeasurements();replacement.Sample(0,[chrome]);replacement.Sample(1000,[chrome with {Created=200}]);replacement.Sample(1600,[chrome with {Created=200}]);
        check(!replacement.StableChromiumProxy,"PID/HWND reuse resets bootstrap stability");
        check(replacement.ChromeRightCensored && replacement.ChromeVisibleDurationMs==1600,"visible-at-end durations explicitly right-censored");
        var minimized=new BattleNetProbeMeasurements();minimized.Sample(0,[chrome with {Minimized=true}]);minimized.Sample(500,[chrome with {Minimized=true}]);
        check(minimized.FirstHiddenOrMinimizedMs==0 && minimized.FirstChromeVisibleMs==null && minimized.ChromeVisibleDurationMs==0,"minimized windows are not counted as visibly exposed client UI");
        var native=typeof(DeveloperBattleNetStartProbe).GetNestedType("Native",BindingFlags.NonPublic)!;
        var startup=native.GetMethod("HiddenStartupInfo",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null)!;
        check((uint)startup.GetType().GetField("Flags")!.GetValue(startup)! == 1 && (ushort)startup.GetType().GetField("ShowWindow")!.GetValue(startup)! == 0 && Marshal.SizeOf(startup)==104,"native STARTUPINFO uses documented x64 layout and exact hidden flags");
        var imports=native.GetMethods(BindingFlags.Static|BindingFlags.NonPublic).Where(m=>m.GetCustomAttribute<DllImportAttribute>()!=null).Select(m=>m.Name).ToHashSet();
        check(!imports.Any(n=>n.Contains("Hook") || n.Contains("ShowWindow") || n.Contains("ReadProcessMemory") || n.Contains("SendInput")),"probe imports no post-launch hide, hooks, input or process-memory APIs");
        check(DeveloperBattleNetStartProbe.ObservationMilliseconds==30000,"observation bound fixed at 30 seconds");
        var runningMethod=native.GetMethod("RunningState",BindingFlags.Static|BindingFlags.NonPublic)!;
        var fakeInfo=new ProcessStartInfo(System.IO.Path.Combine(AppContext.BaseDirectory,"StormHeroesLauncher.OfflineTests.exe"))
            {UseShellExecute=false,WindowStyle=ProcessWindowStyle.Hidden};
        fakeInfo.ArgumentList.Add("--fake-child");fakeInfo.ArgumentList.Add("short-wait");
        using var fake=Process.Start(fakeInfo)!;
        _=fake.Handle; // Retain identity handle through exit, as the research cleanup does.
        check((bool?)runningMethod.Invoke(null,[(uint)fake.Id])==true,"final cleanup check recognizes a running harmless test child");
        await fake.WaitForExitAsync();
        check((bool?)runningMethod.Invoke(null,[(uint)fake.Id])==false,"exited child with retained handle is not falsely reported as incomplete cleanup");
    }
}
