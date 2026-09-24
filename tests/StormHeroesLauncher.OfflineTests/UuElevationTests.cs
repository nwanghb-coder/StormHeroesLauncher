using StormHeroesLauncher.Services;
using StormHeroesLauncher.WindowSupport;
public static class UuElevationTests
{
    public static async Task Run(AppLogger logger,Action<bool,string> check)
    {
        var host = new FakeTrayHost(); var helper = new FakeElevation(host);
        var flow = new UuElevationFlow(logger,host,helper,@"C:\Fake\uu_launcher.exe");
        check(await flow.EnsureAsync(default) == "UU 加速器：启动成功" && helper.Calls == 1 && helper.Cold && host.Starts == 0,"cold main requests helper once and never starts UU itself");
        check(await flow.EnsureAsync(default) == "UU 加速器：已运行" && helper.Calls == 1,"subsequent tray state requests no helper");
        host = new(); helper = new(host) { Code = 1223 }; flow = new(logger,host,helper,@"C:\Fake\uu_launcher.exe");
        check(await flow.EnsureAsync(default) == "UU 加速器：启动已取消" && helper.Calls == 1,"cold UAC cancellation stops before CLI");
        await flow.EnsureAsync(default); check(helper.Calls == 1,"cold cancellation never retries UAC");
        host = new(); helper = new(host) { Code = 24 }; flow = new(logger,host,helper,@"C:\Fake\uu_launcher.exe");
        check((await flow.EnsureAsync(default)).Contains("失败") && helper.Calls == 1,"cold helper failure stops safely");
        host = new() { Valid = false }; helper = new(host); flow = new(logger,host,helper,@"C:\Fake\uu_launcher.exe");
        check((await flow.EnsureAsync(default)).Contains("验证失败") && helper.Calls == 0,"invalid launcher never elevates");
        host = new() { Alive = true, Visible = true, CloseCode = 23 }; helper = new(host) { Code = 1223 }; flow = new(logger,host,helper,@"C:\Fake\uu_launcher.exe");
        check(await flow.EnsureAsync(default) == "UU 加速器：已运行" && helper.Calls == 1 && !helper.Cold,"warm visible cancellation is nonfatal");
        await flow.EnsureAsync(default); check(helper.Calls == 1,"warm cancellation never requests another helper");
        host = new() { Alive = true }; helper = new(host); flow = new(logger,host,helper,@"C:\Fake\uu_launcher.exe");
        check(await flow.EnsureAsync(default) == "UU 加速器：已运行" && helper.Calls == 0 && host.Closes == 0,"already tray means zero UAC and no close action");
        host = new() { Alive = true, Visible = true }; helper = new(host); flow = new(logger,host,helper,@"C:\Fake\uu_launcher.exe");
        check(await flow.EnsureAsync(default) == "UU 加速器：已运行" && helper.Calls == 0 && host.Closes == 1,"normal warm close success does not elevate");
        host = new() { Alive = true, Visible = true, CloseCode = 23 }; helper = new(host); flow = new(logger,host,helper,@"C:\Fake\uu_launcher.exe");
        check(await flow.EnsureAsync(default) == "UU 加速器：已运行" && helper.Calls == 1,"warm normal failure requests one tray-only helper");
        host = new(); var report = UuElevatedOperation.Run(true,@"C:\Fake\uu_launcher.exe",null,host);
        check(report.ExitCode == 0 && report.LauncherValidated && report.UUStarted && host.Starts == 1 && host.Closes == 1,"elevated operation starts UU then closes exactly once");
        host = new() { Alive = true, Visible = true }; report = UuElevatedOperation.Run(true,@"C:\Fake\uu_launcher.exe",null,host);
        check(host.Starts == 0 && host.Closes == 1,"UU startup race does not duplicate UU launch");
        host = new() { NeverWindow = true }; report = UuElevatedOperation.Run(true,@"C:\Fake\uu_launcher.exe",null,host);
        check(report.ExitCode == 21 && host.Delays == 320 && host.Closes == 0,"helper window wait is bounded without close retry");
        host = new() { Valid = false }; report = UuElevatedOperation.Run(true,"invalid",null,host);
        check(report.ExitCode == 20 && host.Starts == 0 && host.Closes == 0,"helper independently rejects unvalidated launcher");
        var battle = new ExternalWindow(new IntPtr(10),20,"Battle.net","Chrome_WidgetWin_0","战网",true,true,IntPtr.Zero,0,false);
        bool battleVisible = true; int posts = 0;
        IReadOnlyList<ExternalWindow> Windows() => battleVisible ? new[] { battle } : Array.Empty<ExternalWindow>();
        check(await BattleNetTray.CloseCore(logger,Windows,() => new HashSet<uint> {20},_ => { posts++; battleVisible = false; return true; },_ => Task.CompletedTask,default) && posts == 1,"Battle.net tray posts one message then confirms hidden/live");
        check(await BattleNetTray.CloseCore(logger,Windows,() => new HashSet<uint> {20},_ => { posts++; return true; },_ => Task.CompletedTask,default) && posts == 1,"Battle.net already hidden does not post again");
        battleVisible = true;
        check(!await BattleNetTray.CloseCore(logger,Windows,() => new HashSet<uint> {20},_ => { posts++; return false; },_ => Task.CompletedTask,default) && posts == 2,"Battle.net failure remains bounded without retries");
        check(BattleNetService.ReadyCandidate(false,true,true,false,true,"Chrome_WidgetWin_0"),"already-running Battle.net may be hidden in tray");
        check(!BattleNetService.ReadyCandidate(false,true,true,false,false,"Chrome_WidgetWin_0"),"cold Battle.net still requires visible readiness");
        check(!BattleNetService.ReadyCandidate(false,true,true,false,true,"Other"),"hidden unrelated window cannot satisfy readiness");
        check(!BattleNetService.ReadyCandidate(false,true,true,true,true,"Chrome_WidgetWin_0"),"hung tray window rejected");
        var start = UuTrayNative.StartInfo(@"C:\Fake\uu_launcher.exe");
        check(!start.UseShellExecute && string.IsNullOrEmpty(start.Verb),"helper launches UU without separate runas");
        var shell = UuElevationRunner.StartInfo(@"C:\Fake\WindowHelper.exe",true,@"C:\Fake\uu_launcher.exe",null,new string('a',32));
        check(shell.UseShellExecute && shell.Verb == "runas" && shell.ArgumentList[0] == "--start-uu-and-tray","main elevates helper, not UU launcher");
    }
}
sealed class FakeTrayHost : IUuTrayHost
{
    public bool Alive, Visible, NeverWindow; public bool Valid = true;
    public int Starts, Closes, Delays, CloseCode;
    public bool Running() => Alive;
    public bool ValidateLauncher(string p) => Valid;
    public bool Start(string p) { Starts++; Alive = true; Visible = !NeverWindow; return true; }
    public UuTrayTarget? FindMain() => Visible ? new(1,2,3) : null;
    public UuTrayReport Close(UuTrayTarget target) { Closes++; if(CloseCode == 0) Visible = false; return new(false,false,target.Pid,target.Hwnd,"UUMAINFORMV40",CloseCode == 0 ? "Posted" : "Failed",Visible,Alive,CloseCode); }
    public void Delay() { Delays++; }
}
sealed class FakeElevation(FakeTrayHost host) : IUuElevation
{
    public int Calls; public bool Cold; public int Code;
    public Task<UuElevationResult> RunAsync(bool cold,string launcher,UuTrayTarget? target,CancellationToken token)
    { Calls++; Cold = cold; if(Code == 0) { host.Alive = true; host.Visible = false; } return Task.FromResult(new UuElevationResult(Code,Code == 1223 ? "Cancelled" : "Accepted")); }
}
