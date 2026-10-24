# Manual Acceptance Checklist

ใช้บัญชีทดสอบแยกจากบัญชีหลัก และห้ามใส่ OTP/2FA ลงใน Issue, Log หรือ Screenshot

## Local verification

- [x] Solution Build ผ่าน 0 warnings / 0 errors
- [x] Full xUnit suite ผ่าน 59 tests
- [x] SQLite initialization และ migration smoke test
- [x] Queue pause/resume/retry/cancel persistence
- [x] Activity log redaction
- [x] DPAPI backup/restore round-trip
- [x] Windows x64 self-contained publish และ release verifier

## Requires a controlled Telegram test account

- [ ] Authentication state reaches `Ready`
- [ ] Session directory is created under the account-scoped local path
- [ ] Logout changes status to `Offline` and closes TDLib client
- [ ] Group/Channel sync reports only data returned by Telegram
- [ ] Member access denial is shown without claiming complete membership
- [ ] Invite Link with Join Request is created and audited
- [ ] No real message is sent without explicit confirmation and consent
- [ ] `FLOOD_WAIT_X` pauses the job until the server-provided time

## Release gates

- [ ] Inno Setup compile succeeds on a machine with `ISCC.exe`
- [ ] Install and uninstall tested on a clean Windows x64 machine
- [ ] Upgrade preserves `%LOCALAPPDATA%\TelegramControlStudio`
- [ ] Backup and restore tested after installation

Items marked unchecked are not claimed as passed.

