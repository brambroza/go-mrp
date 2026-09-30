# Logic เดิม: Masters — ลูกค้า / supplier / บริษัท-ธนาคาร / price list / promotion / ปฏิทิน-วันหยุด / item / หน่วย / คลัง (DK.MK + DK.TL dynamic + DK.INVEN)

ถอดจาก repo DK-ERP-SYSTEM 2026-09-29 · อ้างอิง path ในระบบเดิม · DB: DK_MASTER, DB_PAYROLL (legacy), DK_SYSTEM

**สรุปสำคัญ:** มีฟอร์มเขียนเองแค่ ลูกค้า, supplier, price list/promotion, ปฏิทิน และตัวสร้าง location; **item / หน่วย / แปลงหน่วย / คลัง / กลุ่มวัตถุดิบ / ธนาคาร / credit term ไม่มีฟอร์มใน repo** — ดูแลผ่าน dynamic master (metadata ใน `MSysTableObjForm`; ดู platform.md §6) กฎ required/unique จึงอยู่ใน data ไม่ใช่โค้ด
ลูกค้ามี **2 ชุด**: `DB_PAYROLL.dbo.OMCustomer` (ฟอร์ม DK.MK + งานขาย LM.SaleVat) กับ `DK_MASTER.dbo.TCNMCustomer` (SO ของ DK.SO) · ประเภท item ตัดสินจาก **prefix รหัสกลุ่มวัตถุดิบ** · สูตรแปลงหน่วยมี 2 แบบไม่ตรงกัน
→ ระบบใหม่ต้องมี master ชุดเดียว, item type เป็น field, UOM conversion สูตรเดียว

## 1. ลูกค้า (`DK.MK/Master/MCustomer*.vb`)

**ตาราง (DB_PAYROLL — ชื่อ DB hard-code)**
- `OMCustomer(CNCustomerId, CSCustomerCode, CSCustomerName, CNGrpCustomerId, CSStateActive, CSBranch, CSTaxNo, CSAddress, FNMSysProvinceId, CNDeliverryTime, CNCreditAmt, CNCreditRcvAmt, CNCreditPayAmt, CSStateCheckBudget, FNMSysCreditAccId, FNMSysDebitAccId, CNCustomerRef, CSContactName, CSContactPhone, CSCustomerMainCode, CSNote, CSUserUpd/CDDateUpd/CSTimeUpd)` — update แล้วถ้าไม่สำเร็จจึง insert (`MCustomerAddEdit.vb:243-299`)
- `OMGrpCustomer(CNGrpCustomerId, CSGrpCustomerCode, CNRunNo)` กลุ่มลูกค้า (239, 747)
- `OMCustomer_Delivery(CNDeliveryId, CNCustomerId, CNSeq, CSAddressDelivery, FNMSysCountryId/ProvinceId/DistrictId/SubDistrictId, CSPostCode, CSEmail, CSPhoneNo)` (361-377, แก้ 414-430)
- `OMCustomer_Bank(CNCustomerId, FNMSysBankId, FNMSysBankBranchId, FTBankNumber, FTBankState, FTNote)` (491-500) · `OMCustomer_Sale(CNCustomerId, CSSaleCode)` พนักงานขายที่ดูแล (611)
- อ่านผ่าน view `V_TCNMCustomer` (`MCustomer.vb:34`, `MCustomerAddEdit.vb:71`), `V_TCNMCustomer_Tracking` (`MCustomerTracking.vb:94`)
- ที่อยู่อ้าง `TCNMCountry`, `TCNMProvince`, `TCNMDistrict`, `TCNMDistrictSub` (322-325)

**กฎ**
- required: กลุ่มลูกค้า, รหัสลูกค้า, จังหวัด (180-201) — ชื่อ/เลขภาษี/สาขา ไม่บังคับ
- รหัส: กดปุ่มสร้างจาก running ของกลุ่ม (`OMGrpCustomer.CNRunNo`) — หารหัสล่าสุดที่ขึ้นต้นด้วย pattern ของกลุ่มแล้ว +1 (736-753); แก้ไขแล้วรหัสเป็น read-only (`MCustomer.vb:78-81`); **ไม่มีการเช็กรหัสซ้ำ**
- id ออกจาก `RunID.GetRunNoID` (270)
- active: `CSStateActive` default = 1 ตอนสร้าง (`MCustomer.vb:126`)
- ลบ: ห้ามลบถ้ารหัสถูกใช้ใน `DB_PAYROLL.dbo.TSaleOrder` (447-451) ผ่านแล้วลบ header + delivery + bank + sale (454-461)
- ที่อยู่ส่งของหลายแห่ง `CNSeq` = จำนวนแถวเดิม + 1 (351-367)
- **เครดิต:** `คงเหลือ = (CNCreditAmt + CNCreditRcvAmt) − CNCreditPayAmt` คำนวณแสดงบนจอเท่านั้น (470-472); `CSStateCheckBudget` = flag ให้เช็กวงเงิน แต่**ไม่พบโค้ดที่อ่าน flag นี้นอก DK.MK**; ไม่มี credit term/credit day บนลูกค้า — SO ของ DK ใส่ default คงที่ (credit term, สกุลเงิน, payment term, credit day 30; `DK.SO/SaleOrder.vb:498-506`)

**สำหรับระบบใหม่:** `masters.customers` + `customer_groups`, `customer_addresses` (type = billing/shipping, default flag), `customer_contacts`; code unique ต่อ tenant ออกจาก numbering sequence (pattern ต่อกลุ่มเป็น tenant setting); `credit_limit numeric(18,4)`, `credit_term_id`, `credit_days`, `check_credit` — ยอดใช้ไปคำนวณจาก SO/invoice ค้างชำระ ไม่เก็บมือ; soft delete

## 2. Supplier (`DK.MK/Master/MSupplier*.vb`)

**ตาราง (DK_MASTER)**
- `TCNMSupplier(FNMSysSuplId, FTSuplCode, FNMSysSuplTypeId, FTSuplNameTH/EN, FTAddrTH/EN, FTPostCode, FNMSysCountryId/ProvinceId/DistrictId/SubDistrictId, FNMSysCurId, FNMSysTermOfPMId, FNMSysCrTermId, FNCreditDay, FNVat, FTTaxNo, FTPhone, FTMobile, FTFax, FTWebSite, FTPerson1, FTMail, FTNote, FNPoState, FTStateActive, FTStateSuplContractor)` (`MSupplierAddEdit.vb:119-193`)
- `TCNMSupplier_Bank(FNMSysSuplId, FNMSysBankId, FNMSysBankBranchId, FTBankNumber, FTBankState, FTNote)` (62-73, แก้ 309-319)
- อ้าง `TFINMCurrency`, `TFINMPaymentTerm`, `TFINMCreditTerm`, `TCNMBank`, `TCNMBankBranch` (96-97; `DK.SO/SaleOrder.vb:2160-2167`)

**กฎ**
- required: รหัส supplier เท่านั้น (108-112); `FNVat` ว่าง = 0 (115-117)
- รหัส = รหัสประเภท supplier (3 ตัวแรก) + running 6 หลัก หาจากรหัสล่าสุดของประเภทนั้น +1 เมื่อเลือกประเภท (328-338)
- เงื่อนไขซื้อ: สกุลเงิน, payment term, credit term, credit day, VAT % เป็น default ที่ PR/PO ดึงไปใช้
- `FTStateSuplContractor` = ผู้รับจ้างช่วง; บัญชีธนาคาร key โดยพฤตินัย = supplier + เลขบัญชี (317-318)
- ลบ: **ไม่เช็กการใช้งาน** ลบ header + bank ทันที (346-354)
- list/โหลดผ่าน SP `SP_GET_SUPLMASTER`, `SP_GET_SUPLMASTER_Heading` (`MSupplier.vb:33`, `MSupplierAddEdit.vb:219`)

**สำหรับระบบใหม่:** `masters.suppliers` + `supplier_bank_accounts` + `supplier_contacts`; required: code, ชื่อ, เลขภาษี (ถ้าออก WHT); code unique ต่อ tenant; ลบได้เมื่อไม่มี PR/PO อ้าง ไม่งั้น inactive

## 3. บริษัท / ธนาคาร

- `TCNMCmp(FNMSysCmpId, FTCmpCode, FTDocRun, FPCmpImage, FTStateActive, …)` — หน้า list/แก้เป็นสำเนาของ dynamic master ผูกตาราง `DK_MASTER.dbo.TCNMCmp` (`DK.MK/Master/MCompany.vb:33`, `MAddCompany.vb:292-311`); โลโก้เก็บเป็น image ใน DB (852); field อื่นมาจาก metadata
- `FTDocRun` = prefix เลขเอกสารของบริษัท (platform.md §4); บริษัทคือ scope ตอน login
- `TCNMCmp_Bank(FNMSysCmpId, FNMSysBankId, FNMSysBankBranchId, FTBankNumber, FTBankState, FTNote)` (`MAddCompany.vb:2535-2545`, แก้ 2603); `FTBankState` เป็น index ของ list `MSysListData.FTListName='FTBankState'` (`MCustomerAddEdit.vb:568`)
- ลบบริษัท: ผ่าน `CheckNotUsed` ของ dynamic master (`MCompany.vb:179-183`)
- popup ธนาคาร/ที่อยู่/พนักงานขาย (`MAddBank.vb`, `MAddDelivery.vb`, `MAddSale.vb`) ไม่มี validation — กดบันทึก = ยอมรับ (`MAddBank.vb:21-23`)

**สำหรับระบบใหม่:** tenant = นิติบุคคลที่สมัคร; `platform.companies` (หลายบริษัท/สาขาใต้ tenant ได้ — เลขภาษี, สาขา, ที่อยู่, โลโก้บน R2, prefix เลขเอกสาร) + `company_bank_accounts`; ธนาคารเป็น reference data กลางของระบบ

## 4. Price list และ promotion (`DK.MK/Trans/*.vb`)

**ตาราง (DK_MASTER)**
- `TPROMOTIONPRICE(FNPromotionId, CNGrpCustomerId, FNMSysRawMatId, FNMSysUnitId, FDStartDate, FDEndDate, FNQuantity, FNPrice, FNMSysUnitOfMeasureId, FNUnitEndQuantity, FTRemark, FTBarcodeNo)` — เขียนจาก 2 หน้าจอ: แบบตาราง (`TPriceListSet.vb:129-157`) และแบบรายตัว (`TPromotion.vb:202-237`)
- `TPROMOTIONPRICE_FREEGIFT(FNPromotionId, FNMSysRawMatId, FNQuantity, FNMSysUnitId)` ของแถม ลบแล้ว insert ใหม่ (`TPromotion.vb:257-271`)
- `TPriceListVersion(FNPriceVerId, FTPriceVerName, FDStartDate, FDEndDate, FNMSysRawMatId, CNGrpCustomerId, CNCustomerId, FTStateActive)` (`TPriceListverstionSet.vb:167-197`) + `TPriceListVersion_Detail(FNPriceVerId, FNSeq, FTDescription, FNQuantity, FNPrice, FNMSysUnitId)` (217-240) — 1 version = 1 item + ขั้นราคาตามปริมาณ/หน่วย
- ราคาตั้งบน item: `FNPrice` ของ item master; แก้ราคาเก็บประวัติ `TINVENMMaterialChangePrice(FNMSysRawMatId, FNSeq, FNPrice เดิม, FNPriceTo ใหม่)` (`DK.TL/wAddEditDynamic.vb:2212-2245`)

**กฎบันทึก**
- price list แบบตาราง: required กลุ่มลูกค้า (`TPriceListSet.vb:108-114`); เพิ่มแถวได้เมื่อแถวปัจจุบันมีรหัส item และ `FNPrice ≥ 0`, `FNQuantity ≥ 0` (75-76, 211-212); key update = promotion id + กลุ่มลูกค้า + item + หน่วย (137-140)
- promotion รายตัว: required item เท่านั้น (`TPromotion.vb:313-321`)
- version: required ชื่อ, item, วันเริ่ม, วันสิ้นสุด (`TPriceListverstionSet.vb:127-155`); detail ลบทั้งหมดแล้ว insert ใหม่ (217)
- **ไม่ตรวจ**: วันเริ่ม ≤ วันสิ้นสุด, ช่วงวันที่ซ้อนกัน, ขั้นปริมาณซ้ำ
- ลบ: ลบตรงไม่เช็กการใช้งาน (`MPriceList.vb:124`, `MPriceListVersion.vb:126`)

**กฎเลือกราคา (ตอนคีย์ SO; `GetProductPrice` ใน `LM.SaleVat/SaleOrder/xSaleOrder.vb:2429-2519`)**
1. ราคาฐาน = `V_Product_Price` ที่ item + หน่วย + กลุ่มลูกค้า ตรงกัน และ `FNQuantity ≤ qty ที่สั่ง` เรียงปริมาณมาก→น้อย เอาแถวแรก (2455-2464) — **ไม่กรองวันที่**
2. ราคาเฉพาะ = `V_PricePromotion` ที่ item + หน่วย + กลุ่มลูกค้า + **รหัสลูกค้า** ตรง, `FDStartDate ≤ วันที่ SO ≤ FDEndDate`, `FNQuantity ≤ qty` เรียงปริมาณมาก→น้อย เอาแถวแรก (2466-2477)
3. ถ้าข้อ 2 ไม่ได้ราคา (> 0) → หาใหม่ระดับกลุ่มลูกค้า (รหัสลูกค้าว่าง) เงื่อนไขอื่นเหมือนเดิม (2484-2498)
4. บรรทัดของแถม (`Gift = 1`) ราคา = 0 (2500-2503)
5. `ราคาที่ใช้ = ราคาเฉพาะ ถ้า > 0 ไม่งั้น ราคาฐาน` (2505-2511)
- ลำดับความเฉพาะ: ลูกค้า > กลุ่มลูกค้า > ราคาฐานตามขั้นปริมาณ; quantity break = ขั้นที่ปริมาณขั้นต่ำมากที่สุดที่ไม่เกิน qty
- logic เดียวกันถูก copy ไว้หลายฟอร์ม (`xSaleOrder_TT.vb:2456`, `xPreSaleOrderEdit.vb:2114`, `DK.SO/Packing/ProdOrderFillToTWH.vb:2242`, `DK.INVEN/Transaction/TransferWHToWHFG.vb:2058`)
- ของแถม: ไม่พบโค้ดที่เพิ่มบรรทัดของแถมอัตโนมัติจาก `TPROMOTIONPRICE_FREEGIFT` — ผู้ใช้ติ๊ก `Gift` เอง

**สำหรับระบบใหม่:** `masters.price_lists(scope = customer | customer_group | all, valid_from, valid_to, currency, status)` + `price_list_lines(item_id, uom_id, min_qty, unit_price numeric(18,4))`; ห้ามช่วงวันที่ซ้อนใน scope + item + uom เดียวกัน (exclusion constraint); resolver เดียวใน domain service: customer → group → all → ราคาตั้งของ item, เลือก `min_qty` มากสุดที่ ≤ qty; promotion แยก entity (`type = discount_percent | discount_amount | free_item`, เงื่อนไข, ของแถม) ทำทีหลังใน Enterprise; golden test สำหรับ resolver

## 5. Item / กลุ่มวัตถุดิบ

- **ไม่มีฟอร์ม item ใน repo**; ตารางที่ทุกโมดูลอ่านคือ `DK_MASTER.dbo.TPORawmaterial` คอลัมน์ที่พบ: `FNMSysRawMatId, FTRawMatCode, FTBarcodeNo, FTRawMatNameTH/EN, FNMSysUnitId (หน่วยสต็อก), FNMSysMatGrpId, FNPrice, FNRawmatState, FTStateActive, FTStateNotCreateJob, FNMinimun, FNMaxiimun, FNMSysRawMatColorId, FNMSysRawMatSizeId` (เช่น `DK.MK/Trans/TPriceListSet.vb:48-52`, `DK.MRP/MRPDemandbyPO.vb:44-58`)
- dynamic master มี logic เฉพาะตาราง `TINVENMMaterial`: barcode ห้ามซ้ำกับ item อื่น (`DK.TL/wAddEditDynamic.vb:1340-1384`), ลบไม่ได้ถ้ารหัสถูกใช้ (2453-2456), ประวัติราคา (ข้อ 4)
- กลุ่ม: `TCNMMatGrp(FNMSysMatGrpId, FTMatGrpCode, FTMatGrpNameTH)` (`DK.MRP/bompk/BomListingPKAdd.vb:198`); มี `TINVENMMaterialGrp` อีกตัวที่หน้าสิทธิ์อ้าง (comment; `DK.SE/Permission.vb:1323`)
- **การจัดประเภทจากรหัสกลุ่ม** (`DK.SO/Production/ProductionOrderListEdit.vb:195-199`)

| เงื่อนไขบน `FTMatGrpCode` | ประเภท |
|---|---|
| = `RM` (หรือ 2 ตัวแรก = `RM`) | วัตถุดิบ |
| 2 ตัวแรก = `PK` | บรรจุภัณฑ์ |
| = `SM-B` | bulk (ผลจาก mix) |
| = `SM-FG` | semi-finished |
| อื่น ๆ | FG / ของที่ต้องเปิด job (`DK.MRP/MRPDemandbyPRD.vb:247`) |

- `FNRawmatState = 1` + `FTStateActive = '1'` = เงื่อนไขที่หน้า BOM/MRP ใช้เลือก item ที่ผลิตได้ (`DK.MRP/CalMRPSO.vb:21`, `BomListing.vb:54`); `FTStateNotCreateJob = '1'` = ไม่เปิดใบสั่งผลิต (`MRPDemandbyPRD.vb:247`)
- ไม่พบการใช้ lead time, safety stock, shelf life, lot control flag ในโค้ด; `FNMinimun/FNMaxiimun` พบเฉพาะใน select list ไม่มี logic ใช้ (`DK.INVEN/Transaction/Issue.vb:2481`)

**สำหรับระบบใหม่:** `masters.items(code unique ต่อ tenant, name, name_en, item_type enum = RM | PK | BULK | SEMI | FG | SERVICE, item_group_id, stock_uom_id, purchase_uom_id, sales_uom_id, barcode unique, is_active, is_lot_tracked, shelf_life_days, lead_time_days, safety_stock, min_qty, max_qty, standard_price)`; `item_groups` เป็นการจัดกลุ่มเพื่อรายงาน/สิทธิ์ ไม่ใช้ตัดสินประเภท; ชื่อประเภทที่แสดง (mix/fill, bulk) เป็น tenant setting; ประวัติราคาใน audit log

## 6. หน่วย และการแปลงหน่วย

- `TCNMUnit(FNMSysUnitId, FTUnitCode, FTUnitNameTH, …)`; view `V_Unit(CSUNITCODE, CSUNITNAME, FNMSysUnitId)` (`DK.MK/Trans/TPriceListSet.vb:60`) — ดูแลผ่าน dynamic master
- `TCNMUnitConvert(FNMSysRawMatId, FNMSysUnitId = หน่วยต้นทาง, FNMSysUnitIdTo = หน่วยปลายทาง, FNRateFrom, FNRateTo)` — **ต่อ item** ไม่มีตารางแปลงกลาง; ไม่มีโค้ดเขียนใน repo
- **สูตร A (รายงาน PO/SO tracking):** `qty_stock = qty × FNRateFrom ÷ FNRateTo` โดยไม่มีแถว = 1 และ `FNRateTo = 0` ถือเป็น 1; join ด้วย item + หน่วยเอกสาร + หน่วยสต็อกของ item (`DK.PO/Tracking/PurchaseTracking.vb:320, 415`, `DK.SO/Track/SaleOrderTracking.vb:388`)
- **สูตร B (ฟอร์มขาย/พาเลท):** หาแถวด้วย item + หน่วยเอกสาร (ไม่ดูหน่วยปลายทาง) แล้ว `ถ้า FNRateFrom < FNRateTo → qty × FNRateTo ไม่งั้น qty ÷ FNRateTo` (`DK.SO/Packing/ProdOrderFillToTWH.vb:1531-1545`, `LM.SaleVat/SaleOrder/xSaleOrder.vb:769-782`); หน่วยรหัส `PCS` ถือเป็น 1 เสมอ (766)
- ใบสั่งผลิตคูณ ×1000 hard-code ตาม unit id (mrp-planning.md §3)

**สำหรับระบบใหม่:** `masters.uoms(code, name, category = weight | volume | count | length, is_base)` + `uom_conversions(item_id nullable, from_uom, to_uom, factor numeric(18,6))` — `item_id = null` คือแปลงมาตรฐาน (kg↔g), มี `item_id` คือแปลงเฉพาะ item (กล่อง→ชิ้น); สูตรเดียว `qty_to = qty_from × factor`; ปริมาณใน ledger เก็บเป็น stock UOM เสมอ + เก็บ qty/uom ที่คีย์ไว้คู่กัน; golden test ปัดเศษ 6 ตำแหน่ง

## 7. คลัง / location

- `TCNMWarehouse(FNMSysWHId, …, FTStateActive)` ดูแลผ่าน dynamic master; เป็น scope สิทธิ์ (`DK.SE/Permission.vb:1343-1349`)
- `TCNMWarehouseLocation(FNMSysWHLocId, FNMSysWHId, FTWHLocCode, FTWHLocNameTH/EN, FTRemark, FTStateActive)` — สร้างเป็นชุดจาก `DK.INVEN/Transaction/wGenWHLocation.vb:98-260`

| โหมด | รูปแบบรหัส | บรรทัด |
|---|---|---|
| 0 | รหัสที่พิมพ์เอง 1 ตัว | 109-131 |
| 1 | `RRR-CCC` (แถว-คอลัมน์ 3 หลัก) | 135-163 |
| 2 | `รหัสคลัง-RRR-CCC` | 166-196 |
| 3 | `โซน-RRR-CCC` | 197-228 |
| 4 | `รหัสคลัง-โซน-RRR-CCC` | 229-260 |

- กฎ: ต้องระบุแถว/คอลัมน์เริ่ม-สิ้นสุด > 0 และสิ้นสุด ≥ เริ่ม (41-95); unique = คลัง + รหัส location — ตัวที่มีแล้วข้าม (111-113); ชื่อ TH/EN = รหัส; สร้างแล้ว active ทันที

**สำหรับระบบใหม่:** `masters.warehouses(code, name, type = RM | PK | WIP | FG | QC | SCRAP, company_id, is_active)` + `locations(warehouse_id, code unique ต่อคลัง, zone, row, col, level, is_active)`; ตัวสร้าง location เป็นชุดเก็บไว้ (pattern เป็น input); พิมพ์ป้าย barcode location สำหรับ mobile

## 8. ปฏิทิน / วันหยุด

- วันหยุด: `wHoliday` + `wAddEditHoliday` เป็นสำเนา dynamic master ที่อ่าน metadata จากตารางชื่อ `HSys*` (`HSysTableObjForm`, `HSysObjDynamic_D`, `HSysTTablePK`; `DK.MK/Master/wHoliday.vb:154, 240, 712`) ต่างจากที่อื่นที่ใช้ `MSys*`; list กรองตามปี `LEFT(FDHolidayDate, 4)` (458); required/unique มาจาก metadata (`wAddEditHoliday.vb:969-1030`)
- ตารางวันหยุดที่ scheduler อ่าน: `THRMHoliday(FDHolidayDate)` (`DK.SO/RouteMaster/CalendarMaster.vb:576`)
- ปฏิทินทำงาน: `TCNMWorkTimeCalendar(FNMSysCalendarId, FNMSysMachineId, FTCalendarCode, FTCalendarNameTH/EN, FTStateActive, FNCalendarTypeId)` — หน้า `MWorkingTimeCalendar` insert อย่างเดียว ใส่เครื่อง = 0 และประเภท = 0 เสมอ (`DK.MK/Master/MWorkingTimeCalendar.vb:294-305`)
- **การสร้างวันทำงาน** อยู่ที่ `DK.SO/RouteMaster/CalendarMaster.vb:482-560`: วนทุกวัน 1 ม.ค.–31 ธ.ค. ของปีที่เลือก → เอาเฉพาะ จ–ศ ที่ไม่อยู่ใน `THRMHoliday` → สร้าง 2 กะ 08:00–12:00, 13:00–17:00 กะละ 240 นาที ลง `TCNMCalendar_Detail` (รายละเอียดใน mrp-planning.md §4)
- ไม่มีวันหยุดชดเชย, วันเสาร์ทำงาน, OT, ปฏิทินต่อคลัง/ต่อสาขา

**สำหรับระบบใหม่:** `masters.holidays(tenant_id, date, name, company_id nullable)` มีปุ่ม import วันหยุดราชการไทยรายปี; `work_calendars` + `shift_patterns(วันในสัปดาห์, เวลาเริ่ม-สิ้นสุด, พัก)` เป็น tenant setting; วันทำงาน derive ตอน query/scheduling ไม่ต้อง generate ล่วงหน้าทั้งปี; override รายวัน (ทำงานวันหยุด, OT)

## 9. SP / function / view ที่เกี่ยว (ไม่มี definition ใน repo — ต้องขอ)

- ลูกค้า: `V_TCNMCustomer`, `V_TCNMCustomer_Tracking`
- supplier: `SP_GET_SUPLMASTER`, `SP_GET_SUPLMASTER_Heading`
- ราคา: `SP_Get_PriceList`, `SP_Get_PriceList_From` (`DK.MK/Trans/MPriceList.vb:40`, `TPromotion.vb:338`), `GET_PriceListVer`, `GET_PriceListVer_From`, `GET_PriceListVer_Detail` (`MPriceListVersion.vb:38`, `TPriceListverstionSet.vb:36, 99`), view `V_Product_Price`, `V_PricePromotion`
- หน่วย: `V_Unit`
- dynamic master: `SP_GET_DYNAMIC_OBJECT_CONTROL` + SP validate/save ต่อฟอร์ม (platform.md §8)

## Defects ของระบบเดิม (ห้ามยกมา)

- **SQL injection:** ฟอร์มลูกค้า/supplier ต่อค่าจาก textbox ลง SQL ตรง ๆ ไม่ escape (ชื่อ, ที่อยู่, เลขภาษี; `MCustomerAddEdit.vb:247-268`, `MSupplierAddEdit.vb:124-152`) — ชื่อที่มี `'` ทำให้ save ล้ม
- pattern "update ก่อน ถ้า `Excute` คืน False จึง insert" — False เกิดได้ทั้ง 0 แถวและ SQL error (platform.md Defects) → error ตอน update กลายเป็น insert แถวใหม่ id ใหม่ **รหัสซ้ำได้** (`MCustomerAddEdit.vb:269-299`, `MSupplierAddEdit.vb:153-193`, `TPriceListSet.vb:142-157`)
- ไม่มี unique check รหัสลูกค้า/supplier; รหัสออกด้วย `MAX + 1` แบบ `NOLOCK` ไม่กัน race (`MCustomerAddEdit.vb:750`, `MSupplierAddEdit.vb:331`)
- ชื่อ DB hard-code (`DB_PAYROLL`, `DK_MASTER`, `DK_SYSTEM`; `MCustomerAddEdit.vb:67, 243`, `TPriceListSet.vb:50`) และบันทึกตาราง MASTER ผ่าน connection ของ DB อื่น (`TPriceListSet.vb:142`)
- ลูกค้า 2 ชุด (`OMCustomer` vs `TCNMCustomer`) ไม่มีโค้ด sync
- ลบ supplier / price list ไม่เช็กการใช้งาน; ลบลูกค้าหลายตารางไม่อยู่ใน transaction (`MCustomerAddEdit.vb:454-461`)
- แก้บัญชีธนาคาร supplier SQL ผิด syntax (ขาด `=`; `MSupplierAddEdit.vb:312`) → แก้ไม่ได้จริงและ error ถูกกลืน
- `MWorkingTimeCalendar.Save` execute คำสั่งว่างก่อน (279) แล้ว insert เสมอ → แก้ไขปฏิทินเดิมไม่ได้ สร้างแถวซ้ำ
- price list: `FNQuantity` ถูกตัดเป็น Integer (`TPriceListSet.vb:135, 153`), ไม่กันช่วงวันที่ซ้อน, ราคาฐานไม่ดูวันที่, resolver ถูก copy ≥ 5 ฟอร์ม
- สูตรแปลงหน่วย 2 แบบให้ผลต่างกัน + `PCS` hard-code + ×1000 hard-code
- ประเภท item ตัดสินจาก prefix รหัสกลุ่ม; วงเงินเครดิตคีย์มือไม่ผูกยอดจริง; `catch` ว่างเกือบทุก method ใน DK.MK

## Gap ที่ต้องดู SP definition / data

1. `TPORawmaterial` vs `TINVENMMaterial`: เป็นตารางเดียวกัน (view/synonym) หรือคนละตาราง, schema เต็ม, ฟอร์ม dynamic ตัวไหนเขียน
2. ความหมายและค่าทั้งหมดของ `FNRawmatState`; รายการ `TCNMMatGrp` จริง (มีรหัสนอกเหนือ RM/PK/SM-B/SM-FG อะไรบ้าง) และ `TINVENMMaterialGrp` ต่างกันอย่างไร
3. `V_Product_Price` และ `V_PricePromotion`: ดึงจาก `TPROMOTIONPRICE` หรือ `TPriceListVersion(_Detail)` หรือราคาบน item, กรอง `FTStateActive` ไหม
4. `FNMSysUnitOfMeasureId`, `FNUnitEndQuantity`, `FTBarcodeNo` บน `TPROMOTIONPRICE` ใช้ทำอะไร; ของแถมถูกนำไปใช้ที่ไหน (SP?)
5. `TCNMUnitConvert`: ทิศทางของ `FNRateFrom/FNRateTo` ใน data จริง, สูตร A หรือ B ถูก, มีแถวต่อ item ครบไหม
6. metadata dynamic master (`MSysTableObjForm`, `MSysObjDynamic_D`) ของ item, หน่วย, แปลงหน่วย, คลัง, กลุ่มวัตถุดิบ, ธนาคาร, credit/payment term, สกุลเงิน: field required / unique / default
7. ตารางวันหยุดที่ `wHoliday` เขียนจริง (ชื่อมาจาก `HSysTableObjForm`) = `THRMHoliday` หรือไม่ และตาราง `HSys*` มีใน DB ของ DK ไหม
8. `TCNMWorkTimeCalendar` กับ `TCNMCalendar` (ที่ scheduler ใช้) สัมพันธ์กันอย่างไร
9. `OMCustomer` ↔ `TCNMCustomer`: sync ด้วย trigger/job/view หรือคีย์ 2 ที่; `V_TCNMCustomer` ดึงจากตารางไหน
10. `CSStateCheckBudget` และวงเงินเครดิต: มี SP/trigger ตอนอนุมัติขายที่บล็อกเกินวงเงินไหม; `CNCreditRcvAmt/CNCreditPayAmt` ใครอัปเดต
11. `OMGrpCustomer.CNRunNo` และรูปแบบรหัสลูกค้าจริง; รูปแบบรหัส supplier type
12. `SP_GET_SUPLMASTER*`: คอลัมน์ที่คืน, `FNPoState` คืออะไร
13. `TFINMCreditTerm`, `TFINMPaymentTerm`: field จำนวนวัน/เงื่อนไข, ใช้คำนวณ due date ที่ไหน
14. unique index / FK / trigger จริงบนตาราง master ทั้งหมด (โค้ดไม่บังคับ อาจบังคับที่ DB)
