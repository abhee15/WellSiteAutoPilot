# Explicitly opt-in, read-only AD feasibility probe. Never called by the Server/API.
# Input and output are sensitive; only fixed, sanitized conclusions are printed.
# A transitive AD SID snapshot is NOT an enabled Windows token or an access verdict.
[CmdletBinding(DefaultParameterSetName = 'Live')]
param(
    [Parameter(ParameterSetName = 'Live', Mandatory = $true)][string]$TargetUserSid,
    [Parameter(ParameterSetName = 'Live', Mandatory = $true)][string]$ExpectedGroupSid,
    [Parameter(ParameterSetName = 'Live', Mandatory = $true)][switch]$EnableLiveDirectoryRead,
    [Parameter(ParameterSetName = 'SelfTest', Mandatory = $true)][switch]$SelfTest,
    [ValidateRange(2, 15)][int]$TimeoutSeconds = 5
)
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'NOT EXECUTED: this probe requires Windows.' }

function ConvertTo-LdapSidAssertion([Security.Principal.SecurityIdentifier]$Sid) {
    # LDAP filter uses escaped binary SID bytes, NEVER a name or raw SID text.
    $bytes = [byte[]]::new($Sid.BinaryLength)
    $Sid.GetBinaryForm($bytes, 0)
    return (($bytes | ForEach-Object { '\{0:X2}' -f $_ }) -join '')
}

if ($SelfTest) {
    # Deterministic, domain-free Windows CI check: no LDAP bind, credentials or HTTP.
    $example = [Security.Principal.SecurityIdentifier]::new('S-1-5-21-101-102-103-104')
    $assertion = ConvertTo-LdapSidAssertion $example
    if (-not $assertion.StartsWith('\01\05', [StringComparison]::Ordinal) -or
        $assertion.Length -ne 3 * $example.BinaryLength -or
        $assertion.Contains('(') -or $assertion.Contains(')') -or
        $assertion.Contains('*')) {
        throw 'SID-to-LDAP binary assertion self-test failed.'
    }
    [pscustomobject]@{
        SchemaVersion = 1; State = 'SelfTestPassed'; LiveDirectoryQueried = $false;
        CurrentWindowsTokenInspected = $false; CanAuthorize = $false
    } | ConvertTo-Json -Compress
    return
}

# The script has no LDAP server, credentials, DN, username or site parameter.
# The operator must intentionally enable exactly one bounded live domain read.
$report = [ordered]@{
    SchemaVersion = 1
    State = 'NotExecuted'
    Evidence = 'AdTransitiveSidSnapshotNotWindowsToken'
    UserResolved = $false
    AdGroupSidCount = $null
    ExpectedGroupInAdSnapshot = $null
    ProcessTokenComparison = 'NotComparable'
    ExpectedGroupEnabledInProcessToken = $null
    ProvesEffectiveAccess = $false
    ProvesGroupAbsence = $false
    DirectoryCompleteness = 'Unproven'
    FailureStage = $null
    DirectoryReadMilliseconds = $null
}
$stage = 'ValidateSids'
$rootDse = $null
$searchRoot = $null
$finder = $null
$matches = $null
$userEntry = $null
$baseReader = $null
try {
    if (-not $EnableLiveDirectoryRead) {
        throw 'Explicit directory-read opt-in is required.'
    }
    # Invalid/unbounded external IDs are rejected before the first LDAP operation.
    if ($TargetUserSid.Length -gt 184 -or $ExpectedGroupSid.Length -gt 184) {
        throw 'Invalid SID input.'
    }
    $userSid = [Security.Principal.SecurityIdentifier]::new($TargetUserSid)
    $groupSid = [Security.Principal.SecurityIdentifier]::new($ExpectedGroupSid)
    if ($userSid.Value -ne $TargetUserSid -or $groupSid.Value -ne $ExpectedGroupSid) {
        throw 'Noncanonical SID input.'
    }
    $filterSid = ConvertTo-LdapSidAssertion $userSid

    $stage = 'JoinedDomain'
    # The host chooses its joined domain and integrated service identity; callers
    # cannot supply an LDAP endpoint or credentials.
    Add-Type -AssemblyName System.DirectoryServices
    $joinedDomain = [DirectoryServices.ActiveDirectory.Domain]::GetComputerDomain()
    try {
        if ([string]::IsNullOrWhiteSpace($joinedDomain.Name)) {
            throw 'Joined domain was not resolved.'
        }
    } finally { $joinedDomain.Dispose() }
    $rootDse = [DirectoryServices.DirectoryEntry]::new('LDAP://RootDSE')
    $defaultNamingContext = [string]$rootDse.Properties['defaultNamingContext'].Value
    if ([string]::IsNullOrWhiteSpace($defaultNamingContext)) {
        throw 'No joined-domain naming context.'
    }
    $timeout = [TimeSpan]::FromSeconds($TimeoutSeconds)
    $searchRoot = [DirectoryServices.DirectoryEntry]::new('LDAP://' + $defaultNamingContext)

    $stage = 'ResolveTargetBySid'
    $duration = [Diagnostics.Stopwatch]::StartNew()
    $finder = [DirectoryServices.DirectorySearcher]::new($searchRoot)
    $finder.Filter = '(&(objectCategory=person)(objectClass=user)(objectSid=' + $filterSid + '))'
    $finder.SearchScope = [DirectoryServices.SearchScope]::Subtree
    $finder.SizeLimit = 2  # Reject ambiguous matches; never enumerate users.
    $finder.PageSize = 0
    $finder.ClientTimeout = $timeout
    $finder.ServerTimeLimit = $timeout
    [void]$finder.PropertiesToLoad.Add('distinguishedName')
    $matches = $finder.FindAll()
    if ($matches.Count -ne 1) {
        throw 'The target SID did not resolve uniquely in the joined domain.'
    }
    $report.UserResolved = $true
    $userEntry = $matches[0].GetDirectoryEntry()

    $stage = 'ReadTransitiveSids'
    # Microsoft specifies a BASE-scope query for this expensive computed attribute.
    $baseReader = [DirectoryServices.DirectorySearcher]::new($userEntry)
    $baseReader.SearchScope = [DirectoryServices.SearchScope]::Base
    $baseReader.Filter = '(objectClass=*)'
    $baseReader.SizeLimit = 1
    $baseReader.PageSize = 0
    $baseReader.ClientTimeout = $timeout
    $baseReader.ServerTimeLimit = $timeout
    [void]$baseReader.PropertiesToLoad.Add('tokenGroups')
    $result = $baseReader.FindOne()
    if ($null -eq $result -or -not $result.Properties.Contains('tokengroups')) {
        throw 'The computed tokenGroups attribute was not returned.'
    }
    $groupBytes = $result.Properties['tokengroups']
    if ($groupBytes.Count -lt 1 -or $groupBytes.Count -gt 2048) {
        # No returned values is NOT a verified empty directory membership.
        throw 'The returned group set is absent or exceeds the supported bound.'
    }

    $stage = 'ValidateSnapshot'
    $expectedFound = $false
    foreach ($value in $groupBytes) {
        if ($value -isnot [byte[]]) { throw 'A group value is not a binary SID.' }
        $resolvedSid = [Security.Principal.SecurityIdentifier]::new([byte[]]$value, 0)
        if ($resolvedSid.Value -eq $groupSid.Value) { $expectedFound = $true }
    }
    $report.AdGroupSidCount = [int]$groupBytes.Count
    $report.ExpectedGroupInAdSnapshot = $expectedFound

    # Optional same-process comparison ONLY. No impersonation, no other-user
    # WindowsPrincipal and no inference about an active browser session.
    $stage = 'NativeProcessToken'
    $native = [Security.Principal.WindowsIdentity]::GetCurrent()
    if ($native.IsAuthenticated -and $null -ne $native.User -and
        $native.User.Value -eq $userSid.Value) {
        $report.ProcessTokenComparison = 'SameWindowsProcessOnly'
        $principal = [Security.Principal.WindowsPrincipal]::new($native)
        $report.ExpectedGroupEnabledInProcessToken = $principal.IsInRole($groupSid)
    }

    $report.DirectoryReadMilliseconds = [long]$duration.ElapsedMilliseconds
    $report.State = 'DirectorySnapshotOnly'
}
catch {
    # Never print exception text, LDAP path, SID, username, domain, token or credentials.
    $report.State = if ($stage -eq 'ValidateSids') { 'InvalidInput' } else { 'Unavailable' }
    if ($null -ne (Get-Variable -Name duration -ErrorAction SilentlyContinue)) {
        $report.DirectoryReadMilliseconds = [long]$duration.ElapsedMilliseconds
    }
    $report.FailureStage = $stage
    $report.AdGroupSidCount = $null
    $report.ExpectedGroupInAdSnapshot = $null
    $report.ExpectedGroupEnabledInProcessToken = $null
    $report.ProcessTokenComparison = 'NotComparable'
}
finally {
    # Dispose search collection before the bound objects; no resource is cached.
    foreach ($item in @($matches, $baseReader, $finder, $userEntry, $searchRoot, $rootDse)) {
        if ($null -ne $item -and $item -is [IDisposable]) {
            try { $item.Dispose() } catch { }
        }
    }
}
[pscustomobject]$report | ConvertTo-Json -Compress -Depth 4
if ($report.State -ne 'DirectorySnapshotOnly') { exit 2 }
