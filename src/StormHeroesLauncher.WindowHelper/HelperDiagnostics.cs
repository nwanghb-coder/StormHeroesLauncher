using System.ComponentModel;
using StormHeroesLauncher.WindowSupport;
namespace StormHeroesLauncher.WindowHelper;
// Per-run, best-effort diagnostics independent of the result pipe. Never record input or exception messages.
internal sealed class HelperDiagnostics
{
    private readonly string? path;
    private string stage = "Initialized";
    public HelperDiagnostics()
    {
        try
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"StormHeroesLauncher","Logs");
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory,$"windowhelper-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Environment.ProcessId}-{Guid.NewGuid():N}.log");
            using var file = new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read);
        }
        catch { path = null; } // Diagnostics must never change the launch/return behavior.
    }
    public void Mark(string value) { stage = value; Write(value); }
    public void Failure(Exception ex,int exitCode)
    {
        Write($"Failure Stage={stage} ExitCode={exitCode} Exception={ex.GetType().FullName} HResult=0x{ex.HResult:X8} NativeError={(ex is Win32Exception native ? native.NativeErrorCode.ToString() : "NA")} ThrowSite={ex.TargetSite?.DeclaringType?.FullName}.{ex.TargetSite?.Name}");
        if (ex.InnerException is { } inner)
            Write($"InnerException={inner.GetType().FullName} HResult=0x{inner.HResult:X8}");
    }
    private void Write(string value)
    {
        try { if (path != null) File.AppendAllText(path,$"[{DateTimeOffset.Now:O}] HelperPid={Environment.ProcessId} {value}{Environment.NewLine}"); }
        catch { }
    }
}
// Decorates existing operations; no new process/window queries, no changes to actions or timing.
internal sealed class DiagnosticTrayHost(IUuTrayHost inner,HelperDiagnostics log) : IUuTrayHost
{
    public void PrepareStartup() => inner.PrepareStartup();
    public void StartupDelay(int milliseconds) => inner.StartupDelay(milliseconds);
    public bool Running()
    {
        log.Mark("UuRunningCheckAttempted"); bool result = inner.Running();
        log.Mark("UuMainProcessFound="+result); return result;
    }
    public bool ValidateLauncher(string path)
    {
        log.Mark("LauncherValidationAttempted"); bool result = inner.ValidateLauncher(path);
        log.Mark("LauncherValidated="+result); return result;
    }
    public bool Start(string path)
    {
        log.Mark("UuLaunchAttempted"); bool result = inner.Start(path);
        log.Mark("UuLaunchProcessStarted="+result); return result;
    }
    public UuTrayTarget? FindMain()
    {
        var result = inner.FindMain();
        return result;
    }
    public UuTrayReport Close(UuTrayTarget target)
    {
        var result = inner.Close(target);
        log.Mark($"WmClosePosted={result.WM_CLOSE == "Posted"} ExitCode={result.ExitCode}");
        log.Mark($"UuStillRunning={result.ProcessRunningAfter} WindowVisibleAfter={result.WindowVisibleAfter}"); return result;
    }
    public void Delay() => inner.Delay();
}
