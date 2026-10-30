#Requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string] $Package, [string] $Civil2025Root = 'C:\Program Files\Autodesk\AutoCAD 2025')
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -ne 5 -or $PSVersionTable.PSVersion.Minor -ne 1) { Write-Host 'ERROR: Run this test with Windows PowerShell 5.1 (powershell.exe).'; exit 1 }
function Assert([bool] $condition, [string] $message) { if (-not $condition) { throw $message } }
function Quote([string] $value) { return "'" + $value.Replace("'", "''") + "'" }
if (@(Get-Process -Name acad -ErrorAction SilentlyContinue).Count -gt 0) { Write-Host 'ERROR: acad.exe is running; close it after saving before running this test.'; exit 1 }
if (-not (Test-Path -LiteralPath (Join-Path $Civil2025Root 'C3D\AeccDbMgd.dll'))) { Write-Host 'SKIP: Civil 3D 2025 is not installed at the specified root.'; exit 0 }
$base = Join-Path $env:TEMP ('horizun-package-e2e-' + [guid]::NewGuid().ToString('N'))
try {
    $zip = (Resolve-Path -LiteralPath $Package).Path
    $extract = Join-Path $base 'extract'
    New-Item -ItemType Directory -Path $extract -Force | Out-Null
    Expand-Archive -LiteralPath $zip -DestinationPath $extract
    $git = Get-Command git.exe -ErrorAction SilentlyContinue
    if (-not $git) { throw 'git is required by this test to prove the extraction is outside a repository.' }
    $gitStart = New-Object Diagnostics.ProcessStartInfo
    $gitStart.FileName = $git.Source
    $gitStart.Arguments = '-C "' + $extract + '" rev-parse --is-inside-work-tree'
    $gitStart.UseShellExecute = $false; $gitStart.RedirectStandardOutput = $true; $gitStart.RedirectStandardError = $true; $gitStart.CreateNoWindow = $true
    $gitProcess = [Diagnostics.Process]::Start($gitStart)
    $gitOutput = $gitProcess.StandardOutput.ReadToEnd(); $gitError = $gitProcess.StandardError.ReadToEnd(); $gitProcess.WaitForExit()
    Assert ($gitProcess.ExitCode -ne 0) "Extraction is inside a git repository: $gitOutput $gitError"
    $info = Get-Content -LiteralPath (Join-Path $extract 'build-info.json') -Raw | ConvertFrom-Json
    $pkgBundle = Join-Path $extract 'Horizun.Civil3D.bundle'
    $pkgServer = Join-Path $extract 'server\horizun-civil3d-mcp.exe'
    $expectedVersion = (& $pkgServer --version | Out-String).Trim()
    Assert ($LASTEXITCODE -eq 0 -and $expectedVersion -match [regex]::Escape([string]$info.version)) 'Package server version does not match build-info.'
    foreach ($case in @('A','B')) {
        $sandbox = Join-Path $base "case-$case"
        $app = Join-Path $sandbox 'appdata'; $local = Join-Path $sandbox 'local'; $temp = Join-Path $sandbox 'temp'; $profile = Join-Path $sandbox 'profile'
        foreach ($dir in @($app,$local,$temp,$profile)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
        $bundle = Join-Path $app 'Autodesk\ApplicationPlugins\Horizun.Civil3D.bundle'
        $server = Join-Path $local 'Programs\Horizun\Civil3D-MCP\server\horizun-civil3d-mcp.exe'
        $argsText = '-Years 2025 -AcadRoots @{2025=' + (Quote $Civil2025Root) + '}'
        if ($case -eq 'B') {
            $old = Join-Path $bundle 'Contents\2027\Horizun.Civil3D.dll'
            New-Item -ItemType Directory -Path (Split-Path $old) -Force | Out-Null
            [IO.File]::WriteAllText($old, 'previous 2027 add-in')
            $oldHash = (Get-FileHash -LiteralPath $old -Algorithm SHA256).Hash
            [IO.File]::WriteAllText((Join-Path $bundle 'PackageContents.xml'), '<ApplicationPackage AppVersion="old"><Components Description="Civil 3D 2027"><ComponentEntry AppName="HorizunCivil3DMcp" Version="old" ModuleName="./Contents/2027/Horizun.Civil3D.dll"><RuntimeRequirements OS="Win64" Platform="AutoCAD*" SeriesMin="R26.0" SeriesMax="R26.0" /></ComponentEntry></Components></ApplicationPackage>')
            [IO.File]::WriteAllText((Join-Path $bundle 'host-builds.json'), '[{"year":2027,"runtime":"net8","autocad_assembly_version":"old","civil_assembly_version":"old"}]')
            $fake = Join-Path $sandbox 'fake2027'
            New-Item -ItemType Directory -Path (Join-Path $fake 'C3D') -Force | Out-Null
            [IO.File]::WriteAllText((Join-Path $fake 'C3D\AeccDbMgd.dll'), 'fake')
            $argsText = '-AcadRoots @{2025=' + (Quote $Civil2025Root) + ';2027=' + (Quote $fake) + '}'
        }
        $command = '$env:APPDATA=' + (Quote $app) + ';$env:LOCALAPPDATA=' + (Quote $local) + ';$env:TEMP=' + (Quote $temp) + ';$env:TMP=' + (Quote $temp) + ';$env:USERPROFILE=' + (Quote $profile) + ";`$env:PATH='C:\Windows\System32;C:\Windows;C:\Windows\System32\WindowsPowerShell\v1.0'; & " + (Quote (Join-Path $extract 'install.ps1')) + ' ' + $argsText + '; exit $LASTEXITCODE'
        $out = Join-Path $sandbox 'stdout.txt'; $err = Join-Path $sandbox 'stderr.txt'
        $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
        $launcher = "&([ScriptBlock]::Create([Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('$encoded'))))"
        $p = Start-Process -FilePath "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-Command', $launcher) -Wait -PassThru -NoNewWindow -RedirectStandardOutput $out -RedirectStandardError $err
        $output = (Get-Content -LiteralPath $out -Raw) + "`n" + (Get-Content -LiteralPath $err -Raw)
        $expectedExit = if ($case -eq 'A') { 0 } else { 3 }
        Assert ($p.ExitCode -eq $expectedExit) "Case $case exit $($p.ExitCode), expected $expectedExit. Output: $output"
        if ($case -eq 'A') { Assert ($output -notmatch '(?m)^ERROR:') "Case A emitted ERROR: $output" }
        else {
            Assert ($output -match '(?m)^ERROR: Partial install') "Case B lacks partial install error: $output"
            Assert ((Get-FileHash -LiteralPath $old -Algorithm SHA256).Hash -eq $oldHash) 'Case B changed the 2027 add-in.'
            $xml = [xml](Get-Content -LiteralPath (Join-Path $bundle 'PackageContents.xml') -Raw)
            Assert (@($xml.SelectNodes('//ComponentEntry') | Where-Object { $_.GetAttribute('ModuleName') -eq './Contents/2027/Horizun.Civil3D.dll' }).Count -eq 1) 'Case B lost the 2027 ComponentEntry.'
        }
        Assert ((Get-FileHash -LiteralPath (Join-Path $bundle 'Contents\2025\Horizun.Civil3D.dll') -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath (Join-Path $pkgBundle 'Contents\2025\Horizun.Civil3D.dll') -Algorithm SHA256).Hash) "Case $case 2025 hash mismatch."
        Assert ((Get-FileHash -LiteralPath $server -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath $pkgServer -Algorithm SHA256).Hash) "Case $case server hash mismatch."
        $installedVersion = (& $server --version | Out-String).Trim()
        Assert ($LASTEXITCODE -eq 0 -and $installedVersion -eq $expectedVersion) "Case $case installed version mismatch."
        $manifest = Get-Content -LiteralPath (Join-Path $local 'Programs\Horizun\Civil3D-MCP\manifest.json') -Raw | ConvertFrom-Json
        Assert ([string]$manifest.commit -ceq [string]$info.commit) "Case $case commit mismatch."
        Write-Host "PASS: case $case"
    }
    Write-Host 'PASS: package end-to-end tests.'
} catch { Write-Host "ERROR: $($_.Exception.Message)"; exit 1 }
finally {
    $full = [IO.Path]::GetFullPath($base)
    $parent = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
    if ($full.StartsWith($parent, [StringComparison]::OrdinalIgnoreCase) -and (Split-Path $full -Leaf) -match '^horizun-package-e2e-[a-f0-9]{32}$' -and (Test-Path -LiteralPath $full)) { Remove-Item -LiteralPath $full -Recurse -Force }
}
