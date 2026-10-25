$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$binary = Join-Path $root 'tdlib\tdjson.dll'
$manifest = Join-Path $root 'tdlib\tdjson.manifest.json'

if (-not (Test-Path -LiteralPath $binary) -or -not (Test-Path -LiteralPath $manifest)) {
    Write-Host 'BLOCKED: verified TDLib binary and manifest are required.'
    exit 2
}

$testProject = Join-Path $root 'tests\TelegramControlStudio.Tests\TelegramControlStudio.Tests.csproj'
& dotnet build $testProject --no-restore -m:1 --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    Write-Host 'BLOCKED: the managed test project could not be built before the TDLib runtime POC.'
    exit 2
}

$previousGate = $env:TDLIB_RUNTIME_GATE
$env:TDLIB_RUNTIME_GATE = '1'
try {
    & dotnet test $testProject --no-build --no-restore --filter 'FullyQualifiedName~TdLibNativeRuntimeTests' --verbosity minimal
} finally {
    if ($null -eq $previousGate) { Remove-Item Env:TDLIB_RUNTIME_GATE -ErrorAction SilentlyContinue }
    else { $env:TDLIB_RUNTIME_GATE = $previousGate }
}
if ($LASTEXITCODE -ne 0) {
    Write-Host 'BLOCKED: TDLib native runtime POC failed. Check dependent native DLLs beside tdjson.dll.'
    exit 2
}

Write-Host 'PASS: TDLib native runtime POC'
