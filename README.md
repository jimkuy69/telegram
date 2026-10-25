# Telegram Control Studio

Local-first Windows desktop application for consent-first Telegram account and campaign management.

## Status

The repository contains the WPF foundation, local SQLite persistence, protected-secret boundaries, persistent job queue, dashboard metrics, media/template workflows, invite-link management, and release scripts. TDLib authentication and real Telegram operations remain gated until the TDLib native binary is verified and manual test-account acceptance is completed.

## Product direction

- Windows desktop application using C#/.NET 8 and WPF.
- Dark navy interface with purple/blue accents, Thai/English localization, left navigation, central data workspace, and right-side detail/workflow panels.
- Local SQLite data with per-account isolation.
- Windows DPAPI for protected secrets; no OTP, 2FA password, API hash, or session values in logs.
- Persistent jobs with pause/resume, idempotency, FloodWait handling, audit logs, backup/recovery, and explicit consent/opt-out checks.
- Invite links and join requests are preferred over unsolicited direct invites.

## Safety scope

This project does not implement spam, privacy bypasses, account rotation to evade limits, scraping of undisclosed Telegram data, or credential collection. Real Telegram calls will only be enabled after a TDLib proof of concept, test-account validation, and manual acceptance testing.

## Build and test

```powershell
dotnet restore TelegramControlStudio.sln
dotnet build TelegramControlStudio.sln
dotnet test TelegramControlStudio.sln
```

The current local verification reached 0 build warnings/errors and 113 passing tests, including the TDLib native runtime POC against the verified Windows x64 artifact. NuGet restore requires access to `https://api.nuget.org/v3/index.json`.

## Windows release

### Local portable package

After a successful publish:

```powershell
pwsh ./scripts/publish-win-x64.ps1
pwsh ./scripts/verify-release.ps1
pwsh ./scripts/build-portable.ps1 -Version 0.1.0
```

The portable ZIP and SHA-256 file are written under `artifacts/releases/`. User data belongs under `%LOCALAPPDATA%\\TelegramControlStudio\\`, separate from the installed application directory.

### GitHub Actions installer

The workflow `.github/workflows/release-installer.yml` supports manual `workflow_dispatch` runs from the Actions tab and tag runs matching `v*` (for example `v0.1.0`). It performs self-contained Windows x64 publish, Inno Setup compilation, SHA-256 output, and installer artifact upload.

The workflow is separate from the local preflight because the local machine may not have Inno Setup installed.

## Security boundary

Never commit API hashes, OTPs, 2FA passwords, TDLib sessions, local databases, or native binaries. The release preflight must pass the TDLib native verification gate before real Telegram functionality is enabled.

## License and dependencies

Dependency licenses and native binary hashes are recorded in `THIRD-PARTY-NOTICES.md` before release packaging.
