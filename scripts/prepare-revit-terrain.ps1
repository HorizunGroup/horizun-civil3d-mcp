#Requires -Version 5.1
<# Validate/extract an export_revit ZIP into a NEW folder and emit a Revit
   rehearsal request. Never calls either host or changes shared coordinates. #>
[CmdletBinding(DefaultParameterSetName='Toposolid')]
param(
    [Parameter(Mandatory = $true)][string]$Package,
    [Parameter(Mandatory = $true)][string]$Destination,
    [Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string]$TargetDocument,
    [Parameter(Mandatory = $true,ParameterSetName='Toposolid')][ValidateRange(1, [long]::MaxValue)][long]$TypeId,
    [Parameter(Mandatory = $true,ParameterSetName='Toposolid')][ValidateRange(1, [long]::MaxValue)][long]$LevelId,
    [Parameter(Mandatory = $true,ParameterSetName='Mesh')][switch]$ExactMesh,
    [Parameter(ParameterSetName='Mesh')][ValidateRange(0.000001, 1)][double]$MeshToleranceMm = 0.01,
    [switch]$AllowRetriangulation
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Read-EntryBytes($Entry, [long]$Maximum) {
    if ($Entry.Length -gt $Maximum) { throw "Oversized ZIP entry: $($Entry.FullName)" }
    $inputStream = $Entry.Open()
    $buffered = New-Object System.IO.MemoryStream
    try {
        $buffer = New-Object byte[] 8192
        while (($read = $inputStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
            if ($buffered.Length + $read -gt $Maximum) { throw 'ZIP entry exceeds its byte guard.' }
            $buffered.Write($buffer, 0, $read)
        }
        return ,$buffered.ToArray()
    } finally { $inputStream.Dispose(); $buffered.Dispose() }
}
function Hash-Bytes([byte[]]$Bytes) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($algorithm.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $algorithm.Dispose() }
}
if (-not [IO.Path]::IsPathRooted($Package) -or -not [IO.Path]::IsPathRooted($Destination)) {
    throw 'Package and Destination must be absolute paths.'
}
$packagePath = (Resolve-Path -LiteralPath $Package).Path
$destinationPath = [IO.Path]::GetFullPath($Destination).TrimEnd([IO.Path]::DirectorySeparatorChar)
if (Test-Path -LiteralPath $destinationPath) { throw 'Destination already exists; nothing is overwritten.' }
$parentPath = [IO.Path]::GetDirectoryName($destinationPath)
if (-not [IO.Directory]::Exists($parentPath)) { throw 'The destination parent folder must exist.' }
if ((Get-Item -LiteralPath $packagePath).Length -gt 130MB) { throw 'Package exceeds the compressed byte guard.' }
$archive = [IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    $xmlEntries = @($archive.Entries | Where-Object { $_.FullName -ceq 'terrain.xml' })
    $manifestEntries = @($archive.Entries | Where-Object { $_.FullName -ceq 'manifest.json' })
    $meshEntries = @($archive.Entries | Where-Object { $_.FullName -ceq 'terrain.obj' })
    if ($archive.Entries.Count -ne (2 + $meshEntries.Count) -or $meshEntries.Count -gt 1 -or $xmlEntries.Count -ne 1 -or $manifestEntries.Count -ne 1) {
        throw 'Expected exactly terrain.xml, manifest.json and optional terrain.obj; duplicate/extra/path entries are refused.'
    }
    $xmlBytes = Read-EntryBytes $xmlEntries[0] 64MB
    $manifestBytes = Read-EntryBytes $manifestEntries[0] 128KB
    $meshBytes = $null
    if ($meshEntries.Count -eq 1) { $meshBytes = Read-EntryBytes $meshEntries[0] 64MB }
    $packageHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
} finally { $archive.Dispose() }
$utf8 = New-Object Text.UTF8Encoding($false, $true)
$manifest = $utf8.GetString($manifestBytes) | ConvertFrom-Json
if ($manifest.format -cne 'horizun.civil3d.revit-terrain/v1' -or $manifest.landxml_entry -cne 'terrain.xml') {
    throw 'Unsupported terrain manifest.'
}
$xmlHash = Hash-Bytes $xmlBytes
if ($manifest.landxml_sha256 -cne $xmlHash) { throw 'LandXML hash differs from the manifest.' }
if ($null -ne $manifest.exact_mesh) {
    if ($null -eq $meshBytes -or $manifest.exact_mesh.entry -cne 'terrain.obj' -or
        $manifest.exact_mesh.linear_unit -cne 'meter' -or
        $manifest.exact_mesh.sha256 -cne (Hash-Bytes $meshBytes)) { throw 'Exact mesh asset is missing or differs from the manifest.' }
} elseif ($null -ne $meshBytes) { throw 'OBJ asset has no manifest identity.' }
if ($manifest.convex_coverage -isnot [bool]) { throw 'Manifest lacks the footprint review flag.' }
if (-not $ExactMesh -and -not $manifest.convex_coverage -and -not $AllowRetriangulation) {
    throw 'The TIN has incomplete convex coverage (concavity/holes/islands). The point-only Revit importer can fill it differently. Review the terrain before explicitly using -AllowRetriangulation.'
}
# DTD/entity expansion is prohibited; file names and model text remain data.
$readerSettings = New-Object Xml.XmlReaderSettings
$readerSettings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
$readerSettings.XmlResolver = $null
$xmlStream = New-Object IO.MemoryStream(,$xmlBytes)
$reader = [Xml.XmlReader]::Create($xmlStream, $readerSettings)
$xmlDocument = New-Object Xml.XmlDocument
$xmlDocument.XmlResolver = $null
try { $xmlDocument.Load($reader) } finally { $reader.Dispose(); $xmlStream.Dispose() }
$namespaces = New-Object Xml.XmlNamespaceManager($xmlDocument.NameTable)
$namespaces.AddNamespace('lx', 'http://www.landxml.org/schema/LandXML-1.2')
$surfaces = $xmlDocument.SelectNodes('/lx:LandXML/lx:Surfaces/lx:Surface', $namespaces)
if ($surfaces.Count -ne 1 -or $surfaces[0].GetAttribute('name') -cne $manifest.surface) {
    throw 'The package does not contain the one manifest surface.'
}
$points = $surfaces[0].SelectNodes('lx:Definition/lx:Pnts/lx:P', $namespaces)
$faces = $surfaces[0].SelectNodes('lx:Definition/lx:Faces/lx:F', $namespaces)
$unit = $xmlDocument.SelectSingleNode('/lx:LandXML/lx:Units/*', $namespaces).GetAttribute('linearUnit')
if ($points.Count -lt 3 -or $points.Count -gt 20000 -or $points.Count -ne $manifest.vertices -or
    $faces.Count -lt 1 -or $faces.Count -gt 40000 -or $faces.Count -ne $manifest.visible_faces -or
    $unit -cne $manifest.linear_unit -or $unit -cnotin @('meter', 'foot', 'USSurveyFoot')) {
    throw 'LandXML counts/units do not meet the receiver contract or manifest.'
}
$request = [ordered]@{
    tool = 'horizun_create_elements'
    arguments = [ordered]@{
        target_document = $TargetDocument; dry_run = $true; units = 'm'
        validation_mode = 'revit_rollback'; response_mode = 'full'
        elements = @([ordered]@{ kind = 'toposolid'; landxml_path = [IO.Path]::Combine($destinationPath, 'terrain.xml'); type_id = $TypeId; level_id = $LevelId })
    }
}
$handoff = [ordered]@{
    status = 'prepared_for_revit_rehearsal'; revit_written = $false
    package_sha256 = $packageHash
    landxml_sha256 = $xmlHash; manifest = $manifest; request = $request
    placement_verified = $false; triangulation_preserved_by_toposolid = $false
    exact_mesh_asset = $manifest.exact_mesh
    next = 'Read Revit health and inspect the rollback rehearsal, model/type/level, shared-coordinate controls and resulting top surface. Apply only the identical approved request with its returned token and a new idempotency key; rerun a spatial comparison afterwards.'
}
if ($ExactMesh) {
    if ($null -eq $meshBytes) { throw 'ExactMesh requires a package containing terrain.obj.' }
    $receiverPath = Join-Path $PSScriptRoot 'import-revit-terrain-mesh.py'
    if (-not (Test-Path -LiteralPath $receiverPath -PathType Leaf)) { throw 'Revit mesh receiver script is missing.' }
    $request = [ordered]@{
        tool = 'horizun_execute_python'
        arguments = [ordered]@{
            target_document = $TargetDocument; code_path = [IO.Path]::GetFullPath($receiverPath)
            purpose = 'Rehearse placement of a Civil terrain mesh and roll back after geometric verification.'
            arguments = [ordered]@{ package = $packagePath; expected_package_sha256 = $packageHash; target_document = $TargetDocument; dry_run = $true; position_tolerance_mm = $MeshToleranceMm }
        }
    }
    $handoff.request = $request
    $handoff.status = 'prepared_for_revit_mesh_rehearsal'
    $handoff.next = 'Read Revit health, honor typed-first fallback controls and the authorized Python channel. Supply a new idempotency key for execution. Rehearse first; inspect geometry and survey controls. Apply only with dry_run=false, confirmed_plan_hash and a new idempotency key. Python verification remains self-reported.'
}
$stagePath = [IO.Path]::Combine($parentPath, '.horizun-terrain-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($stagePath) | Out-Null
try {
    [IO.File]::WriteAllBytes([IO.Path]::Combine($stagePath, 'terrain.xml'), $xmlBytes)
    [IO.File]::WriteAllBytes([IO.Path]::Combine($stagePath, 'manifest.json'), $manifestBytes)
    if ($null -ne $meshBytes) {
        [IO.File]::WriteAllBytes([IO.Path]::Combine($stagePath, 'terrain.obj'), $meshBytes)
        if ((Get-FileHash -LiteralPath ([IO.Path]::Combine($stagePath, 'terrain.obj')) -Algorithm SHA256).Hash.ToLowerInvariant() -cne $manifest.exact_mesh.sha256) {
            throw 'Extracted exact mesh bytes failed verification.'
        }
    }
    [IO.File]::WriteAllText([IO.Path]::Combine($stagePath, 'revit-request.json'), ($request | ConvertTo-Json -Depth 20), $utf8)
    if ((Get-FileHash -LiteralPath ([IO.Path]::Combine($stagePath, 'terrain.xml')) -Algorithm SHA256).Hash.ToLowerInvariant() -cne $xmlHash) {
        throw 'Extracted terrain bytes failed verification.'
    }
    [IO.Directory]::Move($stagePath, $destinationPath)
} finally {
    # Only our exact generated staging directory, beneath the explicitly named parent.
    if ([IO.Directory]::Exists($stagePath) -and [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($stagePath)) -ceq $parentPath) {
        [IO.Directory]::Delete($stagePath, $true)
    }
}
$handoff | ConvertTo-Json -Depth 25
