# Windows Release Checklist

## Local gates

- `dotnet build TelegramControlStudio.sln --no-restore --verbosity minimal`
- `dotnet test TelegramControlStudio.sln --no-build --verbosity minimal`
- `powershell -ExecutionPolicy Bypass -File scripts/publish-win-x64.ps1`
- `powershell -ExecutionPolicy Bypass -File scripts/verify-release.ps1`
- `powershell -ExecutionPolicy Bypass -File scripts/build-installer.ps1`

## Package gates

- Confirm `TelegramControlStudio.exe` exists in `artifacts/publish/win-x64`.
- Confirm `tdjson.dll` is absent until its source, version, architecture, license, and SHA-256 are reviewed.
- Confirm Database and Session data are outside the install directory under `%LOCALAPPDATA%/TelegramControlStudio`.
- Run the installer on a clean Windows x64 machine.
- Test uninstall without deleting user data silently.
- Record version, commit, publish date, and native dependency hashes.

## Current evidence

- Build/test are local gates only.
- Telegram End-to-End authentication and messaging are not verified.
- Installer compilation requires Inno Setup (`ISCC.exe`) and is not claimed PASS unless it is run.

