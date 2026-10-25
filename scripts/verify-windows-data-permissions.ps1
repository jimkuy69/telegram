[CmdletBinding()]
param(
    [string]$DataRoot = (Join-Path $env:LOCALAPPDATA "TelegramControlStudio")
)

$ErrorActionPreference = "Stop"

function Write-Result([string]$State, [string]$Message) {
    Write-Host "$State: $Message"
}

if ([string]::IsNullOrWhiteSpace($DataRoot)) {
    Write-Result "BLOCKED" "LOCALAPPDATA is not available; provide -DataRoot explicitly."
    exit 2
}

if (-not (Test-Path -LiteralPath $DataRoot -PathType Container)) {
    Write-Result "NOT VERIFIED" "Data directory does not exist yet: $DataRoot"
    exit 2
}

$acl = Get-Acl -LiteralPath $DataRoot
$writeRights = [System.Security.AccessControl.FileSystemRights]::Write,
               [System.Security.AccessControl.FileSystemRights]::Modify,
               [System.Security.AccessControl.FileSystemRights]::FullControl

$broadWriteRules = @(
    $acl.Access | Where-Object {
        $_.AccessControlType -eq [System.Security.AccessControl.AccessControlType]::Allow -and
        $_.IdentityReference.Value -match '^(Everyone|BUILTIN\\Users|NT AUTHORITY\\Authenticated Users)$' -and
        ((([System.Security.AccessControl.FileSystemRights]::Write -bor [System.Security.AccessControl.FileSystemRights]::Modify -bor [System.Security.AccessControl.FileSystemRights]::FullControl) -band $_.FileSystemRights) -ne 0)
    }
)

Write-Host "DataRoot: $DataRoot"
Write-Host "Owner: $($acl.Owner)"
Write-Host "Access rules: $($acl.Access.Count)"

if ($broadWriteRules.Count -gt 0) {
    foreach ($rule in $broadWriteRules) {
        Write-Host ("Broad write rule: {0} -> {1} ({2})" -f $rule.IdentityReference.Value, $rule.FileSystemRights, $rule.AccessControlType)
    }
    Write-Result "FAIL" "A broad Windows principal can write to the application data root."
    exit 1
}

Write-Result "PASS" "No broad Allow rule grants write/modify/full-control access to the application data root."
Write-Host "Review note: this is a read-only ACL check; it does not change permissions or prove child-file ACLs."
exit 0
