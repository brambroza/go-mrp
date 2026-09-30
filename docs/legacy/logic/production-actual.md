# Logic เดิม: Production actual — mix / fill, ของเสีย, man-hour, yield (DK.SO Actual/*, Track/Rpt*)

ถอดจาก repo DK-ERP-SYSTEM 2026-09-29 · อ้างอิง path ในระบบเดิม · DB: DK_PROD, DK_MASTER, DK_INVENTORY, PUR, SYSTEM

**สรุปสำคัญ:** actual เป็น**แบบฟอร์มกรอกมือ**ผูกกับ "เลขแผน" (`Appointments.FTPlanNo`) ไม่ใช่เลข PD — คอลัมน์ชื่อ `FTProductOrderNo` ในตาราง actual เก็บเลขแผน
ใน VB มีสูตรคำนวณแค่ 3 อย่าง: เวลาสุทธิ (นาที), ยอดผลิตรวม, และ % ผลิตได้เทียบแผน — **Bulk loss, PK loss, % loss, ค่าแรง, downtime เป็นตัวเลขที่ผู้ใช้กรอกเอง** ไม่มีการคำนวณจาก BOM หรือยอดเบิก
yield / man-hour / สรุปปัญหา ในรายงานทั้งหมดมาจาก stored procedure ที่ไม่มีใน repo · ไม่มีสถานะ/อนุมัติของ actual เอง · ไม่มี reweigh ใน DK.SO
→ ระบบใหม่ต้องคำนวณ loss/yield จาก ledger (ยอดเบิก − ยอดผลิต − ยอดคืน) และเก็บ actual ต่อ operation แบบ config ได้

## 1. ตาราง

- `TPDTProdActual` (mix/bulk) key = `FTProductOrderNo (= เลขแผน) + FNMSysRawMatId + FTBatchNo + FNSeq` — เขียนที่ `DK.SO/Actual/RouteActual.vb:560-649`
  - ปริมาณ/เวลา: `FNOrderQuantity` (ยอดแผน), `FNProcessQty` (ยอดผลิตได้), `FNBlendQty`, `FTBlendCode`, `FDStartDate, FTSTime, FDEndDate, FTETime, FNNetTime` (นาที), `FNSetUpTime`, `FNResourceUseQty` (จำนวนคน), `FNMSysMachineId`, `CSEmpCode`
  - ค่าคงที่: `FNMSysOperationId = 0` (= mix) และ `FNMSysUnitId = 1707180007` hard-code (`RouteActual.vb:565,613,615`)
  - ปัญหา (checkbox 0/1): `FTStateLateDeliveryTime`, `FTStateNotCompleteQty`, `FTStateNotQCPass` แต่ละตัวมี `…In` / `…Out` (สาเหตุภายใน/ภายนอก) + `FTNoteProblem`, `FTNoteQCPass` (578-589)
  - เข้าคลัง/QC: `FTStateAppToFG, FTStateAppToFGBy, FDStateAppToFGDate, FTStateAppToFGTime` (1287-1296), `FTStateQC, FTStateQCPass, FTStateQCNotPass, FTDateExpire` (`DK.INVEN/Transaction/StockQCBrowse.vb:1261`) — ดู production-to-wh.md
  - อ่านแต่ไม่พบที่เขียน: `FNMSysExpenseId, FNStateQC, FTRemark` (`RouteActual.vb:1026`)
- `TPDTProdActualFill` (header บรรจุ) key เดียวกัน — เขียนที่ `Actual/RouteActualFill.vb:622-741`; `FNMSysOperationId = 1` (= fill; 698)
  - `FNOrderQuantity, FNMSysUnitId, FDStartDate/FTSTime, FDEndDate/FTETime, FNNetTime, FNMSysMachineId, FNMSysLineId, CSEmpCode, FTNoteProblem`
  - สถานะผล (index ของ combo): `FNStateFIll, FNStateDeliveryOnTime, FNStateDeliveryQty, FNStateQC, FNProdFillType` (638-641, 658)
  - downtime (นาที): `FNDownTimeBreakMin, FNDownTimeMeetingMin, FNDownTimeCleanMin, FNDownTimeEtcMin`; lost time: `FNLostTimeChangPartMin, FNLostTimeBrokenMin, FNLostTimeSettingMin, FNLostTimeEtcMin` (644-651)
  - ของเสีย/ต้นทุน: `FTPKLostType, FNPKLostUse, FNPKLostSupl, FNPKLostPer, FNBulkLostBeforeFill, FNBulkLost, FNBulkLostPer, FNCostTotal, FNCostPerPcs, FNPKProblemNote, FNBulkProblemNote, FNCostProblemNote` (657-669)
- `TPDTProdActualFill_D` (ครั้งที่บรรจุ) key = `เลขแผน + FTBatchNo + FNSeq`: `FDDate, FNProcessQty` (จำนวนส่งมอบ), `FNSampleQty, FNNetTime, FNResourceUseQty, FTStateJob, FTProductOrderMixNo, FTBatchNoMix, FDDateMix, FNMSysRawMatId` (เนื้อ bulk), `FNMSysUnitId, FNQuantityUse` (`RouteActualFill.vb:782-832`)
- `TPDTProdActualFill_UseBulk(FTProductOrderNo, FTBatchNo, FNSeq, FTPDMixNo, FTBatchNoMix, FNQuantity)` (`RouteActualFill.vb:836-870`)
- อ่านอย่างเดียว: `Appointments` (แผน), `TPDMProdcutOrder`, `PUR.TSOTOrder(_Order)`, `TPDMRoute(Operation)`, `DK_MASTER.TINVENMMachine`, `SYSTEM.MSysListData` (list `FNStateJob`, `FTPKLostType`)

| ไฟล์ | สถานะ |
|---|---|
| `Actual/RouteActual.vb` | ฟอร์ม actual mix ที่ใช้จริง |
| `Actual/RouteActualFill.vb` | ฟอร์ม actual fill ที่ใช้จริง |
| `Actual/RouteActualAddFill.vb` | popup ครั้งที่บรรจุ — ไม่ถูกเปิด (handler double-click ไม่มี `Handles`; `RouteActualFill.vb:1774`) |
| `Actual/RouteActualAdd.vb` | ว่าง มีแต่โค้ดที่ comment (228-310) |
| `Actual/RouteActual_.vb` | สำเนาฟอร์ม SO เขียน `TSOTOrder` (852-854, 974-980) ไม่เกี่ยวกับ actual |
| `Actual/RouteActualAddFill - Copy.vb` | class `RouteActualAddFill_Backup` ข้าม |

**สำหรับระบบใหม่:** `production.operation_logs` ต่อ work order + operation + ครั้งที่ (run): `started_at, ended_at, good_qty, sample_qty, scrap_qty, headcount, machine_id, line_id, shift_id` · `production.downtime_logs` (run, reason_code, minutes) · `production.scrap_logs` (run, item, qty, reason_code, ฝ่ายรับผิดชอบ) · เหตุผล/ประเภทเป็น master ต่อ tenant · หน่วยมาจาก item ไม่ hard-code

## 2. ผูกกับแผน (Appointments → actual)

- เปิดฟอร์มด้วยเลขแผน: header โหลดจาก `Appointments a` ที่ `a.FTPlanNo = key` join `TPDMProdcutOrder` ด้วย `a.ProdNo` (mix; `RouteActual.vb:400-406`) หรือ `a.Subject` (fill; `RouteActualFill.vb:481-483`)
- สิ่งที่ผลิต = รหัส item ที่เป็นคำแรกของ `Appointments.Description` (ตัดที่ช่องว่างแรก) (`RouteActual.vb:383`, `RouteActualFill.vb:482`)
- ยอดแผน `FNOrderQuantity` = `Appointments.FNQuantity` ของ slot นั้น ไม่ใช่ยอด PD ทั้งใบ (`RouteActual.vb:384,416-418`; `RouteActualFill.vb:455,494`)
- ที่อื่นยืนยันความสัมพันธ์เดียวกัน: `pl.FTPlanNo = pr.FTProductOrderNo` (`Production/SaleOrderStatus.vb:358,363`)
- โครงแผน/PD ดู mrp-planning.md §3, §5

**สำหรับระบบใหม่:** operation log อ้าง `work_order_id + operation_id` (+ `schedule_slot_id` ถ้ามี) ด้วย FK ไม่ parse จากข้อความ; ยอดเป้าหมายของ run มาจาก slot, ยอดเป้าหมายรวมมาจาก work order

## 3. Actual mix (`RouteActual.vb`)

**บันทึก**
- บังคับกรอก: batch, เลขแผน, วันเริ่ม, วันจบ (526-546) — ไม่ตรวจ `FNProcessQty > 0`, เครื่อง, จำนวนคน
- upsert: update ตาม key ก่อน ถ้าไม่มีแถว → insert; `FNSeq ≤ 0` ถูกตั้งเป็น 1 (596-599) และฟอร์มล้าง `FNSeq = 0` หลัง save (820) → ทางปฏิบัติ **1 batch = 1 แถว**
- หลัง insert/update เรียก `exec ActualPland '<เลขแผน>'` ใน transaction เดียวกัน ไม่ตรวจผล (654-658)
- Batch no: ปุ่มที่ช่อง batch เรียก function `dbo.genbatchmixno(<item id>)` ถ้า error ได้ "999" (1491-1498); ผู้ใช้พิมพ์ทับได้

**สูตร**
- `FNNetTime (นาที) = DateDiff(นาที, วันเริ่ม+เวลาเริ่ม, วันจบ+เวลาจบ)` คำนวณใหม่ทุกครั้งที่แก้เวลา (1216-1233); วันจบ < วันเริ่ม ถูกดันเท่ากับวันเริ่ม (1206-1214); **ไม่หัก** พัก/setup — `FNSetUpTime` เป็นช่องกรอกแยก
- ยอดผลิตรวมของแผน `FNProdSumTotal = Σ FNProcessQty` ของทุก batch ที่ `FNProcessQty > 0` (1235-1249) — `FNBlendQty` ไม่รวม; เงื่อนไขหน่วย KG กับหน่วยอื่นใช้สูตรเดียวกัน
- `% ผลิตได้ = FNProdSumTotal × 100 / FNOrderQty` แสดง 3 ตำแหน่ง; ตัวใดตัวหนึ่ง ≤ 0 → "0.000%" (1251-1261)
- ของเสีย mix: **ไม่มีช่องปริมาณของเสีย** มีแค่ flag ปัญหา 3 กลุ่ม + หมายเหตุ

**ลบ**
- ปุ่ม Delete (header): ลบแถวตาม key (690-714) ไม่ตรวจ `FTStateAppToFG`
- ปุ่มลบรายการใน grid: ลบทุกแถวของ batch ที่เลือก; ห้ามถ้า `FTStateAppToFG = '1'` (1456-1487)

**สำหรับระบบใหม่:** run ของ operation ประเภท "batch" (mix) = 1 lot ของ semi; บังคับ `good_qty > 0`, เครื่อง, เวลา; `run_minutes = ended_at − started_at − Σ downtime` (หัก downtime เป็น tenant setting); ของเสียบันทึกเป็นปริมาณ + reason ไม่ใช่ checkbox

## 4. Actual fill (`RouteActualFill.vb`)

**บันทึก**
- บังคับกรอก header: เลขแผน, วันเริ่ม, วันจบ (592-608); header upsert แบบเดียวกับ mix (677-680)
- ครั้งที่บรรจุเพิ่มเป็นแถวเปล่าใน grid แล้วพิมพ์เอง (1543-1577): ครั้งที่บรรจุ, วันที่, จำนวนส่งมอบ, จำนวนตัวอย่าง, เวลา (นาที), จำนวนคน, สถานะงาน, PD ผสม, ครั้งที่ผสม, วันที่ผสม (`RouteActualFill.Designer.vb:599-762`); ไม่มี validation ระดับแถว
- บันทึกแถว: update ตาม `เลขแผน + FNSeq + FTBatchNo` ไม่เจอ → insert โดย `FNQuantityUse = 0, FNMSysUnitId = 0` เสมอ (799-832)
- `FNQuantiyFG` (แสดงผล) = `exec get_FG_Finish '<เลขแผน>', <item id>` (428-429)

**สูตร**
- ยอดผลิตรวม `FNProdSumTotal = Σ FNProcessQty + Σ FNSampleQty` (1755-1772; ตอนโหลด 926-930) → **ตัวอย่างนับเป็นยอดผลิต**
- เวลารวม header `FNNetTime = Σ FNNetTime ของแถว` (1506-1524; ตอนจำนวนแถวเปลี่ยนนับเฉพาะแถว `FNProcessQty > 0`; 1765) — ไม่ได้มาจากเวลาเริ่ม/จบ (handler ถูกปลด; 1488)
- `% ผลิตได้ = FNProdSumTotal × 100 / FNOrderQty` โดย `FNOrderQty = FNOrderQuantity` (ยอดแผน) (1526-1540)
- Bulk loss / PK loss / % / ค่าแรง: ค่าจากช่องกรอกถูกบันทึกตรงๆ (657-669) **ไม่มีสูตรใน VB**; downtime/lost time 8 ช่องไม่ถูกนำไปหักเวลา
- man-hour: VB เก็บ `FNNetTime` (นาที) กับ `FNResourceUseQty` (คน) ต่อครั้งที่บรรจุ; ค่า `FNManHour` คำนวณใน SP รายงาน
- การสะสม: หลายครั้งที่บรรจุ/หลายวัน = หลายแถว `_D` ของแผนเดียว ไม่มีแนวคิดกะ (shift) ในข้อมูล

**Bulk ที่ใช้:** แถว `_D` เก็บ PD ผสม + ครั้งที่ผสม + วันที่ผสม; ตาราง `TPDTProdActualFill_UseBulk` ถูกเขียนจาก dataset ชุดที่ 2 (836-870) ซึ่งโหลดมาจาก `TPDTProdActualFill_D.FNQuantityUse` (944-956) — popup ที่ใช้เลือก bulk (`fn_GetOrderForFill`; `RouteActualAddFill.vb:376-384`) เปิดไม่ได้ จึงไม่มีทางกรอกปริมาณ bulk ที่ใช้จากหน้าจอนี้

**ตัวอย่าง / reweigh**
- sample: มีเฉพาะ `FNSampleQty` ต่อครั้งที่บรรจุ ไม่สร้าง movement และไม่แยกจากยอดเข้าคลัง
- reweigh: ไม่พบใน DK.SO; คำว่า reweigh ปรากฏเฉพาะ `DK.Lab/RD/MExportReweigh.vb` (flow Lab)

**ลบ**
- ลบแถว: ตาม `เลขแผน + FTBatchNo + FNSeq` (1691-1717); กด Delete ใน grid ลบทุกแถวของครั้งที่บรรจุนั้น (1719-1753)
- ลบ header: ลบ `TPDTProdActualFill` ของ แผน + item และ `_D` **ทั้งหมดของแผน** (965-993); `_UseBulk` ไม่ถูกลบ

**สำหรับระบบใหม่ (golden test):**
- `good_qty_total = Σ good_qty` · `sample_qty` แยกต่างหากและออก movement `QC_SAMPLE`
- `yield_vs_plan % = good_qty_total / planned_qty × 100`
- `yield_vs_theoretical % = good_qty_total / (bulk_issued_qty × conversion ตาม BOM) × 100`
- `bulk_loss = bulk_issued − bulk_returned − (good_qty + sample_qty) × bulk_per_unit` · `bulk_loss % = bulk_loss / bulk_issued × 100`
- `pk_loss = pk_issued − pk_returned − (good_qty + sample_qty) × pk_per_unit` แยก reason (เสียจากการใช้ / เสียจาก supplier)
- `man_hours = Σ (run_minutes × headcount) / 60` · `machine_hours = Σ run_minutes / 60` · `labor_cost_per_unit = labor_cost / good_qty_total`
- สูตรข้างบนเป็นข้อเสนอของระบบใหม่ ต้องเทียบกับ SP เดิม (Gap 1-3) ก่อนล็อก

## 5. สถานะและ flow

- actual **ไม่มี** Draft/Submitted/Approved; บันทึกแล้วมีผลทันที แก้/ลบได้ตลอด (ยกเว้นลบ batch mix ที่อนุมัติเข้าคลังแล้ว)
- flag เดียวที่เป็นสถานะ: `FTStateAppToFG` 0/1 = ส่งผลผลิตเข้าคลังแล้ว (mix: `RouteActual.vb:1263-1307`; fill: `SP_SET_APPLOVEDTOFG`) — รายละเอียดใน production-to-wh.md
- ไม่มีโค้ด VB ที่ปิด PD หรือเปลี่ยน `TPDMProdcutOrder.FTProdStatus` / `Appointments.Status` จาก actual; สิ่งเดียวที่อาจทำคือ SP `ActualPland`
- ค่า combo (`FNStateFIll`, `FNStateDeliveryOnTime`, `FNStateDeliveryQty`, `FNStateQC`, `FNProdFillType`, `FTStateJob`, `FTPKLostType`) เก็บเป็น index ของ list ใน `MSysListData`

**สำหรับระบบใหม่ (state machine):** OperationRun: `Open → Completed → Verified` (+ `Voided`); Completed แล้วแก้ได้เฉพาะผู้มีสิทธิ์และมี audit; WorkOrder เป็น `Completed` อัตโนมัติเมื่อ operation สุดท้ายครบยอดหรือผู้ใช้สั่งปิด (tolerance ขาด/เกินเป็น tenant setting)

## 6. รายงาน (`Track/*`) — VB ส่งแค่ช่วงวันที่ + filter แล้ว bind ผล SP

| ฟอร์ม | SP | คอลัมน์หลักที่แสดง |
|---|---|---|
| `RptFillProduct.vb:288` | `SP_Get_Prodution_Yield(start, end, sect)` | `FNPlanQty, FNProcessQty, Yield`, ผลส่งมอบ `FTNameQty, FTNameTime, FTNameQC` |
| `RptFillProductOverBaht.vb:288` | `SP_Get_Prodution_Yield_OverBaht` | เหมือนบน + `FNCostTotal, FNCostPerPcs` |
| `RptFillProductAsFinish.vb:288` | `SP_Get_Prodution_Asfinish` | barcode, batch, `FNPalletQty`, คลัง/location, วันที่ |
| `RptFillMHProduct.vb:362` | `SP_Get_Prodution_MH(@StartDate, @EndDate, @SectId, @LineId, @RawMatId, @Job, @Protype)` | `FNPlanQty, FNProcessQty, FNNetTime, FNResourceUseQty, FNManHour, FNPKLost, FNPKLostPer, FNBulkLost, FNBulkLostPer` |
| `RptMixMHProduct.vb:322` | `SP_Get_Prodution_MH_Mix(@StartDate, @EndDate, @SectId, @LineId, @RawMatId)` | เหมือนบน + batch, เครื่องผสม |
| `RptSumaryMix.vb:264` | `SP_GET_Sum_Actual_MIX(start, end, '0')` | ต่อเดือน/เครื่อง: `FNPOMixFinish, FNOrderQty, FNActualQty`, จำนวน PO ที่มีปัญหา เวลา/ปริมาณ/คุณภาพ แยก ใน/นอก |
| `RptDefectMix.vb:264` | `SP_GET_SUMDefect_Actual_MIX(start, end, '0')` | batch ที่มีปัญหา + `FTNoteProblem` |
| `ActualmixTracking.vb:264` / `ActualfillTracking.vb:264` | `SP_GET_Actial_MIX(start, end, '0' / '1')` | แผนเทียบ actual ต่อ slot |

filter หลายค่าถูกส่งเป็น string คั่นด้วย comma (`RptFillMHProduct.vb:270-355`)

**สำหรับระบบใหม่:** รายงาน yield / man-hour / loss / OTIF คำนวณจาก operation log + ledger ด้วย query เดียวกับที่ API ใช้ (Dapper) กรองตาม tenant; ปัญหา in/out เป็น reason code ที่มี attribute "ภายใน/ภายนอก"

## 7. Links ไป flow อื่น

- แผน → actual: §2
- เบิกวัตถุดิบเข้าไลน์: `TINVENIssue.FTJobOrderNo = Appointments.FTPlanNo` (inventory-issue.md §5) — actual **ไม่อ่านยอดเบิก** และไม่ backflush
- actual → คลัง/QC/FG: production-to-wh.md
- SO tracking: `SaleOrderStatus.vb:348-369` ให้ flag ต่อ SO line: มี PD, มีแผน, มี actual mix, มี actual fill, FG อนุมัติเข้าคลังแล้ว (`TPDTProdActualFill.FTStateAppToFG = '1'`)

## 8. SP / function ที่เกี่ยว (ไม่มี definition ใน repo — ต้องขอ)

- SP: `ActualPland` (`RouteActual.vb:654`), `get_FG_Finish` (`RouteActualFill.vb:428`), `SP_Get_Prodution_Yield`, `SP_Get_Prodution_Yield_OverBaht`, `SP_Get_Prodution_Asfinish`, `SP_Get_Prodution_MH`, `SP_Get_Prodution_MH_Mix`, `SP_GET_Sum_Actual_MIX`, `SP_GET_SUMDefect_Actual_MIX`, `SP_GET_Actial_MIX` (call site ตามตาราง §6), `SP_GET_DYNAMIC_OBJECT_CONTROL` (`RouteActual.vb:235`)
- function: `genbatchmixno` (`RouteActual.vb:1493`), `genbatchfillno` (`RouteActualAddFill.vb:388`), `fn_GetOrderForFill` (`RouteActualAddFill.vb:379`), `fn_GetProductFillToWhPallet` (`RouteActualFill.vb:939`)

## Defects ของระบบเดิม (ห้ามยกมา)

- loss / % loss / ต้นทุน เป็นตัวเลขกรอกมือ ไม่ผูกกับยอดเบิกจริง (`RouteActualFill.vb:657-669`)
- ตัวอย่างถูกบวกเข้ายอดผลิต (`RouteActualFill.vb:1761-1763`)
- ผูกแผนกับ item ด้วยการตัดข้อความ `Appointments.Description` (`RouteActual.vb:383`, `RouteActualFill.vb:482`)
- หน่วยและ operation hard-code (`RouteActual.vb:565,613`; `RouteActualFill.vb:698`)
- mix ไม่ตรวจยอด > 0 (`RouteActual.vb:526-546`); fill ไม่ตรวจแถวเลย
- ปุ่ม Delete ของ mix ไม่ตรวจว่าอนุมัติเข้าคลังแล้ว (`RouteActual.vb:690-714, 846-863`) และรันคำสั่งลบซ้ำ 2 ครั้ง (700-705)
- ลบ header fill ลบ `_D` ทั้งแผนโดยไม่กรอง item และทิ้ง `_UseBulk` กำพร้า (`RouteActualFill.vb:971-984`)
- popup ครั้งที่บรรจุเปิดไม่ได้ ทำให้ `FNQuantityUse` เป็น 0 เสมอ (`RouteActualFill.vb:1774, 825-826`)
- grid พาเลทในฟอร์ม fill เพิ่ม/แบ่งแถวได้แต่ไม่ถูกบันทึก (`RouteActualFill.vb:1939-2011`)
- double-click batch mix โหลดแถวด้วย batch อย่างเดียว ไม่ใช้ `FNSeq` (`RouteActual.vb:1110-1116`)
- ผล `ActualPland` ไม่ถูกตรวจ (`RouteActual.vb:654-657`); empty catch ทั่วไฟล์; SQL concat (เลขแผน/batch ไม่ escape: `RouteActual.vb:406,1116`; `RouteActualFill.vb:790-801`); จำนวนใช้ Double

## Gap ที่ต้องดู SP definition / data

1. `SP_Get_Prodution_Yield` / `_OverBaht`: สูตร `Yield` (actual ÷ plan หรือ ÷ ทฤษฎีจาก BOM), นับ sample ไหม, ปัดเศษอย่างไร
2. `SP_Get_Prodution_MH` / `_MH_Mix`: สูตร `FNManHour` (นาที × คน ÷ 60?) หัก downtime/lost time ไหม; `FNPKLost` มาจาก `FNPKLostUse + FNPKLostSupl` หรือไม่
3. `FNPKLostPer`, `FNBulkLostPer`: ตัวหารที่โรงงานใช้จริง (ยอดเบิก, ยอดแผน, หรือยอดผลิต) — ต้องถามผู้ใช้เพราะกรอกมือ
4. `ActualPland`: update อะไร (สถานะแผน, `FTProdStatus`, ยอดสะสม, ปิด PD)
5. `genbatchmixno` / `genbatchfillno`: รูปแบบเลข batch/lot และ reset ตามช่วงเวลาไหม
6. `get_FG_Finish` และ `fn_GetOrderForFill`: ยอด FG ที่เสร็จนับจากอะไร, bulk ที่เสนอให้เลือกมาจากยอดเบิกหรือ actual mix
7. ค่า list ใน `MSysListData`: `FNStateJob`, `FTPKLostType`, `FNStateFIll`, `FNStateDeliveryOnTime`, `FNStateDeliveryQty`, `FNStateQC`, `FNProdFillType`
8. ข้อมูลจริงของ `TPDTProdActualFill_UseBulk` และ `FNQuantityUse` มีค่าหรือไม่ (มีโปรแกรมอื่นเขียนไหม)
9. `FNBlendQty` / `FTBlendCode` หมายถึงอะไร (ผสมรวมกับ batch เก่า?) และควรนับเป็นยอดผลิตไหม — สองฟอร์มเข้าคลังใช้ไม่ตรงกัน (production-to-wh.md)
10. unit id 1707180007 คือหน่วยอะไร และ actual mix ทุกตัวใช้หน่วยนี้จริงไหม
11. reweigh ของสายการผลิตทำที่ไหน (นอกระบบ หรือใน DK.Lab) และต้องอยู่ใน Pro หรือไม่
12. trigger บน `TPDTProdActual*` มีไหม; ความหมายคอลัมน์ `FNMSysExpenseId`, `FNStateQC`, `FTRemark` ของ `TPDTProdActual`
