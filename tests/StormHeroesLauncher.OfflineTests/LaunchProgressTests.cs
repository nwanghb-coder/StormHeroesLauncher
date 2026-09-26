using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StormHeroesLauncher;
using StormHeroesLauncher.Configuration;
using StormHeroesLauncher.Models;
using StormHeroesLauncher.Services;

public static class LaunchProgressTests
{
    public static async Task Run(AppLogger logger, Action<bool, string> check)
    {
        var logs = new List<string>();
        var progress = new LaunchProgress(logs.Add);
        var events = new List<LaunchProgressSnapshot> { progress.Current };
        progress.Changed += events.Add;
        LaunchState[] ordered = [LaunchState.Initializing, LaunchState.StartingUU, LaunchState.PreparingUU,
            LaunchState.Boosting, LaunchState.StartingBattleNet, LaunchState.WaitingForBattleNet,
            LaunchState.StartingHeroes, LaunchState.PreparingHeroes, LaunchState.GameReady];
        foreach (var stage in ordered) progress.Report(stage);
        check(events.Select(e => e.Percentage).SequenceEqual(new[] { 5, 15, 25, 40, 55, 65, 80, 90, 100 }), "semantic state percentages match fixed stage mapping");
        check(events.Select(e => e.State).SequenceEqual(ordered) && logs.Count == 9, "ordered state notifications and exactly one log per transition");
        progress.Report(LaunchState.Failed); progress.Report(LaunchState.Boosting);
        check(progress.Current.State == LaunchState.GameReady && logs.Count == 9, "success is terminal; delayed notifications cannot regress progress");
        var skipped = new LaunchProgress();
        skipped.Report(LaunchState.WaitingForBattleNet); skipped.Report(LaunchState.StartingUU);
        check(skipped.Current == new LaunchProgressSnapshot(LaunchState.WaitingForBattleNet, 65), "skipped stages advance directly; earlier stages ignored");
        skipped.Report(LaunchState.Failed); skipped.Report(LaunchState.GameReady);
        check(skipped.Current == new LaunchProgressSnapshot(LaunchState.Failed, 65), "failure retains progress and cannot become success");
        var concurrent = new LaunchProgress(); var percentages = new List<int>();
        concurrent.Changed += s => percentages.Add(s.Percentage);
        await Task.WhenAll(ordered.Select(s => Task.Run(() => concurrent.Report(s))));
        check(percentages.SequenceEqual(percentages.Order()) && concurrent.Current.Percentage == 100, "concurrent notifications serialize without backward progress");
        var badSubscriber = new LaunchProgress(_ => throw new IOException());
        badSubscriber.Changed += _ => throw new InvalidOperationException();
        badSubscriber.Report(LaunchState.GameReady);
        check(badSubscriber.Current.Percentage == 100, "broken log/presentation subscriber cannot fail the launch state publisher");
        check(typeof(LaunchProgressSnapshot).GetProperties().Select(p => p.Name).Order().SequenceEqual(new[] { "Percentage", "State" }) &&
            typeof(LaunchProgress).GetEvent("Changed")!.EventHandlerType == typeof(Action<LaunchProgressSnapshot>), "public state model exposes only semantic state and progress, no PID/HWND/diagnostic data");
        string[] chinese = ["正在准备…", "正在启动网易 UU…", "正在准备加速器…", "正在加速《风暴英雄》…", "正在启动暴雪游戏平台…",
            "正在等待暴雪游戏平台…", "正在启动《风暴英雄》…", "正在准备进入游戏…", "启动完成", "启动失败"];
        check(Enum.GetValues<LaunchState>().Select(LaunchProgressText.For).SequenceEqual(chinese), "all ten Chinese labels are exact single-line presentation text");

        check(StartupRouting.ShowLaunchProgress([], false, true), "normal valid launch selects progress window");
        check(!StartupRouting.ShowLaunchProgress([], true, true), "Shift Settings route excludes progress");
        check(!StartupRouting.ShowLaunchProgress(["--settings"], false, true), "--settings route excludes progress");
        check(!StartupRouting.ShowLaunchProgress([@"C:\Fake\UU.lnk"], false, true), "shortcut import route excludes progress");
        check(!StartupRouting.ShowLaunchProgress([], false, false) && !StartupRouting.ShowLaunchProgress([], false, true, true), "configuration recovery excludes progress");

        // Existing helper/CLI/workflow with fake dependencies: no native launcher or UAC runs.
        var host = new FakeTrayHost(); var elevation = new FakeElevation(host);
        var state = new LaunchProgress(); var stages = new List<LaunchState> { state.Current.State };
        state.Changed += s => stages.Add(s.State);
        var uu = new UuElevationFlow(logger, host, elevation, @"C:\Fake\uu_launcher.exe", state);
        var cli = new ProgressFakeCli(); var options = new UuCliOptions();
        var boost = new HeroesBoostWorkflow(uu.EnsureAsync, cli, options, logger, state);
        var workflow = new HeroesLaunchWorkflow(_ => Task.FromResult(false), t => boost.StartAsync(_ => { }, t),
            _ => { state.Report(LaunchState.StartingBattleNet); state.Report(LaunchState.WaitingForBattleNet); return Task.CompletedTask; },
            _ => { state.Report(LaunchState.StartingHeroes); state.Report(LaunchState.PreparingHeroes); return Task.CompletedTask; },
            () => { }, logger, state);
        await workflow.RunAsync(default);
        check(stages.SequenceEqual(ordered) && elevation.Calls == 1 && cli.Starts == 1 && cli.Statuses == 2,
            "cold fake launch emits semantic stages without changing helper/CLI invocation counts");
        var warmState = new LaunchProgress();
        await new UuElevationFlow(logger, new FakeTrayHost { Alive = true }, new FakeElevation(new()), @"C:\Fake\uu_launcher.exe", warmState).EnsureAsync(default);
        check(warmState.Current.State == LaunchState.PreparingUU, "warm UU skips StartingUU using the existing running check");
        var ready = new LaunchProgress(); bool mutated = false;
        await new HeroesLaunchWorkflow(_ => Task.FromResult(true), _ => { mutated = true; return Task.FromResult(ProgressFakeCli.Ready); },
            _ => { mutated = true; return Task.CompletedTask; }, _ => { mutated = true; return Task.CompletedTask; }, () => mutated = true, logger, ready).RunAsync(default);
        check(!mutated && ready.Current == new LaunchProgressSnapshot(LaunchState.GameReady, 100), "already-running game advances directly to 100 without new launch operations");
        var failure = new LaunchProgress(); failure.Report(LaunchState.Boosting);
        var failing = new HeroesLaunchWorkflow(_ => Task.FromResult(false), _ => Task.FromException<HeroesBoostStatus>(new TimeoutException("fake")),
            _ => Task.CompletedTask, _ => Task.CompletedTask, () => { }, logger, failure);
        bool sameFailure = false;
        try { await failing.RunAsync(default); } catch (TimeoutException) { sameFailure = true; }
        check(sameFailure && failure.Current == new LaunchProgressSnapshot(LaunchState.Failed, 40), "workflow failure reports Failed while preserving original exception and progress");
        var cancelled = new LaunchProgress();
        using (var token = new CancellationTokenSource())
        {
            token.Cancel();
            try { await new HeroesLaunchWorkflow(_ => Task.FromResult(false), _ => Task.FromResult(ProgressFakeCli.Ready),
                _ => Task.CompletedTask, _ => Task.CompletedTask, () => { }, logger, cancelled).RunAsync(token.Token); }
            catch (OperationCanceledException) { }
        }
        check(cancelled.Current.State == LaunchState.Failed, "existing cancellation maps to Failed without adding cancellation behavior");
        var deniedState = new LaunchProgress(); var deniedHost = new FakeTrayHost(); var deniedCli = new ProgressFakeCli();
        var deniedUu = new UuElevationFlow(logger, deniedHost, new FakeElevation(deniedHost) { Code = 1223 }, @"C:\Fake\uu_launcher.exe", deniedState);
        var deniedBoost = new HeroesBoostWorkflow(deniedUu.EnsureAsync, deniedCli, options, logger, deniedState);
        var deniedWorkflow = new HeroesLaunchWorkflow(_ => Task.FromResult(false), t => deniedBoost.StartAsync(_ => { }, t),
            _ => throw new Exception("must not reach Battle.net"), _ => throw new Exception("must not reach Heroes"), () => { }, logger, deniedState);
        try { await deniedWorkflow.RunAsync(default); } catch (InvalidOperationException) { }
        check(deniedState.Current == new LaunchProgressSnapshot(LaunchState.Failed, 15) && deniedCli.Starts == 0,
            "UAC rejection preserves existing failure path, stops before CLI and retains 15 percent");

        Exception? error = null; bool updated = false, closed = false, failureClosed = false, closeIgnored = false, noButtons = false;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            async Task ExerciseAsync()
            {
                var model = new LaunchProgress(); var window = new LaunchProgressWindow(model);
                window.Closed += (_, _) => closed = true;
                window.Close(); closeIgnored = !closed;
                await Task.Run(() => model.Report(LaunchState.WaitingForBattleNet));
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                updated = window.Displayed.Percentage == 65 && window.Title == "StormHeroesLauncher" && window.Icon != null;
                IEnumerable<DependencyObject> Walk(DependencyObject node)
                { yield return node; foreach (var item in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) foreach (var child in Walk(item)) yield return child; }
                noButtons = !Walk(window).OfType<Button>().Any() && Walk(window).OfType<ProgressBar>().Single().IsIndeterminate == false;
                // Offscreen render only; no desktop window, native observation or external application is shown.
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(420, double.PositiveInfinity));
                content.Arrange(new Rect(0, 0, 420, content.DesiredSize.Height)); content.UpdateLayout();
                var image = new RenderTargetBitmap(420, (int)Math.Ceiling(content.ActualHeight + 44), 96, 96, PixelFormats.Pbgra32);
                var background = new DrawingVisual();
                using (var draw = background.RenderOpen()) draw.DrawRectangle(SystemColors.WindowBrush, null, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
                image.Render(background); image.Render(content);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using (var output = File.Create(Path.Combine(AppContext.BaseDirectory, "progress-preview.png"))) encoder.Save(output);
                model.Report(LaunchState.GameReady); await window.FinishAsync();
                updated &= window.Displayed.Percentage == 100;
                model.Report(LaunchState.StartingUU); // Closed subscriber has been detached.
                var failedModel = new LaunchProgress(); var failedWindow = new LaunchProgressWindow(failedModel);
                failedWindow.Closed += (_, _) => failureClosed = true;
                failedModel.Report(LaunchState.PreparingHeroes); failedModel.Report(LaunchState.Failed);
                await failedWindow.FinishAsync();
                failureClosed &= failedWindow.Displayed == new LaunchProgressSnapshot(LaunchState.Failed, 90);
            }
            var task = ExerciseAsync();
            _ = task.ContinueWith(t => { error = t.Exception; dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }, TaskScheduler.Default);
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        bool completed = thread.Join(TimeSpan.FromSeconds(15));
        check(completed && error == null && updated, "WPF dispatcher applies background stages and renders final 100 percent");
        check(completed && error == null && closed && failureClosed, "progress window closes deterministically after success and failure");
        check(closeIgnored && noButtons, "no progress buttons or new user-close cancellation semantics");
    }
}

sealed class ProgressFakeCli : IUuCliService
{
    public static HeroesBoostStatus Ready => new(true, "boosting", null, null, null, null, null, null);
    public int Starts, Statuses;
    public Task<BoostOperationData> StartHeroesBoostAsync(CancellationToken token = default) { Starts++; return Task.FromResult(new BoostOperationData()); }
    public Task<HeroesBoostStatus> GetHeroesBoostStatusAsync(CancellationToken token = default) { Statuses++; return Task.FromResult(Ready); }
    public Task<BoostOperationData> StopHeroesBoostAsync(CancellationToken token = default) => throw new NotSupportedException();
}
