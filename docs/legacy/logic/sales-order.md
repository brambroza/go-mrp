# Logic เดิม: SO entry → อนุมัติ → customer PO → SO (DK.SO)

ถอดจาก repo DK-ERP-SYSTEM 2026-09-29 · อ้างอิง path ในระบบเดิม · DB: PUR (`TSOTOrder*`, `TPURTPurchase*`), DK_MASTER (ลูกค้า/item/หน่วย), DK_PROD (PD, MRP, แผน), SYSTEM (เลขเอกสาร, dynamic form)
BOM / MRP / PD / scheduling ดู `mrp-planning.md` · ลูกค้า / price list ดู `masters.md` · PO ดู `purchasing.md`

**สรุปสำคัญ:** SO ของ DK.SO เป็น **ใบสั่งงานภายใน (demand ให้ผลิต)** ไม่ใช่เอกสารขาย — ฟอร์มถูก copy จาก PO (`Purchase.vb`) line ถูกบันทึกด้วย **ราคา/ส่วนลด/ยอด = 0 เสมอ** เก็บจริงแค่ item, หน่วย, qty, วันส่ง
อนุมัติที่โค้ดเขียนจริงมี **ขั้นเดียว** (`FTStateManagerApp`) — ขั้น supervisor และ reject (`'2'`) มีแต่ใน query รายงาน ไม่มีโค้ด set
"customer PO" = **PO ของบริษัทในเครือ** ที่ออกถึง supplier id คงที่ (= โรงงาน) และอนุมัติแล้ว → กดสร้าง SO ด้วย `SP_Create_SaleOrderAuto` แล้วรัน MRP
ไม่มี credit check, ไม่มี price list, ไม่มียอดส่งมอบเทียบ SO, ไม่มีปิด line ของ SO
→ ระบบใหม่ต้องทำ SO เป็นเอกสารขายจริง (ราคา, VAT, เครดิต, ยอดส่ง) + approval N ขั้นแบบ config + เขียน demand ให้ MRP ตอนอนุมัติ

## 1. ตารางและ entity

- **SO header** `TSOTOrder(FTSaleOrderNo, FDSaleOrderDate, FTSaleOrderBy, FNMSysCmpId, FNMSysCustId, FTPurchaseNo = PO ต้นทาง ('' = คีย์มือ), FNMSysCrTermId, FNCreditDay, FNMSysTermOfPMId, FNMSysCurId, FNExchangeRate, FNMSysDeliveryId, FDDeliveryDate, FNPoType, FNSaleOrderType, FTContactPerson, FTRefer, FTRemark, FNSOAmt, FNDisCountPer/Amt, FNSONetAmt, FNVatPer/Amt, FNSurcharge, FNPOGrandAmt, FTSOGrandAmtTH/EN, FTSaleOrderState, FTStateSendApp, FTStateSuperVisorApp, FTStateManagerApp, FTStatePrint, FTStateSendMail, FTStateColorBox)` — ชื่อ field มาจาก control ของฟอร์ม (`DK.SO/SaleOrder.Designer.vb`); เขียนแบบ dynamic จาก metadata `MSysTableObjForm` (`DK.SO/SaleOrder.vb:198-263`, save 650-1000, insert/update 917-925)
- column ที่เขียนด้วย SQL ตรง: `FTSendAppBy/Date/Time` (`SaleOrder.vb:2523-2528`), `FTSuperManagerName/AppDate/AppTime` (2570-2575), `FTStateCancel, FTStateCancelNote` (1174-1180), `FTPrintBy/Date/Time` (1308-1312); อ่านอย่างเดียว: `FTSuperVisorAppDate` (`Track/SaleOrderTracking.vb:298`)
- **SO line** `TSOTOrder_Order(FTSaleOrderNo, FNSeq, FNMSysRawMatId, FNMSysUnitId, FNPrice, FNDisPer, FNDisAmt, FNQuantity, FNNetAmt, FTRemark, FDDeliveryDate, FTStateClose)` — เขียนที่ `SaleOrder.vb:1380-1462` (คีย์มือ), 1697-1756 (ดึงจาก PO); `FTStateClose` ถูกอ่านใน tracking (`SaleOrderTracking.vb:336-341`) แต่**ไม่มีโค้ดเขียน**
- **customer PO** ไม่มีตารางแยก = `TPURTPurchase` / `TPURTPurchase_OrderNo` (โครงสร้างใน `purchasing.md` §1) ผูกกับ SO ด้วย `TSOTOrder.FTPurchaseNo`
- **ลูกค้า** `DK_MASTER.TCNMCustomer(FNMSysCustId, FTCustCode, FTCustNameTH)` (`SaleOrderTracking.vb:344`) — คนละชุดกับ `OMCustomer` ของงานขาย (`masters.md` §1)
- **item** `TPORawmaterial` ทุก item เลือกได้ ไม่กรองประเภท FG (`SaleOrder.vb:1351-1370`); หน่วยจาก view `V_Unit` (1372)
- ผล MRP / PD ที่อ้าง SO: `TPDTSOMRP.FTSONo`, `TPDMProdcutOrder(FTSaleOrderNo, FNMSysMainRawMatId, FNSOQty, FNSeq)` — ดู `mrp-planning.md` §2-3

**สำหรับระบบใหม่:** `sales.sales_orders` / `sales_order_lines` (line มี `unit_price numeric(18,4)`, `discount_percent`, `qty numeric(18,6)`, `uom_id`, `requested_delivery_date`, `closed_at`, `source_customer_po_line_id` nullable), `sales.customer_pos` / `customer_po_lines` เป็น entity ของตัวเอง (เลข PO ของลูกค้า + ไฟล์แนบ) ไม่ยืมตาราง PO ฝั่งซื้อ; ลูกค้าชุดเดียว (`masters.customers`); ทุกตารางมี `tenant_id`

## 2. SO entry (`SaleOrder.vb`)

**เลขเอกสาร**
- ออกจาก `SP_GEN_DOCUMENTNO` (ชื่อ DB, ชื่อตาราง, doc type ว่าง, prefix = `CmpRunDoc` ของบริษัทที่ login) ตอน save ครั้งแรก **ก่อนเปิด transaction** (`SaleOrder.vb:740, 746`; `DK.TL/Document.vb:3-10`); รูปแบบ/การ reset อยู่ใน SP
- ตัดสินว่าเป็นเอกสารใหม่โดยเทียบเลขบนจอกับ "รูปแบบเลข" ที่ SP คืน (699-713); ตัวสร้างเลขแบบ PO (prefix + Y/N VAT + กลุ่ม + ปี) ถูก comment ทั้งหมด (2886-2922)
- `FTSaleOrderState` = "<user> MANUAL <วันเวลา>" ตอนสร้าง (743)

**ค่า default** (hard-code; `SaleOrder.vb:495-505, 1345`): ลูกค้า `00-0000000000`, credit term `CASH`, สกุล `THB`, payment term `D30`, credit day 30, จุดส่ง `JMT`, VAT 7%, บริษัท = บริษัทที่ login; field บังคับมาจาก metadata (525-598)

**line**
- คีย์ใน grid: แถวใหม่เพิ่มได้เมื่อแถวปัจจุบันมีรหัส item และ price ≥ 0, qty ≥ 0 (1500-1517, 2461-2480); popup `SaleOrderAddItem` อยู่หลัง `Exit Sub` = ไม่ถูกเรียก (1517-1616)
- save line = ลบทุก line ของ SO แล้ว insert ใหม่ตามลำดับ grid, `FNSeq` รัน 1..n (1385-1440); **`FNPrice, FNDisPer, FNDisAmt, FNNetAmt` เขียนเป็น 0 และ `FTRemark` เป็นค่าว่าง** เสมอ (1413-1418, 1428-1433)
- วันส่ง: `FDDeliveryDate` ต่อ line (1419) ไม่มี validation (ไม่เช็กย้อนหลัง / lead time / วันหยุด); filter ใน tracking ใช้ `FDDeliveryDate` ของ header (`SaleOrderTracking.vb:357-361`)
- ลบ line: ลบด้วย SO + item (ไม่ใช้ `FNSeq`) แล้ว save ทั้งใบ (1896-1930)

**ราคา / สูตร (ทำงานบนจอ แต่ line ไม่ถูกเก็บ)**
- แหล่งราคา: ไม่มี — lookup item คืนราคา 0 (1354); `GetPricePO` อ่าน `TPurchasePrice` (ราคาซื้อ) ตาม item + หน่วย แต่ handler ถูกปลด `Handles` (2796, 2820-2838); ไม่ใช้ price list / promotion ของ `masters.md` §4
- line: `FNDisAmt = price × qty × FNDisPer / 100`, `FNNetAmt = qty × price − FNDisAmt` (2385-2428); popup ใช้อีกสูตร `FNDisAmt = price × FNDisPer / 100` ต่อหน่วย (`SaleOrderAddItem.vb:44-77`)
- header: `FNSOAmt = Σ FNNetAmt` (2430-2437); `FNDisCountAmt = FNSOAmt × FNDisCountPer / 100` กรอกได้ 2 ทาง; `FNSONetAmt = FNSOAmt − FNDisCountAmt`; `FNVatAmt = FNSONetAmt × FNVatPer / 100` แก้มือได้; `FNPOGrandAmt = FNSONetAmt + FNVatAmt + FNSurcharge` + ตัวอักษร TH/EN (1815-1894, 1932-1939)
- สกุลเงิน: `TFINMCurrency.FTStateLocal = '1'` → rate ล็อก; เปลี่ยนสกุลแล้ว rate reset เป็น 1; rate ≤ 0 ถูกแก้เป็น 1 (1811-1813, 1967-1981); ไม่มีการแปลงยอดเป็นบาท
- credit check: **ไม่มี** — ไม่อ่านวงเงิน/ยอดค้างของลูกค้าที่ใดใน `SaleOrder.vb`
- แปลงหน่วย: ไม่มีใน SO — tracking join `TCNMUnitConvert` แต่ไม่นำ rate ไปใช้ (`SaleOrderTracking.vb:388`); การแปลงเกิดตอนสร้าง PD (`mrp-planning.md` §3)

**สำหรับระบบใหม่:** เลขเอกสารจาก sequence ต่อ tenant ต่อประเภทภายใน transaction; ราคาจาก price resolver กลาง (`masters.md` §4) เก็บ snapshot ราคา/ส่วนลด/VAT ลง line; นิยามส่วนลดแบบเดียว (% ของยอด line); VAT rate + include/exclude เป็น tenant setting; default (term, สกุล, จุดส่ง) มาจากลูกค้าแล้วค่อย tenant; credit check ตอน submit (`credit_limit − ยอด SO/invoice ค้าง`) เลือกได้ว่า block หรือเตือน; วันส่งต้อง ≥ วันเอกสาร และเทียบ lead time ผลิตได้; qty ใช้ UOM conversion กลาง

## 3. customer PO → SO

**A. สร้างอัตโนมัติ (`SaleOrderApproved.vb`)**
- รายการที่แสดง = `TPURTPurchase` ที่ `FTStateManagerApp = '1'` และ `FNMSysSuplId = 1708080001` (hard-code) และวันที่ PO > 2020-01-01 และเลข PO ยังไม่อยู่ใน `TSOTOrder.FTPurchaseNo` (`SaleOrderApproved.vb:346-347`); filter บริษัทถูก comment (346); กรองช่วงวันที่/เลข PO/supplier ได้ (349-379)
- ติ๊กเลือก → ปุ่มอนุมัติ → ต่อ PO: เรียก `SP_Create_SaleOrderAuto(เลข PO, user, company id, prefix เลขเอกสาร)` (560) → อ่าน SO line ที่ `FTPurchaseNo` = PO นั้น (564-568) → `CalculateSOMRP(SO, item, qty)` ต่อ line (569); การสร้าง PD + วางแผนถูก comment (572-573)
- 1 PO สร้าง SO ได้ครั้งเดียว (PO หายจากรายการเมื่อมี SO อ้าง); สถานะอนุมัติ/ราคา/วันส่งของ SO ที่ SP สร้าง อ่านจากโค้ดไม่ได้ (Gap 1)
- ปุ่ม "re-open" ในฟอร์มนี้ถูกซ่อน (`SaleOrderApproved.designer.vb:599`) และ update ตาราง **PO line** ไม่ใช่ SO (`SaleOrderApproved.vb:829-841`)

**B. ดึง line จาก PO มาใส่ SO มือ (`SaleOrder.vb:1617-1790` + popup `SaleOrdertemlist`)** — ทำงานเมื่อ header มี `FTPurchaseNo`
- ยอดคงเหลือต่อ item: `FNPOBalQty = Σ PO qty − Σ qty ของ SO ใบอื่นที่อ้าง PO เดียวกัน` แสดงเฉพาะ > 0 (1619-1665); ติ๊ก = ใส่ยอดคงเหลือเต็ม (`SaleOrdertemlist.vb:73-84`)
- เกินยอด: `over = (qty ครั้งนี้ + qty ของ SO ใบอื่น) − PO qty` เมื่อ > 0 เก็บใน `FNRcvQtyOver` ของ grid (`SaleOrdertemlist.vb:112-166`) — ไม่พบโค้ดที่ block การบันทึกเมื่อเกิน
- บันทึก line ด้วยราคา/ส่วนลดจาก PO, `FNNetAmt = qty × (FNPrice − FNDisAmt)` (1709-1740) แล้ว reset สถานะส่งอนุมัติ (1759-1768)
- ทาง B เป็นทางเดียวที่ line ของ SO มีราคา แต่ save header ครั้งถัดไปจะเขียนราคาเป็น 0 ทับ (§2)

**สำหรับระบบใหม่:** customer PO เป็นเอกสารรับเข้า (คีย์/แนบไฟล์/import) → action "สร้าง SO" เลือก line + qty ได้ (partial) คุม balance ระดับ line: `po_line.qty − Σ so_line.qty` ห้ามเกิน (หรือ tolerance ต่อ tenant); ลูกค้าในเครือ/inter-company เป็นข้อมูลลูกค้า ไม่ hard-code id; SO ที่สร้างเริ่มที่ `Draft` แล้วเข้า approval route ปกติ

## 4. สถานะและการอนุมัติ SO

| column | ค่า | เปลี่ยนที่ไหน |
|---|---|---|
| `FTStateSendApp` | 0/1 | ปุ่มส่งอนุมัติ (`SaleOrder.vb:2519-2538`); reset 0 ตอน save ทับ (1158-1170) |
| `FTStateManagerApp` | 0/1 (`'2'` = reject เฉพาะใน query) | ปุ่มอนุมัติ (2540-2585); ถอน → 0 (2947-2972) |
| `FTStateSuperVisorApp` | 0 เท่านั้น (`'2'` = reject เฉพาะใน query) | ถูก reset (1165) ไม่เคยถูก set 1 หรือ 2 |
| `FTStateCancel` + note | 0/1 | checkbox ตอน save ต้องมีเหตุผล (1149-1155, 1174-1180) |
| `FTStatePrint` | 1 | พิมพ์ `rptSaleOrder.rpt` (1298-1320) |
| line `FTStateClose` | อ่านอย่างเดียว | ไม่มีโค้ดเขียน / re-open สำหรับ SO |

- flow จริง: Draft → (ส่งอนุมัติ) → อนุมัติขั้นเดียว → สร้าง PD; ปุ่มอนุมัติ**ไม่ตรวจ**ว่าส่งอนุมัติแล้ว และไม่ตรวจ user (2542); ถอนอนุมัติต้องมีทั้งส่ง + อนุมัติ และไม่ตรวจว่ามี PD แล้ว (2950)
- 2 ขั้น (supervisor → manager) และ reject: มีแค่ column แสดงผลใน `SaleOrderTracking.vb:294-300`, `SaleOrderApproved.vb:301-307` — ไม่มีปุ่ม/โค้ด reject SO ใน DK.SO; ไม่มีหน้ากล่องอนุมัติ SO (DK.MNG มีเฉพาะ PO — `purchasing.md` §4)
- auto-approve: `ApproveAuto` (set ส่ง + อนุมัติด้วย user ที่ save) มีอยู่แต่ call ถูก comment (1014-1031, 1188-1193)
- อนุมัติ SO มือ**ไม่รัน MRP / ไม่สร้าง PD** (โค้ดถูก comment 2546-2568) — MRP รันตอนสร้าง PD จาก `ProductionOrderList.vb` (`mrp-planning.md` §3)
- ล็อกแก้ไข: `FTStateManagerApp` = 1 → save และ delete ไม่ได้ (1143-1146, 1218-1221); delete ไม่ได้ถ้ามี `TPDMProdcutOrder.FTSaleOrderNo` = SO (1203-1212, 1223-1226) แต่ **แก้ไข/ลบ line ได้แม้มี PD แล้ว** ถ้ายังไม่อนุมัติ
- ยกเลิก: ทำได้เฉพาะ SO ที่ยังไม่อนุมัติ (เพราะ save ถูกล็อก) → SO ที่อนุมัติแล้วต้องถอนอนุมัติก่อน; ไม่พบโค้ดใน DK.SO / DK.MRP ที่อ่าน `FTStateCancel` ของ SO → SO ที่ยกเลิกยังขึ้นใน tracking และสร้าง PD ได้
- ลบ = hard delete header + line (1033-1056)

**สำหรับระบบใหม่ (state machine):**
- SO: `Draft → Submitted → Approved → InProduction → PartiallyDelivered → Delivered → Closed` (+ `Rejected` กลับ Draft พร้อมเหตุผล, `Cancelled`, `Reopened`)
- line: `Open → PartiallyDelivered → Delivered | ClosedShort`; header derive จาก line
- ขั้นอนุมัติ = **route ต่อ tenant ใน approval engine กลาง** (จำนวนขั้น, ผู้อนุมัติตาม role/วงเงิน/กลุ่มลูกค้า, auto-approve); ค่า default ของ DK = 2 ขั้น supervisor → manager; ผู้อนุมัติ ≠ ผู้สร้าง; 2FA + อนุมัติบน mobile
- `Approved` → เขียน `production.demands` (source = SO line, item, qty, due date) ใน transaction เดียวกัน; ถอนอนุมัติ/ยกเลิก → ยกเลิก demand และทำได้เมื่อยังไม่มี work order ที่ released; ทุก transition ลง audit log; ยกเลิกเป็น soft

## 5. ยอดคงค้าง SO / tracking

- **SO qty vs PD qty (ฟอร์ม SO):** `FNQuantityBal = SO qty − FNSOQty ของ PD แถวแรกที่เจอ` (SO + item, `TOP 1`; `SaleOrder.vb:373-384`)
- **SO qty vs PD qty (ทางหลัก):** `FNQtyBanlance = SO qty − Σ FNSOQty ของ main PD` ต่อ SO + item + `FNSeq` (`Production/ProductionOrderList.vb:379-412`; `mrp-planning.md` §3)
- **SO qty vs ยอดส่งมอบ:** ไม่มี — ไม่พบเอกสารเบิก FG / packing / invoice ที่อ้าง `TSOTOrder` เพื่อตัดยอด
- **`Track/SaleOrderTracking.vb`:** list SO line + flag ส่ง/อนุมัติ/reject (257-395) กรองวันที่ SO, วันส่ง, เลข SO, รหัสลูกค้า; ไม่มี qty คงค้าง; filter บริษัทถูก comment (346)
- **`Production/SaleOrderStatus.vb`:** สถานะผลิตต่อ SO line เป็น flag 0/1 จากการมีอยู่ของข้อมูล (348-369): มี PD → มีแผน (`Appointments`) → เริ่ม mix (`TPDTProdActual`) → เริ่ม fill (`TPDTProdActualFill`) → FG อนุมัติ (`FTStateAppToFG = '1'`)
  - double-click → รายการ PD จาก `fn_GetOrderProdStatus` → ลบแผน + PD ทั้งหมดของ SO ได้ถ้ายังไม่เริ่ม mix (831-879)
  - ปุ่มอนุมัติ (customer PO → SO + MRP + PD + plan; 500-559) ถูกซ่อน (`SaleOrderStatus.designer.vb:370`)
- **`Production/SaleOrderAccept.vb`:** list SO line + flag มี PD แล้ว (338) → double-click เปิด `GenerateProdOrder` (527-553; ทางเก่า — `mrp-planning.md` §3)
- `BI/BISaleOrder.vb` เป็น copy ของ BI ฝั่ง PO (อ่าน `TPURTPurchase`, รับ, คืน; 138-158) ไม่ได้อ่าน SO

**สำหรับระบบใหม่:** ต่อ SO line เก็บ/คำนวณ `qty_ordered, qty_planned (Σ work order), qty_produced, qty_delivered (จาก inventory.movements ที่อ้าง so_line_id), qty_open`; สถานะผลิตมาจาก state ของ work order ไม่ใช่การมีอยู่ของแถว; หน้า tracking เดียวใช้สูตรเดียว กรอง tenant เสมอ

## 6. Link ไป flow อื่น

- customer PO → SO: `TSOTOrder.FTPurchaseNo` (§3); demand ฝั่ง MRP จาก PO เปิด (`Get_PurchaseFGDemand`) อยู่ใน `mrp-planning.md` §2C
- SO → MRP: `CalculateSOMRP` เขียน `TPDTSOMRP`, `TPDTSOMRPSummary` (`SaleOrderApproved.vb:569`; `mrp-planning.md` §2A)
- SO → PD: `TPDMProdcutOrder.FTSaleOrderNo` + `FNSeq` (`ProductionOrderList.vb`, `GenerateProdOrder.vb:89-98`); แผนผลิตอ้าง SO ผ่าน `Appointments.FTSaleOrderNoRef` (`Production/PlanSchedulingManual.vb:780`)
- SO → เบิกวัตถุดิบ: ใบเบิกอ้าง PD (`FTJobOrderNo`) แล้ว join กลับ `TSOTOrder` เพื่อแสดงข้อมูล (`DK.INVEN/Transaction/Issue.vb:2609-2610`) — รายละเอียดใน `inventory-issue.md`
- SO → packing / FG เข้าคลัง: `Packing/PackingFG.vb:610-613` join PD → `TSOTOrder` เพื่อดึงลูกค้า/หน่วย; เลข SO ติดไปกับบาร์โค้ด FG เป็น `FTSaleOrderNoRef` (`Packing/ProdOrderFillToTWH.vb:456`) — flow อยู่ใน `production-to-wh.md` (ยังไม่ถอด)
- SO → invoice: ไม่มีใน DK.SO — งานขาย/packing/invoice ใช้ `DB_PAYROLL.dbo.TSaleOrder` ของ LM.SaleVat (flow 15 `sales-invoice.md`) ไม่พบโค้ดเชื่อมกับ `TSOTOrder`

## 7. SP / function / view ที่เกี่ยว (ไม่มี definition ใน repo — ต้องขอ)

- `SP_Create_SaleOrderAuto` (`SaleOrderApproved.vb:560`, `Production/SaleOrderStatus.vb:516`; param: เลข PO, user, company id, prefix เลขเอกสาร)
- `SP_Create_SaleOrder_With_PD` (`Production/PlanSchedulingManual.vb:1957`)
- `SP_GEN_DOCUMENTNO` (`DK.TL/Document.vb:6,16`), `SP_GET_DYNAMIC_OBJECT_CONTROL` (`SaleOrder.vb:235`)
- function `fn_GetOrderProdStatus(SO, item code)` (`SaleOrderStatus.vb:845`, `ProductionOrderList.vb:1407`)
- view `V_Unit` (`SaleOrder.vb:1372`); metadata `MSysTableObjForm` ของฟอร์ม `SaleOrder`
- Crystal: `rptsaleorder.rpt`, `rptSaleOrder_details.rpt` (`SaleOrder.vb:1271, 1286, 1303`)

## Defects ของระบบเดิม (ห้ามยกมา)

- line ของ SO ถูกเขียนราคา/ส่วนลด/ยอด = 0 และ remark ว่าง ทุกครั้งที่ save; header ถูกบันทึกด้วยยอดบนจอก่อน แล้วยอดรวมถูกคำนวณใหม่จาก line ที่เป็น 0 → header กับ line ไม่ตรงกัน (`SaleOrder.vb:917-925, 1413-1418, 1450-1458`)
- save line = ลบทั้งหมดแล้ว insert ใหม่ `FNSeq` รันใหม่ตามลำดับ grid ขณะที่ PD ผูก SO ด้วย `FNSeq` → แก้ SO หลังสร้าง PD ทำให้ balance ชี้ผิด line (`SaleOrder.vb:1385-1440`; `ProductionOrderList.vb:379-412`)
- `SaveDetail` ไม่คืนค่าและกลืน exception; ผู้เรียกไม่ตรวจผล แล้ว commit ต่อ (`SaleOrder.vb:975-976, 1459-1462`)
- ลบ line ด้วย SO + item ไม่มี `FNSeq` → item เดียวกันหลาย line หายหมด (`SaleOrder.vb:1909`); balance ในฟอร์ม SO ใช้ `TOP 1` ไม่ sum (380)
- อนุมัติได้โดยไม่ต้องส่งอนุมัติ, ไม่ตรวจผู้อนุมัติ, ถอนอนุมัติได้แม้มี PD/แผนแล้ว; ถอนอนุมัติเขียนทับชื่อ/เวลาผู้อนุมัติ ไม่มี audit (`SaleOrder.vb:2542, 2950-2962`)
- reset flag อนุมัติทำนอก transaction ก่อน save — save ล้มเหลวแล้ว flag หายไปแล้ว (`SaleOrder.vb:1158-1172`)
- path ดึง line จาก PO reset column `FTStateApp, FTStateReject, FTStateSendAppBy` ซึ่งไม่ใช่ชุด flag ที่ฟอร์มใช้ (`SaleOrder.vb:1760-1766`)
- `SaleOrderApproved`: transaction เปิดบน DB PROD แต่เรียก SP ของ PUR; อ่าน SO ที่เพิ่งสร้างผ่าน connection อื่น (`NOLOCK`); หลัง `Rollback` ยัง `Commit` ต่อ และแสดงข้อความสำเร็จเสมอ (`SaleOrderApproved.vb:551, 560-568, 580-586`)
- supplier id `1708080001` และวันที่ตัด `2020/01/01` hard-code; ไม่กรองบริษัท (`SaleOrderApproved.vb:346`); ชื่อ DB `DK_MASTER`, `DK_PROD` hard-code (`SaleOrder.vb:1356`, `SaleOrderStatus.vb:858-865`)
- ปุ่ม re-open/อนุมัติที่ซ่อนอยู่เป็นโค้ด copy จาก PO: update ตาราง PO line และอ้าง column ที่ grid ไม่มี (`FNMSysRawMatId`, `FNSeq`, `FTSelect`) (`SaleOrderApproved.vb:829-841`, `SaleOrderStatus.vb:506, 795-807`)
- `SaleOrderStatus`: ลบ PD/แผนของ **ทั้ง SO** ด้วย hard delete นอก transaction ไม่กรอง item ที่เลือก; query รันซ้ำ 2 รอบ; ภาษา EN สร้าง SQL ผิด syntax (`SaleOrderStatus.vb:308, 377-384, 856-868`; SQL ผิดแบบเดียวกันที่ `SaleOrderTracking.vb:314`, `SaleOrderAccept.vb:316`)
- ราคาขายดึงจากตารางราคาซื้อ `TPurchasePrice` (`SaleOrder.vb:2827`); ส่วนลด line 2 นิยาม (ต่อหน่วย vs ทั้ง line) ใน column เดียว (`SaleOrder.vb:2405` vs `SaleOrderAddItem.vb:58`)
- ค่า default hard-code (`SaleOrder.vb:500-505`), unit `PCS` (425); เลขเอกสารออกนอก transaction (740)
- SQL concat ทุก query; ปุ่มส่ง/อนุมัติ/ถอน และ `ApproveAuto` ไม่ escape เลข SO (`SaleOrder.vb:1026, 1207, 2528, 2575, 2962`); `SaleOrderStatus.vb:845, 858-867`
- empty catch ในทุกปุ่มอนุมัติ → ล้มเหลวเงียบ (`SaleOrder.vb:2535, 2582, 2969`; `SaleOrderApproved.vb:592`); hard delete SO
- ฟอร์ม SO ถูก copy ไปเป็นฐานของ `ProductionOrder.vb`, `ProductionOrderList.vb`, `Actual/RouteActual_.vb` พร้อมโค้ด update/delete `TSOTOrder` ที่ค้างอยู่ (`ProductionOrderList.vb:838-856`, `RouteActual_.vb:949-980`) — เสี่ยงเขียน SO จากหน้าจออื่น

## Gap ที่ต้องดู SP definition / data

1. `SP_Create_SaleOrderAuto`: map PO → SO อย่างไร (ลูกค้าได้จากไหน, ราคา/ส่วนลด/VAT, วันส่งต่อ line, รวม/แยก line, หน่วย), set `FTStateSendApp/ManagerApp` ให้เลยหรือไม่, กัน PO ซ้ำอย่างไร, เลข SO ออกใน SP หรือไม่
2. `SP_GEN_DOCUMENTNO` สำหรับ `TSOTOrder`: รูปแบบเลข SO, reset รายปี/เดือน, กันเลขซ้ำอย่างไร
3. metadata `MSysTableObjForm` ของฟอร์ม `SaleOrder`: field จริงของ `TSOTOrder`, field บังคับ, default, เงื่อนไข lookup (ลูกค้า, PO ที่เลือกได้), query ตรวจก่อนลบ (`CheckDelFiled`)
4. `FTStateSuperVisorApp` และค่า `'2'` (reject) ถูก set จากที่ใด (trigger / โปรแกรมอื่น / ไม่เคยใช้) — data จริงมีค่าอะไรบ้าง
5. สิทธิ์ปุ่มส่ง/อนุมัติ/ถอน คุมที่ไหน (`TSEPermission*`) และใครเป็นผู้อนุมัติ SO ในทางปฏิบัติ
6. `TSOTOrder_Order.FTStateClose`: ใครเขียน (trigger / SP) และมี column re-open ของ SO line หรือไม่
7. supplier id `1708080001` คือใคร และความสัมพันธ์บริษัทในเครือ (บริษัทที่ออก PO ↔ `TCNMCustomer` ของ SO); SO คีย์มือที่ลูกค้า = `00-0000000000` ใช้กรณีใด
8. ค่า `FNSaleOrderType`, `FNPoType`, `FTStateColorBox`, `FTStateSendMail`, `FTRefer` ครบทุกค่าและความหมาย
9. `fn_GetOrderProdStatus`: column ที่คืน และนิยามสถานะ PD ต่อ SO
10. trigger บน `TSOTOrder*` (sync ไป `DB_PAYROLL.dbo.TSaleOrder`, roll-up สถานะ, MRP) มีไหม; SO ของ DK.SO กับ sale order ของ LM.SaleVat เชื่อมกันด้วยอะไร
11. ยอดส่งมอบต่อ SO ดูจากที่ใดในทางปฏิบัติ (`FTSaleOrderNoRef` บนบาร์โค้ด FG / รายงาน Crystal) — ตรวจตอนถอด `production-to-wh.md` และ `sales-invoice.md`
12. data จริง: SO ที่ line มีราคา ≠ 0 มีสัดส่วนเท่าไร (ยืนยันว่า SO ไม่ถูกใช้เป็นเอกสารราคา), SO ที่ `FTStateCancel = '1'` แต่มี PD
13. `_StateManual` ใน `ProductionOrderList.vb:1088-1103` เป็นจริงเมื่อ SO **มี** `FTPurchaseNo` (ตรงข้ามกับชื่อตัวแปรและกับที่ `mrp-planning.md` §3 เขียนไว้) — ยืนยันกับผู้ใช้ว่า item `FTStateNotCreateJob` ควรถูกข้ามในกรณีใด
14. layout/สูตรใน `rptsaleorder.rpt` (แสดงราคาหรือไม่, ลายเซ็นผู้อนุมัติ)
