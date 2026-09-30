# Logic เดิม: PR → PO → อนุมัติ → แผนส่งของ / ติดตาม (DK.PO + DK.MNG)

ถอดจาก repo DK-ERP-SYSTEM 2026-09-29 · อ้างอิง path ในระบบเดิม · DB: PUR (เอกสาร), DK_MASTER (supplier/item/ราคา), DK_INVENTORY (รับ/คืน), SYSTEM (เลขเอกสาร, dynamic form)

**สรุปสำคัญ:** PO ที่ใช้จริง (`Purchase.vb`) **ไม่ผูก PR** แล้ว (โค้ดดึง PR ถูก comment) และอนุมัติ **ขั้นเดียว** (`FTStateManagerApp`) — ขั้น supervisor และสถานะ reject (`'2'`) มีแต่ใน query รายงาน ไม่มีโค้ดเขียน
PR (`PurchaseRequest.vb`) เป็นเอกสารกรอกมือ ไม่มีปุ่มส่ง/อนุมัติ ไม่มี trigger จาก MRP/min stock
"แผนส่งของ" ไม่ใช่เอกสารแยก = `FDDeliveryDate` ต่อ PO line เทียบกับยอดรับ/คืน
→ ระบบใหม่ต้องมี PR→PO แบบ line-level balance, approval N ขั้นแบบ config, reject/cancel/close เป็น state จริง

## 1. ตารางและ entity

- **PR header** `TPURTPurchaseRequest(FTPurchaseRQNo, FDPurchaseRQDate, FNMSysCmpId, FNMSysSuplId, FNMSysCrTermId, FNCreditDay, FNMSysTermOfPMId, FNMSysCurId, FNExchangeRate, FNMSysDeliveryId, FNPoType, FTJobOrderNo, FNPoAmt, FNDisCountPer/Amt, FNPONetAmt, FNVatPer/Amt, FNSurcharge, FNPOGrandAmt, FTPOGrandAmtTH/EN, FTPurchaseState, FTStateSendApp, FTStateSuperVisorApp, FTStateManagerApp, FTStatePrint)` — เขียนแบบ dynamic จาก metadata `MSysTableObjForm` (`DK.PO/PurchaseRequest.vb:590-851`), อ่านกลับที่ `Purchase.vb:1902-1912`
- **PR line** `TPURTPurchaseRequest_OrderNo(FTPurchaseRQNo, FNMSysRawMatId, FTOrderNo = job, FNMSysUnitId, FNPrice, FNDisPer, FNDisAmt, FNQuantity, FNNetAmt, FTRemark)` key = PR + item + job — `PurchaseRequest.vb:1122-1161`, แก้ไข 1389-1428, ลบ 1285
- **PO header** `TPURTPurchase` field ชุดเดียวกับ PR + `FTPurchaseNo, FDPurchaseDate, FTPurchaseBy, FNMSysMatGrpId, FNMSysBrandId, FTPurchaseRQNo, FNPoState, FTRemark, FTSendAppBy/Date/Time, FTSuperManagerName/AppDate/AppTime, FTStateCancel, FTStateCancelNote, FTStatePrint/FTPrintBy/Date/Time` — `Purchase.vb:627-976`
- **PO line** `TPURTPurchase_OrderNo(FTPurchaseNo, FNSeq, FNMSysRawMatId, FNMSysUnitId, FNPrice, FNDisPer, FNDisAmt, FNQuantity, FNNetAmt, FDDeliveryDate, FTStateClose, FTStateCloseBy/Date/Time, FTReOpenClose, FTStateReOpenBy/Date/Time)` — `Purchase.vb:1346-1420`; item master ของ PO คือ `TPORawmaterial` (`Purchase.vb:367`)
- **ราคา** `DK_MASTER.TPurchasePrice(FNMSysRawMatId, FNMSysSuplId, FNMSysUnitId, FNQuantity = ขั้นปริมาณ, FNPrice)` — อ่านที่ `Purchase.vb:2109-2122`
- **config** `TCNMMatGrp.FTAutoApproved` (`Purchase.vb:982`), `TCNMCmp.FTDocRun` (2442), `TFINMCurrency.FTStateLocal` (1712), `TINVENMConfigReceiveOver(FNMSysMatGrpId, FNStartRcvQty, FNEndRcvQty, FNRcvOverPercent)` (`DK.INVEN/Transaction/wReceiveItem.vb:167-172`)
- **temp รายงาน** `TmpPurchaseTracking` ต่อ user (`Tracking/Purchaseplandelivery.vb:495-527`)
- `PurchaseLooupEdit.vb` = PO form รุ่นเก่า (ยังผูก PR, เลขจาก `SP_GEN_DOCUMENTNO`) ไม่มีที่ไหนเรียก class นี้ใน repo; `DK.MNG/Form1.vb`, `approvemng.vb` เป็น stub

**สำหรับระบบใหม่:** `purchasing.purchase_requests/_lines`, `purchase_orders/_lines` (line มี `pr_line_id` nullable, `delivery_date`, `closed_at`), `supplier_price_tiers`; item master เดียว ไม่แยก `TPORawmaterial`/`TINVENMMaterial`; ทุกตารางมี `tenant_id`

## 2. PR (`PurchaseRequest.vb`)

- สร้างมือเท่านั้น: `FTPurchaseState` = "<user> MANUAL <วันเวลา>" (660); line ผูก job ได้ผ่าน `FTOrderNo` (1144) — ไม่มีโค้ดสร้าง PR จาก MRP หรือ min stock (สอดคล้อง `mrp-planning.md` §2)
- ค่า default hard-code: credit term `00001`, payment term `0001`, สกุล `BHT`, rate 1, delivery `LKB001` (436-441)
- เพิ่ม/แก้ line ผ่าน popup `PurchaseAddItem` แล้วรวมยอด header ใหม่และ save header ทันที (1173-1185)
- ล็อก: แก้/ลบ/เพิ่ม line ไม่ได้เมื่อมี `TPURTPurchase.FTPurchaseRQNo` = PR นี้ (`CheckReceive` 1316-1330)
- save ทับ PR ที่อนุมัติแล้ว → reset `FTStateSendApp/SuperVisorApp/ManagerApp = '0'` (963-976) แต่**ไม่มีปุ่มหรือโค้ดใด set flag เหล่านี้เป็น 1** ใน DK.PO/DK.MNG
- ลบ = hard delete header + line (853-874); พิมพ์ `PurchaseOrderRequest.rpt` (1018)

**สำหรับระบบใหม่:** PR มี source = Manual / MRP / MinStock / WorkOrder; state `Draft → Submitted → Approved → PartiallyOrdered → Ordered → Closed` (+ Rejected, Cancelled); ค่า default (term, สกุล, จุดส่ง) เป็น tenant setting + supplier default

## 3. PO entry (`Purchase.vb`)

**เลขเอกสาร** (`getDocNew` 2440-2461, `GetDocNo` 2419-2438)
- รูปแบบ = `FTDocRun`(บริษัท) + `Y|N`(VAT) + รหัสกลุ่มวัตถุดิบ + `YY`(ค.ศ. 2 หลัก) + running 5 หลัก
- `Y` เมื่อประเภท = index 0; บริษัท `B` และ `JT` บังคับ `Y` เสมอ (2448-2453); ประเภทถูกบังคับจาก VAT%: > 0 → 0 ไม่งั้น 1 (1568-1572); VAT default 7 (1311)
- running = เลขท้าย 5 หลักสูงสุดของ prefix เดียวกัน + 1 อ่านนอก transaction (2427-2428); PR ใช้ `SP_GEN_DOCUMENTNO` + prefix `FTDocRun` (`PurchaseRequest.vb:659`, `DK.TL/Document.vb:3-16`)

**ราคา**
- เปลี่ยน qty → หาราคาขั้นบันได: แถว `TPurchasePrice` ของ item + supplier ที่ `FNQuantity <= qty` มากสุด ถ้า > 0 ใส่แทนราคาเดิม (2089-2122)
- ราคาตาม item + unit (`GetPricePO` 2353-2370) มี handler แต่ถูกปลด `Handles` (2329, 2371) → ไม่ทำงาน; ไม่มี logic "ราคาซื้อล่าสุด"
- item lookup กรองตามกลุ่มวัตถุดิบและ brand ของ header (1317-1344) → PO หนึ่งใบ = กลุ่มวัตถุดิบเดียว

**สูตร**
- line (grid PO): `FNDisAmt = price × qty × FNDisPer / 100`, `FNNetAmt = qty × price − FNDisAmt` (2170-2196)
- line (popup PR): `FNDisAmt = price × FNDisPer / 100` (ต่อหน่วย), `FNNetAmt = qty × (price − FNDisAmt)` (`PurchaseAddItem.vb:44-77`)
- header: `FNPoAmt = Σ FNNetAmt` (2198-2205; SQL ที่ 1408-1416)
- `FNDisCountAmt = FNPoAmt × FNDisCountPer / 100` กรอกได้ทั้ง % และจำนวนเงิน คำนวณกลับอีกฝั่ง (1589-1602)
- `FNPONetAmt = FNPoAmt − FNDisCountAmt` (1625); `FNVatAmt = FNPONetAmt × FNVatPer / 100` แก้มือได้ (1603-1615)
- `FNPOGrandAmt = FNPONetAmt + FNVatAmt + FNSurcharge` (1634) + ตัวอักษร TH/EN (1676-1683)
- สกุลเงิน: `FTStateLocal = '1'` → rate ล็อกที่ 1; สกุลอื่นกรอกเอง เริ่มที่ 1; rate <= 0 ถูกแก้เป็น 1 (1555-1557, 1711-1726) — ยอดเอกสารเก็บเป็นสกุล PO ไม่แปลงบาท
- validation: field บังคับมาจาก metadata (502-625); line ต้องมี item + unit + qty > 0 (`PurchaseAddItem.vb:84-106`); ยกเลิกต้องมีเหตุผล (1125-1131)

**สำหรับระบบใหม่:** เลขเอกสารจาก sequence ต่อ tenant ต่อประเภท ใน transaction, pattern เป็น config (prefix บริษัท/VAT/กลุ่ม/ปี เป็น token); นิยามส่วนลด line แบบเดียว (% ของยอด line) ใช้ทั้ง PR/PO/รับ; VAT rate และ include/exclude เป็น tenant setting; price tier ต่อ supplier + effective date + ราคาล่าสุดเป็น fallback; เงิน `numeric(18,4)` เก็บ rate ณ วันเอกสาร

## 4. สถานะและการอนุมัติ PO

| column | ค่า | เปลี่ยนที่ไหน |
|---|---|---|
| `FTStateSendApp` | 0/1 | ปุ่มส่งอนุมัติ (`Purchase.vb:2287-2306`); reset 0 ตอน save ทับ (1134-1146) |
| `FTStateManagerApp` | 0/1 | อนุมัติในฟอร์ม PO (2308-2327), bulk `Tracking/PurchaseApprove.vb:262-288`, `DK.MNG/AprovedMng.vb:36-62`; ถอน → 0 (`Purchase.vb:2486-2511`, `PurchaseApprove.vb:325-351`, `AprovedMng.vb:64-90`) |
| `FTStateSuperVisorApp` | 0 เท่านั้น | ถูก reset (1141) ไม่เคยถูก set 1 |
| `FTStateCancel` + note | 0/1 | checkbox ตอน save (1150-1156) |
| `FTStatePrint` | 1 | พิมพ์ (1258-1279) |
| line `FTStateClose` / `FTReOpenClose` | 0/1 | `Tracking/PurchaseActive.vb:545-591` (ปิด), 593-642 (re-open) |

- flow จริง: Draft → ส่งอนุมัติ → อนุมัติ (ขั้นเดียว) → รับของ; ถอนอนุมัติได้ถ้ายังไม่มีรับ (2491)
- auto-approve: หลัง save ถ้ากลุ่มวัตถุดิบ `FTAutoApproved = '1'` → set ส่ง + อนุมัติทันทีด้วย user ที่ save (978-1007, 1164-1169)
- ผู้อนุมัติ: รายการรออนุมัติมาจาก SP ที่รับ username (`AprovedMng.vb:25`, `PurchaseApprove.vb:168,183`) → สิทธิ์/วงเงินอยู่ใน SP; ปุ่มอนุมัติในฟอร์ม PO ไม่ตรวจ user ในโค้ด
- reject: tracking แปล `'2'` เป็น reject (`PurchaseTracking.vb:297,300`) แต่ปุ่ม reject เขียน `'0'` (`AprovedMng.vb:75`)
- ล็อกแก้ไข/ลบ: (ก) มี `TINVENReceive.FTPurchaseNo` = PO (`CheckReceive` 1685-1709; ระดับ item ใช้ `TINVENReceive_Detail_Order`), (ข) `FTStateManagerApp` = 1 (1119-1122, 1184-1187) → ยกเลิก PO ที่อนุมัติแล้วต้องถอนอนุมัติก่อน
- ลบ = hard delete (1009-1032); ปิด/เปิดใหม่ทำระดับ line ไม่มีสถานะปิดระดับ header
- `FNPoState` (จาก supplier master `DK.MK/Master/MSupplierAddEdit.vb:146`) ใช้เลือกภาษาใบ PO: 0 = TH, อื่น = EN (`PurchaseActive.vb:512-526`)

**สำหรับระบบใหม่ (state machine):**
- PO: `Draft → Submitted → Approved → PartiallyReceived → Received → Closed` (+ `Rejected` กลับ Draft พร้อมเหตุผล, `Cancelled`, `Reopened`)
- line: `Open → PartiallyReceived → Received | ClosedShort`; header status derive จาก line
- ขั้นอนุมัติ, วงเงินต่อขั้น, auto-approve ตามกลุ่ม/วงเงิน = config ต่อ tenant; ผู้อนุมัติ ≠ ผู้สร้าง; ทุก transition ลง audit log; ยกเลิกเป็น soft (ไม่ hard delete); 2FA + อนุมัติบน mobile

## 5. ยอดคงค้าง / แผนส่งของ / tracking

- **สูตรกลาง (tracking):** `Balance = (POqty − RcvQty) + ReturnQty`; line ที่ `FTStateClose = '1'` → 0 (`Tracking/PurchaseTracking.vb:290`, `PurchaseTrackingSum.vb:290`, `Purchaseplandelivery.vb:290`)
  - `RcvQty` = Σ `TINVENReceive_Detail.FNQuantity` join ด้วย PO + item + `FNSeq` (`Purchaseplandelivery.vb:373-376`)
  - `ReturnQty` = Σ `TINVENBarcode_OUT` ของ `TINVENReturnToSupplier` join ด้วย PO + item (377-383) แปลงหน่วยด้วย `TCNMUnitConvert.FNRateFrom / FNRateTo` (320, 396)
- **แผนส่งของ** (`Purchaseplandelivery.vb`): กรองช่วง `FDDeliveryDate` ของ line + ประเภท RM/PK/FG จาก 2 ตัวแรกของรหัสกลุ่ม (348-366); พิมพ์ `PurchaseOrderDelivery.rpt` ผ่าน temp table (487-560); ไม่มี partial schedule — 1 line = 1 วันส่ง
- **PO ค้างรับ** (`PurchaseActive.vb`): เฉพาะ Balance > 0 ของบริษัทปัจจุบัน (346, 412) → เลือก line เพื่อปิด/เปิดใหม่
- **PurchaseTracking / Sum:** กรองวันที่ PO, วันส่ง, เลข PO, supplier, item (`PurchaseTracking.vb:348-386`); Sum เพิ่มราคา/มูลค่า; พิมพ์ `PurchaseOrderTracking.rpt`
- **ฝั่งรับของ** ใช้อีกสูตร: `POBal = POqty − รับสะสมของใบรับอื่น` (ไม่บวกคืน) แสดงเฉพาะ > 0 (`DK.INVEN/Transaction/Receive.vb:1808, 1840`)
- **over-receive** (`wReceiveItem.vb:112-196`): `รับสุทธิ = รับครั้งนี้ + รับสะสม − คืน`; ถ้า > POqty → หา `FNRcvOverPercent` ตามกลุ่ม + ช่วง qty; `over = รับสุทธิ − POqty × (1 + % / 100)` (ไม่มี config → `รับสุทธิ − POqty`) เก็บใน `FNRcvQtyOver`
- PR qty vs PO qty: ไม่มีการคุมยอด — PR ล็อกทั้งใบเมื่อถูกอ้าง (§2)

**สำหรับระบบใหม่:** balance คำนวณจาก `inventory.movements` ที่อ้าง `po_line_id` (รับ +, คืน supplier −) สูตรเดียวใช้ทุกหน้าจอ; PO line แตก delivery schedule ได้หลายงวด; tolerance รับเกิน/ขาด % ต่อ tenant override ต่อกลุ่ม/item; PR line balance = qty − Σ PO line qty; แจ้งเตือนของเลยกำหนดส่งผ่าน LINE/push

## 6. Link ไป flow อื่น

- PO → รับ: `TINVENReceive.FTPurchaseNo`; ใบรับดึง supplier, สกุล, rate จาก PO (`Receive.vb:1260-1290`) และ line ค้างรับ (1795-1842) → `inventory-receive.md`
- PO → คืน supplier: `TINVENReturnToSupplier.FTPurchaseNo` (`DK.INVEN/Transaction/ReturnToSupplier.vb:1078-1106`, `SP_GET_Detail_ReturnSupl` 1688) → `inventory-transfer.md`
- PR → PO: เฉพาะ `PurchaseLooupEdit.vb` (copy ยอด header + line ทั้งใบ 809-842, ห้ามเพิ่ม line 1165-1168); ใน `Purchase.vb` ถูก comment (904-950, 1851-1893); รายงานสรุปยัง join `FTPurchaseRQNo` (`wPurchaseOrderReportSummary.vb:153-167`)
- MRP: `FN_Get_PO` (open PO ใน netting) อยู่ใน DB — ดู `mrp-planning.md` §2
- รายงาน: `BIPurchaseOrder.vb`, `wBIPurchaseOrder.vb` (pivot PO/รับ/คืน รายเดือน), `wPurchaseOrderReport*.vb` → `PurchaseOrder_Report.rpt` (ตัด PO ยกเลิก `wPurchaseOrderReportSummary.vb:170`)

## 7. SP / function / view ที่เกี่ยว (ไม่มี definition ใน repo — ต้องขอ)

- `SP_GETDATASENDAPPROVEDPURCHASE` (`DK.MNG/AprovedMng.vb:25`)
- `SP_GETDATASENDAPPROVEDPURCHASE_Load`, `…_Load_D` (`Tracking/PurchaseApprove.vb:168,183`; param `@Username, @SDate, @EDate`)
- `SP_GEN_DOCUMENTNO` (`DK.TL/Document.vb:6,16`), `SP_GET_DYNAMIC_OBJECT_CONTROL` (`Purchase.vb:229`, `PurchaseRequest.vb:226`)
- `SP_GET_Detail_ReturnSupl` (`ReturnToSupplier.vb:1688`), `FN_Get_PO` (MRP)
- view `V_Unit` (`Purchase.vb:1338`); metadata `MSysTableObjForm` (field, field บังคับ, default, lookup ของฟอร์ม PR/PO)
- Crystal: `PurchaseOrder.rpt`, `PurchaseOrder_details.rpt`, `PurchaseOrderRequest.rpt`, `PurchaseOrderDelivery.rpt`, `PurchaseOrderTracking.rpt`, `PurchaseOrder_Report.rpt`

## Defects ของระบบเดิม (ห้ามยกมา)

- bulk approve **ปิด line ที่เลือกไปด้วย** (`FTStateClose = '1'`) → balance เป็น 0 ทันทีหลังอนุมัติ (`Tracking/PurchaseApprove.vb:290-311`)
- reject เขียน `'0'` แต่รายงานอ่าน `'2'`; ขั้น supervisor ไม่เคยถูกใช้ (`AprovedMng.vb:75`, `PurchaseTracking.vb:297-300`)
- ส่วนลด line 2 นิยาม (ต่อหน่วย vs ทั้ง line) ใช้ column เดียวกัน; รวมยอด header 2 สูตร (`Purchase.vb:1408` vs `1658`); ฝั่งรับคิด net price = `FNPrice − FNDisAmt` (`Receive.vb:1805`) ผิดเมื่อส่วนลดเป็นยอดทั้ง line
- เปลี่ยนราคาแล้วส่วนลดไม่คำนวณใหม่: เทียบชื่อ case ไม่ตรง (`Purchase.vb:2171`, `PurchaseAddItem.vb:56`)
- save line = ลบทั้งหมดแล้ว insert ใหม่ **นอก transaction** ของ header, exception ถูกกลืน, `FNSeq` รันใหม่ตามลำดับ grid (`Purchase.vb:952-953, 1346-1420`)
- เลข PO หา max+1 นอก transaction → ชนกันได้เมื่อ save พร้อมกัน (`Purchase.vb:2419-2438`); prefix บริษัท `B`/`JT` hard-code (2448-2453)
- ยอดคืน join ไม่มี `FNSeq` → item เดียวกันหลาย line ได้ยอดคืนซ้ำ (`Purchaseplandelivery.vb:377-383`); สูตร balance ฝั่งรับไม่ตรงกับ tracking
- config รับเกินเทียบ `FNMSysMatGrpId` กับ item id (`wReceiveItem.vb:171`) → หา % ไม่เจอ
- qty ของ price tier ถูกตัดเป็น Integer (`Purchase.vb:2109`)
- ค่า default hard-code (`PurchaseRequest.vb:436-441`), unit `PCS` (`Purchase.vb:410`), ชื่อ DB `DK_MASTER` (1322), แยก RM/PK/FG ด้วย prefix รหัส (`Purchaseplandelivery.vb:348-355`)
- SQL concat ทุก query; หลายจุดไม่ escape (`Purchase.vb:1002, 2296, 2317, 2443`; `AprovedMng.vb:25, 51`; `PurchaseApprove.vb:285`)
- empty catch ในทุกปุ่มอนุมัติ → ล้มเหลวเงียบ (`Purchase.vb:2303, 2324, 2508`; `AprovedMng.vb:59, 87`); อนุมัติทีละ statement ไม่มี transaction
- hard delete PR/PO; ไม่มี audit ของการถอนอนุมัติ (เขียนทับชื่อ/เวลาผู้อนุมัติ `Purchase.vb:2496-2501`); update `FTStatePrint` ของ PR **ไม่มี WHERE** (`PurchaseRequest.vb:1039-1044`, `Purchase.vb:1268-1273`)
- tracking ไม่กรอง PO ที่ยกเลิก; `Purchaseplandelivery`/`PurchaseTracking` ไม่กรองบริษัท (346)

## Gap ที่ต้องดู SP definition / data

1. `SP_GETDATASENDAPPROVEDPURCHASE*`: ใครเห็น PO ใบไหน (ตามกลุ่มวัตถุดิบ / วงเงิน / แผนก), มีหลายระดับไหม
2. สิทธิ์ปุ่มส่ง/อนุมัติ/ถอนในฟอร์ม PO คุมที่ไหน (`TSEPermission*`)
3. `SP_GEN_DOCUMENTNO`: รูปแบบเลข PR, reset รายปี/เดือน
4. metadata `MSysTableObjForm` ของ PR/PO: field บังคับ, default, เงื่อนไข lookup (supplier ที่เลือกได้, PO ที่ใบรับเลือกได้ — ต้องอนุมัติก่อนหรือไม่)
5. PR ใช้งานจริงไหม และ flag อนุมัติของ PR ถูก set จากที่ใด (trigger / โปรแกรมอื่น)
6. ค่า `FNPoType`, `CNSaleOrderType`, `FNPoState` ครบทุกค่าและความหมาย
7. `TPurchasePrice`: มี effective date / สกุลเงินไหม, ใครดูแล (ฟอร์มใน DK.MK)
8. `FNRcvQtyOver > 0` บล็อกการรับหรือแค่เตือน (ดูตอนถอด `inventory-receive.md`), data ใน `TINVENMConfigReceiveOver`
9. trigger บน `TPURTPurchase*` (roll-up ปิด header, sync ไป MRP/บัญชี) มีไหม
10. `FN_Get_PO`: นับ PO สถานะใดเป็น open (อนุมัติแล้ว? ตัดยกเลิก/ปิด?)
11. `FTStateSendMail`, `FTStateColorBox`, `TPURTPurchase_Revised` (revision PO) ยังใช้อยู่ไหม
12. layout/สูตรใน Crystal `PurchaseOrder.rpt` (เงื่อนไข Draft, ลายเซ็นผู้อนุมัติ)
