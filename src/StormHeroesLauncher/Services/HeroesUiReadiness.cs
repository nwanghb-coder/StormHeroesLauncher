namespace StormHeroesLauncher.Services;

// Window class (not title) observed in direct Switcher tests. No version-directory dependency.
public sealed class HeroesUiReadiness
{
    public const int StableMilliseconds = 1500;
    private HeroesWindowIdentity? candidate;
    private long since;
    private readonly Dictionary<(uint, long, long), int> attempts = new();
    public static bool IsMain(HeroesWindowIdentity w) => w.ClassName == "Heroes of the Storm" &&
        w.Visible && w.Enabled && w.Ownerless && !w.Hung && w.Width >= 640 && w.Height >= 360 &&
        (w.Style & 0x40000000) == 0 && (w.ExStyle & 0x80) == 0; // Neither child nor tool window.

    public bool Sample(IReadOnlyList<HeroesWindowIdentity> windows, long milliseconds,
        Func<HeroesProcessIdentity, bool> mayHide, Func<HeroesWindowIdentity, bool> hide)
    {
        foreach (var w in windows.Where(HeroesWindowProbe.IsPreparation))
        {
            var key = (w.Process.Pid, w.Process.Created, w.Hwnd);
            if (!mayHide(w.Process) || attempts.GetValueOrDefault(key) >= 3 || attempts.Count >= 128) continue;
            attempts[key] = attempts.GetValueOrDefault(key) + 1;
            try { hide(w); } catch { /* Visibility changes are best effort, never a launch failure. */ }
        }
        var main = windows.FirstOrDefault(w => IsMain(w) && !windows.Any(p =>
            HeroesWindowProbe.Same(w.Process, p.Process) && HeroesWindowProbe.IsPreparation(p)));
        if (main == null) { candidate = null; return false; }
        if (candidate == null || candidate.Hwnd != main.Hwnd || !HeroesWindowProbe.Same(candidate.Process, main.Process))
        { candidate = main; since = milliseconds; return false; }
        return milliseconds - since >= StableMilliseconds;
    }
    public static bool IsDirectChild(HeroesProcessIdentity game, HeroesProcessIdentity parent,
        long parentExit, long start, IReadOnlySet<uint> before) => !before.Contains(game.Pid) &&
        game.ParentPid == parent.Pid && game.Created >= Math.Max(start, parent.Created) &&
        (parentExit == 0 || game.Created <= parentExit) && game.Session == parent.Session;
}
