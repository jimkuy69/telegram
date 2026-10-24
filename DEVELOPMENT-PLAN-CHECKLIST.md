# Telegram Control Studio — Development Checklist

สถานะใช้หลักฐานปัจจุบันเท่านั้น:

- `[x]` เสร็จและตรวจสอบแล้ว
- `[ ]` ยังไม่เสร็จ หรือยังไม่มีหลักฐานยืนยันเพียงพอ
- ห้ามติ๊ก Real Telegram E2E จากการผ่าน Unit Test เพียงอย่างเดียว

## Phase 0 — Research และ Compliance

- [ ] Research Report จากเอกสาร Telegram และ TDLib
- [ ] Feature Inventory และ Screen Analysis
- [ ] API Feasibility Matrix
- [ ] Risk Register และ Compliance Rules
- [x] ระบุขอบเขตที่ไม่ทำ: Spam, หลบแบน, ข้าม Privacy, Account Rotation
- [ ] ตรวจสอบ Native TDLib Binary และ License อย่างเป็นทางการ

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
- [ ] ติดตั้งและตรวจสอบ `tdjson.dll` จริง
- [ ] Runtime POC กับ TDLib Native จริง

## Phase 3 — Authentication และ Session

- [x] Authorization State Machine
- [x] Phone/OTP/2FA Request Models
- [x] Session Path Policy
- [x] DPAPI Secret Store
- [ ] Login ด้วย Test Account จริง
- [ ] Session Expired และ Logout กับ Telegram จริง

## Phase 4 — Accounts และ Dashboard

- [x] หลายบัญชีใน Local Database
- [x] Active Account และ Account Isolation Foundation
- [x] Queue/Activity/Pending Request Summary
- [ ] Dashboard Metrics ครบตามแผน
- [ ] ยืนยันสถานะบัญชีจาก Telegram จริง

## Phase 5 — Groups และ Members

- [x] Group/Channel Domain และ Permission Fields
- [x] Group Repository และ TDLib Parser Foundation
- [x] Members Search/แสดงข้อจำกัด
- [x] CSV Export สมาชิก
- [ ] Sync Groups/Members กับ Telegram จริง
- [ ] Pagination จากข้อมูลจริงของ Telegram

## Phase 6 — Contacts และ Import/Export

- [x] Contact Repository และ Account Isolation
- [x] Search Contacts
- [x] CSV Import พร้อม Preview/Validation
- [x] Excel Import/Export
- [x] Consent และ Opt-out Policy
- [x] Soft Delete และ Activity Log
- [x] TDLib Contact Sync Foundation
- [ ] Contact Sync กับ Telegram จริง

## Phase 7 — Templates และ Media

- [x] Template CRUD Foundation
- [x] Template Version/Media Link Foundation
- [x] Media Validation และ SHA-256
- [x] Local Media Library
- [x] Media Search
- [x] Media Soft Delete และ Activity Log
- [x] Media CSV Export (Build/Test ผ่านแล้ว; รออัปโหลด GitHub)
- [ ] Preview ที่ผูกกับการส่ง Telegram จริง

## Phase 8 — Messaging Campaigns

- [x] Campaign Wizard แบบ Consent-first
- [x] กรอง Consent และ Opt-out
- [x] Preview/Confirmation ก่อนสร้าง Job
- [x] Idempotency Key และ Recipient Result Foundation
- [x] Pause/Resume/Cancel/Retry Policy Foundation
- [x] FloodWait Policy
- [ ] ส่งข้อความกับ Telegram จริง
- [ ] Manual Acceptance Test แบบผู้รับยินยอม

## Phase 9 — Invitation Management

- [x] Invite Link Policy Validation
- [x] Join Request Domain/Repository
- [x] TDLib Invite Request/Parser Foundation
- [x] Pending Request UI และ Approve/Reject Foundation
- [ ] สร้าง Invite Link กับ Telegram จริง
- [ ] Approve/Reject Join Request กับ Telegram จริง
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
- [ ] Campaign Success/Failure Dashboard
- [ ] FloodWait/Permission/Error Analytics
- [ ] Export Report ครบตามช่วงเวลาและบัญชี

## Phase 12 — Security Hardening

- [x] Secret Redaction Foundation
- [x] Windows DPAPI Foundation
- [x] Encrypted Backup/Restore Foundation
- [x] Path Validation และ SQLite Integrity Check
- [ ] Gitleaks Scan
- [ ] Dependency Vulnerability Scan
- [ ] Import/Media/Path Traversal Security Review
- [ ] File Permission Review บน Windows จริง

## Phase 13 — Testing

- [x] Unit Tests
- [x] SQLite/Migration Tests
- [x] Queue Recovery Tests
- [x] Import/Export Tests
- [x] TDLib Adapter/Parser Mock Tests
- [x] Security/Redaction Tests
- [x] Build ผ่าน 0 warnings / 0 errors
- [x] Automated Tests ผ่าน 101/101
- [ ] UI Test ด้วย Playwright
- [ ] Manual Telegram Acceptance Test
- [ ] Offline/Network Failure Test กับ Runtime จริง

## Phase 14 — Windows Release

- [ ] Self-contained Windows x64 Publish ตรวจสอบบนเครื่องสะอาด
- [ ] Installer Compile ด้วย Inno Setup
- [ ] Uninstaller และ Upgrade Test
- [ ] Version/Hash ของ Native Dependencies
- [ ] Third-party Notices ตรวจครบทุก Dependency
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

- [ ] TDLib Native Runtime จริง
- [ ] Authentication จริง
- [ ] Manual Telegram Acceptance Test
- [ ] Security Hardening
- [ ] Installer และ Clean-machine Test
- [ ] Backup/Restore Drill
