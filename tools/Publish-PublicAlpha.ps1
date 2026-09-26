$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$package = Join-Path $root 'artifacts\HOSLauncher-0.3.0-alpha.1-ReleaseCandidate'
if (Test-Path -LiteralPath $package) { throw 'Candidate directory already exists; preserve it and choose a fresh destination explicitly.' }
Push-Location $root
try {
    & dotnet publish src/StormHeroesLauncher/StormHeroesLauncher.csproj -c Release -p:PublishProfile=Portable -p:DeveloperObserver=false --no-restore --output $package
    if ($LASTEXITCODE -ne 0) { throw 'Normal main publish failed' }
    # Rebuild only to advance public metadata; accepted helper implementation/manifest are unchanged.
    & dotnet publish src/StormHeroesLauncher.WindowHelper/StormHeroesLauncher.WindowHelper.csproj -c Release -p:PublishProfile=Portable -p:DeveloperObserver=false --no-restore --output (Join-Path $package 'app')
    if ($LASTEXITCODE -ne 0) { throw 'Helper publish failed' }
    & (Join-Path $PSScriptRoot 'Verify-PortablePackage.ps1') -Package $package -BuildFlavor Normal
    & (Join-Path $PSScriptRoot 'Verify-LauncherIcon.ps1') -Exe (Join-Path $package 'HOSLauncher.exe') -Ico (Join-Path $root 'src\StormHeroesLauncher\Assets\Launcher.ico')
    Write-Output "PASS: public alpha candidate: $package"
} finally { Pop-Location }
