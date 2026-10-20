#Requires -Version 5.1
# Lifecycle shared by the plugin's diagnostic MCP and its launcher. No SDK or
# Python is required. Runtime installation never edits MCP client settings.
Set-StrictMode -Version 2

function Get-C3DRelease {
    param([string]$PluginRoot)
    $r = Get-Content -LiteralPath (Join-Path $PluginRoot 'runtime-release.json') -Raw | ConvertFrom-Json
    if ($r.version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid release version.' }
    $asset = 'horizun-civil3d-mcp-' + $r.version + '.zip'
    $url = 'https://github.com/HorizunGroup/horizun-civil3d-mcp/releases/download/v' + $r.version + '/' + $asset
    if ($r.asset_name -cne $asset -or $r.url -cne $url) { throw 'Release metadata does not identify the Civil 3D release.' }
    if ($r.sha256 -and $r.sha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid release SHA-256.' }
    return $r
}

function Get-C3DInstallPaths {
    $root = Join-Path $env:LOCALAPPDATA 'Programs\Horizun\Civil3D-MCP'
    return @{
        Root = $root
        Server = Join-Path $root 'server\horizun-civil3d-mcp.exe'
        Manifest = Join-Path $root 'manifest.json'
        Bundle = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\Horizun.Civil3D.bundle'
    }
}

function Test-C3DHashTree {
    param([string]$Root, $Hashes)
    if (-not $Hashes -or @($Hashes.PSObject.Properties).Count -eq 0) { throw 'The installation manifest has no file hashes.' }
    $base = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    foreach ($p in $Hashes.PSObject.Properties) {
        if ([IO.Path]::IsPathRooted($p.Name)) { throw 'Absolute path in installation hashes.' }
        $path = [IO.Path]::GetFullPath((Join-Path $Root $p.Name))
        if (-not $path.StartsWith($base, [StringComparison]::OrdinalIgnoreCase)) { throw 'Path escapes the installation directory.' }
        if ($p.Value -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid installation hash.' }
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Installed file is missing: $($p.Name)" }
        # Refuse links rather than hash one file and later execute another target.
        $item = Get-Item -LiteralPath $path -Force
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'A runtime file is a reparse point.' }
        $probe = $item.Directory
        while ($probe -and $probe.FullName.Length -ge $Root.Length) {
            if ($probe.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'A runtime directory is a reparse point.' }
            $probe = $probe.Parent
        }
        $file = [IO.Path]::GetFullPath((Join-Path $Root $p.Name))
        if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ine $p.Value) { throw "Installed file differs from its manifest: $($p.Name)" }
    }
}

function Get-C3DRuntimeStatus {
    param([string]$PluginRoot)
    $r = Get-C3DRelease $PluginRoot
    $p = Get-C3DInstallPaths
    $out = [ordered]@{
        state = 'not_installed'; ready = $false; version = $r.version
        installed_version = $null; server = $p.Server; manifest = $p.Manifest
        civil3d_running = @((Get-Process -Name acad -ErrorAction SilentlyContinue) | ForEach-Object { $_.Id })
        message = 'Call horizun_c3d_install_runtime to prepare the release. Save your drawings and close Civil 3D before applying the installation.'
    }
    if (-not (Test-Path -LiteralPath $p.Manifest)) { return [pscustomobject]$out }
    try {
        $m = Get-Content -LiteralPath $p.Manifest -Raw | ConvertFrom-Json
        if ($m.product -cne 'Horizun Civil 3D MCP' -or $m.schema -ne 1) { throw 'Unexpected installation manifest.' }
        $out.installed_version = $m.version
        if ($m.version -cne $r.version) {
            $out.state = 'update_required'
            $out.message = 'Install the matching release before restarting this plugin.'
            return [pscustomobject]$out
        }
        # The manifest must cover the entry point, not just any file in its tree.
        if (-not ($m.server_sha256.PSObject.Properties.Name -contains 'horizun-civil3d-mcp.exe')) { throw 'The server executable is absent from the hash manifest.' }
        Test-C3DHashTree (Split-Path -Parent $p.Server) $m.server_sha256
        Test-C3DHashTree $p.Bundle $m.bundle_sha256
        $out.state = 'ready'; $out.ready = $true
        $out.message = 'Installed server and add-in match the recorded SHA-256 hashes. Restart the MCP client to load the drawing tools; open Civil 3D and call horizun_c3d_health to verify the live bridge.'
    } catch {
        $out.state = 'degraded'; $out.message = $_.Exception.Message
    }
    return [pscustomobject]$out
}

function Expand-C3DRelease {
    param([string]$Archive, [string]$Destination)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $root = [IO.Path]::GetFullPath($Destination).TrimEnd('\') + '\'
    $zip = [IO.Compression.ZipFile]::OpenRead($Archive)
    try {
        foreach ($entry in $zip.Entries) {
            $relative = $entry.FullName.Replace('/', '\')
            if ([IO.Path]::IsPathRooted($relative) -or $relative.Contains(':')) { throw 'Unsafe path in release package.' }
            $target = [IO.Path]::GetFullPath((Join-Path $Destination $relative))
            if (-not $target.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw 'Release package escapes its extraction directory.' }
            if ($entry.Length -gt 512MB) { throw 'Release file is unexpectedly large.' }
        }
    } finally { $zip.Dispose() }
    Expand-Archive -LiteralPath $Archive -DestinationPath $Destination
    if (-not (Test-Path -LiteralPath (Join-Path $Destination 'install.ps1') -PathType Leaf)) { throw 'Release package has no installer.' }
    if (-not (Test-Path -LiteralPath (Join-Path $Destination 'server\horizun-civil3d-mcp.exe') -PathType Leaf)) { throw 'Release package has no Civil 3D MCP server.' }
}

function Install-C3DPluginRuntime {
    param([string]$PluginRoot, [bool]$Confirm = $false)
    $r = Get-C3DRelease $PluginRoot
    $status = Get-C3DRuntimeStatus $PluginRoot
    if ($status.ready) { return $status }
    if (-not $Confirm) {
        return [pscustomobject]@{
            state = 'planned'; ready = $false; version = $r.version; dry_run = $true
            source = $r.url; confirm_required = $true
            message = 'This installs the matching server and Civil 3D add-in for the current Windows user and registers only its year folders in existing Civil TRUSTEDPATHS profiles, with a backup. SECURELOAD, scripting permissions and client configurations are preserved. Save your drawings and close every Civil 3D/AutoCAD window; then call again with confirm=true.'
        }
    }
    if (@(Get-Process -Name acad -ErrorAction SilentlyContinue).Count -gt 0) {
        return [pscustomobject]@{state='blocked'; ready=$false; message='SAVE your drawings and close every Civil 3D/AutoCAD window before installing. Nothing was installed.'}
    }
    $p = Get-C3DInstallPaths
    $cache = Join-Path $p.Root '_plugin-cache'
    New-Item -ItemType Directory -Path $cache -Force | Out-Null
    # A process-lifetime exclusive lock cannot become stale after a crash.
    $lock = $null
    try { $lock = [IO.File]::Open((Join-Path $cache 'install.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
    catch { return [pscustomobject]@{state='installing'; ready=$false; message='Another installation holds the runtime lock. Query status and retry when it finishes.'} }
    $stage = $null
    $succeeded = $false
    try {
        $stage = Join-Path $cache ([Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $stage | Out-Null
        $archive = Join-Path $stage $r.asset_name
        $embedded = Join-Path (Join-Path $PluginRoot 'payload') $r.asset_name
        $sha = $r.sha256
        if (Test-Path -LiteralPath $embedded -PathType Leaf) {
            if (-not $sha) { throw 'Embedded release has no pinned SHA-256.' }
            Copy-Item -LiteralPath $embedded -Destination $archive
        } else {
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            if (-not $sha) {
                $api = 'https://api.github.com/repos/HorizunGroup/horizun-civil3d-mcp/releases/tags/v' + $r.version
                $release = Invoke-RestMethod -Uri $api -Headers @{'User-Agent'='horizun-civil3d-plugin'} -TimeoutSec 30
                if ($release.tag_name -cne ('v' + $r.version)) { throw 'GitHub returned a different release.' }
                $asset = @($release.assets | Where-Object { $_.name -ceq $r.asset_name })
                if ($asset.Count -ne 1 -or $asset[0].digest -notmatch '^sha256:([a-fA-F0-9]{64})$') { throw 'Release asset has no SHA-256 digest; use a verified local plugin package.' }
                $sha = $Matches[1]
            }
            Invoke-WebRequest -Uri $r.url -OutFile $archive -UseBasicParsing -TimeoutSec 120 | Out-Null
        }
        if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ine $sha) { throw 'Release package SHA-256 mismatch. The installer was not executed.' }
        $unpack = Join-Path $stage 'release'
        Expand-C3DRelease $archive $unpack
        $log = Join-Path $stage 'install.log'
        # Package mode uses prebuilt binaries. No SDK, compilation or credentials.
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $unpack 'install.ps1') -RegisterTrustedPath *> $log
        if ($LASTEXITCODE -ne 0) { throw "Installer failed with exit code $LASTEXITCODE. Inspect $log; the previous installation is retained or its rollback status is recorded there." }
        $after = Get-C3DRuntimeStatus $PluginRoot
        if (-not $after.ready) { throw ('Installation completed but runtime verification failed: ' + $after.message) }
        $succeeded = $true
        return $after
    } catch {
        return [pscustomobject]@{state='failed'; ready=$false; version=$r.version; message=$_.Exception.Message}
    } finally {
        # The archive and its extraction are not needed after the attempt. A failed attempt keeps only its
        # install.log (the failure message points to it), and only the three newest attempts are kept.
        try {
            if ($stage -and (Test-Path -LiteralPath $stage)) {
                if ($succeeded) { Remove-Item -LiteralPath $stage -Recurse -Force }
                else { Get-ChildItem -LiteralPath $stage -Force | Where-Object { $_.Name -ne 'install.log' } | Remove-Item -Recurse -Force }
            }
            Get-ChildItem -LiteralPath $cache -Directory -Force | Sort-Object LastWriteTimeUtc -Descending |
                Select-Object -Skip 3 | Remove-Item -Recurse -Force
        } catch { }
        $lock.Dispose()
    }
}
