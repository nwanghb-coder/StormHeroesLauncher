using System.Diagnostics;
using System.IO;
using System.Text;
using StormHeroesLauncher.Services;

namespace StormHeroesLauncher.Observer;

public sealed class ObserverClock : IObserverClock
{
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public TimeSpan Elapsed => stopwatch.Elapsed;
    public Task Delay(TimeSpan delay, CancellationToken token) => Task.Delay(delay, token);
}
public sealed class ObserverBudget
{
    public static readonly TimeSpan Maximum = TimeSpan.FromMinutes(15), AfterGame = TimeSpan.FromMinutes(2);
    private long gameAtTicks = -1;
    public void GameDetected(TimeSpan elapsed) => Interlocked.CompareExchange(ref gameAtTicks, elapsed.Ticks, -1);
    public TimeSpan Deadline
    {
        get
        {
            long game = Interlocked.Read(ref gameAtTicks);
            return game < 0 ? Maximum : TimeSpan.FromTicks(Math.Min(Maximum.Ticks, game + AfterGame.Ticks));
        }
    }
    public bool Expired(TimeSpan elapsed) => elapsed >= Deadline;
}
public sealed class ObserverLogLimitException : IOException;
public sealed class JsonlObserverSink : IObserverSink
{
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StormHeroesLauncher", "Observer");
    private readonly StreamWriter writer;
    private long bytes;
    private int count;
    public JsonlObserverSink()
    {
        SafeCliPaths.NoReparse(DirectoryPath);
        Directory.CreateDirectory(DirectoryPath);
        SafeCliPaths.NoReparse(DirectoryPath);
        string path = Path.Combine(DirectoryPath, $"observer-{DateTime.Now:yyyy-MM-dd-HHmmss}-{Guid.NewGuid():N}.jsonl");
        writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
    }
    public void Write(ObserverEvent value)
    {
        string line = ObserverJson.Serialize(value);
        // Reserve space for a summary/failure. No retention deletion of existing evidence.
        if (value.Event is not ("SessionSummary" or "ObserverFailure" or "UpdateSession") &&
            (count >= 10000 || bytes + Encoding.UTF8.GetByteCount(line) > 15 * 1024 * 1024)) throw new ObserverLogLimitException();
        writer.WriteLine(line); writer.Flush();
        bytes += Encoding.UTF8.GetByteCount(line) + 2; count++;
    }
    public void Dispose() => writer.Dispose();
}
public sealed class ObserverRuntime(IObserverSource source, ObserverEngine engine, IObserverClock clock, ObserverBudget budget)
{
    public async Task RunAsync(CancellationToken token)
    {
        string reason = "MaximumLifetime";
        IReadOnlyList<RelatedProcess> last = [];
        TimeSpan lastFiles = TimeSpan.MinValue;
        bool pendingFiles = false;
        try
        {
            engine.Start();
            while (!budget.Expired(clock.Elapsed))
            {
                token.ThrowIfCancellationRequested();
                last = source.Processes();
                pendingFiles |= engine.ProcessSnapshot(last);
                engine.WindowSnapshot(source.Windows(last));
                bool first = lastFiles == TimeSpan.MinValue;
                if (first || clock.Elapsed - lastFiles >= TimeSpan.FromSeconds(10) ||
                    pendingFiles && clock.Elapsed - lastFiles >= TimeSpan.FromSeconds(2))
                {
                    engine.FileSnapshot(source.Files(last), first ? "Startup" : pendingFiles ? "ProcessTransition" : "Periodic");
                    lastFiles = clock.Elapsed; pendingFiles = false;
                }
                engine.AdvanceCorrelations();
                await clock.Delay(TimeSpan.FromMilliseconds(500), token).ConfigureAwait(false);
            }
            reason = budget.Deadline < ObserverBudget.Maximum ? "PostGameComplete" : "MaximumLifetime";
        }
        catch (OperationCanceledException) { reason = clock.Elapsed >= ObserverBudget.Maximum ? "MaximumLifetime" : "LaunchEndedOrCancelled"; }
        catch (Exception ex)
        {
            reason = ex is ObserverLogLimitException ? "LogLimit" : "ObserverFailure";
            try { engine.Emit(Family.Observer, "ObserverFailure", new { errorType = ex.GetType().Name }); } catch { }
            throw;
        }
        finally
        {
            // A failed checkpoint must never prevent the summary attempt.
            try { if (reason is not ("LogLimit" or "ObserverFailure")) engine.FileSnapshot(source.Files(last), "Final"); } catch { }
            try { engine.Summary(reason); } catch { /* Disk loss can make a final record impossible. */ }
        }
    }
}

public sealed class ObserverHost
{
    private readonly CancellationTokenSource stop = new();
    private readonly ObserverClock clock = new();
    private readonly ObserverBudget budget = new();
    private readonly Task worker;
    private readonly Action<string> report;
    private int stopped;
    private string sessionId = Guid.NewGuid().ToString("N");
    private ObserverHost(Func<IObserverClock, ObserverBudget, CancellationToken, Task> run, Action<string> report)
    {
        this.report = report;
        stop.CancelAfter(ObserverBudget.Maximum);
        worker = Task.Run(async () =>
        {
            try { await run(clock, budget, stop.Token).ConfigureAwait(false); }
            catch (Exception ex) { Report("ObserverFailure=" + ex.GetType().Name); }
        });
    }
    private void Report(string text) { try { report(text); } catch { } }
    public static ObserverHost? Start(string uu, string battleNet, Action<string> report)
    {
        try
        {
            string runId = Guid.NewGuid().ToString("N");
            var host = new ObserverHost(async (clock, budget, token) =>
            {
                var scope = new InstallationScope(uu, battleNet, new ObserverFiles());
                using var sink = new JsonlObserverSink();
                var engine = new ObserverEngine(sink, clock, runId);
                await new ObserverRuntime(new WindowsObserverSource(scope), engine, clock, budget).RunAsync(token);
            }, report);
            host.sessionId = runId;
            return host;
        }
        catch (Exception ex) { try { report("ObserverFailure=" + ex.GetType().Name); } catch { } return null; }
    }
    // Fake-only seam: tests exercise the same isolation/lifetime boundary without native observation.
    public static ObserverHost ForTest(Func<IObserverClock, ObserverBudget, CancellationToken, Task> run, Action<string> report) => new(run, report);
    public async Task DetachTailAsync(string uu, string battleNet, Action? launchForTest = null)
    {
        // Flush/end the main observer session before handing off. Never await the tail's lifetime.
        await StopAsync().ConfigureAwait(false);
        int seconds = (int)Math.Clamp((ObserverBudget.Maximum - clock.Elapsed).TotalSeconds, 0, 120);
        if (seconds == 0) return;
        try
        {
            if (launchForTest != null) launchForTest();
            else
            {
                using var child = Process.Start(ObserverTail.CreateStartInfo(Environment.ProcessPath!, uu, battleNet, sessionId, seconds));
                if (child == null) throw new InvalidOperationException();
            }
            Report("ObserverBuild=True; detached observer tail; launcher lifetime independent.");
        }
        catch (Exception ex) { Report("ObserverTailFailure=" + ex.GetType().Name); }
    }
    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref stopped, 1) != 0) return;
        try
        {
            stop.Cancel();
            if (await Task.WhenAny(worker, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false) != worker)
                Report("ObserverFailure=StopTimeout; background sampler abandoned at application exit.");
        }
        catch (Exception ex) { Report("ObserverFailure=" + ex.GetType().Name); }
    }
}

public static class ObserverTail
{
    public static ProcessStartInfo CreateStartInfo(string executable, string uu, string battleNet, string parent, int seconds)
    {
        if (!Path.IsPathFullyQualified(executable) || !Path.GetFileName(executable).Equals("HOSLauncher.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unexpected worker executable");
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AppContext.BaseDirectory };
        foreach (string argument in new[] { "--observer-tail", uu, battleNet, parent, seconds.ToString(System.Globalization.CultureInfo.InvariantCulture) }) info.ArgumentList.Add(argument);
        return info;
    }
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 5 || !Guid.TryParseExact(args[3], "N", out _) || !int.TryParse(args[4], out int seconds) || seconds is < 1 or > 120) return 2;
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        // No launcher mutex, settings writes, windows, external launches or elevated helper in this entry point.
        var worker = Task.Run(async () =>
        {
            var clock = new ObserverClock(); var budget = new ObserverBudget(); budget.GameDetected(TimeSpan.Zero);
            var scope = new InstallationScope(args[1], args[2], new ObserverFiles());
            using var sink = new JsonlObserverSink();
            var engine = new ObserverEngine(sink, clock);
            engine.Emit(Family.Observer, "ObserverTailStarted", new { parentSessionId = args[3], maximumSeconds = seconds });
            await new ObserverRuntime(new WindowsObserverSource(scope), engine, clock, budget).RunAsync(cancel.Token);
        });
        await Task.WhenAny(worker, Task.Delay(TimeSpan.FromSeconds(seconds)));
        cancel.Cancel();
        if (await Task.WhenAny(worker, Task.Delay(2000)) != worker) return 3;
        try { await worker; return 0; } catch { return 3; }
    }
}
