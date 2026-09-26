using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace StormHeroesLauncher.Observer;

public static class ProcessSelection
{
    public static IReadOnlyList<RelatedProcess> Select(IReadOnlyList<ProcessIdentity> snapshot,
        InstallationScope scope, uint ownSession, Func<string?, FileMetadata?> metadata)
    {
        var selected = new Dictionary<uint, RelatedProcess>();
        var candidates = snapshot.Where(p => p.WindowsSessionId == ownSession && !Excluded(p.Name)).ToArray();
        foreach (var p in candidates)
            if (scope.Classify(p.ExecutablePath) is Family family)
                selected[p.Pid] = new(p, family, family, "KnownInstallRoot", Path.GetDirectoryName(p.ExecutablePath), metadata(p.ExecutablePath));
        // Parent identity must exist in this snapshot and predate the child. Never trust a stale PID.
        for (int depth = 0; depth < 8 && selected.Count < 128; depth++)
        {
            bool added = false;
            foreach (var p in candidates)
            {
                if (selected.Count >= 128) break;
                if (selected.ContainsKey(p.Pid) || !selected.TryGetValue(p.ParentPid, out var parent) ||
                    p.CreatedFileTime == null || parent.Identity.CreatedFileTime == null ||
                    p.CreatedFileTime < parent.Identity.CreatedFileTime) continue;
                selected[p.Pid] = new(p, Family.UnknownRelated, parent.RelatedTo, "ObservedParent",
                    p.ExecutablePath == null ? null : Path.GetDirectoryName(p.ExecutablePath), null);
                added = true;
            }
            if (!added) break;
        }
        if (selected.Count > 128) throw new InvalidDataException("Relevant process limit");
        return selected.Values.ToArray();
    }
    private static bool Excluded(string name) => name.StartsWith("StormHeroesLauncher", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("HOSLauncher", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Heroes", StringComparison.OrdinalIgnoreCase);
}

public sealed class WindowsObserverSource(InstallationScope scope) : IObserverSource
{
    private readonly Dictionary<string, (long Checked, FileMetadata? Metadata)> metadata = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> paths = new();
    public IReadOnlyList<RelatedProcess> Processes()
    {
        if (!Native.ProcessIdToSessionId((uint)Environment.ProcessId, out uint session)) throw new Win32Exception();
        var processes = new List<ProcessIdentity>();
        var liveKeys = new HashSet<string>();
        IntPtr snapshot = Native.CreateToolhelp32Snapshot(2, 0); // TH32CS_SNAPPROCESS only: no modules/heaps.
        if (snapshot == new IntPtr(-1)) throw new Win32Exception();
        try
        {
            var entry = new Native.ProcessEntry { Size = (uint)Marshal.SizeOf<Native.ProcessEntry>() };
            if (!Native.Process32First(snapshot, ref entry)) throw new Win32Exception();
            do
            {
                if (processes.Count >= 4096) throw new InvalidDataException("Process snapshot limit");
                if (!Native.ProcessIdToSessionId(entry.Pid, out uint processSession) || processSession != session) continue;
                var process = ReadIdentity(entry.Pid, entry.ParentPid, entry.Name, processSession);
                liveKeys.Add(process.Key);
                processes.Add(process);
            } while (Native.Process32Next(snapshot, ref entry));
            if (Marshal.GetLastWin32Error() != 18) throw new Win32Exception(); // ERROR_NO_MORE_FILES
        }
        finally { Native.CloseHandle(snapshot); }
        foreach (string key in paths.Keys.Where(k => !liveKeys.Contains(k)).ToArray()) paths.Remove(key);
        if (metadata.Count > 512) metadata.Clear();
        return ProcessSelection.Select(processes, scope, session, ReadMetadata);
    }
    private FileMetadata? ReadMetadata(string? path)
    {
        if (path == null) return null;
        long now = Environment.TickCount64;
        if (!metadata.TryGetValue(path, out var cached) || now - cached.Checked >= 10_000)
            metadata[path] = cached = (now, scope.ReadProcessFile(path));
        return cached.Metadata;
    }
    private ProcessIdentity ReadIdentity(uint pid, uint parent, string name, uint session)
    {
        // Limited information only; no VM_READ or debug privilege.
        IntPtr handle = Native.OpenProcess(0x1000, false, pid);
        if (handle == IntPtr.Zero) return new(pid, parent, name, null, session, null);
        try
        {
            long? created = Native.GetProcessTimes(handle, out long creation, out _, out _, out _) ? creation : null;
            string key = $"{pid}:{created}:{name}";
            if (!paths.TryGetValue(key, out string? path) || path == null)
            {
                var buffer = new StringBuilder(32768); uint length = (uint)buffer.Capacity;
                path = Native.QueryFullProcessImageName(handle, 0, buffer, ref length) ? buffer.ToString() : null;
                paths[key] = path;
            }
            return new(pid, parent, name, path, session, created);
        }
        finally { Native.CloseHandle(handle); }
    }
    public IReadOnlyList<WindowMetadata> Windows(IReadOnlyList<RelatedProcess> processes)
    {
        var byPid = processes.ToDictionary(p => p.Identity.Pid);
        var result = new List<WindowMetadata>();
        int visited = 0;
        bool limited = false;
        bool ok = Native.EnumWindows((hwnd, _) =>
        {
            if (++visited > 8192 || result.Count >= 256) { limited = true; return false; }
            if (!Native.IsWindowVisible(hwnd)) return true;
            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            if (!byPid.TryGetValue(pid, out var p)) return true;
            var current = ReadIdentity(pid, p.Identity.ParentPid, p.Identity.Name, p.Identity.WindowsSessionId);
            if (current.CreatedFileTime == null || current.Key != p.Identity.Key) return true;
            var cls = new StringBuilder(256);
            if (Native.GetClassName(hwnd, cls, cls.Capacity) == 0) return true;
            IntPtr owner = Native.GetWindow(hwnd, 4); // GW_OWNER
            Native.GetWindowThreadProcessId(owner, out uint ownerPid);
            result.Add(new(pid, p.Identity.Key, p.Identity.ExecutablePath, hwnd.ToInt64(), cls.ToString(), true,
                Native.IsWindowEnabled(hwnd), owner.ToInt64(), owner == IntPtr.Zero ? null : ownerPid, p.Family, p.RelatedTo));
            return true;
        }, IntPtr.Zero);
        if (!ok || limited) throw new InvalidDataException("Window snapshot incomplete");
        return result;
    }
    public IReadOnlyList<(Family Family, FileMetadata File)> Files(IReadOnlyList<RelatedProcess> processes) => scope.Snapshot(processes);
    public IReadOnlyList<UuStartupWindow> StartupWindows(IReadOnlyList<RelatedProcess> processes)
    {
        var owners = processes.ToDictionary(p => p.Identity.Pid);
        return Windows(processes).Select(w =>
        {
            StartupBounds? bounds = Native.GetWindowRect(new IntPtr(w.Hwnd), out var rect)
                ? new(rect.Left, rect.Top, Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top)) : null;
            return new UuStartupWindow(owners[w.Pid], w, bounds);
        }).ToArray();
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct ProcessEntry
        {
            public uint Size, Usage, Pid;
            public UIntPtr DefaultHeap;
            public uint ModuleId, Threads, ParentPid;
            public int Priority;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
        }
        internal delegate bool EnumCallback(IntPtr hwnd, IntPtr parameter);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);
        [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);
        [DllImport("kernel32.dll")] internal static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);
        [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref uint length);
        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ProcessIdToSessionId(uint pid, out uint session);
        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CloseHandle(IntPtr handle);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool EnumWindows(EnumCallback callback, IntPtr parameter);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowEnabled(IntPtr hwnd);
        [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    }
}
