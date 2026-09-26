using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using StormHeroesLauncher.WindowSupport;
using StormHeroesLauncher.Models;
namespace StormHeroesLauncher.Services;
public sealed record UuElevationResult(int ExitCode, string Uac, UuTrayReport? Report = null);
public interface IUuElevation { Task<UuElevationResult> RunAsync(bool cold, string launcher, UuTrayTarget? target, CancellationToken token); }
public sealed class UuElevationRunner(AppLogger? logger = null) : IUuElevation
{
    public static ProcessStartInfo StartInfo(string helper,bool cold,string launcher,UuTrayTarget? target,string reportId)
    {
        var info = new ProcessStartInfo(helper) { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(helper)! };
        info.ArgumentList.Add(cold ? "--start-uu-and-tray" : "--tray-uu");
        if (cold) { info.ArgumentList.Add("--launcher"); info.ArgumentList.Add(launcher); }
        else { info.ArgumentList.Add(target!.Hwnd.ToString(CultureInfo.InvariantCulture)); info.ArgumentList.Add(target.Pid.ToString(CultureInfo.InvariantCulture)); info.ArgumentList.Add(target.Created.ToString(CultureInfo.InvariantCulture)); }
        info.ArgumentList.Add(reportId); return info;
    }
    public async Task<UuElevationResult> RunAsync(bool cold,string launcher,UuTrayTarget? target,CancellationToken token)
    {
        void Log(string value) => logger?.Write("UU helper result channel: " + value);
        string operation = cold ? "StartAndTray" : "TrayOnly";
        string helper = PackagePaths.WindowHelper;
        if (!File.Exists(helper)) { Log("HelperProcessExitCode=NotStarted ResultTransport=NamedPipe/SHUR-v1 ResultPresent=False ResultLength=0 ResultParse=Failed ResultSchemaVersion=Unknown Reason=HelperMissing"); return new(-2,"HelperMissing"); }
        string id = Guid.NewGuid().ToString("N");
        using var pipe = UuPipeSecurity.CreateServer(id);
        token.ThrowIfCancellationRequested();
        try
        {
            using var child = Process.Start(StartInfo(helper,cold,launcher,target,id));
            if (child == null) { Log("HelperProcessExitCode=Unavailable ResultTransport=NamedPipe/SHUR-v1 ResultPresent=False ResultLength=0 ResultParse=Failed ResultSchemaVersion=Unknown Reason=NoHelperProcess"); return new(-3,"NoHelperProcess"); }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(35));
            UuResultFrame? frame = null;
            try
            {
                Task connection = pipe.WaitForConnectionAsync(timeout.Token);
                Task exited = child.WaitForExitAsync(timeout.Token);
                await Task.WhenAny(connection,exited);
                if (!connection.IsCompleted && child.HasExited)
                {
                    Log($"HelperProcessExitCode={child.ExitCode} ResultTransport=NamedPipe/SHUR-v1 ResultPresent=False ResultLength=0 ResultParse=Failed ResultSchemaVersion=Unknown Reason=HelperExitedBeforeConnection");
                    timeout.Cancel(); return new(-5,"InvalidHelperReport");
                }
                await connection;
                UuPipeSecurity.ValidateClient(pipe,child.Id);
                frame = await UuHelperTransport.ReadAsync(pipe,timeout.Token);
                await exited;
                if(frame.Status != "Complete")
                {
                    Log($"HelperProcessExitCode={child.ExitCode} ResultTransport=NamedPipe/SHUR-v1 ResultPresent={frame.Payload.Length > 0} ResultLength={frame.Payload.Length} ResultParse=Failed ResultSchemaVersion=Unknown Reason={frame.Status} DeclaredLength={frame.DeclaredLength}");
                    return new(-5,"InvalidHelperReport");
                }
                var parsed = UuHelperCodec.Parse(frame.Payload,child.ExitCode,operation,Log);
                return parsed.Valid ? new(child.ExitCode,"Accepted",parsed.Report) : new(-5,"InvalidHelperReport");
            }
            catch(OperationCanceledException) when (!token.IsCancellationRequested)
            {
                Log($"HelperProcessExitCode={(child.HasExited ? child.ExitCode.ToString() : "StillRunning")} ResultTransport=NamedPipe/SHUR-v1 ResultPresent={frame?.Payload.Length > 0} ResultLength={frame?.Payload.Length ?? 0} ResultParse=Failed ResultSchemaVersion=Unknown Reason=Timeout_NoRetry");
                return new(-4,"HelperTimeout_NoRetry");
            }
            catch(IOException ex)
            {
                Log($"HelperProcessExitCode={(child.HasExited ? child.ExitCode.ToString() : "StillRunning")} ResultTransport=NamedPipe/SHUR-v1 ResultPresent={frame?.Payload.Length > 0} ResultLength={frame?.Payload.Length ?? 0} ResultParse=Failed ResultSchemaVersion=Unknown Reason=TransportError HResult=0x{ex.HResult:X8}");
                return new(-5,"InvalidHelperReport");
            }
        }
        catch(Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Log("HelperProcessExitCode=NotStarted ResultTransport=NamedPipe/SHUR-v1 ResultPresent=False ResultLength=0 ResultParse=NotAttempted ResultSchemaVersion=Unknown Reason=UacCancelled");
            return new(1223,"Cancelled");
        }
    }
}
public sealed class UuElevationFlow(AppLogger logger,IUuTrayHost host,IUuElevation elevation,string launcher,LaunchProgress? progress = null)
{
    private bool helperRequested;
    private async Task<UuElevationResult> Helper(bool cold,UuTrayTarget? target,CancellationToken token)
    {
        if (helperRequested) return new(-6,"AlreadyRequested_NoRetry");
        helperRequested = true;
        logger.Write($"UU elevation flow: State={(cold ? "ColdStart" : "RunningVisible")} HelperRequested=True Action={(cold ? "StartAndTray" : "TrayOnly")}");
        var result = await elevation.RunAsync(cold,launcher,target,token);
        logger.Write($"UU helper: UAC={result.Uac} ExitCode={result.ExitCode}");
        if (result.Report != null) logger.WriteOperation("UU helper result",result.Report);
        return result;
    }
    public async Task<string> EnsureAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!host.Running())
        {
            progress?.Report(LaunchState.StartingUU);
            if (!host.ValidateLauncher(launcher)) return "UU 加速器：启动器签名或路径验证失败";
            var result = await Helper(true,null,token);
            if (result.ExitCode == 1223) return "UU 加速器：启动已取消";
                        if(result.ExitCode == 23 && result.Report is { LauncherValidated:true, ProcessRunningAfter:true } && host.Running())
            { logger.Write("UU hide/tray message failed; UU remains running, continue existing CLI flow without another UAC or message retry."); return "UU 加速器：启动成功"; }
            if (result.ExitCode != 0 || !host.Running()) return "UU 加速器：启动或托盘确认失败；已停止流程，不重试 UAC";
            return "UU 加速器：启动成功";
        }
        progress?.Report(LaunchState.PreparingUU);
        try
        {
            var target = host.FindMain();
            if (target == null) { logger.Write("UU elevation flow: State=AlreadyTray HelperRequested=False Action=None; no visible verified main window"); return "UU 加速器：已运行"; }
            logger.Write("UU elevation flow: State=RunningVisible HelperRequested=False Action=TrayOnly_NormalAttempt");
            var normal = host.Close(target); logger.WriteOperation("UU normal tray result",normal);
            if (!host.Running()) return "UU 加速器：托盘操作后 UU 已退出，请检查 UU 关闭行为设置";
            if (normal.ExitCode != 0)
            {
                var result = await Helper(false,target,token);
                if (result.ExitCode != 0) logger.Write("UU tray action skipped/cancelled; continue without retry, no acceleration stop.");
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { logger.Write($"UU warm tray action unavailable: {ex.GetType().Name}; no retry."); }
        return host.Running() ? "UU 加速器：已运行" : "UU 加速器：UU 已退出，已停止启动流程";
    }
}
