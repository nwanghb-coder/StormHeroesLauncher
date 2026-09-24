using System.Runtime.InteropServices;
using System.Text;
using StormHeroesLauncher.Configuration;
namespace StormHeroesLauncher.Services;
public sealed class WindowPolicy(AppLogger logger, Func<string, CancellationToken, Task<bool>> minimize)
{
    public async Task ApplyAsync(string processName, bool shouldMinimize, CancellationToken token)
    {
        if (!shouldMinimize) { logger.Write($"{processName}：保留现有窗口/托盘状态。"); return; }
        try
        {
            bool success = await minimize(processName, token);
            logger.Write(success ? $"{processName}：已确认窗口进入后台。" : $"{processName}：未能确认最小化（窗口不存在、权限限制或应用状态变化）；继续流程，需要人工检查，尚不满足分发验收。");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { logger.Write($"{processName}：窗口处理失败 {ex.GetType().Name}；继续流程，需人工检查。"); }
    }
    public Task BattleNetAsync(BattleNetWindowMode mode, CancellationToken token) => ApplyAsync("Battle.net", mode == BattleNetWindowMode.Minimized, token);
}

// Titles are used only for matching in memory; they are never included in diagnostic logs.
public sealed record ExternalWindow(IntPtr Handle, uint Pid, string ProcessName, string ClassName,
    string Title, bool Visible, bool Enabled, IntPtr Owner, uint OwnerPid, bool Minimized);
public static class ExternalWindowSelection
{
    public static string[] Family(string application) => application == "uu"
        ? ["uu", "uu_launcher", "uu_ball", "uu_cloudsyn"] : application == "Battle.net"
        ? ["Battle.net", "Battle.net Launcher"] : [];
    public static bool IsMain(string application, ExternalWindow window, IReadOnlySet<uint> familyPids)
    {
        if (!window.Visible || !familyPids.Contains(window.Pid) ||
            !Family(application).Contains(window.ProcessName, StringComparer.OrdinalIgnoreCase)) return false;
        // An owned top-level main window is valid if its owner belongs to the same family.
        if (window.Owner != IntPtr.Zero && !familyPids.Contains(window.OwnerPid)) return false;
        if (application == "Battle.net")
            return window.ClassName.StartsWith("Chrome_WidgetWin_", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(window.Title);
        string title = window.Title.Replace(" ", "").Trim();
        return title.Equals("UU加速器", StringComparison.OrdinalIgnoreCase) || title.Equals("网易UU加速器", StringComparison.OrdinalIgnoreCase) ||
            title.Equals("UUAccelerator", StringComparison.OrdinalIgnoreCase);
    }
}
public interface IExternalWindowApi
{
    IReadOnlyList<ExternalWindow> Snapshot(string application);
    bool RequestMinimize(ExternalWindow window);
}
public sealed class ExternalWindowMinimizer(AppLogger logger, IExternalWindowApi api,
    int samples = 20, int intervalMilliseconds = 250)
{
    public async Task<bool> MinimizeAsync(string application, CancellationToken token)
    {
        var attempts = new Dictionary<(IntPtr, uint), int>();
        var logged = new HashSet<(IntPtr, uint, bool, bool)>();
        int stable = 0;
        var previous = new HashSet<(IntPtr, uint)>();
        for (int sample = 0; sample < samples; sample++)
        {
            token.ThrowIfCancellationRequested();
            var snapshot = api.Snapshot(application);
            var family = snapshot.Where(w => ExternalWindowSelection.Family(application).Contains(w.ProcessName, StringComparer.OrdinalIgnoreCase)).Select(w => w.Pid).ToHashSet();
            var selected = snapshot.Where(w => ExternalWindowSelection.IsMain(application, w, family)).ToArray();
            foreach (var window in snapshot.Where(w => w.Visible))
            {
                bool chosen = selected.Contains(window);
                if (logged.Add((window.Handle, window.Pid, chosen, window.Minimized)))
                    Log(application, window, "Candidate", chosen ? "Selected" : "Rejected");
            }
            if (selected.Length > 0 && selected.All(w => w.Minimized))
            {
                var current = selected.Select(w => (w.Handle, w.Pid)).ToHashSet();
                stable = previous.SetEquals(current) ? stable + 1 : 1;
                previous = current;
                if (stable >= 3) { foreach (var window in selected) Log(application, window, "Verify", "VerifiedMinimized (3 consecutive snapshots)"); return true; }
            }
            else { stable = 0; previous.Clear(); }
            foreach (var window in selected.Where(w => !w.Minimized))
            {
                var key = (window.Handle, window.Pid);
                int count = attempts.GetValueOrDefault(key);
                if (count >= 3) continue;
                attempts[key] = count + 1;
                bool accepted = api.RequestMinimize(window);
                Log(application, window, "Minimize(SW_MINIMIZE)", accepted ? "RequestAccepted; awaiting IsIconic" : "RequestRejectedOrIdentityChanged");
            }
            if (sample + 1 < samples) await Task.Delay(intervalMilliseconds, token);
        }
        logger.Write($"{application} window Result={(attempts.Count == 0 ? "NoVerifiedMainWindowOrNoStableConfirmation" : "MinimizeNotConfirmed")}; bounded observation ended; no close/hide/elevation.");
        return false;
    }
    private void Log(string app, ExternalWindow w, string action, string result) => logger.Write(
        $"{app} window candidate: PID={w.Pid} HWND=0x{w.Handle.ToInt64():X} Process={w.ProcessName} Class={w.ClassName} Visible={w.Visible} Enabled={w.Enabled} Owner=0x{w.Owner.ToInt64():X} OwnerPID={w.OwnerPid} IsIconic={w.Minimized} Action={action} Result={result}");
}
public sealed class NativeWindowManager : IExternalWindowApi
{
    public IReadOnlyList<ExternalWindow> Snapshot(string application)
    {
        var processes = new Dictionary<uint, string>();
        foreach (string name in ExternalWindowSelection.Family(application))
            foreach (uint pid in DesktopProcessState.Find(name)) processes.TryAdd(pid, name);
        var windows = new List<ExternalWindow>();
        if (!EnumWindows((window, _) => {
            GetWindowThreadProcessId(window, out uint pid);
            if (!processes.TryGetValue(pid, out string? name)) return true;
            var title = new StringBuilder(256); GetWindowText(window, title, title.Capacity);
            var cls = new StringBuilder(256); GetClassName(window, cls, cls.Capacity);
            IntPtr owner = GetWindow(window, 4); uint ownerPid = 0;
            if (owner != IntPtr.Zero) GetWindowThreadProcessId(owner, out ownerPid);
            windows.Add(new(window, pid, name, cls.ToString(), title.ToString(), IsWindowVisible(window), IsWindowEnabled(window), owner, ownerPid, IsIconic(window)));
            return true;
        }, IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "EnumWindows failed");
        return windows;
    }
    public bool RequestMinimize(ExternalWindow window)
    {
        // Reject stale/reused HWNDs and changed process ownership before acting.
        GetWindowThreadProcessId(window.Handle, out uint pid);
        if (pid != window.Pid || !DesktopProcessState.Find(window.ProcessName).Contains(pid) || !IsWindowVisible(window.Handle)) return false;
        var cls = new StringBuilder(256); GetClassName(window.Handle, cls, cls.Capacity);
        if (cls.ToString() != window.ClassName) return false;
        return ShowWindowAsync(window.Handle, 6); // SW_MINIMIZE; never SW_HIDE or input simulation.
    }
    private delegate bool Callback(IntPtr window, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool EnumWindows(Callback callback, IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int count);
}
