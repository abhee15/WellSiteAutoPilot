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
    Baseline = 'NOT EXECUTED'; Policies = @(); Status = 'FAILED'; FailureStage = 'TokenVerification'; DomainCoverage = 'NOT EXECUTED'
    IdentityRequestStatus = $null; IdentityRequestFailure = $null; IdentityMatches = $null
}
$native = $null
try {
    $native = [Security.Principal.WindowsIdentity]::GetCurrent()
    $report.NativeUserMatches = $native.IsAuthenticated -and $native.User.Value -eq $ExpectedUserSid
    $principal = [Security.Principal.WindowsPrincipal]::new($native)
    $report.EnabledGroupMatches = $principal.IsInRole([Security.Principal.SecurityIdentifier]::new($ExpectedGroupSid))
    if (-not $report.NativeUserMatches -or -not $report.EnabledGroupMatches) { throw 'Fresh native logon verification failed.' }
    # Alternate-credential process creation inherits the parent's environment.
    # Use this logon's known folders and fixture-owned temp, never runneradmin's profile.
    $env:USERPROFILE = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
    $env:LOCALAPPDATA = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    $env:APPDATA = [Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)
    $env:TEMP = Join-Path (Split-Path -Parent $OutputDirectory) 'temp'
    $env:TMP = $env:TEMP
    New-Item -ItemType Directory -Path $env:TEMP -Force | Out-Null
    $report.FailureStage = 'PowerShellBaseline'
    & (Join-Path $PSScriptRoot 'verify-windows-negotiate.ps1') -BaseUrl 'http://localhost:5080' *> $null
    # Verify the resolved identity, not only that some database user was returned.
    # The request and the comparison are separate stages so a failure says which one broke.
    # Only an HTTP status code, an exception type name and a boolean are recorded.
    $report.FailureStage = 'NativeIdentityRequest'
    try {
        $me = Invoke-RestMethod 'http://localhost:5080/api/v1/security/me' -UseDefaultCredentials -TimeoutSec 30
        $report.IdentityRequestStatus = 200
    } catch {
        $report.IdentityRequestFailure = $_.Exception.GetType().Name
        if ($_.Exception.Response) { $report.IdentityRequestStatus = [int]$_.Exception.Response.StatusCode }
        throw
    }
    $report.FailureStage = 'NativeIdentityMatch'
    $report.IdentityMatches = ($me.identityName -ieq $native.Name)
    if (-not $report.IdentityMatches) { throw 'Native baseline identity did not match.' }
    $report.Baseline = 'PASSED'
    $report.FailureStage = 'BrowserPolicies'
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
    }).Count -eq 0) { $report.Status = 'PASSED'; $report.FailureStage = $null }
} catch { } # Never export exception messages, usernames, SID values or credentials.
finally {
    if ($native) { $native.Dispose() }
    $report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'native-session.json') -Encoding utf8
}
if ($report.Status -ne 'PASSED') { exit 1 }
