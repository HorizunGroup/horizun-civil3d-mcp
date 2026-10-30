#Requires -Version 5.1
function Get-HorizunInstallCommit {
    param([bool] $PackageMode, [string] $ScriptRoot, [string] $Repo)
    if ($PackageMode) {
        try {
            $info = Get-Content -LiteralPath (Join-Path $ScriptRoot 'build-info.json') -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
            if ($info.commit -is [string]) { return $info.commit }
        } catch { }
        return ''
    }
    try {
        $result = Invoke-HorizunNative -FilePath 'git' -Arguments @('-C', $Repo, 'rev-parse', '--short', 'HEAD')
        if ($result.ExitCode -eq 0) { return $result.StdOut.Trim() }
    } catch { }
    return ''
}

function Invoke-HorizunNative {
    param([string]$FilePath, [string[]]$Arguments)
    $ErrorActionPreference = 'Continue'
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $FilePath
    # Windows command-line quoting: escape backslashes before quotes and at the end.
    $info.Arguments = (($Arguments | ForEach-Object {
        if ($_ -and $_ -notmatch '[\s"]') { $_ }
        else { '"' + ($_ -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"' }
    }) -join ' ')
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $info
    try {
        [void]$process.Start()
        $out = $process.StandardOutput.ReadToEndAsync()
        $err = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        return [pscustomobject]@{ ExitCode = $process.ExitCode; StdOut = $out.Result; StdErr = $err.Result }
    } finally { $process.Dispose() }
}

function Get-HorizunInstallFailureMessage {
    param([string]$Cause, [bool]$Restored, [int]$ErrorCount, [string]$BackupDir)
    $causeText = $Cause -replace '[\r\n]+', ' '
    if ($Restored) { return "Install failed ($causeText); previous installation restored." }
    return "Install failed ($causeText); rollback_incomplete: $ErrorCount undo action(s) failed. Inspect the backup at $BackupDir before retrying."
}

function Read-HorizunHostBuilds {
    param([Parameter(Mandatory=$true)][string]$Path)
    foreach ($record in (Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json)) { $record }
}

function New-HorizunAssembledBundle {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)][string]$StageBundle,
        [Parameter(Mandatory=$true)][string]$PreviousBundle,
        [Parameter(Mandatory=$true)][string]$Destination,
        [Parameter(Mandatory=$true)][int[]]$InstallYears,
        [Parameter(Mandatory=$true)][int[]]$SupportedYears
    )
    if (Test-Path -LiteralPath $Destination) { throw 'Assembly destination already exists.' }
    Copy-Item -LiteralPath $StageBundle -Destination $Destination -Recurse
    $xml = New-Object System.Xml.XmlDocument
    $xml.PreserveWhitespace = $true
    $xml.Load((Join-Path $StageBundle 'PackageContents.xml'))
    $oldXml = $null
    $oldPath = Join-Path $PreviousBundle 'PackageContents.xml'
    if (Test-Path -LiteralPath $oldPath) {
        $oldXml = New-Object System.Xml.XmlDocument
        $oldXml.PreserveWhitespace = $true
        $oldXml.Load($oldPath)
    }
    $newXml = New-Object System.Xml.XmlDocument
    $newXml.PreserveWhitespace = $true
    $newXml.Load((Join-Path $StageBundle 'PackageContents.xml'))
    $newEntries = @($newXml.SelectNodes('//ComponentEntry'))
    foreach ($entry in @($xml.SelectNodes('//ComponentEntry'))) { [void]$entry.ParentNode.RemoveChild($entry) }
    $preserved = @()
    foreach ($year in $SupportedYears) {
        $target = Join-Path $Destination "Contents\$year"
        if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
        $sourceBundle = if ($InstallYears -contains $year) { $StageBundle } else { $PreviousBundle }
        $source = Join-Path $sourceBundle "Contents\$year"
        if (-not (Test-Path -LiteralPath $source)) {
            if ($InstallYears -contains $year) { throw "Missing staged content for Civil 3D $year." }
            continue
        }
        $entries = if ($InstallYears -contains $year) { $newEntries } elseif ($oldXml) { @($oldXml.SelectNodes('//ComponentEntry')) } else { @() }
        $entryMatches = @($entries | Where-Object { $_.GetAttribute('ModuleName') -match "(?i)(?:^|[\\/])Contents[\\/]$year[\\/]" })
        if ($entryMatches.Count -ne 1) { throw "Expected exactly one ComponentEntry for Civil 3D $year in $sourceBundle." }
        New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
        Copy-Item -LiteralPath $source -Destination $target -Recurse
        $container = $xml.ImportNode($entryMatches[0].ParentNode, $false)
        [void]$container.AppendChild($xml.ImportNode($entryMatches[0], $true))
        [void]$xml.DocumentElement.AppendChild($container)
        if ($InstallYears -notcontains $year) { $preserved += $year }
    }
    $xml.Save((Join-Path $Destination 'PackageContents.xml'))
    $records = @()
    $newRecordsPath = Join-Path $StageBundle 'host-builds.json'
    if (Test-Path -LiteralPath $newRecordsPath) {
        foreach ($record in (Read-HorizunHostBuilds -Path $newRecordsPath)) {
            if ($InstallYears -contains [int]$record.year) { $records += $record }
        }
    }
    $oldRecordsPath = Join-Path $PreviousBundle 'host-builds.json'
    if (Test-Path -LiteralPath $oldRecordsPath) {
        foreach ($record in (Read-HorizunHostBuilds -Path $oldRecordsPath)) {
            if ($preserved -contains [int]$record.year) { $records += $record }
        }
    }
    [IO.File]::WriteAllText((Join-Path $Destination 'host-builds.json'), (ConvertTo-Json -InputObject @($records) -Depth 20), (New-Object Text.UTF8Encoding($false)))
    return [pscustomobject]@{ PreservedYears = @($preserved); SkippedYears = @($SupportedYears | Where-Object { $InstallYears -notcontains $_ }) }
}
