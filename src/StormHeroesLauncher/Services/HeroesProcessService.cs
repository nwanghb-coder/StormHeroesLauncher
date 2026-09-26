using System.Diagnostics;
using System.IO;
namespace StormHeroesLauncher.Services;
public sealed class HeroesProcessService(AppLogger logger, string SwitcherPath, Models.LaunchProgress? progress = null)
{
    public static readonly TimeSpan GameTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan UiTimeout = TimeSpan.FromSeconds(120);
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
        { logger.Write("启动前检测到游戏已运行，不再启动 Switcher。"); await WaitForExistingUiAsync(token); return; }
        ValidateInstallation();
        var info = CreateStartInfo(SwitcherPath);
        logger.WriteOperation("启动 HeroesSwitcher（仅一次，普通权限）", new { info.FileName, info.WorkingDirectory, Arguments = info.ArgumentList.ToArray() });
        var before = HeroesWindowProbe.Processes().Select(p => p.Pid).ToHashSet();
        long launchStarted = DateTime.UtcNow.ToFileTimeUtc();
        progress?.Report(Models.LaunchState.StartingHeroes);
        using var switcher = Process.Start(info);
        if (switcher == null) throw new InvalidOperationException("HeroesSwitcher 未返回启动进程。");
        IntPtr parentHandle = switcher.Handle;
        if (!HeroesWindowProbe.GetProcessTimes(parentHandle, out long created, out _, out _, out _))
            throw new InvalidOperationException("无法确认 Switcher 进程身份。");
        var parent = new HeroesProcessIdentity((uint)switcher.Id, 0, created, (uint)Process.GetCurrentProcess().SessionId, SwitcherPath);
        progress?.Report(Models.LaunchState.PreparingHeroes);
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < GameTimeout)
        {
            token.ThrowIfCancellationRequested();
            var games = HeroesWindowProbe.Processes().Where(p => HeroesWindowProbe.IsGame(p, SwitcherPath)).ToArray();
            if (games.Length > 0)
            {
                logger.Write("已检测到游戏进程，继续等待稳定主窗口。");
                bool MayHide(HeroesProcessIdentity p) =>
                    HeroesWindowProbe.GetProcessTimes(parentHandle, out _, out long exited, out _, out _) &&
                    HeroesUiReadiness.IsDirectChild(p, parent, exited, launchStarted, before);
                await WaitForUiAsync(games, MayHide, token); return;
            }
            await Task.Delay(250, token);
        }
        throw new TimeoutException("未在 30 秒内检测到 HeroesOfTheStorm_x64.exe。请检查 Battle.net 登录及游戏状态；未重复启动 Switcher。");
    }, token);
    public Task WaitForExistingUiAsync(CancellationToken token) => Task.Run(async () =>
    {
        progress?.Report(Models.LaunchState.PreparingHeroes);
        var games = HeroesWindowProbe.Processes().Where(p => HeroesWindowProbe.IsGame(p, SwitcherPath)).ToArray();
        await WaitForUiAsync(games, _ => false, token); // Never hide a pre-existing game's dialogs.
    }, token);

    private async Task WaitForUiAsync(HeroesProcessIdentity[] games, Func<HeroesProcessIdentity, bool> mayHide, CancellationToken token)
    {
        var readiness = new HeroesUiReadiness(); var timer = Stopwatch.StartNew();
        while (timer.Elapsed < UiTimeout)
        {
            token.ThrowIfCancellationRequested();
            var live = games.Where(p => HeroesWindowProbe.Same(p, HeroesWindowProbe.ReadProcess(p.Pid))).ToArray();
            if (live.Length == 0) throw new InvalidOperationException("等待游戏主窗口时，已确认的游戏进程已退出或身份无法确认。");
            bool Hide(HeroesWindowIdentity w)
            {
                bool accepted = HeroesWindowProbe.Hide(w);
                logger.Write($"Heroes preparation SW_HIDE: PID={w.Process.Pid} HWND=0x{w.Hwnd:X} Accepted={accepted}");
                return accepted;
            }
            if (readiness.Sample(HeroesWindowProbe.Windows(live), timer.ElapsedMilliseconds, mayHide, Hide))
            { logger.Write("游戏主窗口已稳定 1500ms，准备窗口已隐藏或消失。"); return; }
            await Task.Delay(100, token);
        }
        throw new TimeoutException("120 秒内未确认稳定游戏主窗口。游戏进程保持运行，请检查游戏状态。");
    }
}
