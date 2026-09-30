# Logic เดิม: โอนคลัง/location + อนุมัติรับ, คืนคลัง, คืน supplier (DK.INVEN)

ถอดจาก repo DK-ERP-SYSTEM 2026-09-29 · อ้างอิง path ในระบบเดิม · DB: DK_INVENTORY (INVEN), DK_MASTER, PUR, ACC
path ย่อ: ไฟล์ที่ไม่ระบุโฟลเดอร์อยู่ใน `DK.INVEN/Transaction/` · ฟอร์ม w* (`wTransferWHToWH*`) เป็นชุด maintenance ไม่ถอด ยกเว้น `wTransferLocation.vb` ที่เป็นทางเดียวของการย้าย location

**สรุปสำคัญ:** สต็อกเดิมไม่มีตารางยอดคงเหลือ ทุกอย่างคือแถวใน `TINVENBarcode_IN` / `TINVENBarcode_OUT` ผูกเลขเอกสาร การโอนคลัง = **OUT ที่คลังต้นทางตอนบันทึก + IN ที่คลังปลายทางตอนผู้รับอนุมัติ** (ช่วงระหว่างนั้นคือ in-transit โดยปริยาย)
การแก้ไข/ยกเลิกทำด้วยการ **ลบแถว IN/OUT ทิ้ง** ไม่มี reversal ไม่มี audit trail และสต็อกขยับตั้งแต่ตอน save ก่อนอนุมัติในเกือบทุกเอกสาร
→ ระบบใหม่ต้องเป็น ledger append-only, ขยับสต็อกเมื่อ post เท่านั้น, ยกเลิก = movement กลับรายการ

## 1. ตารางและโครง barcode IN/OUT

- `TINVENBarcode(FTBarcodeNo, FNMSysRawMatId, FNMSysUnitId, FNQuantity, FNPrice, FTBatchNo, FTDateExpire, FTGrade, FTPurchaseNo, FTOrderNo, FTDocumentNo = เอกสารที่สร้าง barcode, FNMSysWHId, FNMSysWHLocId, FTStateWase)` — master ของ lot/barcode (`DK.INVEN/Barcode.vb:183-212`)
- `TINVENBarcode_IN` / `TINVENBarcode_OUT(FTBarcodeNo, FTDocumentNo, FTDocumentRefNo, FNMSysWHId, FNMSysWHLocId, FTOrderNo, FNQuantity, FTStateReserve, FNMSysCmpId, FTInsUser/FDInsDate/FTInsTime, FTUpd*)` — movement; ชนิดเอกสารรู้จากการ join เลขเอกสารกับ header แต่ละตาราง (`Barcode.vb:265-333`) ไม่มีคอลัมน์ type
- `FTDocumentRefNo` ของ OUT = เลขเอกสารฝั่ง IN ที่ถูกตัดยอด (มาจาก `FTDocumentNo` ของแถว balance; `TransferWHToWH.vb:1317`) · ของ IN ที่เกิดจากการโอน = เลขใบโอนเอง (`TransferWHToWHApprove.vb:1256`)
- enum ชนิดเอกสาร: Reserve 0, Issue 1, ReturnToStock 2, ReturnToSupplier 3, SaleAndTerminate 4, Scrap 5, Adjust 6, TransferOrder 7, TransferCenter 8, TransferWH 9, TransferLocation 10, ProductSet 12, TransferWHRM 13 (`Barcode.vb:3-17`)
- โอนคลัง: `TINVENTransferWH(FTTransferWHNo, FDTransferWHDate, FTTransferWHBy, FNMSysWHId, FNMSysWHLocIdSC = location ต้นทาง, FNMSysWHIdTo, FNMSysWHLocId = location ปลายทาง, FNTransferWHType, FTStateApprove, FTApproveBy/FDApproveDate/FTApproveTime, FTCancelBy/FDCancelDate/FTCancelTime, FTStateCustWH, FTRemark)` + `TINVENTransferWH_Detail(FTDocumentNo, FNSeq, FTBarcodeNo, FNQuantity, FNMSysUnitId, FNRawmatPrice, FNMSysWHId, FNMSysWHLocId)` — header เขียนผ่าน dynamic form (`TransferWHToWH.vb:792-794`), detail ที่ `TransferWHToWHRM.vb:1373-1386`
- ย้าย location: `TINVENTransferLocation(FTTransferLocNo, …)` (`wTransferLocation.vb:859`)
- คืนคลัง: `TINVENReturnToStock(FTReturnStockNo, FDReturnStockDate, FTJobOrderNo, FNMSysWHId, FNMSysWHLocId, FTStateSendApp, FTStateApp, FTStateReject + …By/Date/Time)` + `_Detail(FTBatchNo, FNMSysRawMatId, FNMSysUnitId, FNQuantity, FNPrice, FNSeq)` (`ReturnToStock.vb:1188-1201`)
- คืน supplier: `TINVENReturnToSupplier(FTReturnSuplNo, FDReturnSuplDate, FTPurchaseNo, FNMSysSuplId, FTStateApprove, FTApproveUser, FTRevokeUser)` + `_Detail(FNMSysRawMatId, FNMSysUnitId, FNPrice, FNDisPer, FNDisAmt, FNNetPrice, FNNetAmt, FNQuantity, FNSeq, FTBatchNo)` (`ReturnToSupplier.vb:1732-1747`)
- คืนจากลูกค้า: `TINVENReturnFG`, `_Detail`, `_DocRef(CSSaleOrderNo)` (`ReturnORRefund.vb:1073,1226`)

## 2. โอนคลัง → คลัง (TransferWHToWH*)

มี 2 รูปแบบ แยกด้วย `FNTransferWHType` (form กรองตอน load)

**A. แบบสแกน barcode — `TransferWHToWH.vb` (type 0; 317, 456)**
1. เลือกคลังต้นทาง/ปลายทาง/location ต้นทาง → สแกน barcode; ถ้าเอกสารยังไม่มีเลข ระบบ save header ให้ก่อน (1375-1383); เลขเอกสารจาก `DK.TL.Document.GetDocumentNo` (627)
2. ตรวจยอดด้วย `SP_GET_BARCODE_BALANCE_BATCH` (merge reserve = 1; `Barcode.vb:125-137`): ต้องมีแถวที่ `FNMSysWHId` = คลังต้นทาง และ `FNQuantityBal ≥ qty > 0` ไม่งั้นแจ้ง "Barcode ไม่ใช่ของคลังนี้" / "Balance ไม่พอ" (1296-1355)
3. เอา**แถว balance แถวแรก**ที่ผ่านเงื่อนไข: qty = ที่กรอก หรือทั้งยอดคงเหลือถ้าไม่กรอก; จำ `FTDocumentNo`, `FNMSysWHLocId`, `FTBarcodeNoRef` ของแถวนั้น (1309-1322)
4. เขียน `TINVENBarcode_OUT` ทันที (insert หรือ update ถ้า key เดิม: เอกสาร + barcode + ref + location; 1167-1195) → **สต็อกต้นทางลดทันทีตอนสแกน**
5. เมื่อมีแถวแล้ว ล็อกการแก้คลังต้นทาง/ปลายทาง/location (1446-1453)
- ลบ barcode ในเอกสาร = ลบแถว OUT (1218); ลบเอกสาร = ลบ header + OUT ทั้งหมด (963-971)
- barcode ไม่เปลี่ยนเลขและไม่ถูก update คลังใน `TINVENBarcode` — ตำแหน่งปัจจุบันคำนวณจากคู่ IN/OUT เท่านั้น

**B. แบบกรอกรายการ item + batch — `TransferWHToWHRM.vb`, `TransferWHToWHFG.vb`, `TransferWHToWHFGRM.vb` (type 1; `TransferWHToWHRM.vb:329,625`)** — 3 ไฟล์เกือบเหมือนกัน
1. เพิ่มแถว item/batch/qty/unit ใน grid (2027-2059); ทุกครั้งที่แก้ qty ตรวจ on-hand ด้วย `chevkOnhand` (2148-2168)
2. `chevkOnhand` (1486-1566): แปลงหน่วยด้วย `TCNMUnitConvert.FNRateTo` ถ้าไม่มีใช้ค่า hard-code ตาม unit id (12 / 1000 / 1); อ่าน `FNQuantityBal2` จาก view ที่**เลือกตามรหัสคลัง hard-code**: `2101`, `2202-1` → `V_Material_Batch_Balance_RM`; `2401`, `2601*` → `V_Material_Batch_Barcode_Balance`; อื่นๆ → `V_Material_Batch_Balance`; ผ่านเมื่อ `bal ≥ qty × rate`
3. save: ลบ OUT + detail เดิมทั้งเอกสารแล้วเขียนใหม่ (1331-1340); หา barcode ด้วย `TOP 1` จาก `TINVENBarcode` ตาม item + batch (1343-1363) → เขียน `TINVENTransferWH_Detail` (1373-1386)
4. รวม detail ต่อ barcode แล้วเขียน `TINVENBarcode_OUT` (1419-1449); ถ้า `FTStateCustWH` = โอนไปคลังลูกค้า qty ถูกคูณ rate แปลงหน่วย (1426-1435)
5. `FTStateCustWH`: เปิด tab ลูกค้า/ที่อยู่จัดส่ง, ราคาต่อหน่วย, ยอดรวม + VAT 7% (2317-2324, 2352-2362); ราคาจาก `V_Product_Price`, `V_PricePromotion` (2204-2236); พิมพ์ `rptInvoicepack_Transfer.rpt` (1228-1246)
- ปุ่ม approve ในฟอร์มนี้เป็น stub ว่าง (2275-2277)

**C. อนุมัติรับเข้า — `TransferWHToWHApprove.vb` (type 0), `TransferWHToWHFGApprove.vb` (type 1)**
- grid แสดงแถว OUT ของเอกสาร + `FNApproveQty` = qty ของ IN ถ้ามี ไม่งั้น = qty ที่โอน, `FNStateApproveQty` = 1 เมื่อมี IN แล้ว (`TransferWHToWHApprove.vb:470-506`)
- **Approve** (1167-1190): `FTStateApprove='1'` + ผู้อนุมัติ/วันเวลา และ**ผู้รับแก้คลัง/location ปลายทางได้**ตอนนี้ (1179)
- **Save barcode** (1223-1266): ต้อง approve แล้ว; วนทุกแถว เขียน `TINVENBarcode_IN` ที่คลัง/location ปลายทาง qty = `FNApproveQty` (1086-1095) → รับน้อยกว่าที่โอนได้ แต่ส่วนต่างไม่มีเอกสารรองรับ; ข้ามแถวที่มี OUT อ้างถึงแล้ว (1254)
- ฟอร์ม FG approve ทำ 2 ขั้นในคลิกเดียว และไม่ให้แก้คลังปลายทาง (`TransferWHToWHFGApprove.vb:1183-1196`)
- **Cancel approve** (1192-1221): ห้ามถ้ามี OUT ใดอ้าง `FTDocumentRefNo` = ใบโอนนี้ (ของที่รับแล้วถูกใช้ไป; `Barcode.vb:75-83`) → `FTStateApprove='0'` + ผู้ยกเลิก แล้ว**ลบ IN ทั้งเอกสาร** (1217)
- ลบ IN รายแถวได้ถ้า barcode ยังไม่ถูกตัดจ่ายต่อ (1297-1305)

**D. ย้าย location ในคลังเดียว — `wTransferLocation.vb`:** เขียน OUT ที่ location ต้นทาง และ IN ที่ location ปลายทาง**พร้อมกันใน transaction เดียว** คลังเดียวกัน (1042-1112) ไม่มีขั้นอนุมัติ; ใช้ `SP_GET_BARCODE_BALANCE` (1199); ลบเอกสารลบทั้ง IN/OUT (859-870)

**สำหรับระบบใหม่:** Transfer document เดียว มี mode เป็น tenant setting: `direct` (OUT+IN พร้อมกัน ใช้กับย้าย location) หรือ `two-step` (ship → in-transit → receive); ใช้คลังเสมือน in-transit ต่อ tenant โดย ship = movement ต้นทาง → in-transit, receive = in-transit → ปลายทาง; รับไม่ครบต้องปิดส่วนต่างด้วย movement ชนิด transfer-loss ที่มีเหตุผล; เลือก lot ตาม FIFO/FEFO หรือสแกน ไม่ใช่ `TOP 1`; โอนไปคลังลูกค้า (consignment) แยกเป็น flow ขาย ไม่ปนในใบโอน

## 3. คืนเข้าคลังจากการผลิต (ReturnToStock, ReturnToStockBulk)

- header อ้าง `FTJobOrderNo`; ดึงรายการที่เบิกไปด้วย `sp_getItem_forReturnToStock(JobKey)` แล้วเลือกแถว + กรอก qty ใน popup (`ReturnToStock.vb:1925-1965`) หรือเพิ่มแถว item/batch เอง
- save (1152-1296): ลบ detail แล้วเขียนใหม่เฉพาะแถว `qty > 0`; ลบ IN ของเอกสารแล้วเขียน `TINVENBarcode_IN` ใหม่ที่คลัง/location ใน header, `FTDocumentRefNo` = เลขใบคืนเอง
- barcode ที่รับคืน: ถ้าแถวไม่มี barcode ใช้ `TOP 1` จาก `TINVENBarcode` ตาม **item + คลัง + location (ไม่ดู batch)** (1236-1242)
- `ReturnToStockBulk.vb` ต่างตรงข้ามแถวที่ barcode ถูกจ่ายต่อแล้ว (`CheckTransactionOUT`) และไม่ลบ IN ก่อนเขียน
- ไม่ตรวจว่า qty คืน ≤ qty ที่เบิกของ job ในโค้ด VB (อาจอยู่ใน SP — ดู Gap)

## 4. คืน supplier (ReturnToSupplier, ReturnToSupplierFG, ReturnToFacFG)

- **ReturnToSupplier:** ผูก PO (`FTPurchaseNo`); สแกน barcode → `SP_GET_BARCODE_BALANCE` → ใช้เฉพาะแถว balance ที่ `FTPurchaseNo` ตรงกับ header (`ReturnToSupplier.vb:1268-1290`); ตัดยอดไล่ทีละแถว balance จนครบ qty เขียน OUT ต่อแถว (1305-1351)
- หรือเลือกจาก popup `SP_GET_Detail_ReturnSupl(returnNo, PO)` → ลบ detail เดิม เขียน detail ใหม่พร้อมราคา/ส่วนลดจาก PO, `FNNetAmt = qty × FNNetPrice` แล้วเขียน OUT (1688-1757)
- คลัง/location ของ OUT ถ้าไม่รู้ ใช้ของ `TINVENReceive` ใบแรกของ PO นั้น; barcode ถ้าไม่รู้ ใช้ `TOP 1` ตาม item (1118-1131)
- **ไม่มีขั้นอนุมัติ** ในฟอร์มนี้ — OUT เกิดทันที
- **ReturnToFacFG** (คืน FG กลับโรงงาน ใช้ตาราง `TINVENReturnToSupplier`): save เขียน detail (ราคา = 0) + OUT โดย join detail กับ `TINVENBarcode` ตาม item + batch เอา `MAX(FTBarcodeNo)` (`ReturnToFacFG.vb:1074-1130`); approve แค่ set `FTStateApprove='1'` (1059-1066); revoke ลบ OUT ทั้งเอกสารแล้ว set 0 (2271-2334)

## 5. คืนจากลูกค้า / refund (ReturnORRefund, ReturnORRefundFG)

- อ้าง SO/invoice ผ่าน `SP_GENSaleOrderForReturn` (`ReturnORRefund.vb:1896`); รับคืน = **สร้าง barcode ใหม่** (`SP_GEN_BARCODE_NO`, batch ใหม่จาก `Gen_BatchNo`) + `TINVENBarcode_IN` (1051, 1131-1163)
- approve: `FTStateApprove='1'` และถ้า `FTStateCRNote` สร้างใบลดหนี้ด้วย `ACC.dbo.SP_RERTOCREDITNOTE_INVOICE` ต่อ SO ที่อ้าง (3010-3067); revoke ลบ `TARTCreditNote_H/_D` ตรงๆ แล้ว set 0 (3117-3160)
- ลบเอกสารไม่ได้ถ้า barcode ที่สร้างถูกจ่ายออกแล้ว (`CheckDucumentDocRcvIssue`; 1391)
- อยู่ในขอบเขต flow ขาย/invoice (Enterprise) — ถอดละเอียดใน sales-invoice.md

**สำหรับระบบใหม่ (§3-5):** Return เป็น movement ชนิดของตัวเอง อ้าง movement ต้นทางเสมอ (issue line / receipt line / delivery line) และบังคับ `Σ คืน ≤ qty ต้นทาง`; คืนเข้า lot เดิม (lot id เดิม ไม่ `TOP 1` ตาม item); คืน supplier สร้าง debit note draft ให้ฝั่งจัดซื้อ; สถานะ QC ของของคืน (ใช้ได้/รอตรวจ/เสีย) เป็น tenant setting

## 6. สถานะและการล็อก

| เอกสาร | flag | ค่า | เปลี่ยนที่ |
|---|---|---|---|
| โอนคลัง | `FTStateApprove` | '' / 0 = ยังไม่รับ, 1 = ผู้รับอนุมัติ | `TransferWHToWHApprove.vb:1178`, ยกเลิก 1208 |
| คืนคลัง | `FTStateSendApp`, `FTStateApp`, `FTStateReject` | 0/1 | ส่ง `ReturnToStock.vb:1732`, อนุมัติ 2145, ถอน 2252 |
| คืน supplier (FG) | `FTStateApprove` | 0/1 | `ReturnToFacFG.vb:1064`, ถอน 2282 |
| คืนจากลูกค้า | `FTStateApprove`, `FTStateCRNote` | 0/1 | `ReturnORRefund.vb:3022`, ถอน 3130 |

- โอนคลัง: approve แล้วแก้/ลบ/สแกนเพิ่มไม่ได้ (`TransferWHToWH.vb:1082-1089, 1114-1121, 1286-1293`)
- คืนคลัง: **save ทุกครั้ง reset flag ทั้ง 3 เป็น 0** (`ReturnToStock.vb:1515-1522`); approve ไม่ตรวจว่าส่งอนุมัติแล้วหรือยัง และไม่ตรวจ period (2132-2151); ถอนอนุมัติ set `FTStateReject='1'` โดยไม่แตะ IN (2244-2260) — flag ไม่มีผลกับสต็อกเลย
- **period lock:** `Barcode.CheckCloseStock(คลัง, วันที่เอกสาร)` บล็อกถ้าคลังนั้นมีเดือนที่ปิดแล้ว ≥ วันที่เอกสาร (`Barcode.vb:360-380`); โอนคลังตรวจ**ทั้งคลังต้นทางและปลายทาง**ทุก action (save/ลบ/สแกน/approve/cancel); คืน supplier ตรวจทุกคลังที่มี OUT ในเอกสาร (`ReturnToSupplier.vb:1010-1029`); คืนคลังตรวจเฉพาะ save/ลบ; `wTransferLocation.vb` **ไม่ตรวจเลย**
- วันที่ที่ใช้ตรวจ = วันที่เอกสารใน header ส่วนแถว IN/OUT เก็บแค่วันที่ insert (`FDInsDate`) — วันที่ที่ stock card ใช้อยู่ใน SP (ดู inventory-close.md)

**สำหรับระบบใหม่ (state machine):**
- Transfer: Draft → Submitted → Shipped (post OUT) → Received / PartiallyReceived (post IN) → Closed (+ Cancelled ก่อน ship; หลัง ship ต้อง reverse)
- Return (ทุกชนิด): Draft → Submitted → Approved (N ขั้น config ต่อ tenant) → Posted (+ Rejected, Reversed); แก้เอกสารหลัง Submitted ต้องถอนกลับเป็น Draft อย่างชัดเจน ไม่ reset เงียบ
- period lock ตรวจด้วย posting date ของ movement ทั้งสองฝั่ง; receive ข้ามเดือนได้โดยยอดค้างอยู่ใน in-transit

## 7. SP / function / view ที่เกี่ยว (ไม่มี definition ใน repo — ต้องขอ)

- ยอด barcode: `SP_GET_BARCODE_BALANCE`, `SP_GET_BARCODE_BALANCE_BATCH`, `SP_GET_BARCODE_BALANCE_BATCH_FG`, `SP_GET_BARCODE_BALANCE_ISSUE`, `SP_GET_BARCODE_BALANCE_DOCUMENT`, `SP_GET_BARCODE_OUT_FOR_RET`, `SP_SEARCH_BARCODE` (`Barcode.vb:95-178`)
- popup: `sp_getItem_forReturnToStock` (`ReturnToStock.vb:1925`), `SP_GET_Detail_ReturnSupl` (`ReturnToSupplier.vb:1688`), `SP_GENSaleOrderForReturn` (`ReturnORRefund.vb:1896`)
- สร้างเลข: `SP_GEN_BARCODE_NO`, `Gen_BatchNo` (`ReturnORRefund.vb:1051,1140`), `DK.TL.Document.GetDocumentNo`
- บัญชี: `ACC.dbo.SP_RERTOCREDITNOTE_INVOICE` (`ReturnORRefund.vb:3058`)
- view: `V_Material_Batch_Balance`, `V_Material_Batch_Balance_RM`, `V_Material_Batch_Barcode_Balance` (`TransferWHToWHRM.vb:1501-1545`), `V_Material_Balance` (`ReturnToStock.vb:727`), `V_Product_Price`, `V_PricePromotion`, `V_OMCustomer` (`TransferWHToWHRM.vb:2204-2236`)
- UI: `SP_GET_DYNAMIC_OBJECT_CONTROL` (`TransferWHToWH.vb:278`)

## 8. ที่เกี่ยวข้องในโมดูลอื่น

- ผลิต → คลัง: `DK.SO/Packing/ProdOrderFillToTWH.vb` + `ProdOrderFillToTWHFGApprove.vb` ใช้ `TINVENTransferWH` / `_Detail` และ `CheckCloseStock` ชุดเดียวกัน (`ProdOrderFillToTWH.vb:510-517, 1197-1201`) — ถอดใน production-to-wh.md
- เบิก: ใบคืนคลังอ้าง job order ของใบเบิก `TINVENIssue.FTJobOrderNo`; `CheckDucumentDocRcvIssue` = มี OUT อ้างเอกสารนี้ (`Barcode.vb:24-28`)
- รับของ/PO: คืน supplier อ้าง `TINVENReceive.FTPurchaseNo`; `CheckAccAPayment` ตรวจ `ACC.TAPPayables_D` มีอยู่แต่ฟอร์มคืน supplier ไม่เรียก (`Barcode.vb:30-33`)
- รายงาน: `Report/ReturnToStockTracking.vb:115-117`, `Report/BIReturntoSupplier.vb:144-146` อ่านจาก header + IN/OUT; `Report/TransferTracking.vb` เป็นฟอร์มเปล่า (29 บรรทัด)

## Defects ของระบบเดิม (ห้ามยกมา)

- สต็อกขยับตอน save/สแกน ก่อนอนุมัติ; flag อนุมัติของใบคืนคลังไม่มีผลกับสต็อก (`ReturnToStock.vb:1258, 2145`)
- ยกเลิก/แก้ไข = ลบแถว IN/OUT ไม่เหลือประวัติ (`TransferWHToWHApprove.vb:1217`, `TransferWHToWHRM.vb:1331`, `ReturnToFacFG.vb:2307`)
- เลือก barcode ด้วย `TOP 1` ตาม item (ไม่ดู batch/คลัง/FIFO) → ตัดยอดผิด lot (`ReturnToStock.vb:1236-1241`, `ReturnToSupplier.vb:1129`, `TransferWHToWH.vb:851-853`)
- รหัสคลังและ unit id hard-code ใน logic เลือก view และแปลงหน่วย (`TransferWHToWHRM.vb:1348, 1428-1429, 1499, 1508, 1529-1537`); VAT 0.07 hard-code (2361)
- `wTransferLocation.vb` ตรวจสถานะอนุมัติผิดตาราง (`TINVENTransferWH` ด้วยเลขใบย้าย location; 966-968) และไม่ตรวจ period ปิด
- รับโอนน้อยกว่าที่ส่งได้โดยส่วนต่างหายไปเฉยๆ (`TransferWHToWHApprove.vb:1250, 1094`)
- `CheckTransactionIN` SQL มีวงเล็บเกิน → error ทุกครั้งที่เรียก (`Barcode.vb:60`; เรียกที่ `ReturnToStock.vb:1636`)
- คำสั่ง update ผิด syntax ตอนลบแถว detail บางส่วน (`Update FROM …`; `TransferWHToWHRM.vb:1589`) และ `DELETE … WITH(NOLOCK)` (`ReturnToStock.vb:1229`) — ไม่เห็น error เพราะ `ConnTrans.Excute` กลืน exception คืน 0 (`DK.Data/ConnTrans.cs:195-211`) → ยอด OUT/IN ค้างค่าเดิม
- ถอนอนุมัติใบคืนคลังรันบน connection ของ DB MAR และตัวแปรตรวจเงื่อนไขว่างเสมอ (`ReturnToStock.vb:2249-2260`)
- approve กับเขียน IN แยกคำสั่ง ไม่อยู่ใน transaction เดียว (`TransferWHToWHApprove.vb:1186, 1259`); การสแกนแต่ละครั้ง commit แยก
- dead code: `SaveBarcodeGrid` ใน `TransferWHToWH.vb:827-952` (call ถูก comment ที่ 806)
- empty catch, SQL concat, `NOLOCK` ทุก query

## Gap ที่ต้องดู SP definition / data

1. `SP_GET_BARCODE_BALANCE*`: สูตร `FNQuantityBal` (IN − OUT ต่อ barcode + เอกสาร IN + คลัง + location ใช่ไหม), parameter merge-reserve หัก reserve อย่างไร, เรียงแถวตามอะไร (FIFO ตามวันรับ?), `FTBarcodeNoRef` คืออะไร
2. view `V_Material_Batch_Balance*`: `FNQuantityBal2` ต่างจาก `FNQuantityBal` อย่างไร, 3 view ต่างกันตรงไหน, ทำไมผูกกับรหัสคลัง 2101 / 2202-1 / 2401 / 2601
3. ยอด in-transit: SP on-hand นับใบโอนที่มี OUT แต่ยังไม่มี IN เป็น `FNIntransitQuantity` หรือไม่ ของคลังไหน
4. วันที่ที่ movement ของใบโอนลง stock card: วันที่เอกสาร, วันที่ approve หรือ `FDInsDate` ของแถว IN — กรณี approve ข้ามเดือน
5. `sp_getItem_forReturnToStock`: จำกัด qty คืน ≤ เบิก − คืนแล้วหรือไม่, ผูก barcode เดิมของใบเบิกไหม
6. `SP_GET_Detail_ReturnSupl`: qty ที่คืนได้คิดจากรับ − คืนแล้ว − จ่ายออกแล้วหรือไม่, ราคาเอาจาก PO หรือใบรับ
7. คืน supplier มีผลกับยอดค้างรับของ PO / เจ้าหนี้ (AP) อย่างไร — มี trigger หรือ SP ฝั่ง PUR/ACC ไหม
8. `SP_RERTOCREDITNOTE_INVOICE` และ `SP_GENSaleOrderForReturn`: สร้างอะไรบ้าง, VAT type
9. ค่า `FNTransferWHType` อื่นนอกจาก 0/1 และ type ที่ `ProdOrderFillToTWH` ใช้; ความหมายทางธุรกิจของ `FTStateCustWH`
10. trigger บน `TINVENBarcode_IN/OUT`, `TINVENTransferWH` มีไหม (เช่น update ยอด, log)
11. `MSysTableObjForm` ของฟอร์มโอน/คืน: field บังคับ, ค่า default, query ตรวจก่อนลบ (`CheckDelFiled`)
12. สิทธิ์: ใครอนุมัติรับโอน/คืนได้ — ผูกคลังปลายทางหรือผูกเมนู (`TSEPermission*`, `PermissionWareHouse`)
