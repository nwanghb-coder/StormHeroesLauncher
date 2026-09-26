using System.Text.Json;
using System.Text.Json.Serialization;

namespace StormHeroesLauncher.Observer;

public enum Family { UU, BattleNet, UnknownRelated, Observer }
public sealed record ProcessIdentity(uint Pid, uint ParentPid, string Name, string? ExecutablePath,
    uint WindowsSessionId, long? CreatedFileTime)
{
    public string Key => $"{Pid}:{CreatedFileTime}:{Name}";
}
public sealed record FileMetadata(string Path, string Kind, string Status, long? Size = null,
    DateTimeOffset? LastWriteUtc = null, string? FileVersion = null, string? ProductVersion = null,
    string? Company = null, string? ProductName = null);
public sealed record RelatedProcess(ProcessIdentity Identity, Family Family, Family RelatedTo,
    string Relation, string? ExecutableDirectory, FileMetadata? Metadata);
public sealed record WindowMetadata(uint Pid, string ProcessKey, string? ExecutablePath, long Hwnd,
    string ClassName, bool Visible, bool Enabled, long OwnerHwnd, uint? OwnerPid, Family Family, Family RelatedTo);
public sealed record ProcessEvent(RelatedProcess Process, bool PresentAtStart, double ObservedAtMs);
public sealed record FileEvent(FileMetadata File, FileMetadata? Previous, string Checkpoint);
public sealed record WindowEvent(WindowMetadata Window, string Change);
public sealed record ObserverEvent(int SchemaVersion, DateTimeOffset Timestamp, double ElapsedMs,
    Family Source, string Event, string SessionId, object Data);

public static class ObserverJson
{
    public const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
    public static string Serialize(ObserverEvent value) => JsonSerializer.Serialize(value, Options);
}

public interface IObserverSink : IDisposable { void Write(ObserverEvent value); }
public interface IObserverClock
{
    DateTimeOffset UtcNow { get; }
    TimeSpan Elapsed { get; }
    Task Delay(TimeSpan delay, CancellationToken token);
}
public interface IObserverSource
{
    IReadOnlyList<RelatedProcess> Processes();
    IReadOnlyList<WindowMetadata> Windows(IReadOnlyList<RelatedProcess> processes);
    IReadOnlyList<(Family Family, FileMetadata File)> Files(IReadOnlyList<RelatedProcess> processes);
}
