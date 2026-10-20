#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'install-rollback.lib.ps1')

function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

$root = Join-Path ([IO.Path]::GetTempPath()) ('horizun-c3d-rollback-test-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
try {
    $marker = Join-Path $root 'independent-undo.txt'
    $actions = New-Object System.Collections.Generic.List[scriptblock]
    $actions.Add({ [IO.File]::WriteAllText($marker, 'ran') })
    $missing = Join-Path $root 'missing-backup.json'
    $destination = Join-Path $root 'config.json'
    $actions.Add({ Copy-Item -LiteralPath $missing -Destination $destination -Force -ErrorAction Stop })
    $result = Invoke-HorizunRollback -Actions $actions
    Assert (-not $result.Restored) 'Rollback reported restored after an undo action failed.'
    Assert ($result.Attempted -eq 2 -and $result.Errors.Count -eq 1) 'Rollback did not report the failing action.'
    Assert ($result.Errors[0] -match 'undo action 1') 'Rollback error did not identify the failed action.'
    Assert ([IO.File]::ReadAllText($marker) -eq 'ran') 'Rollback stopped before attempting another undo action.'

    $clean = New-Object System.Collections.Generic.List[scriptblock]
    $clean.Add({ Remove-Item -LiteralPath $marker -Force -ErrorAction Stop })
    $ok = Invoke-HorizunRollback -Actions $clean
    Assert ($ok.Restored -and $ok.Attempted -eq 1 -and $ok.Errors.Count -eq 0) 'Successful rollback was reported incorrectly.'
    Assert (-not (Test-Path -LiteralPath $marker)) 'Successful undo action did not run.'
    Write-Host 'Installer rollback tests passed.'
}
finally {
    $full = [IO.Path]::GetFullPath($root)
    $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $full.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path $full -Leaf) -notmatch '^horizun-c3d-rollback-test-[a-f0-9]{32}$') {
        throw 'Unsafe rollback test cleanup path.'
    }
    Remove-Item -LiteralPath $full -Recurse -Force -ErrorAction SilentlyContinue
}
exit 0
