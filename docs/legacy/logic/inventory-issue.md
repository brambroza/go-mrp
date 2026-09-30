# Logic เดิม: เบิกตาม barcode + FIFO (DK.INVEN Issue*)

ถอดจาก repo DK-ERP-SYSTEM 2026-09-29 · อ้างอิง path ในระบบเดิม · DB: DK_INVENTORY, DK_MASTER, DK_PROD, PUR, MAR (DB_PAYROLL)

**สรุปสำคัญ:** `SP_CUT_BARCODE_FIFO` **ไม่ถูกเรียกแล้ว** — ทุก call site ถูก comment; FIFO ที่ใช้จริงอยู่ใน VB และเป็น FIFO หยาบ:
เลือก batch ด้วย `ORDER BY FTBatchNo ASC` (เรียงตามตัวอักษรของเลข batch ไม่ใช่วันรับ/วันหมดอายุ) แล้วตัด barcode ภายใน batch โดย**ไม่มี ORDER BY**
จังหวะตัดสต็อกไม่เหมือนกันต่อฟอร์ม (RM/PK ตัดตอนอนุมัติ, bulk/semi ตัดทุกครั้งที่ save, สแกน barcode ตัดทันที) · reserve ไม่ได้ใช้จริงในฟอร์มเบิก · ไม่ใช้ expiry และ QC flag
→ ระบบใหม่ต้องมี allocation engine ตัวเดียว (FIFO/FEFO เป็น tenant setting), ตัดสต็อกตอน Post เท่านั้น, และกันยอดติดลบที่ระดับ DB

## 1. ตาราง

- `TINVENIssue` header: `FTIssueNo, FDIssueDate, FNMSysWHId, FNMSysWHLocId, FTJobOrderNo, FNMSysIssueSectId, FTTypeIssue, FNMSysCmpId` (+ `CSSaleOrderNo` ใน `IssueFG.vb`) และ flag `FTStateSendApp/By, FTStateApp/By, FTStateReject/By, FTStateAppDate/Time` — header เขียน generic จาก dynamic form (`DK.INVEN/Transaction/Issue.vb:989-1251`)
- `TINVENIssue_Detail`: `FTIssueNo, FNSeq, FNMSysRawMatId, FNMSysUnitId, FNQuantity, FNRawmatPrice (เขียน 0), FTDocumentRefNo, FTOrderNo, FTStateReserve, FNMSysWHId, FNMSysWHLocId, FNMSysCmpId` (`Issue.vb:1293-1309, 2827-2837`)
  - โหมดกรอกมือ: `FTDocumentRefNo` = **batch no** (`Issue.vb:1284,1300`); โหมดเลือกจากแผน: `FTOrderNo` = เลขแผน/job (`Issue.vb:2834`, `Issue_bulk.vb:2462`)
- `TINVENBarcode_OUT` movement ออก: `FTBarcodeNo, FTDocumentNo (= เลขใบเบิก), FTDocumentRefNo, FNMSysWHId, FNMSysWHLocId, FTOrderNo, FNQuantity, FTStateReserve, FNRawmatPrice, FNMSysCmpId` (`Issue.vb:1383-1389, 1726-1745`)
  - `FTDocumentRefNo`: โหมดสแกน = เลขเอกสารที่นำ barcode เข้า (`Issue.vb:2071`); โหมด FIFO = batch no (1385)
- `TINVENBarcode_OUT_FIFO`: มีแต่คำสั่งลบ (`Issue.vb:1516`) คนเขียนคือ `SP_CUT_BARCODE_FIFO` ที่ถูก comment (`Issue.vb:1786-1791`)
- view balance (ไม่มี definition): `V_Material_Balance` (ต่อ item), `V_Material_Batch_Balance` (ต่อ batch), `V_Material_Barcode_Balance`, `V_Material_Batch_Barcode_Balance` (คอลัมน์ `FTBarcodeNo, FTBatchNo, FTDateExpire, FNInQuantity, FNOutQuantity, FNQuantityBal2, FNMSysWHId, FNMSysWHLocId, FNMSysCmpId`; `Issue_bulk.vb:2851-2856`)
- แหล่งความต้องการ: `DK_PROD.Appointments` (แผน), `TPDMProdcutOrder`, `TPDMProdcutOrder_ConsumtionEdit`, `INVEN.TINVENJobOrder(_Detail)`, `MAR.TSaleOrder(_Detail)`

| ฟอร์ม | ใช้กับ | `FTTypeIssue` | ตัดสต็อกเมื่อ |
|---|---|---|---|
| `Issue.vb` | RM/PK เข้าผลิต | 1 (`Issue.vb:2424`) | กด Approve |
| `Issue_bulk.vb` | bulk | 3 (`Issue_bulk.vb:916,2358`) | ทุก Save |
| `Issue_semi.vb` | semi | 3 (`Issue_semi.vb:924,2358`) | ทุก Save |
| `IssueFG.vb` | FG / ตาม SO | 1 (`IssueFG.vb:2398`) | Save (ไม่มี SO) หรือทันทีที่เลือกบรรทัด SO |
| `IssueBySaleOrder.vb` | ชื่อว่าตาม SO | — | โค้ดสแกนใช้งานไม่ได้ (ดู §5) |
| `Issue_repair.vb` | เบิกซ่อม | 1 | สำเนาของ `Issue.vb` (นอกขอบเขต) |

**สำหรับระบบใหม่:** `issues` + `issue_lines` (item, qty ที่ขอ, อ้าง work order material line / SO line) + `issue_allocations` (lot, location, handling unit, qty) · Post แล้ว allocation กลายเป็น movement `ISSUE` ใน `inventory.movements` · ฟอร์มเดียว แยกประเภทด้วย `issue_type` + item type ไม่ใช่ copy ฟอร์ม

## 2. วิธีเพิ่มรายการเบิก (3 ทาง)

**A. สแกน barcode** (`Issue.vb:1920-2145`)
1. item ของ barcode ต้อง `TPORawmaterial.FNRawmatState = 1` (`CheckRawMet`, 1905-1926)
2. ยอดคงเหลือจาก `SP_GET_BARCODE_BALANCE(barcode, 0, '', เลขใบเบิก, cmp, '1')` — parameter สุดท้าย = รวม reserve (`Barcode.vb:95-108`, `Issue.vb:1981`)
3. เงื่อนไข: มีแถว `FNQuantityBal > 0` ในคลังของ header → ไม่งั้น "Barcode ไม่ใช่ของคลังนี้"; ต้องมีแถว `FTOrderNo = ''` → ไม่งั้น "Barcode ไม่ใช่ของ Order นี้" (2038-2039, 2133-2136)
4. จำนวน: `FNIssueBarType` index 1 = กรอกจำนวนเอง (เบิกบางส่วน) ค่าอื่น = เบิกทั้งยอดคงเหลือของ barcode (1895-1897, 1950-1955, 2017-2021); default = 1 (2423)
5. loop ตัด: หยิบแถวแรกที่ คลัง + location ตรง header และ balance > 0, `qty = min(balance, ยอดที่ยังขาด)` → `SaveBarcode` → โหลด balance ใหม่ด้วย `SP_GET_BARCODE_BALANCE_DOCUMENT` → วนจนครบ (2058-2093)
6. `SaveBarcode` insert หรือ update แถว `TINVENBarcode_OUT` ตาม key (ใบเบิก + barcode + `FTDocumentRefNo` + location) (1712-1802) → **สต็อกถูกตัดทันที**
- สแกน barcode เดิมซ้ำ = ลบแถวเดิมของ barcode นั้นก่อนแล้วเขียนใหม่ (2047)
- `Issue_bulk.vb` / `Issue_semi.vb` บังคับ `balance ≥ qty ที่ขอ` และใส่เลขแผนลง `TINVENBarcode_OUT.FTOrderNo` (`Issue_bulk.vb:1970,2021`, `Issue_semi.vb:1970,2021`; เทียบ `Issue.vb:2036`)

**B. เลือกจากแผนผลิต** (ปุ่ม new item, `Issue.vb:2456-2909`) เมื่อ header มี `FTJobOrderNo`
- `FTJobOrderNo` = `Appointments.FTPlanNo` (`Issue.vb:2623,2762`)
- รายการ: ถ้าแผนมี `TPDMProdcutOrder_ConsumtionEdit` ใช้ยอดที่แก้แล้ว (2509-2623) ไม่งั้น `SP_GET_ItemIssue_RMPK(@IssNo, @PlanNo, @whId, @WhLocId)` (2767-2768); bulk = `SP_GET_ItemIssue_Bulk` (`Issue_bulk.vb:2410`), semi = `SP_GET_ItemIssue_Semi` (`Issue_semi.vb:2410`)
- `ยอดค้างเบิก = ความต้องการของแผน − Σ TINVENBarcode_OUT ของใบเบิกอื่นที่ FTJobOrderNo เดียวกัน` (`Issue.vb:2527-2535`); แสดง on-hand จาก `V_Material_Balance` (2614)
- RM/PK: ลบ `TINVENIssue_Detail` ทั้งใบแล้ว insert บรรทัดที่เลือก จากนั้น `EXEC SP_Issue_RM_PK '<เลขใบเบิก>'` (2810-2882)
- bulk/semi: insert บรรทัด (มี batch จาก SP) แล้วหา barcode ด้วย `TOP 1` จาก `V_Material_Barcode_Balance` ที่ item + คลัง + location + batch และ `FNQuantityBal2 > 0` (ไม่มี ORDER BY) แล้วเรียกขั้นตอนสแกน (`Issue_bulk.vb:2455-2504`)

**C. กรอกมือ** (ไม่มี `FTJobOrderNo`; `Issue.vb:2911-2938, 3153-3369`)
- เลือก item → batch ถูกเติมให้ด้วย `GetBathFIFO` (3197-3216) ผู้ใช้เปลี่ยน batch ได้
- กรอก qty → `CheckOnhand`: `Σ FNQuantityBal2 ของ item + คลัง + location + batch (+ qty เดิมของบรรทัด) ≥ qty` ไม่ผ่าน → แจ้งยอดคงเหลือแล้ว set qty = 0 (3223-3369)

**สำหรับระบบใหม่:** mobile สแกน handling unit/lot → ระบบเสนอ lot ตามกฎ FIFO/FEFO และเตือนถ้าสแกนข้ามลำดับ (block/warn เป็น tenant setting) · เบิกตาม work order ดึง material line + ยอดค้างเบิก · เบิกเกินความต้องการต้องมีเหตุผล/อนุมัติ (config)

## 3. FIFO และการตัด barcode

**ลำดับ batch** (`GetBathFIFO`, `Issue.vb:3197-3216`; เหมือนกันใน `Issue_bulk.vb:2788-2793`, `Issue_semi.vb:2797-2802`, `IssueFG.vb:2990-2995`)
`TOP 1 FTBatchNo FROM V_Material_Batch_Balance WHERE item, cmp, คลัง, location, FTBatchNo <> '' ORDER BY FTBatchNo ASC`
- ไม่ใช้วันรับ, วันหมดอายุ หรือเลข barcode; ไม่กรองยอด > 0 ใน VB (ขึ้นกับ view)
- PO ที่แสดงคู่ batch = `TOP 1 FTPurchaseNo` ของ barcode ใดก็ได้ใน batch นั้น (3208-3210)

**การกระจายลง barcode** (`Issue.SaveData(StatApprove)`, `Issue.vb:1332-1421`)
1. ลบ `TINVENBarcode_OUT` ทั้งหมดของใบเบิก (1336-1337)
2. ต่อบรรทัด detail: อ่าน barcode ของ item + คลัง + location + batch จาก `V_Material_Batch_Barcode_Balance` (**ไม่มี ORDER BY**) (1366-1371)
3. ต่อ barcode: `ใช้ = min(FNQuantityBal2, ยอดที่ยังขาด)` → insert OUT, `FTStateReserve = ''` → ลดยอดที่ขาด (1373-1402) = เบิกบางส่วนของ barcode ได้ และ 1 บรรทัดกระจายได้หลาย barcode
4. ถ้ายอดรวมไม่พอ ส่วนที่ขาด**หายเงียบ** ไม่มี error (1363-1407)
- `Issue_bulk.vb` / `Issue_semi.vb` / `Issue_repair.vb`: logic เดียวกันแต่อ่าน `V_Material_Barcode_Balance` และ `StatApprove` ถูก hard-code เป็น True → ทำทุกครั้งที่ save (`Issue_bulk.vb:1254-1365`, `Issue_semi.vb:1262-1264`, `Issue_repair.vb:1325-1327`)
- `IssueFG.vb` (ไม่มี SO): `ROW_NUMBER() OVER (PARTITION BY item, คลัง, location ORDER BY FTBarcodeNo)` แล้วเอา**เฉพาะแถวที่ 1** ใส่ qty ทั้งบรรทัดลง barcode นั้น ไม่ตรวจยอดคงเหลือ ไม่ดู batch (`IssueFG.vb:1361-1383`) = FIFO ตามเลข barcode

**Unit conversion:** ไม่มีในฟอร์มเบิก — โค้ดแปลงหน่วยใน `CheckOnhand` ถูก comment ตัวคูณ = 1 (`Issue.vb:3275-3327`); ตัวหาร 1000 ของ unit 1707180006 ถูก comment (`Issue.vb:2656,2715`) → เบิกเป็นหน่วยสต็อกของ barcode
**ปัดเศษ:** ความต้องการจากแผนถูก convert เป็น `numeric(18,0)` (`Issue.vb:2643,2674`); `CheckOnhand` รับ qty เป็น Integer ใน `Issue_semi.vb:2819`, `IssueFG.vb:3012`
**Expiry / QC:** ไม่มีการตรวจวันหมดอายุหรือ `FTStateQC` ตอนเบิก (`FTDateExpire` เป็นแค่คอลัมน์ของ view)
**กันยอดติดลบ:** มีเฉพาะระดับ UI (`CheckOnhand`, ตรวจ balance ตอนสแกน) ไม่มีการล็อกแถวหรือ constraint; query ใช้ `WITH (NOLOCK)`
**ต้นทุน:** โหมดสแกน `TINVENBarcode_OUT.FNRawmatPrice` = `TPORawmaterial.FNPrice` (ราคา master) ไม่ใช่ราคารับของ barcode (`Issue.vb:1738-1745`); โหมด FIFO ไม่เขียนราคา (1383-1385); `TINVENIssue_Detail.FNRawmatPrice = 0` (1304)

**สำหรับระบบใหม่:** allocation rule ต่อ tenant/item type: `FIFO` (วันรับของ lot), `FEFO` (expiry), `MANUAL` · เรียง `(expiry | received_at), lot_no, handling_unit` ให้ผลซ้ำได้ (deterministic) · ข้าม lot ที่ไม่ใช่ Released หรือหมดอายุ (min remaining shelf life เป็น setting) · ขาดยอด = error ทั้งเอกสาร · Post ใน transaction เดียวพร้อม lock ยอด lot/location และ check `on_hand ≥ 0` · ต้นทุนออกตาม lot (FIFO cost) เก็บใน movement

## 4. สถานะ, อนุมัติ, ล็อก, ยกเลิก

**Flag บน `TINVENIssue`**
| Flag | ค่า | เปลี่ยนโดย |
|---|---|---|
| `FTStateSendApp` | 0/1 | ปุ่มส่งอนุมัติ → 1 (`Issue.vb:2393-2417`); Save และการสแกน/ลบ barcode → 0 (1631-1638, 2104-2111, 2364-2371) |
| `FTStateApp` | 0/1 | ปุ่ม Approve ("ยืนยันการจ่ายสินค้า") → 1 (`Issue.vb:2944-2978`); Revoke → 0 |
| `FTStateReject` | 0/1 | Revoke ของ `Issue.vb` → 1 (3126-3131); ของ `Issue_bulk.vb` → 0 (2715-2716) |

**Approve**
- `Issue.vb`: `SaveData(True)` = ตัดสต็อกตาม §3 แล้ว set flag (2956-2965) — อนุมัติขั้นเดียว ไม่ตรวจว่าส่งอนุมัติแล้วหรือไม่
- `Issue_bulk.vb` / `Issue_semi.vb`: set flag อย่างเดียว เพราะสต็อกถูกตัดตั้งแต่ save (`Issue_bulk.vb:2570-2599`)
- `IssueFG.vb`: set `MAR.TSaleOrder.CSStatePack = '1'` ของ SO แล้ว set flag (`IssueFG.vb:2755-2790`)

**ล็อก**
- อนุมัติแล้ว: `VerrifyData` ไม่ให้แก้ (`Issue.vb:854-857`), ลบบรรทัดไม่ได้ (3021-3024)
- ปิดสต็อกเดือนนั้นแล้ว (`Barcode.CheckCloseStock`, `Barcode.vb:360-380`): save, ลบ, สแกน (`Issue.vb:1623,1659,1928,1970`)
- มีการคืนเข้าสต็อกอ้างใบเบิก: `TINVENBarcode_IN.FTDocumentRefNo = ใบเบิก` (`Barcode.CheckDocumentRefIn`, `Barcode.vb:85-93`; `Issue.vb:1618,1654`)
- มีบรรทัดแล้ว ล็อก คลัง, location, job, แผนกที่เบิก (`Issue.vb:2259-2293`)
- ใบสั่งผลิตถูกล็อกเมื่อมี `TINVENIssue.FTJobOrderNo` อ้างถึง (ฝั่ง DK.SO — ดู mrp-planning.md §6)

**ยกเลิก / คืนสต็อก**
- Revoke approve: ลบ `TINVENBarcode_OUT` ทั้งหมดของใบเบิก = ยอดกลับเข้า barcode เดิม (`Issue.vb:3123-3124`, `Issue_bulk.vb:2723-2724`)
- ลบบรรทัด: ลบ detail + OUT ของ item + batch นั้น (`Issue.vb:3041-3060`); ลบ barcode ที่สแกน: ลบ OUT (1804-1847)
- ลบเอกสาร: ลบ header + `TINVENBarcode_OUT` + `TINVENBarcode_OUT_FIFO` (`Issue.vb:1500-1529`)
- ไม่มี void/reversal และไม่มีประวัติ — การคืนของจริงใช้เอกสาร ReturnToStock (flow inventory-transfer)

**สำหรับระบบใหม่ (state machine):** Issue: `Draft → Submitted → Approved (N ขั้น config) → Posted → Closed` + `Rejected`, `Voided`; Draft/Submitted อาจจอง (soft allocation) แต่ไม่ตัดยอด; Post = เขียน movement; Void = movement กลับรายการเข้า lot เดิม ทำไม่ได้ถ้างวดปิดแล้วหรือ work order ปิดแล้ว; ทุกการเปลี่ยนสถานะมี audit log

## 5. Links ไป flow อื่น

- **เบิก → แผน/ใบสั่งผลิต:** `TINVENIssue.FTJobOrderNo` = `Appointments.FTPlanNo` → `Appointments.ProdNo` = `TPDMProdcutOrder.FTProductOrderNo` (`Issue.vb:2606-2608, 2743-2744`); ยอดเบิกสะสมต่อแผน = Σ `TINVENBarcode_OUT` ผ่าน `TINVENIssue.FTJobOrderNo` (2527-2535)
- **เบิก → job order (ซ่อมบำรุง):** `LoadJobDetail` อ่าน `TINVENJobOrder(_Detail)` (`Issue.vb:772-835`) — โครงเดิมของ w* forms
- **เบิกตาม SO** (`IssueFG.vb:2429-2592`): header `CSSaleOrderNo`; บรรทัดจาก `MAR.TSaleOrder_Detail` (`CSProductCode, CNQty`), `ยอดค้าง = Σ CNQty − ยอดเบิกของ SO` (2447-2448); detail เก็บ `FTOrderNo = เลข SO` (2523); revoke ตรวจ `TSaleOrder.CSStateApproved` — ถ้าส่ง AR แล้วยกเลิกไม่ได้ (2913-2931)
- **`IssueBySaleOrder.vb`:** แม้ชื่อเป็น SO แต่อ่าน `TINVENJobOrder_Detail` ด้วยเลข SO (608-666, 1538-1541); มี confirm "จ่ายเกินยอด Request" (1569-1573) และบังคับ `balance ≥ qty` (1582); ตัวแปร balance ไม่ถูก assign (1531) และ handler สแกนไม่มี `Handles` (1476, 1720) → ทางสแกนใช้ไม่ได้
- **Reserve:** `FTStateReserve` ถูกเขียนเป็น '' หรือ '0' ทุกที่ในฟอร์มเบิก (`Issue.vb:1385,1737,1756,2836`; `Issue_bulk.vb:1342-1343`); `TINVENReserve` ปรากฏเป็นชนิดเอกสารใน `Barcode.LoadDocumentBarcode` (`Barcode.vb:266-269`) แต่ไม่มีฟอร์มจองใน `DK.INVEN/Transaction`; ยอดจองที่ MRP ใช้มาจาก `FN_Get_Reserve` (`DK.MRP/MRPDemandbyPORM.vb:228`)
- **คืนเข้าสต็อก:** `SP_GET_BARCODE_OUT_FOR_RET` (`Barcode.vb:167-173`) อ่าน OUT ของใบเบิกเพื่อทำใบคืน

## 6. SP / function / view ที่เกี่ยว (ไม่มี definition ใน repo — ต้องขอ)

- ตัด FIFO: `SP_CUT_BARCODE_FIFO` — call ถูก comment ทั้งหมด (`Issue.vb:1790`, `Issue_bulk.vb:1746`, `Issue_semi.vb:1746`, `IssueFG.vb:1752`, `IssueBySaleOrder.vb:1361`, `Issue_repair.vb:1769`); `SP_Issue_RM_PK` (`Issue.vb:2881`, `Issue_repair.vb:2860`)
- balance: `SP_GET_BARCODE_BALANCE`, `SP_GET_BARCODE_BALANCE_ISSUE`, `SP_GET_BARCODE_BALANCE_BATCH`, `SP_GET_BARCODE_BALANCE_BATCH_FG`, `SP_GET_BARCODE_BALANCE_DOCUMENT`, `SP_GET_BARCODE_OUT_FOR_RET`, `SP_SEARCH_BARCODE` (`Barcode.vb:95-178`)
- รายการตามแผน: `SP_GET_ItemIssue_RMPK` (`Issue.vb:2767`), `SP_GET_ItemIssue_Bulk` (`Issue_bulk.vb:2410`), `SP_GET_ItemIssue_Bulk_manual` (`Issue_bulk.vb:1446`), `SP_GET_ItemIssue_Semi` (`Issue_semi.vb:2410`), `get_detail_issue_semi` (`Issue_semi.vb:626`)
- view: `V_Material_Balance`, `V_Material_Batch_Balance`, `V_Material_Barcode_Balance`, `V_Material_Batch_Barcode_Balance`, `V_ISSUEDelivery` (`IssueFG.vb:1657`)
- function: `FN_Get_Reserve`, `FN_Get_Onhand_RMPK` (ฝั่ง MRP)

## Defects ของระบบเดิม (ห้ามยกมา)

- FIFO เรียงด้วยตัวอักษรของ `FTBatchNo` (`Issue.vb:3206`) และตัด barcode ใน batch โดยไม่มี ORDER BY (`Issue.vb:1366-1371`, `Issue_bulk.vb:2489-2494`) → ผลไม่แน่นอน
- ยอดไม่พอแล้วตัดเท่าที่มีโดยไม่แจ้ง (`Issue.vb:1363-1407`); `IssueFG.vb` ใส่ qty ทั้งหมดลง barcode แรกโดยไม่ตรวจยอด (1364-1377) → ติดลบได้
- จังหวะตัดสต็อกต่างกันต่อฟอร์ม; `StatApprove` hard-code True (`Issue_bulk.vb:1254-1255`) ทำให้ปุ่ม Approve ไม่มีความหมายทางสต็อก
- `Issue.vb` โหมดสแกนตรวจแค่ balance > 0 (เงื่อนไข `≥ qty` ถูก comment; 2036) และ loop ที่ 2058 จบเมื่อ qty ครบหรือ SP ไม่คืนแถวเท่านั้น
- Approve ลบ OUT ทั้งใบแล้วสร้างใหม่จาก detail (`Issue.vb:1336-1337`) → barcode ที่สแกนจริงถูกแทนด้วยผลจัดสรรอัตโนมัติ
- `IssueFG` revoke update ตาราง `TINVENReceive` ด้วย `WHERE FTIssueNo` บน connection MAR (`IssueFG.vb:2918-2926`) → ไม่ได้ยกเลิกอะไร และไม่ลบ OUT
- `Issue_bulk` revoke แล้ว set checkbox อนุมัติเป็น True และแสดงข้อความ "ยืนยันการเบิก" (`Issue_bulk.vb:2726-2728`)
- ลบเอกสารไม่ลบ `TINVENIssue_Detail` (`Issue.vb:1500-1529`) → detail กำพร้า
- `Barcode.CheckTransactionIN` มีวงเล็บเกินใน SQL (`Barcode.vb:60`) และเทียบกับ `TINVENMMaterial.FTBarcodeNo` (61) → การกันแก้ barcode ที่คืนสต็อกแล้วไม่ทำงานตามตั้งใจ; `DeleteBarcode` ลบ OUT ด้วยเงื่อนไขเดียวกัน (`Issue.vb:1812-1817`)
- หลังเลือกรายการจากแผน โค้ดรัน query `SELECT TOP 1` ค้างในตัวแปรซ้ำแทนคำสั่ง reset สถานะ (`Issue_bulk.vb:2511`, `IssueFG.vb:2568`)
- ราคาเบิกใช้ราคา master ไม่ใช่ต้นทุน lot (`Issue.vb:1738-1745`); qty ถูกตัดเป็นจำนวนเต็ม (`Issue.vb:2643`, `Issue_semi.vb:2819`)
- reserve, expiry, QC flag ไม่ถูกใช้ตอนเบิก; ฟอร์ม copy-paste 6 ชุด; SQL concat (เลขแผน/รหัส item ไม่ escape: `Issue.vb:2510, 3233`); empty catch; `NOLOCK`

## Gap ที่ต้องดู SP definition / data

1. `SP_Issue_RM_PK`: ตัด FIFO เองไหม เรียงด้วยอะไร เขียน `TINVENBarcode_OUT` / `_OUT_FIFO` ตอนไหน (ทับกับการตัดตอน Approve หรือไม่)
2. `SP_CUT_BARCODE_FIFO` และ `TINVENBarcode_OUT_FIFO`: ยังมีข้อมูล/รายงาน (`IssueFIFOTracking`) ใช้อยู่ไหม ความหมายต่างจาก `TINVENBarcode_OUT` อย่างไร
3. view `V_Material_*_Balance`: สูตร `FNQuantityBal` กับ `FNQuantityBal2` ต่างกันอย่างไร (หัก reserve? หักใบเบิกที่ยังไม่อนุมัติ?), กรอง QC / expiry / `FTStateWase` ไหม, มี ORDER BY ในตัวหรือไม่
4. `SP_GET_BARCODE_BALANCE*` ทั้ง 5 ตัว: ความหมาย parameter merge reserve, `FTOrderNo`, `FTBarcodeNoRef`, และลำดับแถวที่คืน (มีผลกับ loop ตัดยอด)
5. `SP_GET_ItemIssue_RMPK/Bulk/Semi`: ความต้องการคิดจากอะไร (BOM × qty แผน หรือ `TPDMProdcutOrder.FNQuantity`), เลือก batch ให้อย่างไร, แปลงหน่วยไหม
6. รูปแบบ `FTBatchNo` (จาก `Gen_BatchNo`) เรียงตามเวลาได้ไหม — ตัดสินว่า FIFO เดิมถูกต้องแค่ไหน
7. reserve: ใครเขียน `TINVENReserve` และ `FTStateReserve = '1'`; `FN_Get_Reserve` นับจากอะไร
8. ค่า `FTTypeIssue` ทั้งหมด (0, 1, 3, …) และ `FNRawmatState` (1, 2, …) หมายถึงอะไร; CboList ของ `FNIssueBarType`
9. ผู้อนุมัติใบเบิกคือใคร — สิทธิ์ปุ่ม Approve/Revoke มาจาก `TSEPermission*` (ไม่มี data); `FTStateSendApp` ถูกใช้โดยหน้าจอ/รายงานใด (`wIssueApproveTracking`)
10. `MAR.TSaleOrder` (DB_PAYROLL) กับ `PUR.TSOTOrder` เป็น SO คนละระบบ — FG เบิกตามตัวไหนในการใช้งานจริง; `CSStatePack`, `CSStateApproved` มีค่าอะไรบ้าง
11. trigger บน `TINVENBarcode_OUT` / `TINVENIssue` มีไหม (stock card, cost, update สถานะ PD/แผน)
12. ข้อมูลจริงมี barcode ยอดติดลบหรือ OUT ที่ไม่มี IN มากแค่ไหน (ใช้วางแผน migrate ยอดยกมา)
