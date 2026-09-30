# Spec: Production planning (BOM, ความต้องการ, MRP, ใบสั่งผลิต)

อ้างอิง `docs/legacy/logic/mrp-planning.md` · โมดูล api: `Mrp.Production` · schema: `production`

## 1. Scope + แพ็กเกจ

Pro ขึ้นไป: BOM หลาย version หลายระดับ, ความต้องการอิสระ (demand), MRP แบบ netting จริง + lead time + lot size,
ข้อเสนอซื้อ/ผลิตที่กดสร้าง PR / ใบสั่งผลิตได้, ใบสั่งผลิต (work order) พร้อมรายการวัตถุดิบและใบสั่งผลิตลูกของ semi/bulk,
ติดตามยอดเบิกและยอดผลิตเข้าคลังจากเอกสารคลัง

**ยังไม่ทำในรอบนี้** (อยู่ในเฟส 3 ตาม roadmap): routing / เครื่อง / ปฏิทินกะ, scheduling + Gantt, actual mix/fill + ของเสีย + man-hour + yield,
พาเลทเข้าคลัง + FG approve, SO (ตอนนี้ demand กรอกเองหรือมาจาก forecast; เมื่อมีโมดูล SO จะเขียน demand ให้อัตโนมัติ)

## 2. Entities

| ตาราง | คอลัมน์สำคัญ |
|---|---|
| `production.bom_versions` | item_id, version_no, name, batch_size numeric(18,6) (หน่วยสต็อกของ item), status, effective_from, approved_by, activated_at · unique(tenant, item, version_no) · unique(tenant, item) where status = `Active` |
| `production.bom_lines` | bom_version_id, line_no, component_item_id, quantity (ต่อ batch), unit_id, conversion_factor, stock_quantity, loss_percent numeric(7,4), remark |
| `production.demands` | item_id, quantity (หน่วยสต็อก), due_date, source (`Manual`/`Forecast`/`SalesOrder`), reference_no, status (`Open`/`Planned`/`Closed`/`Cancelled`), customer_id |
| `production.mrp_runs` | document_no, run_date, horizon_days, status (`Completed`/`Failed`), item_count, planned_order_count, exception_count, run_by |
| `production.mrp_requirements` | run_id, item_id, level, on_hand, safety_stock, gross, scheduled_receipts, net, planned |
| `production.mrp_planned_orders` | run_id, item_id, order_type (`Buy`/`Make`), quantity, release_date, due_date, is_late, pegging (ต้นเหตุ), status (`Proposed`/`Converted`/`Dismissed`), converted_document_type, converted_document_id, converted_document_no |
| `production.mrp_exceptions` | run_id, item_id, code, message |
| `production.work_orders` | document_no, item_id, quantity, produced_quantity, start_date, due_date, status, bom_version_id, parent_work_order_id, source (`Manual`/`Mrp`/`Demand`), source_reference, warehouse_id, remark |
| `production.work_order_materials` | work_order_id, line_no, component_item_id, required_quantity, issued_quantity, is_made_in_house, child_work_order_id |

## 3. State machine

**BomVersion:** `Draft → Approved → Active → Superseded` · `Draft/Approved → Cancelled` — เปิดใช้ version ใหม่จะ supersede version ที่ active อยู่ของ item เดียวกันใน transaction เดียว
(ระบบเดิม active ได้สูตรเดียวเหมือนกันแต่ไม่มีประวัติ และสร้าง header ซ้ำทุกครั้ง)

**WorkOrder:** `Planned → Released → InProgress → Completed → Closed` · `Planned/Released → Cancelled` · `Released/InProgress ↔ OnHold`
- `Released → InProgress` อัตโนมัติเมื่อมีการเบิกวัตถุดิบครั้งแรก; `InProgress → Completed` เมื่อยอดผลิตเข้าคลัง ≥ จำนวนสั่ง หรือกดจบเอง
- ล็อกการแก้จำนวน/ยกเลิกเมื่อมีการเบิกแล้ว (กฎเดิม `CheckPD`)
- เบิกวัตถุดิบ = ใบเบิกคลัง (`Issue`) ที่ `reference_type = WO`; ผลิตเข้าคลัง = ใบรับ (`Receipt`) ที่ `reference_type = WO`

**Demand:** `Open → Planned (มีใบสั่งผลิตรองรับ) → Closed` · `Open → Cancelled`
**PlannedOrder:** `Proposed → Converted | Dismissed`

## 4. กฎคำนวณ

### 4.1 BOM explosion (สูตรเดิม `CalMRPSO.vb:300-310`)

```
component_qty = round(line.stock_quantity × parent_qty ÷ batch_size × (1 + loss_percent/100), 6)
```
- `batch_size ≤ 0` ถือเป็น 1 (กฎเดิม) แต่ระบบใหม่ไม่ยอมให้บันทึก batch_size ≤ 0
- component เป็น semi/bulk เมื่อ `supply_type = Make` **และ** มี BOM active → explode ต่อ (ระบบเดิมดูจากการมีสูตร active อย่างเดียว)
- ห้าม BOM วนกลับ (A → B → A) ตรวจตอน activate และตอน explode; ลึกสุด 20 ระดับ
- ปริมาณไม่ตัดเป็นจำนวนเต็ม (ระบบเดิมตัด) และแปลงหน่วยด้วยตาราง UOM (ระบบเดิม ×1000 hard-code)

### 4.2 MRP netting (ระบบเดิมไม่ netting ตอนอนุมัติ SO และไม่มี lead time)

ประมวลผล item ตาม low-level code (FG ก่อน แล้วไล่ลง) ต่อ item เดินตามวันที่:

```
projected(0)      = on_hand (เฉพาะ lot Released ในคลังที่ใช้ได้)
ก่อนแต่ละ demand  : projected += scheduled_receipts ที่ date ≤ วันที่ต้องการ  (PO ค้างรับ + ใบสั่งผลิตค้างผลิต)
หลัง demand       : projected −= gross
ถ้า projected < safety_stock:
    net     = safety_stock − projected
    planned = lot_size(net)            // max(net, min_order_qty) แล้วปัดขึ้นเป็นจำนวนเท่าของ order_multiple
    projected += planned
    due_date     = วันที่ต้องการ
    release_date = due_date − lead_time_days ;  is_late = release_date < วันนี้
```
- gross = demand อิสระ (`Open`) + ความต้องการวัตถุดิบค้างเบิกของใบสั่งผลิตที่ `Released/InProgress` + dependent demand จาก planned order ของระดับบน (วันที่ต้องการ = release_date ของแม่)
- สต็อกรอ QC ไม่นับเป็น on-hand แต่รายงานแยกเป็นข้อมูล
- ถ้า on_hand < safety_stock ตั้งแต่ต้นแม้ไม่มี demand → เสนอเติม due = วันนี้ + lead time
- item `Buy` → ข้อเสนอ `Buy` (สร้าง PR); item `Make` → ข้อเสนอ `Make` (สร้างใบสั่งผลิต) แล้ว explode ต่อ 1 ระดับ
- exception: `NO_BOM` (item Make ไม่มี BOM active), `LATE` (ต้องสั่งย้อนหลัง), `INACTIVE_ITEM`
- ผลลัพธ์ deterministic: เรียง item ตามรหัส เรียง demand ตามวันที่แล้วตาม reference

### 4.3 ใบสั่งผลิต

- วัตถุดิบ = explode BOM 1 ระดับ ณ วันสร้าง (เก็บ snapshot ไม่เปลี่ยนตาม BOM ภายหลัง)
- component ที่ผลิตเอง (`Make` + BOM active) → เลือกได้ว่าจะสร้างใบสั่งผลิตลูก (`parent_work_order_id`) due = start ของแม่
- lot split: สร้างหลายใบจาก demand เดียวได้; ยอดคงเหลือของ demand = quantity − Σ ใบสั่งผลิตที่ไม่ถูกยกเลิก
- เบิกเกินความต้องการของบรรทัดไม่ได้ เว้นแต่ tenant ตั้ง `production.overIssuePercent`

Golden test: `BomExploderTests`, `MrpEngineTests`, `ProductionFlowTests`

## 5. API (ใต้ `/api/v1/production`)

| Method | Path | สิทธิ์ |
|---|---|---|
| GET | `/boms` (itemId, status) · `/boms/{id}` · `/boms/{id}/explode?quantity=` · `/items/{itemId}/where-used` | `production.read` |
| POST/PUT | `/boms` · `/boms/{id}` (Draft) · POST `/boms/{id}/approve` · `/activate` · `/cancel` · `/copy` | `production.bom.manage` |
| GET/POST/PUT | `/demands` · `/demands/{id}` · POST `/demands/{id}/cancel` | read / `production.mrp.run` |
| POST | `/mrp-runs` (horizonDays, demandIds?) · GET `/mrp-runs` · `/mrp-runs/{id}` | `production.mrp.run` / read |
| POST | `/mrp-runs/{id}/convert` (plannedOrderIds) → PR ต่อ supplier / ใบสั่งผลิต · `/planned-orders/{id}/dismiss` | `production.mrp.run` |
| GET/POST | `/work-orders` · `/work-orders/{id}` · POST `/release` · `/hold` · `/resume` · `/complete` · `/close` · `/cancel` | read / `production.workorder.manage` |

## 6. หน้าจอ

Web: BOM (ต้นไม้หลายระดับ + where-used), demand, MRP run + ผล (ตารางต่อ item, ข้อเสนอ, exception) + ปุ่มสร้าง PR/ใบสั่งผลิต, ใบสั่งผลิต
Mobile: รับแผน/เบิกเข้าไลน์ตามใบสั่งผลิต (ใช้หน้าจอเบิกของคลัง อ้าง WO)

## 7. Tenant settings

`production.overIssuePercent` (0) · ชื่อเรียกขั้นตอน (mix/fill) และชนิด item เป็นข้อความ i18n ต่อ tenant ภายหลัง — ไม่ hard-code ขั้น mix/fill ของ DK

## 8. สิ่งที่ตัดจากระบบเดิม / Defects ที่ไม่ยกมา

- MRP ไม่ netting, ไม่มี lead time, ไม่สร้าง PR → ทำครบใน 4.2
- ตาราง temp ต่อ user (`TPDMTMPMRPDemand*`) → ผล MRP เก็บเป็น run ที่ตรวจย้อนหลังได้
- แถวใบสั่งผลิต = material line (`TPDMProdcutOrder`) → แยก header กับ materials
- qty ตัดเป็น Integer, UOM ×1000 hard-code, query on-hand ไม่มี WHERE → ไม่ยกมา
