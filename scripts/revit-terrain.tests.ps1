#Requires -Version 5.1
[CmdletBinding()]
param([string]$RevitReaderSource, [string]$CoreAssembly)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('hz-revit-terrain-tests-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
$prepare = Join-Path $PSScriptRoot 'prepare-revit-terrain.ps1'
$utf8 = New-Object Text.UTF8Encoding($false)
$checks = 0
function Check([bool]$Passed, [string]$Label) {
    if (-not $Passed) { throw "FAILED: $Label" }
    $script:checks++
}
function Make-Package([string]$Name, [string]$Unit = 'meter', [bool]$Convex = $true, [bool]$BadHash = $false, [string]$Extra = '') {
    $unitElement = if ($Unit -eq 'meter') { 'Metric' } else { 'Imperial' }
    $xml = '<LandXML xmlns="http://www.landxml.org/schema/LandXML-1.2" version="1.2"><Units><' + $unitElement + ' linearUnit="' + $Unit + '" /></Units><Surfaces><Surface name="EG"><Definition surfType="TIN"><Pnts><P id="1">200 100 5</P><P id="2">200 110 5</P><P id="3">210 110 5</P><P id="4">210 100 5</P></Pnts><Faces><F>1 2 3</F><F>1 3 4</F></Faces></Definition></Surface></Surfaces></LandXML>'
    $bytes = $utf8.GetBytes($xml)
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { $hash = ([BitConverter]::ToString($hasher.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant() } finally { $hasher.Dispose() }
    if ($BadHash) { $hash = 'bad' }
    $manifest = @{ format = 'horizun.civil3d.revit-terrain/v1'; landxml_entry = 'terrain.xml'; landxml_sha256 = $hash
        surface = 'EG'; convex_coverage = $Convex; vertices = 4; visible_faces = 2; linear_unit = $Unit }
    $path = Join-Path $tempRoot ($Name + '.zip')
    $zip = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($pair in @(@('terrain.xml', $xml), @('manifest.json', ($manifest | ConvertTo-Json)))) {
            $entry = $zip.CreateEntry($pair[0]); $data = $entry.Open()
            try { $data.Write($utf8.GetBytes($pair[1]), 0, $utf8.GetByteCount($pair[1])) } finally { $data.Dispose() }
        }
        if ($Extra) { $zip.CreateEntry($Extra) | Out-Null }
    } finally { $zip.Dispose() }
    return $path
}
function Refuses([string]$Name, [string]$PackagePath, [string]$Expected, [string]$DestinationPath = '') {
    if (-not $DestinationPath) { $DestinationPath = Join-Path $tempRoot $Name }
    try { & $prepare -Package $PackagePath -Destination $DestinationPath -TargetDocument 'fixture.rvt' -TypeId 11 -LevelId 22 | Out-Null }
    catch { Check ($_.Exception.Message -like ('*' + $Expected + '*')) $Name; return }
    throw "Expected refusal: $Name"
}
try {
    foreach ($unit in @('meter', 'foot', 'USSurveyFoot')) {
        $packagePath = Make-Package $unit $unit
        $destinationPath = Join-Path $tempRoot ('ready-' + $unit)
        $reply = & $prepare -Package $packagePath -Destination $destinationPath -TargetDocument 'fixture.rvt' -TypeId 11 -LevelId 22 | ConvertFrom-Json
        Check ($reply.request.arguments.dry_run -eq $true -and $reply.revit_written -eq $false) "$unit rehearsal only"
        Check ($reply.request.arguments.elements[0].kind -eq 'toposolid' -and $reply.request.arguments.elements[0].type_id -eq 11) "$unit typed receiver"
        Check (Test-Path -LiteralPath $reply.request.arguments.elements[0].landxml_path) "$unit XML extracted"
        Check ($reply.placement_verified -eq $false -and $reply.triangulation_preserved_by_toposolid -eq $false) "$unit honest placement/connectivity"
        Refuses ('exists-' + $unit) $packagePath 'already exists' $destinationPath
        if ($RevitReaderSource) {
            if (-not ('Horizun.Revit.Core.LandXmlTinRules' -as [type])) { Add-Type -Path $RevitReaderSource }
            $inputXml = [IO.File]::OpenRead($reply.request.arguments.elements[0].landxml_path)
            try { $tin = $null; $bad = [Horizun.Revit.Core.LandXmlTinRules]::Read($inputXml, 'EG', [ref]$tin) }
            finally { $inputXml.Dispose() }
            Check ($null -eq $bad -and $tin.PointsMetres.Count -eq 4) "$unit actual local Revit reader"
            $factor = switch ($unit) { 'meter' { 1.0 } 'foot' { 0.3048 } 'USSurveyFoot' { 1200.0 / 3937.0 } }
            Check ([Math]::Abs($tin.PointsMetres[0][0] - 100 * $factor) -lt 1e-10 -and [Math]::Abs($tin.PointsMetres[0][1] - 200 * $factor) -lt 1e-10) "$unit actual reader axes/units"
        }
    }
    Refuses 'hash' (Make-Package 'hash' 'meter' $true $true) 'hash differs'
    Refuses 'concave' (Make-Package 'concave' 'meter' $false) 'incomplete convex coverage'
    Refuses 'zip-path' (Make-Package 'zip-path' 'meter' $true $false '../outside.xml') 'Expected exactly'
    Refuses 'duplicate' (Make-Package 'duplicate' 'meter' $true $false 'terrain.xml') 'Expected exactly'
    Check (-not (Test-Path -LiteralPath (Join-Path $tempRoot 'hash'))) 'refusal leaves no destination'
    Check (-not (Test-Path -LiteralPath (Join-Path $tempRoot 'concave'))) 'concavity refusal leaves no destination'
    if ($CoreAssembly) {
        [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $CoreAssembly).Path) | Out-Null
        $vertices = New-Object 'System.Collections.Generic.List[System.ValueTuple[double,double,double]]'
        foreach ($p in @(@(100.0, 200.0, 5.0), @(110.0, 200.0, 5.0), @(110.0, 210.0, 5.0), @(100.0, 210.0, 5.0))) {
            $vertices.Add([System.ValueTuple[double,double,double]]::new($p[0], $p[1], $p[2]))
        }
        $triangles = New-Object 'System.Collections.Generic.List[System.ValueTuple[int,int,int]]'
        $triangles.Add([System.ValueTuple[int,int,int]]::new(0, 1, 2))
        $triangles.Add([System.ValueTuple[int,int,int]]::new(0, 2, 3))
        $surface = [Horizun.Civil3D.Core.LxSurface]::new('EG', 'Generated fixture', $vertices, $triangles)
        $source = [System.Text.Json.Nodes.JsonNode]::Parse('{"drawing":"fixture.dwg"}')
        $coordinates = [System.Text.Json.Nodes.JsonNode]::Parse('{"code":null}')
        $prepared = [Horizun.Civil3D.Core.RevitTerrainPackage]::Prepare($surface, 'meter', $source, $coordinates, 'test')
        $corePackage = Join-Path $tempRoot 'actual-core.zip'
        [Horizun.Civil3D.Core.RevitTerrainPackage]::Write($corePackage, $prepared)
        $coreReply = & $prepare -Package $corePackage -Destination (Join-Path $tempRoot 'actual-core') -TargetDocument 'fixture.rvt' -TypeId 11 -LevelId 22 | ConvertFrom-Json
        Check ($coreReply.manifest.vertices -eq 4 -and $coreReply.manifest.plan_area_m2 -eq 100) 'actual Core package -> preparer'
        Check (Test-Path -LiteralPath (Join-Path $tempRoot 'actual-core/terrain.obj')) 'actual Core exact mesh extracted'
        Check ($coreReply.exact_mesh_asset.connectivity_preserved -eq $true -and $coreReply.exact_mesh_asset.placement_verified -eq $false) 'exact mesh preserves connectivity without claiming placement'
        $meshReply = & $prepare -Package $corePackage -Destination (Join-Path $tempRoot 'actual-core-mesh') -TargetDocument 'fixture.rvt' -ExactMesh | ConvertFrom-Json
        Check ($meshReply.request.tool -eq 'horizun_execute_python') 'exact mesh prepares the real Python receiver'
        Check ($meshReply.request.arguments.arguments.dry_run -eq $true -and $meshReply.revit_written -eq $false) 'mesh rehearses by default and does not execute Revit'
        Check ($meshReply.request.arguments.arguments.position_tolerance_mm -eq 0.01) 'mesh request declares the storage precision tolerance'
        $preciseMeshReply = & $prepare -Package $corePackage -Destination (Join-Path $tempRoot 'actual-core-mesh-precision') -TargetDocument 'fixture.rvt' -ExactMesh -MeshToleranceMm 0.005 | ConvertFrom-Json
        Check ($preciseMeshReply.request.arguments.arguments.position_tolerance_mm -eq 0.005) 'explicit mesh tolerance survives request preparation'
        Check (Test-Path -LiteralPath $meshReply.request.arguments.code_path) 'mesh receiver script exists'
        $coreZip = [IO.Compression.ZipFile]::Open($corePackage, [IO.Compression.ZipArchiveMode]::Update)
        try {
            $coreZip.GetEntry('terrain.obj').Delete()
            $badMesh = $coreZip.CreateEntry('terrain.obj').Open()
            try { $badMesh.WriteByte(0) } finally { $badMesh.Dispose() }
        } finally { $coreZip.Dispose() }
        Refuses 'mesh-tampered' $corePackage 'Exact mesh asset'
        if ($RevitReaderSource) {
            $coreInput = [IO.File]::OpenRead($coreReply.request.arguments.elements[0].landxml_path)
            try { $tin = $null; $bad = [Horizun.Revit.Core.LandXmlTinRules]::Read($coreInput, 'EG', [ref]$tin) }
            finally { $coreInput.Dispose() }
            Check ($null -eq $bad -and $tin.PointsMetres.Count -eq 4 -and $tin.PointsMetres[0][2] -eq 5) 'actual Core -> preparer -> actual Revit reader'
        }
    }
    "PASS: $checks terrain handoff checks; no host operations."
} finally {
    $resolvedTempRoot = [IO.Path]::GetFullPath($tempRoot)
    $resolvedTempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if ([IO.Path]::GetDirectoryName($resolvedTempRoot) -ceq $resolvedTempParent -and
        [IO.Path]::GetFileName($resolvedTempRoot).StartsWith('hz-revit-terrain-tests-')) {
        [IO.Directory]::Delete($resolvedTempRoot, $true)
    }
}
