#Requires -Version 5.1
<# Read-only build selection and Autodesk metadata gates. No installation or client settings. #>
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$PublishedServerPath)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'src\Horizun.Civil3D.Plugin\Horizun.Civil3D.Plugin.csproj'
$checks = 0
function Check([bool]$Condition, [string]$Label) {
    if (-not $Condition) { throw "FAILED: $Label" }
    $script:checks++
}
foreach ($case in @(
    @{Year=2024;Runtime='net48';Framework='net48'},
    @{Year=2026;Runtime='net8';Framework='net8.0-windows'},
    @{Year=2026;Runtime='net10';Framework='net10.0-windows'},
    @{Year=2025;Runtime='net8';Framework='net8.0-windows'}
)) {
    $actual = & dotnet msbuild $project "-p:Civil3DYear=$($case.Year)" "-p:Civil3DRuntime=$($case.Runtime)" -getProperty:TargetFramework -nologo
    Check ($LASTEXITCODE -eq 0 -and "$actual".Trim() -ceq $case.Framework) "target framework for $($case.Year)/$($case.Runtime)"
}
$hostRoot = 'C:\Program Files\Autodesk\AutoCAD 2025'
if (Test-Path -LiteralPath (Join-Path $hostRoot 'C3D\AeccDbMgd.dll')) {
    $info = & $PublishedServerPath --inspect-host $hostRoot 2025
    Check ($LASTEXITCODE -eq 0) 'installed 2025 PE metadata'
    $metadata = $info | ConvertFrom-Json
    Check ($metadata.year -eq 2025 -and $metadata.runtime -in @('net8','net10')) '2025 runtime is explicit'
    $oldPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $wrong = & $PublishedServerPath --inspect-host $hostRoot 2024 2>&1; $wrongCode = $LASTEXITCODE }
    finally { $ErrorActionPreference = $oldPreference }
    Check ($wrongCode -ne 0 -and "$wrong" -match 'identity/year mismatch') '2025 DLLs refused for 2024'
    $wrongBuild = & dotnet build $project -c Release -p:Civil3DYear=2024 "-p:AcadDir=$hostRoot" --nologo -v quiet 2>&1
    Check ($LASTEXITCODE -ne 0 -and "$wrongBuild" -match 'DLL year mismatch') 'MSBuild refuses another year before API binding'
}
$global:LASTEXITCODE = 0
Write-Host "Runtime build gates: $checks checks passed. No Autodesk code was loaded or installed."
