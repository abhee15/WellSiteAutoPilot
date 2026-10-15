# Windows-only smoke test for *real* Negotiate on server-hosted UI/API.
# Invoke under a Windows principal that is valid in the local/test WSA database.
# Do not send integrated credentials through Vite or TestHeader.
param(
    [string]$BaseUrl = "http://127.0.0.1:5080"
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http

if ($env:OS -ne "Windows_NT") {
    throw "This smoke test requires Windows SSPI integrated credentials."
}

try {
    $origin = [Uri]::new($BaseUrl)
} catch {
    throw "BaseUrl must be an absolute http(s) origin."
}
if (-not $origin.IsAbsoluteUri -or
    ($origin.Scheme -ne "http" -and $origin.Scheme -ne "https") -or
    $origin.AbsolutePath -ne "/" -or
    $origin.Query -or $origin.Fragment -or
    $origin.UserInfo) {
    throw "BaseUrl must be a plain absolute http(s) origin."
}
$uri = [Uri]::new($origin, "api/v1/security/me")

# Leg 1: an unauthenticated client receives a challenge.
$anonymousHandler = [System.Net.Http.HttpClientHandler]::new()
$anonymousHandler.UseDefaultCredentials = $false
$anonymousClient = [System.Net.Http.HttpClient]::new($anonymousHandler)
try {
    $anonymousResponse = $anonymousClient.GetAsync($uri).GetAwaiter().GetResult()
    try {
        if ($anonymousResponse.StatusCode -ne
            [System.Net.HttpStatusCode]::Unauthorized) {
            throw "Expected anonymous 401, got $([int]$anonymousResponse.StatusCode)."
        }
        $schemes = @($anonymousResponse.Headers.WwwAuthenticate |
            ForEach-Object { $_.Scheme })
        if ($schemes -notcontains "Negotiate") {
            throw "The server did not advertise Negotiate; check Security:AuthenticationMode."
        }
    } finally {
        $anonymousResponse.Dispose()
    }
} finally {
    $anonymousClient.Dispose()
}

# Full integrated challenge-response: default Windows credentials, not a
# synthetic header. The final /security/me response must be 200 and must
# describe an authenticated WSA user. A mere 401 is NOT a pass.
$windowsHandler = [System.Net.Http.HttpClientHandler]::new()
$windowsHandler.UseDefaultCredentials = $true
$windowsClient = [System.Net.Http.HttpClient]::new($windowsHandler)
try {
    $response = $windowsClient.GetAsync($uri).GetAwaiter().GetResult()
    try {
        if ($response.StatusCode -ne [System.Net.HttpStatusCode]::OK) {
            throw "Windows Negotiate did not complete: final HTTP status " +
                "$([int]$response.StatusCode). Check Windows/browser policy, " +
                "Kestrel configuration and active user state."
        }
        $json = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $payload = $json | ConvertFrom-Json
        $parsedId = [Guid]::Empty
        $hasValidId = [Guid]::TryParse([string]$payload.userId, [ref]$parsedId)
        if (-not $payload.identityName -or -not $hasValidId -or
            $parsedId -eq [Guid]::Empty) {
            throw "Negotiate endpoint returned invalid WSA user identity data."
        }
        Write-Host "PASS: true Windows Negotiate challenge-response reached HTTP 200."
        Write-Host "WSA user $parsedId was resolved (identity not printed)."
    } finally {
        $response.Dispose()
    }
} finally {
    $windowsClient.Dispose()
}
