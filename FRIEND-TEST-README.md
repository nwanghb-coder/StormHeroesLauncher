# StormHeroesLauncher 0.2.0-friend-test

LOCAL MANUAL ACCEPTANCE CANDIDATE — NOT APPROVED FOR FRIEND DISTRIBUTION.
This is an experimental private test build, unofficial and unaffiliated with Blizzard or NetEase.

## Requirements
Windows 10/11 x64; Microsoft .NET 10 Desktop Runtime x64 (framework-dependent build).
Only NetEase UU is supported. Install UU, Battle.net and Heroes yourself. Log into UU and Battle.net manually and enable remembered/automatic login. UU membership must be valid, and game/UU updates must already be complete.
An official compatible uu-cli.exe must already be available: choose its existing location in Settings. The CLI and third-party applications are not bundled or installed by this launcher. If your UU installation does not include that CLI, this build is not ready for that machine until it is obtained from its official source. Do not substitute an arbitrary executable.

## Setup and use
Keep the entire folder together. Run StormHeroesLauncher.exe --settings, or hold Shift while starting it (keep Shift held until Settings appears). Choose these four existing files:
- UU: uu_launcher.exe
- Official UU CLI: uu-cli.exe
- Battle.net: Battle.net.exe
- Heroes: Support64\HeroesSwitcher_x64.exe (never select a versioned game executable)
Save and exit; saving does not launch anything. Settings are per Windows user at %LOCALAPPDATA%\StormHeroesLauncher\settings.json.
Normal double-click is headless: ensure UU (normal UAC if necessary), start the proven Heroes International / Asia / Korea acceleration, verify boosting, ensure Battle.net, start Switcher with fixed ordinary switches -sso=1 -launch -uid heroes, detect the game process, then exit. UU/Battle.net remain running. No runtime SSO material is retrieved.

Default BattleNetWindowMode is Minimized: request and verify main-window minimization before Switcher. Normal preserves Battle.net's window state. Already-running UU is left alone; cold-started UU is minimized after boost confirmation. No clicks, focus forcing or internal UI automation. External programs may briefly appear during startup, or restore their own windows afterward. Window failures are nonfatal and logged. Elevated UU may reject window management; this requires visual acceptance before distribution.

Automatic discovery uses existing settings, executable paths of relevant running processes, read-only installation/App Paths registry entries, and a small set of Program Files locations. It never scans drives recursively. Explicit invalid saved paths are kept for correction. Missing/wrong file paths open Settings before any launch/acceleration. Clear a field and use Detect empty paths, or browse directly.

## Local acceptance gate (developer must perform all checks)
1. Open --settings; verify all four files and default Minimized. Save; confirm nothing launches.
2. Prepare normal working network and remembered login yourself; finish updates and ensure membership. Fully exit UU manually for the cold-start case.
3. Double-click the launcher. Confirm normal UU UAC; accept it manually.
4. Confirm UU becomes ready, boosting works and its window does not remain in front.
5. Confirm Battle.net is ready and does not remain in front in Minimized mode.
6. Confirm Heroes launches normally; launcher exits after process detection.
7. Confirm UU keeps accelerating and Battle.net stays running in the background.
8. Repeat with UU/Battle.net already running, including minimized/tray state; verify no unwanted foreground activation.
9. Double-click a second launcher while the first workflow is active; verify no overlapping workflow. After game launch, re-run and verify no duplicate game/boost mutation.
10. Test --settings and Shift startup. Test Normal mode: existing Battle.net state is preserved. Restore Minimized afterward.
11. Edit one saved path to a nonexistent file (in this launcher's settings only). Run normally: recovery UI must identify it and NO partial workflow should start. Fix/save; confirm no automatic launch after saving. Also test declining UU UAC.

Do not send this build to friends until all launch and visual/background criteria pass. If minimization fails, retain local-only status and return the log; do not change Windows security/UAC to work around it.

## Logs and failures
%LOCALAPPDATA%\StormHeroesLauncher\Logs\launcher-yyyy-MM-dd.log
On failure, provide the version, failed step, whether UU/Battle.net were already running, chosen window mode, visible behavior and relevant log. Logs contain installation paths and may therefore reveal local user/folder names; review before sharing. They do not contain command-line secrets or credentials.
Login is not directly inspectable: process/window readiness does not prove login, and game process presence does not prove server authentication. If prompted to log in, do so manually in the application and enable remembered login, then retry. Failures do not automatically stop acceleration or close applications.

No update/subscription recovery, accelerator alternatives, translation, self-update or public distribution support. Missing official CLI and missing .NET runtime require user setup; nothing is installed automatically. This build checks expected filenames/existence, not publisher signatures or arbitrary CLI version compatibility. Only the previously validated CLI/IDs are proven.
