# Windows Release Checklist

TDLib artifact procedure: [TDLIB-RELEASE-ARTIFACT-RUNBOOK.md](TDLIB-RELEASE-ARTIFACT-RUNBOOK.md)

## Local gates

- `dotnet build TelegramControlStudio.sln --no-restore --verbosity minimal`
- `dotnet test TelegramControlStudio.sln --no-build --verbosity minimal`
- `powershell -ExecutionPolicy Bypass -File scripts/publish-win-x64.ps1`
- `powershell -ExecutionPolicy Bypass -File scripts/verify-release.ps1`
- `powershell -ExecutionPolicy Bypass -File scripts/build-installer.ps1`
- `powershell -ExecutionPolicy Bypass -File scripts/verify-tdlib-runtime.ps1`

## Package gates

- Confirm `TelegramControlStudio.exe` exists in `artifacts/publish/win-x64`.
- Confirm the successful TDLib GitHub Actions artifact contains `tdjson.dll`, `tdjson.manifest.json`, all required runtime DLLs, and matching SHA-256 values.
- Confirm the artifact was checked with both `verify-tdlib-native.ps1` and `verify-tdlib-runtime.ps1` before packaging.
- Confirm Database and Session data are outside the install directory under `%LOCALAPPDATA%/TelegramControlStudio`.
- Run `powershell -ExecutionPolicy Bypass -File scripts/verify-windows-data-permissions.ps1` and record the output; this check is read-only and does not alter ACLs.
- Run the installer on a clean Windows x64 machine.
- Test uninstall without deleting user data silently.
- Record version, commit, publish date, and native dependency hashes.

## Current evidence

- Build/test are local gates only.
- Telegram End-to-End authentication and messaging are not verified.
- Release preflight is allowed to pass the TDLib gate only when the verified artifact is present; hash verification alone is insufficient.
- The native DLL is intentionally not committed to `main`; the release evidence must identify the successful Workflow Run and artifact.
- Installer compilation requires Inno Setup (`ISCC.exe`) and is not claimed PASS unless it is run.
