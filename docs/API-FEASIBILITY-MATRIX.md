# API Feasibility Matrix

| Capability | TDLib method/foundation | Local status | Manual gate |
|---|---|---|---|
| Authorization | `updateAuthorizationState`, `setAuthenticationPhoneNumber`, code/password requests | Implemented and mocked | Test account login |
| Chats | `getChats`, `getChat` | Implemented for main chat sync | Authenticated account |
| Supergroup members | `getSupergroupMembers`, `getUser` | Implemented with offset/limit | Permission/privacy and real response |
| Contacts | `getContacts`, `getUser` | Implemented | Authenticated account |
| Text messaging | `sendMessage` + `inputMessageText` | Implemented behind consent/confirmation | One consenting recipient |
| Flood handling | `FLOOD_WAIT_X` error mapping | Implemented and tested | Real Telegram error observation |
| Invite links | TDLib invite-link service | Implemented behind permission gate | Admin test group |
| Join requests | pending request repository and decision service | Implemented | Real join-request flow |
| Direct bulk invite | Telegram permission/privacy dependent | Deliberately restricted | No bulk bypass behavior |

Official references: <https://core.telegram.org/tdlib/getting-started>, <https://core.telegram.org/tdlib/docs/functions_vars_s.html>, <https://core.telegram.org/api/errors>.
