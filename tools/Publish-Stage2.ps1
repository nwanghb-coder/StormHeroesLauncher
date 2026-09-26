param([switch]$DeveloperObserver)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$flavor = if ($DeveloperObserver) { 'Observer' } else { 'Normal' }
$flag = if ($DeveloperObserver) { 'true' } else { 'false' }
$package = Join-Path $root ('artifacts\Stage2-0.3.0-dev.7-' + $flavor)
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
    # Dev.7 changes only Battle.net in the main app; preserve the accepted helper byte-for-byte.
    $frozenHelper = Join-Path $root 'artifacts\Stage2-0.3.0-dev.6-Normal\app\HOSLauncher.WindowHelper.exe'
    $expectedHelperHash = '78763F5C9A02A8ACB67DD3E56DF9CC7A62A0A42F3A6689E320F57D4B0EDE6BB2'
    if (!(Test-Path -LiteralPath $frozenHelper) -or (Get-FileHash -LiteralPath $frozenHelper -Algorithm SHA256).Hash -ne $expectedHelperHash) {
        throw 'Accepted dev.6 helper missing or changed; do not rebuild it implicitly.'
    }
    Copy-Item -LiteralPath $frozenHelper -Destination (Join-Path $helperFolder 'HOSLauncher.WindowHelper.exe')
    & (Join-Path $PSScriptRoot 'Verify-PortablePackage.ps1') -Package $package -BuildFlavor $flavor
    Write-Output "PASS: $flavor package: $package"
} finally { Pop-Location }
