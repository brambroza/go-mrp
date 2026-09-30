# DK-ERP-SYSTEM — Form / Screen Inventory (coverage checklist)

Generated 2026-09-29 from the legacy repo. Excluded: *.Designer.vb, *_bak/*_backup, stale copies (DK.TL/DK.SO, DK.MRP/DK.MRP, "DK.INVEN - Copy"), empty projects (DK.WTY, DK.CWTY, DK.WYC, WindowsApplication1).
Format: `path | type | purpose`. (?) = looks unused/test/duplicate. "not in vbproj" = file exists but is not compiled.
Use this as a checklist: every live form must map to a new-system feature, a tenant setting, or an explicit "cut".

Menu metadata is NOT in the repo: it lives in SYSTEM DB tables `MSysMenu` / `MSysMenuGrp`; forms are opened by reflection from `DK.App/Main.cs` (~L541-560). Many master screens are metadata-driven via `DK.TL/wDynamicMaster` (`MSysTableObjForm`) and have no .vb file — this list under-counts masters. Export `MSysMenu` + `MSysTableObjForm` to confirm which screens are live.

## DK.SO — Sales Order & Production Planning (74)
Non-form: CustomerTimeScales.vb, DK_PRODDataSetERP*.vb, DataLookup.vb, clsCalsProdPland.vb (plan engine)
- SaleOrder.vb | transaction | Sale Order entry (TSOTOrder)
- SaleOrderAddItem.vb | utility | Add item popup
- SaleOrderApproved.vb | approval | Customer PO -> SO approval
- SaleOrdertemlist.vb | utility | Add items referencing PO (FG)
- Actual/RouteActual.vb | transaction | บันทึกข้อมูลการผลิต (TPDTProdActual)
- Actual/RouteActualAdd.vb | transaction | Add/edit actual line
- Actual/RouteActualAddFill.vb | transaction | บันทึกข้อมูลบรรจุ
- Actual/RouteActualAddFill - Copy.vb | transaction | (?) copy
- Actual/RouteActualFill.vb | transaction | filling actuals (TPDTProdActualFill_D)
- Actual/RouteActual_.vb | transaction | (?) older per-step actual
- BI/BISaleOrder.vb | report | SO BI pivot
- Master/MachineProdSet.vb | master | product-to-machine-type mix set (TPDMMixSet_H/D)
- Packing/PackingFG.vb | transaction | FG packing
- Packing/ProdOrderFillToTWH.vb | transaction | filled goods into warehouse
- Packing/ProdOrderFillToTWHFGApprove.vb | approval | approve FG receipt from filling
- Production/GenerateProdOrder.vb | transaction | generate production orders (old path)
- Production/Manpawerplan.vb | tracking | manpower plan (stub)
- Production/OutlookAppointmentForm.vb | utility | scheduler appointment editor
- Production/PlanAcceptPopUp.vb | approval | approve plan popup
- Production/PlanScheduling.vb | transaction | Gantt scheduler (Appointments/Resources)
- Production/PlanScheduling-.vb | transaction | (?) older copy
- Production/PlanSchedulingAdd.vb | transaction | add plan
- Production/PlanSchedulingAddManul.vb | transaction | manual mixing plan
- Production/PlanSchedulingAddManulfill.vb | transaction | manual filling plan
- Production/PlanSchedulingCancelMapPlan.vb | transaction | unlink plan map (TPDMPlanMap)
- Production/PlanSchedulingManual.vb | transaction | manual planning
- Production/PlanSchedulingMapPlan.vb | transaction | link mix to fill plans
- Production/PlanSchedulingMapPlanSemi.vb | transaction | manual semi plan linking
- Production/PopUpDateRecals.vb | utility | recalc start-date popup
- Production/ProductionOrder.vb | transaction | Production order (TPDMProdcutOrder)
- Production/ProductionOrderFG.vb | utility | FG list popup
- Production/ProductionOrderList.vb | transaction | PD list/management (main path)
- Production/ProductionOrderListCreateProd.vb | approval | confirm PD creation
- Production/ProductionOrderListEdit.vb | transaction | edit PD detail
- Production/ProductionOrderRepair.vb | transaction | rework/repair PD
- Production/SaleOrderAccept.vb | approval | accept SO into PD
- Production/SaleOrderStatus.vb | tracking | SO production status
- Production/SaleOrderStatusEdit.vb | transaction | edit SO status
- Report/PDChangComponent.vb | transaction | change production component
- Report/ProductionOderActive.vb | tracking | active PDs
- RouteMaster/CalendarMaster.vb | master | working calendar (TCNMCalendar)
- RouteMaster/CalendarMasterAdd.vb | master | add/edit work time
- RouteMaster/MachinePriorityMaster.vb | master | machine priority list
- RouteMaster/MachinePriorityMasterSet.vb | master | set machine group priority
- RouteMaster/ProdMasterRouteTrack.vb | tracking | check product route before production
- RouteMaster/Rawmatmapbom.vb | master | where-used (RM/PK/bulk/semi to BOM)
- RouteMaster/RouteMaster.vb | master | route list
- RouteMaster/RouteMasterSet.vb | master | define routing steps
- RouteMaster/RouteMasterSetCopy.vb | utility | copy routing
- Track/ActualfillTracking.vb | tracking | filling actuals
- Track/ActualmixTracking.vb | tracking | mixing actuals
- Track/PlanAccept.vb | approval | approve production plan
- Track/PlanIssRaw.vb | tracking | RM issue plan (mix)
- Track/PlanIssRawPRD.vb | tracking | RM issue plan (prep)
- Track/PlanIssRawToFill.vb | tracking | PK issue plan (fill)
- Track/PlanRcvRaw.vb | tracking | RM receipt plan (mix)
- Track/PlanRcvRawToFill.vb | tracking | RM receipt plan (fill)
- Track/PlanTracking.vb | tracking | plan tracking
- Track/PlanfillTracking.vb | tracking | fill plan tracking
- Track/PlanmixTracking.vb | tracking | mix plan tracking
- Track/ProdOrderFillToWH.vb | approval | approve filled goods into WH
- Track/ProdOrderFillToWHPallet.vb | tracking | FG pallet handover detail
- Track/ProdOrderMixToWH.vb | approval | approve mixed bulk into WH
- Track/ProductionOrderActive.vb | approval | approve goods into FG WH
- Track/RptDefectMix.vb | report | mixing defects
- Track/RptFillMHProduct.vb | report | filling man-hours
- Track/RptFillProduct.vb | report | filling results
- Track/RptFillProductAsFinish.vb | report | FG delivery results
- Track/RptFillProductOverBaht.vb | report | filling results by value
- Track/RptMixMHProduct.vb | report | mixing man-hours
- Track/RptSumaryMix.vb | report | mixing summary
- Track/SaleOrderTracking.vb | tracking | SO tracking
- Track/SelectPreview.vb | utility | report select/preview
- Track/SelectWH.vb | utility | warehouse picker

## DK.INVEN — Inventory / Warehouse (117: Transaction 62, Report 55)
Non-form: Barcode.vb, Stock.vb. `w*` forms (wBIExpense*, wINVEN*, wJobOrder, wMAPackagePart, tire/repair/machine park) are a maintenance/asset set — CUT.
### Transaction
- AdjustStock.vb | transaction | stock adjust RM/PK
- AdjustStockFG.vb | transaction | stock adjust FG
- BatchSelectPreview.vb | utility | batch select/preview
- ChangeUnitMaster.vb | master | unit conversion (TINVENChangeUnitMaster)
- CloseStockMonthly.vb | utility | monthly stock close
- Issue.vb | transaction | material issue RM/PK
- IssueBySaleOrder.vb | transaction | issue by SO
- IssueFG.vb | transaction | FG issue
- IssueItem.vb | utility | issue item popup
- Issue_bulk.vb | transaction | bulk issue by batch/barcode
- Issue_repair.vb | transaction | issue for repair
- Issue_semi.vb | transaction | semi issue
- issueBulkItem.vb / issueSemiItem.vb | utility | item popups
- PrintCopy.vb | utility | reprint barcode labels
- ProductSet.vb / ProductSetCreate.vb | master/transaction | product set (kit)
- Receive.vb | transaction | goods receive RM/PK (generates barcode)
- ReceiveFG.vb | transaction | FG receive
- ReceiveWaitQC.vb | approval | receipts pending QC
- wAddItemReceive.vb / wReceiveItem.vb | utility | receive item popups
- recievetransferauto.vb | utility | destination WH picker
- RetBulkItem.vb / RetItem.vb / ReturnItem.vb / ReturnListItem.vb | utility | return popups
- ReturnORRefund.vb / ReturnORRefundFG.vb | transaction | customer return or refund
- Return_OR_Refund.vb | transaction | (?) duplicate
- ReturnToFacFG.vb | transaction | return FG to factory
- ReturnToStock.vb / ReturnToStockBulk.vb | transaction | return to stock from production
- ReturnToSupplier.vb / ReturnToSupplierFG.vb | transaction | return to supplier
- wReturnSuplItem.vb | utility | popup
- SearchBarcode.vb | utility | search barcode
- StockOnhandFGAsOf.vb | report | (?, not in vbproj) duplicate
- StockQC.vb / StockQCAddDetail.vb / StockQCBrowse.vb / StockQCBrowseDetail.vb | transaction/tracking | QC inspection
- TransferWHToWH.vb (+Approve, FG, FGApprove, FGRM, RM) | transaction/approval | warehouse transfers
- wTransferWHToWH.vb / wTransferWHToWHApprove.vb | transaction/approval | (w-set) transfer
- wTransferLocation.vb | transaction | location to location
- WHLocation.vb / WHLocationAddEdit.vb / wGenWHLocation.vb | master/utility | warehouse locations
- wGenerateBarcode.vb | utility | generate barcodes
- wScrapBarcode.vb | transaction | scrap
- wStockAdjustAddItem.vb | utility | adjust popup
- wAccCloseStock.vb | utility | close stock (accounting)
- wJobOrder.vb / wCloseJobOrder.vb / wMAPackagePart.vb | — | maintenance (CUT)
- wListErrorImport.vb | utility | import error list
### Report
- AdjustStockTracking(FG).vb, BIIssue(FG).vb, BIReceive(FG).vb, BIReturntoSupplier(FG).vb | report
- IssueFIFOTracking.vb, IssueTracking(FG).vb, ReceiveTracking(FG).vb, ReturnToStockTracking(FG).vb, TransferTracking.vb | report
- ReCheckQC.vb | transaction | QC re-check / expiry revise
- ReceiveReport.vb | report | (?, not in vbproj) monthly RM receipt summary
- ReserveTracking.vb | tracking | stock reservation
- StockCard.vb, StockCardFG.vb, StockCardFGAsOf.vb, StockCardFGjmt.vb (?), StockCardReturnFG.vb | report
- StockOnhand.vb, StockOnhandBatch.vb, StockOnhandBatch_rm.vb, StockOnhandFG.vb, StockOnhandFGAsOf.vb | report
- wBI*.vb, wINVEN*.vb, wInven*Report.vb, wFormListBarcodeTransaction.vb, w*ApproveTracking.vb | report | (w-set, mostly CUT; wInvenImportExcelFile = Excel import)

## DK.PO — Purchasing (13)
- Purchase.vb | transaction | PO entry
- PurchaseAddItem.vb, PurchaseLooupEdit.vb | utility
- PurchaseRequest.vb | transaction | PR
- BIPurchaseOrder.vb, wBIPurchaseOrder.vb, wPurchaseOrderReport.vb, wPurchaseOrderReportSummary.vb | report
- Tracking/PurchaseActive.vb | tracking | open POs
- Tracking/PurchaseApprove.vb | approval | PO approval
- Tracking/PurchaseTracking.vb, PurchaseTrackingSum.vb | tracking
- Tracking/Purchaseplandelivery.vb | tracking | PO delivery plan vs receipts

## DK.MNG — Management Approval (3)
- AprovedMng.vb | approval | cross-document management approval (SP_GETDATASENDAPPROVEDPURCHASE)
- approvemng.vb, Form1.vb | (?) stubs

## DK.MRP — BOM & MRP (16)
Non-form: BomItem.vb, CalMRPSO.vb
- BomListing.vb, BomListingAdd.vb, BomListingAddFormula.vb, BomListingAddItem.vb | master | BOM
- bompk/BomListingPK.vb, BomListingPKAdd.vb | master | packaging BOM
- bomrm/BomListingRM.vb, BomListingRMAdd.vb | master | RM BOM
- MRPAddName.vb | utility | MRP run name
- MRPDemand.vb | transaction | MRP demand (what-if)
- MRPDemandbyPO.vb, MRPDemandbyPOAddItem.vb, MRPDemandbyPOPK.vb, MRPDemandbyPORM.vb | transaction | by customer PO
- MRPDemandbyPRD.vb, MRPDemandbyPRDBulk.vb | transaction | for production (semi/bulk)

## DK.MK — Marketing Masters & Price List (19)
- Master/MAddBank.vb, MAddCompany.vb, MAddDelivery.vb, MAddSale.vb | master | popups
- Master/MCustomer.vb, MCustomerAddEdit.vb, MCustomerTracking.vb | master/tracking | customer
- Master/MSupplier.vb, MSupplierAddEdit.vb | master | supplier
- Master/MWorkingTimeCalendar.vb, MWorkingTimeList.vb, wHoliday.vb, wAddEditHoliday.vb | master | working time / holidays
- Trans/MPriceList.vb, MPriceListVersion.vb, TPriceListSet.vb, TPriceListverstionSet.vb, TPromotion.vb | master/transaction | price list & promotion

## LM.SaleVat — Sales with VAT / Invoicing (26)
- SaleOrder/xSaleOrder.vb, xSaleOrder_TT.vb, xSaleOrderAddItem.vb | transaction | SO (TSaleOrder)
- SaleOrder/xPreSaleOrder.vb, xPreSaleOrder_TT.vb, xPreSaleOrderEdit.vb | approval/transaction | customer PO -> SO
- SaleOrder/xSaleOrder_Inv.vb, xSaleOrder_Pack.vb, xForm.vb (?), xSaleOrder_RET_Pack.vb | transaction/tracking | invoice, packing, returns
- Packing/xPackingList.vb | transaction | packing list
- Tracking/xSaleOrderCom.vb, xSaleOrderComCNbyInv.vb, xSaleOrderCust.vb, xSaleOrderProdSum.vb, xSaleOrderSummary(_TT).vb, xSaleOrderTracking(_TT).vb | tracking/report
- xAddCustPerson.vb, xCustPerson.vb | master | individual customer
- xGenDVat.vb | report | VAT report
- xGenFGtoBom.vb, xGenOrderToBom.vb, xSaleAmtTrack.vb, xTrackCals.vb | utility/tracking | BOM explosion from invoices (reads external AX/PCLDB DBs)

## DK.Lab — R&D / Lab (19)
- RD/MBom.vb, MBomAdmin.vb, MBomDetail.vb | master | formula BOM (Bom_Master/Detail)
- RD/MChangeCode.vb, MExportReweigh.vb | utility
- RD/MFunction.vb, MMapDocuement.vb, MMapFunc.vb, MMapPricelist.vb, MMapQty.vb, MRawmat.vb, MRawmatRead.vb | master | material, properties, files, price, stock
- RD/MSendtomarketing.vb | transaction | send formula to marketing
- Marketing/MCostAdmin.vb, MCostAll.vb, MProject.vb, MProjectReport.vb | master/report/transaction | costing, projects
- OPeration/MCustomer.vb, MSample.vb | master/transaction | lab customer, sample requests

## DK.ACC — Accounting (126) — NOT rebuilt (export only)
AP 21 (APEntry, APPayment, wAPPVEntry, wAPCreditNote/DebitNote, wAPRcvTaxInvoice, wAPApproveGLTransaction, popups) · AP reports/BI 12 (aging, WHT slip 50 ทวิ, monthly tax filing) · AR 23 (ARTInvoiceEntry, ARTBillingSlips, ARTReceivableEntry, ARTReciveInvoice, deposits, browse popups) · AR reports/BI 23 (8 `wARRpt*` not in vbproj) · GL 9 + GL reports 6 (balance sheet, trial balance, ledger) · Master File 28 (chart of accounts, books, periods, WHT/VAT rates) · Other 4

## LM.Payroll — Sales Incentive (23) — CUT
Employee.vb, xEmployee.vb (?), xSalarySet*.vb, xImportSale.vb, xCalcIncentive.vb, xIncentiveCalc.vb, xComTable*.vb, xCurrentTableStatic*.vb, xIncentiveTracking.vb, xIncomeSet*.vb, xPopupGenerateCurrent.vb, xScoreMall.vb, xScoreSale.vb, xBISaleActual.vb, xIncentiveTrackForYear.vb, xIncentiveTrackingForMonth.vb, Form1.vb (?)

## DK.CWY — Customer Shop Credit (6) — CUT
xAaceptInvoice.vb, xCustomerShopCreditBal.vb, xInvoiceWTracking.vb, xReportCustomerShopCreditBal.vb, Form1.vb (?), Master/x.vb (?)

## DK.SE — Security (10)
MainSecurity.vb, ModifyDescciption.vb, Permission.vb, User.vb, UserChangePassword.vb (+5 `w*` duplicates not in vbproj) · Non-form RunID.vb

## DK.RP (0 forms) — Report.vb (Crystal runner, PDF/Excel/email), ListReport.vb (report list per form from DB)
## DK.UCTR (4) — Listing.vb, ICAppCondition.vb, ICCondition.vb, ucUserLogin.vb
## DK.TL — shared (9) — wDynamicMaster*.vb (metadata-driven masters), wAddEditDynamic.vb, wDynamicBrowseInfo.vb, wApprDialog.vb, wWaitDialog.vb, SplashScreen(new).vb · classes: Document.vb (GetDocumentNo), DynamicForm/Grid, HandlerControl, RunID

## Totals
DK.SO 74 · DK.INVEN 117 · DK.PO 13 · DK.MNG 3 · DK.MRP 16 · DK.MK 19 · LM.SaleVat 26 · DK.Lab 19 · DK.ACC 126 · LM.Payroll 23 · DK.CWY 6 · DK.SE 10 · DK.UCTR 4 · DK.TL 9 = ~465
