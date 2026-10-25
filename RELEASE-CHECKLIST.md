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

- Managed Build/Test/Windows x64 Publish passed on Workflow Run `40171132638` (commit `40815d02`).
- Security scan passed on the same change.
- TDLib native Build, package verification, and no-login Runtime Gate passed on Run `40168091976`, Job `121093157042`.
- Verified TDLib artifact: `TelegramControlStudio-tdlib-win-x64-1.8.67`, Artifact ID `12589959763`, digest `sha256:343fa2a2ef4b0e3431c3a1f9407155a535b8db7acc0c3ac005013c1cf5729e20`.
- Telegram End-to-End authentication and messaging are not verified.
- Release preflight is allowed to pass the TDLib gate only when the verified artifact is present; hash verification alone is insufficient.
- The native DLL is intentionally not committed to `main`; the release evidence must identify the successful Workflow Run and artifact.
- Installer Workflow Run `40157970040` passed: verified TDLib download/hash, Windows x64 publish, Inno Setup install, installer compile, and installer hash.
- Installer artifact: `TelegramControlStudio-installer-main`, Artifact ID `12584962429`, digest `sha256:f5500cf05e3f4ecdc4165ae30205a8a7e60e9b29752c7b4b291a7fc91c5ad886`.
- Installer compilation is verified; clean-machine installation, uninstall, upgrade, and user-data preservation remain unverified.
