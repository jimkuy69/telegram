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
- [x] กำหนดแหล่ง Official TDLib, License, pin version และ manifest/hash gate ใน Workflow
- [x] ตรวจสอบ Native TDLib Artifact และ Runtime จาก Workflow Run `40168091976` (Job `121093157042`: Build, package และ Runtime Gate ผ่าน; Artifact `TelegramControlStudio-tdlib-win-x64-1.8.67`, SHA-256 digest `343fa2a2ef4b0e3431c3a1f9407155a535b8db7acc0c3ac005013c1cf5729e20`)

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
- [x] Workflow สร้าง `tdjson.dll` จาก Official TDLib แบบ pin source/version และสร้าง manifest SHA-256
- [x] ตรวจสอบ TDLib Windows x64 artifact จาก Successful Workflow Run `40168091976` (Artifact ID `12589959763`, ยังไม่หมดอายุ)
- [x] Runtime POC กับ TDLib Native จริงจาก Artifact (no-login `getOption(version)` ผ่านใน Job `121093157042`; ยังไม่ใช่การ Login หรือ Telegram E2E)

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
- [x] Dashboard Metrics ครบตามแผน (DashboardSummary wired to SQLite queue metrics; Build/Test/Security/CI/Installer passed on commit `1042b5c`)
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
- [x] Gitleaks Scan (GitHub Actions ผ่านบน Commit `e2accb6`)
- [x] Dependency Vulnerability Scan (GitHub Actions ผ่านบน Commit `e2accb6`; local scan ยังไม่ยืนยัน)
- [x] Import/Media/Path Traversal Security Review (Media root boundary tests)
- [x] Read-only Windows ACL review script (`scripts/verify-windows-data-permissions.ps1`)
- [ ] File Permission Review บน Windows จริง (ต้องรันกับ `%LOCALAPPDATA%\TelegramControlStudio`)

## Phase 13 — Testing

- [x] Unit Tests
- [x] SQLite/Migration Tests
- [x] Queue Recovery Tests
- [x] Import/Export Tests
- [x] TDLib Adapter/Parser Mock Tests
- [x] Security/Redaction Tests
- [x] Build ผ่าน 0 warnings / 0 errors
- [x] Managed Build/Test/Windows x64 Publish ผ่านบน GitHub Actions Commit `40815d02` (Run `40171132638`; Native Runtime แยกเป็น artifact-gated test)
- [ ] UI Test ด้วย Playwright
- [ ] Manual Telegram Acceptance Test
- [ ] Offline/Network Failure Test กับ Runtime จริง

## Phase 14 — Windows Release

- [x] Self-contained Windows x64 Publish script และ release workflow
- [ ] Release verification จาก Artifact TDLib ที่ตรวจสอบ hash/runtime แล้ว (clean-machine ยังไม่ยืนยัน)
- [x] Installer Compile ด้วย Inno Setup ผ่าน Workflow Run `40157970040` (clean-machine install/uninstall/upgrade ยังไม่ยืนยัน)
- [x] Installer install/uninstall smoke test ผ่านบน Windows runner จาก Workflow Run `40635348736` (Artifact `12793551332`; install, required files และ uninstaller ตรวจผ่าน; upgrade ยังไม่ยืนยัน)
- [x] Uninstaller และ Upgrade Test (Windows runner smoke test ผ่านใน Workflow Run `40642958171`; ติดตั้งซ้ำ ตรวจไฟล์สำคัญ และถอนการติดตั้งผ่าน)
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

- [ ] TDLib Native Runtime จริงจาก Release Artifact (ต้องยืนยันจาก Workflow Artifact รอบปัจจุบัน)
- [ ] Authentication จริง
- [ ] Manual Telegram Acceptance Test
- [ ] Security Hardening
- [x] Installer install/uninstall smoke test บน Windows runner (Clean-machine manual/upgrade ยังไม่ยืนยัน)
- [ ] Backup/Restore Drill
