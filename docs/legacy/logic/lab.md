# Logic เดิม: Lab R&D — วัตถุดิบ R&D / สูตร / ต้นทุนสูตร / project / sample (DK.Lab)

ถอดจาก repo DK-ERP-SYSTEM 2026-09-29 · อ้างอิง path ในระบบเดิม · DB: **LAB** (`Config.DataBase.LAB`; `DK.Data/Config.cs:36-38`) — โค้ดอ้างชื่อ `iNOVA_sYSTEM` ตรง ๆ หนึ่งจุด (`DK.Lab/RD/MSendtomarketing.vb:129`) จึงน่าจะเป็น DB legacy `INOVA_SYSTEM`
path ย่อ: ไฟล์ที่ไม่ระบุโฟลเดอร์อยู่ใน `DK.Lab/` · ฟอร์มคู่ที่เป็นสำเนากัน: `RD/MBom` ↔ `RD/MBomAdmin`, `Marketing/MCostAdmin` ↔ `MCostAll` (ต่างแค่ชื่อ class), `Marketing/MProject` ↔ `MProjectReport`, `RD/MRawmat` ↔ `MRawmatRead` (อ่านอย่างเดียว)

**สรุปสำคัญ:** DK.Lab เป็นระบบ **แยกขาดจาก ERP** — ตาราง, รหัสวัตถุดิบ, ลูกค้า, ราคา เป็นของตัวเองทั้งหมด **ไม่พบโค้ดใดนอก DK.Lab ที่อ้างตาราง LAB** จึงไม่มีการแปลงสูตร lab เป็น BOM ผลิตในโค้ด
สูตรเก็บเป็น "น้ำหนัก" ต่อบรรทัด ไม่บังคับรวม 100% · ต้นทุนสูตรคำนวณใน SP ที่ไม่มีใน repo · "อนุมัติ" มีแค่ flag lock ที่ติดเมื่อส่งสูตรให้การตลาด · sample เป็นทะเบียนตัวอย่างอ้างอิง ไม่มี lifecycle การขอ sample
→ ระบบใหม่ต้องมี formula version + approval จริง, บังคับ % รวม, cost roll-up ใน domain, และ action "promote เป็น BOM"

## 1. วัตถุดิบ R&D (`RD/MRawmat.vb` + popup)

**ตาราง**
- `T_Material(MCode, RecNo, TradName, INCI, Sloc, sub_sloc, Document_name, Doc_Sloc, File_sloc, Cheese_sloc, COA, MSDS, SPEC, GenData, Formular, Technical, Source, Product_Pack, Comment, Product_Sample, Vander, SppName, Current_Price, Disp_Price, Exp_Date, Old_Code, BlendCode, EditINCI, Reach_No, IFRA, Allergen, DCP, Halal, FNMSysCmpId)` — update ถ้า 0 แถวจึง insert ใน transaction (`RD/MRawmat.vb:368-468`)
- `Master_Property(MpCode, Property_Name, FNMSysCmpId)` = function/คุณสมบัติ (`RD/MFunction.vb:280-298`); `Mat_Property(ID, MCode, MPcode, Property_Mat)` map วัตถุดิบ ↔ function + แถวสรุปที่ต่อชื่อ function เป็น string เดียว สร้างใหม่ทุกครั้งที่ save/ลบ (`RD/MMapFunc.vb:119-162`)
- `Price_List(PCode, MCode, Price_KG, Rec_Date, Credit_Due, EXC_Rate, Validity, LeadTime, Unit_Packsize, Price_No, Price_old)` ใบเสนอราคาจาก supplier (`RD/MMapPricelist.vb:121-170`)
- `Mat_Stock(MsCode, MCode, Mat_Date, QTY_Add, PerUnit, QTY_Total, QTY_Bal, Batch, Lot, Comment, QTY_input)` สต็อกของห้อง lab (`RD/MMapQty.vb:121-171`)
- `Mat_File(MfCode, MCode, FileName, Sloc, Sub_Sloc, Doc_backup1..3)` ที่เก็บเอกสาร (`RD/MMapDocuement.vb:131-136`)

**กฎ**
- required: `MCode` เท่านั้น (`RD/MRawmat.vb:336-356`)
- รหัสใหม่ = running 4 หลัก + ปี พ.ศ. 2 หลัก (`NNNNYY`) หา max ของ `RecNo` ที่ลงท้ายด้วยปีปัจจุบัน + 1 (702-725); `PCode` / `MsCode` / `MfCode` = max ทั้งตาราง + 1 (`RD/MMapPricelist.vb:220-231`)
- **ราคาแสดง:** `Disp_Price = Current_Price × 1.75 + 777` คำนวณทันทีเมื่อแก้ `Current_Price` (740-746) — ราคาจริงถูกพรางจากผู้ใช้ R&D; list แสดง `PriceAvg = Current_Price ÷ max(Current_Price ทั้งตาราง)` (256)
- `Current_Price` **คีย์มือ** — บันทึก `Price_List` ไม่ได้ update ราคาปัจจุบันของวัตถุดิบ (`RD/MMapPricelist.vb:121-170`)
- `Mat_Stock`: ทุก field คีย์มือรวมยอดคงเหลือ `QTY_Bal` ไม่มีการคำนวณ (`RD/MMapQty.vb:126-135`)
- เปลี่ยนรหัส (`ocmchangecode`): เก็บรหัสเดิมลง `Old_Code` แล้ว update `MCode` ใน `T_Material`, `Bom_Detail`, `Mat_Stock`, `Price_List`, `Mat_Property`, `Mat_File` ใน transaction เดียว (576-699)
- ลบ = ลบแถวจริง ไม่เช็กว่าถูกใช้ในสูตร (358-367)
- field compliance (`Reach_No`, `IFRA`, `Allergen`, `DCP`) บันทึกจาก**ข้อความที่ถูก highlight** ใน control ไม่ใช่ค่าทั้งช่อง (404-407)

**สำหรับระบบใหม่:** ใช้ `masters.items` ตัวเดียวกับ ERP (type = RM) + ตารางเสริม `lab.material_profiles(item_id, inci_name, trade_name, reach_no, ifra, allergen, halal, …)`; เอกสาร COA/MSDS/SPEC เป็นไฟล์บน R2 มีวันหมดอายุ; ราคาที่ใช้คิดต้นทุนมาจากราคาซื้อล่าสุด/ใบเสนอราคาที่ active ไม่คีย์มือ; การพรางราคาเป็น permission ระดับ field ไม่ใช่สูตร; สต็อก lab เป็นคลังประเภท LAB ใน ledger เดียวกัน

## 2. สูตร (`RD/MBom*.vb`, `RD/MBomDetail.vb`)

**ตาราง**
- `Bom_Master(Bom_Code, Bom_Name, Sum_Weigh, sumReweigh, sub_total_cost, ChkCost, flagDel, Bom_Lock, STID = เจ้าของสูตร, StaffUserName, Group_Name, StrDate, StpDate, CopyFrom, FG, Brand, Halal, FNMSysCmpId)` (`RD/MBomAdmin.vb:363-400`)
- `Bom_Detail(Bom_DCode, Bom_Code, MCode, Weigh, Reweigh)` (`RD/MBomDetail.vb:338-391`)
- เจ้าของสูตรผูกกับ user ของ ERP ผ่าน `UserLabId`, `UserLabName`, `GroupLabName` บนตาราง user (`DK.SE/User.vb:71-98`, `DK.ST/wUserLogIn.cs:280-283`)

**กฎ**
- required: รหัสสูตร + ชื่อสูตร (`RD/MBomAdmin.vb:331-351`); header ใหม่เริ่มด้วย `flagDel=0, Bom_Lock=0, sub_total_cost=0` (377-386)
- **รหัสสูตร = `NNNN/rev/YY`** (YY = ปี พ.ศ.): สูตรใหม่ = running ถัดไปของปี + `/1/` (556-577)
- **Revise** (ปุ่ม adjust): clone header + ทุกบรรทัดเป็นรหัส `NNNN/(rev สูงสุด + 1)/YY`, `CopyFrom` = รหัสเดิม, `Bom_Lock = 0`, เจ้าของ = ผู้กด (579-603, 630-682) — สูตรเดิมไม่ถูกปิด/supersede
- **Copy:** clone เหมือนกันแต่ได้ running ใหม่ `/1/` (508-553)
- **บรรทัด:** item + `Weigh`; **ไม่มีการตรวจผลรวม = 100** และไม่ตรวจ item ซ้ำ; เงื่อนไขเดียวคือสูตรต้องไม่ lock (`RD/MBomDetail.vb:236-275`)
- **ยอดรวม/ต้นทุน** มาจาก SP `sp_Disp_For_Bom_Detail_Admin` คืนคอลัมน์ `TotalPrice`, `SumPW`, `Weigh` ต่อบรรทัด; ฟอร์มรวม `Σ Weigh` เอง (`RD/MBomDetail.vb:298-313`) — ไม่มีโค้ด VB ที่เขียน `Sum_Weigh` / `sub_total_cost` กลับลง `Bom_Master`
- **Reweigh:** admin แก้ค่า `Reweigh` ต่อบรรทัดจาก grid (น้ำหนักชั่งจริง/ปรับ) (`RD/MBomAdmin.vb:761-794`); `RD/MExportReweigh.vb` list สูตรพร้อม flag ว่ามี reweigh แล้วพิมพ์ `BomReport.rpt` ผ่าน `spPrintBomReport_Tmp` (231-232, 319-337)
- **สิทธิ์เห็นข้อมูล:** `MBom` (ผู้ใช้ R&D) เห็นเฉพาะสูตรที่ `STID` = ตัวเอง และซ่อนคอลัมน์ราคา/ต้นทุน (`RD/MBom.vb:234-235, 409-414`); `MBomAdmin` เห็นทุกสูตรของบริษัท + ต้นทุน + แก้ `Brand`, `FG`, `Bom_Lock` ได้ (`RD/MBomAdmin.vb:229-239, 368-375`)
- ลบสูตร = soft delete `flagDel='1'` + `StpDate` = วันนี้ (353-362); ลบบรรทัด = ลบแถวจริง (`RD/MBomDetail.vb:314-336`)

**สำหรับระบบใหม่:** `lab.formulas(code, name, project_id, owner_id, status)` + `formula_versions(version_no, status, base_qty, base_uom, copied_from_version_id)` + `formula_lines(item_id, percent numeric(9,6), phase, sequence, remark)`; **บังคับ Σ percent = 100 ± tolerance (tenant setting) ก่อน submit**; ห้าม item ซ้ำใน phase เดียวกัน; version ใหม่ต้อง supersede ตัวเดิมเมื่อ approve; reweigh/ผลทดลองเก็บเป็น trial record แยกจากสูตร ไม่แก้ทับบรรทัดสูตร

## 3. ต้นทุนสูตรและการส่งให้การตลาด (`RD/MSendtomarketing.vb`, `Marketing/MCost*.vb`)

**ตาราง:** `Project_Bom_Price(PID, PtCode, AtCode, Bom_Code, DatePost, Net, Profit, Price)`

**Flow**
1. ในหน้าสูตรกด "ส่งการตลาด" → เลือก project (`PtCode` บังคับ; `RD/MSendtomarketing.vb:84-99`)
2. insert `Project_Bom_Price`: `Net = Bom_Master.sub_total_cost`, **`Profit = 1.20` คงที่**, `Price = sub_total_cost × 1.2`, `AtCode` = attribute แรกของ project, `DatePost` = วันนี้ (125-141)
3. พิมพ์ `Print_Bom.rpt` ผ่าน `sp_Report_Bom_Cost_ToTmp` (151-169)
4. กลับมาที่หน้าสูตร set `Bom_Lock = 1` (`RD/MBomAdmin.vb:464-506`) → แก้/ลบบรรทัดไม่ได้อีก
5. การตลาดเปิด `MCostAdmin` ค้นด้วย project / ลูกค้า / ผู้ทำ / รหัสสูตร (อย่างน้อย 1 เงื่อนไข; 341-369) ผ่าน `sp_Project_Bom_Price_Admin_Load` แล้วแก้ `Profit` — **ค่าเดียวถูกเขียนลงทุกแถวที่โหลดมา** (`Marketing/MCostAdmin.vb:259-292`)

**กฎ**
- cost roll-up: โค้ด VB ไม่มีสูตร — คาดว่า `Σ (Weigh × ราคาต่อหน่วยของวัตถุดิบ)` อยู่ใน SP (Gap ข้อ 1)
- ราคาขายเสนอ = ต้นทุน × ตัวคูณกำไร; ฟอร์มไม่คำนวณ `Price` ใหม่หลังแก้ `Profit` (ถ้ามีอยู่ใน SP โหลด)
- ส่งสูตรเดิมซ้ำได้ไม่จำกัด — insert แถวใหม่ทุกครั้ง ไม่มี unique (128)
- checkbox `StateApprove` มีบนจอแต่ไม่ถูกอ่าน/บันทึก (`Marketing/MCostAdmin.vb:294-298`)

**สถานะสูตรโดยรวม**

| สถานะ | flag | เปลี่ยนโดย |
|---|---|---|
| กำลังพัฒนา | `flagDel=0, Bom_Lock=0` | สร้าง / copy / revise |
| ส่งการตลาดแล้ว (ล็อก) | `Bom_Lock=1` | ปุ่มส่งการตลาด (`RD/MBomAdmin.vb:464-480`) |
| ปลดล็อก | `Bom_Lock=0` | admin ติ๊กออกแล้ว save header (372) — ไม่มีเหตุผล/ประวัติ |
| ลบ | `flagDel=1` | ปุ่มลบ (353-362) — ไม่เช็กว่า lock หรือถูกส่งการตลาดแล้ว |

**สำหรับระบบใหม่:** FormulaVersion: Draft → Submitted → Approved (ขั้นอนุมัติ config ต่อ tenant) → Released (promote เป็น BOM แล้ว) → Superseded / Rejected / Archived; cost roll-up ใน domain: `cost_per_base = Σ (percent ÷ 100 × base_qty × unit_cost ของ item ณ วันที่คิด) + overhead` เก็บ snapshot ต่อครั้ง (ราคาที่ใช้ + วันที่) ; quotation แยก entity `lab.formula_quotes(formula_version_id, project_id, cost_snapshot_id, markup_percent, price)` unique ต่อ version + project + รอบ; markup เป็น tenant setting; golden test สำหรับ roll-up

## 4. Project (`Marketing/MProject.vb`)

**ตาราง**
- `Project(PtCode, PtName, Product_Name, Cust_id, Marketing_id, StrDate, Comment, FNMSysCmpId)` (345-378)
- ลูกของ project 7 tab ผูกด้วย `PtCode`: `Attribute(AtCode, AttBulk, Attcolor, AttSmell, Cost, DateFinish, Comment)` = spec ที่ลูกค้าต้องการ + ต้นทุนเป้าหมาย, `Effect(ECode, Effect)`, `Sample_in_Project(SipCode, SmCode)`, `How_to_do(HCode, Doing)`, `Marketing_Point(MpCode, Point)`, `Package_Requirement(PkCode, PkAttribute, PkSize, PerUnit, PkCost, Comment)`, `Best_for_goods(BgCode, …)` (243-268, 678-694)
- `Customer_Master(Cust_id, cust_name, cust_Contact, cust_type, Address, District, Subdivision, Province, Zip, Tel, Fax, Country, Condition, FNMSysCmpId)` — ลูกค้าของ lab แยกจาก ERP (`OPeration/MCustomer.vb:298-349`); `T_Staff(StID, StName)` (`Marketing/MProject.vb:243`)

**กฎ**
- รหัส project = `Pt` + ปี พ.ศ. 2 หลัก + running 4 หลัก (413-436)
- แต่ละ tab มีปุ่ม new / save / copy / delete ทำงานตาม tab ที่เปิดอยู่ (517-593)
- ลบ project = ลบแถว `Project` อย่างเดียว ลูกทั้ง 7 ตารางและ `Project_Bom_Price` ค้าง (335-344)
- project **ไม่มี field สถานะ** (เปิด/ปิด/ชนะ/แพ้) และไม่มีวันครบกำหนดระดับ project

**สำหรับระบบใหม่:** `lab.projects(code, customer_id → masters.customers (รองรับ prospect), product_name, owner_id, status = Open | Sampling | Quoted | Won | Lost | OnHold, target_cost, target_date)` + `project_requirements(type = attribute | effect | usage | claim | packaging | benchmark, detail jsonb)` แทน 7 ตาราง; ผูก formula, sample, quote เข้ากับ project

## 5. Sample (`OPeration/MSample.vb`)

- `T_Sample_Product(SMCode, Product_Sample, Qty, Unit, Price, Buy_Date, Sloc, sub_sloc, Document_name, Doc_Sloc, File_sloc, Cheese_sloc, Kind_Product, Vander, SppName, Source, Comment, Old_Code, FNMSysCmpId)` (309-375) — field บอกว่าเป็น**ทะเบียนตัวอย่างสินค้าอ้างอิง/benchmark ที่ซื้อมา** (วันที่ซื้อ, ราคา, ผู้ขาย, ที่เก็บ)
- required: รหัส + ชื่อ (277-297); รหัส = running 4 หลัก + `s` + ปี พ.ศ. 2 หลัก (436-458); `Qty` เก็บเป็นข้อความ (316)
- ผูกเข้ากับ project ผ่าน `Sample_in_Project` (`Marketing/MProject.vb:944-1031`)
- **ไม่พบ lifecycle การขอ sample** (ลูกค้าขอ → lab ทำ → ส่ง → feedback): ไม่มีตารางคำขอ, สถานะ, ผู้อนุมัติ, วันที่ส่ง, ผลตอบรับ ในโมดูลนี้ (ทำที่ไหนในปัจจุบัน → Gap ข้อ 8)

**สำหรับระบบใหม่:** แยก 2 entity — `lab.reference_samples` (ของที่ซื้อมาเทียบ) และ `lab.sample_requests(project_id, formula_version_id, qty, requested_by, due_date, status = Requested → Approved → InPreparation → Sent → FeedbackReceived → Accepted | Rework | Cancelled)`; ส่ง sample = movement OUT จากคลัง LAB; feedback ผูกกลับไป formula version เพื่อเปิด version ถัดไป

## 6. ความสัมพันธ์กับ flow อื่น

- **สูตร lab → BOM ผลิต: ไม่มีในโค้ด** — ไม่พบไฟล์นอก `DK.Lab/` ที่อ้าง `Config.DataBase.LAB`, `Bom_Master`, `Bom_Detail`, `T_Material`; BOM ผลิตอยู่ที่ `DK_PROD.TPDMBomFormula*` (mrp-planning.md §1) ใช้ `FNMSysRawMatId` และปริมาณสัมบูรณ์ต่อ batch ขณะที่ lab ใช้ `MCode` ของตัวเองและ `Weigh` → ปัจจุบันต้องคีย์ BOM ใหม่ด้วยมือ
- จุดเชื่อมเดียวกับ ERP: user (`UserLabId`) และ `FNMSysCmpId` (บริษัท); `Bom_Master.FG` เป็นข้อความอิสระ ไม่ใช่ FK ไป item
- ราคา/สต็อก/ลูกค้า ของ lab ไม่ sync กับ PO, คลัง, `OMCustomer` / `TCNMCustomer`

**สำหรับระบบใหม่:** action "Promote to BOM" บน formula version ที่ Approved: เลือก FG/bulk item + batch size → สร้าง `BomVersion` สถานะ Draft โดย `qty = percent ÷ 100 × batch_size`, loss % เริ่ม 0, เก็บ `source_formula_version_id`; BOM ต้องผ่าน approval ของฝ่ายผลิตอีกขั้น; แก้สูตรหลัง promote = version ใหม่ + BOM version ใหม่ ไม่แก้ทับ

## 7. SP / function / view ที่เกี่ยว (ไม่มี definition ใน repo — ต้องขอ)

- สูตร/ต้นทุน: `sp_Disp_For_Bom_Detail_Admin`, `sp_Disp_For_Bom_Detail_Admin_Load` (`RD/MBomAdmin.vb:280, 431`, `RD/MBomDetail.vb:302`)
- รายงาน: `spPrintBomReport_Tmp` → `BomReport.rpt` (`RD/MBomAdmin.vb:800`, `RD/MExportReweigh.vb:323`), `sp_Report_Bom_Cost_ToTmp` → `Print_Bom.rpt` (`RD/MSendtomarketing.vb:154`)
- การตลาด: `sp_Project_Bom_Price_Admin_Load` (`Marketing/MCostAdmin.vb:217-219`)

## Defects ของระบบเดิม (ห้ามยกมา)

- ระบบ lab แยกขาด: item, ราคา, สต็อก, ลูกค้า ซ้ำกับ ERP และไม่มีทางส่งสูตรไปผลิต
- ไม่บังคับ Σ น้ำหนัก = 100; item ซ้ำในสูตรได้; ไม่มี approval จริง — lock เป็นผลข้างเคียงของการส่งการตลาด และ admin ปลดได้โดยไม่มีประวัติ
- **running ของ `Bom_DCode` ชนกัน:** copy/revise ใช้ 7 หลัก + ปี (`RD/MBomAdmin.vb:605-627`) แต่หน้าเพิ่มบรรทัดใช้ 4 หลัก + ปี โดยตัด 4 ตัวแรกของ max ที่อาจยาว 9 ตัว (`RD/MBomDetail.vb:358-369`) → รหัสซ้ำ/ถอยหลัง; ทุก running เป็น `MAX + 1` ไม่กัน race
- SQL บันทึก reweigh มี `WHERE` สองครั้ง → error ทุกครั้ง, rollback แล้ว commit ต่อ, error ถูกกลืน (`RD/MBomAdmin.vb:772-787`)
- tab Attribute ของ project: คำสั่ง save/copy ใช้คอลัมน์ของตาราง `Project` และมี quote ไม่ปิด; field ที่คีย์จริง (`AttBulk`, `Attcolor`, `AttSmell`, `DateFinish`) ไม่ถูกบันทึก (`Marketing/MProject.vb:696-733, 777-805`)
- pattern rollback แล้วทำต่อ: หลัง `Rollback()` โค้ดยังวน execute และ `Commit()` (`RD/MBomAdmin.vb:522-546`, `Marketing/MCostAdmin.vb:274-285`); ใน change code มี `Exit Sub` ก่อน `Rollback` → transaction ค้าง (`RD/MRawmat.vb:602-605`)
- แก้ `Profit` เขียนค่าเดียวลงทุกแถวที่ค้นเจอ; ตัวคูณ 1.20 และสูตรพรางราคา `× 1.75 + 777` hard-code
- ลบวัตถุดิบ/ลูกค้า/sample/project เป็น hard delete ไม่เช็กการใช้งาน; `MRawmatRead` มีฟังก์ชันลบที่ชี้ตารางผิด (`Master_Property`; `RD/MRawmatRead.vb:354`) — ไม่พบปุ่มที่เรียก
- update หลายจุดไม่กรอง `FNMSysCmpId` (lock, ลบ, change code; `RD/MBomAdmin.vb:356, 488-491`, `RD/MRawmat.vb:361`) → กระทบข้ามบริษัทถ้ารหัสซ้ำ
- SQL concat: popup ราคา/สต็อก/เอกสาร และบรรทัดสูตรไม่ escape ค่า (`RD/MBomDetail.vb:344-349`, `RD/MMapPricelist.vb:113`); field compliance บันทึกจาก `SelectedText`; `Qty` ของ sample และ `QTY_Bal` เป็น string; `catch` ว่างทุก method

## Gap ที่ต้องดู SP definition / data

1. `sp_Disp_For_Bom_Detail_Admin(_Load)`: สูตร `TotalPrice` / `SumPW` — ใช้ `Current_Price`, `Disp_Price` หรือ `Price_List` ล่าสุด, หน่วยราคา (ต่อ kg?) และหน่วยของ `Weigh` (g หรือ %), รวม overhead/packaging หรือไม่
2. `Bom_Master.Sum_Weigh`, `sumReweigh`, `sub_total_cost`, `ChkCost` ถูก update ที่ไหน (trigger / SP / ระบบ web เดิมของ iNOVA)
3. data จริง: Σ `Weigh` ต่อสูตรเป็น 100 เสมอหรือไม่ (ตัดสินว่า migrate เป็น % ได้ตรง ๆ ไหม)
4. ความหมายทางธุรกิจของ `Reweigh` (ชั่งจริงตอนทดลอง / ปรับสูตรสำหรับผลิต) และใครใช้รายงาน reweigh
5. ขั้นตอนจริงจากสูตรที่ลูกค้าอนุมัติ → BOM ผลิตใน `TPDMBomFormula`: ใครคีย์, map `MCode` ↔ `FTRawMatCode` อย่างไร (ใช้ `Old_Code` / `BlendCode`?), แปลงน้ำหนักเป็นปริมาณต่อ batch อย่างไร
6. `sp_Project_Bom_Price_Admin_Load`: คำนวณ `Price` จาก `Net × Profit` ตอนโหลดหรือไม่, คอลัมน์ที่คืน, มีสถานะอนุมัติราคาหรือไม่
7. schema จริงของ `Attribute` (คอลัมน์ `PtName`, `Product_Name`, `Cust_id` มีอยู่จริงหรือคำสั่ง save ล้มตลอด) และ key auto-increment ของตารางลูก project (`AtCode`, `ECode`, `SipCode`, …)
8. การขอ/ส่ง sample ให้ลูกค้าทำที่ไหนในปัจจุบัน (โมดูล DK.SO / DK.INVEN sample, Excel, หรือระบบ iNOVA เดิม) — ต้องถามผู้ใช้
9. DB `LAB` ชี้ไป `INOVA_SYSTEM` จริงหรือไม่ และยังมี web/app เดิมของ iNOVA เขียนตารางชุดนี้อยู่หรือไม่
10. `T_Staff`, `Group_Name`: โครงทีม R&D / การตลาด และสิทธิ์เห็นต้นทุนจริงกำหนดที่เมนูหรือที่ฟอร์ม
11. `BlendCode`, `Cheese_sloc`, `Halal`, `EditINCI` ใช้ทำอะไร; มี premix/blend (สูตรซ้อนสูตร) ใน data หรือไม่
12. unique index / FK / trigger บนตาราง LAB ทั้งหมด
