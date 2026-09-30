# Legacy: สิ่งที่ถอดจาก DK-ERP-SYSTEM

Repo เดิม: `https://github.com/brambroza/DK-ERP-SYSTEM` (private) — VB.NET/C# WinForms .NET 4.0, DevExpress 16.1, SQL Server 13 DB, 1 commit (2024-02-03)
โฟลเดอร์นี้เป็น **reference อ่านอย่างเดียว** ห้ามเชื่อมต่อ/import/copy โค้ดจากระบบเดิม

ถ้าต้องถอด logic เพิ่ม ให้ clone repo เดิมไว้นอกโปรเจกต์นี้ (เช่น `~/Project/2026/dk-erp-legacy`) แล้วอ่านฟอร์มที่อ้างถึงใน `form-inventory.md`
ข้ามโฟลเดอร์ซ้ำ: `DK.INVEN - Copy`, `DK.TL/DK.SO`, `DK.MRP/DK.MRP`, `DK.WTY`, `DK.CWTY`, `DK.WYC`, `WindowsApplication1`, ไฟล์ `*_bak`

## ข้อเท็จจริงเกี่ยวกับระบบเดิม

- logic ทั้งหมดอยู่ใน code-behind ของฟอร์ม (~930 ไฟล์) SQL ต่อ string ~10,000 บรรทัด + stored procedure ~40 ตัว
- **SP/view/function ไม่อยู่ใน repo** → logic บางส่วนอ่านไม่ได้จนกว่าจะได้ script (รายการอยู่ท้ายแต่ละไฟล์ logic)
- เมนู/สิทธิ์/ภาษา อยู่ในตาราง `MSysMenu`, `TSEPermission*`, `MSysLanguage` — ไม่มี data ใน repo
- รายงาน Crystal ~170 ตัว ไม่มีไฟล์ .rpt ใน repo
- ฐานข้อมูล: SYSTEM, SECURITY, LANG, LOG, MASTER, HR, PUR, INVEN, PROD, ACC, MAR, MAIL, LAB (+ legacy DB_PAYROLL, LM_SYSTEM, PCLDB, INOVA_SYSTEM)
- ธุรกิจของ DK: โรงงาน OEM เครื่องสำอาง/เคมี — ขั้นผลิต mix (bulk) → fill (บรรจุ) → pack, มี Bulk/PK loss, reweigh, sample, R&D สูตร

## สถานะการถอด logic ต่อ flow

| # | Flow | โมดูลเดิม | ไฟล์ spec | สถานะ | ใช้ในแพ็ก |
|---|---|---|---|---|---|
| 1 | BOM / สูตร (RM, PK, multi-level) | DK.MRP | logic/mrp-planning.md §1 | **done** | Pro |
| 2 | MRP demand + netting | DK.MRP | logic/mrp-planning.md §2 | **done** (มี gap ที่ต้องดู SP) | Pro |
| 3 | SO → ใบสั่งผลิต (PD) + lot split | DK.SO | logic/mrp-planning.md §3 | **done** | Pro |
| 4 | Route / เครื่อง / ปฏิทิน / กำลังคน | DK.SO RouteMaster | logic/mrp-planning.md §4 | **done** | Pro |
| 5 | Scheduling (finite capacity, Gantt) + แผนเบิก/รับวัตถุดิบ | DK.SO Production, Track | logic/mrp-planning.md §5 | **done** | Pro |
| 6 | สถานะ SO/PD/Plan + อนุมัติแผน | DK.SO | logic/mrp-planning.md §6 | **done** | Pro |
| 7 | SO entry + อนุมัติ 2 ขั้น + customer PO → SO | DK.SO SaleOrder*, SP_Create_SaleOrderAuto | logic/sales-order.md | **done** (2026-09-29, อนุมัติจริงมีขั้นเดียว; มี 3 จุดขัดกับ mrp-planning.md — ดูหมายเหตุท้ายไฟล์นั้น) | Pro |
| 8 | PR → PO → อนุมัติ → แผนส่งของ | DK.PO, DK.MNG | logic/purchasing.md | **done** (2026-09-29, มี gap ที่ต้องดู SP) | Starter |
| 9 | รับของ + barcode IN + รอ QC + QC | DK.INVEN Receive*, StockQC* | logic/inventory-receive.md | **done** (2026-09-29, มี gap ที่ต้องดู SP) | Starter |
| 10 | เบิก FIFO ตามบาร์โค้ด (RM/PK/bulk/semi/FG/by SO) | DK.INVEN Issue*, SP_CUT_BARCODE_FIFO | logic/inventory-issue.md | **done** (2026-09-29, มี gap ที่ต้องดู SP) | Starter |
| 11 | โอนคลัง/location + อนุมัติ, คืนคลัง, คืน supplier | DK.INVEN Transfer*, Return* | logic/inventory-transfer.md | **done** (2026-09-29, มี gap ที่ต้องดู SP) | Starter |
| 12 | ปรับสต็อก, นับ, ปิดสต็อกรายเดือน, stock card/on-hand | DK.INVEN AdjustStock, CloseStockMonthly, Report/* | logic/inventory-close.md | **done** (2026-09-29, logic หลักอยู่ใน SP — ต้องขอ script) | Starter |
| 13 | Production actual mix/fill, ของเสีย Bulk/PK, man-hour, yield | DK.SO Actual/*, Track/Rpt* | logic/production-actual.md | **done** (2026-09-29, สูตร yield / man-hour อยู่ใน SP ทั้งหมด — ยังไม่มีสูตรเดิมให้ทำ golden test; loss กรอกมือ) | Pro |
| 14 | พาเลท/บาร์โค้ดเข้าคลัง, FG approve | DK.SO Packing/*, Track/ProdOrder*ToWH*, SP_GEN_BARCODE_NO | logic/production-to-wh.md | **done** (2026-09-29, เข้าคลังได้ 4 ทางที่ไม่เชื่อมกัน — ต้องถามผู้ใช้ว่าใช้ทางไหนจริง; เลขบรรทัดยังตรวจซ้ำไม่ครบ) | Pro |
| 15 | Pre-sale → อนุมัติขาย → packing → invoice → คอมมิชชัน | LM.SaleVat, LM.Payroll | logic/sales-invoice.md | **done** (2026-09-29, ภาพรวม; ตัดสต็อก/ออก invoice อยู่ใน SP — ต้องขอ script; ค่า `State` บางค่าเป็นการอนุมาน) | Enterprise |
| 16 | Lab R&D สูตร/ต้นทุน/sample | DK.Lab | logic/lab.md | **done** (2026-09-29, ภาพรวม; สูตรต้นทุนอยู่ใน SP; ไม่มี flow sample request ในระบบเดิม) | Enterprise |
| 17 | Master: ลูกค้า, supplier, price list, promotion, ปฏิทิน/วันหยุด | DK.MK | logic/masters.md | **done** (2026-09-29, มี gap ที่ต้องดู metadata/SP) | Core |
| 18 | ผู้ใช้/สิทธิ์ (ระดับเมนู/ปุ่ม/แถว: บริษัท, คลัง, กลุ่มวัตถุดิบ), เลขเอกสาร, ภาษา | DK.SE, DK.ST, DK.TL/Document.vb | logic/platform.md | **done** (2026-09-29, มี gap ที่ต้องดู SP) | Core |
| — | บัญชี AR/AP/GL/ภาษี | DK.ACC | — | **ไม่ทำ** (export ให้โปรแกรมบัญชี) | — |
| — | Maintenance/asset (w* forms ใน DK.INVEN), DK.CWY, incentive เซลส์ | — | — | **ตัด** (นอกขอบเขต) | — |

**เกณฑ์ "done":** มี entity/ตารางเดิม, state flow, สูตร/กฎ, SP ที่เกี่ยว, และรายการ gap ที่ต้องดู SP/data

## ไฟล์ในโฟลเดอร์นี้

- `module-map.md` — โมดูลเดิม → โมดูลใหม่ → web/mobile
- `form-inventory.md` — รายชื่อฟอร์มเดิม ~465 ใช้เช็กว่าครอบคลุมครบ
- `logic/mrp-planning.md` — spec BOM/MRP/PD/scheduling
