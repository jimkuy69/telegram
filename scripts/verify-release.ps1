$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root "artifacts\publish\win-x64"
$exe = Join-Path $publish "TelegramControlStudio.exe"
if (-not (Test-Path -LiteralPath $exe)) { throw "Published executable was not found: $exe" }
$tdjson = Join-Path $root "tdlib\tdjson.dll"
if (Test-Path -LiteralPath $tdjson) { throw "Unverified tdjson.dll must not be packaged." }
$files = Get-ChildItem -LiteralPath $publish -File | Sort-Object FullName
Write-Host "Release verification passed for $($files.Count) published files."
Write-Host "Executable SHA256: $((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash)"
Write-Host "TDLib native dependency: absent (expected until reviewed)."

