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

## Screen acceptance rules

1. The active account is visible before account-scoped actions.
2. Real send/invite actions show target count, consent result and confirmation.
3. `Observed` and incomplete Telegram data are labelled explicitly.
4. Secrets never appear in UI logs, exports or screenshots.
5. A green local/mock state is never presented as Telegram E2E success.
