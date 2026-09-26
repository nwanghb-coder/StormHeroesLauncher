# HOSLauncher Developer Observer — current package 0.3.0-dev.7

Initially implemented in 0.3.0-dev.2. Dev.4 separates the bounded tail into a developer-only
process, freeing the launch mutex for warm relaunch. JSONL schema version remains 1.
The explicit game diagnostic is separate from passive observation: see [HEROES-PREP.md](HEROES-PREP.md).
Dev.5 adds a separate bounded UU cold-start sampler, sharing the JSONL envelope but using an
independent file/session. It never changes windows or labels them as confirmed splashes.
See [polling, fields and acceptance](DEV-5.md#developer-only-startup-window-observation).

This local developer build records passive UU/Battle.net update evidence while the normal
launcher is used. It cannot start, stop, pause, accelerate or configure an update. It adds no
external application writes, elevation, hooks, memory access, packet capture or undocumented IPC.
The accepted launcher still performs its existing launch/CLI/window operations separately.

## Build isolation and artifacts

The MSBuild property `DeveloperObserver=true` defines `DEVELOPER_OBSERVER`. The property defaults
to false. Normal builds remove `Observer/**/*.cs` from compilation, omit the integration calls,
and cannot activate the observer through settings, arguments or a runtime checkbox. An assembly
metadata field records the flag. `Directory.Build.props` separates developer intermediates/output
under `obj/observer/` and `bin/observer/` to prevent incremental-build contamination. Both builds
retain the HOSLauncher product name, asInvoker manifest and portable helper path. No second codebase exists.

From the project root, with the .NET 10 SDK and cached/restorable dependencies:

```powershell
# A. Normal self-contained x64 package; explicitly disables observer.
./tools/Publish-Stage2.ps1
# B. Developer-only self-contained x64 observer package.
./tools/Publish-Stage2.ps1 -DeveloperObserver
```

The script uses the existing Portable publish profiles and verifies the two-EXE package,
including observer type presence/absence inside the actual bundled managed assembly.
It refuses to overwrite populated artifact folders. Outputs:

- `artifacts/Stage2-0.3.0-dev.7-Normal/`
- `artifacts/Stage2-0.3.0-dev.7-Observer/`

Equivalent MSBuild selection is `-p:DeveloperObserver=false` or `-p:DeveloperObserver=true` on
`dotnet build`, `dotnet run` (tests), or `dotnet publish`. Publishing needs `-p:PublishProfile=Portable`
for each executable. No ZIP or public release is created.

Offline tests (fake observation sources only):

```powershell
dotnet restore tests/StormHeroesLauncher.OfflineTests/StormHeroesLauncher.OfflineTests.csproj -p:DeveloperObserver=false -p:NuGetAudit=false --ignore-failed-sources
dotnet run --project tests/StormHeroesLauncher.OfflineTests/StormHeroesLauncher.OfflineTests.csproj -c Release -p:DeveloperObserver=false --no-restore
dotnet restore tests/StormHeroesLauncher.OfflineTests/StormHeroesLauncher.OfflineTests.csproj -p:DeveloperObserver=true -p:NuGetAudit=false --ignore-failed-sources
dotnet run --project tests/StormHeroesLauncher.OfflineTests/StormHeroesLauncher.OfflineTests.csproj -c Release -p:DeveloperObserver=true --no-restore
```

## Lifetime and overhead

Observation starts on a background task immediately before the normal launch workflow, after
configuration validation/CLI preparation. Settings, failed onboarding and shortcut imports do not
start it. A successful workflow confirms a stable main game window (including an already-running game);
observation then continues for at most **120 seconds**, capped at **15 minutes total**. The tail
starts after the existing final window handling completes; no launch steps were reordered.
Launch failure requests immediate stop before showing the existing error dialog.
Shutdown waits at most two seconds for the observer. A stalled native/filesystem call cannot
keep the application alive indefinitely; an abandoned background task may leave an incomplete log.
The observer deadline never cancels the launch workflow or a user's UAC prompt.

At success the main session stops/flushes (at most two seconds), then the same developer executable
starts with --observer-tail. This narrow route runs before mutex acquisition and does not create UI,
write settings, start external apps or elevate. The main launcher releases its mutex and exits without
waiting for the tail. The worker auto-exits after its remaining bounded tail, plus at most two seconds
teardown. No service/task/autorun is installed. Observer startup failure never changes launch success.
Warm relaunch and Settings can proceed while an older worker observes. Separate sessions/files allow
coexistence; ObserverTailStarted records parentSessionId and maximumSeconds. Each file has its own
baseline and summary; update correlation does not persist across the handoff. The 15-minute observation
budget is shared by subtracting elapsed main-session time before dispatching the worker.

- Process and visible-window snapshots: 500 ms delay between completed samples (not a real-time trace).
- File metadata: startup, process transitions with a minimum two-second interval, every ten seconds,
  and final checkpoint. Executable version-resource reads are cached for up to ten seconds.
- At most 4,096 same-session process entries, 128 relevant processes, eight parent-link passes,
  8,192 enumerated windows and 256 relevant windows per sample. Overflow stops observation safely.
- Only two configured local installation roots; at most 64 immediate directory entries each.
  Only numeric UU folders and numeric `Battle.net.*` version folders are inspected below the root.
  No recursion, drive scan, network installation, junction/symlink traversal or directory hashing.
- At most 384 file paths per root/checkpoint. Log budget: 10,000 ordinary events or 15 MiB,
  with reserved terminal records. Hitting the budget stops observation; existing logs are not deleted.

## Collection and limits

Process appearances/disappearances include PID, parent PID, name, image path/directory, creation
FILETIME where readable, Windows session ID, family/relation and in-root file version metadata.
Only the launcher's Windows session is observed. A matching name alone is insufficient: seeds
must live in a configured root. New in-root updater names are automatically visible. Children of
currently observed parents are `UnknownRelated`, with `relatedTo` UU/BattleNet; creation times must
support the parent link. Their outside-root executable metadata is not read. Heroes and the
launcher/helper are excluded. Parent relationships can include non-updater children and are evidence,
not a claim about purpose. Children whose parents disappear between polls may be missed.

The process table is read using [Tool Help process snapshots](https://learn.microsoft.com/en-us/windows/win32/toolhelp/taking-a-snapshot-and-viewing-processes).
Image paths use [QueryFullProcessImageName](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-queryfullprocessimagenamew)
with PROCESS_QUERY_LIMITED_INFORMATION, plus GetProcessTimes and ProcessIdToSessionId.
No process modules, heaps, memory, debug privilege or observer helper IPC are used.

File snapshots contain path, presence/unavailability, size, last-write UTC, file/product version,
company and product name (version resources, **not verified signing-publisher identity**).
The whitelist covers uu_launcher.exe, uu.exe, netease-uu-booster.7z, Battle.net.exe,
Battle.net Launcher.exe, selected version directories and observed in-root executable paths.
Changed or removed paths are recorded. Known roots come only from the configured launcher paths;
separate updater roots (for example an Agent location outside them) are not scanned.

Visible top-level windows are sampled with EnumWindows, GetClassName, IsWindowVisible,
IsWindowEnabled, GetWindow(GW_OWNER) and GetWindowThreadProcessId. Events contain PID/process identity,
HWND, class, visibility/enabled state and owner HWND/PID. `NoLongerObservedVisible` means a window
left the visible set; it does not distinguish hiding from destruction. No titles are read.

**Excluded:** command lines (even shapes in v1), arguments, passwords, tokens, cookies, account/session
values, login credentials, arbitrary window titles, chat/user content, clipboard, process memory,
network traffic, arbitrary external file contents (apart from executable version resources) and
automatic uploads. Paths may contain local usernames.
Access-denied/racy metadata remains null/Unavailable; the observer never elevates to fill gaps.
Fast processes/windows between polls can be missed. File metadata describes disk contents, not loaded
code. Observer failure is reported by exception type only and never fails the game launch.

## Logs and schema

`%LOCALAPPDATA%\StormHeroesLauncher\Observer\observer-YYYY-MM-DD-HHMMSS-<unique-id>.jsonl`

Normal launcher logs remain in their original Logs folder. Developer startup adds `ObserverBuild=True`.
UTF-8 without BOM, independently parseable JSON per line. Schema **1** is defined by
`src/StormHeroesLauncher/Observer/ObserverModels.cs`; event production/correlation is in
`ObserverEngine.cs`. Each record has `schemaVersion`, UTC `timestamp`, monotonic `elapsedMs`,
`source` (UU/BattleNet/UnknownRelated/Observer), `event`, observer-run `sessionId`, and typed `data`.

Events: ObserverStarted, ProcessStarted/ProcessExited, FileSnapshot, InstallationChanged,
InstallationPathObserved, ActiveVersionChanged, WindowChanged, UpdateSession, ObserverFailure,
SessionSummary. Initial processes have `presentAtStart=true`; start/exit times are *observation*
times, not exact OS event times. Process creation FILETIME helps disambiguate PID reuse.

Update sessions are per application, with their own `updateSessionId` in UpdateSession data.
File/version changes or a main-process exit/reappearance within 60 seconds start a candidate.
Helper/window changes alone never start a candidate. Confidence is Low for weak evidence,
Medium for file change + restart, High for version-change evidence. High does not imply success.
`UpdateCompleted` requires version change + restart + main still present + 15 seconds without new
evidence. It is explicitly `inferred=true`, not confirmed by the external updater. Other candidates
end Unresolved after 30 quiet seconds, 120 seconds total, or observation shutdown. Multiple sessions
are supported. No update is triggered to improve confidence.

The terminal SessionSummary includes start/end UTC, monotonic duration, last observed start/end
version points for UU/Battle.net (path + versions, null when unknown), detection/confidence, process
event count, version evidence count and visible-window-class observation count. A file version and
an active-process version may provide two evidence records for the same update. A summary is
best-effort on crash, forced exit, disk loss or stuck OS calls; absence means incomplete evidence.

## Daily developer use

1. Wait for any previous launcher/observer run to finish. Do not overwrite the frozen Friend-0.2 folder.
2. Copy the **entire Observer output folder** to a separate daily-use folder. Keep
   `HOSLauncher.exe` and `app/HOSLauncher.WindowHelper.exe` together.
3. Point your desktop shortcut to that folder's HOSLauncher.exe, or double-click it there.
   Existing LOCALAPPDATA settings are reused; no settings migration or checkbox is required.
4. Use UU/Battle.net normally, including real updates when they occur. The observer does not initiate
   updates. The existing manual tray settings and login requirements still apply.
5. After game detection, allow the two-minute tail to finish. Then inspect the newest JSONL in the
   Observer folder; look for SessionSummary. Avoid opening or sharing a still-growing file.
6. To provide evidence to Codex later, attach the relevant JSONL and describe what you observed and
   approximately when. Review paths for private usernames first; redact locally if needed. Nothing
   is uploaded automatically. Keep the original privately when making a redacted copy.
7. To stop using observation, point the shortcut at the separate Normal package. No persistent
   service, scheduled task or observer setting remains. Existing evidence logs stay available.

Real UU/Battle.net updates, game launches, WindowHelper and UAC are intentionally not exercised by
offline verification. Daily-use evidence is needed to evaluate real updater behavior.

## Initial v1 validation record (0.3.0-dev.2)

SDK 10.0.401; normal suite 251 checks passed; developer suite 309 checks passed.
Both Portable x64 publishes succeeded and passed bundle/runtime/layout/path verification.
The bundled normal assembly contains zero observer types; the developer assembly contains 27.
No real observation source or external launcher was executed during this task.
