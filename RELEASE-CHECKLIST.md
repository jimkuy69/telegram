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
- Confirm the installer artifact includes `release-manifest.json` with repository commit, TDLib Workflow Run/Artifact IDs, and SHA-256 values.
- Record version, commit, publish date, and native dependency hashes.

## Current evidence

- Managed Build/Test/Windows x64 Publish passed on Workflow Run `40171132638` (commit `40815d02`).
- Security scan passed on the same change.
- TDLib native Build, package verification, and no-login Runtime Gate passed on Run `40168091976`, Job `121093157042`.
- Verified TDLib artifact: `TelegramControlStudio-tdlib-win-x64-1.8.67`, Artifact ID `12589959763`, digest `sha256:343fa2a2ef4b0e3431c3a1f9407155a535b8db7acc0c3ac005013c1cf5729e20`.
- Telegram End-to-End authentication and messaging are not verified.
- Release preflight is allowed to pass the TDLib gate only when the verified artifact is present; hash verification alone is insufficient.
- The native DLL is intentionally not committed to `main`; the release evidence must identify the successful Workflow Run and artifact.
- Installer Workflow Run `40172166600` passed: verified TDLib download/hash, Windows x64 publish, Inno Setup install, installer compile, installer hash, and provenance files.
- Installer artifact: `TelegramControlStudio-installer-main`, Artifact ID `12590842159`, digest `sha256:81d0c4bb6e6ca31ac34deb4bb3d4291c6303174f34cada61b5d52d8de9e37e5a`.
- ZIP inspection passed: `release-manifest.json` and `tdlib.provenance.json` are present; `provenanceMatches=true`; installer SHA-256 `62d127d755154a0f0958d603bb74aae09928b75d4f3d156b9d9fdb41a43b62bb`.
- Installer compilation is verified; clean-machine installation, uninstall, upgrade, and user-data preservation remain unverified.
