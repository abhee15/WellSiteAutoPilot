# Invoked by the disposable fixture with a fresh native Windows logon.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ExpectedUserSid,
    [Parameter(Mandatory = $true)][string]$ExpectedGroupSid,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$DriverPath
)
$ErrorActionPreference = 'Stop'
$report = [ordered]@{
    SchemaVersion = 1; NativeUserMatches = $false; EnabledGroupMatches = $false
    Baseline = 'NOT EXECUTED'; Policies = @(); Status = 'FAILED'; DomainCoverage = 'NOT EXECUTED'
}
$native = $null
try {
    $native = [Security.Principal.WindowsIdentity]::GetCurrent()
    $report.NativeUserMatches = $native.IsAuthenticated -and $native.User.Value -eq $ExpectedUserSid
    $principal = [Security.Principal.WindowsPrincipal]::new($native)
    $report.EnabledGroupMatches = $principal.IsInRole([Security.Principal.SecurityIdentifier]::new($ExpectedGroupSid))
    if (-not $report.NativeUserMatches -or -not $report.EnabledGroupMatches) { throw 'Fresh native logon verification failed.' }
    & (Join-Path $PSScriptRoot 'verify-windows-negotiate.ps1') -BaseUrl 'http://localhost:5080' *> $null
    # Verify the resolved identity, not only that some database user was returned.
    $me = Invoke-RestMethod 'http://localhost:5080/api/v1/security/me' -UseDefaultCredentials -TimeoutSec 30
    if ($me.identityName -ine $native.Name) { throw 'Native baseline identity did not match.' }
    $report.Baseline = 'PASSED'
    foreach ($policy in @('Default', 'OriginAllowlist')) {
        $path = Join-Path $OutputDirectory ($policy + '.json')
        try {
            & (Join-Path $PSScriptRoot 'Invoke-BrowserProbe.ps1') -Policy $policy -DriverPath $DriverPath -OutputPath $path *> $null
        } catch { } # Both policy reports must survive a browser failure.
        $status = 'NOT EXECUTED'
        if (Test-Path -LiteralPath $path) {
            $probe = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
            $status = $probe.LocalCoverage
        }
        $report.Policies += @{ Policy = $policy; Status = $status }
    }
    if ($report.Policies.Count -eq 2 -and @($report.Policies | Where-Object {
        $_.Status -ne 'BROWSER SCENARIOS PASSED; CONNECTION EVIDENCE REQUIRED'
    }).Count -eq 0) { $report.Status = 'PASSED' }
} catch { } # Never export exception messages, usernames, SID values or credentials.
finally {
    if ($native) { $native.Dispose() }
    $report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'native-session.json') -Encoding utf8
}
if ($report.Status -ne 'PASSED') { exit 1 }
