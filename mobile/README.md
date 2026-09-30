# @mrp/mobile — แอปมือถือสำหรับงานหน้างานและอนุมัติ

แอป Expo (React Native) + TypeScript ของระบบ MRP SaaS ใช้กับงานที่ `docs/plan.md` กำหนดให้ทำบนมือถือ:
กล่องอนุมัติ, รับของตาม PO, เบิกของ (FIFO), โอนย้าย, นับสต็อก, ค้นหา lot และคิวออฟไลน์

> สถานะ: ผ่าน typecheck / lint / unit test / `expo export` (Android + iOS) / `expo-doctor` แล้ว
> แต่ **ยังไม่เคยรันบนเครื่องจริงหรือ simulator** — ดูหัวข้อ "ข้อจำกัดที่ทราบ" ก่อนนำไป demo

## สิ่งที่ต้องมี

- Node.js 22.5 ขึ้นไป (unit test ของคิวใช้ `node:sqlite`) — เครื่อง dev ใช้ v24
- pnpm 11 (ติดตั้ง dependency จาก root ของ repo เท่านั้น)
- Expo SDK 57 (React Native 0.86, React 19.2)
- มือถือที่ลง **development build** ของแอป หรือ Android emulator / iOS simulator
  - แอปใช้ native module (`expo-camera`, `expo-sqlite`, `expo-secure-store`) — ถ้า Expo Go รุ่นที่ติดตั้งไม่ตรงกับ SDK 57 ให้สร้าง development build ด้วย `npx expo run:android` หรือ `npx expo run:ios`

## ติดตั้งและรัน

```bash
# 1) ติดตั้ง dependency (รันที่ root ของ repo)
cd /path/to/mrp
pnpm install

# 2) ตั้งค่า environment
cp mobile/.env.example mobile/.env.local
# แก้ EXPO_PUBLIC_API_BASE_URL ให้ชี้ไปที่เครื่องที่รัน API (ดูหัวข้อถัดไป)

# 3) รัน Metro
pnpm mobile:start          # เท่ากับ pnpm --filter @mrp/mobile start
```

### รันบนเครื่องจริงกับ API ในเครื่อง dev (ใช้ LAN IP ไม่ใช่ localhost)

`localhost` บนมือถือหมายถึงตัวมือถือเอง จึงต้องใช้ IP ในวง LAN ของเครื่องที่รัน API

1. มือถือกับเครื่อง dev ต้องอยู่ Wi-Fi วงเดียวกัน
2. หา IP ของเครื่อง dev: macOS `ipconfig getifaddr en0` (เช่น `192.168.1.10`)
3. ให้ API รับ connection จากเครื่องอื่น ไม่ใช่แค่ loopback เช่น
   `ASPNETCORE_URLS=http://0.0.0.0:5080 dotnet run --project api/src/Mrp.Api` และอนุญาต port 5080 ใน firewall
4. ตั้งค่าใน `mobile/.env.local`: `EXPO_PUBLIC_API_BASE_URL=http://192.168.1.10:5080`
5. หยุดแล้วรัน `pnpm mobile:start` ใหม่ (ค่า `EXPO_PUBLIC_*` ถูกฝังตอน bundle) แล้วสแกน QR จาก terminal

กรณีอื่น: Android emulator ใช้ `http://10.0.2.2:5080`, iOS simulator ใช้ `http://localhost:5080` ได้

## Environment variables

| ตัวแปร | จำเป็น | คำอธิบาย |
|---|---|---|
| `EXPO_PUBLIC_API_BASE_URL` | ใช่ | URL ของ API host ไม่ต้องมี `/api/v1` ต่อท้าย |
| `EXPO_PUBLIC_USE_RN_FETCH` | ไม่ | ตัวแปรของ Expo เอง: ตั้งเป็น `1` เพื่อใช้ `fetch` ของ React Native แทนของ Expo (ใช้ตอนรัน `test:live`) |

กฎของ `EXPO_PUBLIC_API_BASE_URL` (ตรวจใน `src/config/env.ts`):

- host ที่ไม่ใช่เครื่อง local / private network **ต้องเป็น HTTPS** ไม่เช่นนั้นหน้า login จะแสดงข้อผิดพลาดและกดเข้าสู่ระบบไม่ได้
- HTTP ใช้ได้เฉพาะ `localhost`, `127.x`, `10.x`, `172.16-31.x`, `192.168.x`, `169.254.x`, `*.local`
- ห้ามมี user/password, query string หรือ fragment ใน URL
- ค่า `EXPO_PUBLIC_*` ถูกฝังใน bundle ของแอป **ห้ามใส่ secret**

การตั้งค่า native สำหรับ HTTP ตอน dev: build แบบ release ของ Android/iOS บล็อก HTTP (cleartext) เป็นค่าตั้งต้น ซึ่งถูกต้องสำหรับ production — development build อนุญาตให้ใช้ HTTP ได้

## คำสั่ง

| คำสั่ง (รันจาก root) | ทำอะไร |
|---|---|
| `pnpm --filter @mrp/mobile typecheck` | `tsc --noEmit` (strict + `noUncheckedIndexedAccess`) |
| `pnpm --filter @mrp/mobile lint` | ESLint (`eslint-config-expo`) ไม่ยอมให้มี warning, ห้าม `console.*` |
| `pnpm --filter @mrp/mobile test` | Jest (`jest-expo`) |
| `pnpm --filter @mrp/mobile export` | bundle สำหรับ Android + iOS ด้วย Metro + Hermes ลง `mobile/dist` (พิสูจน์ว่า bundle ได้โดยไม่ต้องมี simulator) |
| `pnpm --filter @mrp/mobile doctor` | `expo-doctor` |
| `pnpm --filter @mrp/mobile test:live` | smoke test กับ API ที่รันอยู่จริง (ดูด้านล่าง) |

### smoke test กับ API จริง (`test:live`)

```bash
EXPO_PUBLIC_USE_RN_FETCH=1 EXPO_PUBLIC_API_BASE_URL=http://localhost:5080 \
  pnpm --filter @mrp/mobile test:live
```

ถ้าไม่ตั้ง `MRP_LIVE_COMPANY` / `MRP_LIVE_USER` / `MRP_LIVE_PASSWORD` จะทดสอบเฉพาะกรณี error (ไม่เขียนข้อมูลลงฐานข้อมูล)
ถ้าตั้งครบจะทดสอบ login → refresh token (rotation + single-flight) → อ่าน list → logout ด้วย ให้ใช้ user ของ tenant ทดสอบเท่านั้น

## โครงสร้าง

```
mobile/
  app/                         หน้าจอ (Expo Router)
    _layout.tsx                โหลด font, ภาษา, session แล้วกั้นหน้าด้วย Stack.Protected
    login.tsx                  เข้าสู่ระบบ + ขั้น 2FA
    (app)/                     เข้าได้เมื่อ login แล้วเท่านั้น
      index.tsx                หน้าหลัก (tile ตามสิทธิ์ + badge คิว)
      approvals/               กล่องอนุมัติ, รายละเอียด + ประวัติ, อนุมัติ/ไม่อนุมัติ
      scanner.tsx              สแกนบาร์โค้ด/QR (ไฟฉาย, พิมพ์รหัสเอง)
      lot-lookup.tsx           ค้นหา lot
      receive/                 รับของตาม PO
      issue.tsx, transfer.tsx  เบิก / โอนย้าย (ใช้ฟอร์มเดียวกัน)
      count.tsx                นับสต็อก
      queue/                   คิวออฟไลน์
      settings.tsx             ภาษา, ธีม, ออกจากระบบ
  src/
    api/                       เรียก API ผ่าน @mrp/api-client ทุกครั้ง
    auth/                      TokenManager (single-flight refresh), session
    offline/                   คิวออฟไลน์: state machine, SQLite store, sender, sync
    lib/                       วันที่ (ISO / พ.ศ.), จำนวน (ทศนิยม 6 ตำแหน่ง), สิทธิ์, error
    validation/                zod schema (limit เท่ากับ Contracts.cs ของ API)
    i18n/                      th (ค่าตั้งต้น), en — ข้อความ UI ทั้งหมดอยู่ใน locales/
    theme/, ui/                design system: ปุ่มสูง 56dp ขึ้นไป, contrast 4.5:1, font Sarabun, light/dark
    features/                  hook และ component ที่ใช้ร่วมกันของแต่ละ flow
    live/                      smoke test กับ API จริง (ไม่อยู่ใน `pnpm test`)
```

## ความปลอดภัย

- **Refresh token** เก็บใน `expo-secure-store` เท่านั้น (iOS Keychain / Android Keystore, `AFTER_FIRST_UNLOCK_THIS_DEVICE_ONLY`)
- **Access token** อยู่ใน memory เท่านั้น ไม่เขียนลงดิสก์ และไม่มีการ log (ESLint ห้าม `console.*`)
- API หมุน refresh token ทุกครั้งที่ใช้ และถ้าใช้ token เก่าซ้ำจะ revoke ทุก session ดังนั้น `TokenManager`
  - refresh แบบ single-flight: หลาย request ที่เรียกพร้อมกันใช้ request เดียว
  - เขียน refresh token ใหม่ลง secure store **ให้เสร็จก่อน** จึงคืน access token ให้ผู้เรียก
- ออกจากระบบ: revoke refresh token ที่ server (best effort), ลบ token, ลบ profile ที่ cache ไว้ และล้าง cache ของ TanStack Query
- จำเฉพาะรหัสบริษัทและชื่อผู้ใช้ **ไม่จำรหัสผ่าน**
- ทุก input ตรวจด้วย zod ก่อนส่ง; ข้อความจากเครื่องสแกนถูกตัดอักขระควบคุมและจำกัด 200 ตัวอักษร
- สิทธิ์ที่กรองบนมือถือ (tile, หน้าจอ) เป็นแค่ UX — ผู้บังคับใช้สิทธิ์จริงคือ API

## คิวออฟไลน์ทำงานอย่างไร

เอกสารคลังทุกใบ (รับ / เบิก / โอน / นับ) ถูกบันทึกลงคิวใน SQLite (`mrp-offline-queue.db`, ตาราง `offline_queue`) ก่อนเสมอ
แล้วจึงส่ง ถ้าออนไลน์จะส่งทันที ถ้าออฟไลน์หรือส่งไม่ถึงจะรอในคิว

### สถานะ

| สถานะ | ความหมาย | ระบบทำอะไรต่อ |
|---|---|---|
| `pending` (รอส่ง) | รอรอบ sync ถัดไป | ส่งเองเมื่อสัญญาณกลับมา / เปิดแอป / กด "ส่งเอกสารที่รอ" |
| `sending` (กำลังส่ง) | กำลังส่งอยู่ | ถ้าแอปถูกปิดกลางคัน รอบ sync ถัดไปจะคืนเป็น `pending` |
| `failed` (ส่งไม่สำเร็จ) | API ปฏิเสธ พร้อม error code/ข้อความ | **ไม่ส่งซ้ำเอง** ผู้ใช้ต้องแก้ไขแล้วกด "ส่งอีกครั้ง" หรือยกเลิกเอกสาร |
| `sent` (ส่งแล้ว) | API รับแล้ว: `Posted` หรือ `Submitted` (= **รออนุมัติ** สต็อกยังไม่เปลี่ยน) | เก็บเป็นประวัติ 7 วันแล้วลบออกจากเครื่อง |

### กติกา retry

| ผลที่ได้ | สถานะถัดไป | หยุดรอบ sync ไหม |
|---|---|---|
| ไม่มีสัญญาณ / timeout (20 วินาที) | `pending` ไม่จำกัดจำนวนครั้ง | หยุด |
| 401 หลัง refresh ไม่ผ่าน (session หมดอายุ) | `pending` รอผู้ใช้ login ใหม่ | หยุด |
| 5xx, 408, 429 | `pending` จนครบ 5 ครั้ง แล้วเป็น `failed` | ไม่หยุด ทำใบถัดไปต่อ |
| 4xx อื่น ๆ (กฎธุรกิจ, validation, ไม่มีสิทธิ์) | `failed` ทันที ไม่ retry อัตโนมัติ | ไม่หยุด |

### ขอบเขตของคิว (tenant + user)

ทุกคำสั่ง SQL กรองด้วย `tenant_id` และ `user_id` ของผู้ที่ login อยู่ และ bind ค่าเป็น parameter ทั้งหมด
ผู้ใช้อีกคนที่ login บนเครื่องเดียวกันจะ **ไม่เห็นและไม่ส่ง** เอกสารของคนก่อน เอกสารของคนก่อนยังอยู่ในเครื่อง
และจะส่งต่อได้เมื่อเจ้าของ login อีกครั้ง คิวเก็บเฉพาะข้อมูลเอกสาร ไม่มี token

### การกันเอกสารซ้ำ

API ยังไม่มี idempotency key ดังนั้น request ที่ timeout อาจสำเร็จที่ server ไปแล้ว แอปจึงส่งเป็นขั้นที่ทำซ้ำได้อย่างปลอดภัย:

1. **สร้างฉบับร่าง** (`POST /inventory/documents`) แล้วบันทึก id ของ server ลงคิวทันที ก่อนทำขั้นถัดไป
2. ถ้าผู้ใช้แก้ไขเอกสารในคิว จะ **อัปเดตฉบับร่างเดิม** (`PUT /documents/{id}`) ไม่สร้างใบใหม่
3. **Post** (`POST /documents/{id}/post`) — ถ้าได้ 409 แปลว่าครั้งก่อนอาจสำเร็จแล้ว แอปจะอ่านเอกสารอีกครั้ง
   ถ้าสถานะเป็น `Posted` / `Submitted` / `Approved` ถือว่าสำเร็จ
4. ถ้าคำตอบของขั้น "สร้างฉบับร่าง" หาย: ก่อนสร้างใหม่ แอปจะค้นหาฉบับร่างเดิมจาก tag `#m:<id ของคิว>`
   ที่แอปใส่ท้ายช่องหมายเหตุ (ค้นจากประเภท + วันที่เอกสาร + สถานะ Draft) แล้วใช้ใบเดิมต่อ

ความเสี่ยงที่ยังเหลือ (ต้องแก้ที่ API ด้วย idempotency key) อยู่ในหัวข้อถัดไป

## ข้อจำกัดที่ทราบ

1. **ยังไม่ได้ทดสอบบนเครื่องจริง/simulator** — flow ของหน้าจอทั้งหมด (login, สแกน, กล้อง, ไฟฉาย, secure store, SQLite บนเครื่อง,
   การตรวจสัญญาณ, sync ตอนกลับมา foreground) ตรวจด้วย unit test, type check และการ bundle เท่านั้น ยังไม่มี E2E test
2. **ฉบับร่างค้างบน server ได้** ในกรณี: (ก) ผู้ใช้ยกเลิกเอกสารในคิวหลังสร้างฉบับร่างแล้ว — API ยังไม่มี endpoint ลบฉบับร่าง
   (ข) คำตอบของการสร้างฉบับร่างหาย และผู้ใช้ไม่มีสิทธิ์ `inventory.read` จึงค้นหาใบเดิมไม่ได้ แอปจะสร้างใบใหม่
   ฉบับร่างไม่ตัดสต็อก แต่จะบล็อกการปิดงวดจนกว่าจะถูกจัดการทางเว็บ
3. **การค้นหาฉบับร่างเดิมอ่านได้สูงสุด 600 ใบ** ต่อประเภทต่อวัน (3 หน้า × 200)
4. **ข้อมูลในคิวไม่ได้เข้ารหัสเพิ่ม** อาศัยการเข้ารหัสของระบบปฏิบัติการและ sandbox ของแอป (เก็บรหัสสินค้า จำนวน lot ไม่มี token)
5. **ออฟไลน์ทำได้เฉพาะการบันทึก/ส่งเอกสาร** การค้นหา PO, สินค้า, lot, คำแนะนำ FIFO, การสร้างใบนับ และกล่องอนุมัติ ต้องออนไลน์
   (ข้อมูลที่โหลดไว้แล้วในหน้าจอยังใช้ต่อได้) ยังไม่มีการ cache master data ลงเครื่อง
6. **แก้ไขเอกสารในคิวได้เฉพาะจำนวนและลบรายการ** ใบนับสต็อกในคิวแก้ไม่ได้ (ส่งอีกครั้งหรือยกเลิกเท่านั้น)
7. **lot ที่สแกนไม่แสดงหน่วยนับ** เพราะ `LotDto` ของ API ไม่มีหน่วย
8. **จำนวนส่งเป็น JSON number** จำนวนที่มีเลขนัยสำคัญเกิน ~15 หลัก (เช่น 99,999,999,999.123456) ถูกปฏิเสธที่แอปเพื่อไม่ให้ค่าเพี้ยน
9. **ใบนับแสดงยอดระบบ** (ไม่ใช่ blind count) และต้องกรอกยอดนับครบทุกรายการก่อนส่ง
10. ยังไม่มี push notification, ตั้งค่า 2FA, QC lot, คืนผู้ขาย, บันทึกผลผลิต (นอกขอบเขตรอบนี้)
11. ยังไม่มี icon / splash ของแอป และยังไม่ได้ตั้งค่า EAS Build
12. **ไม่รองรับ web** — `expo export --platform web` ล้มเหลวเพราะ `expo-sqlite` บน web ต้องตั้งค่า wasm เพิ่ม และแอปนี้เป็นงานหน้างานบนมือถือเท่านั้น
13. workspace ติดตั้งแบบ isolated (symlink) เพราะ pnpm 11 ไม่อ่าน `node-linker=hoisted` จาก `.npmrc` ที่ root —
    Metro และ `expo export` ทำงานได้กับแบบนี้ (ดู `metro.config.js`) ถ้าจะเปลี่ยนเป็น hoisted ต้องตั้ง `nodeLinker: hoisted` ใน `pnpm-workspace.yaml`
