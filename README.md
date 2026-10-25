# Telegram Control Studio

Local-first Windows desktop foundation for consent-first Telegram account and campaign management.

## Current status

- Phase 0: research/compliance completed outside this repository; implementation constraints are recorded in the approved plan.
- Phase 1: WPF solution skeleton, Core domain types, SQLite initialization boundary, dashboard shell, and secret-redaction test added.
- Phase 2: TDLib native loader/adapter and verified Windows x64 package are present; local native runtime tests pass, while live Telegram authentication remains pending.
- Current verification: 137 automated tests pass locally; Windows WPF build passes with 0 warnings and 0 errors. These results do not prove live Telegram E2E.
- Telegram accounts, sessions, OTP, messaging, invitations, and real Telegram calls are intentionally disabled.

## Build

```powershell
dotnet restore TelegramControlStudio.sln
dotnet build TelegramControlStudio.sln
dotnet test TelegramControlStudio.sln
```

NuGet restore requires access to `https://api.nuget.org/v3/index.json`.

## Security boundary

Never commit API hashes, OTPs, 2FA passwords, TDLib sessions, local databases, or native binaries. The application is designed to use Windows-protected secret storage and local data only.

