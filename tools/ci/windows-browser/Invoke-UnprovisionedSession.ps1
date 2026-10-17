# Invoked by the disposable fixture as a second, fresh native Windows logon that is
# neither bootstrap administrator nor a member of any fixture group. A real Windows
# credential must authenticate (not 401) and then be refused by the grant runtime (403).
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ExpectedUserSid,
    [Parameter(Mandatory = $true)][string]$ForbiddenGroupSid,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$report = [ordered]@{
    SchemaVersion = 1; NativeUserMatches = $false; InFixtureGroup = $null
    IdentityRequestStatus = $null; Expected = 403; Status = 'FAILED'; FailureStage = 'TokenVerification'
}
$native = $null
try {
    $native = [Security.Principal.WindowsIdentity]::GetCurrent()
    $report.NativeUserMatches = $native.IsAuthenticated -and $native.User.Value -eq $ExpectedUserSid
    $principal = [Security.Principal.WindowsPrincipal]::new($native)
    $report.InFixtureGroup = $principal.IsInRole([Security.Principal.SecurityIdentifier]::new($ForbiddenGroupSid))
    if (-not $report.NativeUserMatches -or $report.InFixtureGroup) { throw 'Unprovisioned native logon verification failed.' }
    $report.FailureStage = 'IdentityRequest'
    Add-Type -AssemblyName System.Net.Http
    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.UseDefaultCredentials = $true
    $client = [System.Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(30)
    try {
        $response = $client.GetAsync([Uri]'http://localhost:5080/api/v1/security/me').GetAwaiter().GetResult()
        try { $report.IdentityRequestStatus = [int]$response.StatusCode } finally { $response.Dispose() }
    } finally { $client.Dispose() }
    if ($report.IdentityRequestStatus -eq $report.Expected) { $report.Status = 'PASSED'; $report.FailureStage = $null }
    else { $report.FailureStage = 'ExpectedForbidden' }
} catch { } # Never export exception messages, usernames, SID values or credentials.
finally {
    if ($native) { $native.Dispose() }
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'unprovisioned-session.json') -Encoding utf8
}
if ($report.Status -ne 'PASSED') { exit 1 }
