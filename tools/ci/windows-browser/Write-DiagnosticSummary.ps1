# Publishes the sanitized native Windows diagnostic outcome where it can be read
# without downloading the artifact: GitHub annotations (also available through the
# check-runs API) and the job summary. Reads only the three sanitized reports.
# It never fails the job and never prints exception text, names, SIDs or headers.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ReportDirectory
)
$ErrorActionPreference = 'Continue'

# Report fields come from fixed vocabularies. Reduce any value to a safe token so
# it can never inject a workflow command or markup into the annotation or summary.
function ConvertTo-SafeToken($Value) {
    if ($null -eq $Value) { return 'none' }
    $text = ([string]$Value) -replace '[^A-Za-z0-9 _.;=+-]', '?'
    if ($text.Length -gt 80) { $text = $text.Substring(0, 80) }
    return $text
}

function Read-Report([string]$Name) {
    $path = Join-Path $ReportDirectory $Name
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    try { return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json } catch { return $null }
}

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('## Native Windows browser diagnostic')
$lines.Add('')

try {
    $fixture = Read-Report 'fixture.json'
    if (-not $fixture) {
        $message = 'fixture.json was not written: the fixture stopped before its report step. Open the job log.'
        Write-Host "::error title=Native Windows diagnostic::$message"
        $lines.Add("**No fixture report.** $message")
    }
    else {
        $stage = ConvertTo-SafeToken $fixture.FailureStage
        $type = ConvertTo-SafeToken $fixture.FailureType
        $coverage = ConvertTo-SafeToken $fixture.LocalCoverage
        $cleanup = ConvertTo-SafeToken $fixture.Cleanup
        $evidence = ConvertTo-SafeToken $fixture.ConnectionEvidence
        $unprovisioned = ConvertTo-SafeToken $fixture.UnprovisionedUser
        $summary = "LocalCoverage=$coverage FailureStage=$stage FailureType=$type ConnectionEvidence=$evidence Cleanup=$cleanup UnprovisionedUser=$unprovisioned"
        if ($fixture.LocalCoverage -eq 'PASSED') { Write-Host "::notice title=Native Windows diagnostic::$summary" }
        else { Write-Host "::error title=Native Windows diagnostic::$summary" }

        $lines.Add('| Field | Value |')
        $lines.Add('| --- | --- |')
        $lines.Add("| Local coverage | $coverage |")
        $lines.Add("| Failure stage | $stage |")
        $lines.Add("| Failure type | $type |")
        $lines.Add("| Connection evidence | $evidence |")
        $lines.Add("| Cleanup | $cleanup |")
        $lines.Add("| Topology | $(ConvertTo-SafeToken $fixture.Topology) |")
        $lines.Add("| Authentication mode | $(ConvertTo-SafeToken $fixture.AuthenticationMode) |")

        $other = $fixture.UnprovisionedSession
        $otherText = "Result=$unprovisioned Exit=$(ConvertTo-SafeToken $fixture.UnprovisionedExit)"
        if ($other) {
            $otherText += " Status=$(ConvertTo-SafeToken $other.Status) FailureStage=$(ConvertTo-SafeToken $other.FailureStage) " +
                "UserMatches=$(ConvertTo-SafeToken $other.NativeUserMatches) InFixtureGroup=$(ConvertTo-SafeToken $other.InFixtureGroup) " +
                "PermissionCount=$(ConvertTo-SafeToken $other.PermissionCount) IdentityRequestStatus=$(ConvertTo-SafeToken $other.IdentityRequestStatus) Expected=$(ConvertTo-SafeToken $other.Expected)"
        }
        Write-Host "::notice title=Unprovisioned user::$otherText"
        $lines.Add("| Unprovisioned user | $otherText |")

        $native = $fixture.NativeSession
        if ($native) {
            $nativeText = "Status=$(ConvertTo-SafeToken $native.Status) FailureStage=$(ConvertTo-SafeToken $native.FailureStage) " +
                "UserMatches=$(ConvertTo-SafeToken $native.NativeUserMatches) GroupMatches=$(ConvertTo-SafeToken $native.EnabledGroupMatches) " +
                "Baseline=$(ConvertTo-SafeToken $native.Baseline) IdentityRequestStatus=$(ConvertTo-SafeToken $native.IdentityRequestStatus) DisplayNameResolved=$(ConvertTo-SafeToken $native.DisplayNameResolved) " +
                "IdentityRequestFailure=$(ConvertTo-SafeToken $native.IdentityRequestFailure) IdentityRequestErrorId=$(ConvertTo-SafeToken $native.IdentityRequestErrorId) IdentityMatches=$(ConvertTo-SafeToken $native.IdentityMatches)"
            Write-Host "::notice title=Native session::$nativeText"
            $lines.Add('')
            $lines.Add("**Native session:** $nativeText")
        }

        # Status counts only; no endpoints, identities, headers or correlation values.
        $observations = @($fixture.ServerObservations)
        if ($observations.Count -gt 0) {
            $counts = $observations | Group-Object Status | Sort-Object Name |
                ForEach-Object { "$(ConvertTo-SafeToken $_.Name)x$($_.Count)" }
            $challenges = @($observations | Where-Object { $_.NegotiateChallenge }).Count
            $text = "Server observations=$($observations.Count) statuses=$($counts -join ',') negotiateChallenges=$challenges"
            Write-Host "::notice title=Server observations::$text"
            $lines.Add('')
            $lines.Add("**Server observations:** $text")
        }
    }

    foreach ($policy in @('Default', 'OriginAllowlist')) {
        $probe = Read-Report ($policy + '.json')
        $lines.Add('')
        if (-not $probe) {
            Write-Host "::warning title=Browser policy $policy::No report was produced."
            $lines.Add("### $policy browser policy: no report")
            continue
        }
        $lines.Add("### $policy browser policy: $(ConvertTo-SafeToken $probe.LocalCoverage)")
        $lines.Add('| Scenario | Status | Failure stage | Calls | Identity match |')
        $lines.Add('| --- | --- | --- | --- | --- |')
        $overview = New-Object System.Collections.Generic.List[string]
        foreach ($scenario in @($probe.Scenarios)) {
            $name = ConvertTo-SafeToken $scenario.Name
            $status = ConvertTo-SafeToken $scenario.Status
            $failed = ConvertTo-SafeToken $scenario.FailureStage
            $calls = @($scenario.Observations)
            $codes = ($calls | ForEach-Object { ConvertTo-SafeToken $_.status } | Group-Object | Sort-Object Name |
                ForEach-Object { "$($_.Name)x$($_.Count)" }) -join ','
            $lines.Add("| $name | $status | $failed | $($calls.Count) ($codes) | $(ConvertTo-SafeToken $scenario.IdentityMatches) |")
            $overview.Add("$name=$status($codes)")
            if ($scenario.Status -ne 'PASSED') {
                Write-Host "::error title=Browser $policy $name::Status=$status FailureStage=$failed Calls=$($calls.Count) Statuses=$codes"
            }
        }
        # Passing scenarios must be visible too, not only failures.
        Write-Host "::notice title=Browser policy $policy::LocalCoverage=$(ConvertTo-SafeToken $probe.LocalCoverage) Scenarios $($overview -join ' ')"
    }
}
catch {
    # Reporting must never mask the diagnostic result or leak details.
    Write-Host '::warning title=Diagnostic summary::The summary step could not read all reports.'
}

if ($env:GITHUB_STEP_SUMMARY) {
    $lines | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8
}
exit 0
