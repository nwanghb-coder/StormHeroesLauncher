# HOSLauncher — Stage 2 dev.7

Dev.6 manual acceptance was reported successful by the user: active UU reuse, no redundant CLI
start, Battle.net readiness reuse, skipped progress stages and the HOSLauncher rename.
Dev.7 changes Battle.net cold-start suppression only. Real visual acceptance is still pending.

## Source timing audit before edits

The dev.6 EarlyBattleNetSuppression.Arm allocated state before Process.Start, but did not run
observation. MarkLaunch ran just before Process.Start. Actual Poll ran only after Process.Start
returned, progress reporting and readiness logging. Thus process creation latency and that
synchronous setup could leave a newly created window unobserved. No duration was measured on
real Battle.net in this task; these are source-confirmed delay paths, not measured milliseconds.

The first Qt/Chrome detection came from a full NativeWindowManager snapshot. Before acting,
Poll took another snapshot and refreshed process ownership. NativeAction.Valid took another
full snapshot/ownership scan before SW_HIDE; Chrome repeated this between SW_HIDE and WM_CLOSE.
Snapshots collected unused window titles and state. The old shared loop delayed 10 ms for its
first two seconds and 25 ms thereafter; readiness scans and synchronous diagnostic writes also
ran on that same loop. HideTrayAttempt already did NOT wait for hide confirmation, but its
identity callback performed the extra full scan. ActionRequested was recorded after the action
returned; separate HideTrayAttempt timestamps covered Chrome only. First non-visible confirmation
was sampled on a later Poll, treating absent HWNDs as hidden. Readiness required three qualified
samples and also waited until the five-second observation phase ended.

## New sequence and limits

EarlyBattleNetSuppression.StartBeforeLaunch prepares all state, arms and executes the first
observation sample before invoking the Process.Start delegate. Its asynchronous continuation uses
ConfigureAwait(false), so polling can proceed while Process.Start is still returning. There is no
post-launch observer initialization. ProcessStartInfo still uses UseShellExecute=false and the
configured working directory; no foreground, focus, restore or Show/Normal call was found/added.

Requested asynchronous delays from arming:
- 0–500 ms: 5 ms
- 500–2000 ms: 10 ms
- 2000–5000 ms: 25 ms
- At five seconds: stop this dedicated loop; existing bounded normal checkpoints remain.

These are delay targets, not hard real-time sampling guarantees. Enumeration, OS timer resolution,
thread scheduling and Battle.net's own message queue can add latency. There is no 2 ms benefit
measurement, so the lower-overhead 5 ms starting point is used. Cancellation ends observation.
The fake clock produces 370 delays over five seconds. Native work is not forcibly interrupted
mid-call, but no new candidate action is begun after the deadline.

The cold observer snapshots only relevant classes, PID/name, visibility, enabled/owner state,
and same-session Battle.net/Battle.net Launcher processes; no window titles. Qt may belong to
either verified family executable; Chrome must belong to Battle.net.exe. Each action opens and
retains the process handle, checks name/session/liveness, and rechecks HWND existence, PID,
top-level root, no owner, enabled state and exact observed/eligible class.

Chrome: detect → targeted validation → request SW_HIDE → targeted identity revalidation (visibility
no longer required) → post WM_CLOSE → sample process liveness. There is no delay, hide-confirmation
poll, full process-family enumeration, full desktop rescan or disk logging between Hide and Close.
Qt uses SW_HIDE only. Hide rejection/exception retains the safe close path; close rejection is
nonfatal. A lock-protected set shared with normal checkpoints allows one attempt per PID/HWND/class,
up to 32 identities for the launcher lifetime. Each replacement gets its own attempt and timings;
the same identity is not spammed. This deliberately does not retry a HWND that later reappears.

Readiness runs independently at the existing 500 ms cadence, accepts qualified hidden Chromium,
and needs the same window in three consecutive samples. It no longer waits for the fast loop's
deadline. This remains a window-readiness proxy, not proof of successful account authentication.

## ShowWindow decision

Retain ShowWindowAsync(hwnd, SW_HIDE). Microsoft's documented behavior posts the show request
without waiting for the target to process it, avoiding a wait on an unresponsive foreign window.
No evidence establishes that synchronous ShowWindow would improve real exposure here; equal
privilege does not make Battle.net a same-process window. Async is chosen for bounded responsiveness,
not claimed to be faster. No new native action APIs were introduced beyond hide and WM_CLOSE.

References:
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-showwindowasync
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-showwindow

## Diagnostics

Each handled identity records ProcessLaunchT0, ObserverArmedT, WindowFirstDetectedT, HideRequestedT,
WMCloseRequestedT and FirstConfirmedHiddenT. Derived values: ObserverLeadMs,
DetectionLatencyFromLaunchMs, FirstDetectedToHideMs, HideToWMCloseMs and
FirstDetectedToHiddenConfirmMs. Monotonic fractional milliseconds drive intervals; a UTC anchor
formats timestamps. Requests are timestamped immediately before calls. ProcessLaunchT0 is the
request boundary before invoking the launch delegate, not the OS process creation timestamp.

Diagnostics are buffered in the bounded identity table and written when the loop ends, after the
critical action path. Later normal checkpoints reuse the same cold-launch clock and identity table,
logging late-window requests and confirmation updates without restarting fast polling. An updated
confirmation record retains its original request timestamp; it is not a second action.
A replacement has its own record. FirstConfirmedHiddenT requires an actual
matching HWND sampled non-visible; disappearance is separately WindowAbsentT. Unknown confirmations
remain Unknown. ProcessRunningAfter is an immediate post-request sample; ProcessRunningAtConfirmation
is the later sample. Neither proves that a tray icon exists. Readiness logs ReadinessConfirmedT
and ReadinessElapsedMs separately. All exposure-related fields explicitly carry
TimingBasis=RequestAndSampling_NotPhysicalScreenExposure. They do not measure physical screen frames.

## Validation and packages

333 Normal / 403 Observer offline checks passed. New checks cover prelaunch observation including
a blocked fake Process.Start, immediate Qt/Chrome actions, no confirmation delay, hide/close failure,
replacement identities, capacity/no spam, unrelated/owned/disabled candidates, hidden readiness,
native API allowlist, exact bounded fake-clock polling, cancellation and independent timing records.
Existing warm reuse, Heroes workflow, Esc, UI, safety, UU/helper and observer tests also pass.
No real UU, Battle.net, Heroes, UAC or WindowHelper was executed. No diagnostic command was added.

Both packages passed portable x64/self-contained runtime, exact two-EXE layout, product metadata,
developer-path exclusion and Observer isolation checks (Normal types=0; Observer types=34):
- artifacts/Stage2-0.3.0-dev.7-Normal
- artifacts/Stage2-0.3.0-dev.7-Observer

Each contains HOSLauncher.exe and app/HOSLauncher.WindowHelper.exe. Main version/Build ID is
0.3.0-dev.7. WindowHelper remains the dev.6 binary, SHA-256
78763F5C9A02A8ACB67DD3E56DF9CC7A62A0A42F3A6689E320F57D4B0EDE6BB2.
The publisher requires that verified historical artifact and copies it without rebuilding.
UU startup/splash/warm logic, CLI, Single-UAC, pipe security, observer update behavior, Esc,
Heroes prep/GameReady, progress layout, imports/About/Safety, data paths and naming are unchanged.
No push, merge, tag, release or ZIP is part of this task.

## Exact manual cold-start visual acceptance

1. Keep the dev.6 and dev.7 packages in separate complete folders. Close any active launcher
   workflow and Heroes normally. Leave UU running with the accepted target acceleration so this
   comparison isolates Battle.net. Keep the existing Battle.net login and close-to-tray settings.
2. Exit Battle.net using its tray Exit command (clicking X only hides it). Confirm its launcher/main
   processes have exited in Task Manager. Do not terminate unrelated processes or change settings.
3. Run dev.6 HOSLauncher.exe once to establish the same-machine visual reference. Note whether the
   Qt splash and Chrome interface are recognizable/readable, whether login/bootstrap succeeds,
   Battle.net stays running in the tray, and Heroes reaches its normal ready UI.
4. Close Heroes normally, exit Battle.net normally again, then run the dev.7 Normal package from
   the same launch method. Observe the same items. Expect noticeably shorter Chrome exposure,
   ideally unreadable, without requiring zero visible frames. Repeat this paired comparison
   three times with the same network/login/update conditions; record any update run separately.
5. Inspect the newest LOCALAPPDATA/StormHeroesLauncher/Logs/launcher-YYYY-MM-DD.log for dev.7.
   Confirm the prelaunch armed record, per-HWND request/sample metrics, nonnegative observer lead,
   at most one close per Chrome identity, separate replacement records, process-alive evidence
   and ReadinessConfirmedT. Unknown confirmation must not be called a successful hide.
6. Confirm Battle.net remains running, native tray behavior is correct, there is no duplicate
   instance, and Heroes preparation hiding/GameReady and automatic progress close are unchanged.
   Then close Heroes and relaunch with UU/Battle.net warm: no replayed Battle.net cold suppression,
   no redundant CLI start, and skipped progress stages as accepted in dev.6.
7. Repeat the dev.7 cold check with the Observer package if desired; its update observer and
   bounded independent tail should behave as before. Report package/version, whether Chrome was
   readable, startup/tray/game outcome and relevant timing lines. Screen recording, if used, is
   independent visual evidence; request/sample timings alone do not establish UX success.

Stop after the local commit. User visual acceptance remains required.
