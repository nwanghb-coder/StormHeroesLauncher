using StormHeroesLauncher.Configuration;
using StormHeroesLauncher.Models;

namespace StormHeroesLauncher.Services;

public sealed class HeroesBoostWorkflow(Func<CancellationToken, Task<string>> ensureUu,
    IUuCliService cli, UuCliOptions options, AppLogger logger, LaunchProgress? progress = null,
    Func<CancellationToken, Task<bool>>? isUuRunning = null)
{
    public async Task<HeroesBoostStatus> StartAsync(Action<string> reportUu, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (isUuRunning != null)
        {
            bool running = await isUuRunning(token);
            logger.Write($"WarmFastPath UUProcessRunning={running}");
            if (running)
            {
                try
                {
                    var existing = await cli.GetHeroesBoostStatusAsync(token);
                    token.ThrowIfCancellationRequested();
                    bool reusable = existing.Matches(options) && await isUuRunning(token);
                    LogEvidence(existing, reusable);
                    if (reusable)
                    { logger.Write("WarmFastPath UU=AlreadyBoosting UUTargetBoosting=True"); return existing; }
                    logger.Write("WarmFastPath Decision=Fallback Reason=UuTargetNotConfirmed");
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex) { logger.Write($"WarmFastPath Decision=Fallback Reason=UuStatusUnknown ErrorType={ex.GetType().Name}"); }
            }
        }
        token.ThrowIfCancellationRequested();
        reportUu("UU 加速器：正在检测 / 等待启动…");
        var result = await ensureUu(token);
        token.ThrowIfCancellationRequested();
        reportUu(result);
        if (result is not ("UU 加速器：已运行" or "UU 加速器：启动成功"))
            throw new InvalidOperationException(result);
        token.ThrowIfCancellationRequested();
        progress?.Report(LaunchState.PreparingUU);
        var status = await PollAsync(readyOnly: true, token);
        token.ThrowIfCancellationRequested();
        LogEvidence(status, status.Matches(options));
        if (status.Matches(options))
        { logger.Write("WarmFastPath UU=AlreadyBoosting UUTargetBoosting=True"); return status; }
        // Never retry start automatically: an uncertain response may already have changed acceleration.
        progress?.Report(LaunchState.Boosting);
        token.ThrowIfCancellationRequested();
        await cli.StartHeroesBoostAsync(token);
        return await PollAsync(readyOnly: false, token);
    }

    private void LogEvidence(HeroesBoostStatus status, bool reusable)
    {
        string zone = HeroesBoostStatus.Evidence(status.ZoneId, options.ZoneId);
        string server = HeroesBoostStatus.Evidence(status.ServerId, options.ServerId);
        logger.Write($"WarmFastPath GameMatch={status.GameId == options.GameId} ZoneEvidence={zone} ServerEvidence={server} " +
            $"Contradiction={zone == "PresentMismatch" || server == "PresentMismatch"} Decision={(reusable ? "Reuse" : "Fallback")}");
    }

    public Task<HeroesBoostStatus> WaitStoppedAsync(CancellationToken token) =>
        PollAsync(readyOnly: false, token, stopping: true);

    private async Task<HeroesBoostStatus> PollAsync(bool readyOnly, CancellationToken token, bool stopping = false)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(readyOnly ? options.ReadinessTimeoutSeconds : options.BoostTimeoutSeconds));
        try
        {
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                try
                {
                    var status = await cli.GetHeroesBoostStatusAsync(timeout.Token);
                    if (readyOnly || (stopping ? !status.IsBoosting && status.Status == "not_boosting" : status.IsReady))
                    {
                        logger.Write(readyOnly ? "UU CLI 已就绪。" : stopping ? "已确认目标游戏停止加速。" : "已确认目标游戏加速就绪。");
                        return status;
                    }
                }
                catch (UuCliException ex) when (ex.Kind is CliFailureKind.Rejected or CliFailureKind.Timeout)
                {
                    logger.Write($"等待状态期间暂时失败：{ex.Kind}；仅重试 status。");
                }
                await Task.Delay(options.PollIntervalMilliseconds, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            var message = readyOnly ? "UU 加速器尚未就绪，等待超时。" : "加速状态确认超时，请刷新状态确认实际结果。";
            logger.Write(message);
            throw new TimeoutException(message);
        }
    }
}
