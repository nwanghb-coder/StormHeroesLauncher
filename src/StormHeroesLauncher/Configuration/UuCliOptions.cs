using System.IO;
using System.Text.Json;

namespace StormHeroesLauncher.Configuration;

public sealed record UuCliOptions
{
    public string CliPath { get; init; } = Path.Combine(AppContext.BaseDirectory, "tools", "uu-cli.exe");
    public string GameId { get; init; } = "569cbb26a26c753e42982639";
    public string ZoneId { get; init; } = "5e84381b04c2150cc09e485e";
    public string ServerId { get; init; } = "57a3ec73a26c752383c56217";
    public int CommandTimeoutSeconds { get; init; } = 20;
    public int ReadinessTimeoutSeconds { get; init; } = 15;
    public int BoostTimeoutSeconds { get; init; } = 30;
    public int PollIntervalMilliseconds { get; init; } = 1000;

    public static UuCliOptions Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "uu-cli.settings.json");
        var options = File.Exists(path)
            ? JsonSerializer.Deserialize<UuCliOptions>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("CLI 配置不能为空。")
            : new UuCliOptions();
        options.Validate();
        return options;
    }

    public void Validate()
    {
        if (!Path.IsPathFullyQualified(CliPath) ||
            new[] { GameId, ZoneId, ServerId }.Any(string.IsNullOrWhiteSpace) ||
            CommandTimeoutSeconds is < 1 or > 120 ||
            ReadinessTimeoutSeconds is < 1 or > 120 ||
            BoostTimeoutSeconds is < 1 or > 120 ||
            PollIntervalMilliseconds is < 100 or > 5000)
            throw new InvalidDataException("CLI 配置无效：请检查绝对路径、游戏/区服 ID 和超时范围。");
    }
}
