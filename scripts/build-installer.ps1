$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$iscc = Get-Command ISCC.exe -ErrorAction SilentlyContinue
if ($null -eq $iscc) { throw "Inno Setup ISCC.exe was not found. Installer compile is NOT VERIFIED." }
$script = Join-Path $root "installer\TelegramControlStudio.iss"
if (-not (Test-Path -LiteralPath $script)) { throw "Installer script was not found." }
& $iscc.Source $script
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed with exit code $LASTEXITCODE." }
Write-Host "Installer compiled successfully."

