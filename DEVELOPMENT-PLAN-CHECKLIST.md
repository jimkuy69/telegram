# Telegram Control Studio — Development Checklist

สถานะใช้หลักฐานปัจจุบันเท่านั้น:

- `[x]` เสร็จและตรวจสอบแล้ว
- `[ ]` ยังไม่เสร็จ หรือยังไม่มีหลักฐานยืนยันเพียงพอ
- ห้ามติ๊ก Real Telegram E2E จากการผ่าน Unit Test เพียงอย่างเดียว

## Phase 0 — Research และ Compliance

- [x] Research Report จากเอกสาร Telegram และ TDLib (`docs/RESEARCH-REPORT.md`)
- [x] Feature Inventory และ Screen Analysis (`docs/FEATURE-INVENTORY.md`; observed/planned แยกชัดเจน)
- [x] API Feasibility Matrix (`docs/API-FEASIBILITY-MATRIX.md`)
- [x] Risk Register และ Compliance Rules (`docs/RISK-REGISTER.md`)
- [x] ระบุขอบเขตที่ไม่ทำ: Spam, หลบแบน, ข้าม Privacy, Account Rotation
- [x] ตรวจสอบ Native TDLib Binary และ License อย่างเป็นทางการ (artifact manifest + SHA-256; live Telegram E2E ยังไม่ยืนยัน)

## Phase 1 — Project Foundation

- [x] WPF Solution และ .NET Project Structure
- [x] SQLite Local Database และ Migration Foundation
- [x] MVVM, Navigation Shell และ Dark UI
- [x] Global error boundary เบื้องต้น
- [x] Local-only configuration
- [ ] ไทย/อังกฤษแบบครบทุกหน้าจอ

## Phase 2 — TDLib POC

- [x] TDLib JSON Request/Response Models
- [x] Request Correlation ด้วย `@extra`
- [x] Receive/Update Routing Foundation
- [x] Native Library Loader และ Lifecycle Foundation
- [x] ติดตั้งและตรวจสอบ `tdjson.dll` จริง (Windows x64 artifact; SHA-256 ตรง manifest)
- [x] Runtime POC กับ TDLib Native จริง (no-login `getOption(version)` ผ่านบน Windows x64; live auth ยังไม่ยืนยัน)

## Phase 3 — Authentication และ Session

- [x] Authorization State Machine
- [x] Phone/Email/OTP/2FA/Registration Request Models
- [x] Session Path Policy
- [x] DPAPI Secret Store
- [ ] Login ด้วย Test Account จริง
- [ ] Session Expired และ Logout กับ Telegram จริง

## Phase 4 — Accounts และ Dashboard

- [x] หลายบัญชีใน Local Database
- [x] Active Account และ Account Isolation Foundation
- [x] Profile Sync Foundation จาก TDLib `getMe` (Manual Telegram E2E ยังไม่ยืนยัน)
- [x] Queue/Activity/Pending Request Summary
- [ ] Dashboard Metrics ครบตามแผน
- [ ] ยืนยันสถานะบัญชีจาก Telegram จริง

## Phase 5 — Groups และ Members

- [x] Group/Channel Domain และ Permission Fields
- [x] Group Repository และ TDLib Parser Foundation
- [x] Members Search/แสดงข้อจำกัด
- [x] CSV Export สมาชิก
- [ ] Sync Groups/Members กับ Telegram จริง (เพิ่ม service/UI สำหรับ Supergroup แล้ว; Manual Telegram E2E ยังไม่ยืนยัน)
- [ ] Pagination จากข้อมูลจริงของ Telegram (service + UI รองรับ offset/limit; Manual Telegram E2E ยังไม่ยืนยัน)

## Phase 6 — Contacts และ Import/Export

- [x] Contact Repository และ Account Isolation
- [x] Search Contacts
- [x] CSV Import พร้อม Preview/Validation
- [x] Excel Import/Export
- [x] Consent และ Opt-out Policy
- [x] Soft Delete และ Activity Log
- [x] TDLib Contact Sync Foundation
- [ ] Contact Sync กับ Telegram จริง (บริการและปุ่ม UI พร้อม; Manual Telegram E2E ยังไม่ยืนยัน)

## Phase 7 — Templates และ Media

- [x] Template CRUD Foundation
- [x] Template Version/Media Link Foundation
- [x] Media Validation และ SHA-256
- [x] Local Media Library
- [x] Media Search
- [x] Media Soft Delete และ Activity Log
- [x] Media CSV Export (Build/Test ผ่านแล้ว และอัปโหลด GitHub แล้ว)
- [ ] Preview ที่ผูกกับการส่ง Telegram จริง

## Phase 8 — Messaging Campaigns

- [x] Campaign Wizard แบบ Consent-first
- [x] กรอง Consent และ Opt-out
- [x] Preview/Confirmation ก่อนสร้าง Job
- [x] Idempotency Key และ Recipient Result Foundation
- [x] Pause/Resume/Cancel/Retry Policy Foundation
- [x] FloodWait Policy
- [ ] ส่งข้อความกับ Telegram จริง (UI/runner/Consent gate พร้อม; Manual Acceptance ยังไม่ยืนยัน)
- [ ] Manual Acceptance Test แบบผู้รับยินยอม

## Phase 9 — Invitation Management

- [x] Invite Link Policy Validation และ Revoke ผ่าน TDLib gate
- [x] Join Request Domain/Repository
- [x] TDLib Invite Request/Parser Foundation
- [x] Pending Request UI และ Approve/Reject Foundation
- [ ] สร้าง Invite Link กับ Telegram จริง (UI/service/Join Request gate พร้อม; Manual E2E ยังไม่ยืนยัน)
- [ ] Approve/Reject Join Request กับ Telegram จริง (TDLib service/UI gate พร้อม; Manual E2E ยังไม่ยืนยัน)
- [ ] Direct Invite Permission Check แบบ End-to-End

## Phase 10 — Queue, Logging และ Recovery

- [x] Persistent Job Queue
- [x] Job State และ Job Item Foundation
- [x] Retry/FloodWait/Recovery Policy
- [x] Safe Startup Recovery
- [x] Activity Log และ Secret Redaction
- [x] Queue UI
- [ ] Crash Recovery กับงาน Telegram จริง
- [ ] Safe Shutdown ระหว่าง Native TDLib Runtime จริง

## Phase 11 — Dashboard และ Analytics

- [x] Queue และ Pending Request Summary เบื้องต้น
- [x] Activity Log View
- [x] Campaign Success/Failure Dashboard (แยก Campaign Job ตาม account/date range)
- [x] FloodWait/Permission/Error Analytics (SQLite metrics wired to Dashboard)
- [x] Export Report ครบตามช่วงเวลาและบัญชี (CSV; date range จาก Dashboard)

## Phase 12 — Security Hardening

- [x] Secret Redaction Foundation
- [x] Windows DPAPI Foundation
- [x] Encrypted Backup/Restore Foundation
- [x] Path Validation และ SQLite Integrity Check
- [ ] Gitleaks Scan
- [ ] Dependency Vulnerability Scan (local `dotnet list package --vulnerable` requires a successful NuGet restore; current clean result is not verified)
- [x] Import/Media/Path Traversal Security Review (Media root boundary tests; Windows file-permission review remains pending)
- [ ] File Permission Review บน Windows จริง

## Phase 13 — Testing

- [x] Unit Tests
- [x] SQLite/Migration Tests
- [x] Queue Recovery Tests
- [x] Import/Export Tests
- [x] TDLib Adapter/Parser Mock Tests
- [x] Security/Redaction Tests
- [x] Build ผ่าน 0 warnings / 0 errors
- [x] Automated Tests ผ่าน 148/148 (local, no-restore)
- [ ] UI Test ด้วย Playwright
- [ ] Manual Telegram Acceptance Test
- [ ] Offline/Network Failure Test กับ Runtime จริง

## Phase 14 — Windows Release

- [x] Self-contained Windows x64 Publish และ release verification (local ผ่าน; clean-machine ยังไม่ยืนยัน)
- [x] Installer Compile ด้วย Inno Setup (local preflight ผ่าน; clean-machine install ยังไม่ยืนยัน)
- [ ] Uninstaller และ Upgrade Test
- [x] Version/Hash ของ Native Dependencies
- [x] Third-party Notices ตรวจครบทุก Dependency
- [ ] Backup/Restore ก่อน Release

## Phase 15 — Pilot และใช้งานจริง

- [ ] Local Test Account
- [ ] Read-only Pilot
- [ ] Contacts/Template Pilot
- [ ] Consent Messaging Pilot
- [ ] Invite Link/Join Request Pilot
- [ ] Multi-account Pilot
- [ ] Restore Drill
- [ ] Final Go-Live Review

## Current Gate

**สถานะรวม: FOUNDATION และ LOCAL FEATURES ผ่านบางส่วน**

**ยังไม่อนุญาตให้เรียกว่า Production Ready** จนกว่าจะผ่านอย่างน้อย:

- [x] TDLib Native Runtime จริง (local no-login runtime gate ผ่าน)
- [ ] Authentication จริง
- [ ] Manual Telegram Acceptance Test
- [ ] Security Hardening
- [ ] Installer และ Clean-machine Test
- [ ] Backup/Restore Drill
