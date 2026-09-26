using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
namespace StormHeroesLauncher.Services;
public sealed class BattleNetService(AppLogger logger, string ExecutablePath, bool manageWindows = false, Models.LaunchProgress? progress = null)
{
    public static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(60);
    public bool ReusedExisting { get; private set; }
    public Task<bool> TryReuseAsync(CancellationToken token) => Task.Run(async () =>
    {
        ReusedExisting = false;
        try
        {
            bool running = DesktopProcessState.Find("Battle.net").Count > 0;
            logger.Write($"WarmFastPath BattleNetRunning={running}");
            ReusedExisting = running && await ConfirmExistingAsync(
                () => FindReadyWindow(DesktopProcessState.Find("Battle.net"), true),
                t => Task.Delay(500, t), token);
            logger.Write(ReusedExisting ? "WarmFastPath BattleNet=AlreadyReady BattleNetReady=True" :
                "WarmFastPath Decision=Fallback Reason=BattleNetNotReady");
            return ReusedExisting;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        { logger.Write($"WarmFastPath Decision=Fallback Reason=BattleNetProxyUnknown ErrorType={ex.GetType().Name}"); return false; }
    }, token);
    // The same existing proxy: three identical qualified window samples, no login-state claim.
    public static async Task<bool> ConfirmExistingAsync(Func<IntPtr> find, Func<CancellationToken, Task> delay, CancellationToken token)
    {
        IntPtr previous = IntPtr.Zero;
        for (int i = 0; i < 3; i++)
        {
            token.ThrowIfCancellationRequested();
            IntPtr current = find();
            if (current == IntPtr.Zero || i > 0 && current != previous) return false;
            previous = current;
            if (i < 2) await delay(token);
        }
        return true;
    }
    public void ValidateInstallation()
    {
        if (!File.Exists(ExecutablePath)) throw new FileNotFoundException($"Battle.net 文件不存在：{ExecutablePath}");
    }
    public Task EnsureReadyAsync(CancellationToken token) => Task.Run(async () =>
    {
        token.ThrowIfCancellationRequested();
        var clock=Stopwatch.StartNew();
        var native = new BattleNetNativeWindows();
        var early = manageWindows ? new EarlyBattleNetSuppression(logger, native.Snapshot, () => native.Owners,
            (w,h) => BattleNetNativeWindows.Act(w,h,() => clock.Elapsed.TotalMilliseconds), () => clock.Elapsed.TotalMilliseconds) : null;
        bool alreadyRunning = DesktopProcessState.Find("Battle.net").Count > 0;
        if (!alreadyRunning)
        {
            ValidateInstallation();
            if (DesktopProcessState.Find("Battle.net").Count == 0)
            {
                token.ThrowIfCancellationRequested();
                logger.Write($"正常权限启动 Battle.net：{ExecutablePath}");
                progress?.Report(Models.LaunchState.StartingBattleNet);
                void Launch()
                {
                    token.ThrowIfCancellationRequested();
                    using var process = Process.Start(new ProcessStartInfo(ExecutablePath)
                    {
                        UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(ExecutablePath)!
                    });
                    if (process == null) throw new InvalidOperationException("Battle.net 未返回启动进程。");
                }
                // Starts polling before Process.Start, continues independently for at most five seconds.
                if (early != null)
                {
                    BattleNetTray.ColdObserver = early;
                    _ = early.StartBeforeLaunch(Launch, token);
                }
                else Launch();
            }
        }
        else logger.Write("Battle.net 已运行，不重复启动。");
        progress?.Report(Models.LaunchState.WaitingForBattleNet);
        logger.Write("等待 Battle.net 可用顶层窗口（已运行的 Chromium 主窗口可处于托盘隐藏状态），最长 60 秒；此检查不能确认账号登录状态。");
        var timer = Stopwatch.StartNew();
        IntPtr lastWindow = IntPtr.Zero;
        int stableSamples = 0;
        while (timer.Elapsed < ReadinessTimeout)
        {
            token.ThrowIfCancellationRequested();
            if (manageWindows && early?.Active != true) BattleNetTray.HideTransient(logger);
            IntPtr window = FindReadyWindow(DesktopProcessState.Find("Battle.net"), alreadyRunning || manageWindows);
            stableSamples = window != IntPtr.Zero && window == lastWindow ? stableSamples + 1 : window != IntPtr.Zero ? 1 : 0;
            lastWindow = window;
            if (stableSamples >= 3)
            {
                logger.Write($"Battle.net ReadinessConfirmedT={DateTimeOffset.UtcNow:O} ReadinessElapsedMs={timer.Elapsed.TotalMilliseconds:F3} HiddenChromiumAllowed={alreadyRunning || manageWindows}");
                logger.Write("Battle.net 窗口就绪：同一窗口连续三次通过身份/状态检查；已启用窗口管理时冷/暖启动均可隐藏；依赖用户已配置自动登录。");
                return;
            }
            await Task.Delay(500, token);
        }
        throw new TimeoutException("Battle.net 窗口就绪等待超时（60 秒）。请检查 Battle.net 后手动重试启动器。");
    }, token);
    public static bool ReadyCandidate(bool visible,bool enabled,bool ownerless,bool hung,bool alreadyRunning,string className) =>
        enabled && ownerless && !hung && className.StartsWith("Chrome_WidgetWin_",StringComparison.Ordinal) && (visible || alreadyRunning);
    private static IntPtr FindReadyWindow(HashSet<uint> owners,bool alreadyRunning)
    {
        IntPtr found = IntPtr.Zero;
        if (owners.Count == 0) return found;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint owner);
            var cls = new StringBuilder(256); GetClassName(window,cls,cls.Capacity);
            if (owners.Contains(owner) && ReadyCandidate(IsWindowVisible(window),IsWindowEnabled(window),GetWindow(window,4) == IntPtr.Zero,IsHungAppWindow(window),alreadyRunning,cls.ToString()))
            { found = window; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr window,StringBuilder text,int count);
    private delegate bool WindowCallback(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsHungAppWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
}
