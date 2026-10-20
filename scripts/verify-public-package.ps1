#Requires -Version 5.1
<# Read-only publication gate: no Autodesk/customer payloads or private Windows build paths. #>
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string[]]$Package)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach($path in $Package){
    $zip=[IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $path).Path)
    try {
        $checked=0
        foreach($entry in $zip.Entries){
            if($entry.Name -match '(?i)\.(dwg|rvt|pbix|nwd|pdb)$' -or
               $entry.Name -match '(?i)^(AcDbMgd|AcCoreMgd|AcMgd|Aecc.*Mgd|AecBaseMgd|AdWindows)\.dll$'){
                throw "Forbidden customer/Autodesk payload: $($entry.FullName)"
            }
            if($entry.Name -notmatch '(?i)\.(dll|exe|pdb|json|ps1|md|txt)$'){continue}
            if($entry.Length -gt 128MB){throw "Publication scan size bound exceeded: $($entry.FullName)"}
            $stream=$entry.Open();$memory=New-Object IO.MemoryStream
            try{$stream.CopyTo($memory);$bytes=$memory.ToArray()}
            finally{$stream.Dispose();$memory.Dispose()}
            foreach($encoding in @([Text.Encoding]::UTF8,[Text.Encoding]::Unicode)){
                $text=$encoding.GetString($bytes)
                if($text -match '(?i)[A-Z]:[\\/]+Users[\\/]+(?!Public[\\/])[^\\/\x00]+[\\/]'){
                    throw "Private build/profile path in $($entry.FullName)"
                }
            }
            $checked++
        }
        Write-Output "PASS: $path ($checked inspected payloads; no private build paths or forbidden data/binaries)"
    } finally {$zip.Dispose()}
}
