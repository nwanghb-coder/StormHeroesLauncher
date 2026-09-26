using StormHeroesLauncher.WindowSupport;
using System.Text;
using StormHeroesLauncher.Configuration;
using StormHeroesLauncher.Models;
using StormHeroesLauncher.Services;

// This executable is its own fake CLI. It never invokes UU, touches networking, or uses UuService.
if (args.FirstOrDefault() == "--fake-child")
{
    Console.OutputEncoding = Encoding.UTF8;
    if (args[1] == "wait")
    {
        await Task.Delay(TimeSpan.FromSeconds(60));
        return;
    }
    Console.WriteLine(args[2]);
    Console.Error.Write(new string('错', 100000));
    Environment.ExitCode = 7;
    return;
}

var options = new UuCliOptions { PollIntervalMilliseconds = 100, BoostTimeoutSeconds = 1, ReadinessTimeoutSeconds = 1 };
var logFile = Path.Combine(AppContext.BaseDirectory, "offline-tests.log");
var logger = new AppLogger(logFile);
int passed = 0;
void Assert(bool value, string name)
{
    if (!value) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
    passed++;
}
async Task Fails(Func<Task> action, CliFailureKind kind, string name)
{
    try { await action(); throw new Exception("Expected failure: " + name); }
    catch (UuCliException ex) { Assert(ex.Kind == kind, name); }
}
string JsonStatus(string status, bool boosting) => $$$"""
{"success":true,"data":{"isBoosting":{{{boosting.ToString().ToLowerInvariant()}}},"boosters":[{"gameId":"{{{options.GameId}}}","gameName":"风暴英雄国际服","status":"{{{status}}}","nodeName":"首尔电信53","nodeId":"node-1","nodeMode":"进程模式","ping":48,"packetLoss":0.0}]}}
""";
var startJson = $$$"""{"success":true,"data":{"gameId":"{{{options.GameId}}}","zoneId":"{{{options.ZoneId}}}","serverId":"{{{options.ServerId}}}"}}""";
var fake = new FakeRunner();
var cli = new UuCliService(options, logger, fake);
fake.Output = new(0, """{"success":false,"error":{"code":"API_ERROR","message":"secret-token-DO-NOT-LOG"}}""", "secret-stderr");
await Fails(async () => await cli.StartHeroesBoostAsync(), CliFailureKind.Rejected, "exit zero + success false rejected");
Assert(fake.LastArguments.SequenceEqual(new[] { "--json", "start", "--id", options.GameId, "--zone", options.ZoneId, "--server", options.ServerId }), "safe exact start arguments");
fake.Output = new(9, startJson, "");
Assert((await cli.StartHeroesBoostAsync()).GameId == options.GameId, "JSON success authoritative even with nonzero exit");
fake.Output = new(0, """{"success":true,"data":{"gameId":"another-game"}}""", "");
await Fails(async () => await cli.StartHeroesBoostAsync(), CliFailureKind.InvalidData, "wrong start game rejected");
await Fails(async () => await cli.StopHeroesBoostAsync(), CliFailureKind.InvalidData, "wrong stop game rejected");
foreach (var bad in new[] { "not json", "{}", "null", """{"success":true}""", """{"success":"true","data":{}}""" })
{
    fake.Output = new(0, bad, "");
    await Fails(async () => await cli.StartHeroesBoostAsync(), CliFailureKind.InvalidData, "malformed/missing envelope " + bad);
}
fake.Output = new(0, JsonStatus("boosting", true), "");
var state = await cli.GetHeroesBoostStatusAsync();
Assert(state.IsReady && state.GameName == "风暴英雄国际服" && state.NodeName == "首尔电信53" &&
    state.Ping == 48 && state.PacketLoss == 0 && state.NodeMode == "进程模式", "nested status fields");
fake.Output = new(0, $$$"""{"success":true,"data":{"gameId":"{{{options.GameId}}}","isBoosting":true,"status":"boosting","nodeName":"首尔","ping":48,"packetLoss":0}}""", "");
Assert((await cli.GetHeroesBoostStatusAsync()).IsReady, "flat status supported");
fake.Output = new(0, """{"success":true,"data":{"isBoosting":true,"boosters":[{"gameId":"other","status":"boosting"}]}}""", "");
Assert(!(await cli.GetHeroesBoostStatusAsync()).IsReady, "other game cannot satisfy target readiness");
fake.Output = new(0, """{"success":true,"data":{"isBoosting":false,"boosters":[]}}""", "");
Assert((await cli.GetHeroesBoostStatusAsync()).Status == "not_boosting", "empty not boosting response");
fake.Output = new(0, """{"success":true,"data":{"isBoosting":true}}""", "");
await Fails(async () => await cli.GetHeroesBoostStatusAsync(), CliFailureKind.InvalidData, "missing boost state rejected");
fake.Output = new(0, """{"success":true,"data":{"isBoosting":false,"status":"boosting"}}""", "");
await Fails(async () => await cli.GetHeroesBoostStatusAsync(), CliFailureKind.InvalidData, "contradictory status rejected");
fake.Output = new(0, startJson, "");
await cli.StopHeroesBoostAsync();
Assert(fake.LastArguments.SequenceEqual(new[] { "--json", "stop", "--id", options.GameId }), "stop is scoped, never all");

foreach (var invalidBooster in new[] { "null", "{}" })
{
    fake.Output = new(0, "{\"success\":true,\"data\":{\"isBoosting\":true,\"boosters\":[" + invalidBooster + "]}}", "");
    await Fails(async () => await cli.GetHeroesBoostStatusAsync(), CliFailureKind.InvalidData, "invalid booster entry rejected " + invalidBooster);
}
// Workflow: transient readiness failure, ready, one start, starting, boosting.
fake.Queue.Enqueue(new(0, """{"success":false,"error":{"code":"API_ERROR"}}""", ""));
fake.Queue.Enqueue(new(0, """{"success":true,"data":{"isBoosting":false,"boosters":[]}}""", ""));
fake.Queue.Enqueue(new(0, startJson, ""));
fake.Queue.Enqueue(new(0, JsonStatus("starting", false), ""));
fake.Queue.Enqueue(new(0, JsonStatus("boosting", true), ""));
fake.History.Clear();
var flow = new HeroesBoostWorkflow(_ => Task.FromResult("UU 加速器：已运行"), cli, options, logger);
Assert((await flow.StartAsync(_ => { }, CancellationToken.None)).IsReady, "workflow waits for status boosting");
Assert(fake.History.Select(a => a[1]).SequenceEqual(new[] { "status", "status", "start", "status", "status" }), "workflow only starts once after readiness");
var count = fake.History.Count;
var cancelledFlow = new HeroesBoostWorkflow(_ => Task.FromResult("UU 加速器：启动已取消"), cli, options, logger);
try { await cancelledFlow.StartAsync(_ => { }, CancellationToken.None); throw new Exception("UAC cancellation not handled"); }
catch (InvalidOperationException) { Assert(fake.History.Count == count, "UAC cancelled: no CLI call"); }

fake.Output = new(0, JsonStatus("starting", false), "");
try { await flow.WaitStoppedAsync(CancellationToken.None); throw new Exception("expected timeout"); }
catch (TimeoutException) { Assert(true, "bounded status polling timeout"); }
using (var cancel = new CancellationTokenSource())
{
    cancel.Cancel();
    try { await flow.StartAsync(_ => { }, cancel.Token); throw new Exception("expected cancellation"); }
    catch (OperationCanceledException) { Assert(true, "workflow cancellation"); }
}
Assert(!File.ReadAllText(logFile).Contains("secret-token-DO-NOT-LOG") &&
    !File.ReadAllText(logFile).Contains("secret-stderr"), "raw CLI errors and stderr never persisted");

// Real OS process handling, using only this harmless test executable as a child.
var runner = new CliProcessRunner();
var self = Path.Combine(AppContext.BaseDirectory, "StormHeroesLauncher.OfflineTests.exe");
var special = "风暴英雄 空格 \"quoted\" & |";
var output = await runner.RunAsync(self, ["--fake-child", "output", special], TimeSpan.FromSeconds(10), CancellationToken.None);
Assert(output.ExitCode == 7 && output.Stdout.Trim() == special && output.Stderr.Length == 100000,
    "UTF-8, argument boundaries, simultaneous streams and exit code");
await Fails(async () => await runner.RunAsync(self + ".missing", [], TimeSpan.FromSeconds(1), CancellationToken.None),
    CliFailureKind.MissingExecutable, "missing executable");
var invalidExe = Path.Combine(AppContext.BaseDirectory, "not-an-executable.exe");
await File.WriteAllTextAsync(invalidExe, "fake invalid PE for offline test");
await Fails(async () => await runner.RunAsync(invalidExe, [], TimeSpan.FromSeconds(1), CancellationToken.None),
    CliFailureKind.LaunchFailed, "launch failure");
await Fails(async () => await runner.RunAsync(self, ["--fake-child", "wait"], TimeSpan.FromMilliseconds(300), CancellationToken.None),
    CliFailureKind.Timeout, "process timeout");
using (var cancel = new CancellationTokenSource(300))
{
    try { await runner.RunAsync(self, ["--fake-child", "wait"], TimeSpan.FromSeconds(10), cancel.Token); throw new Exception("expected cancellation"); }
    catch (OperationCanceledException) { Assert(true, "process cancellation"); }
}

 // Alpha orchestration tests: only delegates/fakes; no UU/Battle.net/Heroes process inspection or launch.
var steps = new List<string>();
bool alreadyRunning = false;
bool validBoost = true;
bool failBattleNet = false;
bool failGame = false;
var alpha = new HeroesLaunchWorkflow(
    _ => { steps.Add("game-check"); return Task.FromResult(alreadyRunning); },
    _ => { steps.Add("boost"); return Task.FromResult(new HeroesBoostStatus(validBoost, validBoost ? "boosting" : "starting", null, null, null, null, null, null)); },
    _ => { steps.Add("battle"); return failBattleNet ? Task.FromException(new TimeoutException("fake battle timeout")) : Task.CompletedTask; },
    _ => { steps.Add("game"); return failGame ? Task.FromException(new TimeoutException("fake game timeout")) : Task.CompletedTask; },
    () => steps.Add("validate"), logger);
await alpha.RunAsync(CancellationToken.None);
Assert(steps.SequenceEqual(new[] { "game-check", "validate", "boost", "battle", "game" }), "Alpha orders confirmed boost before Battle.net and game");
steps.Clear(); alreadyRunning = true;
await alpha.RunAsync(CancellationToken.None);
Assert(steps.SequenceEqual(new[] { "game-check" }), "Alpha already-running game causes no launch or boost mutation");
alreadyRunning = false; validBoost = false; steps.Clear();
try { await alpha.RunAsync(CancellationToken.None); throw new Exception("Expected boost rejection"); }
catch (InvalidOperationException) { Assert(!steps.Contains("battle") && !steps.Contains("game"), "Alpha unconfirmed boost blocks game and Battle.net"); }
validBoost = true; failBattleNet = true; steps.Clear();
try { await alpha.RunAsync(CancellationToken.None); throw new Exception("Expected Battle.net timeout"); }
catch (TimeoutException) { Assert(!steps.Contains("game"), "Alpha Battle.net failure prevents Switcher"); }
failBattleNet = false; failGame = true; steps.Clear();
try { await alpha.RunAsync(CancellationToken.None); throw new Exception("Expected game timeout"); }
catch (TimeoutException) { Assert(steps.Count(x => x == "game") == 1, "Alpha game failure is not retried"); }
steps.Clear();
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel();
    try { await alpha.RunAsync(cancelled.Token); throw new Exception("Expected cancellation"); }
    catch (OperationCanceledException) { Assert(steps.Count == 0, "Alpha pre-cancellation has no side effects"); }
}
var launchInfo = HeroesProcessService.CreateStartInfo(@"C:\Friend\Heroes\Support64\HeroesSwitcher_x64.exe");
Assert(launchInfo.FileName == @"C:\Friend\Heroes\Support64\HeroesSwitcher_x64.exe" &&
    launchInfo.WorkingDirectory == @"C:\Friend\Heroes\Support64", "Alpha uses stable Switcher path and working directory");
Assert(!launchInfo.UseShellExecute && string.IsNullOrEmpty(launchInfo.Verb) &&
    launchInfo.ArgumentList.SequenceEqual(new[] { "-sso=1", "-launch", "-uid", "heroes" }), "Alpha exact ordinary arguments with no elevation");

// Friend Test configuration/policy tests. No native window APIs or external applications.
var testFolder = Path.Combine(AppContext.BaseDirectory, "settings-tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testFolder);
var settingsStore = new SettingsStore(Path.Combine(testFolder, "settings.json"));
var fresh = settingsStore.Load();
Assert(fresh.Validate().Length == 4 && fresh.BattleNetWindowMode == BattleNetWindowMode.Minimized, "first-run missing paths and default minimized");
string FakeFile(string name) { string path = Path.Combine(testFolder, name); File.WriteAllText(path, "offline fixture only"); return path; }
var configured = new LauncherSettings { UuLauncherPath = FakeFile("uu_launcher.exe"), UuCliPath = FakeFile("uu-cli.exe"), BattleNetPath = FakeFile("Battle.net.exe"), HeroesSwitcherPath = FakeFile("HeroesSwitcher_x64.exe") };
Assert(configured.Validate().Length == 0, "all four configured files validated");
settingsStore.Save(configured);
Assert(settingsStore.Load() == configured, "settings JSON round trip");
Assert(File.ReadAllText(settingsStore.FilePath).Contains("Minimized"), "window mode uses readable JSON enum");
var broken = configured with { BattleNetPath = Path.Combine(testFolder, "missing", "Battle.net.exe") };
Assert(broken.Validate().Length == 1, "invalid saved path identified");
try { settingsStore.Save(broken); throw new Exception("invalid settings saved"); } catch (InvalidDataException) { Assert(settingsStore.Load() == configured, "invalid save preserves prior settings"); }
Assert(StartupRouting.OpenSettings([], false, false), "first-run routes to recovery before workflow");
Assert(StartupRouting.OpenSettings(["--settings"], false, true), "explicit settings route");
Assert(StartupRouting.OpenSettings([], true, true), "Shift settings route");
Assert(StartupRouting.OpenSettings([], false, true, true), "corrupt settings routes to recovery");
Assert(!StartupRouting.OpenSettings([], false, true), "valid normal launch is headless");
Assert((configured with { UuCliPath = configured.BattleNetPath }).Validate().Length == 1, "wrong executable name rejected");
var configuredStart = HeroesProcessService.CreateStartInfo(configured.HeroesSwitcherPath);
Assert(configuredStart.FileName == configured.HeroesSwitcherPath && configuredStart.WorkingDirectory == testFolder, "Switcher start uses supplied friend path");
var configuredSteps = new List<string>();
var configuredFlow = new HeroesLaunchWorkflow(_ => Task.FromResult(false), _ => { configuredSteps.Add(configured.UuCliPath); return Task.FromResult(new HeroesBoostStatus(true, "boosting", null, null, null, null, null, null)); },
    _ => { configuredSteps.Add(configured.BattleNetPath); return Task.CompletedTask; }, _ => { configuredSteps.Add(configuredStart.FileName); return Task.CompletedTask; }, configured.RequireValid, logger);
await configuredFlow.RunAsync(CancellationToken.None);
Assert(configuredSteps.SequenceEqual(new[] { configured.UuCliPath, configured.BattleNetPath, configured.HeroesSwitcherPath }), "configured workflow sequence");
int minimizeCalls = 0;
var policy = new WindowPolicy(logger, (_, _) => { minimizeCalls++; return Task.FromResult(true); });
await policy.BattleNetAsync(BattleNetWindowMode.Minimized, CancellationToken.None);
Assert(minimizeCalls == 1, "Minimized requests window management");
await policy.BattleNetAsync(BattleNetWindowMode.Normal, CancellationToken.None);
await policy.ApplyAsync("uu", false, CancellationToken.None);
Assert(minimizeCalls == 1, "Normal and explicit skip preserve window state");
await policy.ApplyAsync("uu", true, CancellationToken.None);
Assert(minimizeCalls == 2, "UU minimize policy applies regardless of cold or warm startup");
await new WindowPolicy(logger, (_, _) => Task.FromResult(false)).BattleNetAsync(BattleNetWindowMode.Minimized, CancellationToken.None);
await new WindowPolicy(logger, (_, _) => throw new InvalidOperationException("fake window failure")).BattleNetAsync(BattleNetWindowMode.Minimized, CancellationToken.None);
Assert(File.ReadAllText(logFile).Contains("窗口处理失败"), "window failure safely logged without crash");
// Real named mutex semantics tested on another thread; never acquire the application mutex.
string mutexName = @"Local\StormHeroesLauncher.OfflineTest." + Guid.NewGuid().ToString("N");
using (var owner = new Mutex(true, mutexName))
{
    bool duplicateEntered = true;
    var thread = new Thread(() => { using var duplicate = new Mutex(false, mutexName); duplicateEntered = duplicate.WaitOne(0); if (duplicateEntered) duplicate.ReleaseMutex(); });
    thread.Start(); thread.Join(); owner.ReleaseMutex();
    Assert(!duplicateEntered, "second workflow cannot enter held mutex");
}
File.WriteAllText(settingsStore.FilePath, "{");
try { settingsStore.Load(); throw new Exception("corrupt JSON accepted"); } catch (System.Text.Json.JsonException) { Assert(true, "corrupt JSON detected for startup recovery"); }
// Window selection and action use synthetic HWND/PID data only.
ExternalWindow W(long hwnd, uint pid, string process, string title, string cls = "DuiMain", bool visible = true, bool iconic = false) =>
    new(new IntPtr(hwnd), pid, process, cls, title, visible, true, IntPtr.Zero, 0, iconic);
var uuMain = W(101, 11, "uu", "UU加速器");
var uuHelper = W(102, 12, "uu_launcher", "网易UU加速器") with { Owner = new IntPtr(101), OwnerPid = 11 };
var ball = W(103, 13, "uu_ball", "floating ball");
var unrelated = W(104, 14, "other", "UU加速器");
var familyIds = new HashSet<uint> { 11, 12, 13 };
Assert(ExternalWindowSelection.IsMain("uu", uuMain, familyIds), "UU main belongs to uu PID, not launcher PID");
Assert(ExternalWindowSelection.IsMain("uu", uuHelper, familyIds), "related PID and same-family owned main window selected");
Assert(!ExternalWindowSelection.IsMain("uu", ball, familyIds) && !ExternalWindowSelection.IsMain("uu", unrelated, familyIds), "floating ball and unrelated title match rejected");
Assert(!ExternalWindowSelection.IsMain("uu", uuMain with { Visible = false }, familyIds), "hidden tray window left alone");
Assert(!ExternalWindowSelection.IsMain("uu", uuHelper with { OwnerPid = 999 }, familyIds), "unrelated owner rejected");
var battleWindow = W(201, 21, "Battle.net", "戦網", "Chrome_WidgetWin_0");
Assert(ExternalWindowSelection.IsMain("Battle.net", battleWindow, new HashSet<uint> {21}), "Battle.net selected by family and Chromium class independent of title language");
Assert(!ExternalWindowSelection.IsMain("Battle.net", battleWindow with { ProcessName = "chrome" }, new HashSet<uint> {21}), "unrelated Chromium window rejected");
var api = new FakeWindowApi { Windows = [uuMain, uuHelper, ball, unrelated] };
Assert(await new ExternalWindowMinimizer(logger, api, 6, 1).MinimizeAsync("uu", CancellationToken.None), "UU requests and verifies real candidate handles via fake API");
Assert(api.Actions.SequenceEqual(new[] {uuMain.Handle, uuHelper.Handle}), "only selected related main windows receive minimize");
var bapi = new FakeWindowApi { Windows = [battleWindow] };
var bp = new WindowPolicy(logger, new ExternalWindowMinimizer(logger, bapi, 6, 1).MinimizeAsync);
await bp.BattleNetAsync(BattleNetWindowMode.Normal, CancellationToken.None);
Assert(bapi.Snapshots == 0, "Normal skips discovery and action");
await bp.BattleNetAsync(BattleNetWindowMode.Minimized, CancellationToken.None);
Assert(bapi.Actions.Count == 1, "Battle.net Minimized acts and confirms");
var missing = new FakeWindowApi();
Assert(!await new ExternalWindowMinimizer(logger, missing, 3, 1).MinimizeAsync("uu", CancellationToken.None) && missing.Actions.Count == 0, "window missing has bounded no-action fallback");
var rejected = new FakeWindowApi { Windows = [battleWindow], Accept = false };
await new WindowPolicy(logger, new ExternalWindowMinimizer(logger, rejected, 6, 1).MinimizeAsync).BattleNetAsync(BattleNetWindowMode.Minimized, CancellationToken.None);
Assert(rejected.Actions.Count == 3 && File.ReadAllText(logFile).Contains("RequestRejectedOrIdentityChanged"), "failed minimize bounded and logged without crash");
var delayed = new FakeWindowApi { Windows = [uuMain], MissingSamples = 2 };
Assert(await new ExternalWindowMinimizer(logger, delayed, 8, 1).MinimizeAsync("uu", CancellationToken.None), "late main window discovered within bound");
var changing = new FakeWindowApi { Windows = [battleWindow with { Minimized = true }], ChangeHandle = true };
Assert(!await new ExternalWindowMinimizer(logger, changing, 5, 1).MinimizeAsync("Battle.net", CancellationToken.None), "different minimized windows do not count as stable confirmation");
Assert(!File.ReadAllText(logFile).Contains("floating ball") && File.ReadAllText(logFile).Contains("HWND=0x"), "diagnostics identify HWND without window titles");
await CliPreparationTests.Run(logger, Assert);
await UuElevationTests.Run(logger, Assert);
await HelperResultTests.Run(logger, Assert);
await BattleNetWindowTests.Run(logger, Assert);
await HeroesPrepTests.Run(logger, Assert);
EarlySuppressionTests.Run(logger, Assert);
await HideFirstTests.Run(logger, Assert);
ShortcutImportTests.Run(logger, Assert);
ImportPresentationTests.Run(Assert);
AboutSafetyTests.Run(Assert);
IconTests.Run(logger,Assert);
PortableTests.Run(Assert);
await ObserverBuildTests.Run(Assert);
await LaunchProgressTests.Run(logger, Assert);
#if DEVELOPER_OBSERVER
await ObserverTests.Run(Assert);
#endif
Console.WriteLine($"OFFLINE TESTS PASSED: {passed}. No UU CLI was invoked.");

sealed class FakeRunner : ICliProcessRunner
{
    public CliProcessOutput Output { get; set; } = new(0, "", "");
    public Queue<CliProcessOutput> Queue { get; } = new();
    public IReadOnlyList<string> LastArguments { get; private set; } = [];
    public List<string[]> History { get; } = new();
    public Task<CliProcessOutput> RunAsync(string path, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        LastArguments = arguments.ToArray();
        History.Add(arguments.ToArray());
        return Task.FromResult(Queue.Count > 0 ? Queue.Dequeue() : Output);
    }
}

sealed class FakeWindowApi : IExternalWindowApi
{
    public List<ExternalWindow> Windows { get; set; } = [];
    public List<IntPtr> Actions { get; } = [];
    public bool Accept { get; set; } = true;
    public bool ChangeHandle { get; set; }
    public int MissingSamples { get; set; }
    public int Snapshots { get; private set; }
    public IReadOnlyList<ExternalWindow> Snapshot(string application)
    {
        Snapshots++;
        if (Snapshots <= MissingSamples) return [];
        return ChangeHandle ? Windows.Select(w => w with { Handle = new IntPtr(w.Handle.ToInt64() + Snapshots) }).ToList() : Windows.ToList();
    }
    public bool RequestMinimize(ExternalWindow window)
    {
        Actions.Add(window.Handle);
        if (Accept) Windows = Windows.Select(w => w.Handle == window.Handle ? w with { Minimized = true } : w).ToList();
        return Accept;
    }
}
