using System.Net;
using System.Net.Http.Json;
using Mrp.Integration.Tests.Infrastructure;
using Mrp.Inventory.Application;
using Mrp.Masters.Application;
using Mrp.Production.Application;
using Mrp.Production.Domain;
using Mrp.Purchasing.Application;
using Mrp.Purchasing.Domain;
using Mrp.SharedKernel.Domain;

namespace Mrp.Integration.Tests;

[Collection(ApiCollection.Name)]
public sealed class ProductionFlowTests(ApiFixture fixture)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));

    [Fact]
    public async Task Demand_to_mrp_to_purchase_request_and_work_orders_to_issue_and_output()
    {
        var session = await fixture.CreateTenantAsync();
        var data = await Factory.CreateAsync(session.Client);
        var plant = await Plant.CreateAsync(data);

        // Explosion of 2,500 jars uses the active bulk BOM for the second level.
        var explosion = await data.GetAsync<ExplosionDto>($"/api/v1/production/boms/{plant.JarBom.Id}/explode?quantity=2500");
        Assert.Equal(
            [("SM-BULK", 1, 127.5m), ("RM-WATER", 2, 89.25m), ("RM-OIL", 2, 32.83125m), ("PK-JAR", 1, 2525m)],
            explosion.Rows.Select(r => (r.ItemCode, r.Level, r.Quantity)));
        Assert.Equal(["PK-JAR", "RM-OIL", "RM-WATER"], explosion.Materials.Select(m => m.ItemCode));

        // Stock and demand.
        await data.ReceiveAsync(plant.Oil.Id, 100m, Today);
        await data.ReceiveAsync(plant.EmptyJar.Id, 1000m, Today, unitCost: 2m);
        await data.PostAsync<DemandDto>("/api/v1/production/demands", new
        {
            itemId = plant.Jar.Id, quantity = 2500m, dueDate = Today.AddDays(30), referenceNo = "SO-0001",
        });

        var run = await data.PostAsync<MrpRunDto>("/api/v1/production/mrp-runs", new { horizonDays = 60 });
        Assert.Empty(run.Exceptions);
        var planned = run.PlannedOrders.ToDictionary(o => o.ItemCode);
        Assert.Equal((PlannedOrderType.Make, 2500m, Today.AddDays(27), Today.AddDays(30)), Key(planned["FG-JAR"]));
        Assert.Equal((PlannedOrderType.Make, 127.5m, Today.AddDays(25), Today.AddDays(27)), Key(planned["SM-BULK"]));
        Assert.Equal((PlannedOrderType.Buy, 89.25m, Today.AddDays(24), Today.AddDays(25)), Key(planned["RM-WATER"]));
        // Jar: 1000 on hand − 2525 = −1525 → order multiple 1000 → 2000.
        Assert.Equal((PlannedOrderType.Buy, 2000m, Today.AddDays(13), Today.AddDays(27)), Key(planned["PK-JAR"]));
        Assert.False(planned.ContainsKey("RM-OIL"));
        var oil = run.Requirements.Single(r => r.ItemCode == "RM-OIL");
        Assert.Equal((100m, 32.83125m, 0m), (oil.OnHand, oil.Gross, oil.Net));

        // Convert everything: one purchase request with two lines, two work orders.
        var conversion = await data.PostAsync<ConversionResult>($"/api/v1/production/mrp-runs/{run.Id}/convert", new
        {
            plannedOrderIds = run.PlannedOrders.Select(o => o.Id),
        });
        Assert.Equal(["PR", "WO", "WO"], conversion.Documents.Select(d => d.DocumentType).Order());
        var pr = await data.GetAsync<PurchaseRequestDto>($"/api/v1/purchasing/requests/{conversion.Documents.Single(d => d.DocumentType == "PR").Id}");
        Assert.Equal(PurchaseRequestSource.Mrp, pr.Source);
        Assert.Equal(run.DocumentNo, pr.SourceReference);
        Assert.Equal([("PK-JAR", 2000m), ("RM-WATER", 89.25m)], pr.Lines.OrderBy(l => l.ItemCode).Select(l => (l.ItemCode, l.StockQuantity)));

        var twice = await session.Client.PostAsJsonAsync($"/api/v1/production/mrp-runs/{run.Id}/convert", new { plannedOrderIds = new[] { run.PlannedOrders[0].Id } });
        await twice.ShouldBeAsync(HttpStatusCode.Conflict);

        // A second run proposes nothing new: the purchase request and the work orders are counted as supply.
        var second = await data.PostAsync<MrpRunDto>("/api/v1/production/mrp-runs", new { horizonDays = 60 });
        Assert.Empty(second.PlannedOrders);

        // Bulk work order: release, issue materials, receive output.
        var bulkOrderId = conversion.Documents.Where(d => d.DocumentType == "WO")
            .Select(d => d.Id)
            .First(id => data.GetAsync<WorkOrderDto>($"/api/v1/production/work-orders/{id}").Result.ItemCode == "SM-BULK");
        var bulkOrder = await data.GetAsync<WorkOrderDto>($"/api/v1/production/work-orders/{bulkOrderId}");
        Assert.Equal([("RM-WATER", 89.25m), ("RM-OIL", 32.83125m)], bulkOrder.Materials.Select(m => (m.ComponentCode, m.RequiredQuantity)));

        var beforeRelease = await IssueAsync(session.Client, data, bulkOrder, plant.Oil.Id, 10m);
        await beforeRelease.ShouldBeAsync(HttpStatusCode.Conflict);
        Assert.Equal("production.workorder.not_released", await beforeRelease.ProblemCodeAsync());

        await data.PostAsync<WorkOrderDto>($"/api/v1/production/work-orders/{bulkOrder.Id}/release", new { });
        await (await IssueAsync(session.Client, data, bulkOrder, plant.Oil.Id, 30m)).ShouldBeAsync(HttpStatusCode.OK);
        bulkOrder = await data.GetAsync<WorkOrderDto>($"/api/v1/production/work-orders/{bulkOrder.Id}");
        Assert.Equal(WorkOrderStatus.InProgress, bulkOrder.Status);
        Assert.Equal(30m, bulkOrder.Materials.Single(m => m.ComponentCode == "RM-OIL").IssuedQuantity);
        Assert.Equal(2.83125m, bulkOrder.Materials.Single(m => m.ComponentCode == "RM-OIL").OutstandingQuantity);

        var overIssue = await IssueAsync(session.Client, data, bulkOrder, plant.Oil.Id, 3m);
        await overIssue.ShouldBeAsync(HttpStatusCode.UnprocessableEntity);
        Assert.Equal("production.workorder.over_issue", await overIssue.ProblemCodeAsync());
        Assert.Equal(70m, (await data.GetAsync<List<OnHandRow>>($"/api/v1/inventory/on-hand?itemId={plant.Oil.Id}")).Single().Quantity);

        var wrongItem = await IssueAsync(session.Client, data, bulkOrder, plant.EmptyJar.Id, 1m);
        await wrongItem.ShouldBeAsync(HttpStatusCode.UnprocessableEntity);
        Assert.Equal("production.workorder.not_a_material", await wrongItem.ProblemCodeAsync());

        // Output: 100 kg, then the rest; the order completes when the ordered quantity is reached.
        var output = await OutputAsync(data, bulkOrder, 100m);
        bulkOrder = await data.GetAsync<WorkOrderDto>($"/api/v1/production/work-orders/{bulkOrder.Id}");
        Assert.Equal((WorkOrderStatus.InProgress, 100m), (bulkOrder.Status, bulkOrder.ProducedQuantity));
        await OutputAsync(data, bulkOrder, 27.5m);
        bulkOrder = await data.GetAsync<WorkOrderDto>($"/api/v1/production/work-orders/{bulkOrder.Id}");
        Assert.Equal((WorkOrderStatus.Completed, 127.5m), (bulkOrder.Status, bulkOrder.ProducedQuantity));
        Assert.Equal(127.5m, (await data.GetAsync<List<OnHandRow>>($"/api/v1/inventory/on-hand?itemId={plant.Bulk.Id}")).Sum(r => r.Quantity));

        // Voiding the first output reopens the order.
        await data.PostAsync<StockDocumentDto>($"/api/v1/inventory/documents/{output.Id}/void", new { reason = "ชั่งผิด" });
        bulkOrder = await data.GetAsync<WorkOrderDto>($"/api/v1/production/work-orders/{bulkOrder.Id}");
        Assert.Equal((WorkOrderStatus.InProgress, 27.5m), (bulkOrder.Status, bulkOrder.ProducedQuantity));

        var cancel = await session.Client.PostAsJsonAsync($"/api/v1/production/work-orders/{bulkOrder.Id}/cancel", new { reason = "ยกเลิก" });
        await cancel.ShouldBeAsync(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Work_order_with_child_orders_for_semi_finished_components_covers_the_demand()
    {
        var session = await fixture.CreateTenantAsync();
        var data = await Factory.CreateAsync(session.Client);
        var plant = await Plant.CreateAsync(data);
        var demand = await data.PostAsync<DemandDto>("/api/v1/production/demands", new { itemId = plant.Jar.Id, quantity = 1000m, dueDate = Today.AddDays(20) });

        var first = await data.PostAsync<WorkOrderDto>("/api/v1/production/work-orders", new
        {
            itemId = plant.Jar.Id, quantity = 600m, startDate = Today.AddDays(10), dueDate = Today.AddDays(12), demandId = demand.Id, createChildOrders = true,
        });

        var bulkLine = first.Materials.Single(m => m.ComponentCode == "SM-BULK");
        Assert.True(bulkLine.IsMadeInHouse);
        Assert.Equal(30.6m, bulkLine.RequiredQuantity);
        var child = Assert.Single(first.ChildOrders);
        Assert.Equal(bulkLine.ChildWorkOrderId, child.Id);
        Assert.Equal(("SM-BULK", 30.6m, Today.AddDays(8), Today.AddDays(10)), (child.ItemCode, child.Quantity, child.StartDate, child.DueDate));

        var demands = await data.GetAsync<SharedKernel.Web.PagedResult<DemandDto>>("/api/v1/production/demands");
        Assert.Equal((DemandStatus.Open, 600m), (demands.Items.Single().Status, demands.Items.Single().CoveredQuantity));

        // Lot split: a second work order covers the rest.
        var second = await data.PostAsync<WorkOrderDto>("/api/v1/production/work-orders", new
        {
            itemId = plant.Jar.Id, quantity = 400m, startDate = Today.AddDays(14), dueDate = Today.AddDays(16), demandId = demand.Id,
        });
        demands = await data.GetAsync<SharedKernel.Web.PagedResult<DemandDto>>("/api/v1/production/demands");
        Assert.Equal(DemandStatus.Planned, demands.Items.Single().Status);

        await data.PostAsync<WorkOrderDto>($"/api/v1/production/work-orders/{second.Id}/cancel", new { reason = "ลูกค้าเลื่อน" });
        demands = await data.GetAsync<SharedKernel.Web.PagedResult<DemandDto>>("/api/v1/production/demands");
        Assert.Equal((DemandStatus.Open, 600m), (demands.Items.Single().Status, demands.Items.Single().CoveredQuantity));
    }

    [Fact]
    public async Task Only_one_bom_version_is_active_and_cycles_are_refused()
    {
        var session = await fixture.CreateTenantAsync();
        var data = await Factory.CreateAsync(session.Client);
        var plant = await Plant.CreateAsync(data);

        var copy = await data.PostAsync<BomDto>($"/api/v1/production/boms/{plant.JarBom.Id}/copy", new { });
        Assert.Equal((2, BomStatus.Draft), (copy.VersionNo, copy.Status));
        await (await session.Client.PostAsJsonAsync($"/api/v1/production/boms/{copy.Id}/activate", new { })).ShouldBeAsync(HttpStatusCode.Conflict);
        await data.PostAsync<BomDto>($"/api/v1/production/boms/{copy.Id}/approve", new { });
        await data.PostAsync<BomDto>($"/api/v1/production/boms/{copy.Id}/activate", new { });

        var versions = await data.GetAsync<SharedKernel.Web.PagedResult<BomSummary>>($"/api/v1/production/boms?itemId={plant.Jar.Id}");
        Assert.Equal([(2, BomStatus.Active), (1, BomStatus.Superseded)], versions.Items.Select(b => (b.VersionNo, b.Status)));

        var edit = await session.Client.PutAsJsonAsync($"/api/v1/production/boms/{copy.Id}", new
        {
            itemId = plant.Jar.Id, name = "x", batchSize = 1m, lines = new[] { new { componentItemId = plant.Oil.Id, quantity = 1m } },
        });
        await edit.ShouldBeAsync(HttpStatusCode.Conflict);

        // Bulk made from the jar that is made from the bulk.
        var cyclic = await data.PostAsync<BomDto>("/api/v1/production/boms", new
        {
            itemId = plant.Bulk.Id, name = "วนกลับ", batchSize = 100m, lines = new[] { new { componentItemId = plant.Jar.Id, quantity = 1m } },
        });
        await data.PostAsync<BomDto>($"/api/v1/production/boms/{cyclic.Id}/approve", new { });
        var activate = await session.Client.PostAsJsonAsync($"/api/v1/production/boms/{cyclic.Id}/activate", new { });
        await activate.ShouldBeAsync(HttpStatusCode.UnprocessableEntity);
        Assert.Equal("production.bom.cycle", await activate.ProblemCodeAsync());

        var whereUsed = await data.GetAsync<List<WhereUsedRow>>($"/api/v1/production/items/{plant.Bulk.Id}/where-used");
        Assert.Equal([("FG-JAR", 2)], whereUsed.Select(w => (w.ParentItemCode, w.VersionNo)));
    }

    [Fact]
    public async Task Production_data_is_isolated_per_tenant_and_needs_permissions()
    {
        var a = await fixture.CreateTenantAsync();
        var b = await fixture.CreateTenantAsync();
        var dataA = await Factory.CreateAsync(a.Client);
        var plant = await Plant.CreateAsync(dataA);
        var (reader, _, _) = await a.CreateUserAsync("planner-readonly", Permissions.ProductionRead);

        await (await b.Client.GetAsync($"/api/v1/production/boms/{plant.JarBom.Id}")).ShouldBeAsync(HttpStatusCode.NotFound);
        Assert.Empty((await (await b.Client.GetAsync("/api/v1/production/boms")).ReadAsync<SharedKernel.Web.PagedResult<BomSummary>>()).Items);
        await (await reader.GetAsync($"/api/v1/production/boms/{plant.JarBom.Id}")).ShouldBeAsync(HttpStatusCode.OK);
        await (await reader.PostAsJsonAsync("/api/v1/production/mrp-runs", new { horizonDays = 30 })).ShouldBeAsync(HttpStatusCode.Forbidden);
        await (await reader.PostAsJsonAsync($"/api/v1/production/boms/{plant.JarBom.Id}/copy", new { })).ShouldBeAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Cost_roll_up_uses_overhead_setting_and_sales_price_for_margin()
    {
        var session = await fixture.CreateTenantAsync();
        var data = await Factory.CreateAsync(session.Client);
        var plant = await Plant.CreateAsync(data);
        await (await session.Client.PutAsJsonAsync("/api/v1/settings", new[] { new { key = SettingKeys.OverheadPercent, value = 10m } })).ShouldBeAsync(HttpStatusCode.OK);

        // Every item has standard cost 10. Bulk per kg: (0.7 + 0.2575) × 10 × 1.1 = 10.5325.
        // Jar per piece: (0.051 × 10.5325 + 1.01 × 10) × 1.1 = 11.70087325.
        var unpriced = await data.GetAsync<CostBreakdownDto>($"/api/v1/production/costing/items/{plant.Jar.Id}?quantity=1");
        Assert.Equal(11.7009m, unpriced.UnitCost);
        Assert.Null(unpriced.UnitMargin);
        Assert.Contains("NO_PRICE", unpriced.Warnings);
        Assert.Equal(["SM-BULK", "RM-WATER", "RM-OIL", "PK-JAR"], unpriced.Lines.Select(l => l.ItemCode));

        var priced = await session.Client.PutAsJsonAsync($"/api/v1/masters/items/{plant.Jar.Id}", new
        {
            code = plant.Jar.Code, name = plant.Jar.Name, itemType = "FinishedGood", supplyType = "Make", stockUnitId = plant.Jar.StockUnitId,
            leadTimeDays = 3, standardCost = 10m, salesPrice = 18.5m,
        });
        await priced.ShouldBeAsync(HttpStatusCode.OK);

        var summary = await data.GetAsync<List<ItemCostSummary>>("/api/v1/production/costing");
        var jar = summary.Single(s => s.ItemCode == "FG-JAR");
        Assert.Equal((11.7009m, 10m, 18.5m, 6.7991m, 36.75m), (jar.RolledUpUnitCost, jar.StandardCostOnMaster, jar.SalesPrice, jar.UnitMargin, jar.MarginPercent));

        var applied = await data.PostAsync<List<ItemCostSummary>>("/api/v1/production/costing/apply", new { itemIds = new[] { plant.Bulk.Id, plant.Jar.Id } });
        Assert.Equal(10.5325m, applied.Single(s => s.ItemCode == "SM-BULK").StandardCostOnMaster);
        Assert.Equal(11.7009m, applied.Single(s => s.ItemCode == "FG-JAR").StandardCostOnMaster);

        var purchased = await session.Client.PostAsJsonAsync("/api/v1/production/costing/apply", new { itemIds = new[] { plant.Oil.Id } });
        await purchased.ShouldBeAsync(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Schedule_places_child_order_before_parent_and_keeps_locked_slots()
    {
        var session = await fixture.CreateTenantAsync();
        var data = await Factory.CreateAsync(session.Client);
        var plant = await Plant.CreateAsync(data);
        await data.PostAsync<MachineDto>("/api/v1/production/machines", new { code = "MIX-01", name = "เครื่องผสม 1", machineGroup = "MIX", priority = 1 });
        await data.PostAsync<MachineDto>("/api/v1/production/machines", new { code = "FILL-01", name = "สายบรรจุ 1", machineGroup = "FILL", priority = 1 });
        var routing = await session.Client.PutAsJsonAsync($"/api/v1/production/items/{plant.Bulk.Id}/routing", new
        {
            operations = new[] { new { name = "ผสม", machineGroup = "mix", setupMinutes = 30m, minutesPerUnit = 1m } },
        });
        await routing.ShouldBeAsync(HttpStatusCode.OK);
        await (await session.Client.PutAsJsonAsync($"/api/v1/production/items/{plant.Jar.Id}/routing", new
        {
            operations = new[] { new { name = "บรรจุ", machineGroup = "FILL", setupMinutes = 15m, minutesPerUnit = 0.5m } },
        })).ShouldBeAsync(HttpStatusCode.OK);

        var order = await data.PostAsync<WorkOrderDto>("/api/v1/production/work-orders", new
        {
            itemId = plant.Jar.Id, quantity = 100m, startDate = Today.AddDays(3), dueDate = Today.AddDays(10), createChildOrders = true,
        });
        var child = Assert.Single(order.ChildOrders);

        var run = await data.PostAsync<ScheduleRunDto>("/api/v1/production/schedule/run", new { horizonDays = 30 });
        Assert.Equal(2, run.WorkOrderCount);
        Assert.Equal(2, run.SlotCount);
        Assert.Empty(run.Exceptions);
        var childJob = run.Jobs.Single(j => j.WorkOrderId == child.Id);
        var parentJob = run.Jobs.Single(j => j.WorkOrderId == order.Id);
        Assert.True(childJob.EndAt <= parentJob.StartAt);
        Assert.False(parentJob.IsLate);

        var gantt = await data.GetAsync<GanttDto>($"/api/v1/production/schedule?from={Iso(Today)}&to={Iso(Today.AddDays(30))}");
        Assert.Equal(["MIX-01", "FILL-01"], gantt.Slots.OrderBy(s => s.StartAt).Select(s => s.MachineCode));
        var mixSlot = gantt.Slots.Single(s => s.MachineCode == "MIX-01");
        // Bulk: 5.1 kg → 30 + 5.1 = 35.1 → 36 minutes.
        Assert.Equal(36, (int)(mixSlot.EndAt - mixSlot.StartAt).TotalMinutes);

        var locked = await data.PostAsync<ScheduleSlotDto>($"/api/v1/production/schedule/slots/{mixSlot.Id}/lock", new { });
        Assert.Equal(ScheduleSlotStatus.Locked, locked.Status);
        var again = await data.PostAsync<ScheduleRunDto>("/api/v1/production/schedule/run", new { horizonDays = 30 });
        Assert.Equal(1, again.WorkOrderCount);
        var after = await data.GetAsync<List<ScheduleSlotDto>>($"/api/v1/production/schedule/work-orders/{child.Id}");
        Assert.Equal((mixSlot.Id, ScheduleSlotStatus.Locked), (Assert.Single(after).Id, after[0].Status));

        var (reader, _, _) = await session.CreateUserAsync("viewer", Permissions.ProductionRead);
        await (await reader.PostAsJsonAsync("/api/v1/production/schedule/run", new { horizonDays = 30 })).ShouldBeAsync(HttpStatusCode.Forbidden);
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static (PlannedOrderType, decimal, DateOnly, DateOnly) Key(PlannedOrderDto order) =>
        (order.OrderType, order.Quantity, order.ReleaseDate, order.DueDate);

    private static async Task<HttpResponseMessage> IssueAsync(HttpClient client, Factory data, WorkOrderDto order, Guid itemId, decimal quantity)
    {
        var draft = await client.PostAsJsonAsync("/api/v1/inventory/documents", new
        {
            documentType = "Issue", documentDate = Today, warehouseId = data.Main.Id, referenceType = "WO", referenceId = order.Id, referenceNo = order.DocumentNo,
            lines = new[] { new { itemId, quantity } },
        });
        await draft.ShouldBeAsync(HttpStatusCode.OK);
        var document = await draft.ReadAsync<StockDocumentDto>();
        return await client.PostAsJsonAsync($"/api/v1/inventory/documents/{document.Id}/post", new { });
    }

    private static async Task<StockDocumentDto> OutputAsync(Factory data, WorkOrderDto order, decimal quantity)
    {
        var draft = await data.PostAsync<StockDocumentDto>("/api/v1/inventory/documents", new
        {
            documentType = "Receipt", documentDate = Today, warehouseId = data.Main.Id, referenceType = "WO", referenceId = order.Id, referenceNo = order.DocumentNo,
            lines = new[] { new { itemId = order.ItemId, quantity, unitCost = 50m } },
        });
        return await data.PostAsync<StockDocumentDto>($"/api/v1/inventory/documents/{draft.Id}/post", new { });
    }

    /// <summary>A small cream factory: jar of cream made from bulk cream, water, oil and an empty jar.</summary>
    private sealed record Plant(ItemDto Jar, ItemDto Bulk, ItemDto Water, ItemDto Oil, ItemDto EmptyJar, BomDto JarBom, BomDto BulkBom)
    {
        public static async Task<Plant> CreateAsync(Factory data)
        {
            var jar = await ItemAsync(data, "FG-JAR", "FinishedGood", "Make", data.Piece.Id, leadTimeDays: 3);
            var bulk = await ItemAsync(data, "SM-BULK", "Bulk", "Make", data.Kg.Id, leadTimeDays: 2);
            var water = await ItemAsync(data, "RM-WATER", "RawMaterial", "Buy", data.Kg.Id, leadTimeDays: 1);
            var oil = await ItemAsync(data, "RM-OIL", "RawMaterial", "Buy", data.Kg.Id, leadTimeDays: 10);
            var emptyJar = await ItemAsync(data, "PK-JAR", "Packaging", "Buy", data.Piece.Id, leadTimeDays: 14, orderMultiple: 1000m);

            var bulkBom = await ActivateAsync(data, await data.PostAsync<BomDto>("/api/v1/production/boms", new
            {
                itemId = bulk.Id, name = "สูตรครีม A", batchSize = 100m,
                lines = new object[]
                {
                    new { componentItemId = water.Id, quantity = 70m },
                    new { componentItemId = oil.Id, quantity = 25_000m, unitId = data.Gram.Id, lossPercent = 3m },
                },
            }));
            var jarBom = await ActivateAsync(data, await data.PostAsync<BomDto>("/api/v1/production/boms", new
            {
                itemId = jar.Id, name = "บรรจุกระปุก 50 g", batchSize = 1000m,
                lines = new object[]
                {
                    new { componentItemId = bulk.Id, quantity = 50m, lossPercent = 2m },
                    new { componentItemId = emptyJar.Id, quantity = 1000m, lossPercent = 1m },
                },
            }));
            return new Plant(jar, bulk, water, oil, emptyJar, jarBom, bulkBom);
        }

        private static Task<ItemDto> ItemAsync(Factory data, string code, string itemType, string supplyType, Guid unitId, int leadTimeDays, decimal orderMultiple = 0m) =>
            data.PostAsync<ItemDto>("/api/v1/masters/items", new
            {
                code, name = code, itemType, supplyType, stockUnitId = unitId, leadTimeDays, orderMultiple, standardCost = 10m,
            });

        private static async Task<BomDto> ActivateAsync(Factory data, BomDto bom)
        {
            await data.PostAsync<BomDto>($"/api/v1/production/boms/{bom.Id}/approve", new { });
            return await data.PostAsync<BomDto>($"/api/v1/production/boms/{bom.Id}/activate", new { });
        }
    }
}
