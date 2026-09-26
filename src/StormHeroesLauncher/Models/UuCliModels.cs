namespace StormHeroesLauncher.Models;

public sealed class CliEnvelope<T>
{
    public bool? Success { get; init; }
    public T? Data { get; init; }
    public CliError? Error { get; init; }
}

public sealed class CliError
{
    public string? Code { get; init; }
    public string? Message { get; init; }
}

public sealed class BoostOperationData
{
    public string? GameId { get; init; }
    public string? ZoneId { get; init; }
    public string? ServerId { get; init; }
    public string? Message { get; init; }
}

public class BoostDetails
{
    public string? GameId { get; init; }
    public string? ZoneId { get; init; }
    public string? ServerId { get; init; }
    public string? GameName { get; init; }
    public string? Status { get; init; }
    public string? RegionId { get; init; }
    public string? RegionName { get; init; }
    public string? NodeId { get; init; }
    public string? NodeName { get; init; }
    public string? NodeMode { get; init; }
    public double? Ping { get; init; }
    public double? PacketLoss { get; init; }
}

public sealed class BoostStatusData : BoostDetails
{
    public bool? IsBoosting { get; init; }
    public List<BoostDetails>? Boosters { get; init; }
}

public sealed record HeroesBoostStatus(bool IsBoosting, string Status, string? GameName,
    string? NodeName, string? NodeId, string? NodeMode, double? Ping, double? PacketLoss)
{
    public bool IsReady => IsBoosting && string.Equals(Status, "boosting", StringComparison.OrdinalIgnoreCase);
    public string? GameId { get; init; }
    public string? ZoneId { get; init; }
    public string? ServerId { get; init; }
    public static string Evidence(string? actual, string expected) => actual == null ? "Missing" : actual == expected ? "PresentMatch" : "PresentMismatch";
    public bool Matches(Configuration.UuCliOptions options) => IsReady && GameId == options.GameId &&
        (ZoneId == null || ZoneId == options.ZoneId) && (ServerId == null || ServerId == options.ServerId);
}

public enum CliFailureKind { MissingExecutable, LaunchFailed, Timeout, InvalidData, Rejected }

public sealed class UuCliException(CliFailureKind kind, string message, int? exitCode = null,
    string? errorCode = null) : Exception(message)
{
    public CliFailureKind Kind { get; } = kind;
    public int? ExitCode { get; } = exitCode;
    public string? ErrorCode { get; } = errorCode;
}
