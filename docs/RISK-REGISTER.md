# Risk Register and Compliance Rules

| ID | Risk | Control | Evidence status |
|---|---|---|---|
| R-01 | API flooding/spam can restrict or ban accounts | Consent gate, opt-out, interval, FloodWait pause, no rotation | Implemented; live behavior pending |
| R-02 | API hash/session/OTP leakage | DPAPI, in-memory OTP/2FA, redacted logs, local-only data | Automated tests pass |
| R-03 | Telegram returns incomplete member data | `Observed` source note, pagination, limitation text | Implemented; live response pending |
| R-04 | Duplicate sends after restart | Idempotency keys and per-item status | Automated tests pass |
| R-05 | Restore while a TDLib session is alive | Restore blocked while registry has active sessions | Automated test passes |
| R-06 | Native DLL tampering or missing dependencies | SHA-256 manifest and runtime POC before release | Local gate passes |
| R-07 | Import path traversal or unsafe media | Root-boundary validation and file checks | Automated tests pass |
| R-08 | Claims exceed Telegram evidence | VERIFIED/OBSERVED/INFERRED/UNKNOWN labels and manual gates | Documentation control |

Permanent exclusions: spam tooling, ban evasion, account rotation to evade limits, privacy bypass, unsolicited messaging, OTP/password storage, and scraping data Telegram does not expose.
