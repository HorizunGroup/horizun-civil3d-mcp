#Requires -Version 5.1
# Shared file list for both the complete installer and the helper-only installer.
$script:HorizunCivil3DClientTools = @(
    'chatgpt-tunnel.ps1',
    'chatgpt-tunnel.lib.ps1',
    'chatgpt-secret.lib.ps1',
    'mcp-clients.lib.ps1',
    'mcp-stdio.lib.ps1',
    'integration-status.lib.ps1',
    'process.lib.ps1',
    'client-tools.package.ps1',
    'install-tunnel-client.ps1',
    'connect-chatgpt.ps1'
)

function Copy-HorizunCivil3DClientTools {
    param([Parameter(Mandatory=$true)][string]$Destination)
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($file in $script:HorizunCivil3DClientTools) {
        $source = Join-Path $PSScriptRoot $file
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing client helper: $source" }
        Copy-Item -LiteralPath $source -Destination (Join-Path $Destination $file) -Force
    }
}
