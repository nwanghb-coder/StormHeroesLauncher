using System.Text.Json;
using StormHeroesLauncher.Configuration;
using StormHeroesLauncher.Models;

namespace StormHeroesLauncher.Services;

public interface IUuCliService
{
    Task<BoostOperationData> StartHeroesBoostAsync(CancellationToken cancellationToken = default);
    Task<HeroesBoostStatus> GetHeroesBoostStatusAsync(CancellationToken cancellationToken = default);
    Task<BoostOperationData> StopHeroesBoostAsync(CancellationToken cancellationToken = default);
}

public sealed class UuCliService(UuCliOptions options, AppLogger logger, ICliProcessRunner runner) : IUuCliService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<BoostOperationData> StartHeroesBoostAsync(CancellationToken cancellationToken = default)
    {
        var result = await InvokeAsync<BoostOperationData>("start",
            ["--id", options.GameId, "--zone", options.ZoneId, "--server", options.ServerId], cancellationToken);
        VerifyGameId(result.GameId);
        return result;
    }

    public async Task<BoostOperationData> StopHeroesBoostAsync(CancellationToken cancellationToken = default)
    {
        var result = await InvokeAsync<BoostOperationData>("stop", ["--id", options.GameId], cancellationToken);
        VerifyGameId(result.GameId);
        return result;
    }

    public async Task<HeroesBoostStatus> GetHeroesBoostStatusAsync(CancellationToken cancellationToken = default)
    {
        var data = await InvokeAsync<BoostStatusData>("status", ["--id", options.GameId], cancellationToken);
        var status = NormalizeStatus(data, options.GameId);
        logger.Write($"加速状态：isBoosting={status.IsBoosting}, status={status.Status}, " +
            $"node={status.NodeName ?? "-"}, ping={status.Ping?.ToString() ?? "-"} ms, packetLoss={status.PacketLoss?.ToString() ?? "-"}%");
        return status;
    }

    public static HeroesBoostStatus NormalizeStatus(BoostStatusData data, string expectedGameId)
    {
        if (data.IsBoosting is null) throw Invalid();
        BoostDetails? selected = data;
        if (data.Boosters is not null)
        {
            if (data.Boosters.Any(x => x is null || string.IsNullOrWhiteSpace(x.GameId))) throw Invalid();
            var matches = data.Boosters.Where(x => x.GameId == expectedGameId).ToList();
            if (matches.Count > 1) throw Invalid();
            selected = matches.SingleOrDefault();
            // isBoosting at the root may describe another game's acceleration.
            if (selected is null)
                return new(false, "not_boosting", null, null, null, null, null, null);
        }
        else if (data.GameId is not null && data.GameId != expectedGameId) throw Invalid();

        if (string.IsNullOrWhiteSpace(selected.Status))
        {
            if (data.IsBoosting == true) throw Invalid();
            return new(false, "not_boosting", selected.GameName, null, null, null, null, null);
        }
        var state = selected.Status.ToLowerInvariant();
        if (state is not ("boosting" or "starting" or "stopping" or "not_boosting")) throw Invalid();
        if (data.IsBoosting == false && state == "boosting") throw Invalid();
        if (selected.Ping is < 0 || selected.PacketLoss is < 0 or > 100) throw Invalid();
        return new(data.IsBoosting == true && state != "not_boosting", state,
            selected.GameName, selected.NodeName, selected.NodeId, selected.NodeMode,
            selected.Ping, selected.PacketLoss);
    }

    private void VerifyGameId(string? id)
    {
        if (id is not null && id != options.GameId)
        {
            logger.Write("CLI 返回的 gameId 与配置不一致，拒绝报告成功。");
            throw Invalid();
        }
    }

    private async Task<T> InvokeAsync<T>(string operation, string[] arguments, CancellationToken token) where T : class
    {
        string[] allArguments = ["--json", operation, .. arguments];
        logger.WriteOperation($"CLI {operation}", new { path = options.CliPath, arguments = allArguments });
        try
        {
            var output = await runner.RunAsync(options.CliPath, allArguments,
                TimeSpan.FromSeconds(options.CommandTimeoutSeconds), token);
            // Capture raw streams in memory, but do not persist arbitrary CLI output (may contain secrets).
            logger.Write($"CLI {operation}: exitCode={output.ExitCode}, stdoutChars={output.Stdout.Length}, stderrChars={output.Stderr.Length}");
            CliEnvelope<T>? envelope;
            try { envelope = JsonSerializer.Deserialize<CliEnvelope<T>>(output.Stdout.TrimStart('\uFEFF'), JsonOptions); }
            catch (JsonException) { throw Invalid(output.ExitCode); }
            if (envelope?.Success is null) throw Invalid(output.ExitCode);
            if (envelope.Success == false)
                throw new UuCliException(CliFailureKind.Rejected, "UU CLI 报告操作失败，请确认 UU 已就绪后重试。",
                    output.ExitCode, SafeCode(envelope.Error?.Code));
            if (envelope.Data is null) throw Invalid(output.ExitCode);
            logger.Write($"CLI {operation}: JSON success=true");
            return envelope.Data;
        }
        catch (UuCliException ex)
        {
            logger.Write($"CLI {operation} 失败：kind={ex.Kind}, code={ex.ErrorCode ?? "-"}, exitCode={ex.ExitCode?.ToString() ?? "-"}");
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.Write($"CLI {operation} 已取消；请求可能已生效，请刷新状态；未自动发送 stop。");
            throw;
        }
    }

    private static string? SafeCode(string? code) => code is not null && code.Length <= 80 &&
        code.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? code : null;
    private static UuCliException Invalid(int? exit = null) =>
        new(CliFailureKind.InvalidData, "UU CLI 返回了无效数据。", exit);
}
