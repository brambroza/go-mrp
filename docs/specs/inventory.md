# Spec: Inventory (รับ / เบิก FIFO / โอน / ปรับ / นับ / QC / ปิดงวด / stock card)

อ้างอิง `docs/legacy/logic/inventory-receive.md`, `inventory-issue.md`, `inventory-transfer.md`, `inventory-close.md`
โมดูล api: `Mrp.Inventory` · schema: `inventory`

## 1. Scope + แพ็กเกจ

Starter ขึ้นไป: รับของ (อ้าง PO ได้), QC lot, เบิก FIFO/FEFO, โอนคลัง/location, ปรับสต็อก, นับสต็อก, คืน supplier,
ปิดงวดรายเดือน, on-hand, stock card
ยังไม่ทำในรอบนี้: handling unit (พาเลท/กล่องย่อยใต้ lot), โอนแบบ 2 ขั้น (in-transit), QC plan รายหัวข้อ, จอง (reserve) ต่อ SO/WO

## 2. Entities

| ตาราง | คอลัมน์สำคัญ |
|---|---|
| `inventory.lots` | item_id, lot_no (unique ต่อ tenant — เป็นค่าที่พิมพ์บาร์โค้ด), supplier_lot, mfg_date, expiry_date, received_at, qc_status, unit_cost numeric(18,4), source_document_id |
| `inventory.movements` | **ledger append-only** · seq bigint identity, posting_date date, posted_at, movement_type, item_id, lot_id, warehouse_id, location_id, quantity numeric(18,6) **มีเครื่องหมาย หน่วยสต็อก**, unit_cost, document_type, document_id, document_no, document_line_id, po_line_id, reverses_movement_id |
| `inventory.balances` | cache ของ ledger: (item_id, lot_id, warehouse_id, location_id) → quantity · อัปเดตใน transaction เดียวกับ movement เท่านั้น |
| `inventory.stock_documents` | document_type (`Receipt`/`Issue`/`Transfer`/`Adjustment`/`Count`/`SupplierReturn`), document_no, document_date, status, warehouse_id, to_warehouse_id, supplier_id, reference_type, reference_id, reference_no, remark |
| `inventory.stock_document_lines` | line_no, item_id, unit_id, quantity (ตามที่คีย์), stock_quantity (หน่วยสต็อก), conversion_factor, unit_cost, location_id, to_location_id, lot_id (ระบุ lot เอง), lot_no / supplier_lot / mfg_date / expiry_date (รับของ), po_line_id, reason_code, system_quantity (นับ) |
| `inventory.stock_document_allocations` | line_id, lot_id, location_id, quantity — lot ที่ถูกตัดจริงตอน post |
| `inventory.periods` | year, month, status (`Open`/`Closed`), closed_at, closed_by, reopened_at, reopen_reason · unique(tenant, year, month) |
| `inventory.period_balances` | snapshot ตอนปิดงวด: year, month, item_id, lot_id, warehouse_id, location_id, quantity, value |

**กันแก้ ledger ที่ระดับ DB:** trigger บน `movements` ห้าม UPDATE/DELETE ทุกกรณี และห้าม INSERT ที่ `posting_date` อยู่ในงวดที่ปิดแล้ว
app role ได้แค่ SELECT/INSERT บนตารางนี้

## 3. State machine

**StockDocument:** `Draft → Submitted → Approved → Posted` · `Submitted → Rejected → Draft (แก้แล้วส่งใหม่)` · `Posted → Voided`
- Submit เรียก approval engine ตามประเภท (`GR`, `GI`, `TF`, `ADJ`, `CNT`, `RTS`) ไม่มี route → ข้ามไป Approved ทันที
- Post = เขียน movement + อัปเดต balance ใน transaction เดียว; เอกสารแก้ไม่ได้หลัง Submitted
- Void = เขียน movement กลับรายการ (`reverses_movement_id`) ทำไม่ได้ถ้างวดปิดแล้ว หรือทำให้ยอด lot ติดลบ (เช่น ของที่รับถูกเบิกไปแล้ว)
- ระบบเดิมตัดสต็อกคนละจังหวะต่อฟอร์ม (save / approve / scan) → ระบบใหม่ตัดที่ Post จุดเดียว

**Lot QC:** `Quarantine → Released | Rejected` · `Released ↔ OnHold` — เบิก/โอนออกได้เฉพาะ `Released` (คืน supplier ได้ทุกสถานะ)
**Period:** `Open → Closed → Open (reopen เฉพาะงวดปิดล่าสุด ต้องมีเหตุผล)`

## 4. กฎคำนวณ / validation

- **ปริมาณใน ledger เป็นหน่วยสต็อกเสมอ** line เก็บ qty + หน่วยที่คีย์ + factor ณ วันทำเอกสาร
- **On-hand** ต่อ (item, lot, warehouse, location) = Σ `movements.quantity` · available = on-hand ของ lot `Released` ที่ยังไม่หมดอายุ ในคลังประเภท General/Production
- **ตัด lot (allocation)** ตาม `inventory.issueStrategy`:
  - `Fifo`: เรียง `received_at, lot_no, location` · `Fefo`: เรียง `expiry_date (null ท้ายสุด), received_at, lot_no, location`
  - ข้าม lot ที่ไม่ `Released` หรือ `expiry_date < posting_date`
  - ตัดบางส่วนของ lot ได้ บรรทัดเดียวกระจายหลาย lot ได้
  - **ยอดไม่พอ = error ทั้งเอกสาร** (`inventory.insufficient_stock`) — ระบบเดิมตัดเท่าที่มีแล้วเงียบ
  - ผู้ใช้ระบุ lot เอง (สแกน) ได้ ระบบตรวจยอด lot นั้น
- **ห้ามยอดติดลบ** เว้นแต่ `inventory.allowNegativeStock = true`; ตรวจหลัง upsert balance ใน transaction (แถว balance ถูก lock โดย upsert)
- **รับของ:** 1 บรรทัด = 1 lot ใหม่ · lot_no ว่าง → ออกจาก sequence `LOT` · `expiry = mfg_date + shelf_life_days` ถ้าไม่กรอก · `qc_status = Quarantine` เมื่อ `inventory.qcOnReceive` ไม่งั้น `Released`
  ต้นทุนต่อหน่วยสต็อก = `unit_cost ÷ factor` ปัด 4 ตำแหน่ง
- **รับเกิน PO:** `Σ รับ (หน่วยสต็อก) ≤ สั่ง × (1 + purchasing.overReceivePercent/100)` เกิน → error (ระบบเดิมคำนวณแต่ไม่ block และ lookup config ผิด key)
- **โอน:** movement `TransferOut` (ต้นทาง) + `TransferIn` (ปลายทาง) ต่อ lot ใน transaction เดียว lot เดิม ต้นทุนเดิม
- **ปรับ:** บรรทัดบวก = `AdjustIn` (เข้า lot เดิมหรือสร้าง lot ใหม่) บรรทัดลบ = `AdjustOut` บังคับ `reason_code`
- **นับ:** สร้างเอกสารจากยอดระบบ (`system_quantity`) → กรอกยอดนับ → Post เขียน movement เท่ากับ `นับ − ยอดระบบ ณ ตอน post` reason `COUNT`
- **คืน supplier:** อ้าง lot ที่รับมา ตัดออกจาก lot นั้น และลดยอดรับของ PO line
- **ต้นทุนออก:** ตามต้นทุนของ lot (FIFO cost ต่อ lot)
- **ปิดงวด:** ต้องปิดเรียงเดือน · pre-check: ไม่มีเอกสาร `Draft/Submitted/Approved` ที่ `document_date` อยู่ในงวด · snapshot = Σ ledger ถึงสิ้นงวด (เฉพาะยอด ≠ 0)
- **Stock card:** ยอดยกมา = Σ movement ก่อนวันเริ่ม; running balance เรียง `posting_date, seq`

Golden test: `LotAllocatorTests` (FIFO/FEFO/ข้าม lot/ยอดไม่พอ/ระบุ lot), `InventoryFlowTests` (รับ → เบิก → โอน → void → ปิดงวด)

## 5. API (ใต้ `/api/v1/inventory`)

| Method | Path | สิทธิ์ |
|---|---|---|
| GET | `/documents` (type, status, search, from, to) · `/documents/{id}` | `inventory.read` |
| POST/PUT | `/documents` · `/documents/{id}` (เฉพาะ Draft) | ตามประเภท (`inventory.receive` / `.issue` / `.transfer` / `.adjust`) |
| POST | `/documents/{id}/submit` · `/post` · `/void` | ตามประเภท |
| POST | `/documents/count-sheet` (สร้างใบนับจากยอดระบบ) | `inventory.adjust` |
| GET | `/lots` · `/lots/by-number/{lotNo}` (สแกน) | `inventory.read` |
| POST | `/lots/{id}/qc` (Released / Rejected / OnHold) | `inventory.qc` |
| GET | `/on-hand` (item, warehouse, lot, availableOnly) · `/stock-card` (item, warehouse, from, to) | `inventory.read` |
| GET | `/allocation-preview` (item, warehouse, qty) — lot ที่ระบบจะตัด | `inventory.read` |
| GET/POST | `/periods` · `/periods/{year}/{month}/close` · `/reopen` | `inventory.period.close` |

## 6. หน้าจอ

- Web: เอกสารคลัง (list + ฟอร์ม), on-hand, stock card, ปิดงวด, QC
- Mobile (offline queue): รับของตาม PO, เบิก (สแกน lot + ระบบแนะนำ FIFO), โอน, นับ, ค้น lot จากบาร์โค้ด, QC

## 7. Tenant settings

`inventory.issueStrategy` (`Fifo`), `inventory.allowNegativeStock` (false), `inventory.qcOnReceive` (false), `purchasing.overReceivePercent` (0)
ขั้นอนุมัติของเอกสารคลังตั้งที่ approval route

## 8. สิ่งที่ตัดจากระบบเดิม

- คู่ตาราง `TINVENBarcode_IN` / `_OUT` → ledger เดียว มีเครื่องหมาย
- QC hold ด้วยการย้ายคลัง → สถานะของ lot (ยังตั้งคลังประเภท Quarantine ได้ถ้าต้องการแยกพื้นที่)
- hard delete เอกสาร → void ด้วย movement กลับรายการ
- FIFO เรียงตาม string ของ batch no → เรียงตามวันรับ/วันหมดอายุ
- รหัสคลัง/unit id/×12/×1000 ที่ hard-code → master + ตารางแปลงหน่วย
