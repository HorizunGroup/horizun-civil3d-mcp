#Requires -Version 5.1
<# Build portable Codex/Claude plugin ZIP and Claude Desktop MCPB from an existing release ZIP.
   This script does not compile, download, install, or modify client configuration. #>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $ReleasePackage,
    [string] $OutDirectory
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($OutDirectory)) { $OutDirectory = Join-Path $repo 'artifacts' }
function Remove-C3DTemporaryDirectory([string]$Path, [string]$Prefix) {
    $resolved = [IO.Path]::GetFullPath($Path)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
    $parent = [IO.Path]::GetDirectoryName($resolved.TrimEnd('\', '/'))
    $leaf = [IO.Path]::GetFileName($resolved.TrimEnd('\', '/'))
    if (-not [string]::Equals($parent, $tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        $leaf -notmatch ('^' + [regex]::Escape($Prefix) + '[a-f0-9]{32}$')) {
        throw "Refusing to remove a directory outside the dedicated temporary workspace: $resolved"
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
$versionXml = [xml](Get-Content -LiteralPath (Join-Path $repo 'Directory.Build.props') -Raw)
$version = [string]$versionXml.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Directory.Build.props must contain a three-part Version.' }
$assetName = "horizun-civil3d-mcp-$version.zip"
$release = (Resolve-Path -LiteralPath $ReleasePackage).Path
if ((Get-Item -LiteralPath $release).PSIsContainer -or [IO.Path]::GetFileName($release) -cne $assetName) {
    throw "ReleasePackage must name $assetName."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
function New-C3DArchive([string]$Source, [string]$Destination) {
    if (Test-Path -LiteralPath $Destination) { Remove-Item -LiteralPath $Destination -Force }
    $zip = [IO.Compression.ZipFile]::Open($Destination, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $base = [IO.Path]::GetFullPath($Source).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
        foreach ($file in Get-ChildItem -LiteralPath $Source -File -Recurse -Force | Sort-Object FullName) {
            $name = $file.FullName.Substring($base.Length).Replace('\', '/')
            $entry = $zip.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(2020, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $sourceStream = [IO.File]::OpenRead($file.FullName)
            $zipStream = $entry.Open()
            try { $sourceStream.CopyTo($zipStream) }
            finally { $zipStream.Dispose(); $sourceStream.Dispose() }
        }
    } finally { $zip.Dispose() }
}
$archive = [IO.Compression.ZipFile]::OpenRead($release)
$validationDir = Join-Path ([IO.Path]::GetTempPath()) ('horizun-c3d-validation-' + [guid]::NewGuid().ToString('N'))
try {
    $entries = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    foreach ($required in @('install.ps1', 'server/horizun-civil3d-mcp.exe')) {
        if ($entries -cnotcontains $required) { throw "Release package lacks $required." }
    }
    if (-not @($entries | Where-Object { $_ -like 'Horizun.Civil3D.bundle/*' }).Count) {
        throw 'Release package lacks the Civil 3D bundle.'
    }
    $bundleXml = @($archive.Entries | Where-Object { $_.FullName.Replace('\', '/') -ceq 'Horizun.Civil3D.bundle/PackageContents.xml' }) | Select-Object -First 1
    if ($null -eq $bundleXml) { throw 'Release package lacks bundle PackageContents.xml.' }
    $xmlReader = New-Object IO.StreamReader($bundleXml.Open())
    try { $bundleDocument = [xml]$xmlReader.ReadToEnd(); $bundleVersion = $bundleDocument.ApplicationPackage.AppVersion }
    finally { $xmlReader.Dispose() }
    if ($bundleVersion -cne $version) { throw "Bundle AppVersion is '$bundleVersion', expected '$version'." }
    $seriesByYear = @{ '2024' = 'R24.3'; '2025' = 'R25.0'; '2026' = 'R25.1'; '2027' = 'R26.0' }
    $components = @($bundleDocument.SelectNodes('/ApplicationPackage/Components/ComponentEntry'))
    if ($components.Count -eq 0) { throw 'Bundle has no ComponentEntry.' }
    foreach ($component in $components) {
        if ($component.ModuleName -notmatch '/(2024|2025|2026|2027)/') { throw 'Bundle component has no supported year path.' }
        $series = $seriesByYear[$Matches[1]]
        $requirements = @($component.SelectNodes('RuntimeRequirements'))
        if ($requirements.Count -ne 1 -or $requirements[0].SeriesMin -cne $series -or
            $requirements[0].SeriesMax -cne $series -or $requirements[0].OS -cne 'Win64' -or
            $requirements[0].Platform -cne 'AutoCAD*' -or $component.AppType -cne '.Net') {
            throw 'Bundle ComponentEntry requires its own exact year RuntimeRequirements and .Net AppType.'
        }
    }

    [IO.Directory]::CreateDirectory($validationDir) | Out-Null
    $serverExe = Join-Path $validationDir 'horizun-civil3d-mcp.exe'
    $serverEntry = @($archive.Entries | Where-Object { $_.FullName.Replace('\', '/') -ceq 'server/horizun-civil3d-mcp.exe' }) | Select-Object -First 1
    [IO.Compression.ZipFileExtensions]::ExtractToFile($serverEntry, $serverExe)
    $serverVersion = & $serverExe --version
    if ($LASTEXITCODE -ne 0 -or $serverVersion -notmatch ('^horizun-civil3d-mcp ' + [regex]::Escape($version) + ' contract ')) {
        throw "Server --version does not match $version (reported: $serverVersion)."
    }
} finally {
    $archive.Dispose()
    Remove-C3DTemporaryDirectory $validationDir 'horizun-c3d-validation-'
}

$requiredFiles = @(
    '.codex-plugin/plugin.json', '.claude-plugin/plugin.json', '.claude-plugin/marketplace.json',
    'packaging/claude-desktop/manifest.json', 'scripts/plugin-launcher.ps1',
    'scripts/plugin-runtime.lib.ps1', 'scripts/prepare-revit-terrain.ps1', 'scripts/import-revit-terrain-mesh.py', 'LICENSE', 'NOTICE'
)
foreach ($rel in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $repo $rel) -PathType Leaf)) { throw "Missing plugin source: $rel" }
}
if (-not (Test-Path -LiteralPath (Join-Path $repo 'skills') -PathType Container)) {
    throw 'Missing plugin source: skills/'
}
foreach ($rel in @('.codex-plugin/plugin.json', '.claude-plugin/plugin.json', 'packaging/claude-desktop/manifest.json')) {
    $metadata = Get-Content -LiteralPath (Join-Path $repo $rel) -Raw | ConvertFrom-Json
    if ($metadata.version -cne $version) { throw "$rel version does not match $version." }
}
$marketplace = Get-Content -LiteralPath (Join-Path $repo '.claude-plugin/marketplace.json') -Raw | ConvertFrom-Json
if ($marketplace.plugins[0].source.url -cne 'https://github.com/HorizunGroup/horizun-civil3d-mcp.git') {
    throw 'Marketplace source does not name the Civil 3D repository.'
}

$out = [IO.Path]::GetFullPath($OutDirectory)
[IO.Directory]::CreateDirectory($out) | Out-Null
$stage = Join-Path ([IO.Path]::GetTempPath()) ('horizun-c3d-plugin-' + [guid]::NewGuid().ToString('N'))
$pluginStage = Join-Path $stage 'plugin'
$mcpbStage = Join-Path $stage 'mcpb'
try {
    [IO.Directory]::CreateDirectory($pluginStage) | Out-Null
    foreach ($rel in @('.codex-plugin', '.claude-plugin', 'scripts', 'skills', 'payload')) {
        [IO.Directory]::CreateDirectory((Join-Path $pluginStage $rel)) | Out-Null
    }
    foreach ($rel in @('.codex-plugin/plugin.json', '.claude-plugin/plugin.json', '.claude-plugin/marketplace.json',
                       'scripts/plugin-launcher.ps1', 'scripts/plugin-runtime.lib.ps1', 'scripts/prepare-revit-terrain.ps1', 'scripts/import-revit-terrain-mesh.py', 'LICENSE', 'NOTICE')) {
        Copy-Item -LiteralPath (Join-Path $repo $rel) -Destination (Join-Path $pluginStage $rel)
    }
    foreach ($skillItem in Get-ChildItem -LiteralPath (Join-Path $repo 'skills') -Force) {
        Copy-Item -LiteralPath $skillItem.FullName -Destination (Join-Path $pluginStage 'skills') -Recurse -Force
    }
    Copy-Item -LiteralPath $release -Destination (Join-Path $pluginStage "payload/$assetName")

    $digest = (Get-FileHash -LiteralPath $release -Algorithm SHA256).Hash.ToLowerInvariant()
    $runtime = [ordered]@{
        version = $version
        asset_name = $assetName
        sha256 = $digest
        url = "https://github.com/HorizunGroup/horizun-civil3d-mcp/releases/download/v$version/$assetName"
    }
    $runtimePath = Join-Path $pluginStage 'runtime-release.json'
    [IO.File]::WriteAllText($runtimePath, ($runtime | ConvertTo-Json -Depth 5) + "`n", (New-Object Text.UTF8Encoding($false)))

    $pluginZip = Join-Path $out "horizun-civil3d-mcp-$version-plugin.zip"
    $mcpb = Join-Path $out "horizun-civil3d-mcp-$version.mcpb"
    New-C3DArchive $pluginStage $pluginZip

    [IO.Directory]::CreateDirectory($mcpbStage) | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $pluginStage -Force) {
        Copy-Item -LiteralPath $item.FullName -Destination (Join-Path $mcpbStage $item.Name) -Recurse -Force
    }
    Copy-Item -LiteralPath (Join-Path $repo 'packaging/claude-desktop/manifest.json') -Destination (Join-Path $mcpbStage 'manifest.json')
    New-C3DArchive $mcpbStage $mcpb

    $sums = @("$digest  $assetName")
    foreach ($artifact in @($pluginZip, $mcpb)) {
        $hash = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash.ToLowerInvariant()
        $sums += "$hash  $([IO.Path]::GetFileName($artifact))"
    }
    [IO.File]::WriteAllText((Join-Path $out 'SHA256SUMS'), ($sums -join "`n") + "`n", (New-Object Text.UTF8Encoding($false)))

    Write-Output $pluginZip
    Write-Output $mcpb
} finally {
    Remove-C3DTemporaryDirectory $stage 'horizun-c3d-plugin-'
}
