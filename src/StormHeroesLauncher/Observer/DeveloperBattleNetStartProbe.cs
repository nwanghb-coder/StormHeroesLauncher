using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using StormHeroesLauncher.Services;

namespace StormHeroesLauncher.Observer;

// Explicit developer route only. No production launch services, helper, configuration writes or UI automation.
public static class DeveloperBattleNetStartProbe
{
    public const int ObservationMilliseconds = 30000;
    public const string CompletionMessage = "Observation complete.";
    public const string RunningMessage = "Please exit Battle.net from the system tray and wait for Agent to stop before testing again.";
    public static Task<int> RunAsync(string[] args) => Task.Factory.StartNew(() =>
    {
        // Keep the existing workflow mutex on this dedicated thread for the full research run.
        // This prevents a simultaneous normal HOSLauncher workflow from launching Battle.net mid-test.
        using var gate = new Mutex(false,@"Local\StormHeroesLauncher.Alpha.LaunchWorkflow");
        bool acquired;
        try { acquired = gate.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) { Console.WriteLine("Please exit the current HOSLauncher workflow before testing."); return 2; }
        try { return RunCoreAsync(args).GetAwaiter().GetResult(); }
        finally { gate.ReleaseMutex(); }
    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    private static async Task<int> RunCoreAsync(string[] args)
    {
        if (args.Length != 4 || !Enum.TryParse<BattleNetProbeStrategy>(args[1], true, out var strategy) ||
            !Enum.IsDefined(strategy) || !Path.IsPathFullyQualified(args[2]) || !Path.IsPathFullyQualified(args[3]) ||
            !Path.GetFileName(args[2]).Equals("Battle.net.exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(args[2])) return 2;
        string path = Path.GetFullPath(args[2]), outputPath = Path.GetFullPath(args[3]);
        SafeCliPaths.NoReparse(path); SafeCliPaths.NoReparse(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        SafeCliPaths.NoReparse(outputPath);
        using var output = new StreamWriter(new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
        var clock = Stopwatch.StartNew();
        void Log(string name, object data) => output.WriteLine(JsonSerializer.Serialize(new { schemaVersion = 1, timestamp = DateTimeOffset.UtcNow, elapsedMs = clock.Elapsed.TotalMilliseconds, @event = name, data }));
        string installRoot = Path.GetDirectoryName(path)!;
        string agentRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Battle.net", "Agent");
        var before = Native.Processes();
        if (before.Any(p => BattleNetProbePolicy.RelatedName(p.Name) && Native.RunningState(p.Pid) != false))
        { Log("Refused", new { reason = "BattleNetOrAgentNotFullyClosed" }); Console.WriteLine(RunningMessage); return 2; }
        Log("ProbePrepared", new { strategy = strategy.ToString(), executableVersion = FileVersionInfo.GetVersionInfo(path).FileVersion,
            observationMs = ObservationMilliseconds, postLaunchHide = false, sampling = "10ms for 5s then 50ms; request/sample evidence only", nativeTrayFunctional = "NotProvenByMetadata" });
        var observed = new Dictionary<uint, HeroesProcessIdentity>();
        var unreadable = new HashSet<uint>();
        uint session = (uint)Process.GetCurrentProcess().SessionId;
        var priorWindows = new Dictionary<(uint,long,long), ProbeWindow>();
        var measurement = new BattleNetProbeMeasurements();
        bool failed = false;
        DateTimeOffset launchT0 = DateTimeOffset.UtcNow;
        long launchFileTime = launchT0.UtcDateTime.ToFileTimeUtc();
        double launchMs = clock.Elapsed.TotalMilliseconds, launchReturnedMs = 0, firstSampleMs = 0;
        Process? managedRoot = null; IntPtr nativeRoot = IntPtr.Zero;
        uint rootPid = 0;
        try
        {
            IntPtr rootHandle;
            if (strategy == BattleNetProbeStrategy.StartupInfo)
            {
                var startup = Native.HiddenStartupInfo();
                // Explicit application path; quoted mutable command line; no shell, credentials, elevation or inherited handles.
                if (!Native.CreateProcess(path, new StringBuilder('"' + path + '"'), IntPtr.Zero, IntPtr.Zero, false, 0,
                    IntPtr.Zero, installRoot, ref startup, out var process)) throw new Win32Exception();
                Native.CloseHandle(process.Thread); nativeRoot = rootHandle = process.Process; rootPid = process.Pid;
            }
            else
            {
                managedRoot = Process.Start(BattleNetProbePolicy.StartInfo(path, strategy)) ?? throw new InvalidOperationException("No process returned");
                rootHandle = managedRoot.Handle; rootPid = (uint)managedRoot.Id;
            }
            launchReturnedMs = clock.Elapsed.TotalMilliseconds - launchMs;
            var root = Native.Identity(rootHandle, rootPid, (uint)Environment.ProcessId);
            if (root == null || !root.Path.Equals(path, StringComparison.OrdinalIgnoreCase) || root.Created < launchFileTime || root.Session != (uint)Process.GetCurrentProcess().SessionId)
                throw new InvalidOperationException("Root metadata unavailable");
            observed.Add(root.Pid, root);
            Log("ProcessStarted", new { Strategy = strategy.ToString(), ProcessLaunchT0 = launchT0, LaunchReturnedMs = launchReturnedMs,
                root.Pid, root.Created, root.Session, root.Path, startTimeUtc = DateTime.FromFileTimeUtc(root.Created) });
            bool first = true;
            while (clock.Elapsed.TotalMilliseconds - launchMs < ObservationMilliseconds)
            {
                Discover();
                var windows = ReadWindows();
                double ms = clock.Elapsed.TotalMilliseconds - launchMs;
                if (first) { firstSampleMs = ms; first = false; }
                measurement.Sample(ms, windows);
                var current = windows.ToDictionary(w => (w.Pid,w.Created,w.Hwnd));
                foreach (var pair in current)
                    if (!priorWindows.TryGetValue(pair.Key, out var previous) || previous != pair.Value) Log("WindowSample", new { fromLaunchMs = ms, window = pair.Value });
                foreach (var missing in priorWindows.Keys.Except(current.Keys)) Log("WindowAbsent", new { fromLaunchMs = ms, missing.Item1, missing.Item2, missing.Item3 });
                priorWindows = current;
                await Task.Delay(ms < 5000 ? 10 : 50).ConfigureAwait(false);
            }
            Discover();
            measurement.Sample(clock.Elapsed.TotalMilliseconds - launchMs, ReadWindows());
            bool alive = observed.Values.Any(p => Path.GetFileName(p.Path).Equals("Battle.net.exe", StringComparison.OrdinalIgnoreCase) && HeroesWindowProbe.Same(p,HeroesWindowProbe.ReadProcess(p.Pid)));
            DateTimeOffset? Stamp(double? ms) => ms.HasValue ? launchT0.AddMilliseconds(ms.Value) : null;
            Log("MeasurementSummary", new { Strategy = strategy.ToString(), ProcessLaunchT0 = launchT0,
                FirstQtVisibleT = Stamp(measurement.FirstQtVisibleMs), FirstChromeVisibleT = Stamp(measurement.FirstChromeVisibleMs),
                FirstHiddenOrMinimizedT = Stamp(measurement.FirstHiddenOrMinimizedMs),
                measurement.FirstQtVisibleMs, measurement.FirstChromeVisibleMs, measurement.FirstHiddenOrMinimizedMs,
                measurement.QtVisibleDurationMs, measurement.ChromeVisibleDurationMs, measurement.QtRightCensored, measurement.ChromeRightCensored,
                measurement.MaximumSampleGapMs, ProcessStillRunning = alive, measurement.StableChromiumProxy, MetadataComplete = unreadable.Count == 0,
                LoginEvidence = "Stable nonhung enabled ownerless Chromium is a bootstrap proxy; authenticated login not inspected",
                LaunchReturnedMs = launchReturnedMs, FirstSampleMs = firstSampleMs, DurationBasis = "SampleHeldUnion_NotPhysicalFrames; initial launch-to-first-sample gap unobserved" });
        }
        catch (Exception ex) { failed = true; Log("Failure", new { errorType = ex.GetType().Name }); }
        finally
        {
            // Release only our inspection handles. Never close windows, terminate processes or change state.
            managedRoot?.Dispose(); if (nativeRoot != IntPtr.Zero) Native.CloseHandle(nativeRoot);
            Log("ProbeFinished", new { failed, automaticCleanup = false, BattleNetLeftRunning = true });
        }
        Console.WriteLine(failed ? "Observation failed. Battle.net was left untouched; inspect the report." : CompletionMessage);
        return failed ? 4 : 0;

        void Discover()
        {
            observed.Clear();
            foreach (var candidate in Native.Processes().Where(p => BattleNetProbePolicy.RelatedName(p.Name)))
            {
                var identity = HeroesWindowProbe.ReadProcess(candidate.Pid,candidate.Parent);
                if (identity == null)
                {
                    if (Native.RunningState(candidate.Pid) != false && unreadable.Add(candidate.Pid))
                        Log("MetadataUnavailable",new { candidate.Pid,candidate.Name });
                    continue;
                }
                if (BattleNetProbePolicy.Observe(identity,session,launchFileTime,installRoot,agentRoot))
                    observed.Add(identity.Pid,identity);
                if (observed.Count >= 128) throw new InvalidDataException("Observation process limit");
            }
        }
        IReadOnlyList<ProbeWindow> ReadWindows() => HeroesWindowProbe.Windows(observed.Values.ToArray())
            .Where(w => w.ClassName == "Qt5151QWindowIcon" || w.ClassName.StartsWith("Chrome_WidgetWin_", StringComparison.Ordinal))
            .Select(w => new ProbeWindow(w.Process.Pid,w.Process.Created,w.Hwnd,w.ClassName,w.Visible,
                (w.Style & 0x20000000) != 0,w.Enabled,w.Hung,w.Ownerless)).ToArray();
    }

    private static class Native
    {
        internal sealed record SnapshotProcess(uint Pid, uint Parent, string Name);
        internal static IReadOnlyList<SnapshotProcess> Processes()
        {
            var result = new List<SnapshotProcess>();
            IntPtr snapshot = CreateToolhelp32Snapshot(2,0);
            if (snapshot == new IntPtr(-1)) throw new Win32Exception();
            try
            {
                var entry = new Entry { Size = (uint)Marshal.SizeOf<Entry>() };
                if (!Process32First(snapshot, ref entry)) throw new Win32Exception();
                do { if (result.Count >= 8192) throw new InvalidDataException("Process bound"); result.Add(new(entry.Pid,entry.Parent,entry.Exe)); }
                while (Process32Next(snapshot, ref entry));
                if (Marshal.GetLastWin32Error() != 18) throw new Win32Exception();
            }
            finally { CloseHandle(snapshot); }
            return result;
        }
        internal static HeroesProcessIdentity? Identity(IntPtr handle, uint pid, uint parent)
        {
            var path = new StringBuilder(32768); uint length = (uint)path.Capacity;
            return HeroesWindowProbe.GetProcessTimes(handle,out long created,out _,out _,out _) &&
                QueryFullProcessImageName(handle,0,path,ref length) && ProcessIdToSessionId(pid,out uint session)
                ? new(pid,parent,created,session,path.ToString()) : null;
        }
        internal static bool? RunningState(uint pid)
        {
            IntPtr handle = OpenProcess(0x1000 | 0x100000,false,pid);
            if (handle == IntPtr.Zero) return Marshal.GetLastWin32Error() == 87 ? false : null;
            try { return WaitForSingleObject(handle,0) switch { 0 => false, 258 => true, _ => null }; }
            finally { CloseHandle(handle); }
        }
        internal static StartupInfo HiddenStartupInfo() => new() { Size = (uint)Marshal.SizeOf<StartupInfo>(), Flags = 1, ShowWindow = 0 };
        [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] internal struct StartupInfo
        { public uint Size; public IntPtr Reserved,Desktop,Title; public uint X,Y,XSize,YSize,XCount,YCount,Fill,Flags; public ushort ShowWindow,ReservedSize; public IntPtr ReservedBytes,Input,Output,Error; }
        [StructLayout(LayoutKind.Sequential)] internal struct ProcessInfo { public IntPtr Process,Thread; public uint Pid,Tid; }
        [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] private struct Entry
        { public uint Size,Usage,Pid; public UIntPtr Heap; public uint Module,Threads,Parent; public int Priority; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] public string Exe; }
        [DllImport("kernel32.dll",EntryPoint="CreateProcessW",CharSet=CharSet.Unicode,SetLastError=true)] internal static extern bool CreateProcess(string application,StringBuilder command,IntPtr processSecurity,IntPtr threadSecurity,bool inherit,uint flags,IntPtr environment,string directory,ref StartupInfo startup,out ProcessInfo process);
        [DllImport("kernel32.dll",SetLastError=true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags,uint pid);
        [DllImport("kernel32.dll",EntryPoint="Process32FirstW",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool Process32First(IntPtr snapshot,ref Entry entry);
        [DllImport("kernel32.dll",EntryPoint="Process32NextW",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool Process32Next(IntPtr snapshot,ref Entry entry);
        [DllImport("kernel32.dll",SetLastError=true)] internal static extern IntPtr OpenProcess(uint access,bool inherit,uint pid);
        [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")] private static extern bool ProcessIdToSessionId(uint pid,out uint session);
        [DllImport("kernel32.dll",EntryPoint="QueryFullProcessImageNameW",CharSet=CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr handle,uint flags,StringBuilder path,ref uint length);
        [DllImport("kernel32.dll")] internal static extern uint WaitForSingleObject(IntPtr handle,uint ms);
    }
}
