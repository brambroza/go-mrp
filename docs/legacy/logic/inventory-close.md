# Logic เดิม: ปรับสต็อก / นับสต็อก / ปิดสต็อกรายเดือน / stock card + on-hand (DK.INVEN)

ถอดจาก repo DK-ERP-SYSTEM 2026-09-29 · อ้างอิง path ในระบบเดิม · DB: DK_INVENTORY (INVEN), DK_MASTER
path ย่อ: `T/` = `DK.INVEN/Transaction/`, `R/` = `DK.INVEN/Report/` · โครง `TINVENBarcode`, `TINVENBarcode_IN/OUT` ดู inventory-transfer.md §1

**สรุปสำคัญ:** logic หลักของ flow นี้**อยู่ใน stored procedure ทั้งหมด** (`SP_CloseStock_Monthly`, `SP_ReOpenStock_Monthly`, `SP_Stock_Onhand*`) ซึ่งไม่มีใน repo — โค้ด VB เป็นแค่ตัวเรียกและตัวแสดงผล
สิ่งที่ยืนยันได้จากโค้ด: (1) **ไม่มีฟอร์มนับสต็อก** ผลนับถูกคีย์เป็นใบปรับสต็อกเอง (2) ปิดเดือนทำต่อคลัง ล็อกด้วยการเทียบวันที่เอกสารกับเดือนที่ปิดล่าสุด และ **reopen ได้** (3) ใบปรับสต็อกไม่มีรหัสเหตุผล มีแค่ remark
→ ระบบใหม่ต้องมี count sheet → variance → adjustment, เหตุผลปรับเป็น master, ปิดงวดเป็น snapshot + lock ที่ตรวจในชั้น domain

## 1. ตาราง

- `TINVENAdjustStock(FTAdjustStockNo, FDAdjustStockDate, FTAdjustStockBy, FNMSysWHId, FNMSysWHLocId, FNMSysCmpId, FNAdjustType, FTStateApprove, FTApproveUser/FDApproveDate/FTApproveTime, FTRevokeUser/FDRevokeDate/FTRevokeTime, FTRemark)` — header ผ่าน dynamic form (`T/AdjustStock.vb:960`), flag ที่ 2584, 2641
- `TINVENAdjustStock_AddIn_Detail(FTAdjustStockNo, FTPurchaseNo, FTOrderNo, FNMSysRawMatId, FNMSysUnitId, FNPrice, FNQuantity, FTBatchNo, FTDateExpire, FTFabricFrontSize)` — เขียนที่ `T/AdjustStock.vb:1015-1030`, 2416 (ชื่อ AddIn แต่ฟอร์ม FG ใช้เก็บรายการปรับลดด้วย; `T/AdjustStockFG.vb:1153`)
- ปรับเพิ่ม → `TINVENBarcode` (barcode ใหม่) + `TINVENBarcode_IN` (`T/AdjustStock.vb:1129-1155`); ปรับลด → `TINVENBarcode_OUT` (1418-1427)
- `TINVENStockLastMonthly(FNMSysWHId, FTYear, FTMonth, …)` — ตารางผลปิดเดือน; โค้ด VB **อ่านอย่างเดียว** 2 จุด (`DK.INVEN/Barcode.vb:365-368`, `T/CloseStockMonthly.vb:300-302`) ผู้เขียนคือ SP; คอลัมน์ยอดไม่ปรากฏในโค้ด
- `TINVENTempStockTransaction`, `TINVENTempStockTransactionBatch`, `TINVENTempStockTransactionReserve(FTUserLogIn, FNSeq, FNMSysWHId, FNMSysWHLocId, FNMSysRawMatId, FTBatchNo, FTOrderNo, FTDocumentNo, FTDocType, FTDocumentBy, FTDocumentDate, FTDocumentTime, FNQuantity, FNAmt, FNReserveQuantity, FNReserveAmt, FNPOQuantity, FNPOAmt, FNIntransitQuantity, FNIntransitAmt)` — ตารางพักผลรายงานต่อ user เติมโดย SP (`R/StockCard.vb:312-357`, `R/StockOnhand.vb:308-312`)

## 2. ปรับสต็อก (AdjustStock = RM/PK, AdjustStockFG = FG)

แยกฟอร์มด้วย `FNAdjustType`: 0 = RM/PK (`T/AdjustStock.vb:362, 623`), 1 = FG (`T/AdjustStockFG.vb:624`) · เอกสารเดียวมี 2 tab: ปรับเพิ่ม (`otbadjin`) และปรับลด (`otbadjout`); การ save/approve ทำงานกับ **tab ที่เปิดอยู่เท่านั้น** (`T/AdjustStock.vb:975, 1122`)

**ปรับเพิ่ม (RM/PK)**
1. กรอกแถว item, PO อ้างอิง, batch, วันหมดอายุ, qty, ราคา; save เก็บเฉพาะแถวที่มี item + `FTPurchaseNo` + `FTDateExpire` + `qty > 0` (990) → **ปรับเพิ่มต้องอ้างเลข PO เสมอ**
2. batch ว่าง → สร้างด้วย `Gen_BatchNo(item, unit, company)` (1009-1011)
3. เส้นทางหลักขยับสต็อกตอน **approve** (แต่ดู Defects เรื่อง `GenBarcode` ตอน save และ Gap 14): ขอเลขตั้งต้นจาก `SP_GEN_BARCODE_NO` แล้วสร้าง barcode 1 ตัวต่อแถว detail (เลขตั้งต้น + ลำดับแถว) ลง `TINVENBarcode` พร้อมราคา/batch/วันหมดอายุ แล้ว copy เป็น `TINVENBarcode_IN` ที่คลัง/location ของ header (1126-1155)
4. approve set `FTStateApprove='1'` + ผู้อนุมัติ (2572-2590)
5. revoke: ห้ามถ้า barcode ของเอกสารถูกจ่ายออกแล้ว (`CheckDucumentDocRcvIssue`; 2623) → ลบ `TINVENBarcode` + `TINVENBarcode_IN` ของเอกสาร แล้ว set 0 (2641-2669)

**ปรับลด (RM/PK)**
1. สแกน barcode → `SP_GET_BARCODE_BALANCE` (ไม่ merge reserve) → ต้องเป็นของคลังใน header และ `FNQuantityBal ≥ qty > 0` → เขียน `TINVENBarcode_OUT` ทันที (1569-1625, 1414-1427)
2. หรือกรอกแถว item + batch + qty: หา barcode จาก `V_Material_Barcode_Balance` ที่คลัง/location/batch ตรงและ `FNQuantityBal2 ≥ qty`; ถ้าไม่มีตัวเดียวที่พอ ไล่ตัดหลาย barcode ที่ `FNQuantityBal2 > 0` จนครบ (1057-1092)
3. → **ปรับลดขยับสต็อกตั้งแต่ save/สแกน ก่อน approve** และ revoke ไม่ลบ OUT (2660-2688)

**FG (`T/AdjustStockFG.vb`)** ต่างจาก RM: ปรับลดเขียน OUT ตอน approve จาก detail โดย join `TINVENBarcode` ด้วย **item อย่างเดียว** เอา `MAX(FTBarcodeNo)` (1137-1157); revoke ลบทั้ง barcode, IN และ OUT (2699-2709)

**กฎร่วม**
- approve แล้วแก้/ลบ/เพิ่มแถวไม่ได้ (`T/AdjustStock.vb:1337-1338, 1360-1361, 1789`); ลบเอกสารไม่ได้ถ้ามี OUT อ้างถึง (1352); ลบเอกสาร = ลบ header, detail, barcode, IN, OUT (1208-1227)
- ทุก action ตรวจ `CheckCloseStock(คลัง, FDAdjustStockDate)` (1333, 1356, 1693, 1749, 1781, 2000, 2047, 2573, 2628)
- **เหตุผลการปรับ:** ไม่มี field รหัสเหตุผล/ประเภท มีเฉพาะ `FTRemark` (`T/AdjustStock.Designer.vb:60`); ทิศทาง +/− รู้จาก tab
- ต้นทุน: ปรับเพิ่มใช้ราคาที่ผู้ใช้กรอก (`FNPrice`); ปรับลดไม่ระบุราคา
- รายงาน `R/AdjustStockTracking.vb`: ปรับลดอ่านจาก header + OUT + barcode (112-114), ปรับเพิ่มอ่านจาก header + detail (151-153)

**สำหรับระบบใหม่:** Adjustment document → movement ชนิด `adjust_in` / `adjust_out` ลง `inventory.movements` เมื่อ **Posted เท่านั้น** (ทั้งสองทิศทาง); ทุกบรรทัดบังคับ reason code (master ต่อ tenant: นับเกิน/ขาด, เสียหาย, หมดอายุ, ยอดยกมา, แก้ข้อมูลผิด ฯลฯ); ปรับเพิ่มสร้าง lot ใหม่หรือเข้า lot เดิมได้ ไม่บังคับอ้าง PO; ต้นทุนปรับเพิ่ม default = ต้นทุนเฉลี่ย/lot ล่าสุด แก้ได้ตามสิทธิ์; ขั้นอนุมัติและวงเงิน/ปริมาณที่ต้องอนุมัติเป็น tenant setting; ยกเลิก = reversal movement

## 3. นับสต็อก

- **ไม่มีฟอร์ม/ตารางนับสต็อกใน DK.INVEN** — ค้นชื่อไฟล์และชื่อตาราง count/check ไม่พบ (มีแค่ `ReCheckQC` ซึ่งเป็น QC)
- `R/wInvenImportExcelFile.vb` เป็น import PO/ใบรับจาก Excel (เขียน `TPURTPurchase`, `TINVENReceive`; 262, 545) ไม่ใช่ import ผลนับ
- กระบวนการจริงที่อนุมานได้จากเครื่องมือที่มี: พิมพ์ on-hand → นับมือ → คีย์ส่วนต่างเป็นใบปรับสต็อก เพิ่ม/ลด → ปิดเดือน (ต้องยืนยันกับผู้ใช้ — ดู Gap)

**สำหรับระบบใหม่:** CountSession (คลัง/location/กลุ่ม item, full หรือ cycle count) → freeze snapshot ยอดระบบต่อ lot ณ เวลาเริ่ม → นับผ่าน mobile สแกน barcode (blind count เป็น option) → variance = นับ − ระบบ (ปรับด้วย movement ที่เกิดระหว่างนับ) → recount ถ้าเกิน tolerance (config ต่อ tenant) → อนุมัติ → สร้าง Adjustment อัตโนมัติ reason = count variance; ล็อก location ระหว่างนับเป็น option

## 4. ปิดสต็อกรายเดือน (`T/CloseStockMonthly.vb`; `T/wAccCloseStock.vb` เป็นสำเนาเดียวกัน)

- ทำ**ต่อคลัง**: list คลังของบริษัทที่ login ตามสิทธิ์คลัง (`PermissionWareHouse`) พร้อมเดือนที่ปิดล่าสุด และเดือนถัดไปที่จะปิด (271-311)
- เดือนที่ปิดล่าสุด = `MAX(FTYear)`, `MAX(FTMonth)` ต่อคลังจาก `TINVENStockLastMonthly` (300-302); เดือนที่จะปิด = เดือนถัดจากนั้น (298)
- คลังที่ไม่เคยปิด: เดือนแรกที่จะปิด = เดือนของเอกสารแรกสุดระหว่างใบปรับสต็อกใบแรกกับใบรับใบแรกของคลังนั้น (280-294)
- **ปิด:** ติ๊กเลือกคลัง → `SP_CloseStock_Monthly(user, whId, 'YYYY/MM', 'MM', 'YYYY')` ทีละคลัง (372-380) → ปิดได้ทีละ 1 เดือนต่อครั้ง เรียงตามลำดับ
- **ยกเลิกปิด:** `SP_ReOpenStock_Monthly` ด้วยเดือนที่ปิดล่าสุดของคลัง (415-423) → ถอยได้ทีละเดือนจากเดือนล่าสุด
- ไม่มีการตรวจก่อนปิดในโค้ด VB (เอกสารค้างอนุมัติ, ใบโอนค้างรับ, ยอดติดลบ) — `VerifyData` คืน true เสมอ (325-329)
- สิ่งที่ SP ทำ (snapshot ยอด? ยกยอด? คำนวณต้นทุน?) **อ่านจากโค้ดไม่ได้** — ดู Gap 1-3

**Period lock — `Barcode.CheckCloseStock(whId, docDate)` (`DK.INVEN/Barcode.vb:360-380`)**
- บล็อกเมื่อมีแถว `TINVENStockLastMonthly` ของคลังนั้นที่ `FTYear/FTMonth/31 ≥ วันที่เอกสาร` (เทียบเป็น string `yyyy/MM/dd`) → **post ย้อนเข้าเดือนที่ปิดแล้วไม่ได้** จนกว่าจะ reopen; คลัง id ≤ 0 ถือว่าบล็อก
- เป็นการตรวจฝั่ง client ต่อฟอร์ม เรียกจาก: รับ (`Receive`, `ReceiveFG`), เบิก (`Issue*`, `IssueBySaleOrder`), โอน, คืน, ปรับ, `ProductSetCreate`, และ `DK.SO/Packing/ProdOrderFillToTWH*` — ฟอร์มที่ไม่เรียก เช่น `T/wTransferLocation.vb` post เข้าเดือนปิดได้
- วันที่ที่ใช้ = วันที่เอกสารที่ผู้ใช้กรอกใน header ไม่ใช่วันที่บันทึกจริง

**สำหรับระบบใหม่:** period ต่อ tenant (+ ต่อคลังเป็น option) สถานะ Open → Closing → Closed (+ Reopened ต้องมีสิทธิ์และเหตุผล บันทึก audit); close = (1) pre-check: เอกสาร Draft/Submitted ที่ posting date อยู่ในงวด, transfer ค้าง in-transit, lot ยอดติดลบ, count session ค้าง (2) เขียน snapshot `inventory.period_balances(tenant, period, warehouse, location, item, lot, qty, value)` จาก ledger (3) lock; การตรวจ lock อยู่ใน domain service + DB constraint/trigger บน `inventory.movements` ไม่พึ่ง UI; งวดถัดไปเปิดอัตโนมัติ ยอดยกมา = snapshot; reopen ต้อง invalidate snapshot งวดนั้นและงวดหลัง; ปิดงวดรันเป็น Hangfire job

## 5. Stock card

- ฟอร์ม: `R/StockCard.vb` (RM/PK → `SP_Stock_Onhand`), `R/StockCardFG.vb` + `R/StockCardReturnFG.vb` (→ `SP_Stock_Onhand_FG`), `R/StockCardFGAsOf.vb` (เหมือน FG แต่บังคับวันเริ่ม `1990/01/01`; 316), `R/StockCardFGjmt.vb` (→ `SP_Stock_Onhand_batch_jmt`, ตาราง Batch)
- ขั้นตอน (`R/StockCard.vb:312-385`): ลบแถวพักของ user → เรียก SP ด้วย (user, คลังจาก–ถึง, item จาก–ถึง, วันเริ่ม, วันสิ้นสุด; ไม่ระบุวันสิ้นสุด = `9999/99/99`) → SP เติมแถว transaction ลงตารางพัก → query แสดง → ลบแถวพัก
- ต้องเลือกเงื่อนไขคลังหรือ item อย่างน้อย 1 อย่าง (400-420)
- **running balance** คำนวณใน query ของฟอร์ม: `FNBalQty(แถว) = Σ FNQuantity ของแถวพักที่คลัง + location + item เดียวกัน และ FNSeq ≤ FNSeq ของแถวนั้น` (347-355) → ลำดับและยอดยกมาขึ้นกับ `FNSeq` ที่ SP กำหนด
- เครื่องหมาย: `FNQuantity > 0` = รับเข้า (แสดงเลขเอกสาร/qty ฝั่ง IN), `< 0` = จ่ายออก (แสดงค่าบวกฝั่ง OUT); แถว reserve (`FNReserveQuantity > 0`) แสดงเลขเอกสารฝั่ง OUT แต่ไม่เข้ายอด (328-331)
- ระดับของยอด: คลัง + location + item (ไม่แยก lot ยกเว้นรุ่น batch); เรียงแสดงตามคลัง, location, item, `FTDocumentDate`, `FTDocumentTime` (379)
- ข้อมูลเสริม: เลข job ของใบเบิก (`TINVENIssue.FTJobOrderNo`; 366), SO ของใบเบิก FG (`TINVENIssue_Detail.FTOrderNo`; `R/StockCardFG.vb:332`), ชนิดเอกสาร `FTDocType` (เช่น `ISS`)

## 6. On-hand

- ฟอร์ม: `R/StockOnhand.vb` (`SP_Stock_Onhand`), `R/StockOnhandBatch.vb` (`SP_Stock_Onhand_batch`), `R/StockOnhandBatch_rm.vb` (`SP_Stock_Onhand_batch_RM`), `R/StockOnhandFG.vb` + `R/StockOnhandFGAsOf.vb` (`SP_Stock_Onhand_FG`), `R/ReserveTracking.vb` (`SP_Stock_Onhand_Reserve`)
- ใช้ SP ตัวเดียวกับ stock card เรียกด้วยช่วงวัน `''` ถึง `9999/99/99` = ทุก transaction (`R/StockOnhand.vb:273`); รุ่น AsOf ส่งวันสิ้นสุดที่ผู้ใช้เลือก (`R/StockOnhandFGAsOf.vb:273`) → **on-hand ณ วันที่ = รวม transaction ตั้งแต่ต้นถึงวันนั้น**
- ยอด = `Σ FNQuantity`, มูลค่า = `Σ FNAmt` ของแถวพัก group ตาม คลัง + location + item (+ batch ในรุ่น batch) (`R/StockOnhand.vb:386-392`, `R/StockOnhandBatch.vb:320-326`); option รวมทุกคลัง (`FTMergeWH`) group ตาม item อย่างเดียว (276-325)
- ราคาต่อหน่วยที่แสดง = `FNAmt / FNQuantity` เมื่อ qty > 0 (359) — วิธีคิด `FNAmt` อยู่ใน SP
- คอลัมน์อื่นจาก SP: reserve, PO ค้างรับ (`FNPOQuantity`), in-transit; คอลัมน์ชื่อ `FNIntransitAmt` ใน RM ถูกใช้แสดง **ยอดพร้อมใช้ = on-hand + PO ค้างรับ − reserve** (293, 371)
- วันที่เบิกล่าสุด: RM = `MAX(FDIssueDate)` ของใบเบิกที่ตัด barcode ของ item นั้นในคลังนั้น (400-408); FG = วันที่ล่าสุดของแถวพักที่ `FTDocType='ISS'` (`R/StockOnhandFG.vb:349-352`)
- ยอดระดับ barcode สำหรับการทำรายการใช้ `SP_GET_BARCODE_BALANCE*` และ view `V_Material_*Balance*` แยกจาก SP รายงาน (ดู inventory-transfer.md §7) → **มีสูตรยอดคงเหลืออย่างน้อย 3 ชุด**ที่ต้องตรงกันเอง

**สำหรับระบบใหม่ (§5-6):** แหล่งความจริงเดียวคือ `inventory.movements` (qty มีเครื่องหมาย, posting date, lot, คลัง, location, ชนิด, เอกสารอ้างอิง, ต้นทุน); on-hand ปัจจุบัน = cache `inventory.balances` อัปเดตใน transaction เดียวกับ movement; on-hand ณ วันที่ = snapshot งวดล่าสุดก่อนวันนั้น + Σ movement หลัง snapshot; stock card = ยอดยกมาจาก snapshot + running sum ด้วย window function เรียงตาม posting date แล้ว sequence ของ ledger; reserve / on-order / in-transit / QC hold เป็นคอลัมน์แยก และ available = on-hand − reserve − QC hold; รายงานใช้ Dapper อ่านตรง ไม่ใช้ตารางพักต่อ user; วิธีคิดต้นทุน (FIFO ต่อ lot / เฉลี่ย) เป็น tenant setting

## 7. SP / function / view ที่เกี่ยว (ไม่มี definition ใน repo — ต้องขอ)

- ปิดงวด: `SP_CloseStock_Monthly`, `SP_ReOpenStock_Monthly` (`T/CloseStockMonthly.vb:378, 421`; `T/wAccCloseStock.vb:378, 421`)
- รายงาน: `SP_Stock_Onhand` (`R/StockCard.vb:316`, `R/StockOnhand.vb:273`), `SP_Stock_Onhand_FG` (`R/StockCardFG.vb:316`, `R/StockOnhandFG.vb:277`), `SP_Stock_Onhand_batch` (`R/StockOnhandBatch.vb:273`), `SP_Stock_Onhand_batch_RM` (`R/StockOnhandBatch_rm.vb:273`), `SP_Stock_Onhand_batch_jmt` (`R/StockCardFGjmt.vb:316`), `SP_Stock_Onhand_Reserve` (`R/ReserveTracking.vb:274`)
- ปรับสต็อก: `SP_GET_BARCODE_BALANCE` (`DK.INVEN/Barcode.vb:95-108`), `SP_GEN_BARCODE_NO` (`T/AdjustStock.vb:1127`), `Gen_BatchNo` (1011)
- view: `V_Material_Barcode_Balance`, `V_Material` (`T/AdjustStock.vb:1059-1060`)
- สิทธิ์: `DK.ST.Security.PermissionWareHouse` (`T/CloseStockMonthly.vb:309`, `R/StockCard.vb:377`)

## 8. ที่เกี่ยวข้องในโมดูลอื่น

- รับของ / PO: ปรับเพิ่มอ้าง `FTPurchaseNo`; เดือนแรกที่ปิดได้ดูจาก `TINVENReceive.FDReceiveDate` (`T/CloseStockMonthly.vb:288-291`); on-hand แสดง PO ค้างรับ
- เบิก / ผลิต: stock card ผูกใบเบิกกับ job order และ SO; reserve มาจาก `TINVENIssue_Detail.FTStateReserve` (ดู mrp-planning.md §8)
- MRP: netting ใช้ view/function อีกชุด (`V_Material_Balance_ForMRP`, `FN_Get_Onhand_RMPK`) ไม่ได้ใช้ SP on-hand ของรายงาน
- โอน/คืน: ดู inventory-transfer.md

## Defects ของระบบเดิม (ห้ามยกมา)

- หาเดือนที่ปิดล่าสุดด้วย `MAX(FTYear)` และ `MAX(FTMonth)` แยกกัน → ข้ามปีแล้วผิด (เช่น ปิด 2023/12 และ 2024/01 ได้ 2024/12) (`T/CloseStockMonthly.vb:300`)
- ปิด/ยกเลิกปิดกลืน exception แล้วแจ้ง "เรียบร้อย" เสมอ (`T/CloseStockMonthly.vb:385-391, 428-434`)
- period lock ตรวจฝั่ง client ต่อฟอร์ม ข้ามได้ถ้าฟอร์มไม่เรียก; เทียบวันที่เป็น string กับ `/31` ทุกเดือน (`DK.INVEN/Barcode.vb:365-368`)
- reopen ได้โดยไม่มีเหตุผล/อนุมัติ/ประวัติในโค้ด (`T/CloseStockMonthly.vb:403-443`)
- พิมพ์ `DELEET` ทำให้คำสั่งลบ detail ปรับเพิ่มไม่เคยทำงาน และ `ConnTrans.Excute` กลืน SQL error คืน 0 เงียบๆ (`T/AdjustStock.vb:977`, `DK.Data/ConnTrans.cs:195-211`) → แถวที่ผู้ใช้ลบออกจาก grid ยังค้างใน detail
- ปรับเพิ่มมีเส้นทางสร้าง barcode ซ้อน 2 ทาง: ใน transaction ตอน approve (`T/AdjustStock.vb:1126-1155`) และ `SaveDetail` → `GenBarcode` ที่ถูกเรียกท้าย save ทุกครั้ง ซึ่งลบ barcode + IN ของ item แล้วสร้างใหม่โดย batch/วันหมดอายุว่าง (1194, 2399-2461, 2147-2267)
- ปรับลดขยับสต็อกก่อน approve และ revoke ไม่คืนยอด (RM); การเขียน OUT ตอนสแกนใช้ transaction แยกจาก save (`T/AdjustStock.vb:1408-1427, 2660-2688`)
- ปรับลด FG เลือก barcode ด้วย `MAX(FTBarcodeNo)` ตาม item ไม่ดู batch/คลัง และเขียนนอก transaction (`T/AdjustStockFG.vb:1120, 1131, 1140-1157`)
- เลข barcode ปรับเพิ่มคำนวณจากเลขตั้งต้น + ลำดับแถว โดย sequence ขยับครั้งเดียว → เสี่ยงเลขชนเมื่อทำพร้อมกัน (`T/AdjustStock.vb:1127-1136`)
- ตารางพักรายงาน key ด้วย username → user เดียวเปิด 2 รายงานพร้อมกันข้อมูลปนกัน (`R/StockCard.vb:312, 384`)
- running balance ใช้ correlated subquery ต่อแถว (O(n²)) (`R/StockCard.vb:347-355`)
- ชื่อคอลัมน์ไม่ตรงความหมาย (`FNIntransitAmt` = ยอดพร้อมใช้; `R/StockOnhand.vb:293`); ไม่มีรหัสเหตุผลการปรับ; ไม่มีเอกสารนับสต็อก
- empty catch, SQL concat, `NOLOCK` ทุก query

## Gap ที่ต้องดู SP definition / data

1. `SP_CloseStock_Monthly`: เขียนอะไรลง `TINVENStockLastMonthly` (ยอดต่อ barcode / lot / item?, มีมูลค่าไหม), มี pre-check อะไร, คำนวณต้นทุนหรือไม่
2. `SP_ReOpenStock_Monthly`: ลบ snapshot หรือแค่ปลด flag, ตรวจเดือนถัดไป/งวดบัญชีก่อนไหม
3. โครงและ key เต็มของ `TINVENStockLastMonthly`; มี `TINVENStockMonthly` หรือตารางยกยอดอื่นหรือไม่
4. `SP_Stock_Onhand*`: เริ่มจาก snapshot เดือนปิดล่าสุดหรือรวมจาก transaction แรก, ยอดยกมาก่อนวันเริ่มของ stock card เป็นแถวแบบไหน, `FNSeq` เรียงตามอะไร
5. วันที่ที่ SP ใช้ต่อชนิดเอกสาร: วันที่เอกสาร header, วันที่อนุมัติ หรือ `FDInsDate` ของแถว IN/OUT
6. SP นับเอกสารที่ยังไม่อนุมัติหรือไม่ (ใบโอนที่ยังไม่รับ, ใบปรับลดที่ยังไม่ approve, ใบคืนคลังที่ยังไม่อนุมัติ)
7. วิธีคิด `FNAmt` (ราคา barcode ตอนรับ = FIFO ต่อ lot หรือเฉลี่ย), หน่วยที่ใช้รวมยอดเมื่อ barcode ต่างหน่วยกัน
8. ความต่างของ SP ทั้ง 6 ตัว (RM / FG / batch / batch_RM / batch_jmt / Reserve) และค่า `FTDocType` ทั้งหมด
9. นิยาม reserve, PO ค้างรับ, in-transit ใน SP และตรงกับ `FN_Get_Reserve` / `FN_Get_PO` ที่ MRP ใช้หรือไม่
10. กระบวนการนับสต็อกจริงของผู้ใช้: ใช้ Excel นอกระบบหรือไม่, ความถี่, ใครอนุมัติส่วนต่าง
11. การปิดสต็อกผูกกับการปิดงวดบัญชี (DK.ACC) หรือไม่ — `wAccCloseStock` ถูกเรียกจากเมนูบัญชีหรือเมนูคลัง (`MSysMenu`)
12. ค่า `FNAdjustType` อื่นนอกจาก 0/1 ใน data จริง และรูปแบบข้อความใน `FTRemark` ที่ใช้แทนเหตุผล (ใช้ตั้ง reason master)
13. trigger บน `TINVENAdjustStock`, `TINVENBarcode_IN/OUT`, `TINVENStockLastMonthly` มีไหม
14. ใบปรับเพิ่มใน data จริง: มีใบที่ `FTStateApprove` ≠ 1 แต่มีแถว `TINVENBarcode_IN` หรือไม่ และ barcode ของใบปรับมี batch/วันหมดอายุหรือว่าง (ยืนยันว่าเส้นทาง `SaveDetail` → `GenBarcode` ทำงานจริงไหม)
