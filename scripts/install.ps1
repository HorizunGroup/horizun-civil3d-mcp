<#
.SYNOPSIS
  Builds and installs Horizun Civil 3D MCP (plug-in bundle + MCP server) from source.

.DESCRIPTION
  1. Detects installed Civil 3D years (C:\Program Files\Autodesk\AutoCAD <year>\C3D\AeccDbMgd.dll).
  2. Refuses while any acad.exe is running (the plug-in DLL is locked while Civil 3D runs).
  3. Runs the Civil-3D-free tests, builds one plug-in per year and publishes the server.
  4. Backs up what is installed, copies the new files, and RE-READS every installed file's
     SHA-256 against the staged build. Any failure rolls back to the backup.
  5. Optionally registers the server in Claude Desktop (backing up its config first).

  Installs to:
    %APPDATA%\Autodesk\ApplicationPlugins\Horizun.Civil3D.bundle\        (auto-loaded by Civil 3D)
    %LOCALAPPDATA%\Programs\Horizun\Civil3D-MCP\server\horizun-civil3d-mcp.exe

.PARAMETER Years
  Civil 3D years to build. Default: every supported year installed on this machine.
.PARAMETER DryRun
  Detect, test, build and stage, but do not install anything.
.PARAMETER RegisterClaudeDesktop
  Add/update the "horizun-civil3d" entry in %APPDATA%\Claude\claude_desktop_config.json.
.PARAMETER SkipTests
  Skip the unit tests (not recommended).
.PARAMETER Edition
  development (default): developer buttons (Dibujo de ensayo, Sondeo API) on the Horizun Hub ribbon.
  release: the edition people use; no developer buttons. Use it with -PackageOut.
.PARAMETER PackageOut
  Build and write an installable .zip instead of installing. Whoever receives it extracts it and runs
  .\install.ps1 inside it: nothing is compiled on their machine.

  PACKAGE MODE: when this script sits next to a Horizun.Civil3D.bundle folder (an extracted release .zip),
  it installs those prebuilt files directly (no .NET SDK, no source needed).
#>
[CmdletBinding()]
param(
    [int[]] $Years,
    [switch] $DryRun,
    [switch] $RegisterClaudeDesktop,
    [switch] $SkipTests,
    [ValidateSet('development', 'release')] [string] $Edition = 'development',
    [string] $PackageOut
)

$ErrorActionPreference = 'Stop'
$Supported = @(2025, 2026, 2027)
$Repo = Split-Path -Parent $PSScriptRoot
$Stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$Stage = Join-Path $env:TEMP "horizun-c3d-install-$Stamp"
$BundleDir = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\Horizun.Civil3D.bundle'
$InstallRoot = Join-Path $env:LOCALAPPDATA 'Programs\Horizun\Civil3D-MCP'
$ServerDir = Join-Path $InstallRoot 'server'
$BackupDir = Join-Path $InstallRoot "_backup\$Stamp"
$PackageMode = Test-Path (Join-Path $PSScriptRoot 'Horizun.Civil3D.bundle')

function Step([string] $m) { Write-Host "==> $m" -ForegroundColor Cyan }
function Ok([string] $m) { Write-Host "    $m" -ForegroundColor Green }
function Fail([string] $m) { Write-Host "ERROR: $m" -ForegroundColor Red; exit 1 }

function Invoke-Checked([string] $exe, [string[]] $arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "$exe $($arguments -join ' ') failed with exit code $LASTEXITCODE" }
}

function Get-Hashes([string] $root) {
    $map = @{}
    if (-not (Test-Path $root)) { return $map }
    Get-ChildItem -Path $root -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($root.Length).TrimStart('\')
        $map[$rel] = (Get-FileHash -Algorithm SHA256 -Path $_.FullName).Hash
    }
    return $map
}

function Assert-SameTree([string] $staged, [string] $installed, [string] $what) {
    $a = Get-Hashes $staged
    $b = Get-Hashes $installed
    foreach ($k in $a.Keys) {
        if (-not $b.ContainsKey($k)) { throw "$what verification failed: $k is missing after install." }
        if ($a[$k] -ne $b[$k]) { throw "$what verification failed: $k differs from the build (hash mismatch)." }
    }
    Ok "$what verified: $($a.Count) files, SHA-256 identical to the build."
    return $a
}

# --- 1. detect --------------------------------------------------------------
Step 'Detecting Civil 3D installations'
$installed = @()
foreach ($y in $Supported) {
    $dll = "C:\Program Files\Autodesk\AutoCAD $y\C3D\AeccDbMgd.dll"
    if (Test-Path $dll) { $installed += $y; Ok "Civil 3D $y found" }
}
if (-not $Years) { $Years = $installed }
foreach ($y in $Years) {
    if ($Supported -notcontains $y) { Fail "Civil 3D $y is not supported by this build (supported: $($Supported -join ', '))." }
    if ($installed -notcontains $y) { Fail "Civil 3D $y is not installed (no AeccDbMgd.dll). An AutoCAD without Civil 3D does not count." }
}
if ($Years.Count -eq 0) { Fail 'No supported Civil 3D installation found.' }

# --- 2. refuse while Civil 3D runs -----------------------------------------
$acad = @(Get-Process -Name acad -ErrorAction SilentlyContinue)
if ($acad.Count -gt 0 -and -not $DryRun -and -not $PackageOut) {
    Fail ("Civil 3D / AutoCAD is running (acad.exe pid " + (($acad | ForEach-Object { $_.Id }) -join ', ') +
          "). SAVE your drawings and close every Civil 3D window, then run this again. Nothing was changed.")
}

# --- 3. test, build, stage (or use the prebuilt files of a release package) ---
if ($PackageMode) {
    Step 'Release package: installing the prebuilt files next to this script'
    $StageBundle = Join-Path $PSScriptRoot 'Horizun.Civil3D.bundle'
    $StageServer = Join-Path $PSScriptRoot 'server'
    foreach ($y in $Years) {
        if (-not (Test-Path (Join-Path $StageBundle "Contents\$y\Horizun.Civil3D.dll"))) { Fail "This package has no build for Civil 3D $y." }
    }
    $version = ([xml](Get-Content (Join-Path $StageBundle 'PackageContents.xml'))).ApplicationPackage.AppVersion
    $serverVersion = & (Join-Path $StageServer 'horizun-civil3d-mcp.exe') --version
    Ok $serverVersion
} else {
New-Item -ItemType Directory -Force -Path $Stage | Out-Null
$StageBundle = Join-Path $Stage 'Horizun.Civil3D.bundle'
$StageServer = Join-Path $Stage 'server'

if (-not $SkipTests) {
    Step 'Running tests (no Civil 3D needed)'
    $env:HORIZUN_C3D_DATA_ROOT = Join-Path $Stage 'test-data'
    Invoke-Checked 'dotnet' @('test', (Join-Path $Repo 'tests\Horizun.Civil3D.Core.Tests'), '-c', 'Release', '--nologo', '-v', 'q')
    Remove-Item Env:\HORIZUN_C3D_DATA_ROOT
    Invoke-Checked 'powershell' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $Repo 'scripts\chatgpt-tunnel.tests.ps1'))
    Ok 'tests passed'
}

$version = ([xml](Get-Content (Join-Path $Repo 'Directory.Build.props'))).Project.PropertyGroup.Version
foreach ($y in $Years) {
    Step "Building plug-in for Civil 3D $y"
    $out = Join-Path $StageBundle "Contents\$y"
    Invoke-Checked 'dotnet' @('build', (Join-Path $Repo 'src\Horizun.Civil3D.Plugin\Horizun.Civil3D.Plugin.csproj'),
        '-c', 'Release', "-p:Civil3DYear=$y", "-p:HorizunEdition=$Edition", '-o', $out, '--nologo', '-v', 'q')
    if (-not (Test-Path (Join-Path $out 'Horizun.Civil3D.dll'))) { throw "Build for $y produced no Horizun.Civil3D.dll" }
    Ok "Contents\$y\Horizun.Civil3D.dll"
}

Step 'Publishing MCP server (self-contained, win-x64)'
Invoke-Checked 'dotnet' @('publish', (Join-Path $Repo 'src\Horizun.Civil3D.Server\Horizun.Civil3D.Server.csproj'),
    '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=true', '-o', $StageServer, '--nologo', '-v', 'q')
$exe = Join-Path $StageServer 'horizun-civil3d-mcp.exe'
$serverVersion = & $exe --version
Ok $serverVersion

# ChatGPT helpers ship beside the server; account configuration stays independent.
. (Join-Path $PSScriptRoot 'client-tools.package.ps1')
Copy-HorizunCivil3DClientTools -Destination (Join-Path $StageServer 'client-tools')

# PackageContents.xml: one ComponentEntry per built year.
$series = @{ 2025 = 'R25.0'; 2026 = 'R25.1'; 2027 = 'R26.0' }
$entries = foreach ($y in $Years) {
@"
  <Components Description="Civil 3D $y">
    <RuntimeRequirements OS="Win64" Platform="AutoCAD*" SeriesMin="$($series[$y])" SeriesMax="$($series[$y])" />
    <ComponentEntry AppName="HorizunCivil3DMcp" Version="$version" ModuleName="./Contents/$y/Horizun.Civil3D.dll"
        AppDescription="Horizun Civil 3D MCP bridge" AppType=".NET" LoadOnAutoCADStartup="True" />
  </Components>
"@
}
$xml = @"
<?xml version="1.0" encoding="utf-8"?>
<ApplicationPackage SchemaVersion="1.0" AppVersion="$version"
    ProductCode="{6B1C2E4A-7D3F-4A8B-9E21-C3D0A5F1B7E2}"
    UpgradeCode="{2F8D4C6B-1A3E-4B7D-8C9F-0E5A6B3D1C4F}"
    Name="Horizun Civil 3D MCP" Description="Horizun Civil 3D MCP bridge (Horizun Group)" Author="Horizun Group">
  <CompanyDetails Name="Horizun Group" Url="https://horizunhub.com" />
$($entries -join "`r`n")
</ApplicationPackage>
"@
[System.IO.File]::WriteAllText((Join-Path $StageBundle 'PackageContents.xml'), $xml, (New-Object System.Text.UTF8Encoding($false)))
Ok "bundle staged at $StageBundle ($Edition edition)"

if ($PackageOut) {
    Step "Writing release package $PackageOut"
    $pkg = Join-Path $Stage 'package'
    New-Item -ItemType Directory -Force -Path $pkg | Out-Null
    Copy-Item -Recurse $StageBundle (Join-Path $pkg 'Horizun.Civil3D.bundle')
    Copy-Item -Recurse $StageServer (Join-Path $pkg 'server')
    Copy-Item $PSCommandPath (Join-Path $pkg 'install.ps1')
    $readme = @"
Horizun Civil 3D MCP $version ($Edition edition)

1. Close every Civil 3D window (save your drawings first).
2. In this folder run:   powershell -ExecutionPolicy Bypass -File .\install.ps1 -RegisterClaudeDesktop
3. Restart Claude Desktop and open Civil 3D. The "Horizun Hub" ribbon tab shows the bridge state.

Nothing is compiled on this machine. Every installed file is checked against this package (SHA-256).
"@
    [System.IO.File]::WriteAllText((Join-Path $pkg 'LEEME.txt'), $readme, (New-Object System.Text.UTF8Encoding($false)))
    if (Test-Path $PackageOut) { Remove-Item -Force $PackageOut }
    Compress-Archive -Path (Join-Path $pkg '*') -DestinationPath $PackageOut
    Ok "package: $PackageOut ($([math]::Round((Get-Item $PackageOut).Length / 1MB, 1)) MB)"
    exit 0
}
}

if ($DryRun) {
    Step 'Dry run: nothing installed'
    Ok "Staged build: $Stage"
    if ($acad.Count -gt 0) { Write-Host '    Note: Civil 3D is running; a real install would be refused until it is closed.' -ForegroundColor Yellow }
    exit 0
}

# --- 4. install with backup and rollback ------------------------------------
Step 'Installing'
$expectedBundle = [IO.Path]::GetFullPath((Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\Horizun.Civil3D.bundle'))
$expectedServer = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\Horizun\Civil3D-MCP\server'))
if ([IO.Path]::GetFullPath($BundleDir) -ine $expectedBundle -or [IO.Path]::GetFullPath($ServerDir) -ine $expectedServer) { throw 'Unsafe installation target.' }
$rollback = New-Object System.Collections.Generic.List[scriptblock]
try {
    New-Item -ItemType Directory -Force -Path $BackupDir | Out-Null
    if (Test-Path $BundleDir) {
        Copy-Item -Recurse -Force $BundleDir (Join-Path $BackupDir 'Horizun.Civil3D.bundle')
        $rollback.Add({ Remove-Item -Recurse -Force $BundleDir -ErrorAction SilentlyContinue
                        Copy-Item -Recurse -Force (Join-Path $BackupDir 'Horizun.Civil3D.bundle') $BundleDir })
    } else {
        $rollback.Add({ Remove-Item -Recurse -Force $BundleDir -ErrorAction SilentlyContinue })
    }
    if (Test-Path $ServerDir) {
        Copy-Item -Recurse -Force $ServerDir (Join-Path $BackupDir 'server')
        $rollback.Add({ Remove-Item -Recurse -Force $ServerDir -ErrorAction SilentlyContinue
                        Copy-Item -Recurse -Force (Join-Path $BackupDir 'server') $ServerDir })
    } else {
        $rollback.Add({ Remove-Item -Recurse -Force $ServerDir -ErrorAction SilentlyContinue })
    }

    if (Test-Path $BundleDir) { Remove-Item -Recurse -Force $BundleDir }
    Copy-Item -Recurse -Force $StageBundle $BundleDir
    if (Test-Path $ServerDir) { Remove-Item -Recurse -Force $ServerDir }
    New-Item -ItemType Directory -Force -Path $ServerDir | Out-Null
    Copy-Item -Recurse -Force (Join-Path $StageServer '*') $ServerDir

    Step 'Verifying installed files against the build'
    $bundleHashes = Assert-SameTree $StageBundle $BundleDir 'Plug-in bundle'
    $serverHashes = Assert-SameTree $StageServer $ServerDir 'MCP server'
    $installedVersion = & (Join-Path $ServerDir 'horizun-civil3d-mcp.exe') --version
    if ($installedVersion -ne $serverVersion) { throw "Installed server reports '$installedVersion', expected '$serverVersion'." }
    Ok $installedVersion

    $commit = ''
    try { $commit = (git -C $Repo rev-parse --short HEAD 2>$null) } catch { }
    if ($LASTEXITCODE -ne 0) { $commit = '(no commit)'; $global:LASTEXITCODE = 0 }
    $manifest = [ordered]@{
        schema = 1; product = 'Horizun Civil 3D MCP'; version = $version; commit = $commit
        installed_utc = (Get-Date).ToUniversalTime().ToString('o'); civil3d_years = $Years
        server = $installedVersion; bundle_dir = $BundleDir; server_dir = $ServerDir
        bundle_sha256 = $bundleHashes; server_sha256 = $serverHashes
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 (Join-Path $InstallRoot 'manifest.json')
}
catch {
    Write-Host "Install failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host 'Rolling back...' -ForegroundColor Yellow
    for ($i = $rollback.Count - 1; $i -ge 0; $i--) { try { & $rollback[$i] } catch { } }
    Fail 'Install rolled back to the previous state.'
}

# --- 5. Claude Desktop ------------------------------------------------------
$exePath = Join-Path $ServerDir 'horizun-civil3d-mcp.exe'
if ($RegisterClaudeDesktop) {
    Step 'Registering in Claude Desktop'
    $cfgPath = Join-Path $env:APPDATA 'Claude\claude_desktop_config.json'
    if (-not (Test-Path $cfgPath)) { Fail "Claude Desktop config not found at $cfgPath" }
    Copy-Item $cfgPath (Join-Path $BackupDir 'claude_desktop_config.json')
    $cfg = Get-Content -Raw $cfgPath | ConvertFrom-Json
    if (-not $cfg.mcpServers) { $cfg | Add-Member -NotePropertyName mcpServers -NotePropertyValue ([pscustomobject]@{}) }
    $entry = [pscustomobject]@{ command = $exePath; args = @(); env = [pscustomobject]@{} }
    if ($cfg.mcpServers.PSObject.Properties.Name -contains 'horizun-civil3d') { $cfg.mcpServers.'horizun-civil3d' = $entry }
    else { $cfg.mcpServers | Add-Member -NotePropertyName 'horizun-civil3d' -NotePropertyValue $entry }
    $json = $cfg | ConvertTo-Json -Depth 32
    [System.IO.File]::WriteAllText($cfgPath, $json, (New-Object System.Text.UTF8Encoding($false)))
    Ok "registered as 'horizun-civil3d' (backup: $BackupDir\claude_desktop_config.json). Restart Claude Desktop."
}

Step 'Done'
Ok "Bundle : $BundleDir"
Ok "Server : $exePath"
Ok 'Open Civil 3D: the bridge starts automatically. Type HZ_STATUS to check it.'
if (-not $RegisterClaudeDesktop) {
    Write-Host "    To register in Claude Desktop, re-run with -RegisterClaudeDesktop or add:" -ForegroundColor Gray
    Write-Host "    `"horizun-civil3d`": { `"command`": `"$($exePath -replace '\\','\\')`" }" -ForegroundColor Gray
}
exit 0
