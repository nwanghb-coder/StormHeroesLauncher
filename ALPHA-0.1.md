# StormHeroesLauncher Alpha 0.1 (local PC build)

This Alpha runs automatically without showing the former MVP main window. It reuses the existing UU startup and CLI services. It is not a public distribution build.

## Workflow
A session-local named mutex prevents overlapping Alpha launch workflows. If HeroesOfTheStorm_x64.exe already runs in this Windows session, the launcher exits without changing acceleration or starting another game. Otherwise it checks the local Battle.net/Switcher installation files, ensures UU via MVP 1, waits for CLI readiness, sends exactly one target start request, and waits for isBoosting=true and status=boosting through MVP 2.

The fixed CLI settings remain game 569cbb26a26c753e42982639, Asia zone 5e84381b04c2150cc09e485e, Korea server 57a3ec73a26c752383c56217. Existing uu-cli.settings.json is copied with the build and still points to this PC's official UU CLI.

Battle.net is launched only if Battle.net.exe is absent in the current Windows session. Its path is D:\Blizzard App\Blizzard App01\Battle.net.exe. Readiness requires a live process owning the same visible, enabled, ownerless, non-hung top-level window for three samples 500 ms apart. This is a heuristic: it cannot verify auto-login, account/server state, or distinguish every startup/login window. Battle.net auto-login must already be configured.

Exactly one Switcher launch uses D:\Blizzard App\Heroes of the Storm\Support64\HeroesSwitcher_x64.exe with ArgumentList: -sso=1, -launch, -uid, heroes. Working directory: D:\Blizzard App\Heroes of the Storm\Support64. Both Blizzard launches use UseShellExecute=false and no elevation verb. No versioned game path is used.

Success is detecting a live HeroesOfTheStorm_x64 process in the current Windows session; it does not prove login or server connection. The launcher logs success and exits automatically. The old MVP UI source remains but is not opened by Alpha startup.

## Timeouts and errors
- UU: existing 15 seconds after Shell/UAC returns. User time spent responding to UAC is outside this timer.
- CLI: existing 20 seconds per command, 15 seconds readiness, 30 seconds target boosting confirmation, status polling every 1 second.
- Battle.net window readiness: 60 seconds, polling every 500 ms; no repeated application starts.
- Game process appearance: 30 seconds, polling every 250 ms; no repeated Switcher launches.
- Failure: concise MessageBox with log location, then launcher exit with error code. Existing acceleration and external applications remain running. Uncertain CLI starts are not retried or automatically rolled back.
- Only UU may request the normal runas/UAC flow. StormHeroesLauncher remains asInvoker. Do not manually run the launcher as administrator.

Logs: %LOCALAPPDATA%\StormHeroesLauncher\Logs\launcher-YYYY-MM-DD.log. No runtime SSO values, credentials, UIA/MSAA, hooks, game memory, packet manipulation, update handling, discovery wizard, or settings UI are added. The logger retains the pre-existing best-effort disk-write behavior.

## Build and manual acceptance
Release x64 EXE: E:\CodexProjects\StormHeroesLauncher\artifacts\Alpha-0.1\StormHeroesLauncher.exe
Keep the complete output folder including uu-cli.settings.json; .NET 10 Windows Desktop Runtime x64 is required.

1. Manually prepare your normal working network environment and confirm UU can accelerate the selected target. This Alpha actively starts UU acceleration; the earlier UU-OFF diagnostic-only procedure does not apply.
2. Ensure Heroes is closed, Battle.net auto-login is configured, and game updates have already finished.
3. Double-click the Alpha EXE normally. With UU running, no UU UAC should be requested; with UU fully closed, manually accept the normal Windows UAC prompt.
4. Expect no launcher main window. Verify the game appears and StormHeroesLauncher exits. Check the daily log for confirmed boosting, Battle.net readiness, one Switcher request and game-process success.
5. Repeat manually with Battle.net already logged in/running, then with Battle.net closed to validate its auto-login timing. Window readiness cannot guarantee authentication readiness.
6. During a separate run, double-click the Alpha EXE again while the first workflow is active: only one workflow should continue.
7. Optionally test cancellation with UU closed: choose No on UAC. Expect a clear error dialog, no Battle.net/Switcher launch by that attempt, and an error log.
8. If launch fails or times out, inspect the log and Battle.net manually before retrying. Do not expect update/subscription handling.

The agent builds and runs only offline fake-based tests, not a real end-to-end launch.
