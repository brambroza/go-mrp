# Logic เดิม: BOM / MRP / ใบสั่งผลิต / Scheduling (DK.MRP + DK.SO)

ถอดจาก repo DK-ERP-SYSTEM 2026-09-29 · อ้างอิง path ในระบบเดิม · DB: DK_PROD, DK_MASTER, DK_INVENTORY, PUR

**สรุปสำคัญ:** MRP ที่รันตอนอนุมัติ SO **ไม่ทำ netting** (on-hand/on-PO ถูก hard-code เป็น 0) netting มีเฉพาะในหน้าจอ MRPDemand* ซึ่งเป็นรายงานอ่านอย่างเดียว ไม่สร้าง PR
Scheduling เป็น greedy single-pass finite-capacity loader และ **วางแผนเฉพาะ operation สุดท้ายของ route**
→ ระบบใหม่ต้องทำ netting จริง, มี lead time, และสร้าง PR/PD จากผล MRP ได้

## 1. BOM (DK.MRP)

**ตาราง**
- `TPDMBom(FNMSysRawMatId)` header ต่อ item
- `TPDMBomFormula(FNMSysBomId, FNBomSeq, FNMSysRawMatId, FTBomName, FNQuantity = batch size, FNMSysUnitId, FTStateActive, FTStateSendApp, FTStateApp)`
- `TPDMBomFormulaLineItem(FNMSysBomId, FNBomSeq, FNItemSeq, FNMSysRawMatId, FNUsedQuantity, FNUsedPlusPer = loss %, FNMSysUnitId, FNMSysItemBomRefId)` — บันทึกที่ `DK.MRP/BomListingAddFormula.vb:156-200`, `BomListingAddItem.vb:124-175`

**กฎ**
- item หนึ่งมีหลายสูตรได้ แต่ active ได้สูตรเดียว — บันทึกสูตร active จะ set `FTStateActive='0'` ให้สูตรอื่น (`BomListingAddFormula.vb:180-184`); copy สูตร = clone line (190-196)
- ปริมาณเป็นค่าสัมบูรณ์ต่อ batch ไม่ใช่ %: `component_qty = FNUsedQuantity × parent_qty / FNQuantity(batch)` แล้วบวก `FNUsedPlusPer %` (`CalMRPSO.vb:300-310`); batch = 0 ถือเป็น 1
- multi-level: component ถือเป็น semi/bulk ถ้ามีสูตร active ของตัวเอง (`OUTER APPLY TOP 1 TPDMBomFormula`, `CalMRPSO.vb:283`) แล้ว recurse (356-358)
- RM BOM กับ PK BOM ใช้ตารางเดียวกัน ต่างแค่ UI กรองด้วย prefix กลุ่มวัตถุดิบ `PK` (`bomrm/BomListingRMAdd.vb:198`, `bompk/BomListingPKAdd.vb:200,295`); item ต้อง `TPORawmatState=1`
- `RouteMaster/Rawmatmapbom.vb` = where-used report

**สำหรับระบบใหม่:** BOM version มี effective date + สถานะ Draft/Approved/Active; line มี qty ต่อ batch + loss %; type ของ line (RM/PK/semi) ควรมาจาก item type ไม่ใช่ prefix รหัส

## 2. MRP

**A. `CalMRPSO.CalculateSOMRP(sono, itemId, qty)` (`DK.MRP/CalMRPSO.vb:4-145`)** — เรียกจาก SO approve (`DK.SO/SaleOrderApproved.vb:569`, `Production/SaleOrderStatus.vb:527`), สร้าง PD (`Production/ProductionOrderList.vb:1489`), แก้/split PD (`clsCalsProdPland.vb:1640,1737,1835`)
1. ล้าง temp ต่อ user `TPDMTMPMRPDemand`, `_Mat`, `_MatSummary`
2. explode BOM recursive
3. `FNOnhandQuantity = 0, FNOnPOQuantity = 0` (314-316) → `FNTotalQuantity` = gross requirement รวม loss
4. เขียน `TPDTSOMRP` (ต่อ SO + FG ทุก level; `FNMSysRawMatRefId` = parent semi, 0 = ลูกโดยตรงของ FG; `FTStateSemi`, `FNMSysSemiBomId`)
5. เขียน `TPDTSOMRPSummary` (เฉพาะ non-semi, sum; 86-123)
ไม่มี lead time, safety stock, time bucket, ไม่สร้าง PR

**B. `MRPDemand.vb`** — what-if จาก FG qty ที่กรอกเอง บันทึกเป็น run ชื่อ (`TPDMMRPDemandName`, `TPDMMRPDemand`, `_Mat`, `_MatSummary`; 659-708)
- netting summary ใช้ `V_Material_Balance_ForMRP` (460-463): `Net = max(0, Total − (OnHand + OnPO))`
- grid ใช้ `V_Material_Batch_Balance_ForMRP` (156): `Net = max(0, (Reserve + Required) − (Bal2 + PO))`
- semi on-hand ถูกหักระหว่าง explode (528-560) แต่ query on-hand **ไม่มี WHERE** (531) = bug

**C. `MRPDemandbyPO`, `…RM`, `…PK`** — demand จาก `exec Get_PurchaseFGDemand` (customer PO เปิดอยู่; `MRPDemandbyPO.vb:819`), แยก RM/PK ด้วย `LEFT(FTMatGrpCode,2)` (259-265), `FNQuantity` ถูกตัดเป็น Integer (466)
`Net = max(0, (Reserve + Required) − (OnHand + OpenPO + QC))` (`MRPDemandbyPORM.vb:~210`) ใช้ `FN_Get_Onhand_RMPK`, `FN_Get_PO`, `FN_Get_Reserve`, `FN_Get_QCQty`

**D. `MRPDemandbyPRD` / `…Bulk`** — ไฟล์เหมือนกัน; netting แบบ C แต่ list เฉพาะ semi/bulk ที่ `FTStateNotCreateJob='0'` (247) = ต้องเปิด job ผลิต

**PR trigger:** ไม่มีในโค้ด `DK.PO/PurchaseRequest.vb` ไม่ผูก MRP → ทำ PR มือจากรายงาน

**สำหรับระบบใหม่:** MRP run = input (SO/PO/forecast + on-hand + open PO + open PD + reserve + QC hold + safety stock) → gross → net ต่อ item ต่อ bucket (วัน/สัปดาห์) ตาม lead time → ข้อเสนอ PR (ซื้อ) / PD (ผลิต) ที่กดสร้างได้

## 3. SO → ใบสั่งผลิต (PD)

- customer PO → SO: `SP_Create_SaleOrderAuto` (PUR) สร้าง `TSOTOrder` / `TSOTOrder_Order` แล้วรัน MRP; ใน `SaleOrderApproved.vb:572-573` การสร้าง PD ถูก comment ไว้, ใน `SaleOrderStatus.vb:530-531` ยัง active (`CreateOrderProd` + `CalsProdPland`)
- **`ProductionOrderList.vb` (ทางหลัก):** แสดง SO line ที่ `FNQtyBanlance = SOqty − Σ FNSOQty ของ main PD`, flag `FTStateBom` / `FTStateRoute` (379-412); double-click → กรอก qty (split lot เอง) →
  1. `CheckUnitBom` คูณ ×1000 hard-code เมื่อ unit 1707180007 ≠ BOM unit 1707180006 (1541-1544) — **ระบบใหม่ใช้ตาราง UOM conversion**
  2. `CalculateSOMRP`
  3. `CreateOrderProd` (1083-1189)
  4. `ReCalsProdPlandManual` (1496)
- **`CreateOrderProd`:** `TPDMProdcutOrder` แถว = **material line** ของ PD: `FTProductOrderNo, FTProdRefNo (= main PD), FNMSysMainRawMatId (สิ่งที่ผลิต), FNMSysRawMatId (component), FNQuantity (= MRP gross), FNQuantityUsed, FNSOQty, FNSeq (SO line)`
  - main PD (**fill/FG**) ถือ component level 1 (1115-1135)
  - semi แต่ละตัว → **PD ใหม่ต่อ bulk/mix** `FTProdRefNo` = main PD เรียง level มาก→น้อย (1146-1182)
  - เลขเอกสาร `SP_GEN_DOCUMENTNO` (`clsCalsProdPland.vb:6`)
  - SO มือ (`FTPurchaseNo=''`) ข้าม item `FTStateNotCreateJob='1'` (1098-1103)
- `GenerateProdOrder.vb:160-194` (ทางเก่า): แบ่ง SO qty เท่าๆ กันเป็น N PD ชื่อ `SO-01…` (`ceil(qty/N)` ตัวสุดท้ายรับเศษ) หลังลบ PD เดิมทั้งหมด (89)
- ผลิตเก็บสต็อกไม่มี SO: `SP_Create_SaleOrder_With_PD` (`PlanSchedulingManual.vb:1957`) — call ที่ 687 ส่ง argument สลับลำดับกับ signature ที่ 1954 = bug

**สำหรับระบบใหม่:** work order มี header (item, qty, due, source SO line/MRP/stock) + materials (จาก BOM ณ วันสร้าง) + operations (จาก routing); semi = child work order ผูก parent; lot split เป็น action ที่ทำได้ทั้งตอนสร้างและตอนวางแผน

## 4. Route / เครื่อง / ปฏิทิน / กำลังคน

- **Routing** (`RouteMaster/RouteMasterSet.vb:1287-1420`): `TPDMRoute(FNMSysRawMatId, FTRouteCode, AppState)`; `TPDMRouteOperation(FNOperNo, FNOperNextNo, FNTime, FNUnitTime, FNProcessQty, FNTimeSetUp, FNMSysMachineGrpId, FNResourceSetUp, FNResourceQty)`; `TPDMRouteMatchine(FNMSysOperationId, FNMSysMatchineId, FNSeq, FNPriorityNo, FNQuantity, FNMSysUnitId)`; `TPDMRouteResource(FNMSysUnitSectId)`; ชนิด operation จาก `DK_MASTER.TPRODMOperation.FTStateMix` ('1' mix/bulk, '0' fill/pack)
- **ปฏิทิน** (`CalendarMaster.vb:486-560`): `TCNMCalendar` / `_Detail(FDDate, FNSeq, FTStartTime, FTEndTime, FNTotalMinute)` สร้าง จ–ศ ยกเว้น `THRMHoliday` เป็น 2 กะ 08:00–12:00, 13:00–17:00 กะละ 240 นาที; เครื่องชี้ปฏิทินผ่าน `TINVENMMachine.FNMSysCalendarId`
- `MachinePriorityMasterSet.vb:159-171` → `TINVENMMachineGrpPriority` (scheduler จริง**ไม่ใช้**)
- `Master/MachineProdSet.vb:149-187` → `TPDMMixSet_H` (ต่อกลุ่มสินค้า: `FNTimeUseMin = ชม×60`, `FNWipTImeMin = วัน×480`, `FNHResourceTotal`) + `TPDMMixSet_D` (ชนิดเครื่องที่ใช้ได้)
- `Manpawerplan.vb` เป็น stub; มุมมองกำลังคนมาจาก `exec getplanmanpower` (`PlanScheduling.vb:491`)

**สำหรับระบบใหม่:** routing = operations เรียงลำดับ + machine group + setup/run time + min/max batch; ปฏิทินเป็น shift pattern ต่อ tenant (จำนวนกะ/เวลา config ได้) + วันหยุด; machine priority ใช้จริง

## 5. Scheduling (`DK.SO/clsCalsProdPland.vb`)

**`ReCalsProdPland(date, …)`** (198-640): ถ้าไม่ระบุ PD → ลบแผนอนาคตที่ยังไม่อนุมัติ (`Appointments where FTStateApp='0' and StartDate>date`; 217) → loop main PD ที่ `FTProdStatus=0` → `fn_GetOrderProdForPland(PD)` ได้ sub-PD (ขั้น mix/fill)

**`clsShedul`** (641-1080) greedy finite-capacity:
- operation: กรอง mix/fill (665) แล้ว**เอาเฉพาะตัวที่ `FNOperNextNo=0`** (670)
- fill รอ mix: fill เริ่มไม่ได้ก่อน mix end จาก `fn_getWipmix` (675-679)
- slot candidate (704-748): route machines × กะในปฏิทิน หลังวันเริ่ม เรียงตามเวลาว่างเร็วสุด (`max(EndDate ล่าสุดของวัน, กะเริ่ม)`) แล้ว `rx.FNSeq`; `FNPriorityNo` ไม่ใช้
- fill qty ต่อ slot (795-830): `qty = FNProcessQty × (shiftMin − setup) / FNTime`; ถ้าเศษพอดี end = setup + runtime ไม่งั้นใช้ถึงกะจบ
- mix qty ต่อ slot (835-847): ข้ามเครื่องถ้า qty < `FNMinProcQty`; batch cap ที่ `FNMaxProcQty` (lot split ตาม batch); duration = `FNTimeUseMin`
- delivery date (936-949): WIP > 0 → end + WIP/480 วัน เวลา 09:00; ไม่งั้น mix: 14:00 วันเดียวกันถ้า end อยู่ 09:00–12:00 ไม่งั้นวันถัดไป 09:00
- output `addPlan` (1469-1499) → ตาราง DevExpress `Appointments` (`ResourceId` = เครื่อง, `ProdNo, OperNext, FNQuantity, FTPlanNo, DeliveryDate, FNMSysLineId`) + `TPDTPlanRef(FNPlanId, FTPlanNo, FTProductOrderNo, FNQuantity)` (1503-1530)
- วันเริ่มเร็วสุด: caller ส่ง today+2, +1, query ใช้ `date >` → ประมาณ today+4
- `ReCalsProdPlandManual` (176) สร้าง placeholder 1 ตัว (machine 0) ไม่วางแผน

**UI:** `Production/PlanScheduling.vb` = DevExpress Scheduler Gantt bind `Appointments`, `Resources`, `TaskDependencies`; drag/resize เขียนตรงลงตาราง (36-80); status busy ถูกล็อก (441-446); ลบ appointment ลบ `TPDTPlanRef` (46-51); dependency เก็บแต่ engine ไม่บังคับ
`PlanSchedulingManual.vb`: วางแผน mix/fill มือ → `Appointments` (`FNPlanType` 0 mix / 1 fill, `FNStateBulk, FNPlanStatus, FDRcvDate/FTRcvTime, FTPlanMainNo`; 711-731); split ลด qty ต้นทาง + rebuild PD line (`UpdateSplitOrderProd`/`UpdateOrderProd`; 735-751); pegging fill↔mix ใน `TPDMPlanMap` (1339)
`Track/PlanIssRaw*.vb`, `PlanRcvRaw*.vb`: แสดงผล SP แล้วแก้ได้แค่ `Appointments.FDRcvDate/FTRcvTime` (`PlanIssRaw.vb:395-424`); `PlanRcvRawToFill` ใช้ `SP_GET_PLANRCV_Bulk`

**สำหรับระบบใหม่:** วางแผนทุก operation ตามลำดับ (ไม่ใช่แค่ตัวสุดท้าย), forward/backward จาก due date, เคารพ dependency mix→fill, machine priority, กำลังคนเป็น constraint (option), Gantt drag-drop บน web แล้ว recalc downstream, lock แผนที่อนุมัติ/เริ่มแล้ว, แผนเบิก/รับวัตถุดิบ derive จาก slot start − offset (config)

## 6. สถานะและ flow

- **SO** (`TSOTOrder`): `FTStateSendApp` → `FTStateSuperVisorApp` (1 อนุมัติ, 2 ปฏิเสธ) → `FTStateManagerApp` (1/2); line: `FTStateClose`, re-open `FTReOpenClose='1'` (`SaleOrderApproved.vb:829-841`)
- **PD** (`TPDMProdcutOrder`): `FTProdStatus` (0 = รอวางแผน; ไม่ถูก set ใน VB → น่าจะ set ใน DB), `FTStateActive`; PD ล็อกเมื่อมี `TINVENIssue.FTJobOrderNo = PD` (`CheckPD` 1547-1557); วางแผนไม่ได้ถ้า `FTProcess='1'` (`ProductionOrderList.vb:1623`)
- **Plan** (`Appointments`, `PlanAcceptPopUp.vb:523-688`): Draft `Status=0, FTStateSendApp=0` → ส่งอนุมัติ `FTStateSendApp=1, Status=1` → อนุมัติ `FTStateApp=1, Status=2` → ถอน `FTStateApp=0, Status=1`; `ApprovedPlanTran` (1559) set `Status='1'` ไม่ตรงกัน; label จาก `CboList "FTPDStatus"`; recalc ลบเฉพาะแผนที่ยังไม่อนุมัติ
- **BOM:** `FTStateActive` (active 1 ต่อ item), `FTStateSendApp/FTStateApp` · **Route:** `AppState` · **Item flags:** `FNRawmatState`, `FTStateNotCreateJob`, `FTStateActive`

**สำหรับระบบใหม่ (state machine):**
- SO: Draft → Submitted → Approved (N ขั้น config) → InProduction → Delivered → Closed (+ Rejected, Cancelled, Reopened)
- WorkOrder: Planned → Released → InProgress → Completed → Closed (+ OnHold, Cancelled); ล็อกเมื่อมีการเบิก
- ScheduleSlot: Draft → Submitted → Approved → Started → Finished; recalc แตะเฉพาะ Draft/Submitted
- BomVersion: Draft → Approved → Active/Superseded

## 7. SP / function / view ที่เกี่ยว (ไม่มี definition ใน repo — ต้องขอ)

- SP สร้าง SO/PD: `SP_Create_SaleOrderAuto`, `SP_Create_SaleOrder_With_PD`, `SP_GEN_DOCUMENTNO`
- MRP: `Get_PurchaseFGDemand`
- แผน: `SP_GET_PLAN_ACCEPT`, `SP_GET_PLAN_MIX_Manual`, `SP_GET_PLAN_Map_Manual`, `SP_GET_PLANSEMI_Map_Manual`, `SP_GET_PLAN_MIX_Manual_Map`, `SP_GET_PLAN_MIX`, `SP_GET_PLAN_MIX_Report`
- เบิก/รับตามแผน: `SP_GET_PLANIss_RM`, `SP_GET_PLANIss_RM_PRD`, `SP_GET_PLANIss_RM_PRD_Sum`, `SP_GET_PLANRCV_RM`, `SP_GET_PLANRCV_Bulk`
- master/กำลังคน: `SP_GETMachineSet`, `getplanmanpower`, `SP_GET_DYNAMIC_OBJECT_CONTROL`
- function: `fn_GetOrderProdForPland`, `fn_getWipmix`, `fn_GetOrderMatchFill`, `getConsumtionForPland`, `fn_GetProductToFG`, `fn_GetMachinePriority`, `fn_GetMachineMaster`, `fn_get_ProdmasterRoute`, `FN_Get_Onhand_RMPK`, `FN_Get_PO`, `FN_Get_Reserve`, `FN_Get_QCQty`
- view: `V_Material_Balance_ForMRP`, `V_Material_Batch_Balance_ForMRP`, `V_PlanningMixTrack`, `v_rpt_plan_rcvrm_mix`, `v_rpt_plan_rcvrm_fill`

## 8. ที่เกี่ยวข้องในโมดูลอื่น

- reserve: `TINVENIssue_Detail.FTStateReserve` (เช่น `DK.INVEN/Transaction/Issue_bulk.vb:1342`) รวมด้วย `FN_Get_Reserve`
- job order: `TINVENJobOrder` (`wJobOrder.vb:443`)
- PO delivery tracking: `DK.PO/Tracking/Purchaseplandelivery.vb` เทียบ PO line กับรับ/คืน ไม่ได้มาจาก MRP
- actual: `DK.SO/Actual/RouteActual*.vb` (ยังไม่ถอด — ดู README)

## Defects ของระบบเดิม (ห้ามยกมา)

- MRP ตอนอนุมัติ SO ไม่ netting; MRP report ไม่สร้าง PR
- scheduler วางแค่ operation สุดท้าย, ไม่ใช้ machine priority, dependency ไม่บังคับ
- `GetbomLvUpd` filter ขัดกัน (`FNMSysMainRawMatId=X and =0`; `clsCalsProdPland.vb:1598-1599`) → แก้ PD แล้ว level ลึกหาย
- insert `TPDMBom` ใหม่ทุกครั้งที่สร้างสูตร (`BomListingAddFormula.vb:156`) → explode join ซ้ำถ้าไม่มี unique key
- UOM ×1000 hard-code, qty ตัดเป็น Integer, argument สลับใน `SP_Create_SaleOrder_With_PD`
- empty catch, SQL concat

## ข้อที่ถูกโต้แย้งภายหลัง (2026-09-29 จากการถอด `sales-order.md`) — ยังไม่ได้ตรวจซ้ำ

การถอด flow SO พบว่า 3 ข้อในไฟล์นี้อาจไม่ตรงกับโค้ด ให้ถือ `sales-order.md` เป็นหลักจนกว่าจะตรวจซ้ำ:

- §6: line re-open ที่ `SaleOrderApproved.vb:829-841` update ตาราง **PO line** (`TPURTPurchase_OrderNo`) ไม่ใช่ SO และปุ่มถูกซ่อน
- §3: การสร้าง PD ใน `SaleOrderStatus.vb:530-531` — ปุ่มที่เรียกถูกซ่อนใน designer (`SaleOrderStatus.designer.vb:370`) จึงไม่ active ในทางปฏิบัติ
- §3: เงื่อนไขข้าม item `FTStateNotCreateJob` ที่ `ProductionOrderList.vb:1088-1103` ทำงานกลับด้าน (ข้ามเมื่อ SO **มี** PO) — ต้องยืนยันกับผู้ใช้

ไม่กระทบโค้ดระบบใหม่: ระบบใหม่ไม่ได้ยกพฤติกรรมทั้ง 3 ข้อนี้มา

## Gap ที่ต้องดู SP definition / data

1. `SP_Create_SaleOrderAuto` ทำอะไร: ราคา, วันส่ง, split line
2. `FTProdStatus`, `FTProcess` ถูก set ที่ไหน ค่าเต็มมีอะไรบ้าง
3. logic `fn_GetOrderProdForPland`: ลำดับ, `FNStateMix`, รวม qty ต่อ PD อย่างไร
4. `fn_getWipmix`: mix end ตัวไหน gate fill
5. `FN_Get_*` และ `V_Material_*ForMRP`: นับคลังไหน, reserve = จอง PD หรือ SO
6. `Get_PurchaseFGDemand`: PO ไหนนับว่าเปิด, กรองวันที่
7. ค่า CboList `FTPDStatus`, `FNPlanStatus`, `FNStateBulk`, ความหมาย `Appointments.Status/Label`
8. unit id 1707180006/7 และกฎ `TCNMUnitConvert`
9. `TPDMBom` มี PK บน FNMSysRawMatId ไหม, `FNMSysItemBomRefId` คืออะไร
10. `SP_GET_PLANIss_RM` / `SP_GET_PLANRCV_RM`: offset เวลาเบิก/รับกับ plan start
11. `getplanmanpower` และ `FNHResourceTotal`/`FNResourceQty` ใช้เป็น constraint ไหม
12. trigger บน `Appointments` / `TPDMProdcutOrder` มีไหม (roll-up สถานะ, reserve, PR)
