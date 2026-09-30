# Logic เดิม: ผลผลิตเข้าคลัง — barcode / พาเลท / QC ผลผลิต / FG approve (DK.SO Packing/*, Track/ProdOrder*ToWH*, DK.INVEN StockQCBrowse)

ถอดจาก repo DK-ERP-SYSTEM 2026-09-29 · อ้างอิง path ในระบบเดิม · DB: DK_PROD, DK_INVENTORY, DK_MASTER, SECURITY
ความหมาย barcode, ตาราง IN/OUT, ปิดสต็อก, QC วัตถุดิบ ดู inventory-receive.md · โอนคลังทั่วไป ดู inventory-transfer.md · actual ดู production-actual.md

**สรุปสำคัญ:** ผลผลิตเข้าสต็อกได้ **4 ทางที่ไม่เชื่อมกัน** และแต่ละทางสร้าง barcode ใหม่ของตัวเอง:
(1) bulk จาก actual mix → คลัง QC, (2) QC ผลผลิตผ่าน → คลังหลัง QC, (3) fill → คลัง ผ่าน SP `SP_SET_APPLOVEDTOFG`, (4) ใบส่ง FG เข้าคลัง (`TINVENTransferWH` type 5) + FG approve
ไม่มีทางใดตรวจยอดเทียบ actual / แผน, ไม่มี pack size, **1 บรรทัด = 1 พาเลท = 1 barcode** โดยผู้ใช้พิมพ์จำนวนและเลขพาเลทเอง · FG approve ไม่ตรวจผล QC
การยกเลิก = ลบแถว IN/OUT/barcode · ต้นทุน barcode ผลผลิต = 0 เสมอ
→ ระบบใหม่: ผลผลิตเข้าคลังผ่าน inventory Receipt ที่อ้าง work order เส้นทางเดียว สร้าง lot + handling unit และ lot status คุม QC

## 1. ตาราง

- `TPDTProdActual` / `TPDTProdActualFill`: flag `FTStateAppToFG` ('0'/'1'), `FTStateAppToFGBy`, `FDStateAppToFGDate`, `FTStateAppToFGTime` (`DK.SO/Actual/RouteActual.vb:1287-1296`, `Track/ProdOrderMixToWH.vb:411-420`); QC: `FTStateQC, FTStateQCPass, FTStateQCNotPass, FTDateExpire` (`DK.INVEN/Transaction/StockQCBrowse.vb:1261-1263`)
- `TINVENBarcode` + `TINVENBarcode_IN` ของผลผลิต: `FTDocumentNo` = เลขแผน (ทาง 1), เลข QC (ทาง 2), เลขใบส่ง FG (ทาง 4); `FNPrice = 0`, `FTDateExpire = ''`, `FTGrade = ''`, `FTPurchaseNo = ''`, `FTOrderNo = ''`, `FTBatchNo` = batch ผลิต (`RouteActual.vb:1412-1438`, `ProdOrderMixToWH.vb:459-485`, `StockQCBrowse.vb:1344-1370`, `Packing/ProdOrderFillToTWH.vb:1462-1475`)
- `TINVENTransferWH` header ใบส่ง FG: `FTTransferWHNo, FDTransferWHDate, FTTransferWHBy, FNMSysWHId` + location ต้นทาง, `FNMSysWHIdTo` + `FNMSysWHLocId` ปลายทาง, `FNTransferWHType = 5`, `FTStateApprove, FTApproveBy, FDApproveDate, FTApproveTime, FTCancelBy, FDCancelDate, FTCancelTime, FTStateCustWH, FTReferNo, FTRemark` — header เขียน generic จาก dynamic form (`ProdOrderFillToTWH.vb:1030-1032`); flag ที่ 2428-2433, 2462-2467
- `TINVENTransferWH_Detail`: `FTBarcodeNo, FTDocumentNo, FNMSysWHId, FNMSysWHLocId, FTOrderNo (= เลขแผน), FNQuantity, FNSeq, FNRawmatPrice, FNMSysUnitId, FNPalletQty, FTStateReserve, FTDocumentRefNo, FNMSysCmpId` (`ProdOrderFillToTWH.vb:1389-1403`)
- `TINVENQC` (+ `_Detail`, `_Result`) ใช้ร่วมกับ QC วัตถุดิบ โดย `FTReceiveNo` = เลขแผน และ `FNGrpType = 1` (`StockQCBrowse.vb:1238, 1262`)
- config ใน `SECURITY.TSESystemConfig`: `CfgWHMixQC`, `CfgLogMixQC` (`RouteActual.vb:1273-1276`); `CfgWHAfterQCToFG`, `CfgLogAfterQCToFG`, `CfgWHMixAfterQC`, `CfgLogMixAfterQC` (`StockQCBrowse.vb:1288-1297`)

**สำหรับระบบใหม่:** `inventory.receipts` (type = `PRODUCTION`, อ้าง `work_order_id`) + `receipt_lines` (item, lot, qty, handling unit) → movement `PRODUCTION_RECEIPT` ใน `inventory.movements`; `lots` มี `mfg_date, expiry_date, status, source_work_order_id`; `handling_units` (พาเลท/ลัง) มี label ของตัวเองผูก lot; ยอด `produced_qty` ของ work order มาจาก receipt ที่ Post แล้วเท่านั้น

## 2. เลข barcode, batch, วันหมดอายุ

- เลข barcode: `EXEC SP_GEN_BARCODE_NO '<CmpRunID>'` (`RouteActual.vb:1410`, `ProdOrderMixToWH.vb:457`, `StockQCBrowse.vb:1339`, `ProdOrderFillToTWH.vb:1376`) — รูปแบบ prefix อยู่ใน SP; VB รู้แค่ว่า **6 หลักท้ายเป็นเลขรัน**
- หลาย barcode ในคำสั่งเดียว: `barcode ตัวที่ n = ส่วนหน้า (ตัด 6 หลักท้าย) + (เลข 6 หลักท้าย + n − 1)` เติม 0 ให้ครบ 6 หลัก เรียงตาม `FTBatchNo` (`RouteActual.vb:1418`, `ProdOrderMixToWH.vb:465`, `StockQCBrowse.vb:1350`) — SP ถูกเรียกครั้งเดียว จึงไม่รู้ว่าเลขถัดไปถูกใช้ไปแล้ว
- batch/lot: mix = `genbatchmixno(item)`, fill = `genbatchfillno(item)` หรือพิมพ์เอง (production-actual.md §3-4); ใบส่ง FG พิมพ์ batch เองในแต่ละบรรทัด ยาวไม่เกิน 30 ตัว (`ProdOrderFillToTWH.Designer.vb:1089-1107`)
- วันหมดอายุ: barcode ผลผลิตถูกสร้างด้วย `FTDateExpire = ''`; กำหนดตอน QC ผลผลิต โดย default `วันหมดอายุ = วันผลิต (FDProdDate) + 2 ปี` hard-code แก้ได้ก่อนอนุมัติ (`StockQCBrowse.vb:1806-1812`) แล้วเขียนลง `TPDTProdActual` และ barcode ของเลขแผน (1261-1273)
- ต้นทุน: `FNPrice = 0` ทุกทาง (`RouteActual.vb:1420`, `ProdOrderFillToTWH.vb:1470`)

**สำหรับระบบใหม่:** lot no ออกจาก sequence ต่อ tenant ต่อประเภท ใน transaction (format เป็น tenant setting เช่น `{item}{yyMMdd}{run}`); label ของ handling unit ออกทีละใบจาก sequence ไม่บวกเลขเอง; `expiry = mfg_date + shelf_life ของ item`; ต้นทุน lot = ต้นทุนวัตถุดิบที่เบิก + ค่าแรง/overhead ตาม policy

## 3. ทาง 1 — bulk จาก mix เข้าคลัง QC

**A. ปุ่ม Approve ในฟอร์ม actual mix (`RouteActual.vb:1263-1307`)**
1. คลัง/location ปลายทางจาก config `CfgWHMixQC` / `CfgLogMixQC`
2. ต่อ batch ที่ติ๊กเลือกและ `FTStateAppToFG = '0'`: `toWHMix` (1393-1454) สร้าง barcode 1 ใบต่อแถว `TPDTProdActual` ของ แผน + batch + item; `FNQuantity = FNProcessQty`; เขียน `TINVENBarcode_IN` (`FTDocumentNo = FTDocumentRefNo = เลขแผน`)
3. set `FTStateAppToFG = '1'` + ผู้อนุมัติ/วัน/เวลา (1287-1296)

**B. หน้ารวม `Track/ProdOrderMixToWH.vb`**
- รายการจาก `fn_GetProductMixToWh()` (264); ผู้ใช้เลือกคลัง/location เองผ่าน popup `SelectWH` (390-400) — ไม่ใช้ config
- `toWHMix` (442-499): **ลบ barcode + IN ทั้งหมดของเลขแผนก่อน** (447-454) แล้วสร้างใหม่เฉพาะ batch ที่เลือก; `FNQuantity = FNProcessQty + FNBlendQty` (467) → ต่างจากข้อ A
- ไม่กรอง `FTStateAppToFG` ตอนเลือก (387)

**ยกเลิก (`RouteActual.vb:1353-1390`):** ลบ `TINVENBarcode` + `TINVENBarcode_IN` **ทั้งหมดของเลขแผน** แล้ว set `FTStateAppToFG = '0'` เฉพาะ batch ที่เลือก; ไม่ตรวจว่า barcode ถูกเบิก/โอนแล้ว, ไม่ตรวจปิดสต็อก

**สำหรับระบบใหม่:** เมื่อปิด run ของ operation ที่ตั้งค่า "ให้ผลผลิตเป็น lot" ระบบสร้าง Receipt draft อัตโนมัติ ยอด = `good_qty` ของ run; location ปลายทาง default ต่อ operation/item type (tenant setting) แก้ได้ตามสิทธิ์; lot เข้าเป็น `Quarantine` ถ้า item ต้อง QC; void = movement กลับรายการ ทำไม่ได้ถ้า lot ถูกใช้แล้ว

## 4. ทาง 2 — QC ผลผลิต (`DK.INVEN/Transaction/StockQCBrowse.vb`)

- ใบ QC อ้าง `FTReceiveNo` = เลขแผน + item; `FNQuantityRcv = Σ TPDTProdActual.FNProcessQty` ของ แผน + item (1794-1800); หัวข้อตรวจ/ผลตรวจโครงเดียวกับ QC วัตถุดิบ
- `FNQCType`: 0 รอ, 1 ผ่าน, 2 reject; > 0 แล้วกด approve/reject ซ้ำไม่ได้ (1493-1495, 1739-1741)

| ปุ่ม | ผล |
|---|---|
| Approve (1242-1319, 1488-1539) | `FNQCType = 1`; `TPDTProdActual` ของ แผน + item และ barcode ที่ `FTDocumentNo = เลขแผน` → `FTStateQC='1', FTStateQCPass='1', FTStateQCNotPass='0', FTDateExpire`; แล้ว `toWHM` สร้าง barcode ชุดใหม่เข้าคลังปลายทาง |
| Revoke (1391-1438, 1572-1627) | `FNQCType = 0`, ล้าง flag + expiry; ห้ามถ้ามี `TINVENBarcode_OUT.FTDocumentRefNo = เลขแผน` (`Barcode.vb:24-28`); **ไม่ลบ** barcode ที่ `toWHM` สร้าง |
| Reject (1440-1486, 1734-1781) | `FNQCType = 2`, flag not pass; ไม่มี movement |

- คลังปลายทาง: อักษรตัวแรกของรหัสกลุ่มวัตถุดิบ = `D` → `CfgWHAfterQCToFG` / `CfgLogAfterQCToFG` ไม่งั้น → `CfgWHMixAfterQC` / `CfgLogMixAfterQC` (1281-1298)
- `toWHM` (1322-1386): ลบ barcode + IN ที่ `FTDocumentNo = เลข QC` แล้วสร้าง barcode 1 ใบต่อแถว `TPDTProdActual` ของ แผน + item (ทุก batch) โดย `FNQuantity = FNQuantityRcv` ของใบ QC (1352); **ไม่เขียน OUT** จากคลัง QC

**สำหรับระบบใหม่:** QC ผลผลิต = เปลี่ยน lot status `Quarantine → Released | Rejected` บน lot เดิม ไม่สร้าง lot/label ใหม่; ย้ายคลังหลัง QC เป็น movement `TRANSFER` คู่ (ออก–เข้า) ตาม setting; ตัวอย่าง QC เป็น movement `QC_SAMPLE`; revoke ต้อง reverse ทุก movement ที่ approve สร้าง

## 5. ทาง 3 — fill เข้าคลังผ่าน SP (`Track/ProdOrderFillToWH*.vb`)

- `ProdOrderFillToWH.vb`: รายการจาก `fn_GetProductFillToWh()` (264); ต้องมีแถวที่ติ๊กและ `FTStateAppToFG <> '1'` อย่างน้อย 1 แถว (387) → เลือกคลัง/location จาก `SelectWH` (390-400) → ต่อแถวที่ติ๊ก **ทุกแถว** เรียก `SP_SET_APPLOVEDTOFG(เลขแผน, user, เลขแผน, WH, location, cmp)` (407-410)
- `ProdOrderFillToWHPallet.vb`: รายการระดับพาเลทจาก `fn_GetProductFillToWhPallet()` (264) มี `FTBatchNo, FTPalletNo, FTRefNo` (`ProdOrderFillToWHPallet.designer.vb:290-433`); parameter ตัวที่ 3 ของ SP = `FTBatchNo` (411); ไม่กรองแถวที่อนุมัติแล้ว (390)
- `ProductionOrderActive.vb`: รายการจาก `fn_GetProductToFG()` (264); ปุ่มอนุมัติส่งคำสั่งสะกดผิด `exce SP_GEN_BARCODE_NO '<แผน>','<user>'` (395) → error ถูกกลืน ไม่ทำอะไร แต่แจ้งว่าสำเร็จ
- สิ่งที่ SP ทำ (สร้าง barcode, จำนวนต่อพาเลท, set flag) อ่านจากโค้ดไม่ได้ → Gap 1
- ทั้งสามฟอร์มไม่มีปุ่มยกเลิก; `ocmreopenstate_Click` เป็นโค้ด re-open PO ที่ copy มา ไม่มี `Handles` (`ProdOrderFillToWH.vb:432`, `ProdOrderMixToWH.vb:501`)

## 6. ทาง 4 — ใบส่ง FG เข้าคลัง + FG approve (`Packing/*`)

**สร้างใบส่ง (`ProdOrderFillToTWH.vb`)** — โครงจากฟอร์มโอนคลัง
- `FNTransferWHType = 5`; คลังต้นทาง default รหัส `2600` location `J001` hard-code (2014-2021); เลขเอกสาร `DK.TL.Document.GetDocumentNo(…, "5", …)` (836)
- บรรทัด (พิมพ์เอง): เลขแผน (`FTOrderNo`, เลือกจาก browse 370; `Designer.vb:1239-1252`), item, batch, `FNQuantity`, `FNPalletQty` (caption "พาเลทที่" = เลขลำดับพาเลท จำนวนเต็ม; `Designer.vb:1217-1236`), ราคา; `FTRefSalesID` = `Appointments.FTSaleOrderNoRef` (456, 513)
- validation: `FNQuantity > 0`, `FNPalletQty > 0`, batch ไม่ว่าง (752-790) — **ไม่เทียบกับยอด actual, ยอดแผน หรือ on-hand** (`chevkOnhand` ถูก comment ที่จุดเรียก; 2184-2211)
- Save → `SaveBarcodeGrid` (1349-1523): ลบ `TINVENBarcode_OUT` + `_Detail` ของใบ แล้วต่อบรรทัด: ขอเลข barcode ใหม่ (1376) → เขียน `_Detail` → `TINVENBarcode_IN` ที่คลัง/location ต้นทาง → `TINVENBarcode` → `TINVENBarcode_OUT` ที่คลัง/location ต้นทาง จำนวนเท่ากัน
  ผล: ที่คลังผลิต เข้า = ออก (ยอดสุทธิ 0) ของรออนุมัติรับ; **1 บรรทัด = 1 barcode = 1 พาเลท**
- header ล็อกคลังต้นทาง/ปลายทาง/location เมื่อมีบรรทัด (2364-2381)
- ฝากขาย `FTStateCustWH`: `FNAmt = Σ qty × ราคา`, `FNVatAmt = FNAmt × 0.07`, `FNNetAmt = FNAmt + FNVatAmt` (2386-2396)
- พิมพ์ `FGToWarehouseSlip.rpt` (1272-1288)

**อนุมัติรับเข้า**
| ฟอร์ม | Approve | ยกเลิก |
|---|---|---|
| `ProdOrderFillToTWH.vb` | 2450-2484: `FTStateApprove='1'` + ผู้อนุมัติ/วัน/เวลา → ต่อบรรทัดเขียน `TINVENBarcode_IN` ที่คลัง/location ปลายทาง จำนวน = `FNQuantity` (2489-2586) | 2411-2447: `FTStateApprove='0'` + `FTCancel*` แล้วลบ IN **และ OUT ทั้งใบ**; การตรวจว่ามี transaction ต่อถูก comment (2414-2417) |
| `ProdOrderFillToTWHFGApprove.vb` | 1101-1128, 1161-1203: เหมือนกัน จำนวน = `FNApproveQty` (= `FNQuantity` ของบรรทัด; 413-416); โหลดเฉพาะใบ type 5 (329) | 1130-1159: ห้ามถ้ามี OUT ที่ `FTDocumentRefNo = เลขใบ` (`Barcode.vb:75-83`); ลบเฉพาะ IN ของใบ |

**FG approve ตรวจอะไร:** (1) ปิดสต็อกของคลังต้นทาง/ปลายทาง ณ วันที่เอกสาร (`Barcode.CheckCloseStock`; 1103-1109), (2) ใบยังไม่อนุมัติ (`WHERE FTStateApprove <> '1'`; 1117), (3) ต่อ barcode: ไม่มี OUT อ้างใบนี้แล้ว (`CheckTransactionOUT`; 1191) — **ไม่ตรวจ** ผล QC, วันหมดอายุ, ยอดเทียบ actual, สิทธิ์ตามคลัง; อนุมัติขั้นเดียว รับเต็มจำนวนเท่านั้น
**ล็อก:** อนุมัติแล้ว save/ลบ/สแกน/ลบบรรทัดไม่ได้ (`ProdOrderFillToTWH.vb:1207-1214, 1249-1256, 1696-1703, 1971-1978`); คลังปลายทางแก้ไม่ได้หลังอนุมัติ (`…FGApprove.vb:1205-1211`)
**ลบเอกสาร:** ลบจริง header, detail, OUT, IN, barcode (`ProdOrderFillToTWH.vb:1069-1111`)
**`PackingFG.vb`:** สำเนาฟอร์ม actual fill + tree ลัง/พาเลท; query ลังถูก comment (372-391), save header ถูก comment (732 เป็นต้นไป), ปุ่มเพิ่มรายการแสดง `fn_GetProductFGToPack()` แล้วไม่ทำอะไรต่อ (1413-1444) = ยังทำไม่เสร็จ

**พาเลท / pack size / จำนวน label:** ไม่มีการคำนวณ — ไม่มี master ขนาดบรรจุ/จำนวนต่อพาเลทในโค้ดส่วนนี้ ผู้ใช้แบ่งพาเลทเองโดยเพิ่มบรรทัด; จำนวน label = จำนวนบรรทัด

**สำหรับระบบใหม่:**
- Receipt จาก work order: `จำนวนพาเลท = ceil(qty / units_per_pallet)`, พาเลทสุดท้ายรับเศษ; `units_per_pallet`, `units_per_case` เป็น packaging level ของ item (tenant ตั้งเอง) และแก้ต่อใบได้
- ตรวจ `Σ receipt qty ≤ good_qty ของ work order × (1 + tolerance)`; เกิน → block / warn / ต้องอนุมัติ (tenant setting)
- FG approve = ขั้นอนุมัติของ Receipt (จำนวนขั้น config ต่อ tenant) + putaway; เงื่อนไข: lot `Released` (ถ้า item ต้อง QC), งวดยังไม่ปิด, ผู้อนุมัติมีสิทธิ์คลังปลายทาง; รับบางส่วนได้
- mobile: สแกน label พาเลท → ยืนยันรับเข้า location

## 7. สถานะและ flow

| เอกสาร | Flag | ค่า | เปลี่ยนโดย |
|---|---|---|---|
| actual mix | `FTStateAppToFG` | 0/1 | approve `RouteActual.vb:1288`, `ProdOrderMixToWH.vb:412`; revoke `RouteActual.vb:1372` |
| actual fill | `FTStateAppToFG` | 0/1 | `SP_SET_APPLOVEDTOFG` (ไม่มี set ใน VB) |
| QC ผลผลิต | `FNQCType` | 0/1/2 | `StockQCBrowse.vb:1253, 1401, 1451` |
| ใบส่ง FG | `FTStateApprove` | ''/0/1 | approve 2462, revoke 2428 (`ProdOrderFillToTWH.vb`) |

ไม่มีสถานะส่งอนุมัติ/ปฏิเสธ และไม่มีการปิด PD/แผนจากการเข้าคลัง (mrp-planning.md §6)

**สำหรับระบบใหม่ (state machine):** ProductionReceipt: `Draft → Submitted → Approved (N ขั้น) → Posted` + `Rejected`, `Voided`; Lot: `Quarantine → Released | Rejected` (+ `OnHold`); HandlingUnit: `Created → Labeled → Stored → Picked/Shipped`; ทุกการเปลี่ยนสถานะมี audit log

## 8. Links ไป flow อื่น

- actual → เข้าคลัง: ยอดมาจาก `TPDTProdActual.FNProcessQty` (ทาง 1, 2) หรือผู้ใช้พิมพ์ (ทาง 4)
- QC: §4; ฟอร์มเบิกไม่ตรวจ `FTStateQC` (inventory-receive.md §4)
- FG stock → ขาย: เบิก FG ตาม SO ใช้ barcode เหล่านี้ (inventory-issue.md §5); `FTRefSalesID` บนใบส่ง FG; SO tracking ใช้ `TPDTProdActualFill.FTStateAppToFG = '1'` (`Production/SaleOrderStatus.vb:365-369`)
- bulk → fill: bulk ในคลังหลัง QC ถูกเบิกด้วย `Issue_bulk.vb` ตามแผน fill (inventory-issue.md §2)
- ปิดสต็อก: `Barcode.CheckCloseStock` (inventory-close.md)

## 9. SP / function ที่เกี่ยว (ไม่มี definition ใน repo — ต้องขอ)

- SP: `SP_SET_APPLOVEDTOFG` (`ProdOrderFillToWH.vb:408`, `ProdOrderFillToWHPallet.vb:411`), `SP_GEN_BARCODE_NO` (call site §2 + `ProductionOrderActive.vb:395`), `SP_GET_BARCODE_BALANCE_BATCH_FG` (`DK.INVEN/Barcode.vb:139-148` เรียกจาก `ProdOrderFillToTWH.vb:1706`), `SP_GETTINVENQC_Result` (`StockQCBrowse.vb:523`), `SP_GET_DYNAMIC_OBJECT_CONTROL`
- function: `fn_GetProductMixToWh`, `fn_GetProductFillToWh`, `fn_GetProductFillToWhPallet` (มีแบบรับเลขแผน: `RouteActualFill.vb:939`), `fn_GetProductToFG`, `fn_GetProductFGToPack` (`PackingFG.vb:1418`)
- view: `V_Material_Batch_Balance` (`ProdOrderFillToTWH.vb:1563, 2163`), browse 370 (`V_Browse_370`; 512)

## Defects ของระบบเดิม (ห้ามยกมา)

- 4 ทางเข้าคลังไม่เชื่อมกัน ผลผลิตเดียวกันถูกนับเข้าได้หลายครั้ง (เลขแผน, เลข QC, เลขใบส่ง FG) และ QC ผ่านไม่เขียน OUT จากคลัง QC (`StockQCBrowse.vb:1322-1386`)
- `StockQCBrowse.toWHM` ใส่ยอดรวม `FNQuantityRcv` ให้ **ทุก** barcode ต่อแถว actual (1352) → หลาย batch ยอดเข้าคูณจำนวน batch; รันคนละ transaction กับ `ApproveQC` และไม่ตรวจผล (1304)
- `RouteActual.toWHMix`: เงื่อนไขลบใช้ batch ไม่ใส่ quote และลบ IN ด้วย `FTOrderNo = batch` ทั้งที่ IN เขียน `FTOrderNo = ''` (1399-1407); insert IN เลือก**ทุก** barcode ของเลขแผน (1436-1438) → อนุมัติ batch ที่ 2 แล้ว IN ของ batch แรกซ้ำ; คำสั่งลบรันนอก transaction
- ยอดเข้าคลังของ bulk ไม่ตรงกันระหว่าง 2 ฟอร์ม (`FNProcessQty` กับ `FNProcessQty + FNBlendQty`; `RouteActual.vb:1420`, `ProdOrderMixToWH.vb:467`); `ProdOrderMixToWH` ลบ barcode ของ batch อื่นในแผนเดียวกัน (447-454) แต่ flag ของ batch เหล่านั้นยังเป็น '1'
- ยกเลิก mix ลบ barcode ทั้งแผนโดยไม่ตรวจว่าถูกใช้แล้ว (`RouteActual.vb:1362-1368`)
- `SaveBarcodeGrid` ลบเฉพาะ OUT + detail แต่ไม่ลบ `TINVENBarcode` / `TINVENBarcode_IN` เดิม (1361-1370) → save ซ้ำทุกครั้งได้ barcode ใหม่ + IN เก่าค้าง ยอดคลังผลิตเพิ่มเอง; `SaveData` ไม่ตรวจผล `SaveBarcodeGrid` (1047)
- revoke ใน `ProdOrderFillToTWH` ลบ IN/OUT ทั้งใบรวมคู่ต้นทาง โดยไม่ตรวจ transaction ต่อเนื่อง (2414-2439)
- `DeleteBarcode` มี SQL ผิดรูป (`Update FROM …`; 1604) ; `CheckTransactionOUT` เทียบกับ `TINVENMMaterial.FTBarcodeNo` (`Barcode.vb:39-41`) → การกันรับซ้ำไม่น่าเชื่อถือ
- `ProdOrderFillToWH` ตรวจ "ยังไม่อนุมัติ" แค่ว่ามีอย่างน้อย 1 แถว แล้วส่งทุกแถวที่ติ๊กเข้า SP (387, 407); ส่งเลขแผนแทน batch (408)
- `ProductionOrderActive` คำสั่งสะกดผิดแต่แจ้งสำเร็จ (`ProductionOrderActive.vb:395`)
- `RejectQC` อ้าง `TPDTProdActual` ผิด DB (`StockQCBrowse.vb:1459`); QC transaction เปิดบน connection MAR (1249)
- คลัง `2600` / location `J001`, อายุ 2 ปี, VAT 7%, prefix กลุ่ม `D` ฝังในโค้ด (`ProdOrderFillToTWH.vb:2020-2021, 2395`; `StockQCBrowse.vb:1287, 1808`)
- ต้นทุน barcode ผลผลิต = 0; ลบเอกสารจริงไม่มี audit; SQL concat; empty catch; `NOLOCK`

## Gap ที่ต้องดู SP definition / data

1. `SP_SET_APPLOVEDTOFG`: สร้าง barcode/IN อย่างไร, จำนวนต่อพาเลทมาจากไหน, ความหมาย parameter ตัวที่ 3 (เลขแผน หรือ batch), set `FTStateAppToFG` ที่ตารางใด, กันเรียกซ้ำไหม
2. `SP_GEN_BARCODE_NO`: รูปแบบเลข, จอง running กี่เลขต่อครั้ง (VB บวกเลขเองหลังเรียกครั้งเดียว), มี overload 2 parameter จริงไหม
3. `fn_GetProductMixToWh` / `fn_GetProductFillToWh` / `fn_GetProductFillToWhPallet` / `fn_GetProductToFG`: เงื่อนไขว่า "พร้อมเข้าคลัง", ที่มาของ `FTPalletNo`, `FTRefNo`
4. ตารางพาเลทของ fill: มีตารางเก็บพาเลท/ขนาดบรรจุต่อ item ใน DB หรือไม่ (ไม่พบในโค้ด)
5. ทางเข้าคลัง FG ที่ใช้จริงในโรงงาน: ทาง 3 หรือทาง 4 หรือทั้งคู่ และลำดับกับ QC ผลผลิต
6. ค่า config `CfgWHMixQC`, `CfgLogMixQC`, `CfgWHAfterQCToFG`, `CfgLogAfterQCToFG`, `CfgWHMixAfterQC`, `CfgLogMixAfterQC` และผังคลังจริง (2600 / J001 คืออะไร)
7. รหัสกลุ่มวัตถุดิบที่ขึ้นต้น `D` คือกลุ่มใด (FG?) และกลุ่มอื่นที่ผ่าน QC ผลผลิต
8. อายุสินค้า 2 ปีใช้กับทุกผลิตภัณฑ์หรือไม่; มี shelf life ต่อ item ใน master ไหม
9. `SP_GET_BARCODE_BALANCE_BATCH_FG`: สูตรยอดคงเหลือและคอลัมน์ `FTBarcodeNoRef`
10. ข้อมูลจริง: จำนวน barcode ผลผลิตที่ซ้ำ (เลขแผนเดียวมีทั้ง doc แผน / QC / ใบส่ง FG) เพื่อวางแผน migrate ยอดยกมา
11. สิทธิ์ปุ่ม approve / revoke ของแต่ละฟอร์ม (`TSEPermission*`) และใครเป็นผู้อนุมัติ FG
12. trigger บน `TINVENTransferWH`, `TINVENBarcode*`, `TPDTProdActual*` มีไหม (ต้นทุน, stock card, สถานะ PD)
