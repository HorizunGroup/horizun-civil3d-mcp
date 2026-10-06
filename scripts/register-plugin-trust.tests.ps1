#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'register-plugin-trust.ps1')
function Assert([bool]$Pass,[string]$Why) { if (-not $Pass) { throw $Why } }
$folder='C:\Users\fixture\AppData\Roaming\Autodesk\ApplicationPlugins\Horizun.Civil3D.bundle\Contents\2025'
Assert ((Get-HzTrustSeries 2024) -eq 'R24.3') '2024 series mismatch.'
Assert ((Get-HzTrustSeries 2025) -eq 'R25.0') '2025 series mismatch.'
Assert ((Get-HzTrustSeries 2026) -eq 'R25.1') '2026 series mismatch.'
Assert ((Get-HzTrustSeries 2027) -eq 'R26.0') '2027 series mismatch.'
$refused=$false;try{Get-HzTrustSeries 2023|Out-Null}catch{$refused=$true};Assert $refused 'Unsupported year accepted.'
Assert ((Get-HzTrustAppend '' $folder) -ceq $folder) 'Empty paths append failed.'
Assert ((Get-HzTrustAppend 'C:\Existing;"D:\Preserve exact"' $folder) -ceq ('C:\Existing;"D:\Preserve exact";'+$folder)) 'Existing entries changed.'
Assert ((Get-HzTrustAppend 'C:\Existing;' $folder) -ceq ('C:\Existing;'+$folder)) 'Trailing delimiter changed.'
Assert ((Get-HzTrustAppend ('"'+$folder+'"') $folder) -ceq ('"'+$folder+'"')) 'Already trusted exact path duplicated.'
Assert ((Get-HzTrustAppend $folder.ToLowerInvariant() $folder) -ceq $folder.ToLowerInvariant()) 'Case-insensitive path duplicated.'
$parent='C:\Users\fixture\AppData\Roaming\Autodesk\ApplicationPlugins\Horizun.Civil3D.bundle\...'
Assert ((Get-HzTrustAppend $parent $folder) -ceq $parent) 'Existing recursive trust not recognized.'
Assert ((Get-HzTrustAppend 'C:\Users\fixture\AppData\Roaming\Autodesk\ApplicationPlugins\Horizun.Civil3D.bundleExtra\...' $folder) -cne 'C:\Users\fixture\AppData\Roaming\Autodesk\ApplicationPlugins\Horizun.Civil3D.bundleExtra\...') 'Sibling-prefix path mistaken for parent.'
$snapshot=[pscustomobject]@{exists=$true;kind='ExpandString';value='C:\Existing'}
$secure=[pscustomobject]@{exists=$true;kind='DWord';value=2}
$plan=Get-HzTrustChange 2025 'FAKE_CIVIL_PROFILE' $snapshot $secure $folder
Assert ($plan.original.value -ceq 'C:\Existing' -and $plan.original.kind -eq 'ExpandString') 'Original kind/value lost.'
Assert ($plan.secureload_before.value -eq 2 -and $secure.value -eq 2) 'SECURELOAD changed by planning.'
Assert ($plan.proposed -ceq ('C:\Existing;'+$folder)) 'Plan grants more than the exact installed year folder.'
$absent=Get-HzTrustChange 2025 'FAKE_CIVIL_PROFILE' ([pscustomobject]@{exists=$false;kind=$null;value=$null}) $secure $folder
Assert (-not $absent.original.exists -and $null -eq $absent.original.kind) 'Absent original backup information lost.'
$refused=$false;try{Get-HzTrustChange 2025 'FAKE' ([pscustomobject]@{exists=$true;kind='DWord';value=0}) $secure $folder|Out-Null}catch{$refused=$true};Assert $refused 'Unexpected registry kind accepted.'
# A mock registry object verifies read snapshots without using any real HKCU tree.
$fake=[pscustomobject]@{}
$fake|Add-Member ScriptMethod GetValueNames { @('TRUSTEDPATHS') }
$fake|Add-Member ScriptMethod GetValueKind { [Microsoft.Win32.RegistryValueKind]::String }
$fake|Add-Member ScriptMethod GetValue { param($name,$unused,$options) 'C:\Existing' }
$read=Get-HzTrustValue $fake 'TRUSTEDPATHS'
Assert ($read.exists -and $read.kind -eq 'String' -and $read.value -ceq 'C:\Existing') 'Mock snapshot read failed.'
$missing=Get-HzTrustValue $fake 'SECURELOAD'
Assert (-not $missing.exists -and $null -eq $missing.value) 'Missing registry value invented.'
$mock=[pscustomobject]@{Values=@{TRUSTEDPATHS=$plan.proposed};Kinds=@{TRUSTEDPATHS='ExpandString'}}
$mock|Add-Member ScriptMethod GetValueNames { @($this.Values.Keys) }
$mock|Add-Member ScriptMethod GetValueKind { param($name) [Enum]::Parse([Microsoft.Win32.RegistryValueKind],$this.Kinds[$name]) }
$mock|Add-Member ScriptMethod GetValue { param($name,$unused,$options) $this.Values[$name] }
$mock|Add-Member ScriptMethod SetValue { param($name,$value,$kind) $this.Values[$name]=$value; $this.Kinds[$name]=$kind.ToString() }
$mock|Add-Member ScriptMethod DeleteValue { param($name,$unused) $this.Values.Remove($name);$this.Kinds.Remove($name) }
Restore-HzTrustSnapshot $mock $plan
Assert ($mock.Values.TRUSTEDPATHS -ceq 'C:\Existing' -and $mock.Kinds.TRUSTEDPATHS -eq 'ExpandString') 'Rollback lost original value/kind.'
$mock.Values.TRUSTEDPATHS=$absent.proposed;$mock.Kinds.TRUSTEDPATHS='String'
Restore-HzTrustSnapshot $mock $absent
Assert (-not $mock.Values.ContainsKey('TRUSTEDPATHS')) 'Absent original value was not restored as absent.'
$mock.Values.TRUSTEDPATHS='C:\OWNER_CHANGED';$mock.Kinds.TRUSTEDPATHS='ExpandString'
$refused=$false;try{Restore-HzTrustSnapshot $mock $plan}catch{$refused=$true}
Assert ($refused -and $mock.Values.TRUSTEDPATHS -ceq 'C:\OWNER_CHANGED') 'Rollback overwrote a concurrent owner change.'
Write-Output 'PASS: 22 plugin trust planning/snapshot/rollback checks; no real registry writes.'
function TestModuleXml([string]$module,[string]$minimum='R25.0',[string]$maximum='R25.0') {
    $xml=New-Object Xml.XmlDocument
    $xml.LoadXml('<ApplicationPackage><Components><ComponentEntry AppType=".Net" ModuleName="'+$module+'"><RuntimeRequirements SeriesMin="'+$minimum+'" SeriesMax="'+$maximum+'" /></ComponentEntry></Components></ApplicationPackage>')
    return $xml
}
$bundle='C:\Users\fixture\AppData\Roaming\Autodesk\ApplicationPlugins\Horizun.Civil3D.bundle'
$resolve=Resolve-HzTrustModule $bundle 2025 (TestModuleXml './Contents/2025/Horizun.Civil3D.dll') {param($path) $true}
Assert ($resolve -ceq $folder) 'Canonical module did not resolve exactly.'
$resolve=Resolve-HzTrustModule $bundle 2025 (TestModuleXml './Contents/releases/0.9.1-20261005-230345/2025/Horizun.Civil3D.dll') {param($path) $true}
Assert ($resolve -ceq ($bundle+'\Contents\releases\0.9.1-20261005-230345\2025')) 'Immutable module did not resolve exactly.'
foreach($module in @('../outside/Horizun.Civil3D.dll','./Contents/2024/Horizun.Civil3D.dll','./Contents/2025/../2024/Horizun.Civil3D.dll',
    './Contents/releases/0.9.1-20261005-230345/2024/Horizun.Civil3D.dll','C:\outside\Horizun.Civil3D.dll',
    './Contents/releases/0.9.1-20269999-230345/2025/Horizun.Civil3D.dll')) {
    $refused=$false;try{Resolve-HzTrustModule $bundle 2025 (TestModuleXml $module) {param($path) $true}|Out-Null}catch{$refused=$true}
    Assert $refused ('Unsafe module accepted: '+$module)
}
$refused=$false;try{Resolve-HzTrustModule $bundle 2025 (TestModuleXml './Contents/2025/Horizun.Civil3D.dll') {param($path) $false}|Out-Null}catch{$refused=$true};Assert $refused 'Missing declared module accepted.'
$refused=$false;try{Resolve-HzTrustModule $bundle 2025 (TestModuleXml './Contents/2025/Horizun.Civil3D.dll' 'R25.0' 'R25.1') {param($path) $true}|Out-Null}catch{$refused=$true};Assert $refused 'Broad series range accepted.'
$duplicate=TestModuleXml './Contents/2025/Horizun.Civil3D.dll'
[void]$duplicate.ApplicationPackage.Components.AppendChild($duplicate.ApplicationPackage.Components.ComponentEntry.CloneNode($true))
$refused=$false;try{Resolve-HzTrustModule $bundle 2025 $duplicate {param($path) $true}|Out-Null}catch{$refused=$true};Assert $refused 'Ambiguous module declarations accepted.'
$native=TestModuleXml './Contents/2025/Horizun.Civil3D.dll'
$native.ApplicationPackage.Components.ComponentEntry.SetAttribute('AppType','Arx')
$refused=$false;try{Resolve-HzTrustModule $bundle 2025 $native {param($path) $true}|Out-Null}catch{$refused=$true};Assert $refused 'Non-.NET module declaration accepted.'
Write-Output 'PASS: 12 manifest module resolution checks; no real registry writes.'
