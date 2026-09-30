# โมดูลเดิม → โมดูลใหม่

| โมดูลเดิม | ฟอร์ม | ทำอะไร | โมดูลใหม่ (api project) | Web | Mobile | แพ็ก |
|---|---|---|---|---|---|---|
| DK.SE, DK.ST, DK.TL, DK.UCTR | ~25 | login, สิทธิ์ (เมนู/ปุ่ม/แถว), เลขเอกสาร, ภาษา, dynamic master | `Platform` (tenant, identity, approval engine, numbering, i18n) | ผู้ใช้, สิทธิ์, ตั้งค่า | login, เปลี่ยนรหัส | Core |
| DK.MK | 19 | ลูกค้า, supplier, บริษัท/ธนาคาร, price list, promotion, ปฏิทิน/วันหยุด | `Masters` | ทั้งหมด | ค้นลูกค้า (read) | Core |
| DK.PO + DK.MNG | 16 | PR, PO, อนุมัติ, tracking, แผนส่งของ | `Purchasing` | PR, PO, tracking | อนุมัติ PR/PO | Starter |
| DK.INVEN (ไม่รวม w* maintenance) | ~80 | รับ/QC, เบิก FIFO, โอน, คืน, ปรับ, ปิดเดือน, barcode, stock card | `Inventory` (ledger append-only) | location, ปิดเดือน, รายงาน | รับ/เบิก/โอน/คืน/นับ ด้วยสแกน, QC, ค้นบาร์โค้ด, อนุมัติโอน | Starter |
| DK.MRP | 16 | BOM RM/PK multi-level, MRP demand | `Production.Bom`, `Production.Mrp` | BOM, MRP run, ผล → PR | — | Pro |
| DK.SO (SaleOrder*, Production/*, RouteMaster/*, Track/Plan*) | ~45 | SO, อนุมัติ, PD, route/เครื่อง/ปฏิทิน, scheduling Gantt, แผนเบิก/รับ | `Sales.Order`, `Production.Planning` | SO, PD, Gantt, กำลังคน, route master | อนุมัติ SO, รับแผน (plan accept) | Pro |
| DK.SO (Actual/*, Packing/*, Track/ProdOrder*ToWH*, Rpt*) | ~30 | actual mix/fill, ของเสีย, พาเลท, FG approve, yield | `Production.Execution` | dashboard yield, รายงาน | actual mix/fill, พิมพ์ป้าย, พาเลทเข้าคลัง, FG approve | Pro |
| LM.SaleVat + LM.Payroll (ส่วน commission) | ~35 | pre-sale, อนุมัติขาย, packing list, invoice, VAT, คอมมิชชัน | `Sales.Fulfillment`, `Sales.Invoice` | packing, invoice, คอมมิชชัน | pre-order, เช็กสต็อก FG, อนุมัติขาย | Enterprise |
| DK.Lab | 19 | วัตถุดิบ R&D, สูตร, ต้นทุน, project, sample | `Lab` | ทั้งหมด | สถานะ sample | Enterprise |
| DK.ACC | 126 | AR/AP/GL/WHT/งบ | — export เท่านั้น (`Accounting.Export`) | export | อนุมัติจ่าย (ถ้าทำ) | — |
| DK.INVEN w* (maintenance/asset/tire), DK.CWY, LM.Payroll (เงินเดือน) | ~45 | maintenance, เครดิตร้านค้า, incentive | **ตัด** | | | |
| DK.RP | — | Crystal report runner | รายงาน = ตาราง/กราฟบน web + PDF จาก QuestPDF | | | |

## ตารางเดิมที่ควรรู้ (ใช้ตั้งชื่อ entity ใหม่ให้เทียบได้)

| เดิม | ความหมาย | ใหม่ (Postgres) |
|---|---|---|
| TSOTOrder / TSOTOrder_Order | SO header / line | sales.orders / sales.order_lines |
| TPDMBom, TPDMBomFormula, TPDMBomFormulaLineItem | BOM header / formula (batch) / line | production.boms, bom_versions, bom_lines |
| TPDTSOMRP, TPDMMRPDemand* | ผล MRP ต่อ SO / MRP run | production.mrp_runs, mrp_requirements |
| TPDMProdcutOrder | ใบสั่งผลิต (แถว = material line, FTProdRefNo = main PD) | production.work_orders, work_order_materials |
| TPDMRoute, TPDMRouteOperation, TPDMRouteMatchine | routing | production.routings, routing_operations, operation_machines |
| TINVENMMachine, TCNMCalendar(_Detail) | เครื่อง, ปฏิทินกะ | production.machines, work_calendars |
| Appointments, TPDTPlanRef, TPDMPlanMap | แผนบน Gantt (DevExpress), pegging mix↔fill | production.schedule_slots, slot_pegging |
| TPDTProdActual, TPDTProdActualFill_D | actual mix / fill | production.actuals |
| TINVENBarcode(_IN/_OUT) | บาร์โค้ด = lot/หน่วยสต็อก, ledger เข้า/ออก | inventory.lots, inventory.movements |
| TINVENReceive, TINVENIssue, TINVENTransferWH, TINVENAdjustStock*, TINVENReturn* | เอกสารคลัง | inventory.documents (+type) |
| TPURTPurchaseRequest*, TPURTPurchase* | PR, PO | purchasing.requests, purchasing.orders |
| TPORawmaterial, TCNMUnit, TCNMWarehouse(Location) | item master, หน่วย, คลัง/location | masters.items, units, warehouses, locations |
| TCNMCustomer / oMCustomer, TCNMSupplier | ลูกค้า, supplier | masters.customers, suppliers |
| TSEUserLogin, TSEPermission*, MSysMenu | ผู้ใช้, สิทธิ์, เมนู | platform.users, roles, permissions |
