# Telegram Control Studio

Local-first Windows desktop application for consent-first Telegram account and campaign management.

## Status

The repository starts with the Phase 1 foundation: WPF shell, Core domain boundaries, SQLite initialization boundary, and secret-redaction tests. TDLib authentication and real Telegram operations are intentionally disabled until the adapter and manual acceptance gates are complete.

## Product direction

- Windows desktop application using C#/.NET 8 and WPF.
- Dark navy interface with purple/blue accents, Thai/English localization, left navigation, central data workspace, and right-side detail/workflow panels.
- Local SQLite data with per-account isolation.
- Windows DPAPI for protected secrets; no OTP, 2FA password, API hash, or session values in logs.
- Persistent jobs with pause/resume, idempotency, FloodWait handling, audit logs, backup/recovery, and explicit consent/opt-out checks.
- Invite links and join requests are preferred over unsolicited direct invites.

## Safety scope

This project does not implement spam, privacy bypasses, account rotation to evade limits, scraping of undisclosed Telegram data, or credential collection. Real Telegram calls will only be enabled after a TDLib proof of concept, test-account validation, and manual acceptance testing.

## Build

```powershell
dotnet restore TelegramControlStudio.sln
dotnet build TelegramControlStudio.sln
dotnet test TelegramControlStudio.sln
```

NuGet restore requires access to `https://api.nuget.org/v3/index.json`.

## Security boundary

Never commit API hashes, OTPs, 2FA passwords, TDLib sessions, local databases, or native binaries. User data belongs under `%LOCALAPPDATA%\\TelegramControlStudio\\`, separate from the installed application directory.

## License and dependencies

Dependency licenses and native binary hashes will be recorded in `THIRD-PARTY-NOTICES.md` before release packaging.
