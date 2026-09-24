using System.ComponentModel;
using System.Diagnostics;
namespace StormHeroesLauncher.Services;
internal static class DesktopProcessState
{
    public static HashSet<uint> Find(string name)
    {
        using var self = Process.GetCurrentProcess();
        int session = self.SessionId;
        var processes = Process.GetProcessesByName(name);
        try
        {
            var ids = new HashSet<uint>();
            foreach (var process in processes)
            {
                try { if (process.SessionId == session && !process.HasExited) ids.Add((uint)process.Id); }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
            }
            return ids;
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }
}
