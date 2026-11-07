[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$RequestFile,
    [Parameter(Mandatory = $true)][string]$EvidenceDirectory,
    [string]$LibraryPath = $env:CYGNET_LIBRARY_PATH,
    [switch]$IncludeSamples
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'This probe requires a licensed Windows CygNet host and PowerShell 7.' }
if (-not $LibraryPath) { throw 'Supply LibraryPath or set CYGNET_LIBRARY_PATH.' }
$LibraryPath = (Resolve-Path -LiteralPath $LibraryPath).Path
$RequestFile = (Resolve-Path -LiteralPath $RequestFile).Path
foreach ($assembly in @('CygNet.API.Historian.dll', 'CygNet.Data.Historian.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $LibraryPath $assembly))) { throw "Missing licensed assembly: $assembly" }
}
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$project = Join-Path $repoRoot 'tools/WellSiteAutoPilot.CygNetHistoryProbe/WellSiteAutoPilot.CygNetHistoryProbe.csproj'
New-Item -ItemType Directory -Force -Path $EvidenceDirectory | Out-Null
$EvidenceDirectory = (Resolve-Path -LiteralPath $EvidenceDirectory).Path
$oldPath = $env:CYGNET_LIBRARY_PATH
try {
    $env:CYGNET_LIBRARY_PATH = $LibraryPath
    & dotnet build $project --configuration Release 2>&1 | Tee-Object -FilePath (Join-Path $EvidenceDirectory 'native-build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Native build failed. The historian API surface has not been verified.' }
    $probeArgs = @('--request', $RequestFile)
    if ($IncludeSamples) { $probeArgs += @('--samples-file', (Join-Path $EvidenceDirectory 'history-samples.json')) }
    & dotnet run --project $project --configuration Release --no-build --no-launch-profile -- @probeArgs 2>&1 |
        Tee-Object -FilePath (Join-Path $EvidenceDirectory 'native-probe.json')
    if ($LASTEXITCODE -ne 0) { throw 'Native read probe failed. Live history remains unsupported.' }
    Write-Host 'Native read probe finished. Review the report and compare against CygNet before enabling the capability.'
}
finally { $env:CYGNET_LIBRARY_PATH = $oldPath }
