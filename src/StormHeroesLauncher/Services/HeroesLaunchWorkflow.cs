using StormHeroesLauncher.Models;
namespace StormHeroesLauncher.Services;
// Delegates permit offline sequencing tests without launching external applications.
public sealed class HeroesLaunchWorkflow(
    Func<CancellationToken, Task<bool>> isGameRunning,
    Func<CancellationToken, Task<HeroesBoostStatus>> startBoost,
    Func<CancellationToken, Task> ensureBattleNet,
    Func<CancellationToken, Task> launchHeroes,
    Action validateInstallation, AppLogger logger, LaunchProgress? progress = null,
    Func<CancellationToken, Task>? waitForExistingGame = null)
{
    public async Task RunAsync(CancellationToken token)
    {
        try
        {
            progress?.Report(LaunchState.Initializing);
            await RunCoreAsync(token);
            progress?.Report(LaunchState.GameReady);
        }
        catch { progress?.Report(LaunchState.Failed); throw; }
    }
    private async Task RunCoreAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (await isGameRunning(token))
        {
            logger.Write("HeroesOfTheStorm_x64.exe 已运行，跳过加速变更和重复启动。");
            progress?.Report(LaunchState.PreparingHeroes);
            if (waitForExistingGame != null) await waitForExistingGame(token);
            return;
        }
        validateInstallation();
        logger.Write($"StormHeroesLauncher {AboutSafetyContent.Version}：确保 UU 运行并启动目标游戏加速。");
        var status = await startBoost(token);
        if (!status.IsReady) throw new InvalidOperationException("目标游戏尚未确认 isBoosting=true 且 status=boosting。");
        token.ThrowIfCancellationRequested();
        logger.Write("目标游戏加速已确认，开始检查 Battle.net。");
        await ensureBattleNet(token);
        token.ThrowIfCancellationRequested();
        await launchHeroes(token);
        logger.Write("启动成功：已确认稳定游戏主窗口（不代表已登录服务器）。");
    }
}
