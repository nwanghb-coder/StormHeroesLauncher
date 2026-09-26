using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace StormHeroesLauncher.Services;

public sealed record HeroesProcessIdentity(uint Pid, uint ParentPid, long Created, uint Session, string Path);
public sealed record HeroesWindowIdentity(long Hwnd, HeroesProcessIdentity Process, string ClassName,
    bool Visible, bool Enabled, bool Ownerless, bool Hung, long Style, long ExStyle, int Width, int Height);

// Documented, read-only process metadata and top-level window APIs. Only Hide changes visibility.
public static class HeroesWindowProbe
{
    public static HeroesProcessIdentity? ReadProcess(uint pid, uint parent = 0)
    {
        IntPtr handle = OpenProcess(0x1000, false, pid);
        if (handle == IntPtr.Zero) return null;
        try { return ReadProcessHandle(handle, pid, parent); }
        finally { CloseHandle(handle); }
    }
    public static HeroesProcessIdentity? ReadProcessHandle(IntPtr handle, uint pid, uint parent = 0)
    {
        var path = new StringBuilder(32768); uint length = (uint)path.Capacity;
        if (!GetProcessTimes(handle, out long created, out long exited, out _, out _) || exited != 0 ||
            !QueryFullProcessImageName(handle, 0, path, ref length) || !ProcessIdToSessionId(pid, out uint session)) return null;
        return new(pid, parent, created, session, path.ToString());
    }
    public static IReadOnlyList<HeroesProcessIdentity> Processes()
    {
        var result = new List<HeroesProcessIdentity>();
        IntPtr snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == new IntPtr(-1)) throw new Win32Exception();
        try
        {
            var entry = new Entry { Size = (uint)Marshal.SizeOf<Entry>() };
            if (!Process32First(snapshot, ref entry)) throw new Win32Exception();
            do
            {
                if (entry.Exe.Equals("HeroesOfTheStorm_x64.exe", StringComparison.OrdinalIgnoreCase) ||
                    entry.Exe.Equals("HeroesSwitcher_x64.exe", StringComparison.OrdinalIgnoreCase))
                    if (ReadProcess(entry.Pid, entry.Parent) is { } identity) result.Add(identity);
            } while (Process32Next(snapshot, ref entry));
        }
        finally { CloseHandle(snapshot); }
        return result;
    }
    public static bool Same(HeroesProcessIdentity expected, HeroesProcessIdentity? current) => current != null &&
        expected.Pid == current.Pid && expected.Created == current.Created && expected.Session == current.Session &&
        expected.Path.Equals(current.Path, StringComparison.OrdinalIgnoreCase);
    public static bool IsGame(HeroesProcessIdentity p, string switcherPath)
    {
        string versions = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(switcherPath)!, "..", "Versions")) + Path.DirectorySeparatorChar;
        return ProcessIdToSessionId((uint)Environment.ProcessId, out uint own) && p.Session == own &&
            Path.GetFullPath(p.Path).StartsWith(versions, StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(p.Path).Equals("HeroesOfTheStorm_x64.exe", StringComparison.OrdinalIgnoreCase);
    }
    public static IReadOnlyList<HeroesWindowIdentity> Windows(IReadOnlyList<HeroesProcessIdentity> processes)
    {
        var result = new List<HeroesWindowIdentity>();
        var owners = processes.ToDictionary(p => p.Pid);
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (!owners.TryGetValue(pid, out var p) || !Same(p, ReadProcess(pid))) return true;
            var cls = new StringBuilder(256); GetClassName(hwnd, cls, cls.Capacity); GetWindowRect(hwnd, out var rect);
            result.Add(new(hwnd.ToInt64(), p, cls.ToString(), IsWindowVisible(hwnd), IsWindowEnabled(hwnd),
                GetWindow(hwnd, 4) == IntPtr.Zero, IsHungAppWindow(hwnd), GetWindowLongPtr(hwnd, -16).ToInt64(),
                GetWindowLongPtr(hwnd, -20).ToInt64(), Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top)));
            return result.Count < 128;
        }, IntPtr.Zero);
        return result;
    }
    public static bool IsPreparation(HeroesWindowIdentity w) => w.Visible && w.Ownerless && w.ClassName == "#32770";
    public static bool Hide(HeroesWindowIdentity w)
    {
        if (!IsPreparation(w) || !Same(w.Process, ReadProcess(w.Process.Pid))) return false;
        var now = Windows([w.Process]).SingleOrDefault(n => n.Hwnd == w.Hwnd);
        return now != null && IsPreparation(now) && ShowWindowAsync(new IntPtr(w.Hwnd), 0);
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct Entry
    { public uint Size, Usage, Pid; public UIntPtr Heap; public uint Module, Threads, Parent; public int Priority; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Exe; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    private delegate bool Callback(IntPtr hwnd, IntPtr data);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode)] private static extern bool Process32First(IntPtr snapshot, ref Entry entry);
    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode)] private static extern bool Process32Next(IntPtr snapshot, ref Entry entry);
    [DllImport("kernel32.dll")] public static extern bool GetProcessTimes(IntPtr handle, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern bool ProcessIdToSessionId(uint pid, out uint session);
    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref uint size);
    [DllImport("user32.dll")] private static extern bool EnumWindows(Callback callback, IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsHungAppWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rectangle);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
}
