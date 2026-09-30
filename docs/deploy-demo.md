# แผน deploy สำหรับ demo ให้ลูกค้าดู

เขียน 2026-09-29 · สถานะ: **แผน ยังไม่ได้ทำ** · ต้องได้รับอนุมัติจากเจ้าของก่อนเริ่ม deploy (กฎใน CLAUDE.md)

เป้าหมาย: ลูกค้าเปิดลิงก์แล้วลองใช้ flow หลักได้เอง ด้วยข้อมูลตัวอย่าง ไม่ใช่ข้อมูลจริงของลูกค้า
นี่คือ environment สำหรับ demo เท่านั้น **ห้ามใช้เก็บข้อมูลจริงของลูกค้า** (ไม่มี backup ที่รับประกัน ไม่มี SLA)

## ทางเลือกที่เสนอ: Cloud Run + Neon (ฟรี)

| ส่วน | บริการ | หมายเหตุ |
|---|---|---|
| API (.NET 10 container) | Google Cloud Run, asia-southeast1 | min instance 0 → คำขอแรกหลังหลับช้า |
| Web (Next.js standalone) | Google Cloud Run | เรียก API ผ่าน proxy ของ web เอง |
| ฐานข้อมูล | Neon (PostgreSQL 17) region Singapore | ต้องสร้าง role `mrp_owner` และ `mrp_app` เอง |
| Mobile | Expo Go หรือ EAS internal build | ชี้ไปที่ URL ของ API demo |

เงื่อนไข free tier ของทุกเจ้า **ยังไม่ได้ตรวจกับหน้าราคาปัจจุบัน** ต้องตรวจก่อนเริ่ม

สิ่งที่ต่างจาก Terraform ใน `infra/terraform`: ไม่สร้าง Cloud SQL และ VPC ใช้ connection string ของ Neon ใน Secret Manager แทน
ทำเป็น tfvars แยก (`envs/demo.tfvars`) พร้อมตัวแปรปิดส่วนฐานข้อมูล หรือ deploy ด้วย `gcloud run deploy` ตรงๆ สำหรับ demo

## ต้องเสร็จก่อน deploy (เรียงตามลำดับ)

| # | งาน | เหตุผล |
|---|---|---|
| 1 | กู้เครื่อง dev: ตรวจดิสก์, restart Docker, สร้างฐานข้อมูล dev ใหม่ถ้าเสีย | ตอนนี้รัน test ไม่ได้ |
| 2 | รัน `dotnet test` ยืนยัน 143 + 44 ผ่าน | ยืนยันว่าไม่มีอะไรพังระหว่างดิสก์เต็ม |
| 3 | รัน e2e ของ web กับ API จริง แก้จนผ่าน | web ยังไม่เคย login กับ API จริงสำเร็จ |
| 4 | แก้ rate limit: แยก policy ของ `/auth/refresh`, เชื่อ `X-Forwarded-For` เฉพาะ proxy ที่กำหนด | ลูกค้าหลายคนเปิดพร้อมกันจากออฟฟิศเดียวจะโดนบล็อก |
| 5 | ปิด signup สาธารณะบน demo (ตัวแปร config) | กันคนนอกสร้างบริษัทบน demo |
| 6 | Security review + code review แล้วแก้ข้อที่ร้ายแรง | ลิงก์ demo อยู่บน internet |
| 7 | ทำหน้า web ของ flow ที่จะ demo (ดูหัวข้อถัดไป) | ตอนนี้จัดซื้อ / คลัง / ผลิต เป็นหน้าว่าง |
| 8 | Script สร้างข้อมูลตัวอย่าง (บริษัท demo, สินค้า, BOM, สต็อก, PO) | ลูกค้าต้องเห็นระบบที่มีข้อมูล |
| 9 | แก้ `infra/docker/web.Dockerfile` ให้ build ผ่านและเปิดหน้าแรกได้ | รอบก่อนได้ HTTP 500 |
| 10 | ทดสอบสร้าง role บน Neon และรัน `migrate` | ยังไม่เคยทดสอบกับ Neon |
| 11 | `git commit` + สร้าง repository บน GitHub (private) | ต้องได้รับอนุมัติก่อน push |

## ขอบเขต demo ที่เสนอ

ทำหน้า web ให้ครบเฉพาะ flow ที่เล่าเรื่องได้จบใน 15 นาที แทนการทำทั้ง 22 หน้า:

**Flow A — จัดซื้อถึงรับของ (แพ็ก Starter)**
ใบขอซื้อ → ใบสั่งซื้อ → อนุมัติ 2 ขั้น (บนมือถือ) → รับของตาม PO → lot เข้าสต็อก → stock card

**Flow B — วางแผนผลิต (แพ็ก Pro)**
ความต้องการ → รัน MRP → เห็นของขาดและของที่ต้องสั่งล่าช้า → กดสร้าง PR และใบสั่งผลิต → เบิกวัตถุดิบ FIFO → รับผลผลิต

หน้า web ที่ต้องทำสำหรับ 2 flow นี้: ใบขอซื้อ, ใบสั่งซื้อ (list + รายละเอียด), เอกสารคลัง (list), รับของ, เบิก, สต็อกคงเหลือ, stock card,
ความต้องการ, ผล MRP, ใบสั่งผลิต (list + รายละเอียด), BOM (อ่านอย่างเดียว) — รวม 13 หน้า
ที่เหลือ (โอน, ปรับ, นับ, QC, ปิดงวด, แก้ BOM) ทำหลัง demo

แบบหน้าจออยู่ใน artifact "MRP SaaS UI Preview"

## สิ่งที่ต้องบอกลูกค้าตรงๆ ตอน demo

- ยังไม่มี: ใบสั่งขาย, ตารางเครื่อง / Gantt, บันทึกผลผลิตหน้าไลน์, อนุมัติ FG, invoice
- คำขอแรกหลังระบบหลับจะช้า (ข้อจำกัดของ environment ฟรี ไม่ใช่ของระบบจริง)
- ข้อมูลบน demo อาจถูกล้างได้ทุกเมื่อ

## ขั้นตอน deploy (หลังงานข้างบนเสร็จและได้รับอนุมัติ)

1. สร้าง project บน Neon → สร้าง role ด้วย `infra/terraform/sql/01-roles.sql` → เก็บ connection string 2 ชุด
2. สร้าง GCP project → เปิด Cloud Run, Artifact Registry, Secret Manager → ตั้ง budget alert ที่ USD 5
3. ใส่ secret: connection string ของ `mrp_app`, ของ `mrp_owner`, JWT signing key (สุ่ม 64 ตัว)
4. Build และ push image ของ API และ web
5. รัน Cloud Run job `migrate` รอจนสำเร็จ
6. Deploy API → ตรวจ `/healthz` → deploy web โดยตั้ง `API_BASE_URL` เป็น URL ของ API
7. ตั้ง `Cors__Origins__0` ของ API เป็น URL ของ web
8. รัน script ข้อมูลตัวอย่าง
9. ทดสอบ flow A และ B บน URL จริงด้วยมือ ทั้ง web และ mobile ก่อนส่งลิงก์ให้ลูกค้า

## Rollback

Cloud Run เก็บ revision เดิมไว้ ย้าย traffic กลับได้ทันที ฐานข้อมูล demo สร้างใหม่จาก `migrate` + script ข้อมูลตัวอย่างได้

## ยังไม่ตัดสินใจ

- ใช้ชื่อ domain ของบริษัทหรือ URL `*.run.app`
- ลูกค้ากลุ่มไหนจะได้ดู และต้องการบริษัท demo แยกต่อรายหรือใช้ร่วมกัน
- จะให้ลูกค้าลองบนมือถือด้วยหรือไม่ (ถ้าใช่ต้องมี EAS build หรือให้ติดตั้ง Expo Go)
