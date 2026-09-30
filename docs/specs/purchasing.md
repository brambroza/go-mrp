# Spec: Purchasing (PR → PO → อนุมัติ → รับของ)

อ้างอิง `docs/legacy/logic/purchasing.md` · โมดูล api: `Mrp.Purchasing` · schema: `purchasing`

## 1. Scope + แพ็กเกจ

Starter ขึ้นไป: ใบขอซื้อ (PR), ใบสั่งซื้อ (PO), อนุมัติตาม route ของ tenant, สร้าง PO จาก PR, ยอดค้างรับ, ปิด/ยกเลิก
ยังไม่ทำในรอบนี้: PO revision, delivery schedule หลายงวดต่อบรรทัด, พิมพ์ PDF, แจ้งเตือนของเลยกำหนด (LINE/push), debit note

## 2. Entities

| ตาราง | คอลัมน์สำคัญ |
|---|---|
| `purchasing.purchase_requests` | document_no, document_date, status, source (`Manual`/`Mrp`/`MinStock`), source_reference, required_date, remark, requested_by |
| `purchasing.purchase_request_lines` | line_no, item_id, unit_id, quantity, conversion_factor, stock_quantity, required_date, suggested_supplier_id, remark |
| `purchasing.purchase_orders` | document_no, document_date, supplier_id, status, currency, exchange_rate numeric(18,6), vat_percent, discount_amount, subtotal, vat_amount, total (numeric 18,4), credit_days, delivery_date, remark |
| `purchasing.purchase_order_lines` | line_no, item_id, unit_id, quantity, conversion_factor, stock_quantity, unit_price numeric(18,4), discount_percent numeric(5,2), net_amount, delivery_date, pr_line_id, received_stock_quantity, is_closed |

## 3. State machine

**PR:** `Draft → Submitted → Approved → PartiallyOrdered → Ordered` · `Submitted → Rejected → Draft` · `Draft/Approved/PartiallyOrdered → Cancelled` · `Approved/PartiallyOrdered → Closed`
**PO:** `Draft → Submitted → Approved → PartiallyReceived → Received` · `Submitted → Rejected → Draft` · `Draft/Approved → Cancelled (ต้องไม่มีการรับ)` · `Approved/PartiallyReceived → Closed (ปิดยอดค้างรับ)`

- ขั้นอนุมัติมาจาก approval route ของ tenant (`PR`, `PO`) ตามยอดเงิน; ไม่มี route → อนุมัติทันที
  ระบบเดิม: PR ไม่มีโค้ดอนุมัติ, PO อนุมัติขั้นเดียว, ปุ่ม reject เขียนค่าผิด, auto-approve ตามกลุ่มวัตถุดิบ → แทนด้วย route ที่ตั้ง `min_amount` ได้
- แก้เอกสารได้เฉพาะ `Draft`/`Rejected`; รับของได้เฉพาะ PO ที่ `Approved`/`PartiallyReceived` และบรรทัดยังไม่ปิด
- สถานะรับของของ PO อัปเดตเมื่อ inventory post/void ใบรับหรือใบคืน supplier (ผ่าน `IPurchaseOrderGateway` ใน transaction เดียวกัน)

## 4. กฎคำนวณ / validation

ส่วนลดบรรทัดมี **นิยามเดียว** = % ของยอดบรรทัด (ระบบเดิมมี 2 นิยามปนกัน)

```
line.net_amount = round(quantity × unit_price × (1 − discount_percent/100), 2)
subtotal        = Σ line.net_amount
net             = subtotal − discount_amount          (0 ≤ discount_amount ≤ subtotal)
vat_amount      = round(net × vat_percent/100, 2)
total           = net + vat_amount
```

- ปัดเศษแบบ half away from zero; เก็บ `numeric(18,4)`
- `vat_percent`: ค่าของ supplier → ถ้าไม่มีใช้ `purchasing.vatPercent` ของ tenant (ตั้งต้น 7)
- สกุลเงิน = ของ supplier; `THB` บังคับ `exchange_rate = 1`; สกุลอื่นต้องกรอก rate > 0
- ราคาตั้งต้นของบรรทัด = price tier ของ supplier (ดู `masters.md`) ผู้ใช้แก้ได้
- ต้นทุนรับเข้า (ต่อหน่วยสต็อก) = `unit_price × (1 − discount%/100) × exchange_rate ÷ conversion_factor`
- ยอดค้างรับบรรทัด (หน่วยสต็อก) = `stock_quantity − received_stock_quantity` (ปิดบรรทัด → 0); `received` = รับ − คืน supplier
- ยอดค้างสั่งของ PR line = `stock_quantity − Σ stock_quantity ของ PO line ที่อ้าง (ไม่นับ PO ที่ Cancelled/Rejected)`; PO ที่อ้าง PR ต้องเป็น item เดียวกัน และ PR ต้อง `Approved`/`PartiallyOrdered`
- PO `Received` เมื่อทุกบรรทัดรับครบหรือปิด; มีการรับบางส่วน → `PartiallyReceived`

Golden test: `PurchaseOrderTotalsTests`, `PurchasingFlowTests`

## 5. API (ใต้ `/api/v1/purchasing`)

| Method | Path | สิทธิ์ |
|---|---|---|
| GET | `/requests` · `/requests/{id}` · `/orders` · `/orders/{id}` · `/requests/open-lines` | `purchasing.read` |
| POST/PUT | `/requests` · `/requests/{id}` | `purchasing.pr.manage` |
| POST | `/requests/{id}/submit` · `/cancel` · `/close` | `purchasing.pr.manage` |
| POST/PUT | `/orders` · `/orders/{id}` | `purchasing.po.manage` |
| POST | `/orders/{id}/submit` · `/cancel` · `/close` · `/orders/{id}/lines/{lineId}/close` | `purchasing.po.manage` |
| GET | `/orders/{id}/receivable-lines` (สำหรับ mobile รับของ) | `inventory.receive` หรือ `purchasing.read` |

อนุมัติ/ปฏิเสธทำผ่าน `/api/v1/approvals/{id}/approve|reject`

## 6. หน้าจอ

- Web: PR list/ฟอร์ม, PO list/ฟอร์ม (เลือก PR line ค้างสั่ง), tracking ยอดค้างรับ
- Mobile: อนุมัติ PR/PO ในกล่องอนุมัติ, รับของตาม PO (โมดูล inventory)

## 7. Tenant settings

`purchasing.vatPercent` (7), `purchasing.overReceivePercent` (0) · approval route ของ `PR`, `PO`

## 8. สิ่งที่ตัดจากระบบเดิม

- เลข PO ที่ฝัง prefix บริษัท/VAT/กลุ่มวัตถุดิบ → รูปแบบเลขเอกสารต่อ tenant
- hard delete PO → `Cancelled`
- bulk approve ที่ปิดบรรทัดไปด้วย (bug) → อนุมัติกับปิดยอดเป็นคนละ action
