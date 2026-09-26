using System.Windows;
using System.Windows.Input;
using StormHeroesLauncher.Configuration;
using StormHeroesLauncher.Services;
using StormHeroesLauncher.Models;
namespace StormHeroesLauncher;
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
#if DEVELOPER_OBSERVER
        if (e.Args.FirstOrDefault() == "--observer-tail")
        {
            int result = 3;
            try { result = await Observer.ObserverTail.RunAsync(e.Args); } catch { }
            Shutdown(result); return;
        }
        if (e.Args.FirstOrDefault() == "--test-heroes-prep")
        {
            int result = 2;
            try { if (e.Args.Length == 2) result = await Task.Run(() => Observer.DeveloperHeroesPrep.RunAsync(e.Args[1])); }
            catch { result = 3; }
            Shutdown(result); return; // No settings, launch mutex, UU, Battle.net or elevated helper.
        }
#endif
        if (e.Args.FirstOrDefault() is "--test-heroes-prep" or "--observer-tail") { Shutdown(2); return; }
        bool shiftHeld = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        var logger = new AppLogger();
        using var cancellation = new CancellationTokenSource();
        Mutex? mutex = null; bool owned = false; int exitCode = 0;
        LaunchProgress? progress = null;
        LaunchProgressWindow? progressWindow = null;
#if DEVELOPER_OBSERVER
        Observer.ObserverHost? observer = null;
        Observer.UuStartupObserver? startupObserver = null;
#endif
        try
        {
            mutex = new Mutex(false, @"Local\StormHeroesLauncher.Alpha.LaunchWorkflow");
            try { owned = mutex.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
            if (!owned) { logger.Write("已有启动流程/设置窗口正在运行，重复启动退出。"); return; }
            logger.Write($"HOSLauncher {AboutSafetyContent.Version} 启动；single-uac-result-v6。");
            var store = new SettingsStore(); var discovery = new PathDiscovery(logger);
            var cliValidation = new WindowsCliValidation();
            var preparation = new UuCliPreparation(logger, cliValidation, new UuCliArchive());
            LauncherSettings settings; string? loadError = null;
            try { settings = store.Load(); }
            catch (Exception ex) { settings = new(); loadError = $"无法读取设置：{ex.GetType().Name}。请重新配置并保存。"; logger.Write(loadError); }
            if(ShortcutImport.IsImport(e.Args))
            {
                // Resolve Shell links on the WPF STA; all arguments are data and never reach a process launcher.
                var imported=loadError == null ? new ShortcutImport(new ShellShortcutReader(),new ImportIdentity(),preparation.Resolve,logger).Run(e.Args,settings)
                    : new ShortcutImportResult(settings,false,false,"设置文件无法读取，未覆盖现有设置。请打开设置检查。");
                if(imported.Accepted)
                {
                    try {store.SaveImported(imported.Settings);settings=imported.Settings;}
                    catch(Exception ex) {logger.Write($"Shortcut import: SaveFailed Exception={ex.GetType().Name}");imported=imported with {Accepted=false,Message="无法保存配置。请检查设置目录权限。"};}
                }
                var presentation=ShortcutImportPresentation.Decide(imported);
                logger.Write($"Shortcut import UX: Mode={presentation.Mode}");
                if(presentation.Mode!=ImportDisplayMode.Silent)new ShortcutImportWindow(presentation.Message).ShowDialog();
                return; // Every import outcome exits before normal game launch.
            }
            try { settings = await Task.Run(() => discovery.Discover(settings)); }
            catch (Exception ex) { loadError = $"自动检测未完成：{ex.GetType().Name}。请手动选择安装路径。"; logger.Write(loadError); }
            var prepared = await Task.Run(() => preparation.Resolve(settings.UuCliPath, settings.UuLauncherPath));
            settings = settings with { UuCliPath = prepared.Path };
            if (!prepared.Success) loadError = string.Join("\n", new[] { loadError, prepared.Message }.Where(s => !string.IsNullOrEmpty(s)));
            if (StartupRouting.OpenSettings(e.Args, shiftHeld, settings.Validate().Length == 0, loadError != null))
            { new SettingsWindow(settings, store, discovery, preparation, logger, loadError).ShowDialog(); return; }
            // Routing must be resolved first: settings recovery/imports never flash a launch window.
            if (StartupRouting.ShowLaunchProgress(e.Args, shiftHeld, settings.Validate().Length == 0, loadError != null))
            {
                progress = new LaunchProgress(logger.Write);
                try
                {
                    progressWindow = new LaunchProgressWindow(progress, cancellation.Cancel);
                    progressWindow.Show();
                    await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                }
                catch (Exception uiError) { logger.Write($"Progress UI unavailable: {uiError.GetType().Name}; launch continues."); }
            }
            cancellation.Token.ThrowIfCancellationRequested();
            settings.RequireValid(); store.Save(settings);
            logger.WriteOperation("已通过启动前检查", settings);
            var options = new UuCliOptions { CliPath = settings.UuCliPath }; options.Validate();
            var uu = new UuService(logger, settings.UuLauncherPath, progress);


            var windows = new WindowPolicy(logger, (app, token) => Task.Run(() => BattleNetTray.CloseAsync(logger, token), token));

            var cli = new UuCliService(options, logger, new ValidatedCliRunner(cliValidation, new CliProcessRunner()));
            var boost = new HeroesBoostWorkflow(uu.DetectOrStartAsync, cli, options, logger, progress, UuService.IsRunningAsync);
            var battleNet = new BattleNetService(logger, settings.BattleNetPath, settings.BattleNetWindowMode == BattleNetWindowMode.Minimized, progress);
            var heroes = new HeroesProcessService(logger, settings.HeroesSwitcherPath, progress);
#if DEVELOPER_OBSERVER
            logger.Write("ObserverBuild=True; read-only developer observation enabled.");
            observer = Observer.ObserverHost.Start(settings.UuLauncherPath, settings.BattleNetPath, logger.Write);
            if (progress != null) progress.Changed += snapshot =>
            {
                if (snapshot.State == LaunchState.StartingUU && !cancellation.IsCancellationRequested)
                    startupObserver ??= Observer.UuStartupObserver.Start(settings.UuLauncherPath, settings.BattleNetPath, cancellation.Token, logger.Write);
            };
#endif
            var workflow = new HeroesLaunchWorkflow(heroes.IsRunningAsync,
                token => boost.StartAsync(logger.Write, token),
                async token => { await battleNet.EnsureReadyAsync(token); await windows.BattleNetAsync(settings.BattleNetWindowMode, token); },
                async token => { if (!battleNet.ReusedExisting) await windows.BattleNetAsync(settings.BattleNetWindowMode, token); await heroes.LaunchAndWaitAsync(token);
                    logger.Write("游戏进程已确认，复查启动期间可能恢复的外部窗口。");

                    if (!battleNet.ReusedExisting) await windows.BattleNetAsync(settings.BattleNetWindowMode, token); }, settings.RequireValid, logger, progress, heroes.WaitForExistingUiAsync, battleNet.TryReuseAsync);
            await workflow.RunAsync(cancellation.Token);
#if DEVELOPER_OBSERVER
            // Await only bounded flush/process dispatch, never the detached worker's observation lifetime.
            Task observerTail = observer?.DetachTailAsync(settings.UuLauncherPath, settings.BattleNetPath) ?? Task.CompletedTask;
#endif
            await FinishProgressAsync(progressWindow, logger);
#if DEVELOPER_OBSERVER
            await observerTail;
#endif
            logger.Write("流程完成，启动器退出；保留 UU、战网及游戏运行。");
        }
        catch (Exception) when (cancellation.IsCancellationRequested)
        {
            progress?.Report(LaunchState.Cancelled);
            logger.Write("LaunchCancelled Reason=Esc; no rollback, stop-boost or external process termination.");
            await FinishProgressAsync(progressWindow, logger);
        }
        catch (Exception ex)
        {
            progress?.Report(LaunchState.Failed);
#if DEVELOPER_OBSERVER
            Task observerStop = observer?.StopAsync() ?? Task.CompletedTask;
#endif
            await FinishProgressAsync(progressWindow, logger);
#if DEVELOPER_OBSERVER
            await observerStop;
#endif
            exitCode = 1; logger.Write($"启动失败：{ex.GetType().Name}：{ex.Message}");
            MessageBox.Show($"启动未完成：{ex.Message}\n\n请先在 UU 和战网手动登录并启用记住/自动登录，确认 UU 会员有效、游戏更新完成。\n不会自动关闭外部程序或停止加速。\n日志：{logger.LogPath}", $"HOSLauncher {AboutSafetyContent.Version}", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            try { progressWindow?.CloseForShutdown(); } catch { }
            // Release on the owning WPF thread before bounded observer teardown, including Esc.
            if (owned) { mutex!.ReleaseMutex(); owned = false; }
            mutex?.Dispose();
#if DEVELOPER_OBSERVER
            if (startupObserver != null) await startupObserver.StopAsync();
            if (observer != null) await observer.StopAsync();
#endif
            Shutdown(exitCode);
        }
    }
    private static async Task FinishProgressAsync(LaunchProgressWindow? window, AppLogger logger)
    {
        try { if (window != null) await window.FinishAsync(); }
        catch (Exception ex) { logger.Write($"Progress UI close failed: {ex.GetType().Name}; launch result unchanged."); }
    }
}
