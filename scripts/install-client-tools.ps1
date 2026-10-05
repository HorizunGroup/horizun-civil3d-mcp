#Requires -Version 5.1
<# Install ChatGPT helpers independently; the Civil 3D plugin and server stay running. #>
[CmdletBinding()]
param([switch]$DryRun)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'client-tools.package.ps1')
$installRoot = Join-Path $env:LOCALAPPDATA 'Programs\Horizun\Civil3D-MCP'
$destination = Join-Path $installRoot 'server\client-tools'
$server = Join-Path $installRoot 'server\horizun-civil3d-mcp.exe'
if (-not (Test-Path -LiteralPath $server)) { throw 'Install Civil 3D MCP first with scripts/install.ps1.' }
foreach ($file in $script:HorizunCivil3DClientTools) {
    if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot $file))) { throw "Missing helper: $file" }
}
if ($DryRun) { Write-Host "$($script:HorizunCivil3DClientTools.Count) helpers ready for $destination"; exit 0 }
$backup = Join-Path $installRoot ('_backup\client-tools-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$previous = @{}
New-Item -ItemType Directory -Path $backup -Force | Out-Null
foreach ($file in $script:HorizunCivil3DClientTools) {
    $target = Join-Path $destination $file
    $previous[$file] = Test-Path -LiteralPath $target
    if ($previous[$file]) { Copy-Item -LiteralPath $target -Destination (Join-Path $backup $file) }
}
try {
    Copy-HorizunCivil3DClientTools -Destination $destination
    $hashes = [ordered]@{}
    foreach ($file in $script:HorizunCivil3DClientTools) {
        $expected = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $file) -Algorithm SHA256).Hash
        $actual = (Get-FileHash -LiteralPath (Join-Path $destination $file) -Algorithm SHA256).Hash
        if ($expected -ne $actual) { throw "Hash mismatch: $file" }
        $hashes[$file] = $actual
    }
    [ordered]@{ product='Horizun Civil 3D MCP client tools'; installed_utc=[DateTime]::UtcNow.ToString('o'); destination=$destination; backup=$backup; sha256=$hashes } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $installRoot 'client-tools-manifest.json') -Encoding UTF8
}
catch {
    foreach ($file in $script:HorizunCivil3DClientTools) {
        $target = Join-Path $destination $file
        if ($previous[$file]) { Copy-Item -LiteralPath (Join-Path $backup $file) -Destination $target -Force }
        elseif (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
    }
    throw
}
Write-Host "Installed and SHA-256 verified $($hashes.Count) helpers. Backup: $backup"
Write-Host "Next: & '$destination\connect-chatgpt.ps1'"
