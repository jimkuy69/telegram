$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$checks = [System.Collections.Generic.List[object]]::new()

function Add-Check([string]$Name, [bool]$Pass, [string]$Evidence, [string]$State = $null) {
    $checks.Add([pscustomobject]@{ Check = $Name; Result = if ($Pass) { "PASS" } elseif ($State) { $State } else { "FAIL" }; Evidence = $Evidence })
}

$project = Join-Path $root "src\TelegramControlStudio.App\TelegramControlStudio.App.csproj"
$installer = Join-Path $root "installer\TelegramControlStudio.iss"
$notices = Join-Path $root "THIRD-PARTY-NOTICES.md"
$publish = Join-Path $root "artifacts\publish\win-x64\TelegramControlStudio.exe"
$tdjson = Join-Path $root "tdlib\tdjson.dll"

Add-Check "WPF project" (Test-Path -LiteralPath $project) $project
Add-Check "Installer script" (Test-Path -LiteralPath $installer) $installer
Add-Check "Third-party notices" (Test-Path -LiteralPath $notices) $notices
Add-Check "Published executable" (Test-Path -LiteralPath $publish) $publish "NOT VERIFIED"
Add-Check "TDLib native binary hash gate" $false "tdjson.dll is intentionally absent or unverified: $tdjson" "BLOCKED"

$iscc = Get-Command ISCC.exe -ErrorAction SilentlyContinue
Add-Check "Inno Setup compiler" ($null -ne $iscc) ($(if ($iscc) { $iscc.Source } else { "ISCC.exe not found" })) "BLOCKED"

$checks | Format-Table -AutoSize
if ($checks.Result -contains "FAIL") { exit 1 }
if ($checks.Result -contains "BLOCKED" -or $checks.Result -contains "NOT VERIFIED") { exit 2 }
exit 0