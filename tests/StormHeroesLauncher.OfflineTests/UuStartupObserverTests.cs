using System.Reflection;
using System.Runtime.InteropServices;
using StormHeroesLauncher.Observer;

public static class UuStartupObserverTests
{
    public static async Task Run(Action<bool, string> check)
    {
        var clock = new FakeObserverClock(); var sink = new MemoryObserverSink();
        var process = new RelatedProcess(new(10, 1, "uu_launcher.exe", @"C:\UU\uu_launcher.exe", 1, 100), Family.UU, Family.UU, "KnownInstallRoot", @"C:\UU", null);
        var window = new UuStartupWindow(process, new(10, process.Identity.Key, process.Identity.ExecutablePath, 1000, "UnknownClass", true, true, 0, null, Family.UU, Family.UU), new(100, 200, 300, 150));
        var other = window with { Process = process with { Identity = process.Identity with { Pid = 99 }, RelatedTo = Family.BattleNet } };
        int polls = 0;
        await new UuStartupSampler(clock, sink, () => [process], _ => ++polls <= 2 ? [window, other] : [], 90).RunAsync(default);
        var appeared = sink.Events.Single(e => e.Event == "UuStartupWindowAppeared");
        var disappeared = (UuStartupWindowEvent)sink.Events.Single(e => e.Event == "UuStartupWindowDisappeared").Data;
        check(polls == 250 && clock.Elapsed.TotalMilliseconds == 5000 && UuStartupSampler.PollMilliseconds == 20, "UU startup sampling is 20ms for exactly five seconds with fake clock");
        check(disappeared.LifetimeMs == 40 && disappeared.DisappearanceTimestamp != null && disappeared.CandidateKind == "UnknownStartupWindow", "candidate disappearance stores observed lifetime without claiming splash identity");
        string json = ObserverJson.Serialize(appeared);
        check(json.Contains("firstSeenTimestamp") && json.Contains("bounds") && json.Contains("ownerHwnd") && json.Contains("uu_launcher.exe") &&
            json.Contains("KnownInstallRoot") && !json.Contains("title", StringComparison.OrdinalIgnoreCase), "compatible JSONL carries identity, relation, bounds and timing without raw titles");
        check(sink.Events.Count(e => e.Event == "UuStartupWindowAppeared") == 1, "unrelated startup windows ignored");

        var bounded = new FakeObserverClock(); var boundedSink = new MemoryObserverSink();
        await new UuStartupSampler(bounded, boundedSink, () => [process], _ => [window], 90).RunAsync(default);
        check(!boundedSink.Events.Any(e => e.Event == "UuStartupWindowDisappeared") &&
            boundedSink.Events.Any(e => e.Event == "UuStartupWindowObservationEnded"), "still-visible windows at deadline are censored rather than fabricated disappearances");
        var waiting = new FakeObserverClock(); var empty = new MemoryObserverSink();
        await new UuStartupSampler(waiting, empty, () => [process], _ => throw new Exception("Preexisting UU must not be sampled"), 200).RunAsync(default);
        check(waiting.Elapsed.TotalMilliseconds == 35000 && !empty.Events.Any(e => e.Event == "UuStartupWindowAppeared"), "preexisting processes do not trigger high-frequency observation; arming is bounded");
        using var stop = new CancellationTokenSource();
        var cancelledClock = new FakeObserverClock(); var cancelledSink = new MemoryObserverSink(); int calls = 0;
        await new UuStartupSampler(cancelledClock, cancelledSink, () => { if (++calls == 3) stop.Cancel(); return [process]; }, _ => [window], 90).RunAsync(stop.Token);
        check(cancelledClock.Elapsed.TotalMilliseconds < 100 && ObserverJson.Serialize(cancelledSink.Events.Last()).Contains("Cancelled"), "startup sampler cancels promptly and writes summary");
        var imports = typeof(WindowsObserverSource).GetNestedTypes(BindingFlags.NonPublic).SelectMany(t => t.GetMethods(BindingFlags.NonPublic | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<DllImportAttribute>() != null).Select(m => m.Name).ToArray();
        check(imports.Contains("GetWindowRect") && !imports.Any(n => n.Contains("ShowWindow") || n.Contains("PostMessage") || n.Contains("SendMessage") || n.Contains("Terminate") || n.Contains("Hook") || n.Contains("GetWindowText")),
            "startup observation source imports metadata APIs only; no hide/close/terminate/hooks/titles");
    }
}
