# Friend Test 0.2 — CLI Auto v4 (local acceptance only)

Keep the whole published folder together, including WindowHelper and SharpCompress.dll. Requires .NET 10 Desktop Runtime x64. This remains an unofficial private test build.

## Automatic CLI preparation
No manual CLI selection is required normally. Preflight checks a configured CLI, then the launcher's cache, then limited known paths in the verified UU installation. Existing valid configured/cached components are preserved; no speculative upgrade replaces them.

Installation discovery considers only numeric immediate subdirectories under the configured, signature-verified UU launcher directory (maximum 64 immediate directories). It verifies each candidate uu.exe and ranks archive candidates by that executable's signed file version, never by a hardcoded directory number. An equal-version ambiguity stops preparation rather than claiming an active directory. A running old version cannot reliably be inferred solely from files; the selected source is a verified installed version, not a claim about the running version.

Only the installation's netease-uu-booster.7z is read. SharpCompress is pinned to official NuGet version 0.50.4 (exact version range [0.50.4], no additional runtime package dependencies for net10.0). CLI binaries are never downloaded or bundled.

Only uu-cli.exe is extracted, first into an isolated staging folder beneath %LOCALAPPDATA%\StormHeroesLauncher\Tools\UU, then into bin\uu-cli.exe after validation. No archive directory tree is extracted. Additional sibling runtime files cause a clear unsupported-package failure; they are not copied speculatively. Output containment, reparse-point rejection, archive-entry limits, size bounds, encrypted/link entry rejection, and duplicate CLI rejection apply.

Every candidate must be a Windows PE executable named uu-cli.exe with ProductName UU CLI, an Authenticode signature trusted by Windows, and verified signer name NetEase (Hangzhou) Network Co., Ltd. Certificate renewal and file-version updates are accepted; no file hash or 1.0.0.1 restriction. Other legal publisher identities need explicit future review. Source uu_launcher.exe and version uu.exe must also have the trusted NetEase signature. Windows WinVerifyTrust runs without UI and with cache-only trust/revocation retrieval. Offline trust failures are fail-closed; no PowerShell runtime dependency or online certificate/CLI retrieval. This may require troubleshooting a machine's existing trust/revocation cache; the launcher does not alter Windows trust settings.

Validation is repeated immediately before CLI execution, holding a read handle that denies file replacement during validation/execution. The existing CLI command protocol, target IDs, launch order, window handling, UAC behavior, settings schema and game-process detection remain unchanged.

## Settings and failure behavior
Use --settings or Shift at startup. The main settings page displays automatic component status; the manual path is in a collapsed Advanced field. Detect/Prepare and Save both perform discovery/validation; saving never launches the game. A normal launch with a missing/broken CLI first attempts recovery; success updates settings and continues the proven workflow. Failure opens recovery without starting any part of the workflow.

Settings: %LOCALAPPDATA%\StormHeroesLauncher\settings.json
Logs: %LOCALAPPDATA%\StormHeroesLauncher\Logs
Cache: %LOCALAPPDATA%\StormHeroesLauncher\Tools\UU\bin\uu-cli.exe

## Manual local acceptance
1. Back up settings.json. To test a clean CLI path, clear only UuCliPath in that file. If the launcher cache exists, rename it temporarily to preserve it. Do not touch UU installation files.
2. Run this folder's StormHeroesLauncher.exe --settings. Confirm the official component is automatically prepared and the Advanced path points into the cache. Save and exit; no game should launch in Settings mode.
3. Check logs for source version directory, archive, Extraction=Success, Signature=Valid and Validation=Passed. If trust is unavailable, return the log; do not bypass validation.
4. Run normally in your already-tested environment. Confirm boosting, both minimization behaviors, Heroes launch and launcher exit remain correct. Standard helper UAC behavior is unchanged.
5. Run again to confirm the valid configured/cache component is reused without extraction. Set a nonexistent UuCliPath and test recovery through --settings.
6. Confirm UU installation files remain unchanged. A UU update with a broken old configured path should recover through the existing valid cache or a newly identified verified installation source.

Automated tests use only temporary synthetic files/archives and fake positive-signature results. Native positive Authenticode validation and the installed real archive are still subject to manual acceptance. No real launcher, CLI, WindowHelper, UU, Battle.net or game was run for these tests. No friend ZIP has been created.
