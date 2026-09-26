namespace StormHeroesLauncher.Observer;

public sealed record VersionPoint(string Path, string? FileVersion, string? ProductVersion);
public sealed record UpdateEvidence(string UpdateSessionId, string State, string Confidence, bool Inferred,
    double FirstEvidenceMs, double DurationMs, string[] Evidence);

public sealed class ObserverEngine(IObserverSink sink, IObserverClock clock, string? sessionId = null)
{
    public string SessionId { get; } = sessionId ?? Guid.NewGuid().ToString("N");
    private readonly DateTimeOffset started = clock.UtcNow;
    private Dictionary<string, RelatedProcess> processes = new();
    private Dictionary<string, WindowMetadata> windows = new();
    private readonly Dictionary<string, FileMetadata> files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Family, VersionPoint> startVersions = new(), endVersions = new();
    private readonly Dictionary<Family, double> lastMainExit = new();
    private readonly Dictionary<Family, Correlation> correlations = new();
    private int processEvents, versionChanges, windowClassChanges;
    private bool baseline = true, summarized, possibleUpdate;
    private string confidence = "Low";
    public void Emit(Family source, string name, object data) => sink.Write(new(ObserverJson.SchemaVersion,
        clock.UtcNow, clock.Elapsed.TotalMilliseconds, source, name, SessionId, data));
    public void Start() => Emit(Family.Observer, "ObserverStarted", new
    {
        product = "HOSLauncher", observerBuild = true, version = Services.AboutSafetyContent.Version, pollMilliseconds = 500,
        maximumLifetimeSeconds = 900, afterGameSeconds = 120, commandLinesCollected = false, windowTitlesCollected = false
    });
    public bool ProcessSnapshot(IReadOnlyList<RelatedProcess> snapshot)
    {
        var next = snapshot.ToDictionary(p => p.Identity.Key);
        bool changed = false;
        foreach (var (key, p) in processes)
            if (!next.ContainsKey(key))
            {
                Emit(p.Family, "ProcessExited", new ProcessEvent(p, false, clock.Elapsed.TotalMilliseconds));
                processEvents++; changed = true;
                if (IsMain(p) && !next.Values.Any(n => n.RelatedTo == p.RelatedTo && IsMain(n) && processes.ContainsKey(n.Identity.Key)))
                    lastMainExit[p.RelatedTo] = clock.Elapsed.TotalMilliseconds;
            }
        foreach (var (key, p) in next)
        {
            if (!processes.ContainsKey(key))
            {
                Emit(p.Family, "ProcessStarted", new ProcessEvent(p, baseline, clock.Elapsed.TotalMilliseconds));
                processEvents++; changed = true;
                if (!baseline && IsMain(p) && lastMainExit.TryGetValue(p.RelatedTo, out double exit) && clock.Elapsed.TotalMilliseconds - exit <= 60_000)
                { Evidence(p.RelatedTo, "RestartSequence"); lastMainExit.Remove(p.RelatedTo); }
                if (!baseline && !IsMain(p) && correlations.ContainsKey(p.RelatedTo)) Evidence(p.RelatedTo, "RelatedProcessChange");
            }
        }
        foreach (var group in next.Values.Where(IsMain).GroupBy(p => p.RelatedTo))
        {
            var active = group.OrderByDescending(p => p.Identity.CreatedFileTime).First();
            if (active.Metadata is { Status: "Present", FileVersion: not null } m &&
                (!processes.TryGetValue(active.Identity.Key, out var old) || old.Metadata != m))
                Version(group.Key, new(m.Path, m.FileVersion, m.ProductVersion));
        }
        processes = next; baseline = false;
        return changed;
    }
    private static bool IsMain(RelatedProcess p) => p.Family == Family.UU && p.Identity.Name.Equals("uu.exe", StringComparison.OrdinalIgnoreCase) ||
        p.Family == Family.BattleNet && p.Identity.Name.Equals("Battle.net.exe", StringComparison.OrdinalIgnoreCase);
    private void Version(Family family, VersionPoint value)
    {
        if (!startVersions.ContainsKey(family)) startVersions[family] = value;
        if (endVersions.TryGetValue(family, out var old) && old != value &&
            System.IO.Path.GetFileName(old.Path).Equals(System.IO.Path.GetFileName(value.Path), StringComparison.OrdinalIgnoreCase) &&
            (old.FileVersion != value.FileVersion || old.ProductVersion != value.ProductVersion))
        {
            versionChanges++;
            Emit(family, "ActiveVersionChanged", new { previous = old, current = value });
            Evidence(family, "VersionChanged");
        }
        endVersions[family] = value;
    }
    public void FileSnapshot(IReadOnlyList<(Family Family, FileMetadata File)> snapshot, string checkpoint)
    {
        var currentPaths = snapshot.Select(s => s.File.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // A successful bounded snapshot is authoritative for its previously observed paths.
        foreach (var missing in files.Values.Where(f => !currentPaths.Contains(f.Path) && f.Status == "Present").ToArray())
        {
            var absent = missing with { Status = "Missing", FileVersion = null, ProductVersion = null, Size = null, LastWriteUtc = null };
            Family family = fileFamilies[missing.Path];
            Emit(family, "InstallationChanged", new FileEvent(absent, missing, checkpoint));
            files[missing.Path] = absent;
            if (missing.Kind == "File") Evidence(family, "FileMetadataChanged");
        }
        foreach (var (family, file) in snapshot)
        {
            files.TryGetValue(file.Path, out var previous);
            Emit(family, "FileSnapshot", new FileEvent(file, previous, checkpoint));
            if (previous != null && previous != file && file.Status != "Unavailable" && previous.Status != "Unavailable")
            {
                Emit(family, "InstallationChanged", new FileEvent(file, previous, checkpoint));
                if (file.Kind == "File")
                {
                    bool version = previous.Status == "Present" && file.Status == "Present" &&
                        previous.FileVersion != null && file.FileVersion != null &&
                        (previous.FileVersion != file.FileVersion || previous.ProductVersion != file.ProductVersion);
                    if (version) versionChanges++;
                    Evidence(family, version ? "VersionChanged" : "FileMetadataChanged");
                }
            }
            else if (previous == null && checkpoint != "Startup" && file.Status == "Present")
                Emit(family, "InstallationPathObserved", new FileEvent(file, null, checkpoint));
            files[file.Path] = file;
            fileFamilies[file.Path] = family;
            string name = System.IO.Path.GetFileName(file.Path);
            if (file.Status == "Present" && file.FileVersion != null &&
                (family == Family.UU && (name.Equals("uu_launcher.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("uu.exe", StringComparison.OrdinalIgnoreCase)) ||
                 family == Family.BattleNet && name.Equals("Battle.net.exe", StringComparison.OrdinalIgnoreCase)) &&
                (!endVersions.TryGetValue(family, out var active) || active.Path.Equals(file.Path, StringComparison.OrdinalIgnoreCase)))
                Version(family, new(file.Path, file.FileVersion, file.ProductVersion));
        }
    }
    private readonly Dictionary<string, Family> fileFamilies = new(StringComparer.OrdinalIgnoreCase);
    public void WindowSnapshot(IReadOnlyList<WindowMetadata> snapshot)
    {
        var next = snapshot.Where(w => w.Visible).ToDictionary(w => $"{w.ProcessKey}:{w.Hwnd}");
        foreach (var (key, value) in next)
        {
            if (!windows.TryGetValue(key, out var old) || old != value)
            {
                Emit(value.Family, "WindowChanged", new WindowEvent(value, old == null ? "VisibleObserved" : "ClassOrStateChanged"));
                if (old == null || old.ClassName != value.ClassName)
                {
                    windowClassChanges++;
                    if (correlations.ContainsKey(value.RelatedTo)) Evidence(value.RelatedTo, "WindowClassChanged");
                }
            }
        }
        foreach (var (key, value) in windows)
            if (!next.ContainsKey(key)) Emit(value.Family, "WindowChanged", new WindowEvent(value, "NoLongerObservedVisible"));
        windows = next;
    }
    private sealed class Correlation(double first)
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public double First = first, Last = first;
        public HashSet<string> Evidence = new();
    }
    private void Evidence(Family family, string evidence)
    {
        if (family is not (Family.UU or Family.BattleNet)) return;
        double now = clock.Elapsed.TotalMilliseconds;
        if (!correlations.TryGetValue(family, out var group)) correlations[family] = group = new(now);
        group.Last = now;
        if (!group.Evidence.Add(evidence)) return;
        possibleUpdate = true;
        string level = Confidence(group);
        if (level == "High" || level == "Medium" && confidence != "High") confidence = level;
        string state = evidence == "VersionChanged" ? "VersionChanged" : evidence == "RestartSequence" ? "ApplicationRestarting" : "PossibleUpdateDetected";
        Emit(family, "UpdateSession", Data(group, state, now));
    }
    private static string Confidence(Correlation group) => group.Evidence.Contains("VersionChanged") ? "High"
        : group.Evidence.Contains("FileMetadataChanged") && group.Evidence.Contains("RestartSequence") ? "Medium" : "Low";
    private static UpdateEvidence Data(Correlation group, string state, double now) =>
        new(group.Id, state, Confidence(group), true, group.First, now - group.First, group.Evidence.Order().ToArray());
    public void AdvanceCorrelations(bool final = false)
    {
        double now = clock.Elapsed.TotalMilliseconds;
        foreach (var (family, group) in correlations.ToArray())
        {
            bool complete = now - group.Last >= 15_000 && group.Evidence.Contains("VersionChanged") &&
                group.Evidence.Contains("RestartSequence") && processes.Values.Any(p => p.RelatedTo == family && IsMain(p));
            if (complete || final || now - group.Last >= 30_000 || now - group.First >= 120_000)
            {
                Emit(family, "UpdateSession", Data(group, complete ? "UpdateCompleted" : "Unresolved", now));
                correlations.Remove(family);
            }
        }
    }
    public void Summary(string reason)
    {
        if (summarized) return;
        summarized = true;
        AdvanceCorrelations(true);
        Emit(Family.Observer, "SessionSummary", new
        {
            observerSessionId = SessionId, startTime = started, endTime = clock.UtcNow,
            observedDurationMs = clock.Elapsed.TotalMilliseconds, reason,
            uuStartVersion = startVersions.GetValueOrDefault(Family.UU), uuEndVersion = endVersions.GetValueOrDefault(Family.UU),
            battleNetStartVersion = startVersions.GetValueOrDefault(Family.BattleNet), battleNetEndVersion = endVersions.GetValueOrDefault(Family.BattleNet),
            possibleUpdateDetected = possibleUpdate, confidence, processEvents, versionChanges, windowClassChanges,
            observationOnly = true
        });
    }
}
