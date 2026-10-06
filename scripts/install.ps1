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
.PARAMETER AcadRoots
  Optional Autodesk installation/reference roots keyed by year. The managed identities and
  runtime must match that year/update. Use reference-only trees with DryRun or PackageOut.
.PARAMETER DryRun
  Detect, test, build and stage, but do not install anything.
.PARAMETER RegisterClaudeDesktop
  Add/update the "horizun-civil3d" entry in %APPDATA%\Claude\claude_desktop_config.json.
.PARAMETER RegisterTrustedPath
  Explicitly register only this plug-in's year folders in existing Civil profiles' TRUSTEDPATHS.
  The helper backs up the original values and preserves SECURELOAD and other trusted locations.
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
    [switch] $RegisterTrustedPath,
    [switch] $SkipTests,
    [ValidateSet('development', 'release')] [string] $Edition = 'development',
    [string] $PackageOut,
    # Optional authentic Autodesk installation/reference roots keyed by Civil year.
    [hashtable] $AcadRoots = @{}
)

$ErrorActionPreference = 'Stop'
$Supported = @(2024, 2025, 2026, 2027)
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
    $root = if ($AcadRoots.ContainsKey($y)) { $AcadRoots[$y] } elseif ($AcadRoots.ContainsKey("$y")) { $AcadRoots["$y"] } else { "C:\Program Files\Autodesk\AutoCAD $y" }
    $AcadRoots[$y] = [IO.Path]::GetFullPath($root)
    $dll = Join-Path $AcadRoots[$y] 'C3D\AeccDbMgd.dll'
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
    $hostManifestPath = Join-Path $StageBundle 'host-builds.json'
    if (-not (Test-Path -LiteralPath $hostManifestPath)) { Fail 'This package has no Autodesk runtime metadata. Rebuild it with the current installer.' }
    $hostBuilds = @(Get-Content -LiteralPath $hostManifestPath -Raw | ConvertFrom-Json)
    foreach ($y in $Years) {
        $record = @($hostBuilds | Where-Object { [int]$_.year -eq $y })
        if ($record.Count -ne 1) { Fail "This package must have exactly one Autodesk build record for Civil 3D $y." }
    }
    foreach ($build in $hostBuilds) {
        if ($installed -notcontains [int]$build.year) { continue }
        $actualJson = & (Join-Path $StageServer 'horizun-civil3d-mcp.exe') --inspect-host $AcadRoots[[int]$build.year] $build.year
        if ($LASTEXITCODE -ne 0) { Fail "Could not inspect Civil 3D $($build.year)." }
        $actual = $actualJson | ConvertFrom-Json
        if ($actual.runtime -cne $build.runtime -or $actual.autocad_assembly_version -cne $build.autocad_assembly_version -or $actual.civil_assembly_version -cne $build.civil_assembly_version) {
            Fail "This Civil 3D $($build.year) package was built against different Autodesk DLLs/runtime ($($build.runtime)). Obtain a build matching this installed update ($($actual.runtime)); nothing was installed."
        }
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
    Invoke-Checked 'dotnet' @('test', (Join-Path $Repo 'tests\Horizun.Civil3D.Runtime.Tests'), '-c', 'Release', '--nologo', '-v', 'q')
    Remove-Item Env:\HORIZUN_C3D_DATA_ROOT
    Invoke-Checked 'powershell' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $Repo 'scripts\claude-desktop-registration.tests.ps1'))
    Invoke-Checked 'powershell' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $Repo 'scripts\install-rollback.tests.ps1'))
    Invoke-Checked 'powershell' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $Repo 'scripts\register-plugin-trust.tests.ps1'))
    Invoke-Checked 'powershell' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $Repo 'scripts\chatgpt-tunnel.tests.ps1'))
    Ok 'tests passed'
}

$version = ([xml](Get-Content (Join-Path $Repo 'Directory.Build.props'))).Project.PropertyGroup.Version

# Publish before building add-ins: this executable inspects PE metadata without loading Autodesk code.
Step 'Publishing MCP server (self-contained, win-x64)'
Invoke-Checked 'dotnet' @('publish', (Join-Path $Repo 'src\Horizun.Civil3D.Server\Horizun.Civil3D.Server.csproj'),
    '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=true', '-o', $StageServer, '--nologo', '-v', 'q')
$exe = Join-Path $StageServer 'horizun-civil3d-mcp.exe'
$serverVersion = & $exe --version
Ok $serverVersion
$hostBuilds = @()
foreach ($y in $Years) {
    $hostJson = & $exe --inspect-host $AcadRoots[$y] $y
    if ($LASTEXITCODE -ne 0) { Fail "Could not inspect Civil 3D $y Autodesk metadata." }
    $hostInfo = $hostJson | ConvertFrom-Json
    $hostBuilds += $hostInfo
    Step "Building plug-in for Civil 3D $y"
    $out = Join-Path $StageBundle "Contents\$y"
    Invoke-Checked 'dotnet' @('build', (Join-Path $Repo 'src\Horizun.Civil3D.Plugin\Horizun.Civil3D.Plugin.csproj'),
        '-c', 'Release', "-p:Civil3DYear=$y", "-p:Civil3DRuntime=$($hostInfo.runtime)", "-p:AcadDir=$($AcadRoots[$y])", "-p:HorizunEdition=$Edition", '-o', $out, '--nologo', '-v', 'q')
    if (-not (Test-Path (Join-Path $out 'Horizun.Civil3D.dll'))) { throw "Build for $y produced no Horizun.Civil3D.dll" }
    Ok "Contents\$y\Horizun.Civil3D.dll"
}

[IO.File]::WriteAllText((Join-Path $StageBundle 'host-builds.json'), (ConvertTo-Json -InputObject @($hostBuilds) -Depth 6), (New-Object System.Text.UTF8Encoding($false)))

# ChatGPT helpers ship beside the server; account configuration stays independent.
. (Join-Path $PSScriptRoot 'client-tools.package.ps1')
Copy-HorizunCivil3DClientTools -Destination (Join-Path $StageServer 'client-tools')

# PackageContents.xml: one ComponentEntry per built year.
$series = @{ 2024 = 'R24.3'; 2025 = 'R25.0'; 2026 = 'R25.1'; 2027 = 'R26.0' }
$entries = foreach ($y in $Years) {
@"
  <Components Description="Civil 3D $y">
    <ComponentEntry AppName="HorizunCivil3DMcp" Version="$version" ModuleName="./Contents/$y/Horizun.Civil3D.dll"
        AppDescription="Horizun Civil 3D MCP bridge" AppType=".Net" LoadOnAutoCADStartup="True">
      <RuntimeRequirements OS="Win64" Platform="AutoCAD*" SeriesMin="$($series[$y])" SeriesMax="$($series[$y])" />
    </ComponentEntry>
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
    # Portable PDB metadata may retain compiler inputs despite mapped source paths.
    # Keep developer symbols locally; distribution packages contain executable payloads only.
    foreach ($symbol in Get-ChildItem -LiteralPath $pkg -File -Recurse -Filter '*.pdb') {
        $resolvedSymbol = [IO.Path]::GetFullPath($symbol.FullName)
        if (-not $resolvedSymbol.StartsWith([IO.Path]::GetFullPath($pkg).TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to remove a symbol outside the dedicated package directory.'
        }
        Remove-Item -LiteralPath $resolvedSymbol -Force
    }
    Copy-Item $PSCommandPath (Join-Path $pkg 'install.ps1')
    Copy-Item (Join-Path $PSScriptRoot 'claude-desktop-registration.lib.ps1') (Join-Path $pkg 'claude-desktop-registration.lib.ps1')
    Copy-Item (Join-Path $PSScriptRoot 'install-rollback.lib.ps1') (Join-Path $pkg 'install-rollback.lib.ps1')
    Copy-Item (Join-Path $PSScriptRoot 'register-plugin-trust.ps1') (Join-Path $pkg 'register-plugin-trust.ps1')
    $readme = @"
Horizun Civil 3D MCP $version ($Edition edition)

1. Close every Civil 3D window (save your drawings first).
2. In this folder run:   powershell -ExecutionPolicy Bypass -File .\install.ps1 -RegisterClaudeDesktop -RegisterTrustedPath
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
# Validate the optional client config before replacing any installed product file.
# Registration is committed inside the same rollback boundary below.
$claudeConfigPath = $null
$claudeRegistration = $null
$claudeOriginalHash = $null
if ($RegisterClaudeDesktop) {
    $claudeProcesses = @(Get-Process -Name Claude -ErrorAction SilentlyContinue)
    if ($claudeProcesses.Count -gt 0) {
        throw 'Claude Desktop is running. Close it before using -RegisterClaudeDesktop; no installed files or client settings were changed.'
    }
    . (Join-Path $PSScriptRoot 'claude-desktop-registration.lib.ps1')
    $claudeConfigPath = Join-Path $env:APPDATA 'Claude\claude_desktop_config.json'
    $claudeRegistration = Get-HorizunClaudeDesktopRegistration -ConfigPath $claudeConfigPath -ServerPath (Join-Path $ServerDir 'horizun-civil3d-mcp.exe')
    $claudeOriginalHash = (Get-FileHash -LiteralPath $claudeConfigPath -Algorithm SHA256).Hash
}
Step 'Installing'
$expectedBundle = [IO.Path]::GetFullPath((Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\Horizun.Civil3D.bundle'))
$expectedServer = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\Horizun\Civil3D-MCP\server'))
if ([IO.Path]::GetFullPath($BundleDir) -ine $expectedBundle -or [IO.Path]::GetFullPath($ServerDir) -ine $expectedServer) { throw 'Unsafe installation target.' }
. (Join-Path $PSScriptRoot 'install-rollback.lib.ps1')
$rollback = New-Object System.Collections.Generic.List[scriptblock]
try {
    New-Item -ItemType Directory -Force -Path $BackupDir | Out-Null
    $manifestPath = Join-Path $InstallRoot 'manifest.json'
    if (Test-Path -LiteralPath $manifestPath) {
        Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $BackupDir 'manifest.json')
        $rollback.Add({ Copy-Item -LiteralPath (Join-Path $BackupDir 'manifest.json') -Destination $manifestPath -Force -ErrorAction Stop })
    }
    else {
        $rollback.Add({ if (Test-Path -LiteralPath $manifestPath) { Remove-Item -LiteralPath $manifestPath -Force -ErrorAction Stop } })
    }
    if ($RegisterClaudeDesktop) {
        Copy-Item -LiteralPath $claudeConfigPath -Destination (Join-Path $BackupDir 'claude_desktop_config.json')
    }
    if (Test-Path $BundleDir) {
        Copy-Item -Recurse -Force $BundleDir (Join-Path $BackupDir 'Horizun.Civil3D.bundle')
        $rollback.Add({ if (Test-Path -LiteralPath $BundleDir) { Remove-Item -LiteralPath $BundleDir -Recurse -Force -ErrorAction Stop }
                        Copy-Item -LiteralPath (Join-Path $BackupDir 'Horizun.Civil3D.bundle') -Destination $BundleDir -Recurse -Force -ErrorAction Stop })
    } else {
        $rollback.Add({ if (Test-Path -LiteralPath $BundleDir) { Remove-Item -LiteralPath $BundleDir -Recurse -Force -ErrorAction Stop } })
    }
    if (Test-Path $ServerDir) {
        Copy-Item -Recurse -Force $ServerDir (Join-Path $BackupDir 'server')
        $rollback.Add({ if (Test-Path -LiteralPath $ServerDir) { Remove-Item -LiteralPath $ServerDir -Recurse -Force -ErrorAction Stop }
                        Copy-Item -LiteralPath (Join-Path $BackupDir 'server') -Destination $ServerDir -Recurse -Force -ErrorAction Stop })
    } else {
        $rollback.Add({ if (Test-Path -LiteralPath $ServerDir) { Remove-Item -LiteralPath $ServerDir -Recurse -Force -ErrorAction Stop } })
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
        claude_desktop = [ordered]@{
            registration_requested = [bool]$RegisterClaudeDesktop
            registered = [bool]$RegisterClaudeDesktop
            pending_client_restart = [bool]$RegisterClaudeDesktop
        }
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 $manifestPath

    if ($RegisterClaudeDesktop) {
        if (@(Get-Process -Name Claude -ErrorAction SilentlyContinue).Count -gt 0) {
            throw 'Claude Desktop started during installation. Close it and retry registration; the installation will roll back.'
        }
        if ((Get-FileHash -LiteralPath $claudeConfigPath -Algorithm SHA256).Hash -ne $claudeOriginalHash) {
            throw 'Claude Desktop config changed during installation. The installation will roll back instead of overwriting those changes.'
        }
        Step 'Registering in Claude Desktop'
        $rollback.Add({ Copy-Item -LiteralPath (Join-Path $BackupDir 'claude_desktop_config.json') -Destination $claudeConfigPath -Force -ErrorAction Stop })
        Set-HorizunClaudeDesktopRegistration -ConfigPath $claudeConfigPath -Json $claudeRegistration
        Ok "registered as 'horizun-civil3d' (backup: $BackupDir\claude_desktop_config.json). pending_client_restart: restart Claude Desktop."
    }
    if ($RegisterTrustedPath) {
        Step 'Registering this plug-in in existing Civil trusted paths'
        & (Join-Path $PSScriptRoot 'register-plugin-trust.ps1') -Years $Years -Register
    }
}
catch {
    Write-Host "Install failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host 'Rolling back...' -ForegroundColor Yellow
    $rollbackResult = Invoke-HorizunRollback -Actions $rollback
    if (-not $rollbackResult.Restored) {
        foreach ($rollbackError in $rollbackResult.Errors) { Write-Host "Rollback error: $rollbackError" -ForegroundColor Red }
        Fail "rollback_incomplete: $($rollbackResult.Errors.Count) undo action(s) failed. Inspect the backup at $BackupDir before retrying."
    }
    Fail 'Install failed; previous installation restored.'
}

# --- 5. completed installation ----------------------------------------------
$exePath = Join-Path $ServerDir 'horizun-civil3d-mcp.exe'
Step 'Done'
Ok "Bundle : $BundleDir"
Ok "Server : $exePath"
Ok 'Open Civil 3D and verify the bridge with HZ_STATUS or horizun_c3d_health.'
if (-not $RegisterTrustedPath) { Ok 'Unsigned AppData plug-ins require an existing trust grant. Use -RegisterTrustedPath to register only this plug-in with a profile backup.' }
if (-not $RegisterClaudeDesktop) {
    Write-Host "    To register in Claude Desktop, re-run with -RegisterClaudeDesktop or add:" -ForegroundColor Gray
    Write-Host "    `"horizun-civil3d`": { `"command`": `"$($exePath -replace '\\','\\')`" }" -ForegroundColor Gray
}
exit 0
