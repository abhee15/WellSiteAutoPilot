# POC: exercise the guided-authoring API with contract-only (complex) drafts.
# Usage: .\run-api-tests.ps1 [-Base http://localhost:5080/api/v1]
param([string]$Base = 'http://localhost:5080/api/v1')

$results = New-Object System.Collections.Generic.List[object]
$created = New-Object System.Collections.Generic.List[string]

function Call($method, $path, $body) {
    $a = @{ Uri = "$Base/$path"; Method = $method; UseDefaultCredentials = $true; AllowUnencryptedAuthentication = $true; TimeoutSec = 60 }
    if ($null -ne $body) { $a.Body = ($body | ConvertTo-Json -Depth 20 -Compress); $a.ContentType = 'application/json' }
    try {
        $r = Invoke-RestMethod @a
        return @{ ok = $true; status = 200; body = $r; text = '' }
    } catch {
        $text = ''
        try { $text = $_.ErrorDetails.Message } catch {}
        if (-not $text) { $text = $_.Exception.Message }
        $code = 0; try { $code = [int]$_.Exception.Response.StatusCode } catch {}
        return @{ ok = $false; status = $code; body = $null; text = $text }
    }
}

function Record($id, $title, $r, $verdict) {
    $detail = if ($r.ok) { 'HTTP 200' } else { "HTTP $($r.status) $($r.text)" }
    if ($detail.Length -gt 260) { $detail = $detail.Substring(0, 260) }
    $results.Add([pscustomobject]@{ Id = $id; Title = $title; Verdict = $verdict; Detail = $detail })
    "{0,-4} {1,-5} {2}  [{3}]" -f $id, $verdict, $title, $detail
}

$root = 'D:\P4\WFT_Services\AutonomousModules\WellSiteAutoPilot\Trunk'
$manifest = Get-Content "$root\modules\samples\structured-cards\manifest.json" -Raw

$well = $null
foreach ($q in 'RPOC_0413', 'RPOC_00016') {
    $hits = @((Call GET "assets/search?q=$q&limit=20&offset=0" $null).body | ? { $_.isActive -and $_.ownerConnectionId })
    if ($hits.Count) { $well = $hits[0]; break }
}
"well asset: $($well.name) $($well.id)"

$r = Call POST 'guided-module-drafts' @{ manifestJson = $manifest; definition = $null; expectedRevision = $null }
$draft = $r.body
Record 'T1' 'Create contract-only draft (structured-cards manifest, no rule)' $r $(if ($r.ok) { 'PASS' } else { 'FAIL' })
if (-not $draft) { $results | Export-Clixml "$PSScriptRoot\results.xml"; return }
$created.Add($draft.id)

$r = Call GET "guided-module-drafts/$($draft.id)" $null
Record 'T2' 'Read back: definition is null, manifest round-trips' $r $(if ($r.ok -and $null -eq $r.body.definition -and $r.body.manifestJson.Length -gt 100) { 'PASS' } else { 'FAIL' })
$stored = $r.body.manifestJson | ConvertFrom-Json
"  entryPoint stored by server: $($stored.entryPoint)"

$r = Call GET 'guided-module-drafts?limit=100' $null
Record 'T3' 'Draft appears in list' $r $(if ($r.ok -and (@($r.body | ? { $_.id -eq $draft.id }).Count -eq 1)) { 'PASS' } else { 'FAIL' })

$m2 = $manifest | ConvertFrom-Json; $m2.displayName = 'POC complex renamed'
$r = Call PUT "guided-module-drafts/$($draft.id)" @{ manifestJson = ($m2 | ConvertTo-Json -Depth 30 -Compress); definition = $null; expectedRevision = $draft.revision }
Record 'T4' 'Edit with current revision' $r $(if ($r.ok -and $r.body.revision -eq $draft.revision + 1) { 'PASS' } else { 'FAIL' })
$rev = if ($r.ok) { $r.body.revision } else { $draft.revision }

$r = Call PUT "guided-module-drafts/$($draft.id)" @{ manifestJson = $manifest; definition = $null; expectedRevision = $draft.revision }
Record 'T5' 'Edit with stale revision is rejected' $r $(if (-not $r.ok) { 'PASS' } else { 'FAIL' })

$r = Call PUT "guided-module-drafts/$($draft.id)" @{ manifestJson = $manifest; definition = @{ parameter = 'threshold'; default = 100; outcomeCode = 'X'; title = 't'; detail = 'd' }; expectedRevision = $rev }
Record 'T6' 'Cannot switch contract-only draft to rule draft' $r $(if (-not $r.ok) { 'PASS' } else { 'FAIL' })

$m3 = $manifest | ConvertFrom-Json
$m3 | Add-Member -NotePropertyName commandRequirements -NotePropertyValue @(@{ id = 'x'; assetRole = 'well'; command = 'WriteSetpoint'; quantity = 'PumpFillage' }) -Force
$r = Call POST 'guided-module-drafts' @{ manifestJson = ($m3 | ConvertTo-Json -Depth 30 -Compress); definition = $null }
if ($r.ok) { $created.Add($r.body.id) }
Record 'T7' 'Contract-only manifest declaring commands' $r $(if (-not $r.ok) { 'PASS' } else { 'FAIL' })

$m4 = $manifest | ConvertFrom-Json; $m4.dataRequirements = @()
$r = Call POST 'guided-module-drafts' @{ manifestJson = ($m4 | ConvertTo-Json -Depth 30 -Compress); definition = $null }
if ($r.ok) { $created.Add($r.body.id) }
Record 'T8' 'Contract-only manifest with zero inputs (observe)' $r 'INFO'

$r = Call POST 'guided-module-drafts' @{ manifestJson = '{not json'; definition = $null }
Record 'T9' 'Malformed manifest JSON' $r $(if (-not $r.ok) { 'PASS' } else { 'FAIL' })
$r = Call POST 'guided-module-drafts' @{ manifestJson = (' ' * 70000); definition = $null }
Record 'T10' 'Oversize manifest (70k)' $r $(if (-not $r.ok) { 'PASS' } else { 'FAIL' })

$r = Call POST "guided-module-drafts/$($draft.id)/preview" @{ expectedRevision = $rev; value = 1; parametersJson = '{}' }
Record 'T11' 'Sample-case preview fails with UNSUPPORTED' $r $(if (-not $r.ok -and $r.text -match 'UNSUPPORTED') { 'PASS' } else { 'FAIL' })
$r = Call POST "guided-module-drafts/$($draft.id)/submit" @{ expectedRevision = $rev; readings = @(1); pressureReadings = $null }
Record 'T12' 'Submit for review fails with UNSUPPORTED' $r $(if (-not $r.ok -and $r.text -match 'UNSUPPORTED') { 'PASS' } else { 'FAIL' })
$r = Call POST "guided-module-drafts/$($draft.id)/live-preview" @{ expectedRevision = $rev; assetId = $well.id }
Record 'T13' 'Live preview fails with UNSUPPORTED' $r $(if (-not $r.ok -and $r.text -match 'UNSUPPORTED') { 'PASS' } else { 'FAIL' })

$r = Call POST "guided-module-drafts/$($draft.id)/asset-compatibility" @{ expectedRevision = $rev; assetId = $well.id }
Record 'T14' "Asset compatibility ($($well.name))" $r $(if ($r.ok) { 'PASS' } else { 'FAIL' })
if ($r.ok) { $r.body.inputs | % { "  input=$($_.inputId) quantity=$($_.quantity) status=$($_.status) reason=$($_.reasonCode)" } }
$r = Call POST "guided-module-drafts/$($draft.id)/surface-cards-preview" @{ expectedRevision = $rev; assetId = $well.id }
Record 'T15' "Surface-cards preview ($($well.name))" $r $(if ($r.ok) { 'PASS' } else { 'INFO' })
if ($r.ok) { "  cards=$(@($r.body.cards).Count) quality=$($r.body.quality)" }
$r = Call POST "guided-module-drafts/$($draft.id)/quantity-discovery" @{ expectedRevision = $rev; assetId = $well.id; capability = 'Surveillance'; startUtc = (Get-Date).AddDays(-7).ToUniversalTime().ToString('o'); endUtc = (Get-Date).ToUniversalTime().ToString('o') }
Record 'T16' 'Quantity discovery on a card-only contract (observe)' $r 'INFO'

$r = Call GET 'guided-module-drafts?limit=100' $null
Record 'T17' 'Existing rule drafts still list with their rule' $r $(if ($r.ok -and (@($r.body | ? { $_.definition }).Count -ge 1)) { 'PASS' } else { 'FAIL' })

$results | Export-Clixml "$PSScriptRoot\results.xml"
$created | Set-Content "$PSScriptRoot\created-drafts.txt"
"`nsummary: " + (($results | group Verdict | % { "$($_.Name)=$($_.Count)" }) -join '  ')
