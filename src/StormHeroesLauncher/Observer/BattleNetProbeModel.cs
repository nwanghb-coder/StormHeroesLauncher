using System.Diagnostics;
using StormHeroesLauncher.Services;

namespace StormHeroesLauncher.Observer;

public enum BattleNetProbeStrategy { Baseline, Hidden, StartupInfo }
public sealed record ProbeWindow(uint Pid, long Created, long Hwnd, string ClassName,
    bool Visible, bool Minimized, bool Enabled, bool Hung, bool Ownerless);

public static class BattleNetProbePolicy
{
    public static IEnumerable<HeroesProcessIdentity> CleanupOrder(IEnumerable<HeroesProcessIdentity> known, uint rootPid) =>
        known.OrderBy(p => p.Pid == rootPid ? 0 : 1).ThenByDescending(p => p.Created);
    public static ProcessStartInfo StartInfo(string path, BattleNetProbeStrategy strategy) => new(path)
    {
        UseShellExecute = false, WorkingDirectory = System.IO.Path.GetDirectoryName(path)!,
        WindowStyle = strategy == BattleNetProbeStrategy.Hidden ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal
    };
    public static bool RelatedName(string name) => name.Equals("Battle.net.exe", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Battle.net Launcher.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("Agent.exe", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("BlizzardError.exe", StringComparison.OrdinalIgnoreCase);
    public static bool AllowedPath(string path, string installRoot, string agentRoot) =>
        RelatedName(System.IO.Path.GetFileName(path)) &&
        (path.StartsWith(installRoot.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) ||
         System.IO.Path.GetFileName(path).Equals("Agent.exe", StringComparison.OrdinalIgnoreCase) &&
         path.StartsWith(agentRoot.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));
    public static bool Owns(HeroesProcessIdentity child, HeroesProcessIdentity parent, long parentExit,
        long launch, IReadOnlySet<uint> before, string installRoot, string agentRoot) =>
        child.ParentPid == parent.Pid && child.Session == parent.Session && child.Created >= launch &&
        child.Created >= parent.Created && (parentExit == 0 || child.Created <= parentExit) &&
        !before.Contains(child.Pid) && AllowedPath(child.Path, installRoot, agentRoot);
}

// Sample-held union durations: concurrent windows of one class do not multiply exposure.
public sealed class BattleNetProbeMeasurements
{
    public double? FirstQtVisibleMs { get; private set; }
    public double? FirstChromeVisibleMs { get; private set; }
    public double? FirstHiddenOrMinimizedMs { get; private set; }
    public double QtVisibleDurationMs { get; private set; }
    public double ChromeVisibleDurationMs { get; private set; }
    public double MaximumSampleGapMs { get; private set; }
    public bool QtRightCensored { get; private set; }
    public bool ChromeRightCensored { get; private set; }
    public bool StableChromiumProxy { get; private set; }
    private double last, stableSince;
    private bool haveSample;
    private string? stableIdentity;
    public void Sample(double now, IReadOnlyList<ProbeWindow> windows)
    {
        if (haveSample)
        {
            double delta = Math.Max(0, now - last);
            MaximumSampleGapMs = Math.Max(MaximumSampleGapMs, delta);
            if (QtRightCensored) QtVisibleDurationMs += delta;
            if (ChromeRightCensored) ChromeVisibleDurationMs += delta;
        }
        haveSample = true; last = now;
        var relevant = windows.Where(w => w.ClassName == "Qt5151QWindowIcon" || w.ClassName.StartsWith("Chrome_WidgetWin_", StringComparison.Ordinal)).ToArray();
        QtRightCensored = relevant.Any(w => w.ClassName == "Qt5151QWindowIcon" && w.Visible && !w.Minimized);
        ChromeRightCensored = relevant.Any(w => w.ClassName.StartsWith("Chrome_WidgetWin_", StringComparison.Ordinal) && w.Visible && !w.Minimized);
        if (QtRightCensored) FirstQtVisibleMs ??= now;
        if (ChromeRightCensored) FirstChromeVisibleMs ??= now;
        if (relevant.Any(w => !w.Visible || w.Minimized)) FirstHiddenOrMinimizedMs ??= now;
        var chrome = relevant.FirstOrDefault(w => w.ClassName.StartsWith("Chrome_WidgetWin_", StringComparison.Ordinal) && w.Ownerless && w.Enabled && !w.Hung);
        string? identity = chrome == null ? null : $"{chrome.Pid}:{chrome.Created}:{chrome.Hwnd}";
        if (identity != stableIdentity) { stableIdentity = identity; stableSince = now; }
        if (identity != null && now - stableSince >= 1500) StableChromiumProxy = true;
    }
}
