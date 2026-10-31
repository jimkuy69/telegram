# Feature Inventory and Screen Analysis

## Evidence labels

- `OBSERVED`: visible in the supplied Telegram Mini Manager reference screens
- `PLANNED`: requested in the approved Telegram Control Studio plan
- `IMPLEMENTED`: present in this repository; still subject to the checklist's runtime gate

## OBSERVED from the reference screens

| Screen area | Observed elements | Design implication |
|---|---|---|
| Application shell | dark desktop shell, left navigation, top account/settings actions | WPF shell should keep navigation persistent |
| Recipient management | search, group/filter tabs, selection count, status badges, pagination | selection must be explicit and scoped to the active account |
| Message composition | recipient list, message editor, link/media area, preview, send action | preview and confirmation must precede any real send |
| Campaign flow | numbered steps for recipients, message, media/link, review | wizard state should expose the current step and final count |
| Status feedback | success/warning/error badges and summary cards | all Telegram limitations must be visible, not implied |

## PLANNED product features

Accounts, TDLib authentication, groups/channels, member observation, contacts, templates, media, consent-first campaigns, invite links, join requests, persistent jobs, audit logs, dashboard, encrypted backup/restore, installer and pilot gates.

## IMPLEMENTED send safety (Phase 0)

| Feature | Where | Notes |
|---|---|---|
| Test mode with allowlist | Settings > ความปลอดภัยในการส่ง; Queue banner; main-window badge | ON by default; only allowlisted Telegram user IDs can receive messages |
| Emergency stop (kill switch) | Main header button, Queue banner, Settings | Durable DB flag; blocks all sends and pauses running jobs until cleared |
| Blocked-send audit | Activity log | `SendBlocked`, `KillSwitchBlocked`, `KillSwitchPaused`, `EmergencyStopEngaged`, `KillSwitchCleared`, `SendSafetyUpdated` |
| Policy unit tests | `tests/TelegramControlStudio.Tests` | `SendSafetyPolicyTests`, `SendSafetyRepositoryTests`, runner/send-service safety cases |

## IMPLEMENTED consent requests (opt-in)

| Feature | Where | Notes |
|---|---|---|
| Request plan before queueing | ขอยินยอม (Opt-in) window | shows askable/excluded recipients with a Thai reason each, plus the daily quota left and the cooldown, computed by the same gate the runner applies |
| Request eligibility ladder | `ConsentRequestPolicy` | own phone number required, opt-out outranks consent, already-consented skipped, previously declined never re-asked, per-person cooldown (default 30 days), per-day quota (default 20, `0` disables asking) |
| Queueing, not sending | Queue window | the plan becomes a `ConsentRequest` job whose items wait for confirmation; the auto runner sends with flood-wait, pause and history like any campaign |
| Consent evidence | `consent_requests` table | append-only: kind, timestamp, message id, chat id, template id, body sha256, matched keyword, source. Chat text is never stored |
| Reply watcher | inbound TDLib updates | affirmative reply in the chat the request was sent in sets `has_consent`; an opt-out phrase sets `is_opted_out` and always wins; work runs on a bounded pump off the receive loop |
| Per-contact state and inbox | contacts grid column "การขอความยินยอม", กล่องคำตอบ Consent | shows the last event per contact and filters the trail by status |
| Limits editor | Settings > การขอความยินยอม (Opt-in) | cooldown days and daily limit persisted in `app_settings`; invalid values fall back to the shipped defaults |
| Sandbox UI check | `scripts/consent-ui-check.ps1` | drives the real app against a throwaway database copy with the kill switch engaged |
| Tests | `ConsentRequestPolicyTests`, `ConsentRequestQueueServiceTests`, `ConsentRequestRepositoryTests`, `ConsentReplyUpdateParserTests`, `ConsentReplyPolicyTests`, `TdLibConsentReplyHandlerTests` | ladder, quota ceiling, evidence trail, reply parsing and auto-grant/opt-out |

## IMPLEMENTED result report and CSV export

| Feature | Where | Notes |
|---|---|---|
| Date-range report window | sidebar รายงานผล | per-account summary cards, per-day table, failure table and consent totals for a chosen range; read-only, never sends |
| Real per-recipient send time | `job_items.sent_at` (schema v5) | stamped once when an item first reaches `Completed`; earlier rows stay NULL and fall back to `updated_at` |
| Local-day bucketing | `ResultReportCalculator` | outcome time is `sent_at ?? updated_at` converted to the operator's timezone, so an evening send lands on the Thai calendar day it happened |
| Failure grouping | `FailuresGrid` | grouped by the raw `last_error` code with a Thai label and the `JobFailureClassifier` category beside it, so the operator can quote the exact code |
| Consent outcomes in the report | `consent_requests` | asked / granted / rejected / opted-out counts plus awaiting-answer and the grant rate for the same window |
| Campaign vs consent split | `jobs.target_type` | `Campaign:%` and `ConsentRequest:%` jobs are counted separately in the header line |
| CSV export | Export CSV button | four sections (summary, per-day, failures, consent) written as UTF-8 with BOM so Thai opens correctly in Excel; no recipient identifiers are exported |
| Sandbox UI check | `scripts/report-ui-check.ps1` + `scripts/seed-report-sandbox.py` | drives the real app against a synthetic TEMP database with the kill switch engaged, then verifies the exported bytes carry the BOM and every Thai section header |
| Tests | `ResultReportCalculatorTests`, `ResultReportRepositoryTests`, `JobItemRepositoryTests`, `SqliteDatabaseTests`, `CsvExporterTests` | day bucketing, rate excluding pending, reason ordering, window filtering, legacy-schema migration, idempotent `sent_at` stamping and CSV quoting |

## IMPLEMENTED web host (Phase 1)

| Feature | Where | Notes |
|---|---|---|
| Five-screen Blazor Server host | `src/TelegramControlStudio.Web` | dashboard, recipients, compose, queue, settings at `http://127.0.0.1:5199` |
| Consent-first job creation | เขียนข้อความ | recipients without consent are excluded; job is stored as `WaitingForConfirmation` |
| Queue confirmation and cancel | คิวงาน | confirm moves items to `Queued`; cancel marks items `Cancelled` and the aggregate job `Cancelled` |
| Safety controls | คิวงาน + ตั้งค่า | test mode, allowlist (live parse count), emergency stop engage/clear persisted in `send_safety_settings` |
| Shared bootstrap | `StudioHost` | same database, migrations, and TDLib registry as the desktop app; `TELEGRAM_CONTROL_STUDIO_DB` honoured |
| Web audit events | Activity log | `JobCreatedWeb`, `JobQueuedWeb`, `JobCancelledWeb`, `EmergencyStopEngaged`, `KillSwitchCleared`, `SendSafetyUpdated` |

The web host is localhost-only and unauthenticated; it plans work and gates safety. TDLib login and every real send remain in the desktop app.

## Screen acceptance rules

1. The active account is visible before account-scoped actions.
2. Real send/invite actions show target count, consent result and confirmation.
3. `Observed` and incomplete Telegram data are labelled explicitly.
4. Secrets never appear in UI logs, exports or screenshots.
5. A green local/mock state is never presented as Telegram E2E success.
6. Every real send passes test mode, allowlist, consent, and kill-switch checks; each screen that can send shows the current safety state.
7. The web host never sends Telegram messages itself: it may create, confirm, or cancel jobs and change safety settings, and every page shows the live safety state.
