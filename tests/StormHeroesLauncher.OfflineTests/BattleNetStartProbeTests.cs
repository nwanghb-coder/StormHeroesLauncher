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
        var process=new HeroesProcessIdentity(3,99,200,2,path);
        bool Observe(HeroesProcessIdentity p)=>BattleNetProbePolicy.Observe(p,2,150,root,agent);
        check(Observe(process),"metadata observation does not require cleanup ownership or parent ancestry");
        check(!Observe(process with {Session=4}) && !Observe(process with {Created=90}),"other sessions and prelaunch processes excluded");
        check(!Observe(process with {Path=@"C:\FixtureOther\Battle.net.exe"}) && !Observe(process with {Path=@"C:\Fixture\HeroesOfTheStorm_x64.exe"}),"unrelated paths and Heroes excluded");
        check(Observe(process with {Path=agent+@"\Agent.1\Agent.exe"}),"same-session Agent under approved root observed read-only");
        check(DeveloperBattleNetStartProbe.CompletionMessage=="Observation complete.","completion text is exact");
        check(DeveloperBattleNetStartProbe.RunningMessage.Contains("exit Battle.net"),"running-app refusal asks for manual exit");
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
        check(!imports.Any(n=>n.Contains("Hook") || n.Contains("ShowWindow") || n.Contains("ReadProcessMemory") || n.Contains("SendInput") || n.Contains("Terminate") || n.Contains("PostMessage") || n.Contains("SendMessage")),"probe has no window-mutation, termination, hook, input or process-memory imports");
        check(DeveloperBattleNetStartProbe.ObservationMilliseconds==30000,"observation bound fixed at 30 seconds");
        var runningMethod=native.GetMethod("RunningState",BindingFlags.Static|BindingFlags.NonPublic)!;
        var fakeInfo=new ProcessStartInfo(System.IO.Path.Combine(AppContext.BaseDirectory,"StormHeroesLauncher.OfflineTests.exe"))
            {UseShellExecute=false,WindowStyle=ProcessWindowStyle.Hidden};
        fakeInfo.ArgumentList.Add("--fake-child");fakeInfo.ArgumentList.Add("short-wait");
        using var fake=Process.Start(fakeInfo)!;
        _=fake.Handle; // Test stale process entries without acting on the child.
        check((bool?)runningMethod.Invoke(null,[(uint)fake.Id])==true,"read-only preflight recognizes a running harmless test child");
        await fake.WaitForExitAsync();
        check((bool?)runningMethod.Invoke(null,[(uint)fake.Id])==false,"exited child with retained handle is not falsely reported as running Battle.net");
    }
}
