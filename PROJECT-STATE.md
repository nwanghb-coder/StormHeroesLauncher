# HOSLauncher Project State

## 1. Current milestone

Stage 2 / Feature Complete is active on `stage2-feature-complete`, currently `0.3.0-dev.7`, with user-facing identity HOSLauncher. Dev.7 starts Battle.net suppression before Process.Start, uses bounded 5/10/25 ms asynchronous polling and targeted HWND/process revalidation, and keeps hidden-Chromium readiness independent. See docs/DEV-7.md for the timing audit, metrics, limitations and exact manual acceptance. Dev.6 warm reuse, skipped progress and naming passed user manual acceptance and remain unchanged. UU, CLI, helper, security, Esc, Observer, Heroes, UI and data paths are preserved. No Stage 3 work, migration, push, merge or tag.

Historical Dev.5 validation: 303 Normal / 373 Observer offline checks passed. No real UU/Battle.net/Heroes/helper/UAC or native splash test was run. Warm UU reuse requires a fresh boosting response explicitly matching gameId/zoneId/serverId and UU still running; missing zone/server fields use normal fallback and may still issue CLI start. Ready Battle.net reuses the accepted three-sample proxy without cold/tray replay. Esc leaves issued processes and acceleration intact, reports 已取消 and releases the mutex before observer teardown. The developer startup sampler uses 100 ms discovery (35-second bound), then 20 ms polling for five seconds in a separate compatible JSONL session. Normal builds omit it. Main version/Build ID advances; WindowHelper, pipe security, cold suppression and Heroes readiness/hiding are preserved. Outputs: artifacts/Stage2-0.3.0-dev.5-Normal and artifacts/Stage2-0.3.0-dev.5-Observer. Local commit only, no push/merge/tag/ZIP. See docs/DEV-5.md for the required user-run warm/Esc/startup-observation checks.

Historical Dev.5 portable verification passed for both two-EXE self-contained x64 packages: Normal ObserverTypes=0, developer ObserverTypes=34. Both contain the accepted dev.4 helper byte-for-byte (SHA-256 recorded in docs/DEV-5.md). The dev.5 publisher required that verified historical helper artifact instead of rebuilding its metadata. Safety wording remains accurate and unchanged.

Dev.4 validation (2026-09-26): 285 normal / 347 Observer offline checks passed. Three explicitly authorized direct Switcher tests passed, hiding the ownerless #32770 preparation dialog without abnormal game exit and confirming a stable main window around 18 seconds. The final test uses the shared production policy and confirmed graceful cleanup; the first two used exact test-owned termination after a graceful-close attempt. No UU/Battle.net launch, helper or UAC was invoked by these tests. See docs/HEROES-PREP.md for evidence and docs/PROGRESS.md for remaining full-launch/warm-relaunch/DPI acceptance. Outputs: artifacts/Stage2-0.3.0-dev.4-Normal and artifacts/Stage2-0.3.0-dev.4-Observer. Local commit only; no push, merge, tag or ZIP.

Historical dev.4 package verification: normal ObserverTypes=0; developer ObserverTypes=29. Two actual packaged tail workers ran concurrently against empty fixture roots for 12 seconds, bypassed an already-held workflow mutex, wrote separate summaries and exited. Normal rejected developer modes with exit 2. A pre-existing dev.3 Observer launcher (then PID 8772) held the mutex at verification time and was left untouched; that old observation does not establish its current status. Subsequent dev.4 manual acceptance was reported successful by the user.

Progress UI v1 validation (2026-09-26): 274 normal and 332 observer offline checks passed. Offscreen WPF rendering and dispatcher/lifecycle checks passed. Both self-contained x64 portable packages passed static validation (normal ObserverTypes=0; developer ObserverTypes=27). Outputs: artifacts/Stage2-0.3.0-dev.3-Normal and artifacts/Stage2-0.3.0-dev.3-Observer. No real UU/Battle.net/game/WindowHelper/UAC was run. No push, tag, merge or public ZIP for this work; manual acceptance remains pending.

Observer v1 validation (2026-09-26): 251 normal and 309 observer offline checks passed using fake observation sources. Both self-contained x64 portable packages passed verification; packaged normal assembly has zero observer types, developer assembly has 27. Outputs: artifacts/Stage2-0.3.0-dev.2-Normal and artifacts/Stage2-0.3.0-dev.2-Observer. No real UU/Battle.net/game, WindowHelper or UAC was run. Source changes are local only; no push, milestone tag or new release for this work.

Git/GitHub synchronization was completed after the entry inspection: main and friend-0.2 remain at 9dda358225aa0a6df1b4ee9f78664f741381ecd9; origin/stage2-feature-complete was synchronized at 778fdd7. The private repository's frozen Friend-0.2 pre-release was published using the original ZIP and RELEASE-INFO.txt. These historical assets are unchanged.

Stage 2 entry validation (2026-09-26): SDK 10.0.401; Release x64 solution build passed with zero warnings/errors; all 249 offline tests passed, including updated About/Safety identity checks. No real UU/Battle.net/game launch, UAC acceptance or new portable package was performed. Safety behavior and published Safety claims are unchanged. No push or publication was performed; remote state remains unverified after the initial GitHub connectivity failure.

The following release description is historical:

Friend Release is complete and frozen: end of major Stage 1. Trusted friend test only, not a public production release. Accepted baseline: Friend v17 portable. Frozen package: StormHeroesLauncher-Friend-0.2; application version 0.2.0-friend-test; Build ID friend-0.2-release-9e7651132d9a. Freeze changes only main assembly Build ID metadata; the accepted helper is unchanged byte-for-byte. Manual runtime acceptance was supplied by the user; release freeze used static checks only.

Release record: artifacts/Friend-0.2-Release/RELEASE-INFO.txt. Treat the ZIP and recorded hashes as the frozen artifact; a future rebuild is not automatically the same artifact.

Tracked release-record copy: docs/releases/friend-0.2.txt. Frozen source tag: friend-0.2 at 9dda358225aa0a6df1b4ee9f78664f741381ecd9. At Stage 2 entry, the ZIP and both executable hashes matched the record. Binaries remain outside Git and must be preserved separately.

Dev.6 offline checks: 315 Normal / 385 Observer. Output directories: artifacts/Stage2-0.3.0-dev.6-Normal and artifacts/Stage2-0.3.0-dev.6-Observer. Both use HOSLauncher.exe plus app/HOSLauncher.WindowHelper.exe. No real external applications were run by Codex for dev.6; the user subsequently reported successful manual warm acceptance. Historical Friend-0.2 artifacts retain their old identity.

Dev.7 offline checks: 333 Normal / 403 Observer. Both artifacts/Stage2-0.3.0-dev.7-Normal and artifacts/Stage2-0.3.0-dev.7-Observer passed two-EXE self-contained win-x64 verification (Observer types 0 / 34). Both preserve the accepted dev.6 WindowHelper byte-for-byte. No real application was launched for dev.7; cold-start visual improvement is pending user acceptance.

## 2. Product goal

A portable Windows Heroes of the Storm launcher automating NetEase UU startup and acceleration, Battle.net startup, Heroes launch, external-window suppression/native tray behavior, and minimal first-time shortcut configuration. Primary UX: one double-click + at most one standard Windows UAC confirmation + unattended launch afterward.

## 3. Development stages

1. Stage 1 — Friend Release (complete/frozen).
2. Stage 2 — Feature Complete.
3. Stage 3 — UI Design: UI Phase 1, UI Phase 2, UI Phase 3.
4. Stage 4 — Full Release refinement / bug fixing / maintenance.

No additional phase details have been established here.

## 4. Current technology

C#, .NET 10, WPF, x64, Windows 10/11 target, documented Win32 APIs, SharpCompress pinned to 0.50.4. Both executables use self-contained single-file publishing; no trimming. Freeze built with SDK 10.0.401 and bundled runtime 10.0.12. No preinstalled .NET is required.

## 5. Current portable layout (HOSLauncher)

```text
HOSLauncher.exe
app/
  HOSLauncher.WindowHelper.exe
```

Both are self-contained x64 single-file executables. The helper is resolved relative to the main executable, not the working directory. User configuration, logs and CLI cache live under %LOCALAPPDATA%\StormHeroesLauncher. Supported .NET native-library extraction may use the Windows temporary .net cache. Runtime ZIP contains only these two EXEs; no source, tests, PDBs, developer documents or NetEase CLI.

## 6. Verified launch architecture

Main launcher remains asInvoker, with duplicate-launch protection. Cold UU: main -> one narrow elevated WindowHelper through standard runas/UAC -> official UU launcher -> early UU Hide-first window suppression -> native WM_CLOSE tray behavior -> helper exits -> main continues CLI readiness and boosting. Warm UU already in tray may need no helper/UAC. Cancellation is handled without an elevation retry loop.

Helper results use bounded SHUR-v1 versioned framing over a random one-shot named pipe. Preserve same-user ACL restrictions, peer identity validation and bounded payloads. The old cross-elevation CurrentUserOnly/ValidateRemotePipeUser failure was resolved; do not reintroduce it.

CLI Auto discovers/prepares the official CLI from the user's own UU installation/archive. SharpCompress extracts only required content into launcher-owned LOCALAPPDATA\StormHeroesLauncher\Tools\UU. Path traversal/reparse and source/Authenticode validation are required. No CLI redistribution or network CLI download. Existing game/zone/server logic is accepted and must not be guessed or replaced.

Battle.net: confirmed Battle.net-owned transient Qt5151QWindowIcon windows are hidden, never closed; Chrome_WidgetWin_* main windows use Hide-first followed by native WM_CLOSE close-to-tray. Readiness is independent of visibility. Re-enumeration and early/final checks are bounded; no resident monitor.

Heroes launches through Support64\HeroesSwitcher_x64.exe with `-sso=1 -launch -uid heroes`. Detect the game process dynamically; never hardcode a Versions\BaseXXXXX game path or a fixed PID. Do not retrieve runtime authentication material.

Key implementation areas: App.xaml.cs; UuElevationFlow/UuPipeSecurity and WindowHelper; UuCliPreparation; BattleNetService/EarlyBattleNetSuppression/window policy; HeroesProcessService; ShortcutImport; AboutSafetyContent. Preserve the existing launch order and failure handling.

## 7. Shortcut-import onboarding

Drag a UU shortcut, a Battle.net shortcut, or both together onto the main EXE or its desktop shortcut. Valid complete configuration succeeds silently. UU-only with missing counterpart shows the minimal prompt to continue dragging Battle.net; Battle.net-only with missing counterpart prompts for UU. Invalid/partial imports show concise errors. Existing valid counterpart configuration is preserved.

Import never launches games or requests UAC. Resolve local .lnk targets read-only; do not execute imported arguments/scripts/URLs. Keep target identity/signature/installation-structure checks and validated configuration writes. Normal double-click launches; Shift + double-click or --settings opens Settings.

## 8. About / Safety

Creator / Publisher: 阿黄

Contact: 441649289@qq.com

Development assistance: 本启动器在 ChatGPT（含 Codex）协助下开发。

About/Safety covers current features, safety/privacy boundaries, technical implementation, feedback, local logs, and the independent third-party tool disclaimer. It displays assembly version/Build ID. Feedback opens a draft only on explicit user action; no automatic upload or attachment. Logs may include local paths/configuration/extra configured fields: inspect before sharing, and never put credentials into configuration.

Developer Observer builds additionally disclose their read-only metadata collection, separate Observer log directory and bounded background lifetime in Safety. Normal builds retain the original Safety text. No Settings checkbox or UI redesign was introduced.

## 9. Security boundaries

No DLL injection, hooks, UI Automation/MSAA launch actions, process-memory read/write, game-file modification, packet capture/manipulation, simulated game input, UAC bypass, persistent elevated service or hidden scheduled task. No credential/token extraction. Main remains asInvoker; elevation is limited to the narrow helper when needed. No external registry/network/firewall configuration changes. These are implementation boundaries, not a guarantee of anti-cheat compatibility or account safety.

## 10. Current visual behavior

UU and Battle.net startup flashes are reduced to the accepted friend-build level, not guaranteed absent in every frame. Dev.4 explicitly authorizes Heroes preparation suppression following successful direct diagnostics. The shared policy hides only visible ownerless #32770 windows of a proven newly launched game process, with bounded nonfatal SW_HIDE attempts. It does not activate the historical helper-based preparation code. Progress remains at 90% until a visible enabled ownerless non-hung main window of class Heroes of the Storm is stable for 1500 ms and no visible preparation dialog remains. The borderless 340-DIP progress window appears after normal routing, before final validation/UU startup; discovery/CLI preparation for Settings routing remains earlier. Success shows 100% for 350 ms; failure briefly shows Failed before preserving the existing error dialog. Settings/import/About routes never show progress.

## 11. Friend-release accepted behavior

Portable/no installer; no preinstalled .NET; one root user-facing EXE; one double-click; at most one UAC on cold start; warm start may use zero UAC with UU already in tray. User manually configures UU close window -> hide to system tray, and Battle.net click X -> minimize Battle.net to system tray. Existing login/normal environment must be prepared by the user. Accepted manual tests cover the launch chain and friend UX; freeze does not repeat them.

## 12. Known limitations

- Windows 10 x64 manually tested; Windows 11 x64 expected, not manually verified.
- External tray settings are manual; application updates can change external behavior.
- Preparation hiding passed three direct-game tests; full launch/DPI/focus acceptance is still pending. Unknown future main-window classes or a minimized existing game may reach the bounded readiness timeout; the game is left running.
- Settings needs later redesign; neutral temporary icon is not final branding.
- No public-release code-signing/authenticity system; release hashes are integrity references only.
- No multi-accelerator support yet.
- AboutSafetyTests validate 0.3.0-dev.7, version-prefixed Build ID and build-specific Safety text. The existing Safety claims remain accurate for read-only startup metadata. WindowHelper operational code is unchanged; dev.6 rebuilds its HOSLauncher name/metadata. The frozen tag remains unchanged.
- Developer Observer v1 is polling-based; short-lived processes/windows and outside-root updater families without a live parent link may be missed. Inferred update sessions are not updater-confirmed results. Real update-scenario validation is pending.
- Developer observation continues in a separate process for at most two minutes after successful stable-window readiness, reduced by the main session's remaining 15-minute budget, plus up to two seconds teardown. The worker route never acquires the launch mutex, and sessions/files are independent with parentSessionId links. Correlation baselines restart at handoff. Main launcher exits after its brief progress completion and bounded flush/dispatch; warm relaunch is no longer blocked by the tail.

## 13. Future requirements already agreed

- Shortcut/path improvements as needed; optional automatic path discovery controlled from Settings.
- External tray setting detection/configuration only if a safe, stable method exists.
- Battle.net background-authentication experiment: whether HeroesSwitcher wakes fully closed Battle.net.
- Game updates; UU membership/expiration detection; UU update handling.
- Multiple accelerator provider/adapter architecture; node/latency testing.
- Translation later; final progress visual design in Stage 3.
- Configuration migration; reset/default + diagnostics mode; version-update notification.
- Final branded UI/icon.
- Public-release authenticity: Authenticode, SHA-256, Build ID, signed release manifest, installation verification and support report.
- No DRM and no machine-ID upload.

## 14. Important do-not-regress items

Single-UAC architecture; same-user named-pipe security; Hide-first tray behavior; CLI Auto validation and path containment; shortcut-import security; asInvoker main manifest; stable Switcher arguments and dynamic game detection; existing settings/mutex behavior; noninvasive anti-cheat safety boundaries described above. Never broaden pipe ACLs to Everyone or bypass UAC. Do not change accepted runtime behavior as part of packaging.

## 15. Next-stage entry point

Continue Stage 2 in order: Git/GitHub workflow, developer-only update observer, then launch-state model and minimal progress UI. Do not begin all three at once or refactor the accepted launch chain for cleanliness. Read this document and the release record first. Keep the frozen ZIP and tag unchanged; new work gets a new build identity and its own acceptance. Pushes and publication require explicit user instruction.
