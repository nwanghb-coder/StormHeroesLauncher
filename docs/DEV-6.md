# HOSLauncher — Stage 2 dev.6

Dev.6 corrects the overly strict UU warm reuse decision and renames the visible product. It does
not change cold-start architecture, CLI start implementation, Battle.net proxy/suppression, Esc,
Heroes invocation/readiness/hiding, progress layout or observer sampling.

## UU reuse rule

Query the validated CLI for the configured Heroes game ID when UU is running. Reuse only when
the normalized response explicitly identifies that game, isBoosting=true, status=boosting, and
UU remains running. Missing zoneId/serverId is neutral. Each present zone/server ID must match
the configured value. Empty/invalid present values are not treated as absent. No cached result,
node-name inference or guessed region-to-zone mapping is used. There is no configured node ID in
the existing options, so returned node details remain diagnostic rather than an invented constraint.

Wrong/missing game identity, explicit zone/server conflicts, false isBoosting, non-boosting state,
failed query or malformed/ambiguous response use the existing normal fallback. Duplicate game
entries and invalid JSON remain rejected by the existing parser. The CLI start implementation
and arguments are unchanged. A fresh proven reusable status skips both UU ensure/tray work and CLI
start; the normal pre-start readiness check uses the same rule to avoid a redundant start there.

Logs include GameMatch, ZoneEvidence/ServerEvidence (Missing, PresentMatch, PresentMismatch),
Contradiction and Decision=Reuse/Fallback, followed by UU=AlreadyBoosting when reused. They do not
dump raw CLI responses or secrets. When Battle.net also passes its unchanged proxy, progression is
Initializing → StartingHeroes → PreparingHeroes → GameReady (5→80→90→100), with no fake intermediate
stages. Existing Heroes still bypasses all UU/Battle.net actions.

## Product rename boundary

Current executable names are HOSLauncher.exe and app/HOSLauncher.WindowHelper.exe. Assembly/product
metadata and manifest identity are renamed; the helper is rebuilt for the requested name/metadata
change only, with its operational source, privilege level and named-pipe security unchanged.
Helper resolution remains relative to AppContext.BaseDirectory, never CurrentDirectory.

Renamed surfaces: main/Settings/About/import/error window titles; progress product label; About,
Safety and third-party product references; copied support/version heading; feedback subject; startup
product logs; assembly/product metadata; WPF icon assembly resource references; observer worker
executable validation and current product metadata; publisher/verifier and current Stage 2 docs.
Observer self-exclusion recognizes both current and legacy executable names. Sampling is unchanged.
Creator 阿黄, contact 441649289@qq.com and ChatGPT/Codex assistance credit remain unchanged.

Intentionally preserved: repository and E:\CodexProjects\StormHeroesLauncher folder; source project
filenames/namespaces; the launcher mutex and pipe identifiers; all LOCALAPPDATA/StormHeroesLauncher
settings, logs, Observer logs and UU CLI cache paths. There is no migration and no reset of settings.
Old milestone documents and Friend-0.2 files retain their historical identity and hashes.

## Validation and artifacts

Offline suites: 315 Normal / 385 Observer checks. Coverage includes missing/matching optional
metadata reuse, explicit conflicts, wrong/missing game, false/transitioning status, failed/malformed
queries, zero redundant start and skipped stages, product/support identity, window titles, icon
resources, relative helper resolution, retained data paths and observer isolation. No real UU,
Battle.net, Heroes, UAC or helper was run. Native warm acceptance belongs to the user.

Use tools/Publish-Stage2.ps1 (Normal) and tools/Publish-Stage2.ps1 -DeveloperObserver. Both produce
self-contained win-x64 packages with exactly this layout:

```text
HOSLauncher.exe
app/
  HOSLauncher.WindowHelper.exe
```

Outputs: artifacts/Stage2-0.3.0-dev.6-Normal and artifacts/Stage2-0.3.0-dev.6-Observer.
No installer, ZIP, merge, tag or push. Earlier artifact folders are not overwritten.
Both packages passed layout, x64, self-contained runtime, product metadata and embedded-assembly
verification: Normal ObserverTypes=0; Observer ObserverTypes=34. The renamed progress layout was
rendered offscreen and inspected. Frozen friend-0.2 still resolves to 9dda358225aa0a6df1b4ee9f78664f741381ecd9;
the historical ZIP SHA-256 remains A9269BF0ACAB188265290936663117A8712A732EA65E45903AB0A9898EAD1514.

## Exact manual warm-start acceptance

1. Copy the complete desired dev.6 package to a fresh test folder. Point the test shortcut at
   HOSLauncher.exe; retain app/HOSLauncher.WindowHelper.exe beside it. Close older active launcher
   workflows first. Do not move or edit the existing LOCALAPPDATA directory.
2. Leave UU running and visibly accelerating the intended Heroes configuration. Leave Battle.net
   ready in its tray; close Heroes normally. Keep existing login and close-to-tray settings.
3. Launch HOSLauncher. Expect 5→80→90→100, normal game readiness/hiding, and automatic progress close.
   No UU/Battle.net restart or replayed startup stages should occur when both are reusable.
4. Inspect the newest LOCALAPPDATA/StormHeroesLauncher/Logs/launcher-YYYY-MM-DD.log. Confirm a fresh
   CLI status, GameMatch=True, no contradiction, Decision=Reuse, UU=AlreadyBoosting and
   BattleNet=AlreadyReady. ZoneEvidence/ServerEvidence=Missing is an accepted reuse case.
   There must be no subsequent CLI start for this reusable boost, no boosting→starting→boosting
   restart sequence, and no launcher-induced misleading stop notification.
5. Close Heroes normally and repeat once with UU/Battle.net still ready. Then test with Heroes
   already open: no duplicate game and no acceleration change. Do not deliberately switch to an
   unwanted target merely to exercise fallback; offline tests cover those conflicts.
6. Check progress, Settings, About/Safety, imports and copied support text say HOSLauncher. Confirm
   existing settings still load from the old physical data directory. The Observer package should
   retain its independent bounded tail and read-only startup evidence behavior.

The user subsequently reported successful dev.6 warm-start acceptance when requesting dev.7.
The offline results above do not themselves claim a real warm-start run.
