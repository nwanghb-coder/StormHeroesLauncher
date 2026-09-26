# Battle.net hidden-start research — dev.7 baseline

Developer-only research; normal launch behavior and version are unchanged. No strategy is integrated.
The user approved up to six bounded runs with identity-scoped cleanup and separately approved a
resumption after the first cleanup stop. Two baseline runs completed. The second run detected live
processes with uncaptured identities during final cleanup, so all remaining real tests were stopped.
Hidden-start effectiveness is NOT established by this research.

## Local evidence

- Installed executable: D:/Blizzard App/Blizzard App01/Battle.net.exe, version 2.53.1.17862;
  Authenticode signature valid, Blizzard Entertainment, Inc.
- Current build battle.net.dll contains AutoStartMinimized, MinimizedOnStartup, the UI identifier
  launchMinimizedOnStartupCheckBox, and the startup-command template `"%1" --autostarted`.
- No matching minimized-start setting was found in the existing Battle.net *.config files; no
  Battle.net entry was found in the inspected HKCU/HKLM Windows Run keys. Only allowlisted setting
  names/boolean values and installed binary strings were inspected; no account/token values logged.
- These strings establish implementation clues, not a verified standalone minimize argument.
  `--autostarted` indicates autostart context, not an unconditional hide/tray contract. Strategy C
  is not confirmed and will not be guessed. Enabling a saved option would require a configuration
  change, so that path is stopped without modifying settings.

## Probe and protocol

Only DeveloperObserver=true compiles `--test-battlenet-start`. It bypasses settings, normal workflow,
UU, Heroes, WindowHelper and elevation. Normal rejects the mode with exit 2. No production version
bump. Standalone research output: artifacts/BattleNet-Hidden-Start-Research/Probe/HOSLauncher.exe.

Arguments: `--test-battlenet-start Baseline|Hidden|StartupInfo <absolute Battle.net.exe> <new absolute JSONL file>`.
No arbitrary extra Battle.net arguments are accepted. Each run refuses any preexisting Battle.net,
Battle.net Launcher, BlizzardError or Agent process. The existing workflow mutex is held by a
dedicated probe thread for the full run, excluding both concurrent probes and normal launch flows.

A is ordinary Process.Start, UseShellExecute=false, no arguments. B1 adds WindowStyle=Hidden.
B2 uses CreateProcessW with an explicit application path, quoted mutable command line, no inherited
handles, and STARTUPINFO.dwFlags=STARTF_USESHOWWINDOW/wShowWindow=SW_HIDE. B2 will be run only if B1
still produces visible windows. .NET 10 already maps B1 to this same documented hint, so B2 is an
independent implementation control, not a fundamentally different hiding mechanism.

Thirty seconds of read-only observation per run, requested delays 10 ms for five seconds then 50 ms.
There is no post-launch Hide, minimize, Close, activation or input during this measurement phase.
The process-return and first-sample gaps are recorded: a window could flash before the first sample.
IsWindowVisible plus non-minimized state defines sampled exposure; concurrent same-class HWNDs are
combined into a union. Durations are sample-held estimates, with right-censoring when still visible.
No observed window does not prove zero-frame exposure. Hidden/minimized may precede later visibility.

Logs contain Strategy, ProcessLaunchT0, FirstQtVisibleT, FirstChromeVisibleT,
FirstHiddenOrMinimizedT, QtVisibleDurationMs, ChromeVisibleDurationMs, ProcessStillRunning,
sample gaps and per-window identity/state transitions. Stable enabled/nonhung/ownerless Chromium
is recorded as a bootstrap proxy only; authenticated login is not read or inferred as proven.

After observation, one WM_CLOSE per verified Chrome window tests the native tray proxy. After three
seconds, hidden Chromium plus a running process is recorded, but tray icon/menu function is untested.
Cleanup then terminates only retained handles with matching PID, creation time, session, executable
path and observed test ancestry. Descendants must be under the installed Battle.net root (allowlisted
names), or the Agent root with proven ancestry. Parent exit/child creation times reject PID reuse.
The final implementation orders the known root before its workers to reduce possible worker respawn;
this ordering change passed offline tests but was not tested again against real Battle.net.
Preexisting/unproven processes are never closed or killed. Ambiguity or incomplete cleanup stops
the comparison set and requires manual review. Battle.net's ordinary runtime writes may still occur;
the probe never edits its configuration/files. Cleanup termination is a research side effect.

## Measurements

| Strategy | Real runs | Qt / Chrome visibility | Bootstrap / tray | Result |
|---|---:|---|---|---|
| A: ordinary Process.Start | 2 | Qt and Chrome visible in both | Bootstrap progression; tray proxy passed in both | Details below |
| B1: ProcessStartInfo.Hidden | 0 | Not measured | Not measured | Stopped by cleanup boundary |
| B2: explicit STARTUPINFO/SW_HIDE | 0 | Not measured | Not measured | Conditional on B1; not reached |
| C: native minimized startup | 0 | Not tested | Not confirmed locally | No configuration changes |

## Validation and recommendation

334 Normal / 424 Observer offline checks passed. Tests cover start plans, native structure/flags,
identity/ancestry/path/session/PID-reuse rejection, sampled union timing/censoring, minimized states,
bootstrap identity replacement, API boundaries and physical exclusion from Normal. Actual executable
checks: Normal rejects the probe, and the research build rejects incomplete arguments, both exit 2.
Two approved baseline runs completed. Do not integrate a hidden-start strategy on this evidence.
Preserve the dev.7 workflow. Managed Hidden and explicit STARTUPINFO express the same Windows hint;
neither has been validated here against Battle.net. Native minimized startup is unconfirmed, not
proved nonexistent. Any future comparison must separately address cleanup-time replacement workers,
retain fail-closed ownership checks, and obtain permission to resume. No zero-flash or authenticated
login claim is justified. No UU, Heroes, WindowHelper or UAC was launched; no Battle.net settings
were edited by the probe.

### Baseline run 1 — 2026-09-27 00:08:44 +08:00

Raw local evidence: artifacts/BattleNet-Hidden-Start-Research/baseline-1.jsonl.
ProcessLaunchT0: 2026-09-26T16:08:44.4728485Z. Process.Start returned after 5.223 ms;
first sample at 27.939 ms. First Qt visible at +2046.439 ms, first Chrome visible at
+9579.784 ms, first hidden Chromium at +1205.408 ms (initial hidden creation, NOT proof
that the later main interface stayed hidden). Qt visible duration 7451.059 ms. Chrome
visible duration 20432.755 ms, right-censored because it remained visible at the end.
Maximum sample gap 94.970 ms. These are sampled union durations, not screen-frame measurements.

Battle.net remained running; Qt transitioned away and visible Chrome appeared. The enabled,
nonhung Chromium proxy stabilized, but login authentication was not inspected. After observation,
WM_CLOSE was accepted and Chrome was no longer visible while Battle.net remained running: tray
proxy passed, tray icon/menu not exercised. Five live test-owned processes were terminated with
confirmed exits; other captured test processes had already exited.

The original final guard reported cleanupComplete=false from a name-only Toolhelp snapshot while
retained identity handles were still open. The exact remaining entries were not logged, so its cause
cannot be established: exited retained processes were initially suspected, but run 2 also shows that
late live workers are possible. Per the agreed boundary, subsequent runs were stopped.
A read-only follow-up found no Battle.net/Agent/BlizzardError processes remaining. The guard now
checks actual process liveness: signaled exited handles do not count as running, access/identity
uncertainty still fails closed. A harmless test child with a retained handle reproduces and covers
the distinction (two additional offline checks). The user then explicitly permitted resumption.

### Baseline run 2 — 2026-09-27 00:12:55 +08:00

Raw local evidence: artifacts/BattleNet-Hidden-Start-Research/baseline-2.jsonl.
ProcessLaunchT0: 2026-09-26T16:12:55.7050824Z. Return gap 5.036 ms; first sample
26.873 ms. First Qt visible +2052.343 ms; first Chrome visible +18264.761 ms;
initial hidden Chromium +1182.530 ms. Qt visible duration 12829.945 ms; Chrome
11756.160 ms, right-censored. Maximum sample gap 87.821 ms.

Battle.net stayed running with Qt-to-Chrome bootstrap progression and the same weak Chromium
readiness proxy; authenticated login remained unverified. Post-observation WM_CLOSE again produced
hidden Chrome with a live Battle.net process. Five captured live processes had confirmed termination;
others had already exited. At the final check, additional Battle.net PIDs 1580 and 13032 were actually
running. They were not in the captured ownership set and were not terminated. Their creation times
and ancestry were not captured, so replacement-worker respawn during teardown is a hypothesis,
not a proven relationship. The read-only follow-up found no remaining family processes.

The batch stopped again, with no Hidden, native STARTUPINFO or native-argument real run. The final
source adds known-parent-first cleanup to reduce a live parent's opportunity to replace workers;
this is an offline-validated mitigation only. No further Battle.net execution followed this stop.

### Sampled comparison (seconds)

| Run | First Qt visible | First Chrome visible | First hidden/minimized | Qt duration | Chrome duration | Running at observation end |
|---|---:|---:|---:|---:|---:|---|
| Baseline 1 | 2.046 | 9.580 | 1.205 | 7.451 | >=20.433, censored | Yes |
| Baseline 2 | 2.052 | 18.265 | 1.183 | 12.830 | >=11.756, censored | Yes |

Initial hidden windows are not evidence of a hidden main UI: both runs later showed Chrome.
Different bootstrap delays and a fixed 30-second horizon make the censored Chrome durations
unsuitable for ranking startup strategies. There are no B/C measurements to compare against A.
Side effects: ordinary Battle.net bootstrap/runtime activity, post-observation native close requests,
and exact-process termination. Unknown late workers were left alone and subsequently disappeared.

References:
- [.NET WindowStyle support with UseShellExecute=false](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/8.0/processstartinfo-windowstyle)
- [.NET 10 Process implementation](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Diagnostics.Process/src/System/Diagnostics/Process.Windows.cs)
- [STARTUPINFOW](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/ns-processthreadsapi-startupinfow)

No push, merge, tag, production integration or UU splash work.
