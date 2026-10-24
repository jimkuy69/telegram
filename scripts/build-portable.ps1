param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot),
    [string]$Version = "0.1.0"
)

$ErrorActionPreference = "Stop"
$publish = Join-Path $Root "artifacts\publish\win-x64"
$releaseRoot = Join-Path $Root "artifacts\releases"
$stage = Join-Path $releaseRoot "TelegramControlStudio-$Version-win-x64-portable"
$archive = Join-Path $releaseRoot "TelegramControlStudio-$Version-win-x64-portable.zip"
$hashFile = "$archive.sha256"

$exe = Join-Path $publish "TelegramControlStudio.exe"
if (-not (Test-Path -LiteralPath $exe)) {
    throw "Published executable was not found. Run publish-win-x64.ps1 first: $exe"
}

New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
if (Test-Path -LiteralPath $hashFile) { Remove-Item -LiteralPath $hashFile -Force }

Copy-Item -LiteralPath $publish -Destination $stage -Recurse
Compress-Archive -LiteralPath $stage -DestinationPath $archive -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $hashFile -Value "$hash  $(Split-Path -Leaf $archive)" -Encoding utf8
Remove-Item -LiteralPath $stage -Recurse -Force

Write-Host "Portable package: $archive"
Write-Host "SHA256: $hash"

