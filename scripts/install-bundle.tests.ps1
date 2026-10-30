#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'install-bundle.lib.ps1')
function Assert([bool]$condition, [string]$message) { if (-not $condition) { throw $message } }
$root = Join-Path ([IO.Path]::GetTempPath()) ('horizun-c3d-bundle-test-' + [guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $manifest = Join-Path $root 'host-builds.json'
    [IO.File]::WriteAllText($manifest, '[{"year":2024,"runtime":"net48"},{"year":2025,"runtime":"net8"},{"year":2026,"runtime":"net8"}]')
    $records = @(Read-HorizunHostBuilds -Path $manifest)
    Assert ($records.Count -eq 3) 'Three host records were not unrolled.'
    foreach ($record in $records) { Assert ([int]$record.year -in @(2024,2025,2026)) 'Invalid host year.' }
    $lib = (Join-Path $PSScriptRoot 'install-bundle.lib.ps1').Replace("'", "''")
    $manifestQuoted = $manifest.Replace("'", "''")
    $count = & powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ". '$lib'; @(Read-HorizunHostBuilds -Path '$manifestQuoted').Count"
    Assert ($LASTEXITCODE -eq 0 -and ($count -join '').Trim() -eq '3') 'Windows PowerShell 5.1 did not return three host records.'
    [IO.File]::WriteAllText($manifest, '[{"year":2024,"runtime":"net48"}]')
    $records = @(Read-HorizunHostBuilds -Path $manifest)
    Assert ($records.Count -eq 1) 'Single host record was not unrolled.'
    foreach ($record in $records) { Assert ([int]$record.year -eq 2024) 'Invalid single host year.' }
    $native = Invoke-HorizunNative -FilePath 'cmd.exe' -Arguments @('/c', 'echo out & echo err 1>&2 & exit 0')
    Assert ($native.ExitCode -eq 0 -and $native.StdOut -match 'out' -and $native.StdErr -match 'err') 'Native stdout/stderr capture failed.'
    $native = Invoke-HorizunNative -FilePath 'cmd.exe' -Arguments @('/c', 'exit 5')
    Assert ($native.ExitCode -eq 5) 'Native exit code was not captured.'
    $stage = Join-Path $root 'stage'
    $previous = Join-Path $root 'previous'
    foreach ($dir in @($stage, $previous)) { New-Item -ItemType Directory -Path (Join-Path $dir 'Contents\2025') -Force | Out-Null }
    New-Item -ItemType Directory -Path (Join-Path $previous 'Contents\2027') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $stage 'Contents\2025\file'), 'new 2025')
    [IO.File]::WriteAllText((Join-Path $previous 'Contents\2025\file'), 'old 2025')
    [IO.File]::WriteAllText((Join-Path $previous 'Contents\2027\file'), 'old 2027')
    $newEntry = '<ComponentEntry ModuleName="./Contents/2025/Horizun.Civil3D.dll" Version="new" />'
    $oldEntry = '<ComponentEntry ModuleName="./Contents/2027/Horizun.Civil3D.dll" Version="old"><RuntimeRequirements SeriesMin="R26.0" /></ComponentEntry>'
    [IO.File]::WriteAllText((Join-Path $stage 'PackageContents.xml'), "<ApplicationPackage><Components>$newEntry</Components></ApplicationPackage>")
    [IO.File]::WriteAllText((Join-Path $previous 'PackageContents.xml'), "<ApplicationPackage><Components><ComponentEntry ModuleName='./Contents/2025/Horizun.Civil3D.dll' /></Components><Components>$oldEntry</Components></ApplicationPackage>")
    [IO.File]::WriteAllText((Join-Path $stage 'host-builds.json'), '[{"year":2025,"runtime":"new"}]')
    [IO.File]::WriteAllText((Join-Path $previous 'host-builds.json'), '[{"year":2027,"runtime":"old"}]')
    $out = Join-Path $root 'out1'
    $result = New-HorizunAssembledBundle $stage $previous $out @(2025) @(2025,2027)
    Assert ((Get-FileHash (Join-Path $out 'Contents\2027\file')).Hash -eq (Get-FileHash (Join-Path $previous 'Contents\2027\file')).Hash) 'Old 2027 bytes were not preserved.'
    Assert ([IO.File]::ReadAllText((Join-Path $out 'Contents\2025\file')) -eq 'new 2025') 'New 2025 missing.'
    $xml = [xml](Get-Content (Join-Path $out 'PackageContents.xml') -Raw)
    $entries = @($xml.SelectNodes('//ComponentEntry'))
    Assert ($entries.Count -eq 2 -and $entries[0].OuterXml -eq $newEntry -and $entries[1].OuterXml -eq $oldEntry) 'Entries were not preserved.'
    Assert ($result.PreservedYears -contains 2027 -and $result.SkippedYears -contains 2027) 'Preserved/skipped year not reported.'
    $records = @(foreach ($record in (Get-Content (Join-Path $out 'host-builds.json') -Raw | ConvertFrom-Json)) { $record })
    Assert ($records.Count -eq 2 -and @($records | Where-Object { $_.year -eq 2027 -and $_.runtime -eq 'old' }).Count -eq 1) 'Old host metadata missing.'
    New-Item -ItemType Directory -Path (Join-Path $stage 'Contents\2027') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $stage 'Contents\2027\file'), 'wrong runtime')
    [IO.File]::WriteAllText((Join-Path $stage 'PackageContents.xml'), "<ApplicationPackage><Components>$newEntry</Components><Components><ComponentEntry ModuleName='./Contents/2027/Horizun.Civil3D.dll' Version='wrong' /></Components></ApplicationPackage>")
    [IO.File]::WriteAllText((Join-Path $stage 'host-builds.json'), '[{"year":2025,"runtime":"new"},{"year":2027,"runtime":"wrong"}]')
    $out2 = Join-Path $root 'out2'
    $null = New-HorizunAssembledBundle $stage $previous $out2 @(2025) @(2025,2027)
    Assert ([IO.File]::ReadAllText((Join-Path $out2 'Contents\2027\file')) -eq 'old 2027') 'Skipped staged files were installed.'
    $xml2 = [xml](Get-Content (Join-Path $out2 'PackageContents.xml') -Raw)
    Assert (@($xml2.SelectNodes('//ComponentEntry') | Where-Object { $_.GetAttribute('Version') -eq 'wrong' }).Count -eq 0) 'Skipped staged entry was installed.'
    $empty = Join-Path $root 'empty'
    $out3 = Join-Path $root 'out3'
    $null = New-HorizunAssembledBundle $stage $empty $out3 @(2025) @(2025,2027)
    Assert (-not (Test-Path (Join-Path $out3 'Contents\2027'))) 'Absent 2027 was installed.'
    $xml3 = [xml](Get-Content (Join-Path $out3 'PackageContents.xml') -Raw)
    Assert (@($xml3.SelectNodes('//ComponentEntry')).Count -eq 1) 'Absent 2027 entry was installed.'
    Assert ((Get-HorizunInstallFailureMessage 'disk full' $true) -eq 'Install failed (disk full); previous installation restored.') 'Restored error lacks cause.'
    Assert ((Get-HorizunInstallFailureMessage 'disk full' $false 1 'backup') -match '^Install failed \(disk full\); rollback_incomplete:') 'Incomplete error lacks cause.'
    $stderr = Join-Path $root 'stderr.txt'
    $stdout = Join-Path $root 'stdout.txt'
    $process = Start-Process powershell -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File', ('"' + (Join-Path $PSScriptRoot 'install.ps1') + '"'), '-Years','1999') -Wait -PassThru -NoNewWindow -RedirectStandardError $stderr -RedirectStandardOutput $stdout
    Assert ($process.ExitCode -eq 1) 'Invalid year did not exit 1.'
    Assert ([bool]((Get-Content $stderr) | Where-Object { $_ -match '^ERROR: .*1999.*not supported' })) 'stderr lacks ERROR and cause.'
    Assert ([bool]((Get-Content $stdout) | Where-Object { $_ -match '^ERROR: .*1999.*not supported' })) 'stdout lacks ERROR and cause.'
    # Invalid roots throw during detection, before process checks, staging or installation.
    $installer = (Join-Path $PSScriptRoot 'install.ps1').Replace("'", "''")
    $command = "& '$installer' -AcadRoots @{2025='bad|path'}; exit `$LASTEXITCODE"
    $process = Start-Process powershell -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-Command', ('"' + $command + '"')) -Wait -PassThru -NoNewWindow -RedirectStandardError $stderr -RedirectStandardOutput $stdout
    Assert ($process.ExitCode -eq 1) 'Invalid root did not exit 1.'
    Assert ([bool]((Get-Content $stdout) | Where-Object { $_ -match '^ERROR: Unexpected installer error \(' })) 'stdout lacks unexpected error.'
    Assert ([bool]((Get-Content $stderr) | Where-Object { $_ -match '^ERROR: Unexpected installer error \(' })) 'stderr lacks unexpected error.'
    Write-Host 'Installer bundle tests passed.'
}
finally {
    $full = [IO.Path]::GetFullPath($root)
    $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $full.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path $full -Leaf) -notmatch '^horizun-c3d-bundle-test-[a-f0-9]{32}$') { throw 'Unsafe bundle test cleanup path.' }
    Remove-Item -LiteralPath $full -Recurse -Force -ErrorAction SilentlyContinue
}
exit 0
