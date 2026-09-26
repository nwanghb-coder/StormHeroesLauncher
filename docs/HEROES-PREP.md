# Developer Heroes preparation diagnostic — dev.4

Available only in DeveloperObserver=true builds. Normal builds physically exclude the diagnostic
and tail worker, and reject their command-line modes before settings or launch processing.

```powershell
& .\artifacts\Stage2-0.3.0-dev.4-Observer\StormHeroesLauncher.exe --test-heroes-prep 'D:\Blizzard App\Heroes of the Storm\Support64\HeroesSwitcher_x64.exe'
```

This explicit test starts Switcher with **no arguments**, not the normal launch arguments. It does
not invoke UU, Battle.net, the elevated helper, CLI, settings writes or network-environment changes.
Reports are unique JSONL files under `%LOCALAPPDATA%\StormHeroesLauncher\Diagnostics\HeroesPrep\`.
Each records session/time, exact Switcher/game identities, HWND/class/styles/geometry, first detection,
hide requests/confirmation, replacement dialogs, stable main candidates, production readiness and cleanup.
Window titles, command-line contents and process memory are not read. The shared readiness policy
uses documented process metadata and top-level Win32 window enumeration only.

## Ownership and hiding

Before launch, capture existing relevant PIDs. Hold the actual Switcher process handle to obtain
its creation and exit times. A game must be a new direct child of that exact parent lifetime, in
the same session, with the expected executable under the configured installation's Versions subtree.
This conservative direct-child rule was observed in all three tests. Unexpected descendants/identity
ambiguity are reported for manual cleanup; they are not automatically killed or hidden.
Every window action rechecks PID, creation time, session and path. A visible ownerless top-level
`#32770` window in the owned game is the preparation candidate. Use ShowWindowAsync(SW_HIDE),
never WM_CLOSE on preparation dialogs. Each identity/HWND has at most three attempts; tracking is
bounded. Replacement HWNDs are observed independently. Hide rejection/exception is nonfatal.

Production shares the same readiness/suppression policy. Only games proven newly launched by the
current workflow are eligible for hiding. Existing games may be observed but not hidden. Readiness
requires the stable main-window evidence documented in [PROGRESS.md](PROGRESS.md). No preparation
dialog is required; absent/hidden preparation is normal. Hiding never makes process existence ready.

## Test lifetime and cleanup

Observation is bounded to 90 seconds. After a stable main candidate, observe a further ten seconds
for dialog reappearance or abnormal termination. Cleanup is separate from production behavior:
prefer WM_CLOSE on the test-owned real main window, allow two seconds, then terminate only the
originally identified test process through a handle whose identity is revalidated. Confirm its exit.
Pre-existing games, UU, Battle.net and unrelated processes are never cleanup targets. Ambiguous or
inaccessible identities produce manual-cleanup records. Production never terminates game processes.

## Authorized real results — 2026-09-26

User explicitly approved at most three direct Switcher tests and this limited cleanup. Exactly
three were run. No further real application scenarios were launched automatically.

| Run/session | Prep visible | Hide confirmed | Main visible | Stable ready | Game cleanup |
|---|---:|---:|---:|---:|---|
| 1 / 101bc1d176034dc0a80bb45e1895f215 | 3.982 s | 4.103 s | 17.039 s | 18.619 s (discovery) | Exact owned process terminated after graceful attempt |
| 2 / fe8d5a7983054862ab691b0cd2be3900 | 3.338 s | 3.456 s | 16.462 s | 18.073 s (production rule) | Exact owned process terminated after graceful attempt |
| 3 / 0d3e8d6aaa3c4b3a8c582c133c388158 | 3.798 s | 3.937 s | 16.649 s | 18.204 s (shared production hiding/readiness) | Graceful close confirmed |

All three returned exit code 0. Each had one preparation HWND, one accepted hide request, no
replacement/reappearance and no abnormal exit before cleanup. Game remained alive after hiding
and through the ten-second post-readiness observation. Switcher had already exited at cleanup.
The third run explicitly confirmed cleanup completion and no manual-cleanup requirement.

Observed main characteristics: class `Heroes of the Storm`, visible/enabled/ownerless/non-hung,
2560×1440, style 2533883904 (0x97080000), extended style 262144 (0x40000 / WS_EX_APPWINDOW).
Preparation characteristics: class `#32770`, visible/enabled/ownerless, 489×223, style 2496137420,
extended style 65793. Geometry and exact full style values are evidence, not version-specific constants.
The main class and conservative window predicates select readiness; titles/Base paths are not matched.

These observations establish stability on this installed game/desktop only. They do not prove
server login or every future game build/window mode. Full launch-chain, warm relaunch while the tail
is alive, focus and DPI acceptance follow [the manual procedure](PROGRESS.md#manual-acceptance-procedure).
