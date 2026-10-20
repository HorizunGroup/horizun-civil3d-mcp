#Requires -Version 5.1
[CmdletBinding()]
param([switch]$Status)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'plugin-runtime.lib.ps1')
[Console]::InputEncoding = New-Object Text.UTF8Encoding($false)
[Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)
try { $runtime = Get-C3DRuntimeStatus $root }
catch { $runtime = [pscustomobject]@{state='failed'; ready=$false; version='unknown'; message=$_.Exception.Message} }
if ($Status) { $runtime | ConvertTo-Json -Depth 20; exit 0 }
if ($runtime.ready) {
    # Inherit the stdio handles directly: PowerShell pipeline forwarding would
    # buffer replies and prevent a long-lived JSON-RPC conversation.
    $psi = New-Object Diagnostics.ProcessStartInfo
    $psi.FileName = $runtime.server; $psi.UseShellExecute = $false
    $process = [Diagnostics.Process]::Start($psi)
    $process.WaitForExit()
    exit $process.ExitCode
}

function Send-C3DMessage($Message) {
    [Console]::Out.WriteLine(($Message | ConvertTo-Json -Depth 40 -Compress))
    [Console]::Out.Flush()
}
function C3D-ToolResult($Data) {
    return @{content=@(@{type='text'; text=($Data | ConvertTo-Json -Depth 20 -Compress)}); structuredContent=$Data; isError=($Data.state -in @('failed','degraded','blocked'))}
}
$schema = @{type='object'; properties=@{}; additionalProperties=$false}
$tools = @(
    @{name='horizun_c3d_install_status'; description='Diagnose the installed Civil 3D MCP runtime and its SHA-256 hashes. Read-only; live bridge verification requires horizun_c3d_health after Civil 3D starts.'; inputSchema=$schema; annotations=@{readOnlyHint=$true; destructiveHint=$false; openWorldHint=$false}},
    @{name='horizun_c3d_install_runtime'; description='Install the matching prebuilt Civil 3D release for this user. Plan by default; save drawings, close Civil 3D and pass confirm=true to apply. Restart the MCP client afterwards.'; inputSchema=@{type='object'; properties=@{confirm=@{type='boolean'; default=$false}}; additionalProperties=$false}; annotations=@{readOnlyHint=$false; destructiveHint=$true; openWorldHint=$true}}
)
$versions = @('2025-06-18','2025-03-26','2024-11-05')
# Bound input while reading and drain oversize lines without buffering them.
while ($true) {
    $line = New-Object Text.StringBuilder
    $bytes = 0; $oversize = $false; $eof = $false
    while ($true) {
        $ch = [Console]::In.Read()
        if ($ch -eq -1) { $eof = $true; break }
        if ($ch -eq 10) { break }
        $bytes += [Text.Encoding]::UTF8.GetByteCount([string][char]$ch)
        if ($bytes -gt 4MB) { $oversize = $true; continue }
        [void]$line.Append([char]$ch)
    }
    if ($eof -and $line.Length -eq 0 -and -not $oversize) { break }
    if ($oversize) { Send-C3DMessage @{jsonrpc='2.0'; id=$null; error=@{code=-32700; message='Request exceeds the 4 MiB limit.'}}; continue }
    if ($line.Length -eq 0) { continue }
    $id = $null
    try {
        $msg = $line.ToString().TrimStart([char]0xFEFF) | ConvertFrom-Json
        $idProperty = $msg.PSObject.Properties['id']
        if ($null -eq $idProperty -or $null -eq $idProperty.Value) { continue }
        $id = $idProperty.Value
        $result = $null
        switch ($msg.method) {
            'initialize' {
                $version = $versions[0]
                if ($msg.params -and $versions -contains $msg.params.protocolVersion) { $version = $msg.params.protocolVersion }
                $result = @{protocolVersion=$version; capabilities=@{tools=@{listChanged=$false}}; serverInfo=@{name='horizun-civil3d-installer'; version=$runtime.version}; instructions='The drawing runtime is not ready. Use horizun_c3d_install_status, then plan installation with horizun_c3d_install_runtime. Save drawings and close Civil 3D before applying. Restart the MCP client after ready; then verify horizun_c3d_health.'}
            }
            'ping' { $result = @{} }
            'tools/list' { $result = @{tools=$tools} }
            'tools/call' {
                if ($msg.params.name -eq 'horizun_c3d_install_status') { $result = C3D-ToolResult (Get-C3DRuntimeStatus $root) }
                elseif ($msg.params.name -eq 'horizun_c3d_install_runtime') {
                    $confirm = $false
                    $arguments = $msg.params.arguments
                    if ($arguments) {
                        foreach ($key in $arguments.PSObject.Properties.Name) { if ($key -ne 'confirm') { throw "Unknown installation argument: $key" } }
                        $cp = $arguments.PSObject.Properties['confirm']
                        if ($cp) {
                            if ($cp.Value -isnot [bool]) { throw 'confirm must be a boolean.' }
                            $confirm = $cp.Value
                        }
                    }
                    $result = C3D-ToolResult (Install-C3DPluginRuntime $root $confirm)
                } else { throw 'Unknown installation tool.' }
            }
            default { Send-C3DMessage @{jsonrpc='2.0'; id=$id; error=@{code=-32601; message='Method not found.'}}; continue }
        }
        Send-C3DMessage @{jsonrpc='2.0'; id=$id; result=$result}
    } catch { Send-C3DMessage @{jsonrpc='2.0'; id=$id; error=@{code=-32602; message=$_.Exception.Message}} }
}
