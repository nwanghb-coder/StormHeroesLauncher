# Stage 2 progress and readiness — 0.3.0-dev.4

Both packages use the compact progress window. Stage 3 visual design has not started.
UU/CLI, single-UAC, Battle.net tray handling and Switcher launch arguments remain unchanged.
Preparation suppression was enabled only after the authorized direct-game test demonstrated safe
hiding and a distinct stable main window. See [HEROES-PREP.md](HEROES-PREP.md).

## State contract

The WPF-independent LaunchProgress model is forward-only and thread-safe. Duplicate/backward
notifications are ignored; terminal states cannot change. Failure retains the current percentage.
Subscriber failures cannot fail launch processing. Percentages are stage markers, not time estimates.

| State | Progress | Chinese status |
|---|---:|---|
| Initializing | 5% | 正在准备… |
| StartingUU | 15% | 正在启动网易 UU… |
| PreparingUU | 25% | 正在准备加速器… |
| Boosting | 40% | 正在加速《风暴英雄》… |
| StartingBattleNet | 55% | 正在启动暴雪游戏平台… |
| WaitingForBattleNet | 65% | 正在等待暴雪游戏平台… |
| StartingHeroes | 80% | 正在启动《风暴英雄》… |
| PreparingHeroes | 90% | 正在准备进入游戏… |
| GameReady | 100% | 启动完成 |
| Failed | Retained | 启动失败 |

Process detection leaves PreparingHeroes active. GameReady requires the same PID, process creation
time and HWND to have a visible, enabled, ownerless, non-hung main window continuously across samples
for at least 1,500 ms. Its class must be `Heroes of the Storm` (a window class, never a title), its
rectangle at least 640×360, and its styles must exclude WS_CHILD and WS_EX_TOOLWINDOW. No visible
ownerless `#32770` window may remain in that same process. Sampling is every 100 ms plus query time.
The game must belong to the current Windows session and configured installation's Versions subtree;
there is no BaseXXXXX constant. PID/creation/path/session are revalidated on each observation.

The existing 30-second process-detection bound remains. UI confirmation has a further 120-second
bound. Timeout/exit yields Failed and the existing error dialog, without killing the game. This
confirms local window readiness, not server login, authentication or rendered game content.
An already-running game skips launch/boost mutations but still waits for its real main window;
its dialogs are never hidden by this invocation. A minimized pre-existing game must be restored
manually before a qualifying visible main window can be confirmed.

## Window and routing

The centered, 340-DIP-wide borderless window uses WindowStyle=None and ResizeMode=NoResize.
It has 12-DIP margins, a 14-DIP semibold product name above a 12-DIP-high bar, a 13-DIP percentage
in a 40-DIP column on the right, and centered 11-DIP single-line status below. There is no body icon,
title bar, close button, resize control or action button. System colors remain. It is not topmost
and never repeatedly activates itself. Alt+F4 does not introduce workflow cancellation.

Import and Settings routing retain precedence. Progress starts only after normal launch routing
is confirmed; discovery/CLI preparation for deciding Settings recovery remains before it.
Shift, --settings, shortcut imports and About never create progress. Background notifications use
the WPF dispatcher and read the latest snapshot. Success displays 100% for 350 ms; failure displays
Failed for 150 ms, then closes before the existing error dialog. Presentation errors do not change
the workflow result.

## Observer coexistence

The main developer observer session stops/flushes with a two-second bound, then launches the same
developer executable in --observer-tail mode. That route executes before the workflow mutex and
has no UI, settings writes, external launches or elevation. The main launcher closes progress,
releases the mutex and exits without waiting for the tail. Workers have independent session IDs/files
and carry parentSessionId. They can coexist with warm relaunch; each observes for at most 120 seconds,
reduced by the original session's remaining 15-minute budget, plus up to two seconds teardown.
Correlation state restarts in each file. Normal builds exclude worker and diagnostic implementation.

## Build and validation

Use tools/Publish-Stage2.ps1 and tools/Publish-Stage2.ps1 -DeveloperObserver. They create
artifacts/Stage2-0.3.0-dev.4-Normal/ and artifacts/Stage2-0.3.0-dev.4-Observer/, refuse populated
output folders and create no ZIP. The two-EXE self-contained x64 portable layout is unchanged.
Offline validation: 285 normal / 347 Observer checks. Tests cover stable readiness, ownership,
nonfatal bounded hiding, forward-only progress, borderless WPF rendering/lifecycle and independent
tail handoff with mutex reacquisition from a different thread. Full launch-chain and DPI/focus
acceptance remain manual; direct Heroes-only tests are recorded separately.

Both portable packages passed bundle/runtime/x64/layout verification. Packaged normal assembly:
ObserverTypes=0; developer assembly: ObserverTypes=29. Two actual packaged workers observed only
empty fixture roots concurrently for 12 seconds, wrote separate summaries and exited successfully
despite the workflow mutex already being held by a pre-existing dev.3 launcher. The normal package
rejected both developer modes with exit code 2. No external app was launched by this worker check.

## Manual acceptance procedure

1. Preserve frozen/prior builds. Copy the entire new package, including app/WindowHelper, to a
   separate folder and point the test shortcut at its root EXE. Use existing valid settings.
   First close any old launcher instance. During validation, dev.3 Observer PID 8772 (started
   2026-09-26 17:36:51 local time) still held the workflow mutex and was deliberately left untouched.
   Confirm its identity/path before closing; a later process may reuse that PID.
2. Ensure UU/Battle.net are installed and logged in, Heroes is current, UU close hides to tray,
   and Battle.net X minimizes to the system tray. Use a normal desktop session.
3. Cold launch: exit UU/Battle.net manually, double-click the launcher and approve at most one UAC.
   Confirm acceleration and tray behavior remain correct and progress is centered and responsive.
4. Observe 80% then 90%. The temporary preparation dialog should hide; compact progress stays until
   the stable game main window is present. Confirm 100% briefly, then automatic progress close.
5. With Observer, close Heroes normally immediately after successful launch, leave UU/Battle.net in
   tray, and launch again within 120 seconds while the old observer worker is alive. A new progress
   workflow and game launch must run normally. Only overlapping active launch workflows remain
   protected by the mutex; an old observer tail must not cause duplicate rejection.
6. Let both tails exit. Inspect separate JSONL files under LOCALAPPDATA/StormHeroesLauncher/Observer:
   distinct session IDs, ObserverTailStarted with parentSessionId, and terminal SessionSummary.
7. Repeat warm launch with Normal: no observer worker/log session should be created. With Heroes
   already running and visible, confirm readiness without duplicate launch/boost changes.
8. Reject one cold-start UAC: Failed briefly, progress closes, original error appears; no retry/game.
   Verify Shift/--settings, About and shortcut imports never flash progress. Do not corrupt settings.
9. Check 100%, 125% and 150% DPI where available: one-line status, readable text, no clipped controls,
   no repeated focus stealing. Report version, scenario, stage and relevant logs for any failure.

The full manual acceptance sequence has not been run automatically.
