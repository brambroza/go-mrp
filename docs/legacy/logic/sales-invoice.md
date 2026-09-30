# Logic เดิม: Pre-sale → อนุมัติขาย → packing → invoice (VAT) → คอมมิชชัน (LM.SaleVat + LM.Payroll เฉพาะ incentive)

ถอดจาก repo DK-ERP-SYSTEM 2026-09-29 · อ้างอิง path ในระบบเดิม · DB: **DB_PAYROLL** (legacy; ในโค้ดคือ `Config.DataBase.MAR`), DK_ACCOUNT (ACC), DK_INVENTORY (INVEN), DK_MASTER, DK_SYSTEM
path ย่อ: ไฟล์ที่ไม่ระบุโฟลเดอร์อยู่ใน `LM.SaleVat/SaleOrder/` · ฟอร์ม `*_TT` เป็นสำเนาที่ล็อกประเภทเอกสาร = 4 (`xSaleOrder_TT.vb:186, 283`) ไม่ถอดแยก

**สรุปสำคัญ:** เอกสารเดียว (`TSaleOrder`) เป็นทั้งใบสั่งขาย ใบส่งของ และใบกำกับภาษี — **เลข invoice = เลข SO** เดินสถานะด้วย flag 3 ตัว: อนุมัติขาย → แพ็ก (ตัดสต็อก) → อนุมัติตั้งหนี้ (สร้าง invoice ใน ACC)
สต็อกถูกตัด**ตอนอนุมัติแพ็ก** ด้วย SP ที่ไม่มีใน repo; ยกเลิก = ลบใบเบิกและแถว barcode OUT ทิ้ง
"คอมมิชชัน" ใน LM.SaleVat เป็นแค่รายงานยอดขาย ส่วนการคำนวณ incentive อยู่ใน LM.Payroll และ**ไม่ได้ดึงยอดจาก invoice** — ยอดขายจริง import จาก Excel ยอดเก็บเงินคีย์มือ
→ ระบบใหม่แยก SO / delivery / invoice เป็นคนละเอกสาร, เลขเอกสารคนละชุด, invoice export ไปโปรแกรมบัญชี

## 1. ตาราง

**DB_PAYROLL**
- `TSaleOrder(CSSaleOrderNo, CSSendProductCode = เลขใบส่งของ, CDSaeOrderDate, CSCustomerCode, CNGrpCustomerId, CSCustomDocNo = เลข PO ลูกค้า, CSSaleCode, CDDeliveryDate, CSDeliveryTime, CNDeliveryId, CSDeliveryAddress, CSPayType, CNSaleOrderType, CSStatePrint, FTStateOther, CNSaleOrderAmt, CNSaleOrderDiscountAmt1..3 = **% ส่วนลดท้ายบิล**, CNSaleOrderNetAmt, CNSaleOrderVatPer, CNSaleOrderVatAmt, CNSaleOrderGrandTotalAmt, CNSaleOrderGrandTotalAmtTHB = ตัวอักษร, FTStateCustWH, FNMSysWHId, FNMSysWHLocId, FTStateNotConvert, FNMSysCmpId, CSStateSaleOrder, CSStatePack, CSStateApproved + …By/Date/Time, CSSaleOrderNoRef)` — update ก่อน ถ้าไม่สำเร็จจึง insert (`xSaleOrder.vb:244-331`)
- `TSaleOrder_Detail(CSSaleOrderNo, Seq, CSProductCode, CSUnitCode, CNQty, CNUnitPrice, CNDiscountPer1..3, CNDiscountAmt, CNAmount, FTStateFree = ของแถม)` — ลบทั้งหมดแล้ว insert ใหม่ (`xSaleOrder.vb:355-394`)
- `TPreSaleOrder` / `TPreSaleOrder_Detail` โครงเดียวกับ SO — โค้ดมีแต่ update/delete (`xPreSaleOrderEdit.vb:244-280, 357-377, 411-413`) **ไม่มี insert ใน repo**
- `TSaleOrder_Status(CSSendProductCode, CSStatePacking, CSUserIns, …)` แถวเดียวต่อใบส่งของ: `'1'` = แพ็กแล้ว, `'2'` = ตั้งหนี้แล้ว (`xSaleOrder_Pack.vb:1043-1051`, `xSaleOrder.vb:2361-2369`)
- ลูกค้าบุคคล: `MCustomerPerson(CSCustIdNo, CSCustFirstName, CSCustLastName, CSAddress, CNShopId, CNPrefixId)` (`LM.SaleVat/xAddCustPerson.vb:28`), `VTInvoice`, `VTInvoice_H` (`LM.SaleVat/xSaleAmtTrack.vb:277, 299`)
- incentive: `TCommPlan`, `TCommStand`, `TIncome`, `TPayrollCalc`, `TSSaleScore`, `TSSSaleScoreCalc`, `TSSaleRcv`, `TPayroll`, `TSaleProduct` + master `MSalRcvPlan`, `MProduct`, `MSubProduct`, `MProductCategory`, `MMainProductCountry` — key ร่วมทุกตาราง = พนักงาน + ตำแหน่ง + แผนก + ส่วน + เดือน + ปี (ข้อ 6)

**DB อื่นที่ flow นี้เขียน**
- ACC: `TARTInvoiceEntry_H(FTSihDocNo = เลข SO, FCSihAmtNet, FDSihDueDate, FTSihVatInOrEx, FTSihTaxInvNo, FTSihStaApprove, FTSihRefARDocNo)`, `TARTReciveEntry_D(FTArhDocNo, FCArdAmtNet)`, `TARTCreditNote_H/_D` (`xSaleOrder.vb:2274-2297`, `DK.ACC/Accounts Receivable/ARTInvoiceEntry.vb:2138-2205`)
- INVEN: `TINVENIssue`, `TINVENIssue_Detail(FTOrderNo = เลข SO)`, `TINVENBarcode_OUT`, `TINVENReturnFG` (`xSaleOrder_Pack.vb:1115-1124`, `xSaleOrder_RET_Pack.vb:700-706`)

**สำหรับระบบใหม่:** `sales.orders` + `order_lines`, `sales.deliveries` + `delivery_lines` (อ้าง order line), `sales.invoices` + `invoice_lines` (อ้าง delivery line), `sales.credit_notes`; ส่วนลดเก็บทั้ง % และจำนวนเงินต่อขั้น; ลูกค้าใช้ master ชุดเดียว (masters.md §1)

## 2. เลขเอกสาร / ประเภท

- เลข SO ออกตอน insert ด้วย `SP_GEN_DOCUMENTNO 'DB_Payroll','TSaleOrder', <index ของ CNSaleOrderType>` — ส่ง run id ของบริษัทเฉพาะบริษัทรหัส `BRPC` (`xSaleOrder.vb:281-287`)
- เลขใบส่งของ = เลข SO แทนข้อความ `IN` ด้วย `SO` (`xSaleOrder.vb:289, 545`) → เลข SO มี prefix `IN` อยู่แล้ว = เลข invoice; ใบกำกับภาษีพิมพ์จาก `rptInvoice.rpt` ด้วยเลขเดียวกัน (`xSaleOrder_Inv.vb:913-914`)
- ตอน insert SO เรียก `SP_GEN_PACKING <เลข SO>` ทันที (`xSaleOrder.vb:332`)
- `CNSaleOrderType` index 0 = ขายมี VAT (VAT 7%) index อื่น = VAT 0% (`xSaleOrder.vb:560-564, 577-581`); รายงานแปลง index 0/1/2 เป็น VAT type ของ ACC = 2/1/0 (`Tracking/xSaleOrderTracking.vb:63-71`); type 2 → 3 ถูกใช้โดย DK.CWY (ข้อ 7)
- `CSStatePrint` เลือก location ที่ตัดสต็อก, `CSPayType`, `FTStateOther` — ความหมายแต่ละ index อยู่ใน data (`MSysListData`)

**สำหรับระบบใหม่:** sequence ต่อ tenant ต่อประเภทเอกสาร (SO / DO / INV / CN) ภายใน transaction; เลขใบกำกับภาษีต้องเรียงไม่ขาดช่วงต่อสาขา — ออกเลขตอน post invoice เท่านั้น ไม่ใช่ตอนสร้าง SO

## 3. สูตรคำนวณ

**บรรทัด** (`xSaleOrder.vb:1159-1169`, ซ้ำที่ 1199-1207, 1247-1263, 1981-1990) — ทุกขั้นปัด 2 ตำแหน่ง
```
gross = round2(qty × unit_price)
d1 = round2(gross × dis1% )
d2 = round2((gross − d1) × dis2%)
d3 = round2((gross − d1 − d2) × dis3%)
CNDiscountAmt = d1 + d2 + d3 ;  CNAmount = gross − CNDiscountAmt
```
**ท้ายบิล** (`CalFooter`, `xSaleOrder.vb:1490-1508`)
```
CNSaleOrderAmt = Σ CNAmount
f1 = round2(Amt × footer1%) ; f2 = round2((Amt − f1) × footer2%) ; f3 = round2((Amt − f1 − f2) × footer3%)
Net = Amt − f1 − f2 − f3
VAT = round2(Net × VatPer / 100)          ← VAT exclusive เสมอ
GrandTotal = Net + VAT
```
- แก้จำนวนเงินส่วนลดท้ายบิลได้ ระบบคำนวณ % ย้อนกลับ `% = เงิน × 100 ÷ ฐานของขั้นนั้น` (`xSaleOrder.vb:2018, 2048, 2079`)
- ฟังก์ชันปัด `_DDouble` comment ว่า "ตัดทศนิยมไม่ปัด" แต่โค้ดจริงปัดด้วย format 2 ตำแหน่ง (`xSaleOrder.vb:2092-2129`)
- ตอนส่งไป ACC บังคับ VAT type = 2 (exclusive) และส่ง % VAT ของ SO (`xSaleOrder.vb:2280-2281`); ACC รองรับ include ด้วยสูตร `VAT = round2(ยอด × rate ÷ (100 + rate))` (`ARTInvoiceEntry.vb:1071, 1094`) แต่ flow ขายไม่ใช้
- ราคา: `GetProductPrice` (masters.md §4) เรียกเมื่อเปลี่ยน qty / หน่วย / ติ๊กของแถม (`xSaleOrder.vb:1124, 1954, 2625-2639`); ของแถมราคา 0; เปลี่ยนหน่วยแล้ว qty ถูกล้างเป็น 0 (1952)
- **Due date:** SO ไม่มี credit day; ACC คำนวณ `due = วันที่เอกสาร + credit term (วัน)` (`ARTInvoiceEntry.vb:144-148`) — ค่า credit term มาจากไหนตอน auto-generate อยู่ใน `SP_GENERATE_INVOICE`
- **เช็กสต็อกตอนคีย์ qty** (`CheckOnhand`, `xSaleOrder.vb:614-840`): on-hand = `V_Material_Batch_Balance.FNQuantityBal2` **แถวแรกแถวเดียว** ของ item + คลัง + location (748-762); ต้องการ = `qty × ตัวคูณหน่วย` + qty ของบรรทัดอื่นที่เป็น item เดียวกัน (820-826, 1132-1138); ไม่พอ → qty บรรทัดนั้นถูกตั้งเป็น 0 (1147-1151); ตอน save ตรวจซ้ำทุกบรรทัดและ zero บรรทัดที่ไม่พอ (458-497)
- **เช็กวงเงิน** ตอนอนุมัติขาย เฉพาะลูกค้าที่ `CSStateCheckBudget = '1'`: `คงเหลือ = (CNCreditAmt + CNCreditRcvAmt) − CNCreditPayAmt`; ≤ 0 → บล็อก; น้อยกว่ายอดบิล → ถามยืนยันแล้วผ่านได้ (`xSaleOrder.vb:2676-2695`)

**สำหรับระบบใหม่:** สูตรเดียวใน domain service + golden test (ส่วนลด 3 ขั้น cascade, ปัด `numeric(18,4)` แล้วแสดง 2 ตำแหน่ง, กฎปัด half-up ระบุชัด); รองรับ VAT include/exclude/ยกเว้น เป็น field ของเอกสาร; rate VAT เป็น tenant setting มีวันที่มีผล; due date = invoice date + credit days ของลูกค้า; เช็กสต็อก = available-to-promise รวมทุก lot (on-hand − reserved); วงเงินคำนวณจาก invoice ค้างชำระจริง การ override ต้องมีสิทธิ์ + audit

## 4. สถานะและ flow

| ขั้น | ฟอร์ม / ปุ่ม | เงื่อนไข | ผล |
|---|---|---|---|
| Pre-sale → SO | `xPreSaleOrder` อนุมัติ | แถว `State='1'`, สต็อกพอทุกบรรทัด (คลัง hard-code) | `SP_AppPreSaletoSaleOrder` (`xPreSaleOrder.vb:1001-1044, 1094-1095`) |
| อนุมัติขาย | `xSaleOrder` ocmApproveSale | ยังไม่อนุมัติ, ผ่านเช็กวงเงิน | `CSStateSaleOrder='1'` (`xSaleOrder.vb:2645-2702`) |
| อนุมัติขาย (คลังลูกค้า) | เดียวกัน เมื่อ `FTStateCustWH` | ไม่เช็กวงเงิน | + `CSStatePack='1'`, status `'1'`, `SP_GEN_ISSUEAUTO_CUSTWH` (2704-2737) |
| อนุมัติแพ็ก | `xSaleOrder_Pack` | แถว `State='1'` | status `'1'`, `CSStatePack='1'`, **ตัดสต็อก `SP_GEN_ISSUEAUTO`** ใน transaction เดียว (`xSaleOrder_Pack.vb:1019-1084`) |
| จ่ายของมือ | `DK.INVEN/Transaction/IssueFG.vb` อนุมัติ | — | `CSStatePack='1'` + ใบเบิก `FTStateApp='1'` (2755-2775) |
| อนุมัติตั้งหนี้ | `xSaleOrder` ocmApproveinvoice / `xSaleOrder_Inv` (ทีละหลายใบ) | `CSStatePack='1'`, ยังไม่มีใน `TARTInvoiceEntry_H` | `SP_GENERATE_INVOICE` → `ARTInvoiceEntry.ApproveData` (สร้างลูกหนี้อัตโนมัติ) → sync ยอดกับ SO → `CSStateApproved='1'`, status `'2'` (`xSaleOrder.vb:2203-2391`, `xSaleOrder_Inv.vb:961-1020`) |
| พิมพ์ใบกำกับ | `xSaleOrder_Inv` preview | เฉพาะแถว `State='3'` (`xSaleOrder_Inv.vb:878-882`) | `rptInvoice.rpt` |

- ค่า `State` ในหน้า list มาจาก SP: 1 = อนุมัติขายแล้ว, 2 = แพ็กแล้ว, 3 = ตั้งหนี้แล้ว (อนุมานจาก filter ของปุ่ม; `xSaleOrder_Pack.vb:1022, 1100`, `xSaleOrder_Inv.vb:964`)
- **ล็อก:** แก้ไข/ลบ SO ไม่ได้เมื่อ flag ใด flag หนึ่งใน 3 ตัว = 1 หรือมีเลข SO ใน `TARTInvoiceEntry_H` (`verrifyState`, `verrifyStateAR`; `xSaleOrder.vb:2131-2201`)
- **ยกเลิกอนุมัติขาย:** ไม่ได้ถ้าตั้งหนี้แล้ว หรือแพ็กแล้ว (กรณีคลังปกติ); กรณีคลังลูกค้าจะลบใบเบิก + barcode OUT + status แล้วคืน flag (`xSaleOrder.vb:2751-2832`)
- **ยกเลิกแพ็ก:** แถว `State='2'` → ลบ status, `CSStatePack='0'`, ลบ `TINVENIssue` / `TINVENBarcode_OUT` / `TINVENIssue_Detail` ที่ `FTOrderNo` = เลข SO (`xSaleOrder_Pack.vb:1086-1139`); ฝั่ง `IssueFG` ยกเลิกไม่ได้ถ้า `CSStateApproved='1'` (`IssueFG.vb:2913-2930`)
- **ยกเลิกตั้งหนี้:** ปุ่มใน `xSaleOrder_Inv` ว่าง (1022-1024); ต้องให้บัญชีลบ invoice ใน ACC ซึ่งจะ set `CSStateApproved='0'` กลับ (`ARTInvoiceEntry.vb:2502, 2628-2630`)
- **ลบ SO** = ลบแถวจริงทั้ง header/detail (`xSaleOrder.vb:403-414`); ไม่มีสถานะ Cancelled / void
- **คืน/เปลี่ยนสินค้า:** `xSaleOrder_RET_Pack` อนุมัติใบ `TINVENReturnFG` ที่สถานะ "รอส่งสินค้า" → `FTStateApprove='1'` + `SP_GEN_ISSUERETAUTO` (จ่ายของทดแทน) (684-734); ใบลดหนี้สร้างตอนอนุมัติรับคืน (inventory-transfer.md §5)

**สำหรับระบบใหม่ (state machine):**
- SalesOrder: Draft → Submitted → Approved (N ขั้น config) → PartiallyDelivered → Delivered → Closed (+ Rejected, Cancelled)
- Delivery: Draft → Picked → Shipped (post movement OUT ใน ledger) → Cancelled (movement กลับรายการ)
- Invoice: Draft → Posted (ออกเลข, ล็อก) → Exported → Voided (ต้องมี credit note หรือเหตุผล); ห้ามลบ
- ส่งของบางส่วน / invoice บางส่วนได้ (ระบบเดิมทำไม่ได้ เพราะ 1 SO = 1 invoice)

## 5. รายงานขาย (LM.SaleVat/Tracking)

- ทุกตัวเป็น grid จาก SP แล้ว export Excel: `xSaleOrderCom` (`SP_GET_SumSale`, `_D`), `xSaleOrderComCNbyInv` (`SP_GET_SumSaleByInv`), `xSaleOrderCust`, `xSaleOrderProdSum`, `xSaleOrderSummary` (`Tracking/xSaleOrderCom.vb:66-72`) — ไม่มีสูตรคอมมิชชันในฟอร์ม
- `xSaleOrderTracking` รายงานภาษีขาย: นับเฉพาะ SO ที่ `CSStateApproved='1'` และยอด > 0 **หักยอดใบลดหนี้**ที่อ้าง invoice เดียวกัน (`Tracking/xSaleOrderTracking.vb:74-122`); ใบลดหนี้/เพิ่มหนี้ดึงจาก `V_ARTReciveInv_D` ตาม `FTArdDocType` = CN / DN (124-154)

## 6. Incentive พนักงานขาย (LM.Payroll/Payroll)

1. **แผนต่อคนต่อเดือน** `TCommPlan`: `CNPlanExpPercen = (CNPlanIncentiveAmt + CNPlanSaleExpAvg) ÷ CNPlanActualDoneAvg × 100` (`xComTable.vb:202-210`)
2. **ตารางขั้น** `TCommStand` (สร้างเป็นช่วงเท่า ๆ กันได้; `xComTable.vb:85-117`): `CNCostAvgAmt = CNSaleStartAmt × CNCostPer ÷ 100` (191); `CNIncentiveAmt = CNCostAvgAmt − CNPlanSaleExpAvg` (163); ปรับ: ถ้า −3000 ≤ x < 1000 → 1000, ถ้า x < −3000 → x + 3000 (165-169); `CNCostPerNet = (ค่าที่ปรับ + CNPlanSaleExpAvg) ÷ CNSaleStartAmt × 100` (172)
3. **งบ incentive ของเดือน** (`xIncomeSet.vb:475-550`): ยอดรับ = Σ รายรับที่คีย์, ค่าใช้จ่าย = Σ รายจ่ายที่คีย์; ขอบขั้นคำนวณใหม่ `start = (CNIncentiveNetAmt + ค่าใช้จ่าย) ÷ (CNPerStartSaleNet ÷ 100)` (end ใช้ `CNPerEndSaleNet`); งบ = `CNIncentiveNetAmt` ของขั้นที่ `start ≤ ยอดรับ ≤ end`; ไม่เข้าขั้นใด → ค่าปรับ `= ค่าใช้จ่าย − ยอดรับ × CNPlanExpPercen ÷ 100`
4. **คะแนนยอดขายต่อสินค้า** (`xScoreSale.vb:274-317`): `% = actual ÷ plan × 100`; ถ้า > `CNPercentMaxScore` → คะแนน = `CNScoreMax`; ถ้า > 0.99 → `% × CNScoreMax ÷ CNPercentMaxScore`; ไม่งั้น 0
5. **เงื่อนไขเก็บเงิน** (`xScoreSale.vb:663-749`): `CNScoreRev = ยอดเก็บจริง ÷ แผนเก็บ × 100` → หาช่วงใน `MSalRcvPlan` ได้ `CNScoreRevPer` + % หักค่าที่พัก/เบี้ยเลี้ยง; `CNIncentiveNetAmt = งบ × CNScoreRevPer ÷ 100 − (ค่าที่พักที่จ่าย × % + เบี้ยเลี้ยงที่จ่าย × %)`
6. **จ่ายจริง** (`xScoreSale.vb:319-359`): แยกกลุ่มสินค้า 2 กลุ่ม (id hard-code) แต่ละกลุ่ม `จ่าย = NetAmt × น้ำหนักกลุ่ม% × (actual ÷ plan)`; รวม 2 กลุ่มเก็บลง `TPayroll.CNNetPaid` (574-598); หน้า `xIncentiveCalc` คำนวณแบบง่าย `CNNetPaid = CNIncentiveAmt × CNSaleScorePer ÷ 100` (191-228)
- ยอดขายจริง import จาก Excel ลง `TSaleProduct` (ลบของเดือนนั้นต่อคนแล้ว insert; `Import/xImportSale.vb:100-122`); ยอดเก็บเงินคีย์มือ — **ไม่ผูกกับ `TSaleOrder` หรือใบรับชำระ**

**สำหรับระบบใหม่:** README ตัด incentive ออกจากขอบเขตแล้ว; ถ้าจะทำใน Enterprise ให้เป็น rule engine ต่อ tenant: ฐาน = invoice ที่ post แล้วหัก credit note (option: นับเมื่อได้รับชำระ — ต้องรับสถานะชำระกลับจากโปรแกรมบัญชี), อัตราเป็นขั้น (tier) ต่อพนักงาน/กลุ่มสินค้า; ค่าคงที่ −3000 / 1000 และกลุ่มสินค้า 2 กลุ่มเป็นของ DK ห้าม hard-code

## 7. ความสัมพันธ์กับ flow อื่น

- **SO ของ DK.SO vs LM.SaleVat เป็นคนละระบบ:** DK.SO เขียน `PUR.dbo.TSOTOrder` / `TSOTOrder_Order` (ลูกค้า `TCNMCustomer`, ขับ MRP/ใบสั่งผลิต; mrp-planning.md §3) ส่วน LM.SaleVat เขียน `DB_PAYROLL.dbo.TSaleOrder` (ลูกค้า `OMCustomer`, ขายจากสต็อก FG) — **ไม่พบโค้ด VB ใน DK.SO ที่อ่าน/เขียน `TSaleOrder`** และ LM.SaleVat ไม่อ้าง `TSOTOrder`; สิ่งที่ใช้ร่วมกันคือรหัส item (`CSProductCode` = `TPORawmaterial.FTRawMatCode`; `xSaleOrder.vb:706-708`) และ resolver ราคาที่ถูก copy
- **ตัดสต็อก:** ผ่าน SP สร้างใบเบิกอัตโนมัติ (inventory-issue.md) ผูกด้วย `TINVENIssue_Detail.FTOrderNo`; คลัง/location ปกติ hard-code ตาม id บริษัทและ `CSStatePrint` (`xSaleOrder.vb:719-733`)
- **รับคืน:** `SP_GENSaleOrderForReturn` ดึง SO ไปอ้างในใบรับคืน (`DK.INVEN/Transaction/ReturnORRefund.vb:1896`)
- **บัญชี:** invoice + ลูกหนี้ + GL สร้างใน DK_ACCOUNT ทันทีตอนอนุมัติ ไม่มี export file
- **DK.CWY (ตัด):** รวม invoice ประเภท 2 เป็นเอกสารประเภท 3 ใหม่ ผูกด้วย `CSSaleOrderNoRef`, `TSaleOrder_Track` (`DK.CWY/Transection/xAaceptInvoice.vb:79-80, 369, 513, 976`)
- **AX:** `xSaleAmtTrack`, `xGenFGtoBom`, `xTrackCals` อ่านตาราง Dynamics AX (`SALESTABLE`, `CUSTINVOICETRANS`) เพื่อออกใบกำกับรายบุคคลและ explode BOM จากยอดขาย (`LM.SaleVat/xSaleAmtTrack.vb:166, 229`) — นอกขอบเขต; `xGenDVat`, `xGenOrderToBom`, `Packing/xPackingList` เป็นฟอร์มว่าง

**สำหรับระบบใหม่:** SO ชุดเดียว มี field `fulfillment = from_stock | make_to_order` — แบบหลังส่งเข้า MRP; invoice export เป็นไฟล์/ API ตาม format โปรแกรมบัญชีของลูกค้า (เลือก mapping ต่อ tenant) พร้อม flag `exported_at` และ re-export ได้

## 8. SP / function / view ที่เกี่ยว (ไม่มี definition ใน repo — ต้องขอ)

- SO: `sp_getSaleorderheading`, `sp_GetSaleOrderDetail`, `sp_getSaleorderheading_Pre`, `sp_GetSaleOrderDetail_Pre`, `SP_GEN_DOCUMENTNO`, `SP_GEN_PACKING` (`xSaleOrder.vb:282, 332, 1575, 1637`, `xPreSaleOrderEdit.vb:1473, 1541`)
- pre-sale: `sp_getpresaletosaleorder`, `sp_getpresaletosaleorder_detail`, `SP_AppPreSaletoSaleOrder`, view `V_PreSaleorderReport` (`xPreSaleOrder.vb:448-453, 917, 1030`)
- packing/invoice list: `sp_getSaleorderpackinglist`, `_WH`, `_stock`, `_ReturnFG` (`xSaleOrder_Pack.vb:436-443`, `xSaleOrder_Inv.vb:423`, `xSaleOrder_RET_Pack.vb:311`)
- ตัดสต็อก: `SP_GEN_ISSUEAUTO`, `SP_GEN_ISSUEAUTO_CUSTWH`, `SP_GEN_ISSUERETAUTO`, `SP_GEN_ISSUEAUTO_BRPC`, `SP_GENSaleOrderForReturn`
- บัญชี: `DK_ACCOUNT.dbo.SP_GENERATE_INVOICE`, `ACC.dbo.SP_RERTOCREDITNOTE_INVOICE`
- รายงาน: `SP_GET_SumSale`, `_D`, `_D_Cust`, `_Summary`, `_sum`, `_sumsale`, `_sumsaledaily`, `_sum_sup`, `_sumsaledaily_sup`, `SP_GET_SumSaleByInv`, `SP_GetListProductSale`, `SP_GetListProductSaleSum` (+ ชุด `_TT`), `SP_GETCommPlan`
- view: `V_Product`, `V_Product_Price`, `V_PricePromotion`, `V_Unit`, `V_Browse_323`, `V_CustomerDelivery`, `V_Material_Batch_Balance`, `V_sale`, `V_ARTReciveInv_D`, `V_TARTCreditNote_D`, `V_CreditBal`, `V_CreaditUse`, `v_SumSale_sum_report`

## Defects ของระบบเดิม (ห้ามยกมา)

- **SQL injection:** header SO ต่อค่าจาก textbox ตรง ๆ ไม่ escape (ที่อยู่ส่งของ, หมายเหตุ, เลข PO ลูกค้า, เลข SO; `xSaleOrder.vb:249-279, 355, 406-408`)
- pattern "update ไม่สำเร็จ → insert" ทำให้ SQL error กลายเป็นเอกสารใหม่ (`xSaleOrder.vb:280`); header, detail, `SP_GEN_PACKING` ไม่อยู่ใน transaction เดียวกัน
- 1 เอกสารเป็นทั้ง SO / ใบส่งของ / invoice → ส่งบางส่วนไม่ได้, แก้ SO หลังตั้งหนี้ต้องลบ invoice; ยอดใน ACC ถูก update ทับให้เท่ากับ SO ภายหลัง (`xSaleOrder.vb:512-521, 2288-2297`)
- ใน `xSaleOrder_Inv` คำสั่ง sync ยอดใช้เลขเอกสารจาก control ของฟอร์ม ไม่ใช่ของแถวที่กำลังวน → sync ผิดใบ/ไม่ sync (`xSaleOrder_Inv.vb:985, 990`)
- อนุมัติแพ็ก: คำสั่งลบ status รันนอก transaction ขณะที่ insert อยู่ใน transaction (`xSaleOrder_Pack.vb:1043-1051`); ยกเลิกแพ็ก/ยกเลิกขายลบใบเบิกและ barcode OUT ทิ้งโดยไม่มี transaction และไม่มี audit (`xSaleOrder_Pack.vb:1103-1124`, `xSaleOrder.vb:2787-2809`)
- `xForm.vb` กด preview ใบแพ็กแล้ว set `CSStatePack='1'` **โดยไม่ตัดสต็อก** (887-907)
- ปุ่มอนุมัติขาย/ตั้งหนี้ save เอกสารก่อนโดยไม่ตรวจ lock และไม่สนผล save (`xSaleOrder.vb:2211-2215, 2653-2657`); ตรวจ `CSStatePack` ซ้ำ 2 ครั้งแทนที่จะตรวจ `CSStateSaleOrder` (2228-2242)
- `verrifyState` คืน True เมื่อเกิด exception = ปลดล็อกเมื่อ query ล้ม (`xSaleOrder.vb:2180-2182`)
- ยกเลิกอนุมัติใน `IssueFG` update ตาราง `TINVENReceive` ด้วยเงื่อนไข `FTIssueNo` ผ่าน connection ผิด DB (`IssueFG.vb:2917-2926`)
- เช็กสต็อกอ่านแค่ batch แรก, id คลัง/location/หน่วย/รหัสโรงงาน hard-code, ตัวแปรแปลงหน่วยเป็น Integer (`xSaleOrder.vb:254, 618, 719-733, 759, 787-794`; `xPreSaleOrder.vb:1094-1095`)
- เช็กวงเงินข้ามได้ด้วยการกดยืนยัน และวงเงินเป็นค่าคีย์มือ (masters.md)
- ลบ SO เป็น hard delete; ไม่มีสถานะยกเลิก; `CSApprovedBy` ถูกเขียนทับทั้งตอนอนุมัติขาย, ยกเลิก, และตั้งหนี้ (`xSaleOrder.vb:2354-2357, 2697-2700, 2816-2819`)
- incentive: key เดือน/ปีของ `TPayroll` ตอนลบใช้ชื่อคอลัมน์ไม่ตรงกับตอนบันทึก (`xIncentiveCalc.vb:120-121` vs 165-166); คอลัมน์ตอน insert `TSSaleScore` เรียงไม่ตรงกับค่า (`xScoreSale.vb:483-496`); ค่าคงที่และ id กลุ่มสินค้า hard-code; `catch` ว่างทุก method

## Gap ที่ต้องดู SP definition / data

1. `SP_GEN_ISSUEAUTO` / `_CUSTWH` / `_BRPC`: เลือก lot แบบ FIFO หรือไม่, แปลงหน่วยอย่างไร, ทำอย่างไรเมื่อสต็อกไม่พอ, parameter `CSStatePrint` เลือก location ไหน
2. `SP_GENERATE_INVOICE`: mapping SO → `TARTInvoiceEntry_H/_D`, credit term / due date มาจากไหน, เลขใบกำกับ (`FTSihTaxInvNo`) ออกอย่างไร, ส่วนลดท้ายบิลกระจายลงบรรทัดหรือไม่
3. `SP_GEN_PACKING` สร้างอะไรตอน insert SO (มีตาราง packing แยกหรือไม่)
4. `TPreSaleOrder` ถูกสร้างจากที่ไหน (mobile app / ระบบ pre-sale ภายนอก / import) และ `SP_AppPreSaletoSaleOrder` copy + ออกเลขอย่างไร, ค่า `State` ทั้งหมดของ pre-sale
5. ค่า `State` / `Status` ที่ `sp_getSaleorderpackinglist*` คืน (ยืนยัน 1/2/3) และ filter `CNSaleOrderStatus`
6. ค่า list ใน `MSysListData`: `CNSaleOrderType` (index 0–4), `CSStatePrint`, `CSPayType`, `FTStateOther`; ความหมายของ `FTStateNotConvert`
7. รูปแบบเลขจาก `SP_GEN_DOCUMENTNO` ต่อประเภท SO (prefix `IN…`, reset รายเดือน/ปี) และกรณี BRPC
8. `V_Material_Batch_Balance.FNQuantityBal2` หัก reserve / QC / ของรออนุมัติหรือไม่
9. `ARTReceivableEntryAuto.GenerateARTInvoiceEntryAuto` และ GL ที่เกิด — ใช้กำหนด field ที่ต้องมีในไฟล์ export ไปโปรแกรมบัญชี
10. ใบลดหนี้: `SP_RERTOCREDITNOTE_INVOICE` คิดยอดจากราคาใน SO หรือราคา ณ วันคืน, VAT, คืนบางส่วน
11. `CNCreditRcvAmt` / `CNCreditPayAmt` ของลูกค้าถูกอัปเดตโดย trigger/SP ตัวใด (ตอนตั้งหนี้/รับชำระ)
12. รายงาน `SP_GET_SumSale*`: มีคอลัมน์คอมมิชชันหรืออัตราหรือไม่ (ชื่อฟอร์มเป็น "Com" แต่โค้ดไม่มีสูตร)
13. incentive: data ของ `MSalRcvPlan`, `MProduct.CNScoreMax / CNPercentMaxScore`, น้ำหนักกลุ่มใน `MMainProductCountry`, `SP_GETCommPlan`; ธุรกิจยังใช้ module นี้อยู่หรือไม่
14. trigger บน `TSaleOrder`, `TSaleOrder_Status`, `TINVENIssue` (เช่น sync ไป DK.SO หรือ update วงเงิน)
