param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot),
    [string]$BinaryRelativePath = "tdlib\tdjson.dll",
    [string]$ManifestRelativePath = "tdlib\tdjson.manifest.json"
)

$ErrorActionPreference = "Stop"

$binary = Join-Path $Root $BinaryRelativePath
$manifestPath = Join-Path $Root $ManifestRelativePath

if (-not (Test-Path -LiteralPath $binary)) {
    Write-Host "BLOCKED: Native TDLib binary is missing: $binary"
    exit 2
}

if (-not (Test-Path -LiteralPath $manifestPath)) {
    Write-Host "BLOCKED: Native TDLib manifest is missing: $manifestPath"
    exit 2
}

$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
foreach ($field in @("sourceUrl", "version", "license", "architecture", "sha256")) {
    if ([string]::IsNullOrWhiteSpace([string]$manifest.$field)) {
        throw "Manifest field is missing: $field"
    }
}

$actualHash = (Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash.ToLowerInvariant()
$expectedHash = ([string]$manifest.sha256).ToLowerInvariant()
if ($actualHash -ne $expectedHash) {
    throw "TDLib SHA-256 mismatch. Expected $expectedHash, got $actualHash."
}

if ($manifest.architecture -ne "win-x64") {
    throw "Unsupported TDLib architecture: $($manifest.architecture)"
}

Write-Host "PASS: TDLib native dependency verified"
Write-Host "Version: $($manifest.version)"
Write-Host "Architecture: $($manifest.architecture)"
Write-Host "SHA256: $actualHash"
Write-Host "Source: $($manifest.sourceUrl)"
Write-Host "License: $($manifest.license)"

