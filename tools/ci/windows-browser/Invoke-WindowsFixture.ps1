# Dedicated GitHub-hosted Windows diagnostic VM only. Never run against a site DB.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ServerPath,
    [Parameter(Mandatory = $true)][string]$MigratorPath,
    [Parameter(Mandatory = $true)][string]$NatsPath,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT' -or $env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'NOT EXECUTED: this fixture requires a disposable GitHub-hosted Windows runner.'
}
$ServerPath = (Resolve-Path -LiteralPath $ServerPath).Path
$MigratorPath = (Resolve-Path -LiteralPath $MigratorPath).Path
$NatsPath = (Resolve-Path -LiteralPath $NatsPath).Path
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$suffix = [Guid]::NewGuid().ToString('N').Substring(0, 10)
$userName = 'wsa_' + $suffix
$otherUserName = 'wsa_o_' + $suffix
$groupName = 'wsa_g_' + $suffix
$database = 'wsa_windows_' + $suffix
$root = Join-Path $env:PUBLIC ('wsa-diag-' + $suffix)
$childOutput = Join-Path $root 'reports'
$report = [ordered]@{
    SchemaVersion = 1; Commit = $env:GITHUB_SHA; Topology = 'NOT EXECUTED'; AuthenticationMode = 'NOT EXECUTED'
    WindowsVersion = [Environment]::OSVersion.Version.ToString(); NativeSession = $null
    DomainCoverage = 'NOT EXECUTED'; LocalCoverage = 'NOT EXECUTED'; UnprovisionedUser = 'NOT EXECUTED'; UnprovisionedSession = $null; UnprovisionedExit = $null; FailureStage = 'Preflight'; FailureType = $null
    ServerObservations = @(); BrowserCorrelationMatches = @(); ConnectionEvidence = 'NOT CAPTURED'; Cleanup = 'PENDING'
}
$user = $null; $otherUser = $null; $group = $null; $server = $null; $nats = $null; $child = $null
$databaseCreated = $false; $startedPostgres = $false; $startedSecondaryLogon = $false
$postgresStartup = $null; $secondaryLogonStartup = $null
$oldEnvironment = @{}
$environmentKeys = @('PGPASSWORD', 'WSA_DATABASE_CONNECTION_STRING', 'ConnectionStrings__WellSiteAutoPilot',
    'ASPNETCORE_ENVIRONMENT', 'Security__AuthenticationMode', 'Security__BootstrapAdministrators__0',
    'Diagnostics__WindowsAuthentication', 'Logging__LogLevel__Default',
    'Logging__LogLevel__WellSiteAutoPilot.Server.Security.WindowsAuthenticationDiagnosticsMiddleware',
    'Logging__Console__FormatterName', 'Messaging__Nats__Url')
foreach ($key in $environmentKeys) { $oldEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, 'Process') }
function Stop-OwnedTree($Process) {
    if ($Process -and -not $Process.HasExited) {
        & taskkill.exe /PID $Process.Id /T /F *> $null
    }
}
try {
    New-Item -ItemType Directory -Path $childOutput -Force | Out-Null
    $report.FailureStage = 'EdgeDriverPreflight'
    $driverPath = Join-Path $env:EDGEWEBDRIVER 'msedgedriver.exe'
    if (-not (Test-Path -LiteralPath $driverPath -PathType Leaf)) { throw 'Edge WebDriver is missing.' }
    $report.FailureStage = 'PostgreSQLStartup'
    $pgBin = 'C:\Program Files\PostgreSQL\17\bin'
    $psql = Join-Path $pgBin 'psql.exe'
    if (-not (Test-Path -LiteralPath $psql)) { throw 'Expected runner PostgreSQL 17 installation is missing.' }
    $postgres = Get-Service 'postgresql-x64-17'
    $postgresStartup = $postgres.StartType
    if ($postgres.Status -ne 'Running') {
        if ($postgres.StartType -eq 'Disabled') { Set-Service $postgres -StartupType Manual }
        Start-Service $postgres
        $startedPostgres = $true
    }
    $report.FailureStage = 'SecondaryLogonStartup'
    $secondaryLogon = Get-Service 'seclogon'
    $secondaryLogonStartup = $secondaryLogon.StartType
    if ($secondaryLogon.Status -ne 'Running') {
        if ($secondaryLogon.StartType -eq 'Disabled') { Set-Service $secondaryLogon -StartupType Manual }
        Start-Service $secondaryLogon
        $startedSecondaryLogon = $true
    }
    # Public runner-image PostgreSQL fixture credential, unrelated to native logon.
    $report.FailureStage = 'DatabaseCreation'
    $env:PGPASSWORD = 'root'
    & $psql -h 127.0.0.1 -U postgres -d postgres -w -v ON_ERROR_STOP=1 -c "CREATE DATABASE $database" *> $null
    if ($LASTEXITCODE -ne 0) { throw 'Disposable database creation failed.' }
    $databaseCreated = $true
    $connection = "Host=127.0.0.1;Port=5432;Database=$database;Username=postgres;Password=root"
    $env:WSA_DATABASE_CONNECTION_STRING = $connection
    $report.FailureStage = 'Migrations'
    & $MigratorPath *> $null
    if ($LASTEXITCODE -ne 0) { throw 'Disposable database migrations failed.' }
    $report.FailureStage = 'NativeAccountProvisioning'
    # This password is never a command-line argument, environment variable or file.
    $password = ConvertTo-SecureString ('Wsa!9a' + [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(36))) -AsPlainText -Force
    $user = New-LocalUser -Name $userName -Password $password -AccountNeverExpires -PasswordNeverExpires
    $otherUser = New-LocalUser -Name $otherUserName -Password $password -AccountNeverExpires -PasswordNeverExpires
    $group = New-LocalGroup -Name $groupName
    Add-LocalGroupMember -Group $groupName -Member $userName
    # Explicit ordinary local logon rights; never make the fixture user an OS admin.
    Add-LocalGroupMember -SID ([Security.Principal.SecurityIdentifier]::new('S-1-5-32-545')) -Member $userName
    Add-LocalGroupMember -SID ([Security.Principal.SecurityIdentifier]::new('S-1-5-32-545')) -Member $otherUserName
    $credential = [Management.Automation.PSCredential]::new("$env:COMPUTERNAME\$userName", $password)
    Copy-Item (Join-Path $PSScriptRoot 'Invoke-NativeSession.ps1') $root
    Copy-Item (Join-Path $PSScriptRoot 'Invoke-BrowserProbe.ps1') $root
    Copy-Item (Join-Path $PSScriptRoot 'Invoke-UnprovisionedSession.ps1') $root
    Copy-Item (Join-Path $PSScriptRoot '../verify-windows-negotiate.ps1') $root
    $acl = Get-Acl -LiteralPath $root
    $rule = [Security.AccessControl.FileSystemAccessRule]::new($user.SID, 'Modify', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
    $acl.AddAccessRule($rule)
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($otherUser.SID, 'Modify', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
    Set-Acl -LiteralPath $root -AclObject $acl
    $report.FailureStage = 'ServerStartup'
    $nats = Start-Process -FilePath $NatsPath -ArgumentList @('-js', '-sd', (Join-Path $root 'nats')) -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $root 'nats.stdout') -RedirectStandardError (Join-Path $root 'nats.stderr')
    $env:ASPNETCORE_ENVIRONMENT = 'CI'
    $env:Security__AuthenticationMode = 'Negotiate'
    $env:Security__BootstrapAdministrators__0 = "$env:COMPUTERNAME\$userName"
    $env:Diagnostics__WindowsAuthentication = 'true'
    $env:ConnectionStrings__WellSiteAutoPilot = $connection
    $env:Messaging__Nats__Url = 'nats://127.0.0.1:4222'
    $env:Logging__LogLevel__Default = 'Warning'
    ${env:Logging__LogLevel__WellSiteAutoPilot.Server.Security.WindowsAuthenticationDiagnosticsMiddleware} = 'Information'
    $env:Logging__Console__FormatterName = 'simple'
    $server = Start-Process -FilePath $ServerPath -WorkingDirectory (Split-Path -Parent $ServerPath) `
        -ArgumentList @('--urls', 'http://localhost:5080') -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $root 'server.stdout') -RedirectStandardError (Join-Path $root 'server.stderr')
    $ready = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        if ($server.HasExited -or $nats.HasExited) { break }
        try { Invoke-WebRequest 'http://localhost:5080/health/live' -TimeoutSec 2 | Out-Null; $ready = $true; break }
        catch { Start-Sleep -Milliseconds 500 }
    }
    if (-not $ready) { throw 'Direct Kestrel did not become ready.' }
    $report.Topology = 'DirectKestrel'
    $report.AuthenticationMode = 'Negotiate'
    $report.FailureStage = 'NativeSession'
    $pwsh = (Get-Command pwsh.exe).Source
    # Only SID expectations, script paths and driver path appear on the command line.
    # Start-Process uses a real fresh logon with the in-memory credential object.
    $childArguments = @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', "`"$(Join-Path $root 'Invoke-NativeSession.ps1')`"",
        '-ExpectedUserSid', $user.SID.Value, '-ExpectedGroupSid', $group.SID.Value,
        '-OutputDirectory', "`"$childOutput`"", '-DriverPath', "`"$driverPath`"")
    $child = Start-Process -FilePath $pwsh -Credential $credential -LoadUserProfile -WorkingDirectory $root `
        -ArgumentList $childArguments -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $root 'native.stdout') -RedirectStandardError (Join-Path $root 'native.stderr')
    if (-not $child.WaitForExit(600000)) { throw 'Native diagnostic exceeded its time limit.' }
    $nativeReport = Join-Path $childOutput 'native-session.json'
    if (Test-Path -LiteralPath $nativeReport) {
        $report.NativeSession = Get-Content -LiteralPath $nativeReport -Raw | ConvertFrom-Json
    }
    # Second real logon: authenticated by Windows, but neither bootstrap administrator nor
    # in the fixture group. Reported separately; it does not alter the browser-policy verdict.
    $report.FailureStage = 'UnprovisionedSession'
    $otherCredential = [Management.Automation.PSCredential]::new("$env:COMPUTERNAME\$otherUserName", $password)
    $otherArguments = @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', "`"$(Join-Path $root 'Invoke-UnprovisionedSession.ps1')`"",
        '-ExpectedUserSid', $otherUser.SID.Value, '-ForbiddenGroupSid', $group.SID.Value, '-OutputDirectory', "`"$childOutput`"")
    $other = Start-Process -FilePath $pwsh -Credential $otherCredential -LoadUserProfile -WorkingDirectory $root `
        -ArgumentList $otherArguments -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $root 'other.stdout') -RedirectStandardError (Join-Path $root 'other.stderr')
    if (-not $other.WaitForExit(120000)) { Stop-OwnedTree $other; $report.UnprovisionedUser = 'FAILED' }
    else {
        $otherReport = Join-Path $childOutput 'unprovisioned-session.json'
        if ($other.ExitCode -eq 0 -and (Test-Path -LiteralPath $otherReport)) { $report.UnprovisionedUser = 'PASSED' }
        else { $report.UnprovisionedUser = 'FAILED' }
        $report.UnprovisionedExit = $other.ExitCode
        if (Test-Path -LiteralPath $otherReport) {
            Copy-Item -LiteralPath $otherReport -Destination $OutputDirectory
            $report.UnprovisionedSession = Get-Content -LiteralPath $otherReport -Raw | ConvertFrom-Json
        }
    }
    $report.FailureStage = 'NativeSession'
    # These files are produced exclusively by our checked-in sanitized reporters.
    foreach ($policy in @('Default', 'OriginAllowlist')) {
        $path = Join-Path $childOutput ($policy + '.json')
        if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination $OutputDirectory }
    }
    if ($child.ExitCode -eq 0 -and $report.NativeSession.Status -eq 'PASSED') { $report.LocalCoverage = 'PASSED' }
    else { $report.LocalCoverage = 'FAILED' }
} catch {
    $report.FailureType = $_.Exception.GetType().Name
    $report.LocalCoverage = 'FAILED'
    # FailureStage is a fixed vocabulary. Never export the raw exception.
} finally {
    Stop-OwnedTree $child
    Stop-OwnedTree $server
    Stop-OwnedTree $nats
    $log = Join-Path $root 'server.stdout'
    if (Test-Path -LiteralPath $log) {
        $observations = @()
        foreach ($line in Get-Content -LiteralPath $log) {
            if ($line -match 'WSA_WINDOWS_AUTH (\{.*\})$') {
                try {
                    $entry = $Matches[1] | ConvertFrom-Json
                    $observations += $entry | Select-Object ObservedAtUtc, Endpoint, Protocol, Status, Authenticated,
                        AuthenticationKind, NegotiateChallenge, CorrelationId, ConnectionId, ElapsedMilliseconds
                } catch { }
            }
        }
        $report.ServerObservations = $observations
        $hasChallenge = @($observations | Where-Object { $_.Status -eq 401 -and $_.NegotiateChallenge }).Count -gt 0
        $hasSuccess = @($observations | Where-Object { $_.Status -eq 200 -and $_.Authenticated }).Count -gt 0
        if ($hasChallenge -and $hasSuccess) { $report.ConnectionEvidence = 'CAPTURED' }
    }
    # Browser calls must link to their own final server observation; baseline
    # traffic alone cannot satisfy browser connection evidence.
    $allBrowserCallsMatched = $true
    foreach ($policy in @('Default', 'OriginAllowlist')) {
        $path = Join-Path $OutputDirectory ($policy + '.json')
        if (-not (Test-Path -LiteralPath $path)) { $allBrowserCallsMatched = $false; continue }
        try {
            $probe = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
            foreach ($scenario in $probe.Scenarios) {
                if ($scenario.Name -eq 'IdentityNavigation') { continue }
                $matched = 0
                foreach ($call in $scenario.Observations) {
                    if ($call.correlationId -and @($report.ServerObservations | Where-Object {
                        $_.CorrelationId -eq $call.correlationId -and $_.Status -eq $call.status -and $_.ConnectionId
                    }).Count -gt 0) { $matched++ }
                }
                $report.BrowserCorrelationMatches += @{
                    Policy = $policy; Scenario = $scenario.Name; MatchedCalls = $matched
                    ExpectedCalls = @($scenario.Observations).Count
                }
                if ($matched -ne 7 -or $matched -ne @($scenario.Observations).Count) { $allBrowserCallsMatched = $false }
            }
        } catch { $allBrowserCallsMatched = $false }
    }
    if ($report.ConnectionEvidence -ne 'CAPTURED' -or -not $allBrowserCallsMatched) {
        if ($report.LocalCoverage -eq 'PASSED') { $report.FailureStage = 'ConnectionEvidence' }
        $report.LocalCoverage = 'FAILED'
    }
    $cleanupPassed = $true
    if ($databaseCreated) {
        try {
            & $psql -h 127.0.0.1 -U postgres -d postgres -w -v ON_ERROR_STOP=1 -c "DROP DATABASE $database WITH (FORCE)" *> $null
            if ($LASTEXITCODE -ne 0) { $cleanupPassed = $false }
        } catch { $cleanupPassed = $false }
    }
    if ($user) {
        try {
            # A failed child may have exited before its browser descendants.
            # Scope orphan cleanup to this fixture's unique native account SID.
            foreach ($process in Get-CimInstance Win32_Process) {
                $owner = Invoke-CimMethod -InputObject $process -MethodName GetOwnerSid -ErrorAction SilentlyContinue
                if ($owner -and ($owner.Sid -eq $user.SID.Value -or ($otherUser -and $owner.Sid -eq $otherUser.SID.Value))) {
                    Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
                }
            }
            # Remove only this fixture's loaded profile and account.
            Get-CimInstance Win32_UserProfile | Where-Object { $_.SID -eq $user.SID.Value } | Remove-CimInstance
            Remove-LocalUser -SID $user.SID
            if ($otherUser) {
                Get-CimInstance Win32_UserProfile | Where-Object { $_.SID -eq $otherUser.SID.Value } | Remove-CimInstance
                Remove-LocalUser -SID $otherUser.SID
            }
        } catch { $cleanupPassed = $false }
    }
    if ($group) { try { Remove-LocalGroup -SID $group.SID } catch { $cleanupPassed = $false } }
    if ($startedPostgres) { try { Stop-Service 'postgresql-x64-17' } catch { $cleanupPassed = $false } }
    if ($startedSecondaryLogon) { try { Stop-Service 'seclogon' } catch { $cleanupPassed = $false } }
    if ($null -ne $postgresStartup) {
        try { Set-Service 'postgresql-x64-17' -StartupType $postgresStartup } catch { $cleanupPassed = $false }
    }
    if ($null -ne $secondaryLogonStartup) {
        try { Set-Service 'seclogon' -StartupType $secondaryLogonStartup } catch { $cleanupPassed = $false }
    }
    foreach ($key in $environmentKeys) { [Environment]::SetEnvironmentVariable($key, $oldEnvironment[$key], 'Process') }
    try { if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force } } catch { $cleanupPassed = $false }
    $report.Cleanup = if ($cleanupPassed) { 'PASSED' } else { 'FAILED' }
    if (-not $cleanupPassed) {
        if ($report.LocalCoverage -eq 'PASSED') { $report.FailureStage = 'Cleanup' }
        $report.LocalCoverage = 'FAILED'
    }
    $credential = $null
    if (Get-Variable password -ErrorAction SilentlyContinue) { $password.Dispose() }
    if ($report.LocalCoverage -eq 'PASSED') { $report.FailureStage = $null }
    $report | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'fixture.json') -Encoding utf8
}
if ($report.LocalCoverage -ne 'PASSED') { throw "Native Windows diagnostic failed at stage $($report.FailureStage). Inspect only the sanitized reports." }
