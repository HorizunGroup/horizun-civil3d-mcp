#Requires -Version 5.1
# ChatGPT presence only; account eligibility is checked in ChatGPT.
function Get-HorizunChatGptDesktop {
    <#
      Whether the ChatGPT desktop app is installed. Presence of the app says
      NOTHING about whether this account may create developer-mode MCP apps -
      that is a workspace/plan permission, checked in the product, not on disk.
      The status this returns therefore never claims capability.
    #>
    [CmdletBinding()]
    param()
    $info = [ordered]@{
        client              = 'chatgpt-desktop'
        installed           = $false
        package_family_name = $null
        version             = $null
        install_location    = $null
        running             = $false
    }
    $appx = $null
    try { $appx = Get-AppxPackage -Name 'OpenAI.ChatGPT-Desktop' -ErrorAction SilentlyContinue | Select-Object -First 1 } catch { $appx = $null }
    if ($appx) {
        $info.installed = $true
        $info.package_family_name = $appx.PackageFamilyName
        $info.version = [string]$appx.Version
        $info.install_location = $appx.InstallLocation
    }
    $info.running = @(Get-Process -Name 'ChatGPT' -ErrorAction SilentlyContinue).Count -gt 0
    return [pscustomobject]$info
}

