# Battle.net hidden-start research — dev.7 baseline

## Result

On installed Battle.net 2.53.1.17862, ProcessStartInfo.WindowStyle=Hidden prevented **sampled Qt
visibility in both runs**, but **did not prevent Chrome visibility**. Explicit STARTUPINFO/SW_HIDE
produced the same qualitative outcome in both runs. All four runs observed a Qt window in its hidden
state, later visible Chrome, a stable nonhung/enabled Chromium bootstrap proxy, and a running
Battle.net process at the 30-second deadline. No authenticated-login or exact zero-frame claim is made.

Use managed Hidden as the simpler candidate for a future Qt-only improvement; explicit CreateProcess
showed no demonstrated benefit. Neither strategy solves the Chrome main-window exposure. Preserve
the current normal workflow until a separate integration request. Further native tray-on-start
investigation is justified because Windows startup hints did not keep the main Chrome UI hidden.

## Current protocol: observation only, manual exit

Only DeveloperObserver=true compiles the research mode. Normal rejects it with exit 2.
The current probe contains no window-close, hide/minimize or process-termination APIs. No automatic
cleanup or process ownership graph remains. The old cleanup code was removed, not merely disabled.

Arguments: `--test-battlenet-start Hidden|StartupInfo <absolute Battle.net.exe> <new absolute JSONL file>`.
Baseline remains available for research, but no additional baseline was launched in this batch.
Current standalone artifact: artifacts/BattleNet-Hidden-Start-Research/ManualProbe/HOSLauncher.exe.

Each invocation:
1. Takes the existing launcher mutex to exclude simultaneous normal launch/probe activity.
2. Refuses to launch if Battle.net/Agent (or another guarded Battle.net-family process) is still alive,
   printing: `Please exit Battle.net from the system tray and wait for Agent to stop before testing again.`
3. Starts one strategy and observes for 30 seconds, requesting 10 ms delays for five seconds and 50 ms
   thereafter. It performs no post-launch window or process action.
4. Records results, prints exactly `Observation complete.`, releases only its own inspection handles,
   and exits. Battle.net is left running; the user exits it through the system tray.
5. The next invocation independently verifies the stopped state.

The user confirmed manual exit between all four tests; each following preflight passed. All four
probe processes exited successfully with automaticCleanup=false. Battle.net remained running at
each observation endpoint. No UU, Heroes, WindowHelper or UAC was launched. The probe did not edit
Battle.net files, configuration or registry. Ordinary Battle.net runtime activity is not disabled.

Observation selects same-session processes using executable name, installation root and creation
time, with PID/creation identity revalidation for window sampling. It does not assert cleanup
ownership. Unavailable metadata is recorded as incomplete observation instead of triggering cleanup.

## Measured comparison

All values below are seconds relative to ProcessLaunchT0. Durations are sampled unions across
same-class windows; simultaneous windows do not multiply exposure. All Chrome durations are
right-censored: Chrome was still visible when observation ended.

| Strategy / run | First Qt visible | Qt duration | First Chrome visible | Chrome duration through deadline | Running / bootstrap proxy |
|---|---:|---:|---:|---:|---|
| Historical baseline 1 | 2.046 | 7.451 | 9.580 | 20.433, censored | Yes / yes |
| Historical baseline 2 | 2.052 | 12.830 | 18.265 | 11.756, censored | Yes / yes |
| WindowStyle.Hidden 1 | Not sampled | 0 | 9.886 | 20.134, censored | Yes / yes |
| WindowStyle.Hidden 2 | Not sampled | 0 | 8.657 | 21.400, censored | Yes / yes |
| STARTUPINFO/SW_HIDE 1 | Not sampled | 0 | 10.749 | 19.261, censored | Yes / yes |
| STARTUPINFO/SW_HIDE 2 | Not sampled | 0 | 9.861 | 20.222, censored | Yes / yes |

Initial hidden Chromium appeared around 1.16–1.20 seconds in all four new runs. This was initial
hidden creation, not proof that the later main UI would remain hidden. Qt was sampled hidden in
all four runs. There was no observed bootstrap failure; neither login authentication nor actual
tray icon/menu function was inspected. The new protocol intentionally performs no tray-close test.

A fixed observation horizon makes later Chrome appearance produce a shorter censored duration;
that is not evidence of a better hiding strategy. Two runs per strategy are insufficient for
statistical performance claims or broad version/Windows compatibility guarantees.

## Timing evidence and limitations

Raw files are under artifacts/BattleNet-Hidden-Start-Research/ (ignored local research artifacts).
The JSONL records include Strategy, ProcessLaunchT0, FirstQtVisibleT, FirstChromeVisibleT,
FirstHiddenOrMinimizedT, QtVisibleDurationMs, ChromeVisibleDurationMs, ProcessStillRunning,
StableChromiumProxy, per-window state transitions, sample gaps and MetadataComplete.

| File | ProcessLaunchT0 (UTC) | Return gap ms | First sample ms | First hidden ms | Max sample gap ms | Metadata complete |
|---|---|---:|---:|---:|---:|---|
| manual-hidden-1.jsonl | 2026-09-26T16:25:58.8113194Z | 4.085 | 26.248 | 1182.971 | 97.663 | Yes |
| manual-hidden-2.jsonl | 2026-09-26T16:28:05.3907994Z | 3.613 | 24.518 | 1163.124 | 89.581 | Yes |
| manual-startupinfo-1.jsonl | 2026-09-26T16:29:46.3166945Z | 4.058 | 26.838 | 1199.325 | 88.567 | Yes |
| manual-startupinfo-2.jsonl | 2026-09-26T16:31:11.7459207Z | 3.898 | 25.645 | 1174.431 | 102.438 | No: one Agent metadata sample unavailable |

Local time was 2026-09-27, UTC+08:00. The final run logged unavailable metadata for Agent.exe PID
34080 around 0.804 seconds; its cause was not determined. The known Battle.net root's Qt window was
subsequently sampled hidden at 2.006 seconds, and Chrome/health results were recorded normally.
Keep the incomplete-metadata qualification for that run.

The launch-to-first-sample gap and polling intervals can miss brief transitions. These are
request/sample measurements, not physical screen-frame exposure. Even four hidden Qt observations
cannot mathematically prove zero visible frames. The bootstrap proxy can be satisfied by a hidden
Chromium window and is deliberately weaker than successful authenticated login.

Historical raw baselines: baseline-1.jsonl (launch 2026-09-26T16:08:44.4728485Z) and
baseline-2.jsonl (launch 2026-09-26T16:12:55.7050824Z). Their observation phase had no suppression;
automatic close/termination occurred only afterward under the earlier authorization. Cleanup guards
stopped those batches; the second captured late live PIDs not in its ownership set, left untouched,
and later absent on read-only follow-up. No hidden strategies were run in that old batch.
Those cleanup difficulties motivated the user's new protocol and do not apply to the current probe.

## Native Battle.net candidate: report only

The installed executable has a valid Blizzard Entertainment, Inc. Authenticode signature and version
2.53.1.17862. Read-only inspection of the installed Battle.net.17862/battle.net.dll found:

- AutoStartMinimized / MinimizedOnStartup
- launchMinimizedOnStartupCheckBox / on_launchMinimizedOnStartupCheckBox_clicked
- the startup command template `"%1" --autostarted`

These are credible evidence of a native minimized-start setting/UI and an autostart context. They do
not establish that `--autostarted` alone means minimize-to-tray. Read-only checks found no saved
allowlisted minimized-start boolean in the current local Battle.net *.config files, and the prior
inspection found no Battle.net entry in the HKCU/HKLM Windows Run keys. Only allowlisted setting
names/boolean values and installed binary strings were output, never account/token values.

A stable working native tray-start mechanism is **not yet confirmed**, rather than proved absent.
Further investigation of the existing native setting is worthwhile specifically for Chrome.
Do not change configuration or run candidate native arguments without a new explicit approval.
No native candidate was executed or enabled in this research.

## API interpretation and validation

Managed Hidden uses Process.Start with UseShellExecute=false, the normal working directory and no
arguments. .NET 10 maps this to STARTF_USESHOWWINDOW/SW_HIDE. The explicit test uses CreateProcessW,
an exact application path, quoted mutable command line, no inherited handles and the same show hint.
Both are documented startup requests that Battle.net may later override; no extra privilege is used.

334 Normal / 424 Observer offline checks passed. Coverage includes physical Normal exclusion,
start plans, native x64 STARTUPINFO layout/flags, observation scope, union durations/censoring,
bootstrap identity replacement, exact completion text, and an API check rejecting termination,
window messaging, hooks, input simulation and process-memory reads in the probe. The read-only
preflight distinguishes a live process from an exited process whose inspection handle remains open.

Normal launch services, App routing, version, WindowHelper, frozen artifacts and tags are unchanged
by this continuation. No production integration, push, merge, tag or native configuration change.

References:
- [.NET WindowStyle support with UseShellExecute=false](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/8.0/processstartinfo-windowstyle)
- [.NET 10 Process implementation](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Diagnostics.Process/src/System/Diagnostics/Process.Windows.cs)
- [STARTUPINFOW](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/ns-processthreadsapi-startupinfow)
