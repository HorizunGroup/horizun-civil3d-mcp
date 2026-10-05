#Requires -Version 5.1
<# Fetch the official FULL Windows client; verify release checksum before extraction/execution. #>
[CmdletBinding()]
param([ValidatePattern('^v\d+\.\d+\.\d+$')][string]$Version, [string]$StateRoot)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'chatgpt-tunnel.lib.ps1')
$StateRoot = Get-HorizunTunnelStateRoot $StateRoot
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$endpoint = 'https://api.github.com/repos/openai/tunnel-client/releases/' + $(if ($Version) { 'tags/' + $Version } else { 'latest' })
$release = Invoke-RestMethod -Uri $endpoint -Headers @{ 'User-Agent'='Horizun-Civil3D-MCP' } -TimeoutSec 30
if ($release.tag_name -notmatch '^v\d+\.\d+\.\d+$' -or $release.draft -or $release.prerelease) { throw 'Expected a stable official tunnel-client release.' }
$assetName = 'tunnel-client-' + $release.tag_name + '-windows-' + (Get-HorizunWindowsArchitecture) + '.zip'
$asset = @($release.assets | Where-Object { $_.name -ceq $assetName })
$sums = @($release.assets | Where-Object { $_.name -ceq 'SHA256SUMS.txt' })
if ($asset.Count -ne 1 -or $sums.Count -ne 1) { throw "Official release is missing $assetName or SHA256SUMS.txt." }
$prefix = 'https://github.com/openai/tunnel-client/releases/download/' + $release.tag_name + '/'
foreach ($entry in @($asset[0], $sums[0])) {
    if (-not $entry.browser_download_url.StartsWith($prefix,[StringComparison]::Ordinal)) { throw 'Unexpected release asset URL.' }
}
$downloadDir = Join-Path $StateRoot ('downloads\' + $release.tag_name)
New-Item -ItemType Directory -Path $downloadDir -Force | Out-Null
$archive = Join-Path $downloadDir $assetName
$checksums = Join-Path $downloadDir 'SHA256SUMS.txt'
Invoke-WebRequest -UseBasicParsing -Uri $sums[0].browser_download_url -OutFile $checksums -TimeoutSec 60
$match = @([IO.File]::ReadAllLines($checksums) | Where-Object { $_ -match ('^[a-fA-F0-9]{64}\s+\*?' + [regex]::Escape($assetName) + '$') })
if ($match.Count -ne 1) { throw 'Asset checksum missing or duplicated.' }
$expected = ($match[0] -split '\s+')[0].ToLowerInvariant()
if ($asset[0].digest -and $asset[0].digest -cne ('sha256:'+$expected)) { throw 'GitHub asset digest and SHA256SUMS disagree.' }
if (-not (Test-Path -LiteralPath $archive) -or (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) {
    Invoke-WebRequest -UseBasicParsing -Uri $asset[0].browser_download_url -OutFile $archive -TimeoutSec 180
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'Official ZIP checksum mismatch; nothing executed.' }
$destination = Join-Path $StateRoot ('tunnel-client\' + $release.tag_name)
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    $resolved = [IO.Path]::GetFullPath($destination).TrimEnd('\')+'\'
    foreach ($entry in $zip.Entries) {
        $target = [IO.Path]::GetFullPath((Join-Path $destination $entry.FullName))
        if (-not $target.StartsWith($resolved,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe ZIP entry.' }
    }
} finally { $zip.Dispose() }
Expand-Archive -LiteralPath $archive -DestinationPath $destination -Force
$executables = @(Get-ChildItem -LiteralPath $destination -Recurse -Filter 'tunnel-client.exe' -File)
if ($executables.Count -ne 1) { throw 'Expected one full tunnel-client.exe in official ZIP.' }
$path = $executables[0].FullName
if (-not (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $path) 'cloudflared.exe'))) { throw 'Official bundle missing companion cloudflared.exe.' }
$client = Get-HorizunTunnelClient -Override $path -StateRoot $StateRoot
if ($client.status -ne 'compatible') { throw "Official client capability probe failed: $($client.problem)" }
Save-HorizunTunnelSelection -StateRoot $StateRoot -Client $client
[ordered]@{ product='OpenAI tunnel-client'; version=$release.tag_name; asset=$assetName; zip_sha256=$expected; executable_sha256=$client.sha256; downloaded_utc=[DateTime]::UtcNow.ToString('o'); path=$path; source=$asset[0].browser_download_url } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $StateRoot 'tunnel-client-install.json') -Encoding UTF8
Write-Host "Official full tunnel-client $($release.tag_name) verified and installed: $path"

exit 0

exit 0
