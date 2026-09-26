param([switch]$DeveloperObserver)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$flavor = if ($DeveloperObserver) { 'Observer' } else { 'Normal' }
$flag = if ($DeveloperObserver) { 'true' } else { 'false' }
$package = Join-Path $root ('artifacts\Stage2-0.3.0-dev.5-' + $flavor)
# Dev.5 explicitly preserves WindowHelper, including its accepted build metadata.
$acceptedHelper = Join-Path $root 'artifacts\Stage2-0.3.0-dev.4-Normal\app\StormHeroesLauncher.WindowHelper.exe'
$acceptedHelperHash = 'CE4B259E55DE1135D74AB1900AC7C981A1E2FE249466DB9E6DAB773AB222246B'
if (!(Test-Path -LiteralPath $acceptedHelper) -or (Get-FileHash -LiteralPath $acceptedHelper -Algorithm SHA256).Hash -ne $acceptedHelperHash) {
    throw 'Accepted dev.4 WindowHelper is missing or changed. Restore that artifact before publishing dev.5.'
}
if (Test-Path -LiteralPath $package) {
    if (Get-ChildItem -LiteralPath $package -Force | Select-Object -First 1) {
        throw "Output already populated: $package. Preserve it or choose a new version before publishing again."
    }
}
Push-Location $root
try {
    & dotnet publish src/StormHeroesLauncher/StormHeroesLauncher.csproj --configuration Release -p:PublishProfile=Portable "-p:DeveloperObserver=$flag" -p:NuGetAudit=false -p:RestoreIgnoreFailedSources=true --output $package
    if ($LASTEXITCODE -ne 0) { throw 'Main publish failed' }
    $helperFolder = Join-Path $package 'app'
    New-Item -ItemType Directory -Force -Path $helperFolder | Out-Null
    Copy-Item -LiteralPath $acceptedHelper -Destination (Join-Path $helperFolder 'StormHeroesLauncher.WindowHelper.exe')
    & (Join-Path $PSScriptRoot 'Verify-PortablePackage.ps1') -Package $package -BuildFlavor $flavor
    Write-Output "PASS: $flavor package: $package"
} finally { Pop-Location }
