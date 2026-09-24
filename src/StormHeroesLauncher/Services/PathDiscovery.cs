using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using StormHeroesLauncher.Configuration;
namespace StormHeroesLauncher.Services;
public sealed class PathDiscovery(AppLogger logger)
{
    public LauncherSettings Discover(LauncherSettings saved)
    {
        var roots = new List<string>();
        var running = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "uu", "Battle.net", "HeroesSwitcher_x64" })
        {
            foreach (uint pid in DesktopProcessState.Find(name))
            {
                var handle = OpenProcess(0x1000, false, pid); // QUERY_LIMITED_INFORMATION, never VM_READ.
                if (handle == IntPtr.Zero) continue;
                try { var text = new StringBuilder(32768); uint size = (uint)text.Capacity;
                    if (QueryFullProcessImageName(handle, 0, text, ref size)) running.TryAdd(name, text.ToString()); }
                finally { CloseHandle(handle); }
            }
        }
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var basis = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = basis.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                foreach (string child in uninstall?.GetSubKeyNames() ?? [])
                {
                    using var item = uninstall!.OpenSubKey(child);
                    string name = item?.GetValue("DisplayName") as string ?? "";
                    if (name.Contains("Battle.net", StringComparison.OrdinalIgnoreCase) || name.Contains("Heroes of the Storm", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("UU", StringComparison.OrdinalIgnoreCase) || name.Contains("风暴英雄"))
                    {
                        string location = item?.GetValue("InstallLocation") as string ?? "";
                        if (Path.IsPathFullyQualified(location)) roots.Add(location);
                    }
                }
                using var app = basis.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\Battle.net.exe");
                if (app?.GetValue("") is string exe && Path.IsPathFullyQualified(exe)) roots.Add(Path.GetDirectoryName(exe)!);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            { logger.Write($"安装目录检测跳过不可读注册信息：{ex.GetType().Name}"); }
        }
        foreach (string basePath in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
        foreach (string child in new[] { "Battle.net", "Heroes of the Storm", @"Netease\UU", "UU" }) roots.Add(Path.Combine(basePath, child));
        string Pick(string savedPath, string? runningPath, params string[] relatives)
        {
            // Never silently replace an explicitly configured broken path; recovery UI must explain it.
            if (!string.IsNullOrWhiteSpace(savedPath)) return savedPath;
            if (!string.IsNullOrEmpty(runningPath) && File.Exists(runningPath)) return runningPath;
            return roots.SelectMany(root => relatives.Select(relative => Path.Combine(root, relative))).FirstOrDefault(File.Exists) ?? "";
        }
        string? uuRoot = running.TryGetValue("uu", out var uu) ? Path.GetDirectoryName(uu) : null;
        var result = saved with {
            UuLauncherPath = Pick(saved.UuLauncherPath, uuRoot == null ? null : Path.Combine(uuRoot, "uu_launcher.exe"), "uu_launcher.exe"),
            UuCliPath = saved.UuCliPath, // Dedicated validated CLI preparation owns this field.
            BattleNetPath = Pick(saved.BattleNetPath, running.GetValueOrDefault("Battle.net"), "Battle.net.exe"),
            HeroesSwitcherPath = Pick(saved.HeroesSwitcherPath, running.GetValueOrDefault("HeroesSwitcher_x64"), @"Support64\HeroesSwitcher_x64.exe") };
        logger.WriteOperation("路径检测（未扫描磁盘、未启动程序）", result);
        return result;
    }
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref uint size);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
