using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using StormHeroesLauncher.Models;

namespace StormHeroesLauncher.Services;

public sealed record CliProcessOutput(int ExitCode, string Stdout, string Stderr);
public interface ICliProcessRunner
{
    Task<CliProcessOutput> RunAsync(string path, IReadOnlyList<string> arguments,
        TimeSpan timeout, CancellationToken cancellationToken);
}

public sealed class CliProcessRunner : ICliProcessRunner
{
    public Task<CliProcessOutput> RunAsync(string path, IReadOnlyList<string> arguments,
        TimeSpan timeout, CancellationToken cancellationToken) => Task.Run(async () =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(path))
            throw new UuCliException(CliFailureKind.MissingExecutable, "UU CLI 未找到，请检查配置路径。");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(path)
            {
                UseShellExecute = false, CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(path)!,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        try
        {
            if (!process.Start()) throw new InvalidOperationException("Process.Start returned false");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            throw new UuCliException(CliFailureKind.LaunchFailed,
                "UU CLI 无法启动，请检查文件及运行权限。",
                errorCode: ex is Win32Exception win32 ? win32.NativeErrorCode.ToString() : ex.GetType().Name);
        }

        // Read both streams concurrently to avoid stdout/stderr pipe deadlocks.
        var stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var stderr = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            return new CliProcessOutput(process.ExitCode, await stdout, await stderr);
        }
        catch (OperationCanceledException)
        {
            // Only terminate our CLI request process, never UU or its descendants.
            try { if (!process.HasExited) process.Kill(entireProcessTree: false); }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { }
            try { await Task.WhenAll(stdout, stderr).ConfigureAwait(false); } catch (OperationCanceledException) { }
            cancellationToken.ThrowIfCancellationRequested();
            throw new UuCliException(CliFailureKind.Timeout, "UU CLI 查询或操作超时，请刷新状态确认结果。");
        }
    }, cancellationToken);
}
