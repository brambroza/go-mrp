# แผนสร้าง ERP SaaS ใหม่ (ต่อยอด logic จาก DK-ERP)

อัปเดต 2026-09-29 · ฉบับเต็มพร้อมแผนภาพอยู่ใน Claude Docs "แผนสร้าง ERP SaaS ใหม่"

สร้างระบบใหม่ทั้งหมดเป็น SaaS multi-tenant (.NET 10 + PostgreSQL + Next.js + Expo) บน Google Cloud Singapore
ไม่แตะโค้ดหรือฐานข้อมูลเดิม เอามาเฉพาะ business logic ค่า cloud ช่วงแรก ~$35–60/เดือน
แพ็ก Starter เข้า pilot เม.ย. 2027 เปิดขาย ก.ย. 2027 (ทีม 3 คน)

## แพ็กเกจ subscription (draft)

| แพ็ก | โมดูล (มาจาก DK) | ผู้ใช้ | บาท/เดือน |
|---|---|---|---|
| Core (ทุกแพ็ก) | บริษัท/สาขา, ผู้ใช้+สิทธิ์, master สินค้า/วัตถุดิบ/ลูกค้า/supplier/คลัง, กล่องอนุมัติ, dashboard | — | — |
| Starter | PR/PO, รับ/เบิก FIFO/โอน/คืน/นับด้วยบาร์โค้ด, QC, stock card, ปิดสต็อกเดือน (DK.INVEN, DK.PO) | 5 | 2,900 |
| Pro | + SO+อนุมัติ, BOM, MRP, ใบสั่งผลิต, ตารางเครื่อง, actual หน้าไลน์, พาเลทเข้าคลัง, yield (DK.SO, DK.MRP) | 15 | 6,900 |
| Enterprise | + packing/ส่งของ/invoice, Lab R&D, export บัญชี, API, DB แยก (LM.SaleVat, DK.Lab) | 30+ | 15,000+ |

บัญชี (DK.ACC ~126 ฟอร์ม) ไม่ทำเอง → export ไปโปรแกรมบัญชีที่ลูกค้าใช้

## วิธีดึง logic เดิม

1. ทำแผนที่ flow จาก repo DK ทีละ flow
2. เขียน spec ต่อ flow: สถานะ, การเปลี่ยนสถานะ, กฎตรวจสอบ, สูตรคำนวณ, รูปแบบเลขเอกสาร
3. ขอ script SP/view/function ที่ไม่มีใน repo แบบอ่านอย่างเดียว
4. ของเฉพาะ DK → tenant setting
5. golden test: ชุดข้อมูลตัวอย่าง + ผลที่ระบบเดิมคำนวณ → ระบบใหม่ต้องได้เท่ากัน (FIFO, MRP, yield)

## Flow แยก Web / Mobile

Flow หลัก SO → FG (12 ขั้น):
1. สร้าง SO — web
2. อนุมัติ SO (หัวหน้า → ผู้จัดการ) — mobile
3. ออกใบสั่งผลิต + MRP — web
4. จัดตารางเครื่อง + กำลังคน — web
5. เปิด PR/PO — web
6. อนุมัติ PO — mobile
7. รับของ + QC ด้วยสแกน — mobile
8. เบิกวัตถุดิบเข้าไลน์ (FIFO) — mobile
9. บันทึกผลผลิต mix/fill + ของเสีย — mobile
10. พาเลทเข้าคลัง + อนุมัติ FG — mobile
11. Packing · ส่งของ · invoice — web
12. Dashboard yield · สต็อก · ส่งตรงเวลา — web

| บทบาท | Web | Mobile |
|---|---|---|
| ผู้บริหาร/ผู้จัดการ | dashboard, รายงาน | กล่องอนุมัติรวม, แจ้งเตือน, KPI |
| เซลส์ | SO, ลูกค้า, price list, ติดตาม SO | pre-order, เช็กสต็อก FG |
| ฝ่ายวางแผน | BOM, MRP, PD, Gantt, กำลังคน | — |
| จัดซื้อ | PR, PO, supplier, แผนส่งของ | — |
| คลัง | location, ปิดสต็อก, stock card | รับ/เบิก/โอน/คืน/นับ ด้วยสแกน (offline ได้) |
| หัวหน้าไลน์ | — | รับแผน, เบิกเข้าไลน์, actual+ของเสีย, พิมพ์ป้ายพาเลท |
| แอดมินลูกค้า | ผู้ใช้, สิทธิ์, route/ขั้นอนุมัติ, แพ็กเกจ, บิล | — |

## สถาปัตยกรรม

Web browser / Mobile app → Cloudflare (DNS/CDN/WAF) → Cloud Run [web (Next.js) · api (.NET 10) · worker (jobs)]
→ Cloud SQL PostgreSQL (tenant_id + RLS) · Cloudflare R2 (ไฟล์) · บริการนอก (Billing, LINE, Push, Email)

ทุก request ผ่าน api ซึ่งตั้ง tenant ให้ connection ก่อน query สมัครลูกค้าใหม่ = เพิ่มแถว tenant

## Server + Database บน cloud (ราคา list USD เช็ก 2026-09-29)

| ตัวเลือก | App | DB | เริ่มต้น/เดือน | หมายเหตุ |
|---|---|---|---|---|
| **GCP Singapore (แนะนำ)** | Cloud Run | Cloud SQL PG db-g1-small ~$25 | 35–60 | app ลดเหลือ 0 ได้ |
| GCP + Neon | Cloud Run | Neon 0.25 CU ~$19 | 25–45 | DB ลดเหลือ 0 ได้, branch dev |
| DigitalOcean SGP1 | App Platform $5+ | Managed PG $15–61 | 35–50 | บิลคงที่ |
| AWS Bangkok | ECS Fargate + ALB | RDS t4g.small ~$30–35 | 70+ | ถ้าต้องเก็บข้อมูลในไทย |
| Azure SE Asia | Container Apps | PG Flexible B1ms ~$19 | 30–50 | ไทยยังไม่เปิด |

| ช่วง | จัดสรร | USD/เดือน |
|---|---|---|
| 0–10 tenant | Cloud Run scale-to-zero/warm 1, db-g1-small, R2, CF Free | 35–60 |
| 10–100 | min 1 instance, Cloud SQL 2 vCPU/8 GB (+HA) | 200–350 |
| 100+ | 4 vCPU/16 GB + HA + replica, autoscale, CUD | 600–1,200+ |

หมายเหตุ: GCP Bangkok (asia-southeast3) เปิดแล้ว มี Cloud Run แต่ Cloud SQL ยังไม่มี → วาง app+DB ที่ Singapore ด้วยกัน
ไม่ใช้ Vercel (Next.js รันใน container ได้) · ราคา AWS/Azure บางรายการจากเว็บเทียบราคาภายนอก

## Roadmap

| เฟส | ช่วง | เนื้อหา |
|---|---|---|
| 0 ถอด logic + ออกแบบ | 5 ต.ค. – 13 พ.ย. 2026 | spec 4 flow, schema, wireframe, GCP + CI/CD |
| 1 Platform core | 16 พ.ย. 2026 – 15 ม.ค. 2027 | tenant, user/role, master, กล่องอนุมัติ, TH/EN, mobile login |
| 2 Starter | 18 ม.ค. – 26 มี.ค. 2027 | PR/PO, คลังบาร์โค้ด, stock card, ปิดเดือน |
| Pilot | 1 เม.ย. 2027 | ลูกค้า 2–3 ราย ใช้ฟรีแลก feedback |
| 3 Pro | 5 เม.ย. – 25 มิ.ย. 2027 | SO, BOM/MRP, PD, Gantt, actual, FG, yield |
| 4 ขาย/invoice + billing | 28 มิ.ย. – 27 ส.ค. 2027 | packing, invoice, export บัญชี, subscription, self-signup |
| เปิดขาย | 1 ก.ย. 2027 | |

## คำถามเปิด

- สิทธิ์ใน logic ของ DK-ERP (ถ้าเป็นงานลูกค้า ต้องเช็กสัญญา)
- ลูกค้ากลุ่มแรก: โรงงาน OEM เครื่องสำอาง/เคมี หรือโรงงานทั่วไป? มี pilot ไหม?
- รวมกับ GoAlong PPS / WMS / costing ไหม
- บัญชี export ไปโปรแกรมไหน
- ทีมจริง, งบ, ต้องเก็บข้อมูลในไทยไหม

## Sources (ราคา)

cloud.google.com/run/pricing · cloud.google.com/sql/pricing · docs.cloud.google.com/sql/docs/postgres/locations ·
aws.amazon.com/rds/postgresql/pricing · aws.amazon.com/fargate/pricing · azure.microsoft.com/pricing/details/container-apps ·
neon.com/pricing · digitalocean.com/pricing · developers.cloudflare.com/r2/pricing · vercel.com/pricing
