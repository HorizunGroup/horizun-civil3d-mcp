#Requires -Version 5.1
[CmdletBinding()]
param([string]$PublishedServerPath)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'plugin-runtime.lib.ps1')
. (Join-Path $PSScriptRoot 'process.lib.ps1')

function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
function Expect-Throw([scriptblock]$Action, [string]$Message) {
    $threw = $false
    try { & $Action | Out-Null } catch { $threw = $true }
    Assert $threw $Message
}
function Write-Release([string]$Root, [string]$Version = '1.2.3') {
    $asset = "horizun-civil3d-mcp-$Version.zip"
    $doc = [ordered]@{
        version = $Version; asset_name = $asset; sha256 = ('a' * 64)
        url = "https://github.com/HorizunGroup/horizun-civil3d-mcp/releases/download/v$Version/$asset"
    }
    [IO.File]::WriteAllText((Join-Path $Root 'runtime-release.json'), ($doc | ConvertTo-Json))
}
function Invoke-Launcher([string]$Launcher, [string]$AppData, [string]$LocalData, [string[]]$Messages, [switch]$AllowStderr, [switch]$Status) {
    $psi = New-Object Diagnostics.ProcessStartInfo
    $psi.FileName = (Get-Command powershell.exe).Source
    $psi.Arguments = '-NoProfile -ExecutionPolicy Bypass -File ' + (ConvertTo-HorizunWindowsArgument $Launcher) + $(if ($Status) { ' -Status' } else { '' })
    $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
    $psi.RedirectStandardInput = $true; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
    $psi.EnvironmentVariables['APPDATA'] = $AppData
    $psi.EnvironmentVariables['LOCALAPPDATA'] = $LocalData
    $psi.EnvironmentVariables['HORIZUN_C3D_DATA_ROOT'] = Join-Path $LocalData 'isolated-server-data'
    # pwsh's inherited PSModulePath can omit Windows PowerShell's built-in modules.
    $windowsModules = Join-Path (Split-Path -Parent $psi.FileName) 'Modules'
    $psi.EnvironmentVariables['PSModulePath'] = $windowsModules + ';' + $psi.EnvironmentVariables['PSModulePath']
    $p = [Diagnostics.Process]::Start($psi)
    try {
        foreach ($m in $Messages) { $p.StandardInput.WriteLine($m) }
        $p.StandardInput.Close()
        $out = $p.StandardOutput.ReadToEndAsync()
        $err = $p.StandardError.ReadToEndAsync()
        if (-not $p.WaitForExit(15000)) { $p.Kill(); throw 'Plugin launcher did not exit after stdin closed.' }
        $stdout = $out.Result.Trim()
        $stderr = $err.Result.Trim()
        Assert ($p.ExitCode -eq 0) "Launcher exited $($p.ExitCode): $stderr"
        if (-not $AllowStderr) { Assert (-not $stderr) "Launcher wrote unexpected stderr: $stderr" }
        if (-not $stdout) { return @() }
        return @($stdout -split "`r?`n" | ForEach-Object { $_ | ConvertFrom-Json })
    }
    finally { $p.Dispose() }
}
function Make-Zip([string]$Path, [string[]]$Entries) {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::Open($Path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($name in $Entries) {
            $entry = $zip.CreateEntry($name)
            $writer = New-Object IO.StreamWriter($entry.Open())
            try { $writer.Write('test') } finally { $writer.Dispose() }
        }
    }
    finally { $zip.Dispose() }
}

$tempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
$root = Join-Path $tempParent ('horizun-c3d-plugin-test-' + [guid]::NewGuid().ToString('N'))
$plugin = Join-Path $root 'plugin'
$appData = Join-Path $root 'appdata'
$localData = Join-Path $root 'localappdata'
foreach ($dir in @($plugin, (Join-Path $plugin 'scripts'), $appData, $localData)) { [IO.Directory]::CreateDirectory($dir) | Out-Null }
try {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'plugin-launcher.ps1') -Destination (Join-Path $plugin 'scripts\plugin-launcher.ps1')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'plugin-runtime.lib.ps1') -Destination (Join-Path $plugin 'scripts\plugin-runtime.lib.ps1')
    Write-Release $plugin
    $launcher = Join-Path $plugin 'scripts\plugin-launcher.ps1'
    $requests = @(
        '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05"}}',
        '{"jsonrpc":"2.0","method":"notifications/initialized"}',
        '{"jsonrpc":"2.0","id":2,"method":"tools/list"}',
        '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"horizun_c3d_install_status","arguments":{}}}',
        '{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"horizun_c3d_install_runtime","arguments":{"confirm":false}}}',
        '{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"horizun_c3d_install_runtime","arguments":{"confirm":"false"}}}',
        '{"jsonrpc":"2.0","id":6,"method":"tools/call","params":{"name":"horizun_c3d_install_runtime","arguments":{"other":true}}}'
    )
    $replies = @(Invoke-Launcher $launcher $appData $localData $requests)
    Assert ($replies.Count -eq 6) 'A notification produced a reply or a request did not.'
    Assert ($replies[0].result.protocolVersion -eq '2024-11-05') 'Initialize did not negotiate the requested protocol.'
    Assert ($replies[0].result.serverInfo.name -eq 'horizun-civil3d-installer') 'Installer server identity is wrong.'
    Assert (@($replies[1].result.tools).Count -eq 2) 'Installer must advertise exactly two bootstrap tools.'
    Assert ($replies[2].result.structuredContent.state -eq 'not_installed') 'Status did not report the isolated runtime as absent.'
    Assert ($replies[3].result.structuredContent.state -eq 'planned' -and $replies[3].result.structuredContent.dry_run) 'confirm=false did not return a plan.'
    Assert ($replies[4].error.code -eq -32602 -and $replies[5].error.code -eq -32602) 'Wrong type or unknown argument was accepted.'
    Assert (-not (Test-Path -LiteralPath (Join-Path $localData 'Programs\Horizun\Civil3D-MCP'))) 'Dry run touched the installation root.'

    # A bad embedded digest must stop before extracting or invoking install.ps1.
    $payload = Join-Path $plugin 'payload'
    [IO.Directory]::CreateDirectory($payload) | Out-Null
    $badPackage = Join-Path $payload 'horizun-civil3d-mcp-1.2.3.zip'
    Make-Zip $badPackage @('install.ps1', 'server/horizun-civil3d-mcp.exe')
    $confirmReply = @(Invoke-Launcher $launcher $appData $localData @('{"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"horizun_c3d_install_runtime","arguments":{"confirm":true}}}'))
    Assert ($confirmReply.Count -eq 1) 'Confirm call gave the wrong number of replies.'
    if (@(Get-Process -Name acad -ErrorAction SilentlyContinue).Count -eq 0) {
        Assert ($confirmReply[0].result.structuredContent.state -eq 'failed') 'Bad embedded digest did not fail.'
        Assert ($confirmReply[0].result.structuredContent.message -match 'SHA-256 mismatch') ("Bad embedded digest failed for another reason: " + $confirmReply[0].result.structuredContent.message)
    }
    else { Assert ($confirmReply[0].result.structuredContent.state -eq 'blocked') 'Active Civil 3D did not block installation.' }
    Assert (-not (Test-Path -LiteralPath (Join-Path $localData 'Programs\Horizun\Civil3D-MCP\manifest.json'))) 'Bad digest invoked the installer.'

    # Exercise the GitHub digest branch with injected responses, never network.
    Remove-Item -LiteralPath $badPackage
    $releasePath = Join-Path $plugin 'runtime-release.json'
    $releaseDoc = Get-Content -LiteralPath $releasePath -Raw | ConvertFrom-Json
    $releaseDoc.sha256 = $null
    $releaseDoc | ConvertTo-Json | Set-Content -LiteralPath $releasePath
    $script:apiCalls = 0; $script:downloadCalls = 0
    function Invoke-RestMethod {
        param([string]$Uri, $Headers, [int]$TimeoutSec)
        $script:apiCalls++
        Assert ($Uri -eq 'https://api.github.com/repos/HorizunGroup/horizun-civil3d-mcp/releases/tags/v1.2.3') 'Unexpected release API URL.'
        return [pscustomobject]@{tag_name='v1.2.3'; assets=@([pscustomobject]@{name='horizun-civil3d-mcp-1.2.3.zip'; digest=('sha256:' + ('a' * 64))})}
    }
    function Invoke-WebRequest {
        param([string]$Uri, [string]$OutFile, [switch]$UseBasicParsing, [int]$TimeoutSec)
        $script:downloadCalls++
        Assert ($Uri -eq 'https://github.com/HorizunGroup/horizun-civil3d-mcp/releases/download/v1.2.3/horizun-civil3d-mcp-1.2.3.zip') 'Unexpected release download URL.'
        [IO.File]::WriteAllText($OutFile, 'synthetic download with wrong hash')
    }
    $oldAppData = $env:APPDATA; $oldLocalData = $env:LOCALAPPDATA
    try {
        $env:APPDATA = $appData; $env:LOCALAPPDATA = $localData
        if (@(Get-Process -Name acad -ErrorAction SilentlyContinue).Count -eq 0) {
            $apiResult = Install-C3DPluginRuntime $plugin $true
            Assert ($script:apiCalls -eq 1 -and $script:downloadCalls -eq 1) 'Release API branch did not use the injected API and download once.'
            Assert ($apiResult.state -eq 'failed' -and $apiResult.message -match 'SHA-256 mismatch') 'Unpinned release API digest was not enforced.'
            Assert (-not (Test-Path -LiteralPath (Join-Path $localData 'Programs\Horizun\Civil3D-MCP\manifest.json'))) 'Fake download reached installer.'
        }
    }
    finally { $env:APPDATA = $oldAppData; $env:LOCALAPPDATA = $oldLocalData }
    Write-Release $plugin

    # Override process environment only for status fixtures; restore before exit.
    $oldAppData = $env:APPDATA; $oldLocalData = $env:LOCALAPPDATA
    try {
        $env:APPDATA = $appData; $env:LOCALAPPDATA = $localData
        $paths = Get-C3DInstallPaths
        [IO.Directory]::CreateDirectory((Split-Path -Parent $paths.Server)) | Out-Null
        [IO.Directory]::CreateDirectory($paths.Bundle) | Out-Null
        [IO.File]::WriteAllText($paths.Server, 'synthetic-server')
        [IO.File]::WriteAllText((Join-Path $paths.Bundle 'PackageContents.xml'), 'synthetic-bundle')
        $serverHash = (Get-FileHash -LiteralPath $paths.Server -Algorithm SHA256).Hash
        $bundleHash = (Get-FileHash -LiteralPath (Join-Path $paths.Bundle 'PackageContents.xml') -Algorithm SHA256).Hash
        $manifest = [ordered]@{
            schema = 1; product = 'Horizun Civil 3D MCP'; version = '1.2.3'
            server_sha256 = @{'horizun-civil3d-mcp.exe' = $serverHash}
            bundle_sha256 = @{'PackageContents.xml' = $bundleHash}
        }
        $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $paths.Manifest
        Assert ((Get-C3DRuntimeStatus $plugin).state -eq 'ready') 'Valid hash manifest was not ready.'
        Remove-Item -LiteralPath $paths.Server
        Assert ((Get-C3DRuntimeStatus $plugin).state -eq 'degraded') 'Missing server file was accepted.'
        [IO.File]::WriteAllText($paths.Server, 'synthetic-server')
        [IO.File]::WriteAllText($paths.Server, 'tampered-server')
        Assert ((Get-C3DRuntimeStatus $plugin).state -eq 'degraded') 'Tampered server was accepted.'
        [IO.File]::WriteAllText($paths.Server, 'synthetic-server')
        $manifest.version = '9.9.9'
        $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $paths.Manifest
        Assert ((Get-C3DRuntimeStatus $plugin).state -eq 'update_required') 'Wrong installed version was accepted.'
        $manifest.version = '1.2.3'
        $manifest.server_sha256 = @{'../outside.exe' = $serverHash; 'horizun-civil3d-mcp.exe' = $serverHash}
        $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $paths.Manifest
        Assert ((Get-C3DRuntimeStatus $plugin).state -eq 'degraded') 'Traversal in manifest was accepted.'
        [IO.File]::WriteAllText($paths.Manifest, '{invalid')
        Assert ((Get-C3DRuntimeStatus $plugin).state -eq 'degraded') 'Malformed manifest was accepted.'

        # Creating a file symlink needs Developer Mode or elevation on some hosts.
        $manifest.server_sha256 = @{'horizun-civil3d-mcp.exe' = $serverHash}
        $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $paths.Manifest
        $linkTarget = Join-Path (Split-Path -Parent $paths.Server) 'synthetic-target.exe'
        [IO.File]::WriteAllText($linkTarget, 'synthetic-server')
        Remove-Item -LiteralPath $paths.Server -Force
        $linkCreated = $false
        try {
            New-Item -ItemType SymbolicLink -Path $paths.Server -Target $linkTarget -ErrorAction Stop | Out-Null
            $linkCreated = $true
        }
        catch { Write-Host 'Symlink fixture unavailable; reparse check skipped on this host.' }
        if ($linkCreated) {
            Assert ((Get-C3DRuntimeStatus $plugin).state -eq 'degraded') 'Reparse-point server was accepted.'
            Remove-Item -LiteralPath $paths.Server -Force
        }

        if ($PublishedServerPath) {
            if (-not (Test-Path -LiteralPath $PublishedServerPath -PathType Leaf)) { throw "Published test server is missing: $PublishedServerPath" }
            Copy-Item -LiteralPath $PublishedServerPath -Destination $paths.Server -Force
            $manifest.server_sha256 = @{'horizun-civil3d-mcp.exe' = (Get-FileHash -LiteralPath $paths.Server -Algorithm SHA256).Hash}
            $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $paths.Manifest
            Assert ((Get-C3DRuntimeStatus $plugin).state -eq 'ready') 'Published server fixture was not ready.'
            $forward = @(Invoke-Launcher $launcher $appData $localData @(
                '{"jsonrpc":"2.0","id":11,"method":"initialize","params":{"protocolVersion":"2024-11-05"}}',
                '{"jsonrpc":"2.0","method":"notifications/initialized"}',
                '{"jsonrpc":"2.0","id":12,"method":"ping"}',
                '{"jsonrpc":"2.0","id":13,"method":"tools/list"}',
                '{"jsonrpc":"2.0","id":14,"method":"tools/call","params":{"name":"horizun_c3d_capabilities","arguments":{}}}'
            ) -AllowStderr)
            Assert ($forward.Count -eq 4) 'Forwarded MCP requests did not all get replies.'
            Assert ($forward[0].result.serverInfo.name -eq 'horizun-civil3d') 'Forwarding reached the bootstrap instead of the published server.'
            Assert ($forward[1].id -eq 12 -and $forward[2].id -eq 13) 'Native stdio forwarding lost a reply.'
            Assert (@($forward[2].result.tools).Count -gt 2) 'Published server did not list drawing tools.'
            Assert ($forward[3].id -eq 14 -and -not $forward[3].result.isError) 'Forwarding could not query the capability catalog.'
            $catalog = $forward[3].result.structuredContent
            $expectedActionCount = 0
            foreach ($listedTool in @($forward[2].result.tools)) {
                $actionProperty = $listedTool.inputSchema.properties.PSObject.Properties['action']
                if ($actionProperty -and $actionProperty.Value.PSObject.Properties['enum']) {
                    $expectedActionCount += @($actionProperty.Value.enum).Count
                }
            }
            Assert ($catalog.total_tool_count -eq @($forward[2].result.tools).Count) 'Catalog tool count differs from tools/list.'
            Assert ($catalog.total_action_count -eq $expectedActionCount) 'Catalog action count differs from tools/list.'
            Assert ($catalog.source -eq 'declared_contract' -and -not $catalog.live_verified) 'Catalog fabricated host verification.'
        }
    }
    finally { $env:APPDATA = $oldAppData; $env:LOCALAPPDATA = $oldLocalData }

    $zip = Join-Path $root 'traversal.zip'
    Make-Zip $zip @('../outside.txt', 'install.ps1', 'server/horizun-civil3d-mcp.exe')
    Expect-Throw { Expand-C3DRelease $zip (Join-Path $root 'unpack-traversal') } 'ZIP traversal was accepted.'
    Assert (-not (Test-Path -LiteralPath (Join-Path $root 'outside.txt'))) 'ZIP traversal wrote outside extraction root.'
    $zip = Join-Path $root 'missing-installer.zip'
    Make-Zip $zip @('server/horizun-civil3d-mcp.exe')
    Expect-Throw { Expand-C3DRelease $zip (Join-Path $root 'unpack-missing') } 'ZIP without installer was accepted.'
    Write-Host 'Plugin bootstrap tests passed.'
}
finally {
    $resolved = [IO.Path]::GetFullPath($root)
    if (-not $resolved.StartsWith($tempParent, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path $resolved -Leaf) -notmatch '^horizun-c3d-plugin-test-[a-f0-9]{32}$') {
        throw 'Unsafe plugin test cleanup path.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction SilentlyContinue
}
exit 0
