# Stage 2 — Feature Complete

Development branch: `stage2-feature-complete`. Current version: `0.3.0-dev.5` (entry baseline was `0.3.0-dev.1`).
Use sequential SemVer development versions, then alpha/beta/rc/stable when appropriate.
Keep the executable name `StormHeroesLauncher.exe`.

## Work order

1. Git/GitHub workflow: preserve the frozen tag and release record, use focused local commits and reviewable branches, then explicitly authorized synchronization, issues and releases. No automatic push or publication.
2. Developer-only UU/Battle.net update observer: design before implementation. Use a separate build configuration or compile-time feature gate that excludes the capability from normal releases; never depend on remembering to remove it. Read-only JSONL may record version/file/path changes, process-tree and updater/helper lifetimes, visible top-level window classes, and observation timestamps/durations. Observe argument shapes only where safely available and redact values; never collect authentication material. Distinguish observed facts from inferred update stages. No process-memory access, hooks, packet capture or external-app modification. Check About/Safety wording before enabling this behavior.
3. Define a launch-state model independent of visual widgets, then a compact, replaceable progress UI. Candidate states: Initializing, StartingUU, PreparingUU, Boosting, StartingBattleNet, WaitingForBattleNet, StartingHeroes, PreparingHeroes, GameReady, Failed. Define readiness evidence and failure transitions before connecting UI. Keep the Heroes preparing-game-data window visible until equivalent feedback is reliable. Final visual design belongs to Stage 3.

Current workstream: **dev.5 warm Fast Path, cooperative Esc and read-only UU startup evidence**.
See [DEV-5.md](DEV-5.md) for exact decisions, compatibility limits, offline checks and manual tests.
Dev.4 is the broadly accepted manual baseline. See [PROGRESS.md](PROGRESS.md)
for the borderless layout, stable main-window readiness, state mapping and manual acceptance.
Authorized direct Switcher tests established preparation hiding before normal workflow integration;
see [HEROES-PREP.md](HEROES-PREP.md). No final Stage 3 styling was started.
The [Observer tail](OBSERVER.md) runs separately without the workflow mutex; normal builds exclude
all developer worker/diagnostic code. Dev.5 offline checks: 303 normal / 373 Observer. New real
Fast Path, Esc and startup-window evidence acceptance remains manual. No splash hiding is implemented.

Git/GitHub baseline synchronization and the frozen Friend-0.2 pre-release are complete.
Observer and progress implementations are local commits only; do not push without explicit instruction.

## Preserved architecture

Preserve single-UAC behavior, same-user pipe security, narrow WindowHelper elevation,
main asInvoker manifest, CLI Auto validation, SharpCompress 0.50.4, Battle.net Hide-first/tray-second,
shortcut security, mutex protection, LOCALAPPDATA storage and portable path resolution.
HeroesSwitcher arguments remain `-sso=1 -launch -uid heroes`.
Do not refactor the accepted launch chain merely for cleanliness.

No injection, hooks (including CBT/WinEvent), UI Automation/MSAA launch actions,
process-memory access, game-file changes, packet manipulation, simulated game input,
credential/token extraction, UAC bypass, persistent elevated services, hidden scheduled tasks,
or unreviewed undocumented IPC. Stop and report before implementing a feature that crosses these boundaries.

## Deferred Stage 2 requirements

- Optional automatic path discovery enabled from Settings.
- Safe, stable UU/Battle.net tray-setting detection/configuration.
- Fully closed Battle.net authentication experiment using HeroesSwitcher.
- Game updates, UU updater handling and membership expiration/unpaid-state detection.
- Multiple accelerator Provider/Adapter architecture and node/latency testing.
- Translation, configuration migration, restore defaults and diagnostics mode.
- Version update notification and support/reporting improvements.

## Later public release work

Do not implement Authenticode signing, signed release manifests, installation integrity verification,
full support reports, final branded icon/UI or DRM without a specific request.
No machine-ID upload or mandatory online license verification.

Any technical behavior change must be checked against About/Safety claims.
