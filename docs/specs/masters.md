# Spec: Masters (item, หน่วย, คลัง, ลูกค้า, supplier, ราคาซื้อ)

อ้างอิง `docs/legacy/logic/masters.md` · โมดูล api: `Mrp.Masters` · schema: `masters`

## 1. Scope + แพ็กเกจ

Core (ทุกแพ็ก): หน่วย + แปลงหน่วย, กลุ่ม item, item, คลัง + location, ลูกค้า, supplier, ราคาซื้อต่อ supplier
ยังไม่ทำในรอบนี้: price list ขาย + promotion (Enterprise), ปฏิทิน/วันหยุด (ทำพร้อม scheduling ใน Pro), บริษัท/สาขาใต้ tenant

## 2. Entities

ทุกตารางมี `tenant_id` + RLS, `code` unique ต่อ tenant (เทียบแบบไม่สนตัวพิมพ์ — เก็บเป็นตัวพิมพ์ใหญ่), `is_active`, audit columns

| ตาราง | คอลัมน์สำคัญ |
|---|---|
| `masters.units` | code, name, name_en |
| `masters.unit_conversions` | item_id (null = ใช้กับทุก item), from_unit_id, to_unit_id, factor numeric(18,6) · unique(tenant, item, from, to) |
| `masters.item_groups` | code, name, name_en |
| `masters.items` | code, name, name_en, item_type (`RawMaterial`/`Packaging`/`Bulk`/`SemiFinished`/`FinishedGood`/`Service`), item_group_id, stock_unit_id, purchase_unit_id, barcode (unique ต่อ tenant ถ้ามี), supply_type (`Buy`/`Make`), is_lot_tracked, shelf_life_days, lead_time_days, safety_stock, min_stock, max_stock, min_order_qty, order_multiple, standard_cost numeric(18,4) |
| `masters.warehouses` | code, name, warehouse_type (`General`/`Quarantine`/`Production`/`Scrap`) |
| `masters.locations` | warehouse_id, code (unique ต่อคลัง), name |
| `masters.customers` | code, name, name_en, tax_id, branch_no, address, phone, email, contact_name, credit_days, credit_limit numeric(18,4), check_credit |
| `masters.suppliers` | code, name, name_en, tax_id, branch_no, address, phone, email, contact_name, currency, credit_days, vat_percent numeric(5,2) |
| `masters.supplier_prices` | supplier_id, item_id, unit_id, min_qty, unit_price numeric(18,4), currency, valid_from, valid_to |

## 3. State

master ไม่มี workflow: `Active ↔ Inactive` ไม่ลบจริง (เอกสารเก่ายังอ้างได้) inactive แล้วเลือกในเอกสารใหม่ไม่ได้

## 4. กฎ / validation

- **ประเภท item เป็น field** ไม่เดาจาก prefix รหัสกลุ่ม (ระบบเดิมใช้ `RM`/`PK`/`SM-B`/`SM-FG`) ชื่อที่แสดงของแต่ละประเภทแก้ได้ใน i18n ต่อ tenant ภายหลัง
- **แปลงหน่วยสูตรเดียว:** `qty_to = round(qty_from × factor, 6)` ลำดับการหา factor จาก A → B:
  1. A = B → 1
  2. conversion เฉพาะ item (A→B) → factor; (B→A) → 1/factor
  3. conversion กลาง (item_id = null) (A→B) → factor; (B→A) → 1/factor
  4. ผ่านหน่วยสต็อกของ item: A→stock แล้ว stock→B (ใช้กฎ 2–3 แต่ละช่วง)
  5. ไม่พบ → error `masters.uom.no_conversion` (ไม่มี fallback ×12 / ×1000 แบบระบบเดิม)
- factor > 0; ห้ามมีทั้ง A→B และ B→A ของ scope เดียวกัน (กันค่าขัดกัน)
- ราคาซื้อ: เลือกแถวของ supplier + item ที่ `valid_from ≤ วันที่ ≤ valid_to (หรือ null)` และ `min_qty` มากสุดที่ ≤ qty (แปลง qty เป็นหน่วยของแถวราคาก่อน); ไม่พบ → ไม่มีราคาตั้งต้น (ผู้ใช้กรอกเอง)
- `safety_stock`, `lead_time_days`, `min_order_qty`, `order_multiple` ใช้โดย MRP (ระบบเดิมไม่มี)

Golden test: `UnitConverterTests`, `SupplierPriceResolverTests`

## 5. API

REST ต่อ resource ใต้ `/api/v1/masters/{units|unit-conversions|item-groups|items|warehouses|locations|customers|suppliers|supplier-prices}`
`GET /` (search, activeOnly, page, pageSize) · `GET /{id}` · `POST /` · `PUT /{id}` — อ่านใช้ `masters.read` เขียนใช้ `masters.manage`
`GET /masters/items/{id}/convert?from=&to=&qty=` · `GET /masters/supplier-prices/resolve?supplierId=&itemId=&unitId=&qty=&date=`

## 6. หน้าจอ

Web: `/masters/items`, `/masters/units`, `/masters/warehouses`, `/masters/customers`, `/masters/suppliers` (ตาราง + ฟอร์ม drawer) · Mobile: ค้น item/ลูกค้า (read)

## 7. Tenant settings

ไม่มีเฉพาะโมดูลนี้

## 8. สิ่งที่ตัดจากระบบเดิม

- dynamic master (metadata-driven form) → ฟอร์มปกติ
- ลูกค้า 2 ชุด (`OMCustomer`, `TCNMCustomer`) → ชุดเดียว
- ยอดเครดิตที่คีย์มือ → คำนวณจากเอกสารค้างชำระ (ทำในเฟส 4)
