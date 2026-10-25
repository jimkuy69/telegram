# Telegram Control Studio

Local-first Windows desktop foundation for consent-first Telegram account and campaign management.

## Current status

- Phase 0: research/compliance completed outside this repository; implementation constraints are recorded in the approved plan.
- Phase 1: WPF solution skeleton, Core domain types, SQLite initialization boundary, dashboard shell, and secret-redaction test added.
- Phase 2: TDLib native loader/adapter and verified Windows x64 package are present; the local no-login runtime gate loads `tdjson.dll` and passes `getOption(version)`. Live Telegram authentication remains pending.
- Current verification: 143 automated tests pass locally; Windows WPF build passes with 0 warnings and 0 errors. These results do not prove live Telegram E2E.
- Account/session, messaging, invitation, and real Telegram calls remain behind TDLib runtime, consent, confirmation, and manual-acceptance gates; no live Telegram E2E is claimed.

## Build

```powershell
dotnet restore TelegramControlStudio.sln
dotnet build TelegramControlStudio.sln
dotnet test TelegramControlStudio.sln
```

NuGet restore requires access to `https://api.nuget.org/v3/index.json`.

## Security boundary

Never commit API hashes, OTPs, 2FA passwords, TDLib sessions, local databases, or native binaries. The application is designed to use Windows-protected secret storage and local data only.
