using System.Diagnostics;
using StormHeroesLauncher.Services;

namespace StormHeroesLauncher.Observer;

public enum BattleNetProbeStrategy { Baseline, Hidden, StartupInfo }
public sealed record ProbeWindow(uint Pid, long Created, long Hwnd, string ClassName,
    bool Visible, bool Minimized, bool Enabled, bool Hung, bool Ownerless);

public static class BattleNetProbePolicy
{
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
    public static bool Observe(HeroesProcessIdentity process, uint session, long launch, string installRoot, string agentRoot) =>
        process.Session == session && process.Created >= launch && AllowedPath(process.Path,installRoot,agentRoot);

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
