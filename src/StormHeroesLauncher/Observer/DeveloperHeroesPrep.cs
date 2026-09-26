using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using StormHeroesLauncher.Services;

namespace StormHeroesLauncher.Observer;

// Compiled only in developer builds. Direct Switcher test, never UU/Battle.net launch orchestration.
public static class DeveloperHeroesPrep
{
    public static bool Owns(HeroesProcessIdentity game, HeroesProcessIdentity switcher, long switcherExit,
        long testStart, IReadOnlySet<uint> before, string switcherPath) =>
        HeroesUiReadiness.IsDirectChild(game, switcher, switcherExit, testStart, before) &&
        HeroesWindowProbe.IsGame(game, switcherPath);

    public static async Task<int> RunAsync(string switcherPath)
    {
        if (!Path.IsPathFullyQualified(switcherPath) || !File.Exists(switcherPath) ||
            !Path.GetFileName(switcherPath).Equals("HeroesSwitcher_x64.exe", StringComparison.OrdinalIgnoreCase)) return 2;
        SafeCliPaths.NoReparse(switcherPath);
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StormHeroesLauncher", "Diagnostics", "HeroesPrep");
        SafeCliPaths.NoReparse(directory); Directory.CreateDirectory(directory); SafeCliPaths.NoReparse(directory);
        string session = Guid.NewGuid().ToString("N");
        using var output = new StreamWriter(new FileStream(Path.Combine(directory, $"heroes-prep-{DateTime.Now:yyyy-MM-dd-HHmmss}-{session}.jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
        var timer = Stopwatch.StartNew();
        void Log(string name, object data) => output.WriteLine(JsonSerializer.Serialize(new { schemaVersion = 1, sessionId = session, timestamp = DateTimeOffset.UtcNow, elapsedMs = timer.ElapsedMilliseconds, @event = name, data }));
        var before = HeroesWindowProbe.Processes().Select(p => p.Pid).ToHashSet();
        Log("TestStarted", new { switcherPath, existingRelevantPids = before, maximumSeconds = 90, arguments = "None" });
        long start = DateTime.UtcNow.ToFileTimeUtc();
        using var switcher = Process.Start(new ProcessStartInfo(switcherPath) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(switcherPath)! });
        if (switcher == null) return 3;
        // Keep the original process handle: creation/exit times prove the parent lifetime, even after exit.
        IntPtr parentHandle = switcher.Handle;
        if (!HeroesWindowProbe.GetProcessTimes(parentHandle, out long created, out _, out _, out _))
        { Log("ParentIdentityUnavailableManualCleanup", new { switcher.Id }); return 4; }
        uint ownSession = (uint)Process.GetCurrentProcess().SessionId;
        var parent = new HeroesProcessIdentity((uint)switcher.Id, 0, created, ownSession, switcherPath);
        Log("SwitcherStarted", parent);
        var owned = new Dictionary<uint, HeroesProcessIdentity>();
        var prior = new Dictionary<long, HeroesWindowIdentity>();
        var attempts = new Dictionary<long, int>();
        bool ambiguity = false, ready = false, failed = false, cleanupComplete = true;
        long candidateSince = 0, candidateHwnd = 0, readyAt = 0;
        var productionReadiness = new HeroesUiReadiness();
        bool productionReady = false;
        try
        {
            while (timer.Elapsed < TimeSpan.FromSeconds(90))
            {
                HeroesWindowProbe.GetProcessTimes(parentHandle, out _, out long exited, out _, out _);
                var all = HeroesWindowProbe.Processes();
                foreach (var p in all.Where(p => HeroesWindowProbe.IsGame(p, switcherPath) && !before.Contains(p.Pid)))
                {
                    if (!Owns(p, parent, exited, start, before, switcherPath))
                    { if (!ambiguity) Log("AmbiguousProcessManualCleanup", p); ambiguity = true; continue; }
                    if (!owned.ContainsKey(p.Pid)) { owned[p.Pid] = p; Log("HeroesDetected", p); }
                }
                var live = owned.Values.Where(p => HeroesWindowProbe.Same(p, HeroesWindowProbe.ReadProcess(p.Pid))).ToArray();
                var windows = HeroesWindowProbe.Windows(live);
                foreach (var w in windows)
                {
                    if (!prior.TryGetValue(w.Hwnd, out var previous) || previous != w) Log("WindowObserved", w);
                    if (previous is { Visible: true, ClassName: "#32770" } && !w.Visible)
                        Log("HideConfirmed", new { w.Hwnd, w.Process.Pid, processAlive = true });
                }
                prior = windows.ToDictionary(w => w.Hwnd);
                bool Hide(HeroesWindowIdentity w)
                {
                    attempts[w.Hwnd] = attempts.GetValueOrDefault(w.Hwnd) + 1;
                    bool accepted = HeroesWindowProbe.Hide(w);
                    Log("HideRequested", new { window = w, accepted, replacement = attempts.Count > 1, attempt = attempts[w.Hwnd] });
                    return accepted;
                }
                bool readyNow = productionReadiness.Sample(windows, timer.ElapsedMilliseconds, _ => true, Hide);
                if (!productionReady && readyNow)
                { productionReady = true; Log("ProductionReadinessConfirmed", new { stableMilliseconds = HeroesUiReadiness.StableMilliseconds }); }
                // Discovery heuristic only; production rule is chosen after reports are reviewed.
                var candidate = windows.FirstOrDefault(w => w.Visible && w.Ownerless && w.Enabled && !w.Hung && w.ClassName != "#32770" && w.Width >= 640 && w.Height >= 360);
                if (candidate != null && !windows.Any(HeroesWindowProbe.IsPreparation))
                {
                    if (candidateHwnd != candidate.Hwnd) { candidateHwnd = candidate.Hwnd; candidateSince = timer.ElapsedMilliseconds; Log("MainCandidate", candidate); }
                    if (!ready && timer.ElapsedMilliseconds - candidateSince >= 1500)
                    { ready = true; readyAt = timer.ElapsedMilliseconds; Log("CandidateStable", candidate); }
                }
                else { candidateHwnd = 0; candidateSince = 0; }
                if (ready && timer.ElapsedMilliseconds - readyAt >= 10000) break;
                if (owned.Count > 0 && live.Length == 0) { failed = true; Log("AllTestGamesExited", new { beforeReadiness = !ready }); break; }
                await Task.Delay(100);
            }
        }
        catch (Exception ex) { failed = true; Log("TestFailure", new { errorType = ex.GetType().Name }); }
        finally
        {
            foreach (var p in owned.Values) cleanupComplete &= await CleanupAsync(p, Log);
            cleanupComplete &= await CleanupAsync(parent, Log);
            Log("TestSummary", new { readyCandidateObserved = ready, productionReady, failed, hideTargets = attempts.Count, gameCount = owned.Count, manualCleanupRequired = ambiguity || !cleanupComplete, durationMs = timer.ElapsedMilliseconds });
        }
        return ready && productionReady && !ambiguity && !failed && cleanupComplete ? 0 : 4;
    }
    private static async Task<bool> CleanupAsync(HeroesProcessIdentity identity, Action<string, object> log)
    {
        IntPtr handle = OpenProcess(0x1000 | 0x100000 | 1, false, identity.Pid);
        if (handle == IntPtr.Zero) { log("Cleanup", new { identity.Pid, result = "ExitedOrInaccessibleManualCheck" }); return false; }
        try
        {
            var current = HeroesWindowProbe.ReadProcessHandle(handle, identity.Pid);
            if (WaitForSingleObject(handle, 0) == 0) { log("Cleanup", new { identity.Pid, result = "AlreadyExited" }); return true; }
            if (!HeroesWindowProbe.Same(identity, current)) { log("Cleanup", new { identity.Pid, result = "IdentityChangedOrInaccessibleManualCleanup" }); return false; }
            foreach (var w in HeroesWindowProbe.Windows([identity]).Where(w => w.Visible && w.Ownerless && w.ClassName != "#32770"))
                if (HeroesWindowProbe.Same(identity, HeroesWindowProbe.ReadProcessHandle(handle, identity.Pid)))
                    PostMessage(new IntPtr(w.Hwnd), 0x10, IntPtr.Zero, IntPtr.Zero);
            for (int i = 0; i < 20 && WaitForSingleObject(handle, 0) == 258; i++) await Task.Delay(100);
            if (WaitForSingleObject(handle, 0) == 0) { log("Cleanup", new { identity.Pid, result = "GracefulOrAlreadyExited" }); return true; }
            if (!HeroesWindowProbe.Same(identity, HeroesWindowProbe.ReadProcessHandle(handle, identity.Pid))) { log("Cleanup", new { identity.Pid, result = "IdentityChangedManualCleanup" }); return false; }
            bool terminated = TerminateProcess(handle, 0);
            bool exited = terminated && WaitForSingleObject(handle, 2000) == 0;
            log("Cleanup", new { identity.Pid, result = exited ? "TerminatedExactTestProcess" : "TerminationFailedManualCleanup" });
            return exited;
        }
        finally { CloseHandle(handle); }
    }
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll")] private static extern bool TerminateProcess(IntPtr handle, uint exitCode);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
}
