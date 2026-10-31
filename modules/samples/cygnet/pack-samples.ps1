$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
foreach ($dir in Get-ChildItem $PSScriptRoot -Directory) {
    $payload = Join-Path $root "artifacts\module-payloads\$($dir.Name)"
    dotnet publish (Join-Path $dir.FullName "$($dir.Name).csproj") -c Release -o $payload
    if ($LASTEXITCODE) { throw "publish failed: $($dir.Name)" }
    Write-Host "Payload for $($dir.Name): $payload (pack with the CLI 'module pack'; see .github/workflows/ci.yml)"
}
