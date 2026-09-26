# Stage 2 dev.5 — warm reuse, Esc and UU startup evidence

Historical dev.5 record. Dev.6 supersedes its strict optional-metadata rule and product filename;
see [DEV-6.md](DEV-6.md). Existing Esc and observation behavior remains in use.

Dev.4 manual acceptance was reported broadly successful by the user. Its single-UAC launch,
UU/Battle.net tray handling, Heroes preparation hiding/readiness, progress layout and detached
Observer tail are the accepted baseline. Dev.5 does not optimize Battle.net cold-start visuals
or hide any additional UU window. Main version/Build ID is 0.3.0-dev.5; WindowHelper is unchanged.

## Exact warm decision

1. Check Heroes first. If already running, retain its existing readiness wait without any UU or
   Battle.net probe/action. Never launch a duplicate game or change acceleration.
2. If UU is running, issue one fresh validated CLI `status --id <configured game>` query. Reuse
   requires isBoosting=true, status=boosting, explicit gameId/zoneId/serverId matching all three
   configured identifiers, and UU still running when the reply is examined. There is no cached
   successful-start receipt or remembered status. Reuse skips ensure/tray/start/boost actions and
   emits `WarmFastPath UU=AlreadyBoosting UUTargetBoosting=True`.
3. Missing/mismatched identifiers, unknown status or status errors fall back to the existing ensure
   and readiness flow. Before CLI start, that flow also checks whether its readiness response proves
   the exact target already active; if so it skips start. Otherwise issue the existing single start,
   with the unchanged arguments/validation/status polling. Never automatically retry start.
4. For already-running Battle.net, use its existing proxy: same enabled, ownerless, non-hung
   Chrome_WidgetWin_* top-level window for three samples 500 ms apart. Hidden tray windows qualify.
   This is not account-login detection. Ready reuse logs `WarmFastPath BattleNet=AlreadyReady`,
   skips process launch, cold suppression and all redundant tray checkpoints around Heroes.
   Missing/changing/unknown proxy falls back to the unchanged ensure and window-handling flow.
5. Start Heroes with the accepted arguments and preparation/readiness policy. Full warm progress is
   Initializing (5%) → StartingHeroes (80%) → PreparingHeroes (90%) → GameReady (100%). No animation
   or false intermediate states is added. Partial reuse skips only the work actually avoided.

Important compatibility limit: existing repository status examples expose game/node data without
zoneId/serverId. Those replies cannot prove the full configured target and deliberately use fallback,
which may issue CLI start. Actual installed CLI support for these optional response fields is not
claimed or tested here. Region/node names are not guessed to mean zone/server IDs. Logs explain
UuTargetNotConfirmed/UuStatusUnknown rather than claiming successful reuse. No credentials are logged.

## Esc contract

Esc handled through the focused progress window's normal WPF PreviewKeyDown requests the launch
CancellationToken. It sets terminal Cancelled / 已取消, preserves the percentage, and closes progress
after a brief interval (normally up to 150 ms). There is no Cancel button or global keyboard hook.
Delayed notifications cannot advance or fail an already-cancelled progress model.

Cancellation means stop further launcher actions, not rollback. Tokens guard stage transitions and
start boundaries. Leave already-started UU, Battle.net, Switcher and Heroes alive; never send CLI stop.
The CLI runner stops waiting on Esc without killing an already-issued request; its pipe cleanup is
bounded. The existing command-timeout handling may still terminate only its own timed-out CLI
request, never the acceleration service or descendants. An already-requested elevated helper may
complete its original operation; no new helper request or IPC protocol is introduced.

App cancellation has a separate success-exit path with no failure dialog. It closes progress and
releases the workflow mutex on the owning WPF thread before bounded observer teardown. No new tail
is started on cancellation. UAC rejection remains the original failure path. Esc cannot operate on
the UAC secure desktop or when another application has focus; no focus stealing is added. Cancellation
does not save settings or undo the normal pre-launch save that may already have completed.

## Developer-only startup-window observation

Only the developer build subscribes to the actual StartingUU boundary. It creates a separate
unique-session JSONL file in the existing LOCALAPPDATA/StormHeroesLauncher/Observer directory.
This keeps the existing observer sink single-writer and its update schema/polling unchanged.

While waiting for a new uu_launcher.exe/uu.exe identity created since arming, query at 100 ms intervals
for at most 35 seconds (including possible UAC wait). On detection, sample at 20 ms intervals for
at most five seconds. Query execution time is additional; this is polling, not a guaranteed trace.
An unusually long UAC delay or a splash shorter than discovery/query latency can be missed. The
observer never elevates to bypass inaccessible metadata. Cancellation stops it; exit waits at most
200 ms, after releasing the launch mutex. It does not affect the ordinary 500 ms observer polling.

Candidates are visible windows from new, same-session, installation-validated UU processes or their
observed related children. Identity/creation time is checked; unrelated and pre-existing processes
are excluded. Capture only documented process/window metadata. New events are:

- UuStartupObservationStarted: polling, active and arming bounds.
- UuStartupWindowAppeared / UuStartupWindowDisappeared: CandidateKind=UnknownStartupWindow;
  PID/parent PID/creation/session, process name/path and relationship, HWND/class/visible/enabled,
  owner HWND/PID, approximate left/top/width/height, first-seen/disappearance timestamps and observed
  lifetime. Event envelope retains schemaVersion=1, timestamp, elapsedMs, source and sessionId.
- UuStartupWindowObservationEnded: still-visible candidates at cancellation/deadline are censored;
  disappearanceTimestamp stays null, so the bound is not mislabeled as actual disappearance.
- SessionSummary: candidate count and termination reason. Maximum 256 candidate lifetimes per run.

No raw window titles, automatic Splash classification, hiding, closing, termination, clicks,
UI Automation, hooks, injection, process-memory access or packet capture. Normal assemblies omit
all startup-observer types. Existing About/Safety claims remain accurate; no Safety text change is needed.

## Offline validation and packages

303 Normal / 373 Observer checks pass. Focused coverage includes strict target reuse, uncertain
fallback, Battle.net proxy stability, omitted progress states, already-running Heroes, Esc routing,
no subsequent stages/stop command, cancellation mutex release, a harmless CLI child's survival,
bounded startup sampling, event serialization, unrelated-window filtering and compilation isolation.
No real UU/Battle.net/Heroes, UAC, helper or native splash observation was run for dev.5.

Build with tools/Publish-Stage2.ps1, then the same command with -DeveloperObserver. Outputs:

- artifacts/Stage2-0.3.0-dev.5-Normal/
- artifacts/Stage2-0.3.0-dev.5-Observer/

Both retain self-contained x64 root StormHeroesLauncher.exe plus app/StormHeroesLauncher.WindowHelper.exe.
The publisher requires the accepted dev.4 Normal helper artifact and copies it unchanged, checking
SHA-256 CE4B259E55DE1135D74AB1900AC7C981A1E2FE249466DB9E6DAB773AB222246B. This preserves its
accepted binary/build metadata as well as source; restore that artifact if publishing on another machine.
Package verification passes with zero Observer types in Normal and 34 in Observer, including the
five new developer startup-observation types. Both packages retain exactly two executable files.
No ZIP, tag, merge or push. The frozen Friend-0.2 release and earlier packages are preserved.

## Exact manual acceptance

Use a copied complete dev.5 package and valid existing settings. Preserve older/frozen folders.
Close old launcher workflows first; ordinary dev.4+ observer tails may coexist. Keep required native
UU/Battle.net tray settings and logins. Inspect Logs/launcher-YYYY-MM-DD.log alongside the visible result.

1. **Warm Fast Path:** leave UU running and accelerating the intended Heroes game/zone/server;
   leave Battle.net ready in tray; close Heroes normally. Launch dev.5. Expect AlreadyBoosting and
   AlreadyReady logs, no CLI start following confirmed reuse, no transient boost-stop notification,
   no duplicate Battle.net process, and 5→80→90→100 progress. If UuTargetNotConfirmed appears, the
   status reply did not prove all target IDs: report that limitation rather than treating fallback
   as successful fast-path acceptance. Do not change target IDs to manufacture a passing result.
2. **Esc during UU:** with UU closed, launch. While progress has focus at StartingUU/PreparingUU,
   press Esc. If UAC is showing, first resolve it; it cannot send Esc to this window. Expect 已取消,
   progress close, no error dialog, and no subsequent Battle.net/Heroes launch. Any issued helper/UU
   start may finish; leave it running. Repeat during Boosting if that stage is observable, confirming
   acceleration is not stopped. Do not infer cancellation from an Esc delivered to another app.
3. **Esc during Battle.net:** start from a state requiring Battle.net launch. After its start request,
   focus the launcher progress window and press Esc during WaitingForBattleNet. Battle.net remains
   running; Heroes must not be launched; no failure dialog. No new window actions after cancellation.
4. **Esc during Heroes preparation:** launch normally; focus progress at 90% and press Esc. Progress
   changes to 已取消 and closes. Already-requested Switcher/game remain running and may finish
   opening. No game kill, boost stop or failure dialog. Reopen launcher Settings via Shift to verify
   prompt mutex release, then close Settings normally; a new launch can also enter safely.
5. **Cold UU observation:** use Observer, exit UU normally, then launch with normal UAC acceptance.
   Observe the logo manually without trying to hide it. After launch, locate the JSONL beginning
   with UuStartupObservationStarted. Inspect appearance/disappearance or censored-end events,
   class/identity/bounds/lifetime and SessionSummary. UnknownStartupWindow remains the label.
   Repeat with Normal and confirm no startup-observer session is created. Do not expect polling to
   prove that every very short or inaccessible window was captured.
6. Confirm existing UAC rejection, already-running Heroes, progress/readiness and tray behavior
   remain intact. No further Battle.net visual optimization or splash suppression is part of dev.5.
