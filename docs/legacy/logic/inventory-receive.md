# Logic เดิม: รับของ / Barcode IN / รอ QC / QC (DK.INVEN)

ถอดจาก repo DK-ERP-SYSTEM 2026-09-29 · อ้างอิง path ในระบบเดิม · DB: DK_INVENTORY, DK_MASTER, PUR, DK_PROD, SECURITY

**สรุปสำคัญ:** barcode 1 ใบ = 1 บรรทัดรับ (item + batch) ทั้งจำนวน ไม่ใช่พาเลท/หน่วยบรรจุ เว้นแต่ผู้ใช้สั่งแบ่งเอง
สต็อกเข้าทันทีที่สร้าง barcode (`TINVENBarcode_IN`) **ไม่รออนุมัติและไม่รอ QC** — "QC hold" ทำโดยแยกคลัง: QC ผ่านแล้วระบบโอนของจากคลังรับไปคลังผลิตอัตโนมัติ
รับเกิน PO คำนวณได้แต่**ไม่บล็อก** · ไม่มีสถานะยกเลิก มีแต่ลบเอกสารจริง
→ ระบบใหม่ต้องมี lot status (Quarantine/Released/Rejected) ใน ledger, tolerance รับเกินเป็น tenant setting, และ void แบบ reversal

## 1. ตาราง

- `TINVENReceive` header: `FTReceiveNo, FDReceiveDate, FTPurchaseNo, FNMSysSuplId, FNMSysCurId, FNExchangeRate, FNMSysWHId, FNMSysWHLocId, FNRceceiveType, FNMSysCmpId` + flag `FTStateSendApp/By, FTStateApp/By, FTStateReject/By, FTStateAppDate/Time` — เขียนแบบ generic จากรายการ field ของ dynamic form (`DK.INVEN/Transaction/Receive.vb:772-1036`) จึงไม่เห็นรายชื่อคอลัมน์ครบในโค้ด
- `TINVENReceive_Detail` ต่อ item (หน่วยซื้อ): `FNMSysRawMatId, FNMSysUnitId, FNPrice, FNDisPer, FNDisAmt, FNNetPrice, FNNetAmt, FNQuantity, FNSeq, FTDescriptions, FTLotId` (`Receive.vb:1887-1903`) + `FTStateQC, FTStateQCPass, FTStateQCNotPass, FTDateExpire` (`StockQC.vb:1494`)
- `TINVENReceive_Detail_Order` ต่อ item (หน่วยสต็อก): `FTOrderNo` (เขียน '' เสมอ), `FNQuantity, FNQuantityStock, FNMSysUnitIdStock, FNPricePerStock, FNConvRatio, FNNetStockAmt` (`Receive.vb:2145-2165`) + flag QC/expire ชุดเดียวกัน (`wGenerateBarcode.vb:123`)
- `TINVENBarcode` master ของ barcode: `FTBarcodeNo, FNMSysRawMatId, FNMSysUnitId (หน่วยสต็อก), FNQuantity (จำนวนตั้งต้น), FNPrice (= FNPricePerStock), FTDocumentNo (เอกสารต้นทาง), FTPurchaseNo, FTBatchNo, FTGrade, FTDateExpire, FTStateWase, FNMSysWHId, FNMSysWHLocId, FNMSysCmpId, FTOrderNo, FTStateQC, FTStateQCPass, FTStateQCNotPass` (`Receive.vb:2508-2525`, `wGenerateBarcode.vb:251-271`)
- `TINVENBarcode_IN` movement เข้า: `FTBarcodeNo, FTDocumentNo, FTDocumentRefNo, FNMSysWHId, FNMSysWHLocId, FTOrderNo, FNQuantity, FNMSysCmpId` (`Receive.vb:2535-2543`); คู่กับ `TINVENBarcode_OUT` (ดู inventory-issue.md)
- QC: `TINVENQC` (`FTQCNo, FTReceiveNo, FNMSysRawMatId, FTBatchNo, FDQCDate, FTQCBy, FNQuantityRcv, FNQuantityQC, FTDateExpire, FNQCType, FTRemark` — ชื่อ control ใน `StockQC.designer.vb`), `TINVENQC_Detail(FNSeq, FNMSysQCDetailId, FTStatePass, FTStateNotPass, FNQCVale, FTRemark, FTRandomType, FTQCType, FNQuantityQC, FNQuantityRejected)` (`StockQC.vb:861-889`), `TINVENQC_Result` (`StockQCBrowse.vb:915-932`), master หัวข้อตรวจ `DK_MASTER.TQCMQCDetail` ต่อ item (`StockQC.vb:1697-1709`)
- อื่น: `TINVENMConfigReceiveOver` (% รับเกิน), `TCNMUnitConvert`, `TINVENStockLastMonthly` (ปิดเดือน), `TINVENMailSendAppRcvOver`, `SECURITY.TSESystemConfig`

**สำหรับระบบใหม่:** `receipts` + `receipt_lines` (qty หน่วยซื้อ + qty หน่วยสต็อก + conversion ที่ใช้ ณ วันรับ) · `lots` (item, lot no, mfg/expiry, supplier lot, QC status) · `handling_units` (barcode/พาเลท ผูก lot) เป็น option · movement ลง `inventory.movements` แบบ append-only แทนคู่ตาราง IN/OUT

## 2. รับของ (`Receive.vb` = RM/PK, `ReceiveFG.vb` = FG)

**ประเภทรับ** `FNRceceiveType`: 0 ปกติ, 1 ซ่อม, 2 ของแถม (`wReceiveItem.vb:3-7`); ของแถมดึงราคา/ส่วนลดเป็น 0 (`Receive.vb:1816-1817`)
`Receive.vb` ตั้งกลุ่มเป็น "Raw" (`Receive.vb:1304`), `ReceiveFG.vb` เป็น "FG" และใช้ dynamic form ตัวเดียวกับ Receive (`ReceiveFG.vb:240,1282`)

**A. รับอ้าง PO** (`Receive.vb:1783-1984`)
1. popup แสดงบรรทัด PO จาก `PUR.TPURTPurchase_OrderNo` sum ตาม item + หน่วย + ราคา + `FNSeq` (1814-1827)
2. `FNRcvHisQty` = ยอดรับของใบรับอื่นใน PO เดียวกัน, `FNPOBalQty = PO qty − FNRcvHisQty` (1807-1808, 1833); แสดงเฉพาะ `FNPOBalQty > 0` (1840); จับคู่ยอดรับกับบรรทัด PO ด้วย item + ราคาสุทธิ `(FNPrice − FNDisAmt)` (1839)
3. ติ๊กเลือก → qty รับ default = ยอดคงเหลือ PO (`wReceiveItem.vb:73-84`)
4. บันทึก `TINVENReceive_Detail`: `FNNetAmt = qty × FNNetPrice` ปัดตาม `AmtFormat` (`Receive.vb:1899,1916`) → `EqualizeJob` → สร้าง barcode อัตโนมัติ (1985)
- เมื่อมีบรรทัดแล้ว ล็อก header: คลัง, location, PO, ประเภทรับ, สกุลเงิน, supplier (`Receive.vb:1439-1476`); grid แก้ไม่ได้ถ้ามี PO (2605-2635)
- `ReceiveFG.vb` กรอง PO line ที่ `FTStateClose <> '1'` และรับอ้างใบคืน supplier (`TINVENReturnToSupplier_Detail`) ได้ด้วย = รับของเปลี่ยน (`ReceiveFG.vb:1809-1830`)

**B. รับไม่อ้าง PO:** เพิ่มแถวเปล่าใน grid (`Receive.vb:1674-1688`) แล้ว double-click เปิด popup กรอก item/หน่วย/ราคา/ส่วนลด/จำนวน (1331-1437); `FNNetAmt = qty × (price − disAmt)` (`wAddItemReceive.vb:54-83`); 1 item ต่อ 1 บรรทัดต่อใบรับ (1367-1371)

**รับเกิน PO** (`wReceiveItem.CheckReceiveOver`, `wReceiveItem.vb:112-196`) เฉพาะประเภทปกติ
`รับสะสม = qty ครั้งนี้ + รับของใบอื่น − คืน supplier (หารด้วย FNConvRatio)`
ถ้า `รับสะสม > PO qty`: หา `FNRcvOverPercent` จาก `TINVENMConfigReceiveOver` ตามกลุ่มวัตถุดิบ + ช่วง `FNStartRcvQty..FNEndRcvQty` ของ PO qty
`over = รับสะสม − (PO qty + PO qty × % / 100)` ถ้าไม่มี config `over = รับสะสม − PO qty`
ผลลัพธ์เขียนลงคอลัมน์ `FNRcvQtyOver` เท่านั้น ส่วนที่ตัด qty ลงถูก comment (99-105) → **ไม่บล็อก**; `ReceiveFG.vb` เพิ่มแถวลง `TINVENMailSendAppRcvOver` เมื่อ over > 0 (`ReceiveFG.vb:1968,2026-2036`)

**Unit conversion** (`EqualizeJob`, `Receive.vb:1992-2194`)
- หน่วยสต็อก = `TPORawmaterial.FNMSysUnitId` (2014); ถ้าต่างจากหน่วยรับ: `ratio = FNRateFrom × FNRateTo` จาก `TCNMUnitConvert` ต่อ item (2026-2031); ไม่พบ → hard-code 12 (unit 1706150002) หรือ 1000 (unit 1707180007) (2034-2047); `ReceiveFG.vb` ไม่มี hard-code
- `FNQuantityStock = qty × ratio` (2060)
- `FNPricePerStock = FNNetPrice × FNExchangeRate / ratio` (2162), `FNNetStockAmt = FNPricePerStock × FNQuantityStock` (2164)
- ติ๊กรับไม่ได้ถ้าไม่มี conversion ยกเว้น unit 1706150001 / 1707180006 และกลุ่ม `SH`, `SP` (`wReceiveItem.vb:221-305`)
- `FNExchangeRate`: สกุลเงิน local (`TFINMCurrency.FTStateLocal='1'`) ล็อกเป็น 1 (`Receive.vb:1311-1325`); ค่า ≤ 0 ถูกบังคับเป็น 1 (1503-1505)

**สำหรับระบบใหม่:** receipt line อ้าง PO line ตรงๆ (ไม่จับคู่ด้วยราคา) · over-receive tolerance % ต่อ tenant/กลุ่มสินค้า + เลือกได้ว่า block / warn / ต้องอนุมัติ · UOM conversion จากตารางเท่านั้น ไม่มี fallback · ต้นทุนต่อหน่วยสต็อกเก็บใน movement `numeric(18,4)`

## 3. Barcode

**ความหมาย:** barcode = ก้อนสต็อกของ item เดียว จากเอกสารรับใบเดียว มี batch, expiry, grade, ราคาต่อหน่วยสต็อก, คลัง/location ตั้งต้น (`Receive.vb:2508-2525`) ใกล้เคียง "lot ต่อใบรับ" มากกว่าพาเลท
**เลข barcode:** `EXEC SP_GEN_BARCODE_NO '<CmpRunID>'` (`Receive.vb:2499`); 6 หลักท้ายเป็น running number (โค้ดอื่นบวกเลขจาก 6 หลักท้าย: `StockQCBrowse.vb:1350`, `ReturnORRefund.vb:1148`); prefix อยู่ใน SP
**Batch no:** `EXEC Gen_BatchNo <item>, <unit>` (`Receive.vb:2500`); ผู้ใช้แก้ในตาราง barcode ได้ บันทึกตอนกด Save (1176-1188)

**สร้างอัตโนมัติ** (`Receive.GenBarcode`, `Receive.vb:2424-2573`) หลังเพิ่มบรรทัดรับ
- ลบ barcode เดิมของใบรับ แล้วสร้าง **1 barcode ต่อบรรทัด** `TINVENReceive_Detail_Order` ที่ `FNQuantityStock − Σ barcode qty > 0` (2455-2460, 2483) จำนวน = ยอดที่เหลือทั้งหมด (2501-2502)
- เขียน `TINVENBarcode` + `TINVENBarcode_IN` (`FTDocumentNo = FTDocumentRefNo = เลขใบรับ`) ใน transaction ต่อ barcode (2504-2550)
- `ReceiveFG.GenBarcode`: ถ้าใบรับมี barcode ของ item นั้นแล้ว ใช้ใบเดิมแล้วบวก qty ใน `TINVENBarcode_IN` (`ReceiveFG.vb:2661-2724`)

**แบ่งเอง** (`wGenerateBarcode.vb`, ใช้กับ Receive / Adjust / Scrap: 15-19, 115-151)
- ต่อบรรทัดกรอกจำนวนใบ N → `qty ต่อใบ = ยอดคงเหลือ / N` (194-226); ใบสุดท้ายรับเศษเมื่อ qty เกินยอดเหลือ หรือติ๊ก `FTStaLastAll` (244-246)
- กรอก batch, grade, expiry, `FTStateWase` ต่อบรรทัด และคัดลอก flag QC จากบรรทัดรับ (251-271)

**ลบ barcode** (`Receive.DeleteBarcodeGen`, `Receive.vb:2318-2370`): ลบ `TINVENBarcode` + `TINVENBarcode_IN` ได้ถ้ายังไม่มี `TINVENBarcode_OUT` (`Barcode.CheckTransactionOUT`, `Barcode.vb:34-52`)
**On-hand ต่อ barcode** = Σ `TINVENBarcode_IN.FNQuantity` − Σ `TINVENBarcode_OUT.FNQuantity` ต่อ barcode + คลัง + location; คำนวณใน SP/view (`SP_GET_BARCODE_BALANCE`, `V_Material_Barcode_Balance`) ไม่มี definition ใน repo
**พิมพ์ label:** `PrintCopy` เลือก barcode + จำนวนสำเนา (`Receive.vb:2383-2421`)

**สำหรับระบบใหม่:** แยก `lot` (batch/expiry/QC) ออกจาก `handling unit` (label ที่สแกน) · lot no ออกจาก sequence ต่อ tenant หรือรับ supplier lot · จำนวนต่อ label มาจาก pack size ของ item (tenant setting) · on-hand = sum ของ `inventory.movements` ต่อ item + lot + location

## 4. รอ QC และ QC

**รายการรอ QC** (`ReceiveWaitQC.vb`): `EXEC USP_GET_DATA_RAWMAT_QC` refresh ด้วย timer (96, 131-137); double-click เปิดเมนู `mnuinvenqc` ส่ง ใบรับ + item + qty (139-166)

**ใบ QC วัตถุดิบ** (`StockQC.vb`): 1 ใบ ต่อ ใบรับ + item + batch
- `FNQuantityRcv` = Σ `TINVENBarcode_IN` ของใบรับ + item + batch (1838-1845); `FNQuantityQC` = จำนวนสุ่มตรวจ
- หัวข้อตรวจเลือกจาก `TQCMQCDetail` ของ item; ผ่าน/ไม่ผ่านเป็นคู่ตรงข้ามต่อหัวข้อ (1585-1612)

**`FNQCType`:** 0 รอ, 1 อนุมัติ (ผ่าน), 2 reject (`StockQC.vb:1198,1429,1486`); > 0 แล้วแก้/ลบไม่ได้ (988-991, 1005-1008)

| ปุ่ม | ผล |
|---|---|
| Approve (`StockQC.vb:1187-1275`) | `FNQCType=1`; barcode ของ ใบรับ + item + batch → `FTStateQC='1', FTStateQCPass='1', FTStateQCNotPass='0', FTDateExpire` = วันหมดอายุที่กรอกในใบ QC (1225-1228); ถ้าไม่มีแถวตรง → update ทุก barcode ของ item + batch ที่ expiry ว่าง (1232-1235) แล้วเรียก `toWHMix` |
| Revoke (`1419-1473`) | `FNQCType=0`; ล้าง flag + expiry ของทุก barcode ของ ใบรับ + item; ทำไม่ได้ถ้าใบรับถูกเบิกแล้ว (1627-1630) |
| Reject (`1475-1530`) | `FNQCType=2`; `TINVENReceive_Detail`, `TINVENReceive_Detail_Order`, `TINVENBarcode` → `FTStateQC='1', FTStateQCNotPass='1'`; ไม่มี movement |

**โอนเข้าคลังผลิตหลัง QC ผ่าน** (`toWHMix`, `StockQC.vb:1279-1412`)
- คลัง/location ปลายทางจาก `TSESystemConfig`: กลุ่มวัตถุดิบขึ้นต้น `RM` → `CfgWHMixAfterQCRM` / `CfgLogMixAfterQCRM`, อื่นๆ → `CfgWHMixAfterQCPK` / `CfgLogMixAfterQCPK` (1243-1259)
- ต่อ barcode ของใบรับ: เขียน `TINVENBarcode_OUT` ที่คลังรับ (`FTDocumentNo` = เลข QC) (1295-1317) และ `TINVENBarcode_IN` ที่คลังปลายทาง (1371-1391)
- จำนวนเข้า: RM = เท่าที่รับ; กลุ่มอื่น = `FNQuantityRcv − FNQuantityQC` (หักตัวอย่าง) (1378, 1387)

**QC hold vs available:** ฟอร์มเบิกไม่ตรวจ `FTStateQC` เลย (ไม่มีการอ้าง flag นี้ใน `Issue*.vb`) การกันใช้ของก่อน QC จึงขึ้นกับการแยกคลังเท่านั้น
**Expiry / shelf life:** วันหมดอายุกรอกมือที่ใบ QC หรือ popup สร้าง barcode ไม่มีการคำนวณจาก shelf life ของ item
**QC ผลผลิต** (`StockQCBrowse.vb`): โครงเดียวกันแต่อ้าง `DK_PROD.TPDTProdActual` และสร้าง barcode ใหม่เข้าคลังจาก `CfgWHAfterQCToFG` / `CfgWHMixAfterQC` (1242-1386) → อยู่ใน flow production-to-wh

**สำหรับระบบใหม่:** lot status `Quarantine → Released | Rejected` (+ `OnHold`); การเบิก/จองเห็นเฉพาะ Released · QC plan ต่อ item (หัวข้อ, เกณฑ์, sampling) · ตัวอย่างที่ใช้ตรวจเป็น movement ประเภท `QC_SAMPLE` · ย้ายคลังหลัง QC เป็น tenant setting (เปิด/ปิด + location ปลายทางต่อ item type) · expiry = mfg date + shelf life ของ item แก้ได้ตอน QC · revoke ต้อง reverse movement

## 5. สถานะ, ล็อก, ลบ

- `FTStateSendApp`: Save / เพิ่ม / ลบบรรทัด reset flag ทั้งสามเป็น '0' (`Receive.vb:1164-1171, 1948-1955, 2247-2254`); ปุ่มส่งอนุมัติ set `'1'` (2575-2599)
- `FTStateApp`, `FTStateReject`: ไม่พบโค้ดใน repo ที่ set เป็น '1' สำหรับใบรับ (ดู Gap 3)
- ล็อกแก้ไข:
  1. ปิดสต็อกแล้ว: มี `TINVENStockLastMonthly` ของคลังที่ `ปี/เดือน/31 ≥ วันที่เอกสาร` (`Barcode.CheckCloseStock`, `Barcode.vb:360-380`) — ตรวจก่อน save, ลบ, เพิ่ม/ลบบรรทัด, สร้าง/ลบ barcode
  2. ถูกเบิกแล้ว: มี `TINVENBarcode_OUT.FTDocumentRefNo = ใบรับ` (`Barcode.vb:24-28`; `Receive.vb:1209,1653,2199`)
  3. ถูกอ้างจ่ายเงิน: มี `ACC.TAPPayables_D.FTDocrefNo = ใบรับ` (`Barcode.vb:30-33`; `Receive.vb:764-767`) — `ReceiveFG.vb` ไม่ตรวจข้อนี้
- ลบเอกสาร = ลบจริงทั้ง header, detail, detail_order, barcode, barcode_in (`Receive.vb:1038-1076`) ไม่มี void/reversal
- เลขเอกสาร: `DK.TL.Document.GetDocumentNo` ตาม `CmpRunID` (`Receive.vb:836`); `ReceiveFG.vb` ใช้ prefix บริษัทเฉพาะ `CmpCode = "BRPC"` (`ReceiveFG.vb:720,796,825`)

**สำหรับระบบใหม่ (state machine):** Receipt: `Draft → Posted → (QC) → Closed` + `Voided`; Post = เขียน movement `RECEIPT` เข้า lot สถานะ Quarantine (หรือ Released ถ้า item ไม่ต้อง QC); void = movement กลับรายการ ทำไม่ได้ถ้า lot ถูกใช้แล้ว; ขั้นอนุมัติเป็น config ต่อ tenant

## 6. Links ไป flow อื่น

- PO → รับ: `TINVENReceive.FTPurchaseNo`; ยอดค้างรับคิดจากใบรับทั้งหมดของ PO (`Receive.vb:1833-1839`); `DK.PO/Tracking/*` อ่าน `TINVENReceive` (ดู purchasing)
- รับ → AP: `ACC.TAPPayables_D.FTDocrefNo` (`Barcode.vb:30-33`)
- รับ → คืน supplier: `TINVENReturnToSupplier` + `TINVENBarcode_OUT` หักยอดรับสะสม (`wReceiveItem.vb:139-150`)
- รับ → โอนคลังอัตโนมัติ: ปุ่ม auto transfer เรียก `SP_AutoTransfer` สร้าง `TINVENTransferWH` ที่ `FTReferNo = ใบรับ` (`Receive.vb:401-410, 2654-2690`)
- รับ → เบิก: `TINVENBarcode_OUT.FTDocumentRefNo` = เลขใบรับ (ดู inventory-issue.md)

## 7. SP / function / view ที่เกี่ยว (ไม่มี definition ใน repo — ต้องขอ)

- เลข/batch: `SP_GEN_BARCODE_NO` (`Receive.vb:2499`, `ReceiveFG.vb:2669`, `wGenerateBarcode.vb:241`, `StockQCBrowse.vb:1339`, `AdjustStock.vb:1127`, `DK.SO/Actual/RouteActual.vb:1410`, `DK.SO/Packing/ProdOrderFillToTWH.vb:1376`), `Gen_BatchNo` (`Receive.vb:2500`, `ReceiveFG.vb:2677`)
- QC: `USP_GET_DATA_RAWMAT_QC` (`ReceiveWaitQC.vb:96`), `SP_GETTINVENQC_Result` (`StockQCBrowse.vb:523`)
- โอน: `SP_AutoTransfer` (`Receive.vb:2668`)
- balance: `SP_GET_BARCODE_BALANCE`, `SP_SEARCH_BARCODE` (`Barcode.vb:100,176`), view `V_Material_Balance` (`Receive.vb:1561`)
- form/รายงาน: `SP_GET_DYNAMIC_OBJECT_CONTROL` (`StockQC.vb:313`), view `V_Receive_rpt` (`Receive.vb:1244`)

## Defects ของระบบเดิม (ห้ามยกมา)

- สต็อกเข้าก่อนอนุมัติ/QC และฟอร์มเบิกไม่ตรวจ `FTStateQC`
- รับเกิน PO ไม่บล็อก (`wReceiveItem.vb:99-105`); query หา % รับเกินกรอง `FNMSysMatGrpId` ด้วยรหัส item (`wReceiveItem.vb:171`) → ไม่เจอ config
- `Receive.GenBarcode` ลบ barcode เดิมโดยระบุชื่อ DB เป็น PUR (`Receive.vb:2428,2431`) ขณะที่ตารางอยู่ INVEN; insert ล้มเหลวแล้วยัง return True (2527-2533, 2545-2548)
- `toWHMix` เขียน OUT ต่อ barcode ด้วยยอดรวม `FNQuantityRcv` ของทั้ง batch (`StockQC.vb:1301,1316`) → หลาย barcode ต่อ batch ยอดออกเกิน; รันคนละ transaction กับ `ApproveQC` และไม่ตรวจผล (1264, 1280)
- Revoke QC ไม่ย้อน OUT/IN ที่ `toWHMix` เขียน (`StockQC.vb:1419-1473`); revoke แล้ว approve ใหม่ → IN ที่คลังผลิตถูก insert ซ้ำ (1371-1398 ไม่มีการลบ/ตรวจของเดิม)
- `Barcode.CheckTransactionOUT` เทียบเลข barcode กับ `TINVENMMaterial.FTBarcodeNo` (รหัส barcode ของ item master) ไม่ใช่ `TINVENBarcode.FTBarcodeNo` (`Barcode.vb:39-41`) → การกันลบ barcode ที่ถูกใช้แล้วไม่น่าเชื่อถือ
- Approve QC fallback update barcode ข้ามเอกสาร (`StockQC.vb:1232-1235`); Revoke/Reject ไม่กรอง batch (1455-1457, 1513-1515)
- `StockQCBrowse.RejectQC` อ้าง `TPDTProdActual` ผิด DB (INVEN แทน PROD; `StockQCBrowse.vb:1459`)
- UOM hard-code 12 / 1000 และ unit id / กลุ่ม `SH`,`SP` ฝังในโค้ด (`Receive.vb:2036-2041`, `wReceiveItem.vb:237-255`)
- checkbox `FTAutoTransferState` ถูกใช้แทนสถานะส่งอนุมัติ (`Receive.vb:393, 2593`)
- ลบเอกสารจริง ไม่มี audit; `WITH (NOLOCK)` ทุก query; SQL concat; empty catch; จำนวน/เงินใช้ Double

## Gap ที่ต้องดู SP definition / data

1. `SP_GEN_BARCODE_NO`: รูปแบบเลข (prefix, ปี/เดือน, จำนวนหลัก), กัน running ซ้ำอย่างไร (ถูกเรียกนอก transaction)
2. `Gen_BatchNo`: รูปแบบ batch และเรียงตามเวลาได้จริงไหม (FIFO ฝั่งเบิกเรียงด้วย `FTBatchNo`)
3. ใบรับถูกอนุมัติที่ไหน: `FTStateApp` / `FTStateReject` set ใน SP, trigger หรือโปรแกรมอื่น และมีผลกับสต็อก/AP ไหม
4. `USP_GET_DATA_RAWMAT_QC`: เงื่อนไข "รอ QC" (ทุก item หรือเฉพาะ item ที่ตั้งให้ต้อง QC)
5. view balance (`V_Material_Balance`, `V_Material_Barcode_Balance`) กรอง `FTStateQC` / `FTStateWase` / expiry ไหม
6. `SP_AutoTransfer`: สร้างเอกสารโอนแบบไหน ต้องอนุมัติไหม
7. ค่า config `CfgWHMixAfterQCRM/PK`, `CfgLogMixAfterQCRM/PK`, `CfgWHAfterQCToFG`, `CfgWHMixAfterQC` และผังคลัง/location จริง
8. ข้อมูล `TINVENMConfigReceiveOver` และกฎรับเกินที่ใช้จริง; `TINVENMailSendAppRcvOver` ถูกประมวลผลต่อที่ไหน
9. `TCNMUnitConvert`: ความหมาย `FNRateFrom` / `FNRateTo`; unit id 1706150001/2, 1707180006/7 คือหน่วยอะไร
10. รายการคอลัมน์จริงของ `TINVENReceive` / `TINVENQC` (มาจาก metadata ของ dynamic form) และค่า CboList ของ `FNRceceiveType`, `FNQCType`, `FTRandomType`, `FTQCType`
11. `TINVENReceive_Detail_Order.FTOrderNo` และ `TINVENBarcode.FTOrderNo` เคยใช้ผูก order/job ไหม (ปัจจุบันเขียน '' เสมอ)
12. trigger บน `TINVENBarcode`, `TINVENBarcode_IN/OUT` มีไหม (เช่น update ยอด cache, stock card)
