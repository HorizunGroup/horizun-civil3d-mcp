#Requires -Version 5.1
<# Packaging gate. Pass a release ZIP produced by install.ps1 -Edition release -PackageOut. #>
[CmdletBinding()]
param([string] $ReleasePackage, [switch] $MetadataOnly)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repo = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content -LiteralPath (Join-Path $repo 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version

function Assert-C3D([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
# Any real Windows profile path (placeholders such as <name> or %USERPROFILE% are allowed) or synced folder.
$PrivatePath = '(?i)[A-Z]:[\\/]Users[\\/](?!Public[\\/])[^\\/"<>%\s]+[\\/]|OneDrive[\\/]'
foreach ($rel in @('.codex-plugin/plugin.json', '.claude-plugin/plugin.json', 'packaging/claude-desktop/manifest.json')) {
    $path = Join-Path $repo $rel
    $text = Get-Content -LiteralPath $path -Raw
    $metadata = $text | ConvertFrom-Json
    Assert-C3D ($metadata.version -ceq $version) "$rel version mismatch."
    Assert-C3D ($text -notmatch $PrivatePath) "$rel contains a private path."
}
$codex = Get-Content -LiteralPath (Join-Path $repo '.codex-plugin/plugin.json') -Raw | ConvertFrom-Json
$claude = Get-Content -LiteralPath (Join-Path $repo '.claude-plugin/plugin.json') -Raw | ConvertFrom-Json
foreach ($plugin in @($codex, $claude)) {
    $server = $plugin.mcpServers.'horizun-civil3d'
    Assert-C3D ($server.command -ceq 'powershell.exe') 'Plugin command must be powershell.exe.'
    Assert-C3D ($server.args[-1] -ceq '${CLAUDE_PLUGIN_ROOT}/scripts/plugin-launcher.ps1') 'Plugin launcher path mismatch.'
}
$mcpbManifest = Get-Content -LiteralPath (Join-Path $repo 'packaging/claude-desktop/manifest.json') -Raw | ConvertFrom-Json
Assert-C3D ($mcpbManifest.manifest_version -ceq '0.4' -and $mcpbManifest.server.type -ceq 'binary') 'MCPB manifest version/type mismatch.'
Assert-C3D ($mcpbManifest.server.entry_point -ceq 'scripts/plugin-launcher.ps1') 'MCPB entry point mismatch.'
Assert-C3D ($mcpbManifest.server.mcp_config.command -ceq 'powershell.exe') 'MCPB command mismatch.'
foreach ($rel in @('scripts/plugin-launcher.ps1', 'scripts/plugin-runtime.lib.ps1', 'scripts/prepare-revit-terrain.ps1', 'scripts/import-revit-terrain-mesh.py', 'LICENSE', 'NOTICE')) {
    Assert-C3D (Test-Path -LiteralPath (Join-Path $repo $rel) -PathType Leaf) "Missing $rel."
}
Assert-C3D (@(Get-ChildItem -LiteralPath (Join-Path $repo 'skills') -Filter SKILL.md -Recurse -File).Count -gt 0) 'Missing plugin skill.'
if ($MetadataOnly) { Write-Host 'PASS: plugin metadata and required sources.'; return }
if (-not $ReleasePackage) { throw 'Pass -ReleasePackage for the full packaging test, or -MetadataOnly for CI without Civil 3D.' }
$release = (Resolve-Path -LiteralPath $ReleasePackage).Path
$out = Join-Path ([IO.Path]::GetTempPath()) ('horizun-c3d-package-test-' + [guid]::NewGuid().ToString('N'))
function Read-ZipText($Zip, [string]$Name) {
    $entry = $Zip.GetEntry($Name)
    Assert-C3D ($null -ne $entry) "Missing ZIP entry $Name"
    $reader = New-Object IO.StreamReader($entry.Open())
    try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
}
function Read-ZipBytes($Zip, [string]$Name) {
    $entry = $Zip.GetEntry($Name)
    Assert-C3D ($null -ne $entry) "Missing ZIP entry $Name"
    $stream = $entry.Open(); $memory = New-Object IO.MemoryStream
    try { $stream.CopyTo($memory); return $memory.ToArray() }
    finally { $stream.Dispose(); $memory.Dispose() }
}

try {
    $built = @(& (Join-Path $PSScriptRoot 'build-plugin.ps1') -ReleasePackage $release -OutDirectory $out)
    Assert-C3D ($built.Count -eq 2) 'Builder must return the plugin ZIP and MCPB paths.'
    # Exercise the production gate against malformed release archives, not source text.
    $negativeDir = Join-Path $out 'negative'
    [IO.Directory]::CreateDirectory($negativeDir) | Out-Null
    $negativePackage = Join-Path $negativeDir ([IO.Path]::GetFileName($release))
    foreach ($fault in @('parent_scope', 'wrong_year', 'wrong_app_type')) {
        Copy-Item -LiteralPath $release -Destination $negativePackage -Force
        $negativeZip = [IO.Compression.ZipFile]::Open($negativePackage, [IO.Compression.ZipArchiveMode]::Update)
        try {
            $document = [xml](Read-ZipText $negativeZip 'Horizun.Civil3D.bundle/PackageContents.xml')
            $component = $document.SelectSingleNode('/ApplicationPackage/Components/ComponentEntry')
            $requirements = $component.SelectSingleNode('RuntimeRequirements')
            switch ($fault) {
                'parent_scope' { [void]$component.RemoveChild($requirements); [void]$component.ParentNode.AppendChild($requirements) }
                'wrong_year' { $requirements.SetAttribute('SeriesMax', 'R99.0') }
                'wrong_app_type' { $component.SetAttribute('AppType', 'ARX') }
            }
            $negativeZip.GetEntry('Horizun.Civil3D.bundle/PackageContents.xml').Delete()
            $entry = $negativeZip.CreateEntry('Horizun.Civil3D.bundle/PackageContents.xml')
            $writer = New-Object IO.StreamWriter($entry.Open())
            try { $writer.Write($document.OuterXml) } finally { $writer.Dispose() }
        } finally { $negativeZip.Dispose() }
        $rejected = $false
        try { & (Join-Path $PSScriptRoot 'build-plugin.ps1') -ReleasePackage $negativePackage -OutDirectory (Join-Path $negativeDir 'output') | Out-Null }
        catch { $rejected = $_.Exception.Message -like '*ComponentEntry requires its own*' }
        Assert-C3D $rejected "Production package gate accepted $fault."
    }
    $expectedHash = (Get-FileHash -LiteralPath $release -Algorithm SHA256).Hash.ToLowerInvariant()
    foreach ($path in $built) {
        Assert-C3D (Test-Path -LiteralPath $path -PathType Leaf) "Missing package $path"
        $zip = [IO.Compression.ZipFile]::OpenRead($path)
        try {
            $names = @($zip.Entries | ForEach-Object { $_.FullName })
            foreach ($name in @('scripts/plugin-launcher.ps1', 'scripts/plugin-runtime.lib.ps1',
                                'scripts/prepare-revit-terrain.ps1',
                                'LICENSE', 'NOTICE', '.codex-plugin/plugin.json', '.claude-plugin/plugin.json',
                                '.claude-plugin/marketplace.json', 'runtime-release.json')) {
                Assert-C3D ($names -ccontains $name) "$path lacks $name"
            }
            Assert-C3D (@($names | Where-Object { $_ -like 'skills/*/SKILL.md' }).Count -gt 0) "$path lacks a skill."
            Assert-C3D (@($names | Where-Object { $_ -match '(^/|^[A-Za-z]:|\.\.)' }).Count -eq 0) "$path has an unsafe ZIP path."
            foreach ($name in $names | Where-Object { $_ -match '(?i)\.(json|ps1|md|txt)$|^(LICENSE|NOTICE)$' }) {
                $text = Read-ZipText $zip $name
                Assert-C3D ($text -notmatch ($PrivatePath + '|AppData[\\/]Local[\\/]Temp[\\/]horizun-c3d')) "$name contains a private local path."
            }
            $runtimeText = Read-ZipText $zip 'runtime-release.json'
            $runtime = $runtimeText | ConvertFrom-Json
            Assert-C3D ($runtime.version -ceq $version) 'Runtime version mismatch.'
            Assert-C3D ($runtime.asset_name -ceq "horizun-civil3d-mcp-$version.zip") 'Runtime asset name mismatch.'
            Assert-C3D ($runtime.sha256 -ceq $expectedHash) 'Runtime SHA-256 does not match the release ZIP.'
            Assert-C3D ($runtime.url -ceq "https://github.com/HorizunGroup/horizun-civil3d-mcp/releases/download/v$version/$($runtime.asset_name)") 'Runtime URL mismatch.'
            Assert-C3D ($runtimeText -notmatch '(?i)[A-Z]:[\\/]Users[\\/]|OneDrive') 'Runtime metadata contains a private path.'
            $payload = Read-ZipBytes $zip "payload/horizun-civil3d-mcp-$version.zip"
            $hasher = [Security.Cryptography.SHA256]::Create()
            try { $payloadHash = [BitConverter]::ToString($hasher.ComputeHash($payload)).Replace('-', '').ToLowerInvariant() }
            finally { $hasher.Dispose() }
            Assert-C3D ($payloadHash -ceq $expectedHash) 'Payload release differs from input ZIP.'
            foreach ($name in @('.codex-plugin/plugin.json', '.claude-plugin/plugin.json')) {
                $text = Read-ZipText $zip $name
                $manifest = $text | ConvertFrom-Json
                Assert-C3D ($manifest.version -ceq $version) "$name version mismatch."
                Assert-C3D ($text -notmatch '(?i)[A-Z]:[\\/]Users[\\/]|OneDrive') "$name contains a private path."
            }
            if ($path -like '*.mcpb') {
                $manifest = (Read-ZipText $zip 'manifest.json') | ConvertFrom-Json
                Assert-C3D ($manifest.version -ceq $version) 'MCPB version mismatch.'
                Assert-C3D ($manifest.server.type -ceq 'binary') 'MCPB server must be binary.'
                Assert-C3D ($manifest.server.entry_point -ceq 'scripts/plugin-launcher.ps1') 'MCPB entry point mismatch.'
                Assert-C3D ($manifest.server.mcp_config.command -ceq 'powershell.exe') 'MCPB command mismatch.'
            }
        } finally { $zip.Dispose() }
    }
    Write-Host 'PASS: plugin ZIP/MCPB metadata, payload and SHA-256; three malformed autoloader archives rejected.'
} finally {
    $resolved = [IO.Path]::GetFullPath($out)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
    $parent = [IO.Path]::GetDirectoryName($resolved.TrimEnd('\', '/'))
    $leaf = [IO.Path]::GetFileName($resolved.TrimEnd('\', '/'))
    if (-not [string]::Equals($parent, $tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        $leaf -notmatch '^horizun-c3d-package-test-[a-f0-9]{32}$') { throw "Unsafe test cleanup path: $resolved" }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
