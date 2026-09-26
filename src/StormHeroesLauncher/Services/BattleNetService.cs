using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
namespace StormHeroesLauncher.Services;
public sealed class BattleNetService(AppLogger logger, string ExecutablePath, bool manageWindows = false, Models.LaunchProgress? progress = null)
{
    public static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(60);
    public void ValidateInstallation()
    {
        if (!File.Exists(ExecutablePath)) throw new FileNotFoundException($"Battle.net 文件不存在：{ExecutablePath}");
    }
    public Task EnsureReadyAsync(CancellationToken token) => Task.Run(async () =>
    {
        token.ThrowIfCancellationRequested();
        var clock=Stopwatch.StartNew();
        var early=manageWindows ? new EarlyBattleNetSuppression(logger,()=>new NativeWindowManager().Snapshot("Battle.net"),()=>DesktopProcessState.Find("Battle.net"),(w,h)=>BattleNetTray.NativeAction(w,h,logger),()=>clock.ElapsedMilliseconds) : null;
        early?.Arm();
        bool alreadyRunning = DesktopProcessState.Find("Battle.net").Count > 0;
        if (!alreadyRunning)
        {
            ValidateInstallation();
            if (DesktopProcessState.Find("Battle.net").Count == 0)
            {
                logger.Write($"正常权限启动 Battle.net：{ExecutablePath}");
                progress?.Report(Models.LaunchState.StartingBattleNet);
                early?.MarkLaunch();
                using var process = Process.Start(new ProcessStartInfo(ExecutablePath)
                {
                    UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(ExecutablePath)!
                });
                if (process == null) throw new InvalidOperationException("Battle.net 未返回启动进程。");
            }
        }
        else logger.Write("Battle.net 已运行，不重复启动。");
        progress?.Report(Models.LaunchState.WaitingForBattleNet);
        logger.Write("等待 Battle.net 可用顶层窗口（已运行的 Chromium 主窗口可处于托盘隐藏状态），最长 60 秒；此检查不能确认账号登录状态。");
        var timer = Stopwatch.StartNew();
        IntPtr lastWindow = IntPtr.Zero;
        int stableSamples = 0; long nextReadiness=0;
        while (timer.Elapsed < ReadinessTimeout)
        {
            token.ThrowIfCancellationRequested();
            early?.Poll();
            if (timer.ElapsedMilliseconds < nextReadiness) { await Task.Delay(early?.Active == true ? early.PollDelay : 100,token); continue; }
            nextReadiness=timer.ElapsedMilliseconds+500;
            if (manageWindows && early?.Active != true) BattleNetTray.HideTransient(logger);
            IntPtr window = FindReadyWindow(DesktopProcessState.Find("Battle.net"), alreadyRunning || manageWindows);
            stableSamples = window != IntPtr.Zero && window == lastWindow ? stableSamples + 1 : window != IntPtr.Zero ? 1 : 0;
            lastWindow = window;
            if (stableSamples >= 3 && early?.Active != true)
            {
                logger.Write("Battle.net 窗口就绪：同一窗口连续三次通过身份/状态检查；已启用窗口管理时冷/暖启动均可隐藏；依赖用户已配置自动登录。");
                return;
            }
            await Task.Delay(early?.Active == true ? early.PollDelay : 500, token);
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
