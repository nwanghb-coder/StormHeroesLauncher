param([switch]$DeveloperObserver)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$flavor = if ($DeveloperObserver) { 'Observer' } else { 'Normal' }
$flag = if ($DeveloperObserver) { 'true' } else { 'false' }
$package = Join-Path $root ('artifacts\Stage2-0.3.0-dev.2-' + $flavor)
if (Test-Path -LiteralPath $package) {
    if (Get-ChildItem -LiteralPath $package -Force | Select-Object -First 1) {
        throw "Output already populated: $package. Preserve it or choose a new version before publishing again."
    }
}
Push-Location $root
try {
    & dotnet publish src/StormHeroesLauncher/StormHeroesLauncher.csproj --configuration Release -p:PublishProfile=Portable "-p:DeveloperObserver=$flag" -p:NuGetAudit=false -p:RestoreIgnoreFailedSources=true --output $package
    if ($LASTEXITCODE -ne 0) { throw 'Main publish failed' }
    & dotnet publish src/StormHeroesLauncher.WindowHelper/StormHeroesLauncher.WindowHelper.csproj --configuration Release -p:PublishProfile=Portable "-p:DeveloperObserver=$flag" -p:NuGetAudit=false -p:RestoreIgnoreFailedSources=true --output (Join-Path $package 'app')
    if ($LASTEXITCODE -ne 0) { throw 'Helper publish failed' }
    & (Join-Path $PSScriptRoot 'Verify-PortablePackage.ps1') -Package $package -BuildFlavor $flavor
    Write-Output "PASS: $flavor package: $package"
} finally { Pop-Location }
