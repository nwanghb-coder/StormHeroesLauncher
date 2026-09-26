# StormHeroesLauncher Project State

## 1. Current milestone

Stage 2 / Feature Complete is active on `stage2-feature-complete`, currently `0.3.0-dev.3`. Minimal Progress UI v1 is implemented in both builds, backed by a WPF-independent semantic state model. Developer Observer v1 remains behind `DeveloperObserver=true`; normal builds physically exclude observer code. See docs/PROGRESS.md, docs/OBSERVER.md, docs/STAGE-2.md and docs/GIT-WORKFLOW.md. Final Stage 3 visual design has not started. Subsequent development builds use 0.3.0-dev.4, etc., followed when appropriate by alpha, beta, rc and stable. New development does not use Friend naming.

Progress UI v1 validation (2026-09-26): 274 normal and 332 observer offline checks passed. Offscreen WPF rendering and dispatcher/lifecycle checks passed. Both self-contained x64 portable packages passed static validation (normal ObserverTypes=0; developer ObserverTypes=27). Outputs: artifacts/Stage2-0.3.0-dev.3-Normal and artifacts/Stage2-0.3.0-dev.3-Observer. No real UU/Battle.net/game/WindowHelper/UAC was run. No push, tag, merge or public ZIP for this work; manual acceptance remains pending.

Observer v1 validation (2026-09-26): 251 normal and 309 observer offline checks passed using fake observation sources. Both self-contained x64 portable packages passed verification; packaged normal assembly has zero observer types, developer assembly has 27. Outputs: artifacts/Stage2-0.3.0-dev.2-Normal and artifacts/Stage2-0.3.0-dev.2-Observer. No real UU/Battle.net/game, WindowHelper or UAC was run. Source changes are local only; no push, milestone tag or new release for this work.

Git/GitHub synchronization was completed after the entry inspection: main and friend-0.2 remain at 9dda358225aa0a6df1b4ee9f78664f741381ecd9; origin/stage2-feature-complete was synchronized at 778fdd7. The private repository's frozen Friend-0.2 pre-release was published using the original ZIP and RELEASE-INFO.txt. These historical assets are unchanged.

Stage 2 entry validation (2026-09-26): SDK 10.0.401; Release x64 solution build passed with zero warnings/errors; all 249 offline tests passed, including updated About/Safety identity checks. No real UU/Battle.net/game launch, UAC acceptance or new portable package was performed. Safety behavior and published Safety claims are unchanged. No push or publication was performed; remote state remains unverified after the initial GitHub connectivity failure.

The following release description is historical:

Friend Release is complete and frozen: end of major Stage 1. Trusted friend test only, not a public production release. Accepted baseline: Friend v17 portable. Frozen package: StormHeroesLauncher-Friend-0.2; application version 0.2.0-friend-test; Build ID friend-0.2-release-9e7651132d9a. Freeze changes only main assembly Build ID metadata; the accepted helper is unchanged byte-for-byte. Manual runtime acceptance was supplied by the user; release freeze used static checks only.

Release record: artifacts/Friend-0.2-Release/RELEASE-INFO.txt. Treat the ZIP and recorded hashes as the frozen artifact; a future rebuild is not automatically the same artifact.

Tracked release-record copy: docs/releases/friend-0.2.txt. Frozen source tag: friend-0.2 at 9dda358225aa0a6df1b4ee9f78664f741381ecd9. At Stage 2 entry, the ZIP and both executable hashes matched the record. Binaries remain outside Git and must be preserved separately.

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

## 5. Current portable layout

```text
StormHeroesLauncher.exe
app/
  StormHeroesLauncher.WindowHelper.exe
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

UU and Battle.net startup flashes are reduced to the accepted friend-build level, not guaranteed absent in every frame. Heroes' preparing-game-data window intentionally remains visible alongside the new compact progress window. Earlier preparation-window suppression code is not active in the launch flow. Do not silently re-enable suppression. The progress window appears after routing confirms normal launch, before final validation/UU startup; discovery/CLI preparation used to decide Settings routing remains before the window. It reports fixed stage percentages, briefly shows completion for 350 ms, and closes before the developer observer tail ends. Failure briefly shows Failed before closing and preserving the existing error dialog. Settings/import/About routes never show progress.

## 11. Friend-release accepted behavior

Portable/no installer; no preinstalled .NET; one root user-facing EXE; one double-click; at most one UAC on cold start; warm start may use zero UAC with UU already in tray. User manually configures UU close window -> hide to system tray, and Battle.net click X -> minimize Battle.net to system tray. Existing login/normal environment must be prepared by the user. Accepted manual tests cover the launch chain and friend UX; freeze does not repeat them.

## 12. Known limitations

- Windows 10 x64 manually tested; Windows 11 x64 expected, not manually verified.
- External tray settings are manual; application updates can change external behavior.
- Heroes preparation dialog remains visible; new progress UI still needs real launch/DPI/focus acceptance.
- Settings needs later redesign; neutral temporary icon is not final branding.
- No public-release code-signing/authenticity system; release hashes are integrity references only.
- No multi-accelerator support yet.
- AboutSafetyTests now validate 0.3.0-dev.3, version-prefixed Build ID and unchanged build-specific Safety text. The historical test remains unchanged on the frozen tag.
- Developer Observer v1 is polling-based; short-lived processes/windows and outside-root updater families without a live parent link may be missed. Inferred update sessions are not updater-confirmed results. Real update-scenario validation is pending.
- Developer observation lasts two minutes after successful launch workflow completion (confirmed game presence), at most 15 minutes total, plus up to two seconds teardown. The existing launch mutex remains held during the tail, so repeat launcher/settings invocations exit as duplicates until it finishes. Progress UI closes independently; normal builds exit after the brief completion display.

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
