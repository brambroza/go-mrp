# MRP SaaS — โปรเจกต์ใหม่ (ต่อยอด logic จาก DK-ERP-SYSTEM)

> อ่านไฟล์นี้ก่อนทำงานทุกครั้ง แล้วอ่าน `docs/plan.md` และ `docs/legacy/README.md`

## เป้าหมาย

สร้าง ERP สำหรับโรงงาน SME ไทยเป็น **SaaS multi-tenant** ขายแบบ subscription
มี **web app** (งานออฟฟิศ) และ **mobile app** (งานหน้างาน + อนุมัติ) ใช้งานง่าย ทันสมัย
deploy บน cloud ราคาถูก scale ได้

**กติกาสำคัญ:**
- นี่คือโปรเจกต์ใหม่ **ไม่แตะโค้ดหรือฐานข้อมูลของ DK-ERP-SYSTEM เลย** ไม่ import ไม่เชื่อมต่อ ไม่ copy โค้ด VB
- เอามาเฉพาะ **business logic** ที่ถอดเป็น spec ไว้ใน `docs/legacy/` แล้วเขียนใหม่ทั้งหมด
- สิ่งที่เป็นของโรงงาน DK โดยเฉพาะ (mix/fill, Bulk/PK, อนุมัติ 2 ขั้น, ปฏิทิน 2 กะ) ต้องเป็น **ค่าตั้งต่อ tenant** ไม่ hard-code
- ไม่ยกข้อบกพร่องของระบบเดิมมาด้วย (ดู `docs/legacy/logic/mrp-planning.md` หัวข้อ "Defects")

## Tech stack (ตัดสินใจแล้ว)

| ชั้น | เลือก |
|---|---|
| API | .NET 10 ASP.NET Core, modular monolith (แยก project ต่อโมดูล), EF Core + Npgsql, Dapper สำหรับรายงาน |
| DB | PostgreSQL 17 — 1 database, schema ต่อโมดูล, ทุกตารางมี `tenant_id`, บังคับ Row-Level Security |
| Web | Next.js (App Router) + TypeScript + Tailwind + shadcn/ui + TanStack Query/Table |
| Mobile | Expo (React Native) + TypeScript, สแกนบาร์โค้ดด้วยกล้อง, offline queue (SQLite) |
| Shared | OpenAPI → generate TS client ใน `packages/api-client` |
| Auth | ASP.NET Core Identity + JWT/refresh, 2FA สำหรับผู้อนุมัติ |
| Jobs | Hangfire (storage = Postgres) |
| ไฟล์/PDF | Cloudflare R2, QuestPDF |
| แจ้งเตือน | Expo Push, LINE Messaging API |
| Cloud | GCP Singapore: Cloud Run (api/web/worker) + Cloud SQL Postgres, Cloudflare หน้าบ้าน, Terraform ใน `infra/` |
| CI/CD | GitHub Actions → Artifact Registry → Cloud Run |

## โครง repo

```
mrp/
  CLAUDE.md
  docs/
    plan.md                 แผนธุรกิจ + roadmap + แพ็กเกจ + cloud
    legacy/                 สิ่งที่ถอดจาก DK-ERP (อ่านอย่างเดียว ห้ามเชื่อมต่อ)
      README.md             สถานะการถอด logic ต่อ flow (อะไรถอดแล้ว/ยัง)
      module-map.md         โมดูลเดิม → โมดูลใหม่ → web/mobile
      form-inventory.md     รายชื่อหน้าจอเดิม ~465 ฟอร์ม ใช้เป็น checklist ความครบ
      logic/mrp-planning.md spec BOM / MRP / ใบสั่งผลิต / scheduling (ถอดแล้ว)
    specs/                  spec ของระบบใหม่ เขียนต่อ flow ก่อนเขียนโค้ด
  api/          .NET 10
  web/          Next.js
  mobile/       Expo
  packages/api-client/
  infra/        Terraform + Dockerfile
```

## วิธีทำงาน (ทุก feature)

1. อ่าน `docs/legacy/logic/<flow>.md` (ถ้ายังไม่มี ให้ถอดจาก DK-ERP ก่อน — ดู `docs/legacy/README.md` ว่า clone อยู่ที่ไหน)
2. เขียน `docs/specs/<flow>.md` ของระบบใหม่: entity, state machine, กฎคำนวณ, API, หน้าจอ web/mobile, สิ่งที่ทำเป็น tenant setting
3. เขียน migration + domain + API + test (golden test สำหรับสูตรคำนวณ: FIFO, MRP netting, yield)
4. ทำ UI web แล้วค่อย mobile
5. อัปเดต `docs/legacy/README.md` ให้ flow นั้นเป็น "done"

## กฎโค้ด

- ทุก query ต้องผ่าน tenant filter (EF global query filter + RLS `SET app.tenant_id`) — ห้าม query ข้าม tenant
- ห้าม SQL string concat, ใช้ parameter เสมอ
- เลขเอกสารออกจาก sequence ต่อ tenant ต่อประเภท ภายใน transaction (แทน `SP_GEN_DOCUMENTNO`)
- สต็อกทุกการเคลื่อนไหวเป็น ledger append-only (`inventory.movements`) ยอดคงเหลือคำนวณ/cache จาก ledger ห้าม update ยอดตรง
- สถานะเอกสารเป็น state machine ชัดเจน (`Draft → Submitted → Approved → …`) ขั้นอนุมัติเป็น config ต่อ tenant
- ภาษา UI: TH เป็นหลัก EN รอง เก็บใน i18n ไม่ hard-code
- เงิน: `numeric(18,4)`, ปริมาณ: `numeric(18,6)`, วันเวลา: `timestamptz` เก็บ UTC แสดง Asia/Bangkok
- test: xUnit สำหรับ domain, Playwright สำหรับ web flow หลัก
- commit message ภาษาอังกฤษ, conventional commits

## ลำดับงาน (ดู roadmap ใน docs/plan.md)

- **เฟส 0 — ถอด logic + ออกแบบ** (ปัจจุบัน)
  - [x] ถอด BOM / MRP / ใบสั่งผลิต / scheduling → `docs/legacy/logic/mrp-planning.md`
  - [ ] ถอด inventory: รับ/เบิก FIFO ตามบาร์โค้ด/โอน/นับ/QC/ปิดเดือน
  - [ ] ถอด SO + อนุมัติ + PR/PO + รับของ
  - [ ] ถอด production actual (mix/fill, ของเสีย, man-hour, yield) + พาเลทเข้าคลัง + FG approve
  - [ ] ถอด packing / invoice / commission (LM.SaleVat) และ Lab R&D (ถ้าจะทำ Enterprise)
  - [ ] ขอ script SP/view/function ที่ไม่มีใน repo (รายการอยู่ท้าย mrp-planning.md)
  - [ ] ออกแบบ schema Postgres สำหรับ Core + Starter
  - [ ] scaffold monorepo, Terraform GCP, CI/CD, deploy hello-tenant
- **เฟส 1 — Platform core:** tenant, user/role, master, กล่องอนุมัติ, i18n, mobile login
- **เฟส 2 — Starter:** PR/PO, คลังบาร์โค้ด, stock card, ปิดเดือน → pilot
- **เฟส 3 — Pro:** SO, BOM/MRP, PD, scheduling, actual, FG
- **เฟส 4 — ขาย/invoice + billing/subscription**

## แพ็กเกจ (draft)

Core (ทุกแพ็ก) · Starter = คลัง+จัดซื้อ · Pro = +ผลิต/MRP · Enterprise = +ขาย/invoice, Lab, API, DB แยก
ราคาตั้งต้น 2,900 / 6,900 / 15,000 บาท/เดือน — ยังไม่ validate

## สิ่งที่ยังไม่ตัดสินใจ (ถามเจ้าของก่อนทำ)

- Billing provider: Stripe หรือ Opn
- จะรวมกับ GoAlong PPS / WMS เดิมเป็นแพลตฟอร์มเดียวไหม
- ลูกค้าต้องเก็บข้อมูลในไทยไหม (ถ้าใช่ → AWS Bangkok แทน GCP Singapore)
- Enterprise ใช้ DB แยก instance หรือแค่ schema แยก
