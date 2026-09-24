using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
namespace StormHeroesLauncher.Services;
public sealed record HeroesPrepWindow(IntPtr Hwnd,uint Pid,long Created,string ProcessName,string ClassName,bool Visible,bool TopLevel,IntPtr Owner,bool WorkflowOwned);
public static class HeroesPreparationWindow
{
    public static bool Matches(HeroesPrepWindow w) => w.WorkflowOwned && w.ProcessName.Equals("HeroesOfTheStorm_x64.exe",StringComparison.OrdinalIgnoreCase) && w.Visible && w.TopLevel && w.Owner == IntPtr.Zero && w.ClassName == "#32770";
    public static async Task ObserveCore(AppLogger log,Func<IReadOnlyList<HeroesPrepWindow>> snapshot,Func<HeroesPrepWindow,bool> hide,Func<CancellationToken,Task> delay,CancellationToken token)
    {
        var attempted=new HashSet<(IntPtr,uint,long)>();
        try
        {
            for(int sample=0;sample<50;sample++)
            {
                token.ThrowIfCancellationRequested();
                foreach(var w in snapshot().Where(Matches))
                {
                    var key=(w.Hwnd,w.Pid,w.Created);
                    if(attempted.Count>=2 || attempted.Contains(key)) continue;
                    if(!snapshot().Any(f => f == w && Matches(f))) continue;
                    attempted.Add(key);
                    bool accepted=hide(w);
                    bool visible=snapshot().Any(f=>f.Hwnd==w.Hwnd && f.Pid==w.Pid && f.Created==w.Created && f.Visible);
                    log.Write($"Heroes preparation window{(attempted.Count==2 ? " replacement" : "")}: PID={w.Pid} HWND=0x{w.Hwnd.ToInt64():X} Class=#32770 Action=Hide Result={(accepted ? "Requested" : "RejectedOrChanged")} VisibleAfter={visible}");
                }
                await delay(token);
            }
            foreach(var w in snapshot().Where(w=>attempted.Contains((w.Hwnd,w.Pid,w.Created))))
                log.Write($"Heroes preparation window: PID={w.Pid} HWND=0x{w.Hwnd.ToInt64():X} Class=#32770 Action=Verify VisibleAfter={w.Visible}");
        }
        catch(OperationCanceledException) { throw; }
        catch(Exception ex) { log.Write($"Heroes preparation window: Action=Hide Result=Unavailable Exception={ex.GetType().Name}; launch continues"); }
    }
    public static Task ObserveAsync(AppLogger log,int switcherPid,DateTime started,string switcherPath,CancellationToken token)
    {
        try
        {
            var native=new PrepNative((uint)switcherPid,started.ToFileTimeUtc(),switcherPath);
            return ObserveCore(log,native.Snapshot,native.Hide,t=>Task.Delay(100,t),token);
        }
        catch(Exception ex) { log.Write($"Heroes preparation window: Result=Unavailable Exception={ex.GetType().Name}; launch continues"); return Task.CompletedTask; }
    }
}
internal sealed class PrepNative(uint parent,long earliest,string switcherPath)
{
    private readonly string versions=System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(switcherPath)!,"..","Versions"))+System.IO.Path.DirectorySeparatorChar;
    public IReadOnlyList<HeroesPrepWindow> Snapshot()
    {
        var ids=new HashSet<uint>();
        IntPtr snap=CreateToolhelp32Snapshot(2,0);
        if(snap==new IntPtr(-1)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var entry=new Entry {Size=(uint)Marshal.SizeOf<Entry>()};
            if(Process32First(snap,ref entry)) do
            {
                if(entry.Parent==parent && entry.Exe.Equals("HeroesOfTheStorm_x64.exe",StringComparison.OrdinalIgnoreCase)) ids.Add(entry.Pid);
                entry.Size=(uint)Marshal.SizeOf<Entry>();
            } while(Process32Next(snap,ref entry));
        }
        finally {CloseHandle(snap);}
        var trusted=new Dictionary<uint,long>();
        foreach(uint pid in ids)
        {
            if(!ProcessIdToSessionId(pid,out uint session) || !ProcessIdToSessionId((uint)Environment.ProcessId,out uint own) || session!=own) continue;
            IntPtr p=OpenProcess(0x1000,false,pid); if(p==IntPtr.Zero) continue;
            try
            {
                var path=new StringBuilder(32768); uint size=(uint)path.Capacity;
                if(!GetProcessTimes(p,out long created,out long exited,out _,out _) || created<earliest || exited!=0 || !QueryFullProcessImageName(p,0,path,ref size)) continue;
                string full=System.IO.Path.GetFullPath(path.ToString());
                if(full.StartsWith(versions,StringComparison.OrdinalIgnoreCase) && System.IO.Path.GetFileName(full).Equals("HeroesOfTheStorm_x64.exe",StringComparison.OrdinalIgnoreCase)) trusted[pid]=created;
            }
            finally {CloseHandle(p);}
        }
        var result=new List<HeroesPrepWindow>();
        EnumWindows((hwnd,_)=>{
            GetWindowThreadProcessId(hwnd,out uint pid);
            if(trusted.TryGetValue(pid,out long created))
            {
                var cls=new StringBuilder(256); GetClassName(hwnd,cls,cls.Capacity);
                result.Add(new(hwnd,pid,created,"HeroesOfTheStorm_x64.exe",cls.ToString(),IsWindowVisible(hwnd),GetAncestor(hwnd,2)==hwnd,GetWindow(hwnd,4),true));
            }
            return true;
        },IntPtr.Zero);
        return result;
    }
    public bool Hide(HeroesPrepWindow target)
    {
        // Query metadata and HWND identity again immediately before the only permitted visibility operation.
        if(!Snapshot().Any(w=>w==target && HeroesPreparationWindow.Matches(w))) return false;
        return ShowWindowAsync(target.Hwnd,0);
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct Entry
    {
        public uint Size,Usage,Pid; public UIntPtr Heap; public uint Module,Threads,Parent; public int Priority; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] public string Exe;
    }
    private delegate bool Callback(IntPtr hwnd,IntPtr data);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags,uint pid);
    [DllImport("kernel32.dll",EntryPoint="Process32FirstW",CharSet=CharSet.Unicode)] private static extern bool Process32First(IntPtr snap,ref Entry entry);
    [DllImport("kernel32.dll",EntryPoint="Process32NextW",CharSet=CharSet.Unicode)] private static extern bool Process32Next(IntPtr snap,ref Entry entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access,bool inherit,uint pid);
    [DllImport("kernel32.dll")] private static extern bool ProcessIdToSessionId(uint pid,out uint session);
    [DllImport("kernel32.dll")] private static extern bool GetProcessTimes(IntPtr p,out long created,out long exited,out long kernel,out long user);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr p,uint flags,StringBuilder path,ref uint size);
    [DllImport("user32.dll")] private static extern bool EnumWindows(Callback callback,IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd,StringBuilder cls,int count);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd,uint flag);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd,uint flag);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr hwnd,int command);
}
