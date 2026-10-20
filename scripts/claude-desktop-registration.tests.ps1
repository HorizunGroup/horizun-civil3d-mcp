#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'claude-desktop-registration.lib.ps1')

function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

$root = Join-Path ([IO.Path]::GetTempPath()) ('horizun-claude-registration-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
try {
    $config = Join-Path $root 'claude_desktop_config.json'
    $server = 'C:\Programs\Horizun\horizun-civil3d-mcp.exe'
    [IO.File]::WriteAllText($config, '{"mcpServers":{"another":{"command":"leave-me"}},"theme":"dark"}')
    $json = Get-HorizunClaudeDesktopRegistration -ConfigPath $config -ServerPath $server
    $before = [IO.File]::ReadAllText($config)
    Assert ($before.Contains('leave-me')) 'Preflight modified the config.'
    Set-HorizunClaudeDesktopRegistration -ConfigPath $config -Json $json
    $after = Get-Content -LiteralPath $config -Raw | ConvertFrom-Json
    Assert ($after.theme -eq 'dark') 'Registration removed an unrelated setting.'
    Assert ($after.mcpServers.another.command -eq 'leave-me') 'Registration removed another MCP server.'
    Assert ($after.mcpServers.'horizun-civil3d'.command -eq $server) 'Registration used the wrong server.'
    Assert (@($after.mcpServers.'horizun-civil3d'.args).Count -eq 0) 'Registration added arguments.'

    $again = Get-HorizunClaudeDesktopRegistration -ConfigPath $config -ServerPath $server
    Set-HorizunClaudeDesktopRegistration -ConfigPath $config -Json $again
    $afterAgain = Get-Content -LiteralPath $config -Raw | ConvertFrom-Json
    Assert (@($afterAgain.mcpServers.PSObject.Properties.Name | Where-Object { $_ -eq 'horizun-civil3d' }).Count -eq 1) 'Registration is not idempotent.'

    foreach ($bad in @('{broken', 'null', '[]', '{"mcpServers":[]}', '{"mcpServers":null}')) {
        [IO.File]::WriteAllText($config, $bad)
        $failed = $false
        try { Get-HorizunClaudeDesktopRegistration -ConfigPath $config -ServerPath $server | Out-Null }
        catch { $failed = $true }
        Assert $failed "Invalid config was accepted: $bad"
        Assert ([IO.File]::ReadAllText($config) -eq $bad) "Invalid config was modified: $bad"
    }

    Remove-Item -LiteralPath $config
    $failed = $false
    try { Get-HorizunClaudeDesktopRegistration -ConfigPath $config -ServerPath $server | Out-Null }
    catch { $failed = $true }
    Assert $failed 'Missing config was accepted.'

    Write-Host 'Claude Desktop registration tests passed.'
}
finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
exit 0
