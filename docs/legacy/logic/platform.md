# Logic เดิม: Platform — ผู้ใช้ / สิทธิ์ / เลขเอกสาร / ภาษา / dynamic master (DK.SE + DK.ST + DK.TL + DK.UCTR + DK.Data + DK.App)

ถอดจาก repo DK-ERP-SYSTEM 2026-09-29 · อ้างอิง path ในระบบเดิม · DB: SECURITY, SYSTEM, LANG, LOG, MASTER

**สรุปสำคัญ:** รหัสผ่านเก็บแบบ **เข้ารหัสย้อนกลับได้** (Caesar shift + reverse) ไม่ใช่ hash, ไม่มี expiry/lockout/นโยบายความยาว, หน้าจอแก้ผู้ใช้ถอดรหัสมาแสดงได้
สิทธิ์เป็น role (`TSEPermission`) ผูก user หลาย role → ได้สิทธิ์แบบ union; ระดับแถวใช้จริงแค่ **บริษัท (ตอน login)** และ **คลัง**; **กลุ่มวัตถุดิบมีตารางแต่ถูก comment ทั้งฝั่งบันทึกและฝั่งกรอง**
เลขเอกสารออกจาก SP `SP_GEN_DOCUMENTNO` (ไม่มีใน repo) ส่วนใหญ่เรียก**นอก transaction** → ระบบใหม่ต้องออกเลขใน transaction ต่อ tenant

## 1. การเชื่อมต่อและ session

- DB 13 ตัวตาม enum `SYSTEM, SECURITY, LANG, LOG, MASTER, HR, PUR, INVEN, PROD, ACC, MAR, MAIL, LAB` (`DK.Data/Config.cs:23-38`); อ่าน server/db/user/password ต่อ DB จาก `Database.xml` และ `DatabaseVat.xml` ข้าง exe (54, 86) password ใน XML ผ่าน `DataDecrypt` (68) → มี server ชุดที่ 2 ("Vat") ถ้า server name ต่างกัน (133-146)
- query ทุกตัวเป็น text command ข้าม DB ด้วยชื่อเต็ม `[db].dbo.table`, `CommandTimeout = 0` (`DK.Data/Conn.cs:31`), อ่านด้วย `WITH(NOLOCK)` เกือบทั้งหมด
- transaction: `ConnTrans` เปิด connection + `BeginTransaction` ต่อ DB เดียว (`DK.Data/ConnTrans.cs:19-26`, Commit 213, Rollback 220)
- state ของ session เป็น static: `UserInfo` (UserName, UserPassword plain ในหน่วยความจำ, เครื่อง, IP; `DK.ST/UserInfo.cs:14-33`), `SysInfo` (Admin, CmpCode, CmpID, CmpRunID/CmpRunDoc, ModuleName, MenuName; `DK.ST/SysInfo.cs:13-28`)
- ทศนิยมแสดงผล hard-code: อัตราแลกเปลี่ยน 5, ราคา 5, ปริมาณ 4, เงิน 2, % 2 (`DK.ST/Config.cs:89-122`)

**สำหรับระบบใหม่:** 1 database + schema ต่อโมดูล, connection string จาก secret manager, tenant/user context มาจาก JWT claim ต่อ request (ไม่มี static state), ทศนิยมเป็น tenant setting

## 2. Login และรหัสผ่าน

**ตาราง (SECURITY)**
- `TSEUserLogin(FTUserName PK โดยพฤตินัย, FTPassword, FTUserDescriptionTH/EN, FTStateActive, FTStateAdmin, FTStateAdminFollowModule, FNMSysTeamGrpId, FTStateStock, FTStateSupervisorStock, FPUserImage, FPUserLicense, UserLabId, GroupLabId, FTIns*/FTUpd*)` — เขียนที่ `DK.SE/User.vb:57-102` (รูปที่ 109-132), ลบที่ `DK.SE/MainSecurity.vb:619-622`
- `TSEUserLoginState(FTUserName, FTLogInIP, FTLogInDate, FTLogInTime, FTLogInCom)` — session ที่ active (`DK.ST/UserInfo.cs:78-120`)
- `TSEUserLoginLogOutHistory(FTUserName, FTIP, FTDate, FTTime, FTCom, FTStateStatus)` — 0 = login (`UserInfo.cs:126-134`), 1 = logout (`DK.App/Main.cs:946-952`)

**Flow (`DK/Program.cs:39-65` → `UserInfo.Login` → `DK.ST/wUserLogIn.cs`)**
1. โหลด config, list, ข้อความระบบ; บริษัท default อ่านจาก registry `HKCU\Software\DKERP` (`Program.cs:56`, `DK.UL/AppRegistry.cs:28`)
2. หน้า login เลือกภาษา (จาก `MSysListData` `FTListName='FNLang'`; `wUserLogIn.cs:35-37`) และบริษัท (`TCNMCmp`: `FNMSysCmpId, FTCmpCode, FTDocRun`; 74-76) → set `CmpID`, `CmpRunID = CmpRunDoc = FTDocRun` (243-246)
3. `VerifyLogin` (253-329): หา user ด้วยชื่อ → `DataDecrypt(FTPassword)` เทียบกับที่พิมพ์ (273) → ต้อง `FTStateActive='1'` (274) → `Admin = FTStateAdmin='1'` (277)
4. ถ้าไม่ใช่ admin ต้องมี role ที่ผูกบริษัทที่เลือก: join `TSEUserLoginPermission` + `TSEPermissionCmp` + `TCNMCmp` (287-297) ไม่มี = เข้าไม่ได้
5. single-session แบบเตือน: ถ้ามีแถวใน `TSEUserLoginState` ถามยืนยันแล้ว overwrite เครื่อง/IP (`UserInfo.cs:84-107`) — เครื่องเดิม**ไม่ถูกเตะออก**; admin ข้ามการเช็ก (76); ปิดโปรแกรมลบแถว state (`Main.cs:936-939`)

**รหัสผ่าน**
- อัลกอริทึม `DataEncrypt` (`Config.cs:286-340`): สลับครึ่งหน้า-หลัง, ใส่ความยาวนำหน้า, ครอบด้วยอักษรคงที่, เลื่อนรหัสอักขระด้วยเลขสุ่ม 1–10, กลับลำดับ, ต่อท้ายอักษรที่ใช้หา shift → ถอดกลับได้โดยไม่ต้องมี key (`DataDecrypt` 225-284)
- ตั้ง/เปลี่ยนรหัส: encrypt แล้ว decrypt ทวนจนกว่าจะตรง (`User.vb:46-51`, `UserChangePassword.vb:50-55`); เปลี่ยนรหัสต้องใส่รหัสเดิมเทียบกับค่าในหน่วยความจำ (`UserChangePassword.vb:6`)
- ข้อจำกัดเดียว: รับเฉพาะอักขระ ASCII 48–122 (`User.vb:288-297`); ไม่มีความยาวขั้นต่ำ, expiry, history, lockout (`LimitLogINTime/CountLogINTime` ประกาศไว้แต่ไม่ถูกใช้; `UserInfo.cs:23-24`)
- หน้าแก้ผู้ใช้ถอดรหัสมาใส่ textbox (`User.vb:265`)
- `DK.UCTR/ucUserLogin.vb` เป็น login แบบ flyout อีกชุด เทียบด้วย `DataEncrypt(ค่าที่เก็บ)` (471) ซึ่งสุ่ม → ใช้ไม่ได้จริง และไม่เช็กบริษัท

**สำหรับระบบใหม่:** ASP.NET Core Identity (PBKDF2/Argon2) + JWT access/refresh, lockout + password policy เป็น tenant setting, 2FA สำหรับผู้อนุมัติ, session/device list ที่ revoke ได้จริง, migrate user โดย**บังคับตั้งรหัสใหม่** (ไม่ย้ายรหัสเดิม), login history เก็บใน `platform.auth_events`

## 3. สิทธิ์ (permission)

**ตาราง**
- metadata (SYSTEM): `MSysModule(FNMSysModuleID/FNPSysModuleID, FTModuleNameTH/EN)`, `MSysMenuGrp(FNMnuGrpID, FTMnuGrpName, FTGrpCaptionTH/EN, FTStateActive)`, `MSysMenu(FNPSysModuleID, FNMnuGrpID, FTMnuName, FNSeq, FTCaptionTH/EN, FTFormName, FTStateActive, …)`, `MSysObjectForm(FTMnuName, FTFormName)`, `MSysObjectControl(FTMnuName, FTFormName, FTObjectName)`
- role (SECURITY): `TSEPermission(FNMSysPermissionID, FTPermissionCode unique, FTPermissionNameTH/EN)` — `DK.SE/Permission.vb:1144-1170`; code ซ้ำไม่ได้ (1217-1222); ต้องมี code + ชื่อ TH + EN (1213-1235)
- `TSEUserLoginPermission(FTUserName, FNMSysPermissionID)` user ↔ role หลายต่อหลาย ลบแล้ว insert ใหม่ (`User.vb:139-154`)
- `TSEPermissionModule` (`Permission.vb:1179-1190` ใน transaction 1149-1202), `TSEPermissionMenu(FNMSysPermissionID, FTMnuName)` (955-980, 1013-1040), `TSEPermissionObjectControl(…, FTMnuName, FTFormName, FTObjName)` (1020-1050)
- ระดับแถว: `TSEPermissionCmp(FNMSysCmpId)` (1117-1135), `TSEPermissionWarehouse(FNMSysWHId)` (1068, 1106-1110), `TSEPermissionMatGrp(FNMSysRawMatGrpId)` (ลบที่ 1065 แต่ insert ถูก comment 1092-1101), HR: `TSEPermissionEmployeeType(FTStateSalary, FTStateAll, FTStateAllUnit)`, `…EmployeeTypeSect`, `…EmployeeTypeUnitSect`
- ลบ role ลบลูก 5 ตาราง (`MainSecurity.vb:660-675`) แต่**ไม่ลบ** Cmp/Warehouse/MatGrp

**การประเมินสิทธิ์**
- **admin (`FTStateAdmin='1'`) ข้ามทุกชั้น** — ทุกฟังก์ชันใน `DK.ST/Security.cs` คืน query เดิมเมื่อ admin
- **โมดูล/เมนู:** แสดงเฉพาะ `MSysModule` / `MSysMenu` ที่ active และ join ได้กับ role ของ user (`DK.App/Main.cs:71-82, 99-107`, `DK.ST/TitleMenu.cs:167-191`) = union ของทุก role; ไม่มี deny
- **ปุ่ม (object):** เปิดฟอร์ม → `SysLanguage.LoadObjectLanguage` เรียก `Security.PermissionObject(MenuName, form)` (`DK.ST/SysLanguage.cs:593, 605`): ปิด `SimpleButton` ทุกปุ่มยกเว้น `ocmexit` (`Security.cs:145-174`) แล้วเปิดเฉพาะปุ่มที่ชื่อ control อยู่ใน `TSEPermissionObjectControl` ของ menu + form นั้น (68-89) — คุมเฉพาะ `SimpleButton` ไม่คุม grid/menu item/shortcut
- รายชื่อปุ่มถูก **ลงทะเบียนอัตโนมัติเมื่อ admin เปิดฟอร์ม** → insert `MSysObjectForm`, `MSysObjectControl` (21-44, 105-142; ข้ามฟอร์มที่ชื่อขึ้นต้น `AddEdit`)
- **บริษัท:** เช็กครั้งเดียวตอน login (ข้อ 2.4); ในเอกสารกรองด้วย `PermissionOrderCmpData` เฉพาะเมนูที่ `HSysMenu.FTFilterPermissionCmp='1'` และอ้างตาราง `TMERTOrder` ของระบบเดิมอีกตัว (`Security.cs:315-360`) → ไม่ได้ใช้กับเอกสาร DK
- **คลัง:** `PermissionWareHouse(where, alias)` ต่อ `FNMSysWHId IN (คลังของ role ที่มีเมนูปัจจุบัน)` (907-947) — เงื่อนไขผูก**ทั้ง user และ `SysInfo.MenuName`**; ใช้ใน lookup ตามชื่อ control (`FNMSysWHId*`, `FTIssueNo`, `FTReceiveNo`, `FTReturnStockNo`, `FTAdjustStockNo`; `DK.TL/HandlerControl.vb:2282-2304, 2811-2829, 5070-5088`) และรายงานคลัง ~30 ตัว (เช่น `DK.INVEN/Report/StockCard.vb:377`, `Transaction/CloseStockMonthly.vb:309`)
- **กลุ่มวัตถุดิบ:** `PermissionRawMatGrp` มี (`Security.cs:871-905`) แต่ทุก call site ถูก comment (`HandlerControl.vb:2290-2292, 2815-2817`) = **ไม่มีผล**
- flag บน user: `FTStateStock`, `FTStateSupervisorStock` (`User.vb:68-69`) — อ่านที่ `DK.INVEN/Transaction/wJobOrder.vb:1577` เท่านั้น
- อนุมัติ: `wApprDialog` เป็นแค่ dialog ยืนยัน ตกลง/ยกเลิก ไม่ถามรหัส (`DK.TL/wApprDialog.vb:11-30`); สิทธิ์อนุมัติ = สิทธิ์เห็นปุ่มอนุมัติ

**สำหรับระบบใหม่:** permission เป็น code คงที่ในโค้ด (`inventory.receive.create`, `…approve`) seed ตอน deploy ไม่ auto-register; role ต่อ tenant ผูก permission; data scope แยกจาก role: `user_scopes(user, scope_type = company|warehouse|item_group, scope_id)` บังคับที่ API (query filter) ไม่ใช่ที่ UI; ไม่มี super-admin ข้าม tenant ในแอปลูกค้า; ขั้นอนุมัติใช้ approval engine + permission แยก

## 4. เลขเอกสาร

**A. `SP_GEN_DOCUMENTNO` (SYSTEM) — ไม่มี definition**
- wrapper: `DK.TL/Document.vb:3-10` (connection ใหม่), 13-20 (ใน `ConnTrans`), สำเนาใน `DK.SO/clsCalsProdPland.vb:3-6`, แบบ 4 parameter `DK.ST/GenDoc.cs:12-23`
- parameter ตามลำดับ: (1) ชื่อ DB หรือชื่อโมดูล, (2) ชื่อตาราง, (3) doc type, (4) `'Y'` = ขอ**รูปแบบ/placeholder** ไม่กินเลข, `''` = ออกเลขจริง, (5) prefix เพิ่ม = `TCNMCmp.FTDocRun` ของบริษัท, (6) วันที่เอกสาร (แปลงเป็น ค.ศ.)
- ตัวอย่าง call site

| เอกสาร | ตาราง | doc type | prefix | ที่มา |
|---|---|---|---|---|
| PR / PO | `SysTableName` ของฟอร์ม | `''` | ไม่ส่ง | `DK.PO/PurchaseRequest.vb:659`, `PurchaseLooupEdit.vb:633` |
| SO | `SysTableName` | `''` | `CmpRunDoc` | `DK.SO/SaleOrder.vb:740` |
| PD | `TPDMProdcutOrder` | `''` | `CmpRunID` | `DK.SO/Production/ProductionOrderList.vb:1109` (ใน transaction), `SaleOrderApproved.vb:609` (นอก) |
| แผนผลิต | `Appointments` | `'0'` / `''` | `''` | `clsCalsProdPland.vb:1481`, `Production/PlanScheduling.vb:381` |
| เบิก | `SysTableName` | `'1'` (RM/FG), `'3'` (bulk) | `_CmpH` / `''` | `DK.INVEN/Transaction/Issue.vb:1053`, `IssueFG.vb:1051`, `Issue_bulk.vb:979` |
| โอนคลัง | `SysTableName` | `'0'`, `'1'` (FG) | `CmpRunDoc` | `TransferWHToWHApprove.vb:768`, `TransferWHToWHFG.vb:649` |
| รับ FG / พาเลท | `SysTableName` | `''`, `'5'` | `CmpRunID` / `CmpRunDoc` | `ReceiveFG.vb:826`, `DK.SO/Packing/ProdOrderFillToTWH.vb:836` |
| SO ขาย (LM) | `TSaleOrder` | index ประเภท SO | `CmpRunID` | `LM.SaleVat/SaleOrder/xSaleOrder.vb:282` |

- กลไกหน้าจอ: กด New → ช่องเลขเอกสารแสดง placeholder (`DK.TL/HandlerControl.vb:2399`); ตอน save ถ้าค่าในช่อง = placeholder ถือเป็นเอกสารใหม่แล้วค่อยออกเลขจริง ไม่งั้นถือเป็นแก้ไขและต้องหาเอกสารเจอ (`DK.PO/PurchaseRequest.vb:636-659`)
- prefix บริษัทหาจาก control บริษัทในฟอร์ม ไม่เจอจึงใช้บริษัทที่ login (`PurchaseRequest.vb:555-560, 625-635`) — ฟอร์มส่ง prefix ไม่เหมือนกัน (ตารางข้างบน)
- concurrency: เลขจริงส่วนใหญ่ออกก่อนเปิด transaction (`PurchaseRequest.vb:659` แล้วเปิด `ConnTrans` ที่ 663) → save ล้มเหลวเลขหาย; การกันเลขซ้ำอยู่ใน SP ทั้งหมด (ดู Gap)

**B. `SP_GEN_BARCODE_NO`** (INVEN) — รับ `CmpRunID` (`DK.SO/Track/ProdOrderMixToWH.vb:457`, `DK.INVEN/Transaction/ReceiveFG.vb:2669`); อีก call ส่ง PD + user และพิมพ์คำสั่งผิดเป็น `exce` (`DK.SO/Track/ProductionOrderActive.vb:395`)

**C. surrogate id `RunID.GetRunNoID(table, field, db)`** (`DK.TL/RunID.vb:6-45`)
- id ยาว 8 หลัก = `YYMM` (ปี ค.ศ. 2 หลัก + เดือน จาก `GETDATE()`) + running 4 หลัก; หา `MAX + 1` ของ id ที่ขึ้นต้นเดือนปัจจุบัน ไม่เจอเริ่ม `0001`
- อ่านด้วย `NOLOCK` ไม่มี lock/retry, คืน `Integer` → **เกิน 9,999 แถวต่อเดือนต่อตาราง = ชน prefix เดือนถัดไป**, สอง client พร้อมกันได้ id ซ้ำ
- `GetRunNoIDCmp` (`DK.SE/RunID.vb:41-71`): นำหน้าด้วยรหัส ASCII ของ 2 ตัวท้าย `CmpCode` + `YYMMDD` + 4 หลัก

**สำหรับระบบใหม่:** `platform.number_sequences(tenant_id, doc_type, scope_key (company/branch), period_key, last_no)` + `number_formats(tenant_id, doc_type, pattern เช่น {prefix}{company}{YY}{MM}-{seq:4}, reset = never|year|month)`; ออกเลขด้วย `UPDATE … RETURNING` ใน transaction เดียวกับ insert เอกสาร; draft ไม่กินเลข (ออกตอน submit) หรือยอมให้เลขข้ามได้ตาม tenant setting; primary key ใช้ identity/UUID ไม่ฝังวันที่

## 5. ภาษา

- 2 ภาษา: `EN = 1`, `TH = 2` (`DK.ST/Lang.cs:14-18`); ค่าที่เลือกเก็บ registry ต่อเครื่อง (`wUserLogIn.cs:204`)
- ป้ายหน้าจอ: `MSysLanguage(FTFormName, FTObjectName, FTLangEN, FTLangTH)` (LANG) — โหลดทั้งฟอร์มครั้งเดียวต่อการเปิด (`DK.ST/SysLanguage.cs:32-36`) แล้วเดินทุก control (label, button, check, group, tab, grid column/band, bar, layout item, tree, pivot, lookup; 92-520) key = ชื่อฟอร์ม + ชื่อ control
- ข้อความ auto-seed: ฟอร์มที่ยังไม่มีแถว หรือ admin เปิดฟอร์ม → insert ทุก control โดยใช้ caption จาก designer เป็นทั้ง TH และ EN (`Lang.cs:36-79`, `SysLanguage.cs:653-666`)
- ข้อความแจ้งเตือน: `MSystemMessasge(FNHSysMessageID, FTMessageTH, FTMessageEN)` โหลด id 1000000001–1000000011 ตอนเริ่ม (`DK.TL/HandlerControl.vb:5354-5392`); ข้อความอื่นอ้างด้วย id ตัวเลขใน `HSystemMessasge` ถ้าไม่มีจะ insert ข้อความ hard-code ภาษาไทยจากโค้ด (`DK.MG/ShowMsg.cs:96-104`)
- เมนู/โมดูล/list: คอลัมน์คู่ `…TH` / `…EN` ในตารางเอง (`MSysMenu.FTCaptionTH/EN`, `MSysListData.FTNameTH/EN`); master data ก็เป็นคอลัมน์คู่ (`FTRawMatNameTH/EN`)
- แก้ caption โมดูล/เมนู/ฟอร์ม/ปุ่มได้จากหน้า security (`DK.SE/ModifyDescciption.vb:81-98`)

**สำหรับระบบใหม่:** UI string เป็นไฟล์ i18n ใน repo (`th.json`, `en.json`) key ตามความหมายไม่ใช่ชื่อ control; ข้อความ error จาก API เป็น code + parameter แปลที่ client; tenant override label ได้ผ่านตาราง `platform.label_overrides` (option); master data เก็บ `name` + `name_en`; ภาษาเป็น preference ของ user

## 6. Dynamic master (หน้าจอ master จาก metadata)

- เมนูที่ `MSysMenu` ระบุ dynamic form = `'2'` เปิด `wDynamicMasterAddEditDynamic` (`DK.App/Main.cs:507-510`) ฟอร์มอื่นโหลด DLL ด้วย reflection จากชื่อ assembly + form ใน `MSysMenu` (552-560)
- metadata (SYSTEM): `MSysTableObjForm(FNFormObjID, FTDynamicFormName, FTBaseName, FTPrefix, FTTableName, FTSortField, FTProcValidateEdit, FTProcSave, FNFormPopUpWidth/Height)` (`DK.TL/wAddEditDynamic.vb:277-288`), field จาก `SP_GET_DYNAMIC_OBJECT_CONTROL(objId)` (298), `MSysObjDynamic_H/_D`, `MSysTTablePK`, `MSysTTablePKRef`, `MSysLayOut` (layout ที่ผู้พัฒนาจัด), lookup: `MSysBrowse`, `MSysBrowseObj`, `MSysBrowseRet`, combo: `MSysListData`
- flag ต่อ field: `FTPK`, `FTValidate='Y'` (required), `FTStaCheckDup='Y'` (unique), `FTStateLockEdit`, `FTStateReadOnly`, `FTStateCopyNotChange`, `FTGenAutoByField` (ประกอบค่าจาก field อื่น), `FTStateDFFocus` (`wAddEditDynamic.vb:407-445, 645`)
- กฎ
  - key เดี่ยวที่ขึ้นต้น `FNMSys` → ออก id ด้วย `RunID.GetRunNoID` (1981)
  - unique: field ที่ flag ทุกตัวรวมกันด้วย AND ต้องไม่ซ้ำกับแถวอื่น (1141-1222)
  - ลบได้เมื่อไม่มีตารางอื่นอ้าง key — หาตารางอ้างอิงจากชื่อคอลัมน์ที่ขึ้นต้นเหมือนกันใน `MSysObjDynamic_D` และจาก `MSysTTablePKRef` (377-404, `CheckNotUsed` 1647-1658)
  - audit column เติมอัตโนมัติ `FTInsUser/FDInsDate/FTInsTime`, `FTUpdUser/FDUpdDate/FTUpdTime` (2101-2105, 2330-2341)
  - hook ต่อฟอร์ม: SP ชื่อใน `FTProcValidateEdit` / `FTProcSave` ถูก exec หลัง validate / save (1569, 1639)
  - กรณีพิเศษ hard-code ตามชื่อตาราง: `TINVENMMaterial` barcode ห้ามซ้ำ (1340-1384) และแก้ราคาเก็บประวัติ `TINVENMMaterialChangePrice` (2212-2245)
- lookup กรองสิทธิ์ตามชื่อ control (ข้อ 3)

**สำหรับระบบใหม่:** ไม่ทำ form generator; master แต่ละตัวเป็น entity + API + หน้าจอ TanStack Table ที่ใช้ component กลางร่วมกัน; custom field ต่อ tenant (ถ้าต้องมี) เก็บ `jsonb` + schema ใน tenant setting; กฎลบใช้ FK จริง + soft delete (`is_active`)

## 7. Audit / log

- ทุกตารางมี stamp คนสร้าง/แก้ + วันที่ + เวลาเป็น **string** (`Convert(varchar(10),GETDATE(),111)` + `114`; เช่น `DK.ST/Security.cs:37`) — ไม่มี before/after
- `MSysCommandLog(FTCommandUser, FDCommandDate, FTCommandTime, FTMnuName, FTFormName, FTCommand)` (LOG): **เก็บ SQL text ทุกคำสั่งที่ execute สำเร็จ** (`DK.Data/Conn.cs:321, 398-421`, `ConnTrans.cs:207, 260-269`) เมนู/ฟอร์มเป็นค่าว่างเสมอ (405-411) → รวมคำสั่ง update รหัสผ่าน (เข้ารหัสแบบย้อนกลับได้)
- login/logout history (ข้อ 2); ประวัติราคา item (ข้อ 6); ไม่มี log การเปลี่ยนสิทธิ์

**สำหรับระบบใหม่:** `created_at/by`, `updated_at/by` เป็น `timestamptz` + user id; audit log ต่อ entity (before/after เป็น jsonb) สำหรับเอกสาร, master, สิทธิ์; ไม่ log ข้อมูลลับ; auth event แยกตาราง

## 8. SP / function / view ที่เกี่ยว (ไม่มี definition ใน repo — ต้องขอ)

- เลข: `SP_GEN_DOCUMENTNO` (SYSTEM), `SP_GEN_BARCODE_NO` (INVEN)
- dynamic: `SP_GET_DYNAMIC_OBJECT_CONTROL`, SP ที่ชื่ออยู่ใน data `MSysTableObjForm.FTProcValidateEdit` / `FTProcSave`
- view: `V_Browse_804`, `V_Browse_805` (อ้างใน comment `DK.SE/User.vb:253-254`) และ view ที่ `MSysBrowse` ชี้ (อยู่ใน data)
- data ที่ต้อง export: `MSysModule`, `MSysMenuGrp`, `MSysMenu`, `MSysObjectForm`, `MSysObjectControl`, `MSysTableObjForm`, `MSysObjDynamic_H/_D`, `MSysListData`, `MSysLanguage`, `MSystemMessasge`, `TSEPermission*`

## Defects ของระบบเดิม (ห้ามยกมา)

- รหัสผ่านเข้ารหัสย้อนกลับได้โดยไม่มี key (`DK.Data/Config.cs:225-340`), แสดงรหัสเดิมในหน้าแก้ผู้ใช้ (`DK.SE/User.vb:265`), เก็บ plain ในหน่วยความจำตลอด session (`DK.ST/UserInfo.cs:16`)
- มีรหัสผ่านตัวอย่างค้างใน comment (`DK/Program.cs:26`) และ connection string ฝังใน config (`DK.SO/app.config:6, 8`); password ของ DB ใน `Database.xml` ใช้อัลกอริทึมเดียวกัน (`Config.cs:68`)
- SQL injection ที่ login: ชื่อผู้ใช้และบริษัทต่อ string ไม่ escape (`DK.ST/wUserLogIn.cs:267, 291-292`, `DK.UCTR/ucUserLogin.vb:464`); SQL concat ทั้งระบบ พึ่ง `Quoted` แทน parameter
- `MSysCommandLog` เก็บ SQL ทุกคำสั่งรวมข้อมูลอ่อนไหว (`DK.Data/Conn.cs:398-421`)
- สิทธิ์บังคับที่ UI เท่านั้น (disable ปุ่ม) client ต่อ DB ตรงด้วย login เดียว; admin ข้ามทุกอย่าง; `catch` ว่างใน `PermissionObject` (`DK.ST/Security.cs:95-99`) — ถ้า query สิทธิ์ล้ม ปุ่มค้างสถานะ disable ทั้งหมดโดยไม่มี error
- สิทธิ์กลุ่มวัตถุดิบไม่ทำงาน (บันทึกและกรองถูก comment); สิทธิ์บริษัทเช็กแค่ตอน login
- บันทึกสิทธิ์เมนู/ปุ่ม/คลัง/บริษัทแบบ delete แล้ว insert ทีละแถว**นอก transaction** (`DK.SE/Permission.vb:947-1000, 1062-1135`); `User.vb:148` insert ไม่ระบุชื่อ DB; ลบ role ทิ้งแถว scope ค้าง
- `RunID.GetRunNoID`: race condition + เพดาน 9,999 ต่อเดือน (`DK.TL/RunID.vb:6-45`)
- เลขเอกสารออกนอก transaction; ฟอร์มส่ง prefix/doc type ไม่สม่ำเสมอ; ชื่อ DB hard-code (`"DK_PROD"`, `[DK_SYSTEM]`; `clsCalsProdPland.vb:1481`, `xSaleOrder.vb:282`)
- `Conn.Excute` คืน `False` ทั้งกรณี error และกรณี 0 แถว และกลืน exception (`DK.Data/Conn.cs:316-329`) — โค้ด master ใช้ค่านี้ตัดสินว่า "ต้อง insert"
- caption กลุ่มเมนูสลับภาษา TH/EN (`DK.ST/TitleMenu.cs:44`); เปลี่ยนภาษาในหน้า login เรียก verify login ทันที (`wUserLogIn.cs:204-209`)
- โหลด DLL จาก path ข้าง exe ตามชื่อใน DB ด้วย reflection (`DK.App/Main.cs:552`) ไม่ตรวจลายเซ็น

## Gap ที่ต้องดู SP definition / data

1. `SP_GEN_DOCUMENTNO`: รูปแบบเลข (prefix ต่อ doc type, ปี พ.ศ./ค.ศ., เดือน, จำนวนหลัก), reset รายปี/เดือน, ตารางที่เก็บ running, ใช้ lock อะไร, parameter 1 (ชื่อ DB vs ชื่อโมดูล) มีผลอย่างไร
2. ความหมาย doc type `'0'`, `'1'`, `'3'`, `'5'` ต่อตาราง และค่าของ `SysDocType` ของแต่ละฟอร์ม
3. `TCNMCmp.FTDocRun` มีค่าอะไร, ต่างกันต่อบริษัท/สาขาไหม, ทำไมบางฟอร์มไม่ส่ง
4. `SP_GEN_BARCODE_NO`: รูปแบบ barcode และ signature ที่ถูกต้อง (1 หรือ 2 parameter)
5. data `MSysMenu`/`MSysMenuGrp`/`MSysModule`: เมนูที่ใช้จริง, ค่า dynamic form, process type
6. data `TSEPermission*`: role ที่ใช้จริงมีกี่ตัว, ใช้ scope คลัง/บริษัทอย่างไร, `TSEPermissionMatGrp` มีข้อมูลไหม
7. `FTStateAdminFollowModule` / `AdminAllModule` ใช้ที่ไหน (ไม่พบการอ่านใน repo), `FTStateStock` / `FTStateSupervisorStock` ความหมายทางธุรกิจ
8. `SP_GET_DYNAMIC_OBJECT_CONTROL` และ data `MSysTableObjForm`: master ตัวไหนเป็น dynamic (ใช้ยืนยัน masters.md), SP validate/save ต่อฟอร์ม
9. `MSysLanguage` / `MSystemMessasge` / `HSystemMessasge`: จำนวนแถว, แปล EN ครบไหม (seed ใส่ค่าเดียวกันทั้ง 2 ภาษา)
10. trigger / constraint ใน DB SECURITY และ SYSTEM (unique บน `FTUserName`, `FTPermissionCode`)
11. `DatabaseVat.xml` / server "Vat": ข้อมูลชุดที่ 2 คืออะไร ต้อง migrate ไหม
12. จำนวน user จริง และมี user ใช้ร่วมกันหลายคนไหม (กระทบ license/seat ของ SaaS)
