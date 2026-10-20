#Requires -Version 5.1
<# Read-only by default. -Register appends only installed year folders to existing Civil profiles. #>
[CmdletBinding()]
param(
    [int[]]$Years = @(2025),
    [string]$BundlePath = (Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\Horizun.Civil3D.bundle'),
    [switch]$Register
)
$ErrorActionPreference = 'Stop'

function Get-HzTrustSeries([int]$Year) {
    switch ($Year) { 2024 { 'R24.3' } 2025 { 'R25.0' } 2026 { 'R25.1' } 2027 { 'R26.0' } default { throw "Unsupported Civil year: $Year" } }
}
function Resolve-HzTrustModule([string]$Canonical, [int]$Year, $Xml, [scriptblock]$FileExists) {
    $series=Get-HzTrustSeries $Year
    $entries=@($Xml.SelectNodes('/ApplicationPackage/Components/ComponentEntry') | Where-Object {
        $_.AppType -ieq '.Net' -and $_.RuntimeRequirements.SeriesMin -ceq $series -and $_.RuntimeRequirements.SeriesMax -ceq $series
    })
    if ($entries.Count -ne 1) { throw "Expected exactly one .NET component with an exact Civil $Year series range." }
    $module=[string]$entries[0].ModuleName
    $normalized=$module.Replace('/','\')
    $canonicalRelative='.\Contents\'+$Year+'\Horizun.Civil3D.dll'
    $pattern='^\.\\Contents\\releases\\(0\.9\.[12]-[0-9]{8}-[0-9]{6})\\'+$Year+'\\Horizun\.Civil3D\.dll$'
    if (-not $normalized.Equals($canonicalRelative,[StringComparison]::OrdinalIgnoreCase)) {
        if ($normalized -notmatch $pattern) { throw 'Declared module must use a canonical year path or safe immutable 0.9.1/0.9.2 generation path; traversal and other modules are refused.' }
        $generation=$Matches[1]
        [void][DateTime]::ParseExact($generation.Substring(6),'yyyyMMdd-HHmmss',[Globalization.CultureInfo]::InvariantCulture)
    }
    $full=[IO.Path]::GetFullPath([IO.Path]::Combine($Canonical,$normalized.Substring(2)))
    if (-not $full.StartsWith($Canonical.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Declared module escapes the canonical bundle.' }
    if (-not (& $FileExists $full)) { throw "Declared module is missing: $full" }
    return [IO.Path]::GetDirectoryName($full)
}
function Get-HzTrustAppend([string]$Original, [string]$Directory) {
    foreach ($entry in ($Original -split ';')) {
        $path = $entry.Trim().Trim('"').Replace('/', '\').TrimEnd('\')
        if ($path.Equals($Directory.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) { return $Original }
        if ($path.EndsWith('\...')) {
            $parent = $path.Substring(0, $path.Length-4).TrimEnd('\')
            if ($Directory.StartsWith($parent+'\', [StringComparison]::OrdinalIgnoreCase)) { return $Original }
        }
    }
    if ([string]::IsNullOrEmpty($Original)) { return $Directory }
    return $Original + $(if ($Original.EndsWith(';')) { '' } else { ';' }) + $Directory
}
function Get-HzTrustValue($Key, [string]$Name) {
    $exists = @($Key.GetValueNames() | Where-Object { $_ -ieq $Name }).Count -gt 0
    if (-not $exists) { return [pscustomobject]@{ exists=$false; kind=$null; value=$null } }
    return [pscustomobject]@{ exists=$true; kind=$Key.GetValueKind($Name).ToString(); value=$Key.GetValue($Name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) }
}
function Get-HzTrustChange([int]$Year, [string]$RegistryPath, $Snapshot, $SecureLoad, [string]$Directory) {
    if ($Snapshot.exists -and $Snapshot.kind -notin @('String','ExpandString')) { throw "TRUSTEDPATHS has unexpected registry kind in $RegistryPath" }
    $newValue = Get-HzTrustAppend ([string]$Snapshot.value) $Directory
    return [pscustomobject]@{ year=$Year; registry_path=$RegistryPath; trusted_directory=$Directory; original=$Snapshot;
        secureload_before=$SecureLoad; proposed=$newValue; change_needed=((-not $Snapshot.exists) -or $newValue -cne [string]$Snapshot.value);
        status='inspected' }
}
function Restore-HzTrustSnapshot($Key, $Change) {
    $current = Get-HzTrustValue $Key 'TRUSTEDPATHS'
    $writtenKind = if ($Change.original.exists) { $Change.original.kind } else { 'String' }
    if (-not $current.exists -or $current.value -cne $Change.proposed -or $current.kind -ne $writtenKind) {
        throw 'Rollback refused: TRUSTEDPATHS was changed by another writer; original backup retained.'
    }
    if ($Change.original.exists) {
        $kind = [Enum]::Parse([Microsoft.Win32.RegistryValueKind],$Change.original.kind)
        $Key.SetValue('TRUSTEDPATHS',$Change.original.value,$kind)
    } else { $Key.DeleteValue('TRUSTEDPATHS',$false) }
    $after = Get-HzTrustValue $Key 'TRUSTEDPATHS'
    if (($after | ConvertTo-Json -Compress) -cne ($Change.original | ConvertTo-Json -Compress)) { throw 'Rollback snapshot verification failed.' }
}
function Invoke-HzPluginTrust {
    $canonical = [IO.Path]::GetFullPath((Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\Horizun.Civil3D.bundle')).TrimEnd('\')
    if (-not [IO.Path]::IsPathRooted($BundlePath) -or -not [IO.Path]::GetFullPath($BundlePath).TrimEnd('\').Equals($canonical,[StringComparison]::OrdinalIgnoreCase)) {
        throw 'BundlePath must be the canonical per-user Horizun.Civil3D.bundle path.'
    }
    if (-not $Years.Count -or @($Years | Select-Object -Unique).Count -ne $Years.Count) { throw 'Years must be nonempty and unique.' }
    $manifestPath=Join-Path $canonical 'PackageContents.xml'
    if (-not [IO.File]::Exists($manifestPath) -or (Get-Item -LiteralPath $manifestPath).Length -gt 1MB) { throw 'A bounded installed PackageContents.xml is required.' }
    $readerSettings=New-Object Xml.XmlReaderSettings
    $readerSettings.DtdProcessing=[Xml.DtdProcessing]::Prohibit
    $readerSettings.XmlResolver=$null
    $reader=[Xml.XmlReader]::Create($manifestPath,$readerSettings)
    $packageXml=New-Object Xml.XmlDocument
    $packageXml.XmlResolver=$null
    try { $packageXml.Load($reader) } finally { $reader.Dispose() }
    $changes = @(); $missing = @()
    foreach ($year in $Years) {
        $series = Get-HzTrustSeries $year
        $directory = Resolve-HzTrustModule $canonical $year $packageXml { param($path) [IO.File]::Exists($path) }
        $seriesPath = 'Software\Autodesk\AutoCAD\'+$series
        $seriesKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($seriesPath,$false)
        if ($null -eq $seriesKey) { $missing += $year; continue }
        $beforeCount = $changes.Count
        try {
            foreach ($product in $seriesKey.GetSubKeyNames()) {
                if ($product -notmatch '^ACAD-[0-9A-Fa-f]+:[0-9A-Fa-f]+$') { continue }
                foreach ($profile in @('<<C3D_Metric>>','<<C3D_Imperial>>')) {
                    $relative = $seriesPath+'\'+$product+'\Profiles\'+$profile+'\Variables'
                    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($relative,$false)
                    if ($null -eq $key) { continue }
                    try { $changes += Get-HzTrustChange $year $relative (Get-HzTrustValue $key 'TRUSTEDPATHS') (Get-HzTrustValue $key 'SECURELOAD') $directory }
                    finally { $key.Dispose() }
                }
            }
        } finally { $seriesKey.Dispose() }
        if ($changes.Count -eq $beforeCount) { $missing += $year }
    }
    $backup = $null
    if ($Register -and @($changes | Where-Object change_needed).Count -gt 0) {
        $backupRoot = Join-Path $env:LOCALAPPDATA 'Programs\Horizun\Civil3D-MCP\_backup'
        [void][IO.Directory]::CreateDirectory($backupRoot)
        $backup = Join-Path $backupRoot ('plugin-trust-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N')+'.json')
        $backupText = [ordered]@{ schema='horizun.civil3d.plugin-trust/v1'; utc=[DateTime]::UtcNow.ToString('o'); bundle=$canonical; profiles=$changes } | ConvertTo-Json -Depth 15
        [IO.File]::WriteAllText($backup,$backupText,(New-Object Text.UTF8Encoding($false)))
        if ([IO.File]::ReadAllText($backup) -cne $backupText) { throw 'Trust backup failed verification; nothing registered.' }
        $applied = New-Object 'Collections.Generic.List[object]'
        try { foreach ($change in $changes) {
            if (-not $change.change_needed) { $change.status='already_trusted'; continue }
            $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($change.registry_path,$true)
            if ($null -eq $key) { throw "Profile disappeared; backup retained: $backup" }
            try {
                $current = Get-HzTrustValue $key 'TRUSTEDPATHS'
                $secure = Get-HzTrustValue $key 'SECURELOAD'
                if (($current | ConvertTo-Json -Compress) -cne ($change.original | ConvertTo-Json -Compress) -or
                    ($secure | ConvertTo-Json -Compress) -cne ($change.secureload_before | ConvertTo-Json -Compress)) { throw 'Profile changed after preflight; refusing stale trust registration.' }
                $kind = if ($change.original.exists) { [Enum]::Parse([Microsoft.Win32.RegistryValueKind],$change.original.kind) } else { [Microsoft.Win32.RegistryValueKind]::String }
                $key.SetValue('TRUSTEDPATHS',$change.proposed,$kind)
                $applied.Add($change)
                $after = Get-HzTrustValue $key 'TRUSTEDPATHS'
                $secureAfter = Get-HzTrustValue $key 'SECURELOAD'
                if ($after.value -cne $change.proposed -or $after.kind -ne $kind.ToString() -or
                    ($secureAfter | ConvertTo-Json -Compress) -cne ($change.secureload_before | ConvertTo-Json -Compress)) { throw 'Trust registration verification failed; backup retained.' }
                $change.status='registered_verified'
            } finally { $key.Dispose() }
        } } catch {
            $registrationFailure = $_.Exception.Message
            $rollbackErrors = @()
            for ($index=$applied.Count-1;$index -ge 0;$index--) {
                $change=$applied[$index]
                $rollbackKey=$null
                try {
                    $rollbackKey=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($change.registry_path,$true)
                    if ($null -eq $rollbackKey) { throw 'Profile disappeared before rollback.' }
                    Restore-HzTrustSnapshot $rollbackKey $change
                    $change.status='rolled_back_verified'
                } catch { $rollbackErrors += ($change.registry_path+': '+$_.Exception.Message); $change.status='rollback_failed_or_concurrent_change' }
                finally { if ($null -ne $rollbackKey) { $rollbackKey.Dispose() } }
            }
            [ordered]@{ readonly=$false; backup=$backup; profiles=$changes; registration_error=$registrationFailure; rollback_errors=$rollbackErrors; secureload_changed=$false } | ConvertTo-Json -Depth 15 | Write-Output
            if ($rollbackErrors.Count) { throw "Trust registration failed with incomplete rollback; backup retained: $backup" }
            throw "Trust registration failed; all applied values restored and verified. Backup: $backup. $registrationFailure"
        }
    }
    [ordered]@{ readonly=(-not $Register); bundle=$canonical; backup=$backup; profiles=$changes; years_without_existing_civil_profiles=$missing;
        secureload_changed=$false; note='Only existing Civil metric/imperial profiles are inspected. No missing key/profile is created. Existing hosts retain their in-memory settings; restart acceptance is separate.' } | ConvertTo-Json -Depth 15
}
if ($MyInvocation.InvocationName -ne '.') { Invoke-HzPluginTrust }
