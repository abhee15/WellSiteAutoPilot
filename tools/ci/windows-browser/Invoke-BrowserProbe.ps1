# Run under the real Windows account being tested. No HTTP credentials,
# TestHeader, authentication storage, HAR, screenshots or raw browser logs.
[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://localhost:5080',
    [ValidateSet('Default', 'OriginAllowlist')][string]$Policy = 'Default',
    [string]$DriverPath = '',
    [ValidateRange(1024, 65535)][int]$DriverPort = 9517,
    [Parameter(Mandatory = $true)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
if ($env:OS -ne 'Windows_NT') { throw 'NOT EXECUTED: a native Windows logon is required.' }
$origin = [Uri]::new($BaseUrl)
if (-not $origin.IsAbsoluteUri -or $origin.Scheme -notin @('http', 'https') -or
    $origin.AbsolutePath -ne '/' -or $origin.Query -or $origin.Fragment -or $origin.UserInfo) {
    throw 'BaseUrl must be a plain HTTP(S) origin.'
}
$native = [System.Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $native.IsAuthenticated -or -not $native.User) { throw 'NOT EXECUTED: native Windows identity is unavailable.' }
if (-not $DriverPath) {
    if ($env:EDGEWEBDRIVER) { $DriverPath = Join-Path $env:EDGEWEBDRIVER 'msedgedriver.exe' }
    else { $DriverPath = (Get-Command msedgedriver.exe -ErrorAction Stop).Source }
}
if (-not (Test-Path -LiteralPath $DriverPath -PathType Leaf)) { throw 'NOT EXECUTED: Microsoft Edge WebDriver is unavailable.' }
$driverOrigin = "http://127.0.0.1:$DriverPort"
$runRoot = Join-Path ([IO.Path]::GetTempPath()) ('wsa-browser-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot | Out-Null
$report = [ordered]@{
    SchemaVersion = 1; Topology = 'CALLER MUST VERIFY DIRECT KESTREL'; RequiredAuthenticationMode = 'Negotiate'; Policy = $Policy
    NativeIdentityClass = 'WindowsProcessToken'; WindowsVersion = [Environment]::OSVersion.Version.ToString()
    DomainCoverage = 'NOT EXECUTED'; ConnectionEvidence = 'NOT CAPTURED'; Scenarios = @()
    LocalCoverage = 'NOT EXECUTED'
}
$driver = $null
$session = $null
function Invoke-Driver([string]$Method, [string]$Path, $Body = $null) {
    $parameters = @{ Uri = $driverOrigin + $Path; Method = $Method; TimeoutSec = 35 }
    if ($null -ne $Body) { $parameters.ContentType = 'application/json'; $parameters.Body = ConvertTo-Json $Body -Depth 15 -Compress }
    try { return (Invoke-RestMethod @parameters).value }
    catch { throw "WebDriver command failed: $Method $Path. Raw driver output is intentionally omitted." }
}
function Navigate([string]$Path) {
    Invoke-Driver 'POST' "/session/$session/url" @{ url = [Uri]::new($origin, $Path).AbsoluteUri } | Out-Null
}
function Invoke-BrowserFetch([string[]]$Paths, [bool]$Parallel) {
    # Only final status, opaque correlation ID and an identity-match boolean
    # cross back into the report. No response bodies or request headers.
    $script = @'
const paths = arguments[0], parallel = arguments[1], expected = arguments[2], done = arguments[arguments.length - 1];
const read = async path => {
  try {
    const correlation = 'wsa-browser-' + Date.now().toString(36) + '-' + Math.random().toString(36).slice(2);
    const response = await fetch(path, { credentials: 'same-origin', cache: 'no-store', headers: { 'X-Correlation-ID': correlation } });
    let identityMatches = null;
    if (path === '/api/v1/security/me' && response.status === 200) {
      const body = await response.json();
      identityMatches = typeof body.identityName === 'string' && body.identityName.toUpperCase() === expected.toUpperCase();
    }
    return { path, status: response.status, correlationId: response.headers.get('X-Correlation-ID'), identityMatches };
  } catch { return { path, status: 0, correlationId: null, identityMatches: null }; }
};
(async () => {
  const observations = [];
  if (parallel) observations.push(...await Promise.all(paths.map(read)));
  else for (const path of paths) observations.push(await read(path));
  done(observations);
})().catch(() => done([]));
'@
    return @(Invoke-Driver 'POST' "/session/$session/execute/async" @{ script = $script; args = @(@($Paths), $Parallel, $native.Name) })
}
try {
    $driver = Start-Process -FilePath $DriverPath -ArgumentList @("--port=$DriverPort") -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $runRoot 'driver.stdout') -RedirectStandardError (Join-Path $runRoot 'driver.stderr')
    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        try { Invoke-Driver 'GET' '/status' | Out-Null; $ready = $true; break }
        catch { Start-Sleep -Milliseconds 200 }
    }
    if (-not $ready) { throw 'NOT EXECUTED: WebDriver did not become ready.' }
    $paths = @('/api/v1/security/me', '/api/v1/system/info', '/api/v1/assets', '/api/v1/executions',
        '/api/v1/asset-types', '/api/v1/logic-modules', '/api/v1/configured-logic')
    foreach ($scenario in @('IdentityNavigation', 'Sequential', 'ColdConcurrent', 'WarmConcurrent', 'Spa')) {
        $case = [ordered]@{ Name = $scenario; Status = 'FAILED'; Observations = @(); IdentityMatches = $false; BrowserVersion = '' }
        try {
            $profile = Join-Path $runRoot $scenario
            $arguments = @('--headless=new', '--no-first-run', '--no-default-browser-check', "--user-data-dir=$profile")
            if ($Policy -eq 'OriginAllowlist') { $arguments += '--auth-server-allowlist=' + $origin.DnsSafeHost }
            $created = Invoke-Driver 'POST' '/session' @{ capabilities = @{ alwaysMatch = @{
                browserName = 'MicrosoftEdge'; 'ms:edgeOptions' = @{ args = $arguments }
            } } }
            $session = $created.sessionId
            $case.BrowserVersion = $created.capabilities.browserVersion
            Invoke-Driver 'POST' "/session/$session/timeouts" @{ pageLoad = 20000; script = 25000 } | Out-Null
            if ($scenario -in @('IdentityNavigation', 'WarmConcurrent')) {
                Navigate '/api/v1/security/me'
                $identity = Invoke-Driver 'POST' "/session/$session/execute/sync" @{
                    script = 'try { const b = JSON.parse(document.body.innerText); return typeof b.identityName === "string" && b.identityName.toUpperCase() === arguments[0].toUpperCase(); } catch { return false; }'
                    args = @($native.Name)
                }
                $case.IdentityMatches = $identity -eq $true
                if (-not $case.IdentityMatches) { throw 'The browser did not resolve the native Windows process identity.' }
            } else { Navigate '/health/live' }
            if ($scenario -eq 'IdentityNavigation') { $case.Status = 'PASSED' }
            else {
                if ($scenario -eq 'Spa') {
                    Navigate '/'
                    $loaded = $false
                    for ($attempt = 0; $attempt -lt 50; $attempt++) {
                        $loaded = Invoke-Driver 'POST' "/session/$session/execute/sync" @{
                            script = 'return !!document.querySelector(".app-shell") && [...document.querySelectorAll("nav a")].some(a => a.getAttribute("href") === "#assets");'
                            args = @()
                        }
                        if ($loaded) { break }
                        Start-Sleep -Milliseconds 200
                    }
                    if (-not $loaded) { throw 'The server-hosted SPA did not load an authorized workspace.' }
                }
                $observations = @(Invoke-BrowserFetch $paths ($scenario -in @('ColdConcurrent', 'WarmConcurrent', 'Spa')))
                $case.Observations = $observations
                $case.IdentityMatches = @($observations | Where-Object { $_.path -eq '/api/v1/security/me' -and $_.identityMatches -eq $true }).Count -eq 1
                if ($observations.Count -eq $paths.Count -and $case.IdentityMatches -and
                    @($observations | Where-Object { $_.status -ne 200 }).Count -eq 0) { $case.Status = 'PASSED' }
            }
        } catch {
            # Report no exception message, body, HTML, browser log or auth token.
            $case.Status = 'FAILED'
        } finally {
            if ($session) { try { Invoke-Driver 'DELETE' "/session/$session" | Out-Null } catch {}; $session = $null }
        }
        $report.Scenarios += $case
    }
    if (@($report.Scenarios | Where-Object { $_.Status -ne 'PASSED' }).Count -eq 0) {
        $report.LocalCoverage = 'BROWSER SCENARIOS PASSED; CONNECTION EVIDENCE REQUIRED'
    } else { $report.LocalCoverage = 'FAILED' }
} finally {
    if ($driver -and -not $driver.HasExited) { & taskkill.exe /PID $driver.Id /T /F *> $null }
    $native.Dispose()
    New-Item -ItemType Directory -Path (Split-Path -Parent $OutputPath) -Force | Out-Null
    $report | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
    Remove-Item -LiteralPath $runRoot -Recurse -Force -ErrorAction SilentlyContinue
}
if ($report.LocalCoverage -eq 'FAILED') { throw 'Windows browser scenarios failed. Inspect the sanitized scenario report.' }
