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

## IMPLEMENTED delivery reliability (v0.2.9)

| Feature | Where | Notes |
|---|---|---|
| Private chat resolved before every send | `TdLibMessageSendService.ResolvePrivateChatIdAsync` | `sendMessage` needs a chat id and TDLib answers `400: Chat not found` for a user whose private chat was never created locally, so each send (campaign text and consent request) calls `createPrivateChat(user_id, force:false)` first and sends to the returned chat id |
| Chat id kept as evidence | `consent_requests.chat_id` | the resolved chat id is what the consent row records, so the reply watcher matches answers in the same chat the request went to |
| Log-safe send error detail | `TdLibErrorSummary` | turns a TDLib error JSON into a one-line `code: message` (whitespace collapsed, truncated, `UNKNOWN` fallbacks) and stores it as `TDLIB_MESSAGE_SEND_FAILED <detail>`; message text is never part of the reason |
| Actionable failure label in the report | `ReportDisplay.DescribeReason` | a failed send now shows "Telegram ไม่ยอมรับการส่งข้อความ (400: Chat not found)" instead of the raw code |
| Retry acts on the selected job | `QueueWindow.Retry_Click` | the selection is restored after the queue reload, so "ลองใหม่" actually starts the runner instead of silently doing nothing |
| Clean shutdown | `App.OnExit` | TDLib and the reply watcher are disposed off the dispatcher thread, so closing the app no longer deadlocks and updates can be installed |
| Tests | `TdLibErrorSummaryTests`, `TdLibMessageSendServiceTests` | code+message parsing, truncation, detail extraction, and the private-chat-then-send call order |

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

## IMPLEMENTED account deletion

| Feature | Where | Notes |
|---|---|---|
| Trash button in the header | `DeleteAccountButton` next to เพิ่มบัญชี | acts on the account currently selected in the switcher, so an operator can retire a mistaken or abandoned account without touching the database by hand |
| Connected accounts refuse | `DeleteAccount_Click` | if the TDLib client is still registered the box tells the operator to disconnect first, so a live session is never removed mid-flight |
| Blast radius read before confirming | `AccountRepository.CountOwnedRowsAsync` | the first confirmation lists, in Thai, how many jobs, recipients, contacts, consent events, secrets and session rows are about to go; an empty account says so instead of showing nothing |
| Second confirmation | `DeleteAccount_Click` | warns that the DPAPI-protected API values and the TDLib session files are gone too, and that re-adding the account needs a fresh OTP |
| Child-first cascade in one transaction | `AccountRepository.DeleteAsync` | deletes the 16 account-owned tables children first, rolls back on any failure, and returns `AccountDeletion` with the per-table counts so the result box can state exactly how many rows were removed |
| Session folder cleanup | `RemoveAccountSessionDirectory` | deletes `%LOCALAPPDATA%\TelegramControlStudio\sessions\{id}` only after `SessionPathPolicy.IsWithinSessionRoot` confirms the path, and reports if the folder was still locked |
| Sandbox UI check | `scripts/account-delete-ui-check.ps1` + `scripts/seed-account-delete-sandbox.py` | rehearses the destructive flow against a throwaway TEMP database holding one survivor and two junk accounts, then verifies the survivor keeps its rows and no orphan rows remain |
| Tests | `AccountRepositoryDeletionTests` | every owned table emptied, the other account untouched, counts reported before deleting, unknown account returns null without writing |

## IMPLEMENTED group list truth and member view

| Feature | Where | Notes |
|---|---|---|
| Stale chats disappear on sync | `TelegramGroupRepository.PruneAsync` | after a complete round-trip every group missing from Telegram's main chat list is soft-deleted, and its member rows are removed so a chat joined again cannot show stale members; the prune only runs when Telegram actually returned chats |
| Honest empty member list | `GroupMembersWindow` | an empty grid now says whether Telegram hides the member list (`can_view_members = 0`), the search matched nothing, or the chat has simply not been synced — instead of looking broken |
| Select-page survives an open cell | `SelectVisibleContacts_Click` | the handler commits the pending cell edit and lets the two-way binding carry the selection; the old `Items.Refresh()` threw inside the edit transaction and closed the app |
| Tests | `TelegramGroupRepositoryPruneTests` | keeps the survivors, hides the rest, drops pruned member rows, leaves other accounts untouched, idempotent on a second run |

## IMPLEMENTED per-menu help popover

| Feature | Where | Notes |
|---|---|---|
| Asterisk help button on every sidebar menu | `MainWindow.xaml`, `NavHelpButton` style | each of the 16 menu rows carries a small `*` button that opens a popover instead of navigating |
| Usage and scope text per menu | `MainWindow.xaml.cs` `MenuHelp` | two short lines per menu: what to click, then what the menu is allowed to do (consent, test-mode allowlist, kill switch, opt-in quota/cooldown, Telegram limits) |
| Sandbox check | `scripts/menu-help-ui-check.ps1` | 16 asterisk buttons found, opt-in popover shows quota/cooldown/declined text, a second menu swaps the text, kill switch engaged first, `RESULT=PASS` |

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

## IMPLEMENTED in-window account switcher

| Feature | Where | Notes |
|---|---|---|
| Shared account picker control | `AccountSelectorControl.xaml(.cs)` | one dark-theme row (`บัญชี` + ComboBox) listing every account as `Name • Status`; raises `AccountChanged` only for a real user change, so the initial load never triggers a reload |
| Switch without leaving the window | 12 tool windows | Invite Links, Pending Requests, Groups, Activity Log, Campaign Wizard, Contact Import, Media Library, Template Editor, Queue & History, Settings, Consent Inbox, Report each carry the selector in their header |
| Reload per window | each `OnAccountChanged` | lists reload for the new account, selection and previews reset, and Settings clears the previous account's API ID/hash/native-path text before repopulating so nothing stale is shown |
| Global settings stay global | `SettingsWindow` | test mode, allowlist, kill switch and opt-in quota/cooldown are not account-scoped and are untouched by a switch |
| No logout on switch | `MainWindow.xaml.cs` | switching only changes which account a window reads; TDLib sessions stay connected and the badge count is unaffected |

Sandbox check (`TELEGRAM_CONTROL_STUDIO_DB` copy): Queue & History went from `แสดง 0 จากทั้งหมด 0 งาน` on Jim Heehom to `7 จากทั้งหมด 7` on jimmy, Report subtitle switched to `บัญชี 8 (jimmy) • คิวส่งข้อความ 5 คิว • คิวคำขอยินยอม 2 คิว`, and Settings repopulated the API ID for the selected account — all without closing a window.

## IMPLEMENTED concurrent TDLib sessions (v0.3.1)

| Feature | Where | Notes |
|---|---|---|
| One native client instance per session | `TdLibNativeClient` | uses `td_json_client_create/send/receive/execute/destroy` instead of the process-global `td_send`/`td_receive`; TDLib aborts the process (`0xc0000409` in `ucrtbase.dll`, `Client.cpp:285 "Receive must not be called simultaneously from two different threads"`) as soon as two clients read through the global entry points, which closed the whole app on the second account connect |
| Fixed receive thread per session | `TdLibNativeSession.ReceiveThreadMain` | one dedicated `TCS-TDLib-Receive` thread per session reads that client only, with no `Task.Yield()` thread hopping; the thread is created inside `TdLibNativeSession` so a session can never be read from two threads |
| Disconnect stops the thread before destroying | `TdLibNativeSession.ShutdownAsync` | `close` is awaited, the receive thread is joined, and only then is the client instance destroyed and the library handle released — a receive call in flight during destroy would be undefined behaviour, so a thread that refuses to stop leaks the instance instead |
| Two-client regression test | `TdLibNativeRuntimeTests.TwoClientsReceiveOnSeparateDedicatedThreads` (gate `TDLIB_RUNTIME_GATE=1`) | two verified clients each send and receive on their own dedicated thread and must get their own `@extra` response; under the old global `td_receive` this test takes the process down instead of failing |

## Screen acceptance rules

1. The active account is visible before account-scoped actions.
2. Real send/invite actions show target count, consent result and confirmation.
3. `Observed` and incomplete Telegram data are labelled explicitly.
4. Secrets never appear in UI logs, exports or screenshots.
5. A green local/mock state is never presented as Telegram E2E success.
6. Every real send passes test mode, allowlist, consent, and kill-switch checks; each screen that can send shows the current safety state.
7. The web host never sends Telegram messages itself: it may create, confirm, or cancel jobs and change safety settings, and every page shows the live safety state.
