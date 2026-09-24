# Single-UAC v5 — local acceptance only

Keep the entire folder together. Main executable and helper remain asInvoker; only the main's single helper request uses Shell runas. Nothing bypasses or auto-confirms Windows UAC.

Cold UU: verify configured uu_launcher.exe (existing native NetEase Authenticode validation), request helper once. Helper independently validates, starts UU with UseShellExecute=false from its elevated context, waits up to 15 seconds for uu.exe / UUMAINFORMV40 / visible ownerless top-level window, posts exactly one WM_CLOSE, observes for 3 seconds and requires the original UU process to remain alive and the original window to be non-visible. Only then does the existing CLI readiness/start/boost-confirm flow continue. No separate main-process launch of UU and no later UU WindowHelper call occur.

Warm visible UU: try one normal WM_CLOSE and verify. If it fails, request one elevated tray-only helper, never relaunch UU. Cancelling helper UAC or tray-only failure is nonfatal while UU remains alive. Already-hidden UU: no helper/UAC. No automatic retry or alternate-target close is performed. Wrong close-to-tray configuration can exit UU; in that case the launch workflow stops instead of restarting it.

Helper result is sent over a random, bounded, same-user named pipe using documented .NET Windows pipe APIs. It connects before acting, never writes elevated report files and exits after its bounded operation. Parent wait is bounded to 35 seconds after Shell returns. Logs contain helper state/target/result, not window titles or credentials. Switching to another administrator account at UAC may fail the same-user channel safely; no bypass is attempted.

Battle.net: the previously diagnostic-only WM_CLOSE action is now used at the existing window-policy call sites when mode=Minimized. Mode=Normal stays untouched. Existing discovery and cold readiness remain; warm readiness now also accepts an already-running enabled/non-hung/ownerless Chrome_WidgetWin_* window while hidden, to avoid waiting forever on native tray state. Readiness still cannot prove login. This is the minimal readiness compatibility change for the requested native tray behavior.

CLI Auto v4 / SharpCompress / CLI protocol / target IDs / Switcher arguments / game detection / settings / mutex / Shift behavior remain unchanged. Current setting name Minimized is retained for compatibility even though the configured native close-to-tray action now removes Battle.net from the desktop/taskbar.

Manual acceptance (do not distribute before passing):
1. Prepare normal networking, remembered UU/Battle.net login, valid membership and completed updates. Confirm both apps are configured to close to tray. Use mode=Minimized.
2. Fully exit UU manually; close Heroes. Double-click this folder's StormHeroesLauncher.exe. Confirm exactly ONE Windows UAC for WindowHelper. Accept once.
3. Confirm UU starts then leaves desktop/taskbar before CLI boosting, tray icon remains, acceleration begins, Battle.net trays, Heroes starts, launcher exits. Confirm no second UAC.
4. Exit Heroes manually, keep UU in tray, run again. Confirm ZERO UU helper UAC and successful workflow. Include Battle.net already in tray to verify warm readiness.
5. Exit Heroes, restore UU's main window, run again. Expect at most one helper UAC if normal close cannot cross integrity levels. Confirm tray and acceleration continue.
6. Cold cancellation: fully exit UU, run, decline the single UAC. Confirm safe stop, no boost/game launch, no retry.
7. Warm cancellation: leave UU running and visible, run and decline helper UAC. Confirm game workflow continues with UU visible and acceleration intact; no second prompt.
8. Check %LOCALAPPDATA%\StormHeroesLauncher\Logs for single-uac-v5, State, HelperRequested, Action, LauncherValidated, UUStarted, TargetPID/HWND, WM_CLOSE, WindowVisibleAfter, ProcessRunningAfter and ExitCode. Return the log if any criterion fails.

No real application or elevated helper was run during development tests. This folder requires .NET 10 Desktop Runtime x64. No ZIP or friend-ready claim.
