#Requires -Version 5.1

function Invoke-HorizunRollback {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][System.Collections.Generic.List[scriptblock]]$Actions)

    $errors = New-Object System.Collections.Generic.List[string]
    for ($i = $Actions.Count - 1; $i -ge 0; $i--) {
        try { & $Actions[$i] | Out-Null }
        catch { $errors.Add(("undo action {0}: {1}" -f $i, $_.Exception.Message)) }
    }
    return [pscustomobject]@{
        Restored = ($errors.Count -eq 0)
        Attempted = $Actions.Count
        Errors = @($errors.ToArray())
    }
}
