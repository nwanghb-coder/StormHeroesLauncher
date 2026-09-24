using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using StormHeroesLauncher.WindowSupport;
namespace StormHeroesLauncher.Services;
public sealed class UuTrayNative(Action<string>? timingLog = null, bool hideFirst = false) : IUuTrayHost
{
        private DateTimeOffset launchT0;
    private Stopwatch? startup;
    private long detected;
    public void PrepareStartup() { _ = Pids(); startup=Stopwatch.StartNew(); launchT0=DateTimeOffset.UtcNow; timingLog?.Invoke("UU suppression: Armed=True BeforeLaunch=True BudgetMs=5000 FastPollMs=10 SlowPollMs=25"); }
    public void StartupDelay(int milliseconds) => Thread.Sleep(milliseconds);
    public bool Running() => Pids().Count != 0;
    private static HashSet<uint> Pids()
    {
        using var self = Process.GetCurrentProcess();
        var processes = Process.GetProcessesByName("uu"); var result = new HashSet<uint>();
        try { foreach (var p in processes) { try { if (p.SessionId == self.SessionId && !p.HasExited) result.Add((uint)p.Id); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } } }
        finally { foreach (var p in processes) p.Dispose(); }
        return result;
    }
    public bool ValidateLauncher(string path) => Path.IsPathFullyQualified(path) &&
        Path.GetFileName(path).Equals("uu_launcher.exe",StringComparison.OrdinalIgnoreCase) && new WindowsCliValidation().SourceExecutable(path).Valid;
    public static ProcessStartInfo StartInfo(string path) => new(path) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path)! };
    public bool Start(string path)
    {
        using var locked = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if (!ValidateLauncher(path)) throw new InvalidDataException("UU launcher validation failed");
        if (Running()) return false;
        launchT0=DateTimeOffset.UtcNow; startup?.Restart();
        using var process = Process.Start(StartInfo(path)); // Already elevated. No runas, no second elevation request.
        if (process == null) throw new InvalidOperationException("UU start returned no process");
        return true;
    }
    public UuTrayTarget? FindMain()
    {
        var pids = Pids(); var targets = new List<UuTrayTarget>(); bool unavailable = false;
        if (!EnumWindows((hwnd,_) => {
            GetWindowThreadProcessId(hwnd,out uint pid);
            if (pids.Contains(pid) && IsWindowVisible(hwnd)) { var target = Read(hwnd); if (target != null) targets.Add(target); else { var cls = new StringBuilder(256); GetClassName(hwnd,cls,cls.Capacity); if (cls.ToString() == "UUMAINFORMV40") unavailable = true; } }
            return true;
        },IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        if (unavailable) throw new InvalidDataException("UU window identity unavailable");
        if (targets.Count > 1) throw new InvalidDataException("Multiple UU main windows; no action taken");
        var found=targets.SingleOrDefault(); if(found != null) detected=startup?.ElapsedMilliseconds ?? 0; return found;
    }
    public static UuTrayTarget? Read(IntPtr hwnd)
    {
        if (!IsWindow(hwnd) || GetAncestor(hwnd,2) != hwnd || GetWindow(hwnd,4) != IntPtr.Zero) return null;
        var cls = new StringBuilder(256); GetClassName(hwnd,cls,cls.Capacity);
        if (cls.ToString() != "UUMAINFORMV40") return null;
        GetWindowThreadProcessId(hwnd,out uint pid);
        if (!ProcessIdToSessionId(pid,out uint session) || !ProcessIdToSessionId((uint)Environment.ProcessId,out uint own) || session != own) return null;
        IntPtr process = OpenProcess(0x1000,false,pid);
        if (process == IntPtr.Zero) return null;
        try
        {
            var path = new StringBuilder(32768); uint size = (uint)path.Capacity;
            if (!QueryFullProcessImageName(process,0,path,ref size) || !Path.GetFileName(path.ToString()).Equals("uu.exe",StringComparison.OrdinalIgnoreCase) ||
                !GetProcessTimes(process,out long created,out long exited,out _,out _) || exited != 0) return null;
            return new(hwnd.ToInt64(),pid,created);
        }
        finally { CloseHandle(process); }
    }
    public UuTrayReport Close(UuTrayTarget target)
    {
        var hwnd = new IntPtr(target.Hwnd); var now = Read(hwnd);
        bool Valid() => now != null && now.Pid == target.Pid && now.Created == target.Created && IsWindowVisible(hwnd);
        UuTrayReport Report(string posted,int code,bool? visible,bool running) => new(false,false,target.Pid,target.Hwnd,"UUMAINFORMV40",posted,visible,running,code);
        if (!Valid()) return Report("NotPosted",22,null,Running());
        // Revalidate immediately before the only message. No retries with another target.
        now = Read(hwnd);
        if (!Valid()) return Report("NotPosted",22,null,Running());
        long requested=startup?.ElapsedMilliseconds ?? 0;
                HideTrayAttempt? attempt=null;
        bool posted;
        if(hideFirst)
        {
            attempt=HideTrayAttempt.Run($"UU: PID={target.Pid} HWND=0x{target.Hwnd:X} Class=UUMAINFORMV40",startup != null ? launchT0.AddMilliseconds(detected) : DateTimeOffset.UtcNow,
                ()=>Read(hwnd)==target && IsWindowVisible(hwnd),()=>ShowWindowAsync(hwnd,0),()=>Read(hwnd)==target,
                ()=>PostMessage(hwnd,0x10,IntPtr.Zero,IntPtr.Zero),message=>timingLog?.Invoke(message));
            posted=attempt.Posted;
        }
        else posted=PostMessage(hwnd,0x10,IntPtr.Zero,IntPtr.Zero);
        if(!posted) { attempt?.Observe(IsWindowVisible(hwnd),SameProcessAlive(target)); return Report("Failed",23,IsWindowVisible(hwnd),SameProcessAlive(target)); }
        for (int i = 0; i < 15; i++) Thread.Sleep(200);
        bool alive = SameProcessAlive(target);
        bool? visible = !IsWindow(hwnd) ? false : Read(hwnd) == target ? IsWindowVisible(hwnd) : null;
        if(visible.HasValue) attempt?.Observe(visible.Value,alive);
                if(startup != null)
        {
            long confirmed=startup.ElapsedMilliseconds;
            timingLog?.Invoke($"UU: PID={target.Pid} HWND=0x{target.Hwnd:X} Class=UUMAINFORMV40 ProcessLaunchT0={launchT0:O} WindowFirstDetected={launchT0.AddMilliseconds(detected):O} ActionRequested={launchT0.AddMilliseconds(requested):O} ActionConfirmed={(alive && visible == false ? launchT0.AddMilliseconds(confirmed).ToString("O") : "Unknown")} DetectionLatencyMs={detected} ActionLatencyMs={requested-detected} EstimatedVisibleExposureMs={(alive && visible == false ? (confirmed-detected).ToString() : "Unknown")} ExposureBasis=FirstDetectedToObservedHidden_UpperBound Action=WM_CLOSE");
        }
        return Report("Posted",alive && visible == false ? 0 : 24,visible,alive);
    }
    private static bool SameProcessAlive(UuTrayTarget target)
    {
        IntPtr process = OpenProcess(0x1000,false,target.Pid); if (process == IntPtr.Zero) return false;
        try { return GetProcessTimes(process,out long created,out long exited,out _,out _) && created == target.Created && exited == 0 && GetExitCodeProcess(process,out uint code) && code == 259; }
        finally { CloseHandle(process); }
    }
    public void Delay() => Thread.Sleep(250);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr hwnd,int command);
    private delegate bool Callback(IntPtr hwnd,IntPtr data);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool EnumWindows(Callback callback,IntPtr data);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd,StringBuilder text,int count);
    [DllImport("user32.dll",EntryPoint="PostMessageW",SetLastError=true)] private static extern bool PostMessage(IntPtr hwnd,uint msg,IntPtr w,IntPtr l);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access,bool inherit,uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern bool ProcessIdToSessionId(uint pid,out uint session);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr process,uint flags,StringBuilder text,ref uint count);
    [DllImport("kernel32.dll")] private static extern bool GetProcessTimes(IntPtr process,out long created,out long exited,out long kernel,out long user);
    [DllImport("kernel32.dll")] private static extern bool GetExitCodeProcess(IntPtr process,out uint code);
}
