# Clean-Machine Test Report

Use this report only on a disposable Windows x64 test machine or a clean VM. Do not record phone numbers, OTP, 2FA passwords, API hashes, session data, or personal contacts.

## Build under test

- Installer artifact: TelegramControlStudio-installer-main
- Installer artifact ID: 12584962429
- Installer SHA-256: f5500cf05e3f4ecdc4165ae30205a8a7e60e9b29752c7b4b291a7fc91c5ad886
- Workflow run: https://github.com/jimkuy69/telegram/actions/runs/40157970040
- TDLib native artifact/runtime run: https://github.com/jimkuy69/telegram/actions/runs/40168091976
- Test date/time:
- Windows version/build:
- Machine or VM label:
- Operator:

## Pre-install checks

- [ ] Test machine is disposable or backed up.
- [ ] Installer ZIP and SHA-256 were verified.
- [ ] No existing TelegramControlStudio user data is present, or its state is recorded.
- [ ] No primary Telegram account will be used.

## Install checks

- [ ] Installer starts without unexpected permission prompts.
- [ ] Installation completes successfully.
- [ ] Application launches.
- [ ] Version is displayed correctly.
- [ ] Native dependency is found and its manifest/hash check passes.
- [ ] Database is created under %LOCALAPPDATA%\\TelegramControlStudio.
- [ ] Session data is outside the install directory.
- [ ] Application starts without an Administrator account when not required.

## Safe application checks

- [ ] Main window opens.
- [ ] Settings window opens.
- [ ] Add Account wizard opens without entering credentials.
- [ ] Encrypted backup/restore controls are visible.
- [ ] No secret is present in logs or exported reports.
- [ ] No Telegram message or invite is sent during this test.

## Upgrade checks

Record the previous version and install the candidate over it.

- Previous version:
- Candidate version:
- [ ] Upgrade completes without losing application data.
- [ ] Database migration completes.
- [ ] Application starts after upgrade.
- [ ] Existing non-secret preferences remain available.

## Uninstall checks

- [ ] Uninstaller completes.
- [ ] Installed binaries are removed.
- [ ] User data deletion is explicitly explained or offered.
- [ ] User data is not silently deleted.
- [ ] A documented reinstall/restore path remains available.

## Result

Overall result: PASS / FAIL / PARTIAL / NOT VERIFIED

Failed checks and sanitized evidence:

- 
- 
- 

Known risks or follow-up issues:

- 

Only after the report is complete should the matching items in DEVELOPMENT-PLAN-CHECKLIST.md and RELEASE-CHECKLIST.md be updated. A clean-machine pass does not prove Telegram authentication or messaging E2E.
