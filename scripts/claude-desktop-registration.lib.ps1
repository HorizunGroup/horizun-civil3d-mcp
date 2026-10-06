#Requires -Version 5.1

function Get-HorizunClaudeDesktopRegistration {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ConfigPath,
        [Parameter(Mandatory = $true)][string]$ServerPath
    )

    if (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) {
        throw "Claude Desktop config not found at $ConfigPath"
    }
    try { $cfg = Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json -ErrorAction Stop }
    catch { throw "Claude Desktop config is not valid JSON at $ConfigPath : $($_.Exception.Message)" }
    if ($null -eq $cfg -or $cfg -isnot [pscustomobject]) {
        throw "Claude Desktop config must be a JSON object at $ConfigPath"
    }
    $serversProperty = $cfg.PSObject.Properties['mcpServers']
    if ($null -eq $serversProperty) {
        $cfg | Add-Member -NotePropertyName mcpServers -NotePropertyValue ([pscustomobject]@{})
    }
    elseif ($null -eq $serversProperty.Value -or $serversProperty.Value -isnot [pscustomobject]) {
        throw "Claude Desktop mcpServers must be a JSON object at $ConfigPath"
    }
    $entry = [pscustomobject]@{ command = $ServerPath; args = @(); env = [pscustomobject]@{} }
    if ($cfg.mcpServers.PSObject.Properties.Name -contains 'horizun-civil3d') {
        $cfg.mcpServers.'horizun-civil3d' = $entry
    }
    else {
        $cfg.mcpServers | Add-Member -NotePropertyName 'horizun-civil3d' -NotePropertyValue $entry
    }
    return ($cfg | ConvertTo-Json -Depth 32)
}

function Set-HorizunClaudeDesktopRegistration {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ConfigPath,
        [Parameter(Mandatory = $true)][string]$Json
    )
    $tempPath = "$ConfigPath.tmp-$([guid]::NewGuid().ToString('N'))"
    $replaceBackup = "$tempPath.bak"
    try {
        [IO.File]::WriteAllText($tempPath, $Json, (New-Object Text.UTF8Encoding($false)))
        [IO.File]::Replace($tempPath, $ConfigPath, $replaceBackup)
    }
    finally {
        if (Test-Path -LiteralPath $tempPath) { Remove-Item -LiteralPath $tempPath -Force }
        if (Test-Path -LiteralPath $replaceBackup) { Remove-Item -LiteralPath $replaceBackup -Force }
    }
}
