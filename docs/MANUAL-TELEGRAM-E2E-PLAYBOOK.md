# Manual Telegram E2E Playbook

This document is the controlled acceptance procedure for the first real Telegram test. It does not replace unit tests or the TDLib native runtime gate.

## Safety boundary

- Use a dedicated Telegram test account, never a primary/personal account.
- Do not paste phone numbers, OTP codes, 2FA passwords, API hashes, session files, or backup keys into GitHub, issues, comments, or logs.
- Start with read-only checks. Do not send messages, invite users, or modify group membership until the operator has explicitly confirmed the test target and consent.
- Use a small consented test target only. No bulk messaging, account rotation, privacy bypass, or flood-limit avoidance.
- Stop immediately on an unexpected authorization state, restriction, privacy error, or FloodWait.

## Preflight

1. Download the TDLib artifact from the successful workflow run.
2. Verify the artifact name, SHA-256 digest, architecture, and manifest before copying it beside the application.
3. Run the Windows data-permission check against the selected local app-data directory.
4. Start the application with an empty test data directory.
5. Confirm that the application shows the correct native dependency hash and does not print secrets.

Evidence to capture: workflow URL, artifact ID/digest, application version, Windows architecture, and sanitized application logs.

## Authentication acceptance

1. Open Add Account.
2. Enter the test account phone number only in the application.
3. Enter the OTP in the secure UI when requested.
4. Enter the 2FA password only in the secure UI when requested.
5. Confirm that the authorization state reaches Ready.
6. Confirm that the account profile is isolated under its account ID.
7. Close and reopen the application.
8. Confirm that the account state is restored without exposing OTP or 2FA material.
9. Log out and confirm the session is closed and local session cleanup follows the configured policy.

Pass criteria:

- No secret appears in UI logs, activity logs, crash logs, or exported reports.
- A wrong OTP/2FA value produces a safe mapped error.
- The test account can be removed without changing another account's data.

## Read-only Telegram checks

With the test account authorized:

1. Refresh the account profile.
2. Load groups/channels visible to that account.
3. Check permissions before opening member or invite operations.
4. Sync a small contact set only if the test data is consented.
5. Confirm that unavailable data is reported as unavailable, not as a complete list.

Do not mark a read-only check as passed from a mock or cached database alone.

## Consent messaging acceptance

1. Create one template.
2. Select one or a few consented recipients.
3. Verify the Preview and opt-out filter.
4. Confirm the Job summary and explicit confirmation step.
5. Send only after the operator confirms.
6. Verify per-recipient result, idempotency behavior, pause/resume, and cancel.
7. Confirm that a recipient without consent is skipped and recorded.

## Invite-link acceptance

1. Select a test group where the account has the required permission.
2. Create an invite link with a short expiration and low usage limit.
3. Prefer Join Request mode.
4. Approve and reject only controlled test requests.
5. Revoke the link after the test.
6. Verify the audit record.

Direct invites remain disabled unless the permission, privacy, and consent checks all pass.

## Recovery acceptance

1. Start a small consented read-only or messaging Job.
2. Pause it and restart the application.
3. Confirm the persisted Job state and resume token.
4. Resume or cancel explicitly.
5. Verify that already-completed items are not sent twice.
6. Create an encrypted backup and perform a restore drill in a separate test data directory.

## Evidence record

Record only sanitized evidence:

- Date/time and application version
- Test account label, not its phone number
- Workflow run and artifact ID
- Test case name and result
- Job ID and item counts
- Error category and FloodWait duration if applicable
- Screenshots with phone numbers, usernames, OTP, and session data redacted

A passing manual test does not authorize production use or bulk operations. Update DEVELOPMENT-PLAN-CHECKLIST.md only after the corresponding real Telegram evidence exists.
