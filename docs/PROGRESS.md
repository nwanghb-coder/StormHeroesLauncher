# Minimal Progress UI v1 — 0.3.0-dev.3

This is functional Stage 2 feedback, not the final Stage 3 design. Both normal and developer
Observer packages include it. The existing Heroes preparing-game-data dialog stays visible.

## State contract

`Models/LaunchProgress.cs` defines LaunchState, the immutable LaunchProgressSnapshot(State,
Percentage), the thread-safe LaunchProgress publisher and the presentation text mapping.
There are no WPF types, observer dependencies or technical diagnostic fields in this contract.
Subscribers cannot fail launch processing. Repeated/backward stages are ignored; terminal states
cannot be replaced. Failure retains the current percentage. Values are stage markers, not elapsed
time estimates. Each accepted transition logs `LaunchState: <state> Progress=<value>` once.

| State | Progress | Chinese status | Existing boundary |
|---|---:|---|---|
| Initializing | 5% | 正在准备… | Normal launch route confirmed; final validation/save |
| StartingUU | 15% | 正在启动网易 UU… | Existing UU running check selects cold-start path |
| PreparingUU | 25% | 正在准备加速器… | Warm UU handling or successful UU ensure; CLI readiness poll |
| Boosting | 40% | 正在加速《风暴英雄》… | Before the existing single CLI start request and boost-status polling |
| StartingBattleNet | 55% | 正在启动暴雪游戏平台… | Immediately before actual Battle.net launch |
| WaitingForBattleNet | 65% | 正在等待暴雪游戏平台… | Existing readiness loop and subsequent tray handling |
| StartingHeroes | 80% | 正在启动《风暴英雄》… | Immediately before the existing Switcher call |
| PreparingHeroes | 90% | 正在准备进入游戏… | Switcher started; existing game-process polling/final window handling |
| GameReady | 100% | 启动完成 | Existing workflow success, including already-running game |
| Failed | Retained | 启动失败 | Existing exception/cancellation path; original exception preserved |

GameReady confirms the existing game-process condition, not server login. No new readiness or
success criterion is added. Warm UU skips StartingUU; existing Battle.net skips StartingBattleNet;
an already-running game can go directly from Initializing to GameReady.

## Window and routing

The 420-DIP centered window uses the application icon, system colors, product name, one status
line and a determinate horizontal progress bar with a percentage. It is not topmost and has no
custom artwork or action buttons. Native close/Alt+F4 requests are ignored during launch; they
do not introduce cancellation or kill external programs. Existing UAC cancellation still works.

Import processing and Settings routing retain their existing precedence. The window is created
immediately after normal launch routing is confirmed, before final validation/save and before UU
startup. The existing automatic discovery/CLI preparation needed to decide whether Settings must
open remains before window creation. This avoids flashing a launch window on recovery/Settings
routes. Shift, --settings, imports and About never create a progress window.

Services publish semantic stages through an optional model reference; they never reference the
window or WPF controls. LaunchProgressWindow subscribes to state and marshals background updates
with Dispatcher.BeginInvoke. It reads the latest snapshot when dispatched, so queued notifications
cannot paint an older stage. Existing asynchronous waits remain asynchronous.

On success the window displays 100% / 启动完成 for 350 ms, then closes automatically. This brief
display happens after launch succeeds; it never delays game launch. On failure it displays 启动失败
for 150 ms, closes, then the existing error dialog and diagnostics remain available. There is no
second progress error message. WPF uses the existing OnExplicitShutdown policy. Cleanup detaches
subscriptions. Presentation errors are logged without changing the launch result.

## Observer coexistence

Observer implementation and JSONL schema are unchanged. Normal builds still exclude observer
types. In developer builds, the existing two-minute observation tail starts immediately after
workflow success; the progress window closes independently after its brief completion display.
Closing this window does not stop observation or keep the UI resident. The existing mutex stays
held during the observer tail, as documented in OBSERVER.md. No UI states are added to JSONL.

## Build and validation

```powershell
./tools/Publish-Stage2.ps1
./tools/Publish-Stage2.ps1 -DeveloperObserver
```

These produce self-contained x64 two-EXE packages in:

- `artifacts/Stage2-0.3.0-dev.3-Normal/`
- `artifacts/Stage2-0.3.0-dev.3-Observer/`

Validation (SDK 10.0.401): **274 normal / 332 Observer offline checks passed**. Both publishes passed
bundle, runtime, x64, layout and path checks. The bundled normal assembly contains zero observer
types; the developer assembly contains 27. Offscreen progress layout was rendered and inspected.

The script refuses to overwrite populated folders and creates no ZIP. Both test configurations
use the existing commands in OBSERVER.md. Tests use fake launch dependencies and offscreen WPF
rendering; no real UU/Battle.net/Heroes/WindowHelper/UAC is run. Native startup interaction, focus,
UAC desktop switching, DPI scaling and external-app timing require manual acceptance.

## Manual acceptance procedure

1. Preserve the frozen release and prior build folders. Copy the entire desired new package to a
   separate location, keeping `app/StormHeroesLauncher.WindowHelper.exe` alongside the root EXE.
   Point the daily-use shortcut at the new root EXE. Settings continue to use LOCALAPPDATA.
2. Ensure UU and Battle.net are logged in, Heroes is installed/current, and both applications have
   the previously required native close-to-tray settings. Test on a normal desktop session.
3. Cold launch: manually exit UU/Battle.net, double-click the launcher, and approve the standard UAC.
   Confirm one UAC at most, responsive centered progress, forward-only stage percentages, working
   acceleration/tray behavior, and game launch. The Heroes preparation dialog must remain visible.
4. During the Battle.net-to-game wait, confirm the 65/80/90% states describe the current stage.
   Progress must not advance on a timer while the underlying operation is still waiting.
5. Confirm 100% / 启动完成 briefly appears and the progress window closes automatically. Normal
   launcher exits; Observer launcher continues its bounded background tail without a progress window.
6. Repeat with UU/Battle.net already in tray; verify safe stage skipping and no duplicate launches.
   With Heroes already running, verify direct completion and no acceleration change.
7. Cold-start again and reject UAC: confirm 启动失败, clean window closure and the existing error
   dialog/log. No automatic UAC retry or game launch should occur.
8. Confirm Shift + double-click, --settings, About, shortcut imports and invalid-configuration
   recovery show no progress window. Avoid changing valid configuration just to induce an error.
9. For Observer, allow its two-minute tail to finish, then inspect the separate JSONL summary.
   Check normal launcher logs for concise LaunchState entries. Existing observer failure must not
   block progress or game launch. Do not induce failures by changing external application files.
10. Check 100%, 125% and 150% display scaling if available: status stays on one line and controls
    remain readable. Report the build, scenario, visible stage and relevant local logs on failure.

No manual acceptance steps above were executed automatically.
