namespace StormHeroesLauncher.Observer;

public sealed record StartupBounds(int Left, int Top, int Width, int Height);
public sealed record UuStartupWindow(RelatedProcess Process, WindowMetadata Window, StartupBounds? Bounds);
public sealed record UuStartupWindowEvent(string CandidateKind, UuStartupWindow Candidate,
    DateTimeOffset FirstSeenTimestamp, DateTimeOffset? DisappearanceTimestamp, double LifetimeMs, string EndReason);

// Read-only source/delegates: this observer has no window-action or process-control API.
public sealed class UuStartupSampler(IObserverClock clock, IObserverSink sink,
    Func<IReadOnlyList<RelatedProcess>> processes,
    Func<IReadOnlyList<RelatedProcess>, IReadOnlyList<UuStartupWindow>> windows, long armedFileTime)
{
    public const int PollMilliseconds = 20, ActiveMilliseconds = 5000, ArmMilliseconds = 35000;
    private readonly string session = Guid.NewGuid().ToString("N");
    private readonly Dictionary<string, (UuStartupWindow Window, DateTimeOffset First, double At)> live = new();
    private int count;
    private void Emit(string name, object data) => sink.Write(new(ObserverJson.SchemaVersion, clock.UtcNow,
        clock.Elapsed.TotalMilliseconds, Family.UU, name, session, data));
    public async Task RunAsync(CancellationToken token)
    {
        TimeSpan armed = clock.Elapsed; TimeSpan? started = null;
        string reason = "NoNewUuProcess";
        try
        {
            Emit("UuStartupObservationStarted", new { pollMilliseconds = PollMilliseconds, activeMilliseconds = ActiveMilliseconds, armMilliseconds = ArmMilliseconds });
            while (clock.Elapsed - armed < TimeSpan.FromMilliseconds(ArmMilliseconds + ActiveMilliseconds))
            {
                token.ThrowIfCancellationRequested();
                var selected = processes().Where(p => p.RelatedTo == Family.UU && p.Identity.CreatedFileTime >= armedFileTime).ToArray();
                if (started == null)
                {
                    if (selected.Any(p => p.Identity.Name.Equals("uu_launcher.exe", StringComparison.OrdinalIgnoreCase) || p.Identity.Name.Equals("uu.exe", StringComparison.OrdinalIgnoreCase)))
                    { started = clock.Elapsed; reason = "ObservationEnded"; }
                    else if (clock.Elapsed - armed >= TimeSpan.FromMilliseconds(ArmMilliseconds)) break;
                }
                if (started != null)
                {
                    if (clock.Elapsed - started.Value >= TimeSpan.FromMilliseconds(ActiveMilliseconds)) break;
                    var keys = selected.Select(p => p.Identity.Key).ToHashSet();
                    var current = windows(selected).Where(w => w.Process.RelatedTo == Family.UU &&
                        keys.Contains(w.Process.Identity.Key) && w.Window.Visible).ToDictionary(w => $"{w.Window.ProcessKey}:{w.Window.Hwnd}:{w.Window.ClassName}");
                    foreach (var key in live.Keys.Where(k => !current.ContainsKey(k)).ToArray()) End(key, "NoLongerVisibleOrPresent", true);
                    foreach (var (key, candidate) in current)
                    {
                        if (live.ContainsKey(key)) continue;
                        if (++count > 256) { reason = "CandidateLimit"; return; }
                        live[key] = (candidate, clock.UtcNow, clock.Elapsed.TotalMilliseconds);
                        Emit("UuStartupWindowAppeared", new UuStartupWindowEvent("UnknownStartupWindow", candidate, clock.UtcNow, null, 0, ""));
                    }
                }
                await clock.Delay(TimeSpan.FromMilliseconds(started == null ? 100 : PollMilliseconds), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { reason = "Cancelled"; }
        catch (Exception ex) { reason = "ObserverFailure"; try { Emit("ObserverFailure", new { errorType = ex.GetType().Name }); } catch { } }
        finally
        {
            // A window still visible at the bound is censored, not falsely reported as disappeared.
            foreach (var key in live.Keys.ToArray()) { try { End(key, reason, false); } catch { } }
            try { Emit("SessionSummary", new { uuStartupWindowCandidates = Math.Min(count, 256), reason }); } catch { }
        }
    }
    private void End(string key, string reason, bool disappeared)
    {
        var value = live[key]; live.Remove(key);
        Emit(disappeared ? "UuStartupWindowDisappeared" : "UuStartupWindowObservationEnded",
            new UuStartupWindowEvent("UnknownStartupWindow", value.Window, value.First,
                disappeared ? clock.UtcNow : null, clock.Elapsed.TotalMilliseconds - value.At, reason));
    }
}

public sealed class UuStartupObserver
{
    private readonly CancellationTokenSource stop;
    private readonly Task worker;
    private UuStartupObserver(string uu, string battleNet, CancellationToken token, Action<string> report)
    {
        stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        stop.CancelAfter(TimeSpan.FromMilliseconds(UuStartupSampler.ArmMilliseconds + UuStartupSampler.ActiveMilliseconds));
        long armed = DateTime.UtcNow.ToFileTimeUtc();
        worker = Task.Run(async () =>
        {
            try
            {
                using var sink = new JsonlObserverSink();
                var source = new WindowsObserverSource(new InstallationScope(uu, battleNet, new ObserverFiles()));
                await new UuStartupSampler(new ObserverClock(), sink, source.Processes, source.StartupWindows, armed).RunAsync(stop.Token);
            }
            catch (Exception ex) { try { report("UuStartupObserverFailure=" + ex.GetType().Name); } catch { } }
        });
    }
    public static UuStartupObserver? Start(string uu, string battleNet, CancellationToken token, Action<string> report)
    { try { return new(uu, battleNet, token, report); } catch { return null; } }
    public async Task StopAsync()
    {
        stop.Cancel();
        await Task.WhenAny(worker, Task.Delay(200)); // Never delay cancellation/mutex release for observation.
        if (worker.IsCompleted) stop.Dispose();
    }
}
