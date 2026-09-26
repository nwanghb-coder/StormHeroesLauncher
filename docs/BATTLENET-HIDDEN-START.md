# Battle.net hidden-start research — dev.7 baseline

## Result

Native follow-up: two approved argument-only `--autostarted` runs also exposed both Qt and Chrome.
Bootstrap remained healthy by the same proxy. Do not integrate the argument; see the native
continuation below for current binary evidence, configuration uncertainties and measurements.

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

Arguments: `--test-battlenet-start Baseline|Hidden|StartupInfo|AutoStarted <absolute Battle.net.exe> <new absolute JSONL file>`.
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

## Initial native Battle.net candidate (before approved follow-up)

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
No native candidate was executed or enabled during that initial comparison. The later approved
argument-only tests are documented below.

## Native investigation continuation — 2026-09-27

Scope: current installed `D:\Blizzard App\Blizzard App01\Battle.net.17862\battle.net.dll`,
version 2.53.1.17862, SHA-256
`79CA9737360A9D4653AC21FAC07FF9E4668691489EC2F812E7DA5FC047B507B8`.
This is an x86 PE. Addresses below are **file offsets**, not live-process addresses.
Only on-disk binary strings and narrow static references were inspected; the DLL was not loaded
or executed for inspection. No process memory, credentials, tokens, configuration edits or registry
writes were used. Findings concern this installed build, not a supported public API contract.

| Clue | Current implementation evidence | Interpretation and limits |
|---|---|---|
| `MinimizedOnStartup` | String at `0x14e5a10`; setting registration at `0x44a215`, stored in settings-object slot 0. Category constructors resolve to `User` → `Client`. Current main-window diagnostics reference `isMinimizedOnStartup=` at `0x45cca3`. | A Battle.net-owned boolean-like startup presentation setting in the logical `User/Client` category. It participates in minimized-start decisions; this does not prove hidden creation, tray placement, or zero Chrome exposure. |
| `AutoStartMinimized` | String at `0x14e59fc`; registration at `0x44a23e`, stored in settings-object slot 4. Its category constructor resolves to root `Client`, distinct from `User/Client`. Both slots are read by the current code around `0x45cd08` and `0x4a6e70`. | A separate minimized-at-autostart preference. Static control flow combines the two setting getters with OR; around `0x4a6e55`, that combined condition is gated by `AutoStarted`. This establishes an autostart-specific decision, not an unconditional hide command. |
| `--autostarted` | `autostarted` at `0x1474b80` and help text “Indicates program was started automatically” at `0x1474b54` are passed together to option registration near `0x4c959`. Parsing references near `0x4eccd` lead to setting the `AutoStarted` object true near `0x4ed15`. That object's getter is read near `0x45cdd3` and `0x4a6e55`. The current DLL also contains the startup command template `"%1" --autostarted` beside Windows Run-key strings. | Credible current recognition and consumption of an automatic-start context flag. The flag is not itself an unconditional minimize-to-tray request; outcome depends on settings and later startup decisions. Actual forwarding through the root launcher and visible-window behavior still require a real test. |

The current UI contains `launchMinimizedOnStartupCheckBox` (string `0x151acd4`, construction
reference `0x6a1f75`) and its clicked-slot metadata. This supports a native startup-minimized UI
preference, but the exact callback-to-setting binding and current checkbox state were not verified.
Registration calls contain zero arguments consistent with defaults, but these are insufficient to
assert the effective setting values. Do not equate a missing saved property with false.

No matching saved startup-minimized value was found in narrow allowlisted checks of:

- `%APPDATA%\Battle.net\Battle.net.config`
- `%APPDATA%\Battle.net\1796798.config`
- `%APPDATA%\Battle.net\46ef9d7b.config`
- Checked HKCU/HKLM Blizzard Entertainment roots, including WOW6432Node. The existing
  `HKLM\SOFTWARE\WOW6432Node\Blizzard Entertainment\Battle.net` key had no matching value.

Consequently, the **logical settings exist in current Battle.net code**, but no exact persisted
file/property or registry value can yet be reported. Current value and serialized data type are
unknown; code consumes boolean-like values. The category paths above are logical registration paths,
not verified JSON paths. Battle.net owns their registration and its UI contains the relevant preference;
restart requirements and whether either is already enabled remain unverified. No configuration test
is justified until the exact storage and restoration procedure are known.

**Approved experiment:** after the user explicitly approved, two separate launches of
`D:\Blizzard App\Blizzard App01\Battle.net.exe --autostarted`, normal startup window style,
without Hidden/SW_HIDE or any configuration change. Observe each for 30 seconds using the existing
read-only developer probe protocol, emit exactly `Observation complete.`, and leave Battle.net running.
Require Battle.net/Agent to be fully stopped before each invocation; the user exits via tray between
runs. No processes were terminated and no automated cleanup occurred. The developer-only probe now
has a fixed `AutoStarted` strategy using exactly this argument; no arbitrary argument forwarding.

Record Qt/Chrome visibility, Chrome first-visible time and sampled duration, process survival and
bootstrap proxy. Hidden/minimized window observations alone cannot prove a usable tray icon or login;
record those as unverified unless separately observed. Compare with the existing baseline and Hidden
results. Retain dev.7 behavior unchanged: native-primary plus Hide-first fallback remains a hypothesis,
not an integration decision. If neither minimized preference is enabled, failure of the argument alone
would not prove the native mechanism absent.

### Approved argument-only results

| Run | First Qt visible (s) | Qt duration (s) | First Chrome visible (s) | Chrome duration (s) | Running / bootstrap proxy |
|---|---:|---:|---:|---:|---|
| AutoStarted 1 | 2.044 | 16.546 | 23.335 | 6.684, censored | Yes / yes |
| AutoStarted 2 | 2.033 | 18.257 | 20.290 | 9.775, censored | Yes / yes |

Both invocations passed the no-running-Battle.net/Agent preflight, exited with code 0 and printed
`Observation complete.`. The user confirmed manual tray exit between runs. The second run left
Battle.net running for the user to exit. Both reports have MetadataComplete=true; no failure or
automatic cleanup was recorded. Window observations contain hidden and visible Qt/Chrome states,
but no sampled minimized state. Chrome remained visible at each deadline. A functional tray icon
was not inspected, and authenticated login remains outside the bootstrap proxy.

Evidence (ignored local artifacts under `artifacts/BattleNet-Hidden-Start-Research/`):

| File | Launch UTC | First sample ms | Max sample gap ms |
|---|---|---:|---:|
| `manual-autostarted-1.jsonl` | 2026-09-26T16:45:24.8823492Z | 26.089 | 94.232 |
| `manual-autostarted-2.jsonl` | 2026-09-26T16:46:36.5064871Z | 25.485 | 81.049 |

**Decision:** `--autostarted` alone did not prevent either Qt or Chrome visibility under the current
preferences. Its later Chrome appearance and shorter censored duration are not demonstrated exposure
improvements: Chrome was still visible, and the 30-second horizon simply ended sooner after appearance.
Do not adopt the argument as the primary production strategy. Hidden remains the tested Qt startup
hint; explicit STARTUPINFO offered no observed benefit, and Chrome still requires the current dev.7
post-launch handling if suppression is desired. No production changes were made.

Further native-minimized investigation is justified only to identify the actual Battle.net-owned
preference, its effective value and exact persistence/restoration path. The current result does not
disprove a native minimized-start mechanism when correctly configured. Do not change settings or
combine strategies without a separately approved experiment. No additional real test is scheduled.

334 Normal / 425 Observer offline checks passed after adding the fixed argument-only strategy; the
Observer check verifies normal window style, exactly one argument and no shell/elevation. Internal
option semantics, defaults, UI bindings and forwarding can change with Battle.net versions. Two runs
do not establish broad compatibility or zero-frame exposure. This continuation used local evidence
only; the API references below belong to the earlier Hidden/SW_HIDE comparison.

## Read-only preference discovery — 2026-09-27

This follow-up performed no application launch or real test, no Battle.net/configuration/registry
writes, no process-memory access and no UI automation. Only this research document was edited.
The developer-probe/test changes already in the working tree belong to the preceding approved tests.
The current DLL fingerprint remains the 2.53.1.17862 SHA-256 recorded above.

### Storage and current values

| Candidate | Exact observed storage / current value / type | Current-build and UI evidence | Semantics, restart and confidence |
|---|---|---|---|
| `MinimizedOnStartup` | No saved property found in the three Roaming configuration files listed below or checked registry roots. Effective value and persisted type **unknown**. `User/Client` is the code's logical category, not a verified property in an active file. | Current setting registration and getters described above. Current UI has a startup-minimized checkbox with an explicit tray-start label; the exact callback binding to this setting versus `AutoStartMinimized` is not yet established. | Participates in startup presentation. Boolean-like code consumption; no proof that it unconditionally hides Qt or Chrome. High confidence in current code presence, low confidence in effective configuration. A fresh launch would be needed to measure startup effects; whether a UI change requires a restart to persist or become active is unknown. |
| `AutoStartMinimized` | No saved property found in the same inspected stores. Effective value and persisted type **unknown**. Root `Client` is the logical category only. | Current registration and getters participate in the `AutoStarted`-gated decision. The current binary includes the autostart command template and the separate startup-minimized UI control. | Auto-start-context presentation preference; it is not a general close-to-tray setting. `--autostarted` alone exposed both Qt and Chrome twice. High confidence in code presence and gating; effective value, storage binding and restart requirement remain unverified. |
| `HideOnClose` | **`C:\Users\nwang\AppData\Roaming\Battle.net\1796798.config`**, JSON path **`User/Client/HideOnClose`**, current saved value **`"true"`**, JSON **string**, not JSON boolean. | Current DLL string at file offset `0x14e5a4c`, registration reference `0x44a2ea` under the same `User/Client` category, stored in settings slot `0x14`. Current code/UI also contains close-behavior controls and tray-on-exit wording. | Strong evidence for closing a window to tray rather than exiting; **not evidence of minimized startup**. High confidence in the stored value, medium confidence in UI correspondence. The active-profile association was not established, so this is not a claim about the currently effective preference. Whether a restart is required is unknown. |

Configuration scope (selective setting-name/value inspection, no credential/token values inspected
or reported):

- `C:\Users\nwang\AppData\Roaming\Battle.net\Battle.net.config`: no matching startup/minimized/close-to-tray setting.
- `C:\Users\nwang\AppData\Roaming\Battle.net\1796798.config`: the single matching saved setting was `User/Client/HideOnClose="true"`.
- `C:\Users\nwang\AppData\Roaming\Battle.net\46ef9d7b.config`: no matching setting.

The check included nested non-authentication preference objects and directly related names containing
minimize/startup/autostart/hide-on-close/close-to-tray/exit-on-close. A missing property does not establish
false: defaults, another profile or another store may supply a value. No account identity was inferred
from either filename.

Registry checks covered the HKCU/HKLM `Software\Blizzard Entertainment` roots and their WOW6432Node
variants. Only `HKLM\SOFTWARE\WOW6432Node\Blizzard Entertainment` existed in the checked context;
ten keys including the root were enumerated, with no matching preference value. This is a bounded
negative result, not a claim that no possible registry/store location exists.

Directory metadata checks covered the Roaming, Local and ProgramData Battle.net roots and the current
installation root/version directory. No additional top-level preference `.config` was found in the
Local/ProgramData roots or current version directory. Local `CachedData.db`, account databases,
browser caches and shared memory were **not opened**: their relevance to these preferences was not
established, and opening account/browser stores would broaden the task toward sensitive data.

### Stronger UI and store correlation from the current binary

- The existing `launchMinimizedOnStartupCheckBox` is stored at UI-object offset `0x98` during
  construction. Current code at file offset `0x6a3e76` uses that same field when assigning the
  compiled English label **“Launch Battle.net minimized to the system tray”** (string `0x151b100`).
  This establishes the control's intended UI meaning beyond guessing from its name. It does not
  establish its checked state, localized presentation, callback persistence target or actual outcome.
- The current settings metaobject includes `on_closeBehaviorComboBox_activated`, separately from
  `on_launchMinimizedOnStartupCheckBox_clicked`. The string **“Minimize Battle.net to the system tray”**
  at `0x151b628` has code references at `0x6a4d07` and `0x6a4e85`. **“On exit, minimize to system tray”**
  at `0x14e8104` has a current code reference at `0x45b45c`. These support distinct close/tray behavior;
  they do not prove that every tray-related control writes `HideOnClose`.
- Current binary configuration-loader strings include `Battle.net.config` (code reference
  `0xdf9f27`), separate user and side-by-side configuration containers, and user-config migration
  between identifier schemes. This supports multiple Battle.net-owned configuration sources, but
  does not identify which user file currently supplies startup preferences. No identity mapping was
  attempted.

### Decision

**No verified active, persisted startup-minimized preference with a known current value was identified
in the inspected sources.** This is not evidence that the native preference is absent or legacy:
the current build has an explicit tray-start UI and current setting reads. `HideOnClose` is a concrete
saved close-behavior preference, not a suitable substitute for either startup setting.

The two approved `--autostarted` runs remain the runtime evidence: both exposed Qt and Chrome,
neither sampled a minimized state, and both met the bootstrap-health proxy. Do not integrate the
argument or change normal HOSLauncher behavior.

A controlled **configuration-edit** test is not yet justified: exact active storage, effective original
value and restoration semantics for the startup preference remain unknown. The next useful evidence
would be a user-supplied observation of the existing startup-minimized checkbox state, without changing
it, or a narrowly scoped static trace of its persistence callback. No further launch, setting change
or real test is authorized or scheduled by this read-only follow-up. Internal settings remain
version-specific, and any later experiment needs separate explicit approval.

## API interpretation and validation (earlier Hidden/SW_HIDE comparison)

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
