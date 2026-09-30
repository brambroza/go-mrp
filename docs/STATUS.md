# สถานะงาน — MRP SaaS

อัปเดต 2026-09-29 (session แรกของการเขียนโค้ด) · ยังไม่มี git commit (repo เพิ่ง `git init` รอเจ้าของสั่ง commit)

## 1. เสร็จและทดสอบแล้ว

ผลทดสอบล่าสุดที่รันจริง: **domain 143 ผ่าน / integration 44 ผ่าน** (integration รันกับ PostgreSQL 17 ใน container โดย API ต่อด้วย role ที่ติด RLS)

| ส่วน | สิ่งที่มี | ทดสอบด้วย |
|---|---|---|
| Monorepo + dev stack | `api/` (.NET 10), pnpm workspace, `docker-compose.yml` (Postgres 17 พอร์ต 5433), `scripts/dev-env.sh`, `.env.example`, `.gitignore` | รัน migrate + API จริงบนเครื่อง |
| SharedKernel | tenant context, `DbSession` (connection/transaction เดียวข้ามโมดูล), RLS bootstrap, state machine, validation filter, error RFC 7807 + `code` | integration |
| Platform | signup/login/refresh (หมุน token + จับ reuse)/logout, lockout, 2FA (TOTP), ผู้ใช้ + role + permission, จำกัดจำนวนผู้ใช้ตามแพ็ก, tenant settings, เลขเอกสารต่อ tenant, approval engine + กล่องอนุมัติ | `PlatformTests`, `ApprovalRequestTests` |
| Masters | หน่วย + แปลงหน่วย, กลุ่ม item, item, คลัง + location, ลูกค้า, supplier, ราคาซื้อเป็นขั้น | `UnitConverterTests`, `SupplierPriceResolverTests` |
| Inventory | รับ / เบิก FIFO-FEFO / โอน / ปรับ / นับ / คืน supplier, QC lot, void ด้วย movement กลับรายการ, ปิด-เปิดงวด + snapshot, on-hand, stock card, preview การตัด lot, ledger append-only (trigger) | `LotAllocatorTests`, `InventoryFlowTests` (รวมเบิกพร้อมกัน 6 รายการไม่ตัดเกิน) |
| Purchasing | PR, PO, ยอดรวม/ส่วนลด/VAT, อนุมัติหลายขั้นตามยอด, PO จาก PR, รับของตาม PO, กันรับเกิน, ปิด/ยกเลิก | `PurchaseTests`, `PurchasingFlowTests` |
| Production (planning) | BOM หลาย version หลายระดับ + กัน BOM วน, explode, where-used, demand, MRP netting (lead time, safety stock, lot size, scheduled receipt), แปลงข้อเสนอเป็น PR/ใบสั่งผลิต, ใบสั่งผลิต + ใบลูกของ semi, ติดตามยอดเบิก/ยอดผลิตจากเอกสารคลัง | `PlanningTests` (golden), `ProductionFlowTests` |
| API client | `packages/api-client` generate จาก OpenAPI (96 path) | `tsc --noEmit` |
| ถอด logic ระบบเดิม | flow 8–12, 17, 18 → `docs/legacy/logic/*.md` | อ่านจากโค้ดจริง มี file:line; สุ่มตรวจบางจุด ยังไม่ได้ตรวจทุก reference |

การแยก tenant ที่ทดสอบแล้ว: ทุกตารางที่มี `tenant_id` เปิด RLS + FORCE + policy (มี test ตรวจทั้งฐานข้อมูล), role ของ API ไม่มีสิทธิ์ bypass,
query ตรงด้วย SQL ข้าม tenant ได้ 0 แถว, insert ข้าม tenant ถูกปฏิเสธ, เอกสาร/lot/PO/BOM ของ tenant อื่นได้ 404

## 2. กำลังทำ (agent ทำงานอยู่ตอนเขียนไฟล์นี้ — ยังไม่ได้ตรวจรับ)

| งาน | โฟลเดอร์ | หมายเหตุ |
|---|---|---|
| Web foundation + auth + masters + อนุมัติ + ตั้งค่า | `web/` | **agent ส่งงานแล้ว ยังไม่ได้ตรวจรับ** — ตามรายงาน: typecheck, lint ผ่าน, unit test 101, build 33 route ผ่าน · **Playwright e2e ยังไม่ได้รันกับ API จริง** (ฐานข้อมูล dev เสียจากดิสก์เต็ม) — signup / login / refresh / ทุกหน้าข้อมูล **ยังไม่เคยทำงานกับ API จริงสำเร็จ** ผ่านแค่กับ mock · หน้า inventory / purchasing / production ยังเป็น placeholder ต้องทำต่อ · agent แก้ `pnpm-workspace.yaml` (ปิด build script ของ 3 package) ต้อง review |
| Mobile: login, อนุมัติ, สแกน, รับ/เบิก/โอน/นับ, offline queue | `mobile/` | **agent ส่งงานแล้ว ยังไม่ได้ตรวจรับ** — ตามรายงาน: typecheck, lint ผ่าน, unit test 180/180, bundle Android + iOS ผ่าน · **ยังไม่เคยรันบนเครื่องจริงหรือ simulator และยังไม่เคย login กับ API จริงสำเร็จ** · กล้อง, SQLite บนเครื่อง, secure store, sync ตอนสัญญาณกลับ ยังไม่ได้ทดสอบ |
| Dockerfile, Terraform GCP, GitHub Actions | `api/Dockerfile`, `infra/`, `.github/` | **agent ส่งงานแล้ว ยังไม่ได้ตรวจรับ** — ตามรายงาน: API image build + migrate + health ผ่าน (arm64/amd64), `terraform validate` ผ่าน, actionlint ผ่าน · **ยังไม่ผ่าน/ไม่ได้ตรวจ:** web image (GET / ได้ 500), `terraform plan/apply` ไม่เคยรันกับ GCP, workflow ไม่เคยรันบน GitHub, script สร้าง role บน Cloud SQL จริง · ยังไม่ได้ deploy และห้าม deploy จนกว่าเจ้าของอนุมัติ |
| ถอด logic flow 7, 13, 14, 15, 16 | `docs/legacy/logic/` | **agent ส่งครบแล้ว (18/18 flow)** ยังไม่ได้สุ่มตรวจ file:line · ข้อค้นพบที่กระทบแผน: (1) SO เดิมเป็นใบสั่งงานภายใน ไม่มีราคา/เครดิต/ยอดส่งมอบ และอนุมัติจริงขั้นเดียว (2) yield, man-hour, ปิดเดือน, ตัดสต็อกขาย, ออก invoice อยู่ใน stored procedure ที่ไม่มีใน repo (3) loss กรอกมือ ไม่มีสูตร (4) ผลผลิตเข้าคลังได้ 4 ทางที่ไม่เชื่อมกัน (5) Lab แยกขาดจาก ERP ไม่มีการแปลงสูตร lab เป็น BOM (6) SO ของ DK.SO กับ LM.SaleVat เป็นคนละระบบ |

## 3. ยังไม่ได้ทำ

- **Security audit และ code review อิสระของ API** — สั่งรันไม่ได้ใน session นี้ ต้องรันก่อนให้ลูกค้า pilot ใช้ (ดูข้อ 5)
- **วัด coverage** — ยังไม่ได้รัน `--collect:"XPlat Code Coverage"` จึงยืนยันเกณฑ์ ≥ 80% ไม่ได้
- Routing / เครื่อง / ปฏิทินกะ / scheduling + Gantt, actual mix/fill + ของเสีย + yield, พาเลทเข้าคลัง + FG approve (เฟส 3 ที่เหลือ)
- SO (เฟส 3) — ตอนนี้ demand ของ MRP กรอกเอง
- Packing / invoice / export บัญชี / billing-subscription (เฟส 4), Lab (Enterprise)
- Hangfire worker (ปิดงวดและ MRP ตอนนี้รันใน request), แจ้งเตือน Expo Push / LINE, ไฟล์แนบ R2, PDF (QuestPDF)
- Playwright flow ของ inventory / purchasing / production บน web
- ขอ script SP/view/function จากระบบเดิม (รายการอยู่ท้ายไฟล์ logic แต่ละไฟล์)

## 4. ข้อสมมติที่ใช้ไปก่อน — รอเจ้าของยืนยัน

| เรื่อง | ค่าที่ใช้ | แก้ได้ที่ |
|---|---|---|
| จำนวนผู้ใช้ต่อแพ็ก | Starter 5 / Pro 15 / Enterprise 30 | `Tenant.DefaultMaxUsers` |
| อนุมัติเอกสารตัวเอง | ห้าม | setting `approval.allowSelfApprove` |
| สต็อกไม่พอ | ปฏิเสธทั้งเอกสาร | — (ระบบเดิมตัดเท่าที่มีแล้วเงียบ) |
| รับของเกิน PO | ห้าม เว้นแต่ตั้ง % | setting `purchasing.overReceivePercent` |
| ลำดับตัด lot | FIFO ตามวันรับ | setting `inventory.issueStrategy` (`Fefo`) |
| รอ QC ตอนรับของ | ปิด | setting `inventory.qcOnReceive` |
| VAT ตั้งต้น | 7% | setting `purchasing.vatPercent` / supplier |
| MRP นับ PR ที่ยังไม่ออก PO เป็น supply | ใช่ (กันเสนอซื้อซ้ำเมื่อรันรอบถัดไป) | `MrpService.BuildInputAsync` |

เรื่องที่ CLAUDE.md ระบุว่ายังไม่ตัดสินใจ (billing provider, รวมกับ PPS/WMS, เก็บข้อมูลในไทย, Enterprise แยก DB) — **ยังไม่ได้ทำอะไรที่ผูกกับคำตอบ**
ยกเว้น Terraform ที่เขียนสำหรับ GCP Singapore ตาม CLAUDE.md

## 5. ความเสี่ยงและสิ่งที่ต้องตรวจก่อนใช้จริง

1. **ยังไม่มี security audit / code review อิสระ** ของ API — ควรรัน `/security-review` และ `/code-review` แล้วแก้ก่อน pilot
2. **Optimistic concurrency (`Version` → `xmin`)** ประกาศไว้บนเอกสารหลัก แต่ยังไม่มี test ที่พิสูจน์ว่าสอง request แก้เอกสารเดียวกันพร้อมกันแล้วตัวหลังได้ 409
   (ยอด lot ไม่ตัดเกินมี test แล้ว แต่ยอดรับของ PO / ยอดเบิกของใบสั่งผลิตพึ่งกลไกนี้)
3. **ตาราง `platform.tenants` ไม่มี RLS** (ตั้งใจ — ใช้หา tenant ตอน login) แต่ role ของ API ยังได้สิทธิ์ UPDATE/DELETE ตารางนี้ ควรลดเหลือ SELECT/INSERT
   และให้การเปลี่ยนแพ็ก/ระงับ tenant ทำผ่าน role แยก
4. **Signup เปิดสาธารณะ** มีแค่ rate limit ต่อ IP — ก่อนเปิดขายต้องมียืนยัน email หรือ captcha
5. **สิทธิ์ฝังใน access token 15 นาที** — ถอนสิทธิ์/ปิดผู้ใช้มีผลเต็มที่หลัง token หมดอายุ (refresh token ถูก revoke ทันที)
6. **Cloud SQL:** ผู้ใช้ที่สร้างผ่าน console/Terraform ได้สิทธิ์ `cloudsqlsuperuser` — ต้องสร้าง `mrp_app` เป็น role ธรรมดาด้วย SQL ไม่งั้น API จะไม่ยอมเริ่มทำงาน (ตั้งใจให้เป็นแบบนั้น)
7. **Mobile offline:** API ยังไม่มี idempotency key ตอนสร้างเอกสาร — ถ้า request timeout แล้วส่งซ้ำอาจได้ draft ซ้ำ (post ซ้ำไม่ได้เพราะสถานะกัน)
8. **Logic ที่อยู่ใน stored procedure ของระบบเดิม** (ปิดเดือน, ยอดคงเหลือ, เลขเอกสาร) อ่านจาก repo ไม่ได้ — กฎในระบบใหม่ออกแบบใหม่ตาม spec
   golden test ตอนนี้เทียบกับค่าที่คำนวณมือ **ยังไม่ได้เทียบกับผลจากระบบเดิม** ตามที่ `docs/plan.md` ข้อ 5 ต้องการ
10. **Data Protection keys เก็บใน container** — บน Cloud Run หายทุกครั้งที่ restart และต่างกันต่อ instance ตอนนี้กระทบน้อย
    (TOTP ไม่ใช้, reset รหัสผ่านโดย admin สร้างและใช้ token ใน request เดียว) แต่ต้อง persist ก่อนทำ "ลืมรหัสผ่าน" ทาง email
11. **URL `*.run.app` เปิดสาธารณะ ข้าม Cloudflare ได้** — ต้องจำกัดก่อนเปิดใช้จริง (ตัวเลือกอยู่ใน `infra/terraform/README.md`)
12. **ค่า cloud 2 environment ประมาณ USD 60–80/เดือน เกินเป้า 35–60** (ตัวเลขประมาณของ agent ยังไม่ได้เทียบ pricing calculator) — ทางลด: staging ใช้ `db-f1-micro` หรือปิดเมื่อไม่ใช้
13. **Runtime image เป็น Alpine (musl)** — QuestPDF ยังไม่ได้ทดสอบบน musl ถ้าใช้ไม่ได้ต้องเปลี่ยน base image
14. **ดิสก์เครื่อง dev เกือบเต็ม** (เหลือ ~2 GB ตอน agent รายงาน, Docker.raw 56/60 GB) — build ล้มเพราะดิสก์เต็มไปแล้ว 1 ครั้ง และ Docker daemon อาจทำงานผิดปกติ
15. **ฐานข้อมูล dev อาจเสียหายจากดิสก์เต็ม** — log ของ API พบ PostgreSQL error `58030: could not open file "global/pg_filenode.map": I/O error`
    ทำให้ API ตอบ 500 (agent mobile เจอตอนทดสอบ login) เป็นปัญหาเครื่อง ไม่ใช่ bug ของโค้ด login · หลังเคลียร์ดิสก์ให้ restart container
    ถ้ายังเสียให้สร้าง volume ใหม่แล้วรัน `migrate` (ข้อมูล dev เป็นข้อมูลทดสอบทั้งหมด)
16. **ช่องว่างของ API ที่ mobile พบ (ยังไม่ได้แก้):** ไม่มี idempotency key, ไม่มี endpoint ลบ draft (draft ค้างจะบล็อกการปิดงวด),
    ผู้ที่มีแค่สิทธิ์ `inventory.receive` เลือก PO ไม่ได้ (ต้องมี `purchasing.read`) และ `/orders/{id}/receivable-lines` ใน spec ยังไม่ได้ทำ,
    `/masters/locations` กรองตามคลังด้วย query เฉพาะไม่ได้, `LotDto` ไม่มีหน่วยนับ, lot ที่มี `/` ในเลขอาจค้นด้วย path ไม่ได้,
    retry ของ `@mrp/api-client` ใช้ไม่ได้กับ request ที่มี body
17. **`.npmrc` `node-linker=hoisted` ไม่มีผลกับ pnpm 11** — ต้องย้ายไปตั้ง `nodeLinker` ใน `pnpm-workspace.yaml` ถ้าต้องการ hoisted
18. **Rate limit ของ `/auth/refresh` ใช้โควตาเดียวกับ login (20 ครั้ง/นาที/IP)** — ทุกครั้งที่โหลดหน้าใหม่ web จะ refresh และโรงงานหลัง NAT เดียวใช้ IP เดียวกัน
    ผู้ใช้จริงไม่กี่คนก็ชนเพดานได้ ต้องแยก policy ของ refresh ก่อน pilot
19. **API เชื่อ `X-Forwarded-For` จากทุกที่** (ล้าง `KnownProxies`) — ปลอม header เพื่อหลบ rate limit ต่อ IP ได้ ต้องจำกัดให้เชื่อเฉพาะ proxy ของเรา
20. **ช่องว่างของ API ที่ web พบ (ยังไม่ได้แก้):** validation error ไม่มี `code` และเป็นภาษาอังกฤษ, `two_factor_invalid` ได้ 401 ตอน login แต่ 400 ตอนเปิด 2FA,
    ไม่มี endpoint บอกประเภทเอกสารที่ตั้ง route อนุมัติได้, `GET /settings` ไม่คืนค่าตั้งต้น, profile ไม่บอกว่า session ผ่าน 2FA หรือยัง,
    ไม่มี endpoint ให้ผู้ใช้แก้ profile / เปลี่ยนรหัสผ่านตัวเอง, กฎรหัสผ่านใน spec (ตัวอักษร + ตัวเลข) ไม่ตรงกับ API (ตัวพิมพ์เล็ก + ตัวเลข)
21. **`infra/docker/web.Dockerfile` อาจชี้ path ผิด** — standalone server ของ web อยู่ที่ `.next/standalone/web/server.js` ยังไม่ได้ตรวจว่า Dockerfile ใช้ path นี้
9. **สิทธิ์ใช้ business logic ของ DK-ERP** ยังเป็นคำถามเปิดใน `docs/plan.md` — ถอดเฉพาะคำอธิบาย logic ไม่ได้ copy โค้ด

## 6. เทียบกับ roadmap

| เฟส | สถานะ |
|---|---|
| 0 ถอด logic + ออกแบบ | ถอดแล้ว 13/18 flow (อีก 5 กำลังทำ), schema Core + Starter + Pro(planning) มี migration แล้ว, scaffold แล้ว, Terraform/CI กำลังทำ, **ยังไม่ deploy** |
| 1 Platform core | API เสร็จ · web กำลังทำ · mobile login กำลังทำ |
| 2 Starter | API เสร็จ · หน้าจอ web ยังไม่ทำ · mobile กำลังทำ |
| 3 Pro | API: BOM / MRP / ใบสั่งผลิต เสร็จ · SO, scheduling, actual, FG ยังไม่ทำ |
| 4 ขาย/invoice + billing | ยังไม่เริ่ม |

ตัวเลขข้างบนคือ "โค้ดมีและ test ผ่าน" ไม่ใช่ "พร้อมให้ลูกค้าใช้" — ยังไม่มี UAT, ยังไม่มี staging, ยังไม่มีคู่มือผู้ใช้
