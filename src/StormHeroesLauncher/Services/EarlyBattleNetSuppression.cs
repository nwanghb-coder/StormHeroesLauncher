using System.Globalization;

namespace StormHeroesLauncher.Services;

// One bounded observer per cold start. Readiness never waits for this task to finish.
public sealed class EarlyBattleNetSuppression(AppLogger logger, Func<IReadOnlyList<ExternalWindow>> snapshot,
    Func<HashSet<uint>> owners, Func<ExternalWindow, bool, BattleNetVisualResult> action, Func<double> milliseconds)
{
    private sealed record Entry(ExternalWindow Window, double Detected, BattleNetVisualResult Action)
    {
        public double? Hidden { get; set; }
        public double? Absent { get; set; }
        public bool AliveAtConfirmation { get; set; }
    }
    private readonly Dictionary<(IntPtr, uint, string), Entry> handled = new();
    private readonly object gate = new();
    private double armedAt;
    private double launchAt = double.NaN;
    private DateTimeOffset epoch;
    private volatile bool armed, finished;
    public bool Active => armed && !finished && milliseconds() - armedAt < 5000;
    public int PollDelay => milliseconds() - armedAt < 500 ? 5 : milliseconds() - armedAt < 2000 ? 10 : 25;

    public void Arm()
    {
        armedAt = milliseconds(); epoch = DateTimeOffset.UtcNow; armed = true;
        logger.Write("Battle.net suppression: Armed=True BeforeLaunch=True BudgetMs=5000 PollMs=5/10/25 TimingBasis=RequestAndSampling_NotPhysicalScreenExposure");
    }
    public void MarkLaunch() => Volatile.Write(ref launchAt, milliseconds());

    // The first sample executes before launch; continuations do not need Process.Start to return.
    public Task StartBeforeLaunch(Action launch, CancellationToken token, Func<int, CancellationToken, Task>? delay = null)
    {
        token.ThrowIfCancellationRequested();
        Arm();
        var observation = ObserveAsync(token, delay);
        token.ThrowIfCancellationRequested();
        MarkLaunch();
        launch();
        return observation;
    }
    public async Task ObserveAsync(CancellationToken token, Func<int, CancellationToken, Task>? delay = null)
    {
        delay ??= Task.Delay;
        try
        {
            while (Active)
            {
                token.ThrowIfCancellationRequested();
                Poll();
                if (Active) await delay(Math.Min(PollDelay, Math.Max(1, (int)(5000 - (milliseconds() - armedAt)))), token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { Finish(); }
    }
    public void Poll()
    {
        lock (gate) PollCore();
    }
    private void PollCore()
    {
        if (!Active) { Finish(); return; }
        try
        {
            var windows = snapshot(); var pids = owners();
            // Actions precede confirmation work and diagnostic disk I/O.
            foreach (var w in windows)
            {
                bool transient = w.ClassName == "Qt5151QWindowIcon";
                if (!BattleNetTray.Candidate(w, pids, transient)) continue;
                var key = (w.Handle, w.Pid, w.ClassName);
                if (handled.ContainsKey(key) || handled.Count >= 32 || !Active) continue;
                double detected = milliseconds();
                handled.Add(key, new(w, detected, action(w, transient)));
            }
            foreach (var entry in handled.Values)
            {
                if (entry.Hidden.HasValue || entry.Absent.HasValue) continue;
                var current = windows.FirstOrDefault(w => w.Handle == entry.Window.Handle &&
                    w.Pid == entry.Window.Pid && w.ClassName == entry.Window.ClassName);
                // Destruction/absence is not a sampled IsWindowVisible=false result.
                if (current == null) entry.Absent = milliseconds();
                else if (!current.Visible) entry.Hidden = milliseconds();
                entry.AliveAtConfirmation = pids.Contains(entry.Window.Pid);
            }
        }
        catch (Exception ex)
        {
            Finish();
            logger.Write($"Battle.net suppression: Result=Unavailable Exception={ex.GetType().Name}; readiness continues");
        }
    }
    public void Finish()
    {
        lock (gate) FinishCore();
    }
    // Normal checkpoints reuse the launch clock after fast polling ends. They do not restart it.
    public BattleNetVisualResult ActAtCheckpoint(ExternalWindow window, bool transient)
    {
        lock (gate)
        {
            var key = (window.Handle, window.Pid, window.ClassName);
            if (handled.TryGetValue(key, out var prior)) return prior.Action with
                { HideAccepted = false, ClosePosted = false, Result = "AlreadyHandled" };
            if (handled.Count >= 32) return new(null, null, false, false, false, "Capacity");
            double detected = milliseconds();
            var entry = new Entry(window, detected, action(window, transient));
            handled.Add(key, entry);
            if (finished) Log(entry);
            return entry.Action;
        }
    }
    public void ConfirmAtCheckpoint(ExternalWindow window, ExternalWindow? sample, bool alive)
    {
        lock (gate)
        {
            if (!handled.TryGetValue((window.Handle, window.Pid, window.ClassName), out var entry) ||
                entry.Hidden.HasValue || entry.Absent.HasValue) return;
            if (sample == null) entry.Absent = milliseconds();
            else if (!sample.Visible) entry.Hidden = milliseconds();
            else return;
            entry.AliveAtConfirmation = alive;
            if (finished) Log(entry);
        }
    }
    private void FinishCore()
    {
        if (finished) return;
        finished = true;
        foreach (var e in handled.Values) Log(e);
        logger.Write("Battle.net suppression: FastObservationEnded=True; bounded normal checkpoints retained");
    }
    private void Log(Entry e)
    {
        var a = e.Action; double launch = Volatile.Read(ref launchAt);
        string Stamp(double? t) => t.HasValue && !double.IsNaN(t.Value) ? epoch.AddMilliseconds(t.Value - armedAt).ToString("O") : "Unknown";
        string Ms(double? t) => t.HasValue && !double.IsNaN(t.Value) ? t.Value.ToString("F3", CultureInfo.InvariantCulture) : "Unknown";
        logger.Write($"Battle.net suppression: PID={e.Window.Pid} HWND=0x{e.Window.Handle.ToInt64():X} Class={e.Window.ClassName} " +
            $"ProcessLaunchT0={Stamp(launch)} ObserverArmedT={Stamp(armedAt)} WindowFirstDetectedT={Stamp(e.Detected)} " +
            $"HideRequestedT={Stamp(a.HideRequested)} WMCloseRequestedT={Stamp(a.CloseRequested)} FirstConfirmedHiddenT={Stamp(e.Hidden)} WindowAbsentT={Stamp(e.Absent)} " +
            $"ObserverLeadMs={Ms(launch - armedAt)} DetectionLatencyFromLaunchMs={Ms(e.Detected - launch)} FirstDetectedToHideMs={Ms(a.HideRequested - e.Detected)} " +
            $"HideToWMCloseMs={Ms(a.CloseRequested - a.HideRequested)} FirstDetectedToHiddenConfirmMs={Ms(e.Hidden - e.Detected)} " +
            $"HideAccepted={a.HideAccepted} WMClosePosted={a.ClosePosted} ProcessRunningAfter={a.ProcessAlive} ProcessRunningAtConfirmation={e.AliveAtConfirmation} Result={a.Result} " +
            "TimingBasis=RequestAndSampling_NotPhysicalScreenExposure");
    }
}
