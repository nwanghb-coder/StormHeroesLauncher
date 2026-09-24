using System.Globalization;
using System.IO.Pipes;
using StormHeroesLauncher.Services;
using StormHeroesLauncher.WindowSupport;
namespace StormHeroesLauncher.WindowHelper;
internal static class Program
{
    private static int Main(string[] args)
    {
        var log = new HelperDiagnostics();
        bool cold = args.Length == 4 && args[0] == "--start-uu-and-tray" && args[1] == "--launcher";
        bool warm = args.Length == 5 && args[0] == "--tray-uu";
        if (!cold && !warm) { log.Mark("ArgsRejected ExitCode=10"); return 10; }
        string id = args[^1]; if (!Guid.TryParseExact(id,"N",out _)) { log.Mark("ReportIdRejected ExitCode=10"); return 10; }
        UuTrayTarget? target = null;
        if (warm)
        {
            if (!long.TryParse(args[1],NumberStyles.None,CultureInfo.InvariantCulture,out long hwnd) || hwnd <= 0 ||
                !uint.TryParse(args[2],NumberStyles.None,CultureInfo.InvariantCulture,out uint pid) || pid == 0 ||
                !long.TryParse(args[3],NumberStyles.None,CultureInfo.InvariantCulture,out long created) || created <= 0)
                { log.Mark("TargetArgsRejected ExitCode=10"); return 10; }
            target = new(hwnd,pid,created);
        }
        log.Mark("ArgsValidated Operation="+(cold ? "StartAndTray" : "TrayOnly"));
        try
        {
            log.Mark("ResultPipeCreateAttempted");
            using (var pipe = UuPipeSecurity.CreateClient(id))
            {
                log.Mark("ResultPipeConnectAttempted");
                pipe.Connect(5000);
                log.Mark("ResultPipeOwnerValidationAttempted");
                UuPipeSecurity.ValidateServer(pipe);
                log.Mark("ResultPipeConnected");
                UuTrayReport report;
                try
                {
                    log.Mark("UuOperationEntered");
                    report = UuElevatedOperation.Run(cold,cold ? args[2] : "",target,new DiagnosticTrayHost(new UuTrayNative(log.Mark,hideFirst:true),log));
                }
                catch (Exception ex) { log.Failure(ex,25); report = new(false,false,0,0,"","Unknown",null,false,25); }
                log.Mark($"UuOperationCompleted ExitCode={report.ExitCode}");
                log.Mark("ResultWriteAttempted");
                UuHelperTransport.Write(pipe,UuHelperResult.From(cold ? "StartAndTray" : "TrayOnly",report));
                log.Mark("ResultWriteSucceeded");
                log.Mark($"ResultPipeDisposeAttempted PendingExitCode={report.ExitCode}");
                // Preserve disposal inside the outer catch and preserve the existing return code.
                return report.ExitCode;
            }
        }
        catch (Exception ex) { log.Failure(ex,26); return 26; }
    }
}
