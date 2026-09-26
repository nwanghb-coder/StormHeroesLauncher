using StormHeroesLauncher.Models;
using StormHeroesLauncher.Services;

public static class HeroesReadinessTests
{
    public static async Task Run(AppLogger logger, Action<bool, string> check)
    {
        var game = new HeroesProcessIdentity(20, 10, 200, 1, @"C:\Game\Versions\Base1\HeroesOfTheStorm_x64.exe");
        var parent = new HeroesProcessIdentity(10, 0, 100, 1, @"C:\Game\Support64\HeroesSwitcher_x64.exe");
        var main = new HeroesWindowIdentity(30, game, "Heroes of the Storm", true, true, true, false, 0x97080000, 0x40000, 2560, 1440);
        var prep = main with { Hwnd = 40, ClassName = "#32770", Width = 489, Height = 223 };
        var rule = new HeroesUiReadiness();
        check(!rule.Sample([], 0, _ => true, _ => true), "process existence alone is not UI readiness");
        check(!rule.Sample([main], 100, _ => false, _ => false) && !rule.Sample([main], 1599, _ => false, _ => false) &&
            rule.Sample([main], 1600, _ => false, _ => false), "main UI must remain stable for 1500ms; no prep dialog is required");
        check(!rule.Sample([main, prep], 1700, _ => false, _ => false), "visible prep resets readiness");
        int hides = 0;
        for (int i = 0; i < 8; i++) rule.Sample([prep], 1800 + i, _ => true, _ => { hides++; throw new InvalidOperationException(); });
        check(hides == 3, "hide failure is nonfatal and retry count is bounded");
        check(!rule.Sample([main, prep with { Visible = false }], 2000, _ => true, _ => false) &&
            rule.Sample([main, prep with { Visible = false }], 3500, _ => true, _ => false), "hidden prep permits stable main UI");
        check(!rule.Sample([main with { Process = game with { Created = 201 } }], 3600, _ => true, _ => true), "PID reuse resets stable window identity");
        check(!HeroesUiReadiness.IsMain(main with { ClassName = "Other" }) && !HeroesUiReadiness.IsMain(main with { Hung = true }) &&
            !HeroesUiReadiness.IsMain(main with { ExStyle = 0x80 }) && !HeroesUiReadiness.IsMain(main with { Ownerless = false }), "unknown, hung, tool and owned windows cannot imply readiness");
        check(HeroesUiReadiness.IsDirectChild(game, parent, 250, 90, new HashSet<uint>()) &&
            !HeroesUiReadiness.IsDirectChild(game, parent, 250, 90, new HashSet<uint> { 20 }) &&
            !HeroesUiReadiness.IsDirectChild(game, parent, 150, 90, new HashSet<uint>()) &&
            !HeroesUiReadiness.IsDirectChild(game with { ParentPid = 99 }, parent, 250, 90, new HashSet<uint>()) &&
            !HeroesUiReadiness.IsDirectChild(game with { Session = 2 }, parent, 250, 90, new HashSet<uint>()), "ownership rejects preexisting, parent PID reuse, wrong parent and other sessions");
        int forbidden = 0;
        new HeroesUiReadiness().Sample([prep], 0, _ => false, _ => { forbidden++; return true; });
        check(forbidden == 0, "preexisting game dialog is never hidden");

        var state = new LaunchProgress(); var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workflow = new HeroesLaunchWorkflow(_ => Task.FromResult(true), _ => throw new Exception(), _ => throw new Exception(),
            _ => throw new Exception(), () => throw new Exception(), logger, state, _ => ready.Task);
        Task run = workflow.RunAsync(default);
        check(!run.IsCompleted && state.Current.State == LaunchState.PreparingHeroes, "already-running game remains PreparingHeroes until actual readiness");
        ready.SetResult(); await run;
        check(state.Current.State == LaunchState.GameReady, "readiness completion advances workflow to GameReady");
    }
}
