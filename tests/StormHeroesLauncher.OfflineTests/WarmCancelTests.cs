using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using StormHeroesLauncher;
using StormHeroesLauncher.Configuration;
using StormHeroesLauncher.Models;
using StormHeroesLauncher.Services;

public static class WarmCancelTests
{
    public static async Task Run(AppLogger logger, Action<bool, string> check)
    {
        var options = new UuCliOptions { PollIntervalMilliseconds = 100 };
        HeroesBoostStatus active = new(true, "boosting", null, null, null, null, null, null)
            { GameId = options.GameId, ZoneId = options.ZoneId, ServerId = options.ServerId };
        var cli = new WarmCli { Status = active };
        var progress = new LaunchProgress(); var emitted = new List<LaunchState>(); progress.Changed += s => emitted.Add(s.State);
        int ensure = 0, battle = 0, game = 0;
        var boost = new HeroesBoostWorkflow(_ => { ensure++; return Task.FromResult("UU 加速器：已运行"); }, cli, options, logger, progress, _ => Task.FromResult(true));
        var workflow = new HeroesLaunchWorkflow(_ => Task.FromResult(false), t => boost.StartAsync(_ => { }, t),
            _ => { battle++; return Task.CompletedTask; }, _ => { game++; progress.Report(LaunchState.StartingHeroes); progress.Report(LaunchState.PreparingHeroes); return Task.CompletedTask; },
            () => { }, logger, progress, reuseBattleNet: t => BattleNetService.ConfirmExistingAsync(() => new IntPtr(99), _ => Task.CompletedTask, t));
        await workflow.RunAsync(default);
        check(cli.Starts == 0 && ensure == 0 && battle == 0 && game == 1 && cli.Statuses == 1, "fully warm target boost and Battle.net proxy skip all redundant start actions");
        check(emitted.SequenceEqual(new[] { LaunchState.StartingHeroes, LaunchState.PreparingHeroes, LaunchState.GameReady }), "warm fast path emits only actual Heroes stages, jumping from 5 to 80 percent");
        check(!active.Matches(options with { ServerId = "other" }) && !active.Matches(options with { ZoneId = "other" }) &&
            !(active with { GameId = null }).Matches(options) && !(active with { ZoneId = null }).Matches(options), "missing/wrong target identifiers cannot authorize boost reuse");
        var data = new BoostStatusData { IsBoosting = true, Boosters = [new() { GameId = options.GameId, ZoneId = options.ZoneId, ServerId = options.ServerId, Status = "boosting" }] };
        check(UuCliService.NormalizeStatus(data, options.GameId).Matches(options), "status normalization preserves explicit game/zone/server from selected booster");
        check(!UuCliService.NormalizeStatus(new() { IsBoosting = true, GameId = options.GameId, Status = "boosting" }, options.GameId).Matches(options), "legacy target-only status remains uncertain for zone/server reuse");
        foreach (bool uncertain in new[] { false, true })
        {
            var fallback = new WarmCli { Status = active with { IsBoosting = false, Status = "not_boosting" }, AfterStart = active, FailFirst = uncertain };
            int ensured = 0;
            await new HeroesBoostWorkflow(_ => { ensured++; return Task.FromResult("UU 加速器：已运行"); }, fallback, options, logger,
                isUuRunning: _ => Task.FromResult(true)).StartAsync(_ => { }, default);
            check(ensured == 1 && fallback.Starts == 1 && fallback.Stops == 0, uncertain ? "uncertain status falls back safely to one normal start" : "non-boosting status still calls one CLI start");
        }
        int index = 0;
        check(!await BattleNetService.ConfirmExistingAsync(() => IntPtr.Zero, _ => Task.CompletedTask, default) &&
            !await BattleNetService.ConfirmExistingAsync(() => new IntPtr(++index), _ => Task.CompletedTask, default), "missing/changing Battle.net window rejects reuse");
        battle = 0; game = 0;
        await new HeroesLaunchWorkflow(_ => Task.FromResult(false), _ => Task.FromResult(active),
            _ => { battle++; return Task.CompletedTask; }, _ => { game++; return Task.CompletedTask; }, () => { }, logger,
            reuseBattleNet: _ => Task.FromResult(false)).RunAsync(default);
        check(battle == 1 && game == 1, "non-ready Battle.net falls back to proven ensure flow");
        bool touched = false;
        await new HeroesLaunchWorkflow(_ => Task.FromResult(true), _ => { touched = true; throw new Exception(); }, _ => { touched = true; throw new Exception(); },
            _ => { touched = true; throw new Exception(); }, () => touched = true, logger,
            waitForExistingGame: _ => Task.CompletedTask, reuseBattleNet: _ => { touched = true; throw new Exception(); }).RunAsync(default);
        check(!touched, "already-running Heroes bypasses every UU/Battle.net action and fast-path probe");

        // Cancellation at each issued stage must leave all earlier work in place and block later delegates.
        foreach (int cancelAt in new[] { 0, 1, 2 })
        {
            using var stop = new CancellationTokenSource();
            var state = new LaunchProgress(); var calls = new List<string>();
            async Task Step(string name, int phase, CancellationToken token)
            {
                calls.Add(name);
                if (phase == cancelAt) { stop.Cancel(); await Task.Delay(Timeout.Infinite, token); }
            }
            var run = new HeroesLaunchWorkflow(_ => Task.FromResult(false), async t => { await Step("UU/boost", 0, t); return active; },
                t => Step("Battle.net", 1, t), t => Step("Heroes requested", 2, t), () => { }, logger, state);
            bool cancelled = false;
            try { await run.RunAsync(stop.Token).WaitAsync(TimeSpan.FromSeconds(1)); }
            catch (OperationCanceledException) { cancelled = true; }
            state.Report(LaunchState.GameReady); state.Report(LaunchState.Failed);
            check(cancelled && calls.Count == cancelAt + 1 && state.Current.State == LaunchState.Cancelled,
                $"Esc phase {cancelAt}: prompt cancellation, no later steps, terminal Cancelled rather than Failed");
        }
        using (var stop = new CancellationTokenSource())
        {
            var state = new LaunchProgress(); var cancelCli = new WarmCli { Status = active with { IsBoosting = false, Status = "not_boosting" } };
            state.Changed += s => { if (s.State == LaunchState.Boosting) stop.Cancel(); };
            try { await new HeroesBoostWorkflow(_ => Task.FromResult("UU 加速器：已运行"), cancelCli, options, logger, state).StartAsync(_ => { }, stop.Token); }
            catch (OperationCanceledException) { }
            check(cancelCli.Starts == 0 && cancelCli.Stops == 0, "Esc at boost boundary sends neither a start nor stop command");
        }
        bool escaped = false; Exception? uiError = null;
        var ui = new Thread(() =>
        {
            try
            {
                using var stop = new CancellationTokenSource(); var state = new LaunchProgress(); state.Report(LaunchState.PreparingHeroes);
                var window = new LaunchProgressWindow(state, stop.Cancel);
                var key = new KeyEventArgs(Keyboard.PrimaryDevice, new TestPresentationSource(), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                window.RaiseEvent(key);
                escaped = key.Handled && stop.IsCancellationRequested && state.Current == new LaunchProgressSnapshot(LaunchState.Cancelled, 90) && LaunchProgressText.For(state.Current.State) == "已取消";
                window.CloseForShutdown();
            }
            catch (Exception ex) { uiError = ex; }
        });
        ui.SetApartmentState(ApartmentState.STA); ui.Start(); ui.Join();
        check(escaped && uiError == null, "WPF Esc route requests cooperative cancellation and preserves percentage with 已取消");

        bool released = false; Exception? mutexError = null;
        string mutexName = @"Local\StormHeroesLauncher.CancelTest." + Guid.NewGuid().ToString("N");
        using var acquired = new ManualResetEventSlim(); using var cancelRequest = new ManualResetEventSlim();
        var owner = new Thread(() =>
        {
            using var mutex = new Mutex(false, mutexName); bool held = false;
            try
            {
                held = mutex.WaitOne(0); acquired.Set(); cancelRequest.Wait();
                using var stop = new CancellationTokenSource(); stop.Cancel();
                var run = new HeroesLaunchWorkflow(_ => Task.FromResult(false), _ => throw new Exception("No action after Esc"),
                    _ => throw new Exception(), _ => throw new Exception(), () => { }, logger);
                try { run.RunAsync(stop.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
            }
            catch (Exception ex) { mutexError = ex; }
            finally { if (held) mutex.ReleaseMutex(); }
        });
        owner.Start(); acquired.Wait(); cancelRequest.Set();
        bool finished = owner.Join(1000);
        using (var probe = new Mutex(false, mutexName))
        { released = probe.WaitOne(0); if (released) probe.ReleaseMutex(); }
        check(finished && released && mutexError == null, "cancelled workflow unwinds promptly and permits mutex acquisition from another thread");

        // Harmless test executable proves external cancellation does not kill an issued CLI request.
        string marker = Path.Combine(AppContext.BaseDirectory, "cancel-survival-" + Guid.NewGuid().ToString("N"));
        using (var stop = new CancellationTokenSource())
        {
            Task request = new CliProcessRunner().RunAsync(Path.Combine(AppContext.BaseDirectory, "StormHeroesLauncher.OfflineTests.exe"),
                ["--fake-child", "cancel-survival", marker], TimeSpan.FromSeconds(10), stop.Token);
            var timer = Stopwatch.StartNew();
            while (!File.Exists(marker + ".started") && timer.ElapsedMilliseconds < 5000) await Task.Delay(20);
            stop.Cancel();
            try { await request; } catch (OperationCanceledException) { }
            while (!File.Exists(marker + ".finished") && timer.ElapsedMilliseconds < 5000) await Task.Delay(20);
            check(File.Exists(marker + ".finished"), "Esc stops waiting without killing already-issued CLI process");
        }
    }
    private sealed class TestPresentationSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
    private sealed class WarmCli : IUuCliService
    {
        public required HeroesBoostStatus Status;
        public HeroesBoostStatus? AfterStart;
        public bool FailFirst;
        public int Starts, Stops, Statuses;
        public Task<BoostOperationData> StartHeroesBoostAsync(CancellationToken token = default) { token.ThrowIfCancellationRequested(); Starts++; if (AfterStart != null) Status = AfterStart; return Task.FromResult(new BoostOperationData()); }
        public Task<HeroesBoostStatus> GetHeroesBoostStatusAsync(CancellationToken token = default)
        { token.ThrowIfCancellationRequested(); Statuses++; if (FailFirst && Statuses == 1) throw new UuCliException(CliFailureKind.Timeout, "fake"); return Task.FromResult(Status); }
        public Task<BoostOperationData> StopHeroesBoostAsync(CancellationToken token = default) { Stops++; throw new Exception("No rollback permitted"); }
    }
}
