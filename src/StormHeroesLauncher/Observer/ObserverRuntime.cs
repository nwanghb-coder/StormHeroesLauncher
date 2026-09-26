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
            return new(async (clock, budget, token) =>
            {
                var scope = new InstallationScope(uu, battleNet, new ObserverFiles());
                using var sink = new JsonlObserverSink();
                var engine = new ObserverEngine(sink, clock);
                await new ObserverRuntime(new WindowsObserverSource(scope), engine, clock, budget).RunAsync(token);
            }, report);
        }
        catch (Exception ex) { try { report("ObserverFailure=" + ex.GetType().Name); } catch { } return null; }
    }
    // Fake-only seam: tests exercise the same isolation/lifetime boundary without native observation.
    public static ObserverHost ForTest(Func<IObserverClock, ObserverBudget, CancellationToken, Task> run, Action<string> report) => new(run, report);
    public async Task AfterGameAsync()
    {
        budget.GameDetected(clock.Elapsed);
        Report("ObserverBuild=True; game detected; bounded post-launch observation active.");
        TimeSpan remaining = budget.Deadline - clock.Elapsed;
        if (remaining > TimeSpan.Zero) await Task.WhenAny(worker, Task.Delay(remaining)).ConfigureAwait(false);
        await StopAsync().ConfigureAwait(false);
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
