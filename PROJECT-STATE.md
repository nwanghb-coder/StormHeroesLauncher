# StormHeroesLauncher Project State

## 1. Current milestone

Friend Release is complete and frozen: end of major Stage 1. Trusted friend test only, not a public production release. Accepted baseline: Friend v17 portable. Frozen package: StormHeroesLauncher-Friend-0.2; application version 0.2.0-friend-test; Build ID friend-0.2-release-9e7651132d9a. Freeze changes only main assembly Build ID metadata; the accepted helper is unchanged byte-for-byte. Manual runtime acceptance was supplied by the user; release freeze used static checks only.

Release record: artifacts/Friend-0.2-Release/RELEASE-INFO.txt. Treat the ZIP and recorded hashes as the frozen artifact; a future rebuild is not automatically the same artifact.

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

## 9. Security boundaries

No DLL injection, hooks, UI Automation/MSAA launch actions, process-memory read/write, game-file modification, packet capture/manipulation, simulated game input, UAC bypass, persistent elevated service or hidden scheduled task. No credential/token extraction. Main remains asInvoker; elevation is limited to the narrow helper when needed. No external registry/network/firewall configuration changes. These are implementation boundaries, not a guarantee of anti-cheat compatibility or account safety.

## 10. Current visual behavior

UU and Battle.net startup flashes are reduced to the accepted friend-build level, not guaranteed absent in every frame. Heroes' preparing-game-data window intentionally remains visible as feedback during the long wait before the game appears. Earlier preparation-window suppression code is not active in the launch flow. Progress UI is deferred. Do not silently re-enable suppression.

## 11. Friend-release accepted behavior

Portable/no installer; no preinstalled .NET; one root user-facing EXE; one double-click; at most one UAC on cold start; warm start may use zero UAC with UU already in tray. User manually configures UU close window -> hide to system tray, and Battle.net click X -> minimize Battle.net to system tray. Existing login/normal environment must be prepared by the user. Accepted manual tests cover the launch chain and friend UX; freeze does not repeat them.

## 12. Known limitations

- Windows 10 x64 manually tested; Windows 11 x64 expected, not manually verified.
- External tray settings are manual; application updates can change external behavior.
- Heroes preparation dialog remains visible; progress UI deferred.
- Settings needs later redesign; neutral temporary icon is not final branding.
- No public-release code-signing/authenticity system; release hashes are integrity references only.
- No multi-accelerator support yet.
- Minor developer-only issue: AboutSafetyTests.cs still expects the old friend-v17-portable Build ID prefix. It was intentionally not changed or run during the freeze. Update that metadata expectation before the next full offline test run; it does not affect the runtime package.

## 13. Future requirements already agreed

- Shortcut/path improvements as needed; optional automatic path discovery controlled from Settings.
- External tray setting detection/configuration only if a safe, stable method exists.
- Battle.net background-authentication experiment: whether HeroesSwitcher wakes fully closed Battle.net.
- Game updates; UU membership/expiration detection; UU update handling.
- Multiple accelerator provider/adapter architecture; node/latency testing.
- Translation later; progress UI later.
- Configuration migration; reset/default + diagnostics mode; version-update notification.
- Final branded UI/icon.
- Public-release authenticity: Authenticode, SHA-256, Build ID, signed release manifest, installation verification and support report.
- No DRM and no machine-ID upload.

## 14. Important do-not-regress items

Single-UAC architecture; same-user named-pipe security; Hide-first tray behavior; CLI Auto validation and path containment; shortcut-import security; asInvoker main manifest; stable Switcher arguments and dynamic game detection; existing settings/mutex behavior; noninvasive anti-cheat safety boundaries described above. Never broaden pipe ACLs to Everyone or bypass UAC. Do not change accepted runtime behavior as part of packaging.

## 15. Next-stage entry point

Begin the next conversation with Stage 2 / Feature Complete and an explicitly chosen requirement, not a refactor of the frozen friend-release launch chain. Read this document and RELEASE-INFO.txt first. Keep the frozen ZIP unchanged; new work gets a new build identity and its own acceptance.