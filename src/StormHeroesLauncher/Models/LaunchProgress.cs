namespace StormHeroesLauncher.Models;

public enum LaunchState
{
    Initializing, StartingUU, PreparingUU, Boosting, StartingBattleNet,
    WaitingForBattleNet, StartingHeroes, PreparingHeroes, GameReady, Failed, Cancelled
}

public sealed record LaunchProgressSnapshot(LaunchState State, int Percentage);

// Semantic state only. No WPF, process identities, diagnostic payloads or observer dependencies.
public sealed class LaunchProgress
{
    private readonly object gate = new();
    private readonly Action<string>? log;
    private LaunchProgressSnapshot current = new(LaunchState.Initializing, 5);
    public LaunchProgressSnapshot Current { get { lock (gate) return current; } }
    public event Action<LaunchProgressSnapshot>? Changed;

    public LaunchProgress(Action<string>? log = null)
    {
        this.log = log;
        Log(current);
    }
    public void Report(LaunchState state)
    {
        lock (gate)
        {
            if (!Enum.IsDefined(state) || current.State is LaunchState.GameReady or LaunchState.Failed or LaunchState.Cancelled || state == current.State) return;
            bool terminal = state is LaunchState.Failed or LaunchState.Cancelled;
            int value = terminal ? current.Percentage : Percentage(state);
            if (!terminal && value < current.Percentage) return;
            current = new(state, value);
            Log(current);
            // Diagnostics/presentation cannot change launch success, including a broken subscriber.
            if (Changed != null)
                foreach (Action<LaunchProgressSnapshot> subscriber in Changed.GetInvocationList())
                    try { subscriber(current); } catch { }
        }
    }
    private void Log(LaunchProgressSnapshot value)
    {
        try { log?.Invoke($"LaunchState: {value.State} Progress={value.Percentage}"); } catch { }
    }
    public static int Percentage(LaunchState state) => state switch
    {
        LaunchState.Initializing => 5, LaunchState.StartingUU => 15, LaunchState.PreparingUU => 25,
        LaunchState.Boosting => 40, LaunchState.StartingBattleNet => 55, LaunchState.WaitingForBattleNet => 65,
        LaunchState.StartingHeroes => 80, LaunchState.PreparingHeroes => 90, LaunchState.GameReady => 100,
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };
}

public static class LaunchProgressText
{
    public static string For(LaunchState state) => state switch
    {
        LaunchState.Initializing => "正在准备…", LaunchState.StartingUU => "正在启动网易 UU…",
        LaunchState.PreparingUU => "正在准备加速器…", LaunchState.Boosting => "正在加速《风暴英雄》…",
        LaunchState.StartingBattleNet => "正在启动暴雪游戏平台…", LaunchState.WaitingForBattleNet => "正在等待暴雪游戏平台…",
        LaunchState.StartingHeroes => "正在启动《风暴英雄》…", LaunchState.PreparingHeroes => "正在准备进入游戏…",
        LaunchState.GameReady => "启动完成", LaunchState.Failed => "启动失败", LaunchState.Cancelled => "已取消",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };
}
