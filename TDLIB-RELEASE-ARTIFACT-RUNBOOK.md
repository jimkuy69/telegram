# TDLib Release Artifact Runbook

เอกสารนี้ใช้ตรวจ TDLib Artifact ก่อนสร้าง Installer เท่านั้น และไม่ใช่หลักฐานการ Login Telegram จริง

## 1. สร้าง Artifact

1. เปิด GitHub Actions ของ repository
2. เลือก workflow **Build TDLib native Windows x64**
3. กด **Run workflow** ที่ branch `main`
4. รอให้ทุก job เป็นสีเขียว
5. เปิด workflow run ที่สำเร็จ และดาวน์โหลด Artifact ชื่อ `TelegramControlStudio-tdlib-win-x64-<version>`

ห้ามนำ `tdjson.dll` จากแหล่งอื่นมาแทน Artifact ที่ผ่าน workflow นี้

## 2. ตรวจไฟล์ที่ต้องมี

แตกไฟล์ Artifact ไปยังโฟลเดอร์ชั่วคราวที่อยู่นอกโฟลเดอร์ติดตั้ง แล้วตรวจว่ามี:

- `tdjson.dll`
- `tdjson.manifest.json`
- DLL runtime ที่มากับ Artifact
- ค่า `architecture` เป็น `win-x64`
- ค่า `sourceUrl`, `version`, `license`, และ `sha256` ไม่ว่าง

## 3. ตรวจ Provenance และ Hash

จาก root ของ repository ที่ checkout ไว้ ให้รัน:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/verify-tdlib-native.ps1 `
  -Root C:\path\to\artifact-root `
  -BinaryRelativePath "tdlib\tdjson.dll" `
  -ManifestRelativePath "tdlib\tdjson.manifest.json"
```

ผลที่ยอมรับได้คือ:

```
PASS: TDLib native dependency verified
```

## 4. ตรวจ Runtime

วาง Artifact ตาม layout ที่สคริปต์ Runtime Gate คาดหวัง แล้วรัน:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/verify-tdlib-runtime.ps1
```

ผลที่ยอมรับได้ต้องมีทั้ง:

- Native library โหลดได้บน Windows x64
- no-login POC เรียก `getOption(version)` สำเร็จ
- ไม่มี OTP, 2FA, หรือ Telegram account ถูกใช้

## 5. บันทึกหลักฐาน

เก็บข้อมูลต่อไปนี้ไว้กับ Release evidence:

- Workflow run URL
- Commit SHA ของ source
- Artifact name
- TDLib version
- `tdjson.dll` SHA-256
- ผลลัพธ์ของทั้งสอง verifier
- วันที่และเครื่อง Windows ที่ตรวจ

ห้ามติ๊ก Runtime Gate หรือ Installer Gate จากการมีไฟล์เพียงอย่างเดียว และห้ามสรุปว่า Telegram End-to-End ผ่านจาก no-login POC
