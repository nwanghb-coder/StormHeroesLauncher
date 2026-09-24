namespace StormHeroesLauncher.WindowSupport;
public sealed record UuTrayTarget(long Hwnd, uint Pid, long Created);
public sealed record UuTrayReport(bool LauncherValidated, bool UUStarted, uint TargetPID, long TargetHWND,
    string Class, string WM_CLOSE, bool? WindowVisibleAfter, bool ProcessRunningAfter, int ExitCode);
public interface IUuTrayHost
{
    bool Running();
    bool ValidateLauncher(string path);
    bool Start(string path);
    UuTrayTarget? FindMain();
    UuTrayReport Close(UuTrayTarget target);
    void Delay();
    void PrepareStartup() { }
    void StartupDelay(int milliseconds) => Delay();
}
public static class UuElevatedOperation
{
    public static UuTrayReport Run(bool cold, string launcher, UuTrayTarget? supplied, IUuTrayHost host)
    {
        bool validated = false, started = false;
        UuTrayReport Fail(int code) => new(validated,started,0,0,"","NotPosted",null,host.Running(),code);
        if (cold)
        {
            validated = host.ValidateLauncher(launcher);
            if (!validated) return Fail(20);
            host.PrepareStartup();
            var timer = System.Diagnostics.Stopwatch.StartNew();
            if (!host.Running()) { started = host.Start(launcher); }
            for (int i = 0; i < 320 && timer.ElapsedMilliseconds < 5000; i++)
            {
                var target = host.FindMain();
                if (target != null) return host.Close(target) with { LauncherValidated = validated, UUStarted = started };
                host.StartupDelay(i < 200 && timer.ElapsedMilliseconds < 2000 ? 10 : 25);
            }
            return Fail(21);
        }
        return supplied == null ? Fail(22) : host.Close(supplied);
    }
}
