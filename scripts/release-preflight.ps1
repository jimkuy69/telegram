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
$tdManifest = Join-Path $root "tdlib\tdjson.manifest.json"
$tdHashPass = $false
$tdHashEvidence = "tdjson.dll or manifest missing: $tdjson / $tdManifest"
if ((Test-Path -LiteralPath $tdjson) -and (Test-Path -LiteralPath $tdManifest)) {
    $expectedHash = (Get-Content -Raw -LiteralPath $tdManifest | ConvertFrom-Json).sha256.ToLowerInvariant()
    $actualHash = (Get-FileHash -LiteralPath $tdjson -Algorithm SHA256).Hash.ToLowerInvariant()
    $tdHashPass = $expectedHash -eq $actualHash
    $tdHashEvidence = "Expected=$expectedHash Actual=$actualHash"
}
Add-Check "TDLib native binary hash gate" $tdHashPass $tdHashEvidence "BLOCKED"

$runtimeScript = Join-Path $root "scripts\verify-tdlib-runtime.ps1"
if (Test-Path -LiteralPath $runtimeScript) {
    $runtimeOutput = (& pwsh -NoProfile -ExecutionPolicy Bypass -File $runtimeScript 2>&1 | Out-String).Trim()
    $runtimePass = $LASTEXITCODE -eq 0
    $runtimeEvidence = if ($runtimePass) { "TDLib no-login runtime POC passed." } else { ($runtimeOutput -split "`r?`n" | Select-Object -Last 1) }
    Add-Check "TDLib native runtime gate" $runtimePass $runtimeEvidence "BLOCKED"
} else {
    Add-Check "TDLib native runtime gate" $false $runtimeScript "BLOCKED"
}

$iscc = Get-Command ISCC.exe -ErrorAction SilentlyContinue
if (-not $iscc -and (Test-Path -LiteralPath "C:\InnoSetup7\ISCC.exe")) {
    $iscc = Get-Item -LiteralPath "C:\InnoSetup7\ISCC.exe"
}
Add-Check "Inno Setup compiler" ($null -ne $iscc) ($(if ($iscc) { $iscc.Source } else { "ISCC.exe not found" })) "BLOCKED"

$checks | Format-Table -AutoSize
if ($checks.Result -contains "FAIL") { exit 1 }
if ($checks.Result -contains "BLOCKED" -or $checks.Result -contains "NOT VERIFIED") { exit 2 }
exit 0
