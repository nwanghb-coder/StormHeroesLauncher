# Alpha 1 candidate acceptance gate

No GitHub release, tag, push or merge is authorized by preparation. Publish only as a pre-release
after the user completes acceptance of the exact candidate hashes in PUBLIC-RELEASE-INFO.txt.

## Manual runtime checklist (user-operated only)

1. Verify ZIP SHA-256, fully extract to a new writable folder (including a path with spaces/non-ASCII),
   and confirm only HOSLauncher.exe and app/HOSLauncher.WindowHelper.exe are present.
2. On Windows 10 x64, preferably also a clean machine without installed .NET, check the icon and open
   Settings using Shift-launch. Confirm HOSLauncher, 0.3.0-alpha.1, the recorded Build ID, publisher,
   contact and Safety text. Do not describe Windows 11 as verified until separately tested.
3. With a safe backup of existing launcher settings, exercise first-use shortcut drop import for UU
   and Battle.net, Settings save/reopen and CLI Auto. Confirm CLI is discovered/prepared locally and
   settings/log/cache remain under %LOCALAPPDATA%\StormHeroesLauncher. Do not share unreviewed logs.
4. Enable the documented UU/Battle.net tray-close preferences and log in manually. Cold start from
   fully stopped applications: at most one standard UAC; UU starts, accelerates the correct game and
   reaches tray; Battle.net launches; Heroes launches. Check actual acceleration and game readiness.
5. Observe Battle.net cold-start suppression and Heroes preparation-dialog suppression. Record any
   remaining flash, stuck hidden/login/update window or inability to restore from tray. Verify tray
   restoration and manual exit work. A visible login/update requirement must remain recoverable.
6. Warm start with UU already accelerating Heroes and Battle.net ready: verify reuse without duplicate
   clients or redundant acceleration restart; skipped phases and progress remain meaningful.
7. Start with Heroes already running: verify no duplicate game or unnecessary UU/Battle.net actions.
8. Check semantic progress through cold and warm paths. Press Esc early and while waiting: later steps
   stop promptly, status is cancelled, already-issued programs/acceleration remain intact. Retry afterward.
9. Cancel UAC and test an invalid/missing configured path: clear failure, no unintended later launches,
   no stuck workflow lock. Restore configuration through Settings afterward.
10. Launch through a shortcut with a different working directory: helper resolves relative to the EXE,
    settings/logs still use LOCALAPPDATA, no dependence on the extraction folder being current.
11. Verify About copy-version, open-log-folder and feedback draft actions. No automatic log upload or
    mail sending. Confirm the Normal launcher exits after the workflow and starts no Observer worker.
12. Record OS, exact Build ID, pass/fail for each item and reproducible issues. Any functional failure
    blocks publication until reviewed; do not reuse a failed candidate's hashes for a rebuilt package.

## Preparation scope

Candidate prepared 2026-09-27 (local time):

- Version: `0.3.0-alpha.1`; Build ID: `0.3.0-alpha.1-c59e3d215658`.
- ZIP: `artifacts/HOSLauncher-0.3.0-alpha.1-win-x64.zip`, 91,623,875 bytes.
- ZIP SHA-256: `FC617096EC8C64221C6EBF47368888754C0F6CD2984DE8C3F83DCBDF8FF4D8DE`.
- Main EXE SHA-256: `2B93F7582FFD024EC1B79A75E9BE389C0D8B3DD33B2A96B425BA25A4A941A1AB`.
- Main EXE: 142,463,322 bytes; helper: 73,586,491 bytes; total extracted: 216,049,813 bytes.
- Helper SHA-256: `E4B78CD7B0E59872926DB424A9DAA7D87FA2B0A6E0E0B3ACA77768FF5965EB19`.
- `PUBLIC-RELEASE-INFO.txt` is beside the unpackaged candidate, outside the two-file ZIP.
- 334 Normal offline checks passed, including identity, Safety, warm reuse, Esc, progress, shortcut
  import and portable-path behavior. Package managed metadata contains zero Observer types.
- Both bundles contain their runtime and are x64; native host code matches the installed Microsoft
  self-contained host. Main embeds WPF runtime. Both actual PE manifest resources are asInvoker,
  uiAccess=false; helper manifest matches source. All six main icon resources match the ICO.
- Candidate EXE rejects `--observer-tail`, `--test-heroes-prep`, and `--test-battlenet-start` with exit 2
  before any normal workflow. No real UU/Battle.net/Heroes launch occurred during preparation.
- ZIP entries, CRC and both payload hashes were verified. No development/private-path byte strings
  found by the package verifier. No settings, CLI, logs, source, PDB or research assets packaged.
- Safety review found no contradiction in current Normal launch/helper APIs and local file handling;
  it is an implementation review, not a security certification. Full runtime acceptance remains pending.
- SDK informational version retains build-parent `+51327fbd4b5f76f4683a648c5ce9d8b480e17ccf`.
  This is the parent at packaging time, not the subsequent preparation commit. The Build ID and
  payload hashes above identify this exact candidate; do not silently rebuild it after committing.

Accepted dev.7 launch behavior is unchanged. Main/helper version metadata advances to alpha.1;
the helper is rebuilt for public metadata with the same implementation and manifest. Prior research
changes remain developer-only and are excluded physically from the Normal binary. The tested
`--autostarted` argument is not integrated. Historical Friend-0.2 artifacts/tag remain immutable.
