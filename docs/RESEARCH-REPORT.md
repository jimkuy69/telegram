# Telegram Control Studio — Research Report

สถานะเอกสารนี้แยกหลักฐานเป็น `VERIFIED`, `OBSERVED`, `INFERRED` และ `UNKNOWN` และไม่ใช้ตัวเลข Limit ที่ไม่ได้ยืนยันเป็นกฎระบบ

## VERIFIED จากเอกสารทางการ

- Telegram ระบุว่าแอปภายนอกต้องใช้ `api_id` และ `api_hash` ของผู้พัฒนาเอง และการใช้ API เพื่อ flooding/spamming อาจทำให้ถูกแบน: <https://core.telegram.org/api/obtaining_api_id>
- TDLib เริ่ม authorization ด้วย `updateAuthorizationState` และอาจมีขั้น phone, code, password, email หรือ registration ก่อน `authorizationStateReady`: <https://core.telegram.org/tdlib/getting-started>
- TDLib ระบุว่า `sendMessage` ต้องระบุ chat และเนื้อหาข้อความ จึงต้องผ่าน consent/confirmation gate ของแอปก่อนเรียกใช้งาน
- TDLib มี `getSupergroupMembers` และใช้ `supergroup_id`; ข้อมูลจำนวนสมาชิกอาจไม่พร้อมเสมอไป: <https://core.telegram.org/tdlib/docs/functions_vars_s.html>
- Telegram ระบุรูปแบบ `FLOOD_WAIT_X` และให้ client รอตามเวลาที่แจ้ง: <https://core.telegram.org/api/errors>

## OBSERVED จาก implementation นี้

- Native TDLib no-login runtime POC โหลด `tdjson.dll` และเรียก `getOption(version)` ผ่านบน Windows x64
- Local SQLite แยกข้อมูลด้วย `account_id` และ session directory แยกตามบัญชี
- Activity Log redact API hash, OTP, 2FA, session และ DPAPI patterns
- Campaign runner รองรับ consent, opt-out, idempotency และ FloodWait state

## INFERRED / นโยบายผลิตภัณฑ์

- การอ่านสมาชิกต้องรายงานเป็นข้อมูลที่ Telegram ส่งกลับมา ไม่เรียกว่า “สมาชิกทั้งหมด” โดยอัตโนมัติ
- Invite Link และ Join Request เป็นช่องทางหลัก; Direct Invite ต้องมี permission และ consent ที่ตรวจสอบได้
- ระบบไม่ทำ spam, account rotation เพื่อหลบ limit, privacy bypass หรือการขโมยสมาชิก

## UNKNOWN / ต้อง Manual Acceptance

- Authentication ด้วย test account จริง
- ข้อจำกัดเฉพาะบัญชี/กลุ่มและผลของ privacy settings
- การส่งข้อความจริง, Invite Link จริง และ Join Request จริง
- Clean-machine installer, uninstall/upgrade และ restore drill
