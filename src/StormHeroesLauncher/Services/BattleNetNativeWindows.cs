using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace StormHeroesLauncher.Services;

// Title-free snapshots; no synchronous cross-process messages.
internal sealed class BattleNetNativeWindows
{
    public HashSet<uint> Owners { get; private set; } = [];
    private static readonly object gate = new();
    private static readonly HashSet<(uint, IntPtr, string)> attempted = [];

    public IReadOnlyList<ExternalWindow> Snapshot()
    {
        var main = DesktopProcessState.Find("Battle.net");
        var launchers = DesktopProcessState.Find("Battle.net Launcher");
        Owners = main.Union(launchers).ToHashSet();
        var windows = new List<ExternalWindow>();
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (!Owners.Contains(pid)) return true;
            string cls = Class(hwnd);
            if (cls != "Qt5151QWindowIcon" && !cls.StartsWith("Chrome_WidgetWin_", StringComparison.Ordinal)) return true;
            windows.Add(new(hwnd, pid, main.Contains(pid) ? "Battle.net" : "Battle.net Launcher",
                cls, "", IsWindowVisible(hwnd), IsWindowEnabled(hwnd), GetWindow(hwnd, 4), 0, false));
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    public static BattleNetVisualResult Act(ExternalWindow target, bool transient, Func<double> milliseconds)
    {
        try
        {
            // Keep the process handle across both validations to reject PID reuse.
            using var process = Process.GetProcessById((int)target.Pid);
            using var self = Process.GetCurrentProcess();
            _ = process.Handle;
            bool family = process.ProcessName.Equals("Battle.net", StringComparison.OrdinalIgnoreCase) ||
                transient && process.ProcessName.Equals("Battle.net Launcher", StringComparison.OrdinalIgnoreCase);
            if (!family || process.SessionId != self.SessionId || process.HasExited)
                return new(null, null, false, false, false, "WrongProcessOrSession");
            bool Valid(bool visible)
            {
                if (process.HasExited || !IsWindow(target.Handle)) return false;
                GetWindowThreadProcessId(target.Handle, out uint pid);
                return pid == target.Pid && GetAncestor(target.Handle, 2) == target.Handle &&
                    GetWindow(target.Handle, 4) == IntPtr.Zero && IsWindowEnabled(target.Handle) &&
                    (!visible || IsWindowVisible(target.Handle)) && Class(target.Handle) == target.ClassName &&
                    (transient ? target.ClassName == "Qt5151QWindowIcon" :
                        target.ClassName.StartsWith("Chrome_WidgetWin_", StringComparison.Ordinal));
            }
            lock (gate)
            {
                if (attempted.Count >= 32 || !attempted.Add((target.Pid, target.Handle, target.ClassName)))
                    return new(null, null, false, false, !process.HasExited, "AlreadyHandledOrCapacity");
            }
            return BattleNetVisualAction.Run(transient, Valid, () => ShowWindowAsync(target.Handle, 0),
                () => PostMessage(target.Handle, 0x10, IntPtr.Zero, IntPtr.Zero), () => !process.HasExited, milliseconds);
        }
        catch { return new(null, null, false, false, false, "Unavailable"); }
    }

    private static string Class(IntPtr hwnd)
    { var text = new StringBuilder(256); GetClassName(hwnd, text, text.Capacity); return text.ToString(); }
    private delegate bool WindowCallback(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr data);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool ShowWindowAsync(IntPtr hwnd, int command);
    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
}
