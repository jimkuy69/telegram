# API Feasibility Matrix

| Capability | TDLib method/foundation | Local status | Manual gate |
|---|---|---|---|
| Authorization | `updateAuthorizationState`, `setAuthenticationPhoneNumber`, code/password requests | Implemented and mocked | Test account login |
| Chats | `getChats`, `getChat` | Implemented for main chat sync; `chatTypeSupergroup.is_channel` now decides Group vs Channel | Authenticated account |
| Supergroup members | `getSupergroupMembers`, `getUser` | Implemented with offset/limit over `chatMember.member_id` (MessageSender); a member Telegram has not downloaded yet is skipped | Admin test group — Telegram only exposes the list an account is allowed to read |
| Contacts | `getContacts`, `getUser` | Implemented | Authenticated account |
| Text messaging | `sendMessage` + `inputMessageText` | Implemented behind consent/confirmation | One consenting recipient |
| Flood handling | `FLOOD_WAIT_X` error mapping | Implemented and tested | Real Telegram error observation |
| Invite links | `createChatInviteLink`, `revokeChatInviteLink` | Implemented behind permission gate; reads the flat `chatInviteLink` reply and quotes Telegram's error | Admin test group addressed by TDLib's negative chat id |
| Join requests | `processChatJoinRequest` | Decision service implemented (any non-error reply confirms); capture of `updateChatJoinRequest` is not wired into the inbound sink yet | Real join-request flow |
| Direct bulk invite | Telegram permission/privacy dependent | Deliberately restricted | No bulk bypass behavior |

Official references: <https://core.telegram.org/tdlib/getting-started>, <https://core.telegram.org/tdlib/docs/functions_vars_s.html>, <https://core.telegram.org/api/errors>.
