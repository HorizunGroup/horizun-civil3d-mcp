#Requires -Version 5.1
<# Guided account completion; credentials are read without echo and never passed as arguments. #>
[CmdletBinding()]
param([ValidatePattern('^tunnel_[0-9a-f]{32}$')][string]$TunnelId, [switch]$SetApiKey, [switch]$InstallTunnelClient, [switch]$Force, [switch]$Interactive)
$ErrorActionPreference='Stop'
$helper=Join-Path $PSScriptRoot 'chatgpt-tunnel.ps1'
if ($Interactive) {
    Write-Host 'Crea un Tunnel para Civil 3D: https://platform.openai.com/settings/organization/tunnels'
    Write-Host 'Crea una runtime API key: https://platform.openai.com/settings/organization/api-keys'
    Write-Host 'El Tunnel ID identifica la conexion; la API key se pedira de forma oculta.'
    if (-not $TunnelId) { $TunnelId=Read-Host 'Tunnel ID (tunnel_...)' }
    if ($TunnelId -notmatch '^tunnel_[0-9a-f]{32}$') { Write-Host 'Hace falta un Tunnel ID valido para continuar.'; exit 3 }
    . (Join-Path $PSScriptRoot 'chatgpt-secret.lib.ps1')
    . (Join-Path $PSScriptRoot 'chatgpt-tunnel.lib.ps1')
    if (-not (Test-HorizunChatGptSecret -StateRoot (Get-HorizunTunnelStateRoot))) { $SetApiKey=$true }
}
if ($InstallTunnelClient) { & (Join-Path $PSScriptRoot 'install-tunnel-client.ps1'); if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE } }
if (-not $TunnelId -and -not $SetApiKey) { & $helper -Status; exit $LASTEXITCODE }
$arguments=@{}
if ($SetApiKey) { $arguments.SetApiKey=$true }
if ($TunnelId) {
    $arguments.Init=$true; $arguments.TunnelId=$TunnelId; $arguments.Force=[bool]$Force
    $arguments.Doctor=$true; $arguments.Start=$true; $arguments.IUnderstandTrafficLeavesThisMachine=$true
}
# Start consent is included by this explicitly requested connection command.
& $helper @arguments
exit $LASTEXITCODE
