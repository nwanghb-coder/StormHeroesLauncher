using StormHeroesLauncher.Models;
namespace StormHeroesLauncher.Services;
// Delegates permit offline sequencing tests without launching external applications.
public sealed class HeroesLaunchWorkflow(
    Func<CancellationToken, Task<bool>> isGameRunning,
    Func<CancellationToken, Task<HeroesBoostStatus>> startBoost,
    Func<CancellationToken, Task> ensureBattleNet,
    Func<CancellationToken, Task> launchHeroes,
    Action validateInstallation, AppLogger logger, LaunchProgress? progress = null,
    Func<CancellationToken, Task>? waitForExistingGame = null,
    Func<CancellationToken, Task<bool>>? reuseBattleNet = null)
{
    public async Task RunAsync(CancellationToken token)
    {
        try
        {
            progress?.Report(LaunchState.Initializing);
            await RunCoreAsync(token);
            token.ThrowIfCancellationRequested();
            progress?.Report(LaunchState.GameReady);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { progress?.Report(LaunchState.Cancelled); throw; }
        catch { progress?.Report(LaunchState.Failed); throw; }
    }
    private async Task RunCoreAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (await isGameRunning(token))
        {
            token.ThrowIfCancellationRequested();
            logger.Write("WarmFastPath HeroesRunning=True Decision=KeepExistingGame");
            logger.Write("HeroesOfTheStorm_x64.exe 已运行，跳过加速变更和重复启动。");
            progress?.Report(LaunchState.PreparingHeroes);
            if (waitForExistingGame != null) await waitForExistingGame(token);
            return;
        }
        logger.Write("WarmFastPath HeroesRunning=False");
        token.ThrowIfCancellationRequested();
        validateInstallation();
        logger.Write($"StormHeroesLauncher {AboutSafetyContent.Version}：确保 UU 运行并启动目标游戏加速。");
        var status = await startBoost(token);
        token.ThrowIfCancellationRequested();
        if (!status.IsReady) throw new InvalidOperationException("目标游戏尚未确认 isBoosting=true 且 status=boosting。");
        token.ThrowIfCancellationRequested();
        logger.Write("目标游戏加速已确认，开始检查 Battle.net。");
        bool reused = reuseBattleNet != null && await reuseBattleNet(token);
        token.ThrowIfCancellationRequested();
        if (!reused) await ensureBattleNet(token);
        else logger.Write("WarmFastPath Decision=LaunchHeroesDirectly");
        token.ThrowIfCancellationRequested();
        await launchHeroes(token);
        logger.Write("启动成功：已确认稳定游戏主窗口（不代表已登录服务器）。");
    }
}
