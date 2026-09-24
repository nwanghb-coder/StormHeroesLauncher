using System.Diagnostics;
using System.IO;
namespace StormHeroesLauncher.Services;
public sealed class HeroesProcessService(AppLogger logger, string SwitcherPath)
{
    public static readonly TimeSpan GameTimeout = TimeSpan.FromSeconds(30);
    public void ValidateInstallation()
    {
        if (!File.Exists(SwitcherPath)) throw new FileNotFoundException($"HeroesSwitcher 文件不存在：{SwitcherPath}");
    }
    public Task<bool> IsRunningAsync(CancellationToken token) =>
        Task.Run(() => DesktopProcessState.Find("HeroesOfTheStorm_x64").Count > 0, token);
    public static ProcessStartInfo CreateStartInfo(string SwitcherPath)
    {
        var info = new ProcessStartInfo(SwitcherPath)
        {
            UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(SwitcherPath)!
        };
        foreach (string argument in new[] { "-sso=1", "-launch", "-uid", "heroes" }) info.ArgumentList.Add(argument);
        return info;
    }
    public Task LaunchAndWaitAsync(CancellationToken token) => Task.Run(async () =>
    {
        token.ThrowIfCancellationRequested();
        if (DesktopProcessState.Find("HeroesOfTheStorm_x64").Count > 0)
        { logger.Write("启动前检测到游戏已运行，不再启动 Switcher。"); return; }
        ValidateInstallation();
        var info = CreateStartInfo(SwitcherPath);
        logger.WriteOperation("启动 HeroesSwitcher（仅一次，普通权限）", new { info.FileName, info.WorkingDirectory, Arguments = info.ArgumentList.ToArray() });
        var launchStarted = DateTime.UtcNow;
        using var switcher = Process.Start(info);
        if (switcher == null) throw new InvalidOperationException("HeroesSwitcher 未返回启动进程。");
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < GameTimeout)
        {
            token.ThrowIfCancellationRequested();
            if (DesktopProcessState.Find("HeroesOfTheStorm_x64").Count > 0)
            { logger.Write("已检测到当前 Windows 会话的 HeroesOfTheStorm_x64.exe。"); return; }
            await Task.Delay(250, token);
        }
        throw new TimeoutException("未在 30 秒内检测到 HeroesOfTheStorm_x64.exe。请检查 Battle.net 登录及游戏状态；未重复启动 Switcher。");
    }, token);
}
