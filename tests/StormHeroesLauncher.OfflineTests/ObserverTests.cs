using System.Text.Json;
using StormHeroesLauncher.Observer;

public static class ObserverTests
{
    public static async Task Run(Action<bool, string> check)
    {
        var clock = new FakeObserverClock();
        var sink = new MemoryObserverSink();
        var engine = new ObserverEngine(sink, clock);
        var files = new FakeObserverFiles();
        var scope = new InstallationScope(@"C:\UU\uu_launcher.exe", @"C:\Battle.net\Battle.net.exe", files);
        ProcessIdentity P(uint pid, uint parent, string name, string path, long created = 100, uint session = 1) => new(pid, parent, name, path, session, created);
        var uu = P(10, 1, "uu.exe", @"C:\UU\100\uu.exe");
        var child = P(11, 10, "new-updater.exe", @"C:\Temp\new-updater.exe", 110);
        var selected = ProcessSelection.Select([uu, child, P(12, 1, "uu.exe", @"C:\Other\uu.exe"),
            P(13, 10, "HeroesOfTheStorm_x64.exe", @"C:\Game\HeroesOfTheStorm_x64.exe", 120),
            P(14, 1, "uu.exe", @"C:\UU\100\uu.exe", 100, 2),
            P(15, 10, "old-child.exe", @"C:\Other\old.exe", 90)], scope, 1, scope.ReadProcessFile);
        check(selected.Count == 2 && selected[1].Family == Family.UnknownRelated && selected[1].RelatedTo == Family.UU,
            "root process and unknown child observed; unrelated, game, other-session and recycled-parent candidates ignored");
        check(selected[1].Metadata == null && files.ReadPaths.All(p => p.StartsWith(@"C:\UU\")), "outside-root child has no external file metadata read");
        check(scope.Classify(@"C:\UU-Other\uu.exe") == null && scope.Classify(@"C:\UU\..\Other\uu.exe") == null,
            "installation containment rejects prefix collisions and traversal");
        files.Denied = @"C:\UU\junction";
        check(scope.Classify(@"C:\UU\junction\uu.exe") == null, "reparse-like denied path ignored");
        bool rejectedRoot = false;
        try { _ = new InstallationScope(@"C:\uu_launcher.exe", @"C:\Battle.net\Battle.net.exe", files); } catch (InvalidDataException) { rejectedRoot = true; }
        check(rejectedRoot, "drive root rejected before observation");
        files.ReadPaths.Clear();
        var snapshot = scope.Snapshot(selected);
        check(files.Enumerated.SequenceEqual(new[] { @"C:\UU", @"C:\Battle.net" }) &&
            !files.ReadPaths.Any(p => p.Contains("unrelated") || p.Contains("nested") || p.Contains("Temp")),
            "only two configured roots enumerated nonrecursively; unknown folders never inspected");
        check(snapshot.Any(s => s.File.Path == @"C:\UU\100\netease-uu-booster.7z") &&
            snapshot.Any(s => s.File.Path == @"C:\Battle.net\Battle.net.123\Battle.net.exe"), "known version folders and booster archive metadata included");
        var inRootHelper = selected[0] with { Identity = P(30, 10, "patcher.exe", @"C:\UU\patcher.exe", 150) };
        scope.Snapshot([inRootHelper]);
        check(scope.Snapshot([]).Any(s => s.File.Path == @"C:\UU\patcher.exe" && s.File.Status == "Present"),
            "helper file continues to be observed after process exit; exit is not treated as deletion");
        engine.Start();
        engine.ProcessSnapshot(selected);
        engine.FileSnapshot(snapshot, "Startup");
        var window = new WindowMetadata(10, uu.Key, uu.ExecutablePath, 123, "UUMAINFORMV40", true, true, 0, null, Family.UU, Family.UU);
        engine.WindowSnapshot([window]);
        engine.AdvanceCorrelations();
        check(!sink.Events.Any(e => e.Event == "UpdateSession"), "baseline helper and window appearance alone never imply an update");
        clock.Advance(1000);
        engine.ProcessSnapshot([]);
        clock.Advance(1000);
        var updated = selected[0] with { Identity = uu with { Pid = 20, CreatedFileTime = 200 }, Metadata = selected[0].Metadata! with { FileVersion = "2.0", ProductVersion = "2.0" } };
        engine.ProcessSnapshot([updated]);
        engine.WindowSnapshot([window with { Pid = 20, ProcessKey = updated.Identity.Key, ClassName = "NewUUClass" }]);
        check(sink.Events.Any(e => e.Event == "ActiveVersionChanged") && sink.Events.Any(e => e.Event == "ProcessExited"), "restart and version transition recorded separately");
        clock.Advance(16000);
        engine.AdvanceCorrelations();
        check(sink.Events.Where(e => e.Event == "UpdateSession").Select(e => (UpdateEvidence)e.Data).Any(e => e.State == "UpdateCompleted" && e.Confidence == "High" && e.Inferred),
            "version change plus restart plus quiet running app produces explicitly inferred completion");
        engine.Summary("TestFinished"); engine.Summary("Duplicate");
        check(sink.Events.Count(e => e.Event == "SessionSummary") == 1, "exactly one session summary");
        foreach (string line in sink.Events.Select(ObserverJson.Serialize))
        {
            using var json = JsonDocument.Parse(line);
            check(json.RootElement.GetProperty("schemaVersion").GetInt32() == 1 && !line.Contains('\n') &&
                json.RootElement.GetProperty("sessionId").GetString() == engine.SessionId, "independently valid versioned JSONL event");
        }
        using (var json = JsonDocument.Parse(ObserverJson.Serialize(sink.Events.Last())))
        {
            var data = json.RootElement.GetProperty("data");
            check(data.GetProperty("possibleUpdateDetected").GetBoolean() && data.GetProperty("processEvents").GetInt32() >= 3 &&
                data.GetProperty("uuStartVersion").GetProperty("fileVersion").GetString() == "1.0" &&
                data.GetProperty("uuEndVersion").GetProperty("fileVersion").GetString() == "2.0", "summary includes timing, counts and start/end versions");
        }
        string[] forbidden = ["password", "token", "cookie", "credential", "commandLine", "windowTitle", "clipboard", "accountId", "packet"];
        IEnumerable<string> Keys(JsonElement item)
        {
            if (item.ValueKind == JsonValueKind.Object) foreach (var p in item.EnumerateObject()) { yield return p.Name; foreach (var key in Keys(p.Value)) yield return key; }
            else if (item.ValueKind == JsonValueKind.Array) foreach (var childElement in item.EnumerateArray()) foreach (var key in Keys(childElement)) yield return key;
        }
        // The two false collection-policy booleans are not payload fields.
        check(sink.Events.Where(e => e.Event != "ObserverStarted").All(e =>
        { using var json = JsonDocument.Parse(ObserverJson.Serialize(e)); return !Keys(json.RootElement).Any(k => forbidden.Contains(k, StringComparer.OrdinalIgnoreCase)); }),
            "event payload schemas contain no secret/content fields");
        check(sink.Events.Any(e => e.Data is ProcessEvent) && sink.Events.Any(e => e.Data is FileEvent) && sink.Events.Any(e => e.Data is WindowEvent),
            "process, version snapshot and window serialization exercised");

        var budget = new ObserverBudget();
        check(!budget.Expired(TimeSpan.FromSeconds(899)) && budget.Expired(TimeSpan.FromSeconds(900)), "15 minute hard observer budget");
        budget.GameDetected(TimeSpan.FromSeconds(20)); budget.GameDetected(TimeSpan.FromSeconds(30));
        check(budget.Deadline == TimeSpan.FromSeconds(140) && budget.Expired(TimeSpan.FromSeconds(140)), "post-game two-minute bound cannot extend on repeated notification");
        var late = new ObserverBudget(); late.GameDetected(TimeSpan.FromSeconds(890));
        check(late.Deadline == ObserverBudget.Maximum, "post-game tail never exceeds absolute lifetime");

        var runtimeClock = new FakeObserverClock(); var runtimeSink = new MemoryObserverSink();
        var runtimeSource = new FakeObserverSource(); var runtimeBudget = new ObserverBudget(); runtimeBudget.GameDetected(TimeSpan.Zero);
        await new ObserverRuntime(runtimeSource, new(runtimeSink, runtimeClock), runtimeClock, runtimeBudget).RunAsync(CancellationToken.None);
        check(runtimeClock.Elapsed == TimeSpan.FromSeconds(120) && runtimeSource.ProcessCalls == 240, "runtime exits after bounded post-game samples using monotonic clock");
        check(runtimeSource.FileCalls == 13 && runtimeSink.Events.Last().Event == "SessionSummary", "startup, ten-second checkpoints and final snapshot; summary last");

        var failedSink = new MemoryObserverSink(); var failureClock = new FakeObserverClock();
        bool failed = false;
        try { await new ObserverRuntime(new FakeObserverSource { Fail = true }, new(failedSink, failureClock), failureClock, new()).RunAsync(CancellationToken.None); }
        catch (IOException) { failed = true; }
        check(failed && failedSink.Events.Any(e => e.Event == "ObserverFailure") && failedSink.Events.Last().Event == "SessionSummary", "sampler failure still emits failure and final summary");
        check(!failedSink.Events.Select(ObserverJson.Serialize).Any(s => s.Contains("SECRET")), "exception messages are excluded from observer records");
        var reports = new List<string>();
        var host = ObserverHost.ForTest((_, _, _) => throw new IOException("SECRET"), reports.Add);
        var steps = new List<string>();
        var workflow = new StormHeroesLauncher.Services.HeroesLaunchWorkflow(
            _ => { steps.Add("check"); return Task.FromResult(false); },
            _ => { steps.Add("boost"); return Task.FromResult(new StormHeroesLauncher.Models.HeroesBoostStatus(true, "boosting", null, null, null, null, null, null)); },
            _ => { steps.Add("battle"); return Task.CompletedTask; },
            _ => { steps.Add("game"); return Task.CompletedTask; },
            () => steps.Add("validate"), new StormHeroesLauncher.Services.AppLogger(Path.Combine(AppContext.BaseDirectory, "observer-workflow-test.log")));
        await workflow.RunAsync(CancellationToken.None);
        await host.AfterGameAsync();
        check(steps.SequenceEqual(new[] { "check", "validate", "boost", "battle", "game" }) && reports.Any(s => s == "ObserverFailure=IOException") && !reports.Any(s => s.Contains("SECRET")), "observer failure preserves existing launch sequence and never propagates into launch");
        var throwingReporter = ObserverHost.ForTest((_, _, _) => throw new IOException(), _ => throw new Exception());
        await throwingReporter.AfterGameAsync();
        check(true, "diagnostic logger failure also isolated");

        var uncertainClock = new FakeObserverClock(); var uncertainSink = new MemoryObserverSink(); var uncertain = new ObserverEngine(uncertainSink, uncertainClock);
        uncertain.ProcessSnapshot([selected[0]]); uncertain.ProcessSnapshot([]); uncertain.ProcessSnapshot([selected[0] with { Identity = uu with { CreatedFileTime = 300 } }]);
        uncertainClock.Advance(31000); uncertain.AdvanceCorrelations(); uncertain.Summary("Test");
        check(uncertainSink.Events.Where(e => e.Event == "UpdateSession").Select(e => (UpdateEvidence)e.Data).Last().State == "Unresolved", "restart without version evidence remains unresolved");
        var missingClock = new FakeObserverClock(); var missingSink = new MemoryObserverSink(); var missing = new ObserverEngine(missingSink, missingClock);
        missing.FileSnapshot([(Family.UU, new(@"C:\UU\100\uu.exe", "File", "Present", FileVersion: "1"))], "Startup");
        missing.FileSnapshot([], "Periodic");
        check(missingSink.Events.Any(e => e.Event == "InstallationChanged" && ((FileEvent)e.Data).File.Status == "Missing"), "removed installation paths recorded");
        var unavailableSink = new MemoryObserverSink(); var unavailable = new ObserverEngine(unavailableSink, new FakeObserverClock());
        unavailable.FileSnapshot([(Family.UU, new(@"C:\UU\uu.exe", "File", "Present", FileVersion: "1"))], "Startup");
        unavailable.FileSnapshot([(Family.UU, new(@"C:\UU\uu.exe", "File", "Unavailable"))], "Periodic");
        check(!unavailableSink.Events.Any(e => e.Event == "UpdateSession"), "unavailable file metadata does not fabricate update evidence");
        var reusedSink = new MemoryObserverSink(); var reused = new ObserverEngine(reusedSink, new FakeObserverClock());
        reused.ProcessSnapshot([selected[0]]);
        reused.ProcessSnapshot([selected[0] with { Identity = uu with { CreatedFileTime = 500 } }]);
        check(reusedSink.Events.Count(e => e.Event == "ProcessStarted") == 2 && reusedSink.Events.Count(e => e.Event == "ProcessExited") == 1,
            "same PID with a different creation time is a distinct process");
        var overLimit = new InstallationScope(@"C:\UU\uu_launcher.exe", @"C:\Battle.net\Battle.net.exe", new FakeObserverFiles { TooMany = true });
        bool boundedScan = false;
        try { overLimit.Snapshot([]); } catch (InvalidDataException) { boundedScan = true; }
        check(boundedScan, "directory enumeration stops at 65th entry instead of scanning arbitrary trees");
    }
}

sealed class FakeObserverClock : IObserverClock
{
    public TimeSpan Elapsed { get; private set; }
    public DateTimeOffset UtcNow => new DateTimeOffset(2026, 9, 26, 0, 0, 0, TimeSpan.Zero) + Elapsed;
    public void Advance(int ms) => Elapsed += TimeSpan.FromMilliseconds(ms);
    public Task Delay(TimeSpan delay, CancellationToken token) { token.ThrowIfCancellationRequested(); Elapsed += delay; return Task.CompletedTask; }
}
sealed class MemoryObserverSink : IObserverSink
{
    public List<ObserverEvent> Events = [];
    public void Write(ObserverEvent value) => Events.Add(value);
    public void Dispose() { }
}
sealed class FakeObserverFiles : IObserverFiles
{
    public List<string> ReadPaths = [], Enumerated = [];
    public string? Denied;
    public bool TooMany;
    public void ValidatePath(string path) { if (Denied != null && path.StartsWith(Denied)) throw new InvalidDataException(); }
    public IEnumerable<string> ImmediateDirectories(string root)
    {
        Enumerated.Add(root);
        if (TooMany) return Enumerable.Range(1, 10000).Select(n => Path.Combine(root, n.ToString()));
        return root == @"C:\UU" ? [@"C:\UU\100", @"C:\UU\unrelated", @"C:\UU\100\nested"] : [@"C:\Battle.net\Battle.net.123"];
    }
    public FileMetadata Read(string path, bool directory = false)
    { ReadPaths.Add(path); return new(path, directory ? "Directory" : "File", "Present", 100, DateTimeOffset.UnixEpoch, directory ? null : "1.0", directory ? null : "1.0", "Vendor"); }
}
sealed class FakeObserverSource : IObserverSource
{
    public int ProcessCalls, FileCalls;
    public bool Fail;
    public IReadOnlyList<RelatedProcess> Processes() { ProcessCalls++; if (Fail) throw new IOException("SECRET"); return []; }
    public IReadOnlyList<WindowMetadata> Windows(IReadOnlyList<RelatedProcess> processes) => [];
    public IReadOnlyList<(Family Family, FileMetadata File)> Files(IReadOnlyList<RelatedProcess> processes) { FileCalls++; return []; }
}
