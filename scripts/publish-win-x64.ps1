$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\TelegramControlStudio.App\TelegramControlStudio.App.csproj"
$output = Join-Path $root "artifacts\publish\win-x64"

if (-not (Test-Path -LiteralPath $project)) {
    throw "Application project was not found."
}

if (Test-Path -LiteralPath (Join-Path $root "tdlib\tdjson.dll")) {
    throw "tdjson.dll is present without a release hash gate. Add the reviewed hash before packaging."
}

dotnet restore $project --runtime win-x64
if ($LASTEXITCODE -ne 0) { throw "Runtime-specific restore failed." }

dotnet publish $project --configuration Release --runtime win-x64 --self-contained true --output $output --no-restore
if ($LASTEXITCODE -ne 0) { throw "Windows x64 publish failed." }

Write-Host "Published application to $output"
Write-Host "User data must remain under %LOCALAPPDATA%\TelegramControlStudio."
