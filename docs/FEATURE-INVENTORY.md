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

## IMPLEMENTED read-only grid cells and copy buttons (v0.3.2)

| Feature | Where | Notes |
|---|---|---|
| Grid cells never enter edit mode | every `DataGridTextColumn` in the 12 window XAML files | the bindings now carry `Mode=OneWay`; WPF defaults a text column's binding to TwoWay, and clicking such a cell starts `DataGridCell.BeginEdit`, which threw `InvalidOperationException: A TwoWay or OneWayToSource binding cannot work on the read-only property 'Id' of ... ContactRow` — the row view models are read-only, so the edit was never going to commit anything |
| Unhandled exception kills the app | `App.DispatcherUnhandledException` | that exception reached the global handler, wrote `logs\startup-crash.log` and shut the process down, which is why a single cell click closed the program |
| Copy Telegram ID / Username | `MainWindow.xaml` contacts grid + `CopyTelegramId_Click` / `CopyUsername_Click` | each of the two columns is now a template column with a copy glyph (`CopyTelegramIdButton`, `CopyUsernameButton`) that puts that row's value on the clipboard — the supported way to take a value out of the grid now that cells are not editable |
| Clipboard failure stays local | `MainWindow.CopyToClipboard` | the clipboard belongs to the desktop and another process can hold it, so a failed write shows a Thai error box instead of propagating to the global handler; an empty username copies nothing |

Sandbox check (`TELEGRAM_CONTROL_STUDIO_DB` copy, kill switch engaged through `SendSafetyRepository`): a real mouse click on the first row's Telegram ID cell left the process alive with no dialog, and the copy button put `8154557922` on the clipboard.

## IMPLEMENTED TDLib reply shapes (v0.3.3)

| Feature | Where | Notes |
|---|---|---|
| Group members are read from the sender | `TdLibGroupMemberParser.ReadUserId` | TDLib describes each entry of `getSupergroupMembers` as a `chatMember` whose id lives in `member_id.user_id` (`messageSenderUser`). The parser only looked for a top-level `user_id`, so every sync stored zero rows and the member list looked empty. `messageSenderChat` senders (a channel posting as itself) are skipped because they are not users |
| Invite links are read from the flat reply | `TdLibInviteLinkResponseParser.Parse` | `createChatInviteLink` returns `chatInviteLink` with `invite_link` as a plain string and **no chat id**, so the chat the request was sent for is recorded instead of being read back from the reply; `member_limit`/`expiration_date`/`is_revoked` map to the stored limit, expiry and revoked state |
| Telegram's reason reaches the operator | `TdLibResponseGuard` used by `TdLibGroupMemberSyncService`, `TdLibInviteLinkService`, `TdLibJoinRequestDecisionService` | an `error` reply carries the same `@extra` as a successful one, so it arrived through the normal path and the caller reported only "no valid response"; the services now quote the real code, e.g. `TDLib getSupergroupMembers rejected by Telegram: 403: CHAT_ADMIN_REQUIRED` |
| Confirmation no longer demands `ok` | `TdLibInviteLinkService.RevokeAsync`, `TdLibJoinRequestDecisionService.DecideAsync` | `revokeChatInviteLink` and `processChatJoinRequest` answer with a `chatInviteLink`, never an `ok` object, so requiring one made both operations report failure even when Telegram had carried them out |
| Supergroups are labelled by kind | `TelegramGroupUpdateParser.Parse` | `chatTypeSupergroup` covers both groups and channels; `is_channel` is what separates them, so a group the operator owns no longer reads as a channel |
| Member list says what it does not know | `GroupMembersWindow.ShowEmptyHint` | TDLib's `chat` object has no `can_view_members` field, so the old empty state declared the chat restricted without ever having asked. The hint now states that only Sync can tell, and that Telegram hands out only the members the account may see |


## IMPLEMENTED inbound join requests (v0.3.4)

| Feature | Where | Notes |
|---|---|---|
| Join requests captured | `ConsentReplyPump.ProcessAsync` | the inbound sink already carried every update Telegram sends, but only the consent handler read it, so `updateChatJoinRequest` was parsed by tests and never reached `join_requests`; the pump now hands each update to both handlers |
| One handler cannot hide the other's work | same | a failure in either handler is caught per handler, so a malformed reply does not discard a join request on the same trail — and vice versa |
| Window says what an empty list means | `PendingRequestsWindow.LoadAsync` | the grid told the operator nothing when no request had arrived; it now counts the pending rows or explains that Telegram only sends them while this account is an administrator of the chat and its session is open |
| Refresh without reopening | `PendingRequestsWindow` + `Refresh_Click` | requests arrive while the window stays open, so pulling the list again no longer needs an account switch or a restart |

## IMPLEMENTED previews and pickers the operator can see (v0.3.5 - v0.3.9)

| Feature | Where | Notes |
|---|---|---|
| Attached media previews in the template editor | `TemplateEditorWindow.UpdateMediaPreview` | choosing a picture used to change only a caption, so the preview pane still showed text alone; jpg/jpeg/png/gif/bmp now render inside the preview, read with `BitmapCacheOption.OnLoad` and frozen so `LocalAppData\TelegramControlStudio\media` stays free for the send |
| Unsupported media is named, not shown broken | same | WebP and PDF have no Windows image codec, so they are announced as "จะส่งเป็นไฟล์แนบ" instead of an empty image box |
| Campaigns pick a saved template by name | `CampaignWizardWindow.ReloadTemplatesAsync` | the wizard asked for a numeric Template ID, which nobody can read off the Template Editor; it now lists `name (version)` for the active account, refills on every dropdown open, and says how many templates exist or tells the operator to create one first |
| The chosen template writes its body into the message box | `CampaignWizardWindow` + `TemplateBox_SelectionChanged` | creating a campaign reads the body from the database, so an empty box would have sent something different from what the operator saw; a refresh never overwrites a draft that was already edited |
| Invite links pick the chat from the synced groups | `InviteLinksWindow.ReloadChatsAsync` | the free-text Chat ID box is how a positive id like `8263209286` reached TDLib and came back as "did not return a valid invite link response"; the window now offers the chats it already knows, titled with the negative TDLib id, and preselects the first one |
| One harness for all three | `scripts/tool-choice-ui-check.ps1` | runs the renamed `TcsSandbox.exe` against a copy of the database with the kill switch engaged, drives the media dialog, both dropdowns and the create button, and refuses to continue if the badge is missing. It also refuses to start unless the database override points at the copy, because a lost override once opened the live database |
| Copy button on every saved invite link | `InviteLinksWindow.CopyInviteLink_Click` | the Invite URL column is a template column now, so the operator no longer has to select grid text by hand to paste the link into Telegram; the window answers with the copied URL, and a clipboard held by another process is reported instead of silently doing nothing |
| The harness seeds an invite link and reads the clipboard | same | it inserts one link per account through `InviteLinkRepository`, clicks `CopyInviteLinkButton` and fails the run unless the clipboard carries the seeded URL |
| The copy button survives a narrow window | `InviteLinksWindow.xaml` | v0.3.8 put the text and the button in a StackPanel, so at the operator's window width the star-sized column gave the cell ~130px and the button was laid out outside the visible clip — present in UIA, invisible on screen. The cell is a DockPanel now with the button pinned right, the URL trimmed with an ellipsis, a `MinWidth` of 300 on the column, and the harness shrinks the window and compares the button rect against it |

## IMPLEMENTED contact import errors the operator can act on (v0.3.10)

| Feature | Where | Notes |
|---|---|---|
| Every rejected row says why, in Thai | `ContactCsvImporter.Preview` | the window used to show `ROW_2_INVALID … ROW_15_INVALID` for a file built from its own template, which gave the operator nothing to fix. A row now names the column and quotes the offending value, e.g. `แถว 2: telegram_user_id ต้องเป็นเลขจำนวนเต็มบวก (อ่านได้ "1.23457E+11")` — the scientific notation Excel writes for a long id |
| A short row is its own failure | same | Excel omits trailing blank cells, so a row that stops at `phone_number` arrived as the same generic code as a bad id. It now reports how many columns were read and that `has_consent` / `is_opted_out` may not be left blank |
| Consent stays explicit | same | a blank consent column is still refused rather than defaulted to `false`, because an omitted answer must not be read as permission either way |
| Header mismatch names the expected header | `ContactCsvImporter.HeaderError` | the required 7-column header is quoted in full, and the empty-workbook path in `ContactXlsxImporter` shares the same message instead of returning `HEADER_INVALID` |
| The template says what it checks | `ContactImportWindow.DownloadTemplate_Click` | after saving the template the dialog states that `telegram_user_id` must be a positive integer and that the two consent columns take `true`/`false` or `1`/`0` |

## Phone-only lists resolve their Telegram IDs through Telegram (v0.3.11)

| Feature | Where | Notes |
|---|---|---|
| A row with only a phone number is no longer an error | `ContactCsvImporter.Preview` / `ContactImportResult.PhoneOnly` | v0.3.10 told the operator 5,000 rows were invalid because column A was empty, and there was nothing to fix — the file genuinely has no Telegram IDs. A blank id with a valid international number now routes to its own list, while a row carrying neither is still refused. |
| `importContacts` in batches of 200 | `TdLibContactPhoneImportService` | `contacts.importContacts` accepts at most 200 numbers per request, so 5,000 rows go out as 25 requests; the service asserts the cap and reports progress per batch. |
| TDLib answers `user_ids` positionally (v0.3.14) | `TdLibImportedContactsParser` | v0.3.11–v0.3.13 read an `imported[].client_id` reply that TDLib 1.8.67 does not send: the real `importedContacts` object carries `user_ids` in the same order as the request, with `0` for a number that is not a registered user. The probe that guessed a 0/1 `client_id` base is gone, because the position is the mapping. |
| Duplicate detection covers both kinds | same | the same phone in two rows, or a phone that also appears on a row that has an ID, is reported per row rather than imported twice. |
| Every batch is read back before it is stored (v0.3.12) | `TdLibContactPhoneImportService.VerifyAsync` | the reply is positional, so a reordered or truncated answer would still put an id on the wrong phone. The first recognised row of every batch is read back with `getUser`; a phone that comes back different drops that batch and stops the run instead of storing other people's ids against the list. |
| Second confirmation names the side effect | `ContactImportWindow.ResolveByPhoneAsync` | `importContacts` writes into the signed-in account's Telegram contact list and shows on every device of that account, so the dialog states that before the numbers leave the machine; double-clicking the confirm button cannot send the same numbers twice. |
| Unresolved rows say why | `TdLibContactPhoneImportService` | a zero in `user_ids` means Telegram has no registered account for that number (or phone search is off), reported per row, and a throttled or aborted run lists the rows it never got to send instead of dropping them. |
| Resolution only runs against a live account | `ContactImportWindow` | if the selected account is not connected to TDLib the import still commits the rows that already carry IDs and states how many numbers were left waiting. |
| The preview screen is driven, not assumed | `scripts/import-preview-ui-check.ps1` | runs a renamed copy of the published app against a copy of the database, picks a fixture CSV through the real file dialog, and fails unless the window reports `ผ่าน 1 • รอ resolve จากเบอร์โทร 3 • ผิดพลาด 2` and names both refused rows. It closes the window without touching Confirm, so no number ever reaches Telegram. |

## IMPLEMENTED quick send (v0.3.15)

| Feature | Where | Notes |
|---|---|---|
| Sync → tick → type → send in one window | `QuickSendWindow` (left nav "ส่งเร็ว (Quick Send)") | the contacts already stored for the active account load into a checkable grid with a search box (name, username, phone or Telegram ID); `ซิงก์จาก Telegram` pulls the account's contact list again with per-contact progress. Existing recipients, message editor, link field and interval sit on the same screen, so a send no longer walks through the import, campaigns, queue and confirm screens. |
| One confirmation per batch asserts consent | `QuickSendQueueService.QueueAsync` | the dialog names the ticked count, the eligible count, how many are not yet consented, how many are opted out, whether TEST MODE narrows the list, and the per-recipient interval. Answering "ใช่" is recorded as the operator's assertion that the selected people agreed; only then are those rows marked consented in `contacts.has_consent`, before the job is created. |
| Selection math is shown before the click | `QuickSendWindow.UpdateSelection` | header reads `เลือก N • จะส่งได้ M • ยังไม่เคยยินยอม X • opt-out Y`, where eligible excludes opted-out rows and, while TEST MODE is on, anyone outside the allowlist. |
| Send uses the same choke point as campaigns | `TdLibCampaignJobRunner` | opted-out rows are dropped at queue time and every recipient is re-checked against the kill switch, consent and TEST MODE inside the runner, so the quick path cannot bypass the policy; flood-wait pauses the job and the kill switch stops it mid-batch. |
| Result is summarised, not just counted | `QuickSendWindow.ShowSummaryAsync` | after the run the dialog groups skipped rows by reason (consent, allowlist, kill switch, missing contact) and failed rows by message, and reports how many consents this batch recorded. |
| Randomized message variants were refused | design | the reference tool rotates several message variants to look less like bulk sending. That exists to evade Telegram's spam detection, so this app sends exactly the text the operator confirmed, through the consent-first gate instead. |

## Screen acceptance rules

1. The active account is visible before account-scoped actions.
2. Real send/invite actions show target count, consent result and confirmation.
3. `Observed` and incomplete Telegram data are labelled explicitly.
4. Secrets never appear in UI logs, exports or screenshots.
5. A green local/mock state is never presented as Telegram E2E success.
6. Every real send passes test mode, allowlist, consent, and kill-switch checks; each screen that can send shows the current safety state.
7. The web host never sends Telegram messages itself: it may create, confirm, or cancel jobs and change safety settings, and every page shows the live safety state.
