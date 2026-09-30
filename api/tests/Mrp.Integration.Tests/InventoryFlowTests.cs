using System.Net;
using System.Net.Http.Json;
using Mrp.Integration.Tests.Infrastructure;
using Mrp.Inventory.Application;
using Mrp.Inventory.Domain;
using Mrp.Platform.Application;
using Mrp.SharedKernel.Domain;
using Npgsql;

namespace Mrp.Integration.Tests;

[Collection(ApiCollection.Name)]
public sealed class InventoryFlowTests(ApiFixture fixture)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task Issue_consumes_lots_first_in_first_out_and_updates_on_hand_and_stock_card()
    {
        var session = await fixture.CreateTenantAsync();
        var data = await Factory.CreateAsync(session.Client);
        var item = await data.RawMaterialAsync();
        var first = await data.ReceiveAsync(item.Id, 100m, Today.AddDays(-2), unitCost: 90m, lotNo: "LOT-Z");
        var second = await data.ReceiveAsync(item.Id, 50m, Today.AddDays(-1), unitCost: 110m, lotNo: "LOT-A");

        var draft = await (await data.DraftAsync("Issue", Today, [new { itemId = item.Id, quantity = 120m }])).ReadAsync<StockDocumentDto>();
        var posted = await data.PostAsync<StockDocumentDto>($"/api/v1/inventory/documents/{draft.Id}/post", new { });

        Assert.Equal(StockDocumentStatus.Posted, posted.Status);
        var allocations = posted.Lines.Single().Allocations;
        Assert.Equal([("LOT-Z", 100m), ("LOT-A", 20m)], allocations.Select(a => (a.LotNo, a.Quantity)));

        var onHand = await data.GetAsync<List<OnHandRow>>($"/api/v1/inventory/on-hand?itemId={item.Id}");
        var remaining = Assert.Single(onHand);
        Assert.Equal("LOT-A", remaining.LotNo);
        Assert.Equal(30m, remaining.Quantity);
        Assert.Equal(3300m, remaining.Value);

        var card = await data.GetAsync<StockCardDto>($"/api/v1/inventory/stock-card?itemId={item.Id}&from={Iso(Today.AddDays(-1))}&to={Iso(Today)}");
        Assert.Equal(100m, card.OpeningBalance);
        Assert.Equal(30m, card.ClosingBalance);
        Assert.Equal(3, card.Rows.Count);
        Assert.Equal(150m, card.Rows[0].Balance);
        Assert.Equal(30m, card.Rows[^1].Balance);
        Assert.Equal(second.DocumentNo, card.Rows[0].DocumentNo);
        Assert.NotEqual(first.DocumentNo, second.DocumentNo);
    }

    [Fact]
    public async Task Shortage_rejects_the_whole_document_and_posts_nothing()
    {
        var session = await fixture.CreateTenantAsync();
        var data = await Factory.CreateAsync(session.Client);
        var enough = await data.RawMaterialAsync();
        var short_ = await data.RawMaterialAsync();
        await data.ReceiveAsync(enough.Id, 10m, Today);
        await data.ReceiveAsync(short_.Id, 5m, Today);

        var draft = await (await data.DraftAsync("Issue", Today,
        [
            new { itemId = enough.Id, quantity = 10m },
            new { itemId = short_.Id, quantity = 5.000001m },
        ])).ReadAsync<StockDocumentDto>();
        var response = await session.Client.PostAsJsonAsync($"/api/v1/inventory/documents/{draft.Id}/post", new { });

        await response.ShouldBeAsync(HttpStatusCode.UnprocessableEntity);
        Assert.Equal("inventory.insufficient_stock", await response.ProblemCodeAsync());
        var onHand = await data.GetAsync<List<OnHandRow>>("/api/v1/inventory/on-hand");
        Assert.Equal([10m, 5m], onHand.OrderByDescending(r => r.Quantity).Select(r => r.Quantity));
        var document = await data.GetAsync<StockDocumentDto>($"/api/v1/inventory/documents/{draft.Id}");
        Assert.Equal(StockDocumentStatus.Draft, document.Status);
    }

    [Fact]
    public async Task Receipt_in_purchase_unit_is_stored_in_stock_unit_with_cost_per_stock_unit()
    {
        var session = await fixture.CreateTenantAsync();
        var data = await Factory.CreateAsync(session.Client);
        var bottle = await data.PackagingAsync(piecesPerBox: 48m);

        var receipt = await data.ReceiveAsync(bottle.Id, 2m, Today, unitCost: 120m, unitId: data.Box.Id);

        var line = receipt.Lines.Single();
        Assert.Equal(96m, line.StockQuantity);
        Assert.Equal(48m, line.ConversionFactor);
        var lot = await data.GetAsync<LotDto>($"/api/v1/inventory/lots/by-number/{line.LotNo}");
        Assert.Equal(2.5m, lot.UnitCost);
        Assert.Equal(96m, lot.OnHand);
        Assert.StartsWith("LOT-", lot.LotNo);
    }

    [Fact]
    public async Task Transfer_moves_the_same_lot_between_warehouses()
    {
        var session = await fixture.CreateTenantAsync();
        var data = await Factory.CreateAsync(session.Client);
        var item = await data.RawMaterialAsync();
        await data.ReceiveAsync(item.Id, 40m, Today, lotNo: "LOT-T1");

        var draft = await (await data.DraftAsync("Transfer", Today, [new { itemId = item.Id, quantity = 15m }], toWarehouseId: data.Line.Id)).ReadAsync<StockDocumentDto>();
        await data.PostAsync<StockDocumentDto>($"/api/v1/inventory/documents/{draft.Id}/post", new { });

        var onHand = await data.GetAsync<List<OnHandRow>>($"/api/v1/inventory/on-hand?itemId={item.Id}");
        Assert.Equal(
            [("WH-LINE", "LOT-T1", 15m), ("WH-MAIN", "LOT-T1", 25m)],
            onHand.OrderBy(r => r.WarehouseCode).Select(r => (r.WarehouseCode, r.LotNo, r.Quantity)));
    }

    [Fact]
    public async Task Void_reverses_stock_and_is_blocked_when_the_received_lot_was_already_used()
    {
        var session = await fixture.CreateTenantAsync();
        var data = await Factory.CreateAsync(session.Client);
        var item = await data.RawMaterialAsync();
        var receipt = await data.ReceiveAsync(item.Id, 100m, Today);
        var issueDraft = await (await data.DraftAsync("Issue", Today, [new { itemId = item.Id, quantity = 30m }])).ReadAsync<StockDocumentDto>();
        var issue = await data.PostAsync<StockDocumentDto>($"/api/v1/inventory/documents/{issueDraft.Id}/post", new { });

        var blocked = await session.Client.PostAsJsonAsync($"/api/v1/inventory/documents/{receipt.Id}/void", new { reason = "รับผิดรายการ" });
        await blocked.ShouldBeAsync(HttpStatusCode.UnprocessableEntity);
        Assert.Equal("inventory.insufficient_stock", await blocked.ProblemCodeAsync());

        var voided = await data.PostAsync<StockDocumentDto>($"/api/v1/inventory/documents/{issue.Id}/void", new { reason = "เบิกผิดรายการ" });
        Assert.Equal(StockDocumentStatus.Voided, voided.Status);
        var onHand = await data.GetAsync<List<OnHandRow>>($"/api/v1/inventory/on-hand?itemId={item.Id}");
        Assert.Equal(100m, Assert.Single(onHand).Quantity);

        await data.PostAsync<StockDocumentDto>($"/api/v1/inventory/documents/{receipt.Id}/void", new { reason = "รับผิดรายการ" });
        Assert.Empty(await data.GetAsync<List<OnHandRow>>($"/api/v1/inventory/on-hand?itemId={item.Id}"));

        var again = await session.Client.PostAsJsonAsync($"/api/v1/inventory/documents/{receipt.Id}/void", new { reason = "ซ้ำ" });
        await again.ShouldBeAsync(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Quarantined_lot_cannot_be_issued_until_quality_control_releases_it()
    {
        var session = await fixture.CreateTenantAsync();
        var data = await Factory.CreateAsync(session.Client);
        await session.Client.PutAsJsonAsync("/api/v1/settings", new[] { new { key = SettingKeys.QcOnReceive, value = true } });
        var item = await data.RawMaterialAsync(shelfLifeDays: 365);
        var receipt = await data.ReceiveAsync(item.Id, 20m, Today);
        var lot = await data.GetAsync<LotDto>($"/api/v1/inventory/lots/by-number/{receipt.Lines.Single().LotNo}");
        Assert.Equal(LotQcStatus.Quarantine, lot.QcStatus);
        Assert.Equal(Today.AddDays(365), lot.ExpiryDate);

        var draft = await (await data.DraftAsync("Issue", Today, [new { itemId = item.Id, quantity = 5m }])).ReadAsync<StockDocumentDto>();
        var blocked = await session.Client.PostAsJsonAsync($"/api/v1/inventory/documents/{draft.Id}/post", new { });
        await blocked.ShouldBeAsync(HttpStatusCode.UnprocessableEntity);

        var released = await data.PostAsync<LotDto>($"/api/v1/inventory/lots/{lot.Id}/qc", new { status = "Released", remark = "ผ่าน" });
        Assert.Equal(LotQcStatus.Released, released.QcStatus);

        // The failed attempt left the document approved (no route) but not posted; posting again succeeds.
        var posted = await data.PostAsync<StockDocumentDto>($"/api/v1/inventory/documents/{draft.Id}/post", new { });
        Assert.Equal(StockDocumentStatus.Posted, posted.Status);
    }

    [Fact]
    public async Task Issue_waits_for_approval_when_the_tenant_configured_a_route()
    {
        var session = await fixture.CreateTenantAsync();
        var data = await Factory.CreateAsync(session.Client);
        var (approver, _, approverRole) = await session.CreateUserAsync("wh-head", Permissions.InventoryRead);
        var (clerk, _, _) = await session.CreateUserAsync("wh-clerk", Permissions.InventoryRead, Permissions.InventoryIssue, Permissions.MastersRead);
        var route = await session.Client.PutAsJsonAsync("/api/v1/approval-routes/GI", new
        {
            name = "อนุมัติเบิก", isActive = true,
            steps = new[] { new { stepNo = 1, name = "หัวหน้าคลัง", roleId = approverRole, requireTwoFactor = false } },
        });
        await route.ShouldBeAsync(HttpStatusCode.OK);
        var item = await data.RawMaterialAsync();
        await data.ReceiveAsync(item.Id, 10m, Today);

        var draftResponse = await clerk.PostAsJsonAsync("/api/v1/inventory/documents", new
        {
            documentType = "Issue", documentDate = Today, warehouseId = data.Main.Id, lines = new[] { new { itemId = item.Id, quantity = 4m } },
        });
        await draftResponse.ShouldBeAsync(HttpStatusCode.OK);
        var draft = await draftResponse.ReadAsync<StockDocumentDto>();

        var submitted = await (await clerk.PostAsJsonAsync($"/api/v1/inventory/documents/{draft.Id}/post", new { })).ReadAsync<StockDocumentDto>();
        Assert.Equal(StockDocumentStatus.Submitted, submitted.Status);
        Assert.Equal(10m, (await data.GetAsync<List<OnHandRow>>($"/api/v1/inventory/on-hand?itemId={item.Id}")).Single().Quantity);

        var inbox = await (await approver.GetAsync("/api/v1/approvals/inbox")).ReadAsync<SharedKernel.Web.PagedResult<ApprovalRequestDto>>();
        var request = Assert.Single(inbox.Items);
        Assert.Equal(draft.DocumentNo, request.DocumentNo);
        await (await approver.PostAsJsonAsync($"/api/v1/approvals/{request.Id}/approve", new { comment = "ok" })).ShouldBeAsync(HttpStatusCode.OK);

        var posted = await (await clerk.PostAsJsonAsync($"/api/v1/inventory/documents/{draft.Id}/post", new { })).ReadAsync<StockDocumentDto>();
        Assert.Equal(StockDocumentStatus.Posted, posted.Status);
        Assert.Equal(6m, (await data.GetAsync<List<OnHandRow>>($"/api/v1/inventory/on-hand?itemId={item.Id}")).Single().Quantity);
    }

    [Fact]
    public async Task User_needs_the_permission_of_the_document_type()
    {
        var session = await fixture.CreateTenantAsync();
        var data = await Factory.CreateAsync(session.Client);
        var item = await data.RawMaterialAsync();
        var (issuer, _, _) = await session.CreateUserAsync("issuer", Permissions.InventoryRead, Permissions.InventoryIssue);

        var response = await issuer.PostAsJsonAsync("/api/v1/inventory/documents", new
        {
            documentType = "Receipt", documentDate = Today, warehouseId = data.Main.Id, lines = new[] { new { itemId = item.Id, quantity = 4m } },
        });

        await response.ShouldBeAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Count_posts_the_difference_between_counted_and_system_quantity()
    {
        var session = await fixture.CreateTenantAsync();
        var data = await Factory.CreateAsync(session.Client);
        var item = await data.RawMaterialAsync();
        await data.ReceiveAsync(item.Id, 100m, Today, lotNo: "LOT-C1");
        await data.ReceiveAsync(item.Id, 60m, Today, lotNo: "LOT-C2");

        var sheet = await data.PostAsync<StockDocumentDto>("/api/v1/inventory/documents/count-sheet", new { documentDate = Today, warehouseId = data.Main.Id });
        Assert.Equal([100m, 60m], sheet.Lines.Select(l => l.SystemQuantity));

        var counted = sheet.Lines.Select(l => new { itemId = l.ItemId, lotId = l.LotId, quantity = l.LotNo == "LOT-C1" ? 97.5m : 60m }).ToArray();
        var update = await session.Client.PutAsJsonAsync($"/api/v1/inventory/documents/{sheet.Id}", new
        {
            documentType = "Count", documentDate = Today, warehouseId = data.Main.Id, lines = counted,
        });
        await update.ShouldBeAsync(HttpStatusCode.OK);
        await data.PostAsync<StockDocumentDto>($"/api/v1/inventory/documents/{sheet.Id}/post", new { });

        var onHand = await data.GetAsync<List<OnHandRow>>($"/api/v1/inventory/on-hand?itemId={item.Id}");
        Assert.Equal([("LOT-C1", 97.5m), ("LOT-C2", 60m)], onHand.OrderBy(r => r.LotNo).Select(r => (r.LotNo, r.Quantity)));
    }

    [Fact]
    public async Task Adjustment_needs_a_reason_and_can_go_both_ways()
    {
        var session = await fixture.CreateTenantAsync();
        var data = await Factory.CreateAsync(session.Client);
        var item = await data.RawMaterialAsync(standardCost: 80m);
        await data.ReceiveAsync(item.Id, 10m, Today);

        var missingReason = await data.DraftAsync("Adjustment", Today, [new { itemId = item.Id, quantity = -1m }]);
        await missingReason.ShouldBeAsync(HttpStatusCode.BadRequest);

        var draft = await (await data.DraftAsync("Adjustment", Today,
        [
            new { itemId = item.Id, quantity = -2.5m, reasonCode = "DAMAGED" },
            new { itemId = item.Id, quantity = 4m, reasonCode = "FOUND" },
        ])).ReadAsync<StockDocumentDto>();
        await data.PostAsync<StockDocumentDto>($"/api/v1/inventory/documents/{draft.Id}/post", new { });

        var onHand = await data.GetAsync<List<OnHandRow>>($"/api/v1/inventory/on-hand?itemId={item.Id}");
        Assert.Equal([4m, 7.5m], onHand.Select(r => r.Quantity).Order());
    }

    [Fact]
    public async Task Closed_period_rejects_postings_in_the_application_and_in_the_database()
    {
        var session = await fixture.CreateTenantAsync();
        var data = await Factory.CreateAsync(session.Client);
        var item = await data.RawMaterialAsync();
        var lastMonth = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-1);
        var receipt = await data.ReceiveAsync(item.Id, 100m, lastMonth.AddDays(2));
        var pending = await (await data.DraftAsync("Issue", lastMonth.AddDays(3), [new { itemId = item.Id, quantity = 1m }])).ReadAsync<StockDocumentDto>();

        var withPending = await session.Client.PostAsJsonAsync($"/api/v1/inventory/periods/{lastMonth.Year}/{lastMonth.Month}/close", new { });
        await withPending.ShouldBeAsync(HttpStatusCode.Conflict);
        Assert.Equal("inventory.period.pending_documents", await withPending.ProblemCodeAsync());

        await data.PostAsync<StockDocumentDto>($"/api/v1/inventory/documents/{pending.Id}/post", new { });
        var closed = await data.PostAsync<PeriodDto>($"/api/v1/inventory/periods/{lastMonth.Year}/{lastMonth.Month}/close", new { });
        Assert.Equal(PeriodStatus.Closed, closed.Status);

        var late = await data.DraftAsync("Receipt", lastMonth.AddDays(5), [new { itemId = item.Id, quantity = 1m }]);
        await late.ShouldBeAsync(HttpStatusCode.Conflict);
        Assert.Equal("inventory.period.closed", await late.ProblemCodeAsync());

        await using (var connection = new NpgsqlConnection(fixture.AppConnectionString))
        {
            await connection.OpenAsync();
            await using var setTenant = new NpgsqlCommand($"SELECT set_config('app.tenant_id', '{session.TenantId}', false)", connection);
            await setTenant.ExecuteNonQueryAsync();
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO inventory.movements (id, tenant_id, posting_date, posted_at, movement_type, item_id, lot_id, warehouse_id, quantity, unit_cost, document_type, document_id, document_no)
                SELECT gen_random_uuid(), tenant_id, @date, now(), movement_type, item_id, lot_id, warehouse_id, 1, unit_cost, document_type, document_id, document_no
                FROM inventory.movements LIMIT 1
                """,
                connection);
            insert.Parameters.AddWithValue("date", lastMonth.AddDays(10));
            var error = await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());
            Assert.Contains("is closed", error.MessageText);
        }

        // Voiding a document of the closed month is allowed: the reversal is dated today.
        var reopened = await data.PostAsync<PeriodDto>($"/api/v1/inventory/periods/{lastMonth.Year}/{lastMonth.Month}/reopen", new { reason = "แก้ใบรับ" });
        Assert.Equal(PeriodStatus.Open, reopened.Status);
        await (await data.DraftAsync("Receipt", lastMonth.AddDays(5), [new { itemId = item.Id, quantity = 1m }])).ShouldBeAsync(HttpStatusCode.OK);
        Assert.NotNull(receipt);
    }

    [Fact]
    public async Task Ledger_cannot_be_updated_or_deleted_even_with_direct_database_access()
    {
        var session = await fixture.CreateTenantAsync();
        var data = await Factory.CreateAsync(session.Client);
        var item = await data.RawMaterialAsync();
        await data.ReceiveAsync(item.Id, 10m, Today);

        foreach (var connectionString in new[] { fixture.AppConnectionString, fixture.OwnerConnectionString })
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var setTenant = new NpgsqlCommand($"SELECT set_config('app.tenant_id', '{session.TenantId}', false)", connection);
            await setTenant.ExecuteNonQueryAsync();
            foreach (var sql in new[] { "UPDATE inventory.movements SET quantity = 999", "DELETE FROM inventory.movements", "TRUNCATE inventory.movements" })
            {
                await using var command = new NpgsqlCommand(sql, connection);
                await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            }
        }

        Assert.Equal(10m, (await data.GetAsync<List<OnHandRow>>($"/api/v1/inventory/on-hand?itemId={item.Id}")).Single().Quantity);
    }

    [Fact]
    public async Task Concurrent_issues_never_oversell_a_lot()
    {
        var session = await fixture.CreateTenantAsync();
        var data = await Factory.CreateAsync(session.Client);
        var item = await data.RawMaterialAsync();
        await data.ReceiveAsync(item.Id, 10m, Today);
        var drafts = new List<StockDocumentDto>();
        for (var index = 0; index < 6; index++)
        {
            drafts.Add(await (await data.DraftAsync("Issue", Today, [new { itemId = item.Id, quantity = 3m }])).ReadAsync<StockDocumentDto>());
        }

        var responses = await Task.WhenAll(drafts.Select(d => session.Client.PostAsJsonAsync($"/api/v1/inventory/documents/{d.Id}/post", new { })));

        Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity));
        Assert.Equal(1m, (await data.GetAsync<List<OnHandRow>>($"/api/v1/inventory/on-hand?itemId={item.Id}")).Single().Quantity);
    }

    [Fact]
    public async Task Stock_of_one_tenant_is_invisible_to_another()
    {
        var a = await fixture.CreateTenantAsync();
        var b = await fixture.CreateTenantAsync();
        var dataA = await Factory.CreateAsync(a.Client);
        var dataB = await Factory.CreateAsync(b.Client);
        var item = await dataA.RawMaterialAsync();
        var receipt = await dataA.ReceiveAsync(item.Id, 10m, Today, lotNo: "LOT-SECRET");

        Assert.Empty(await dataB.GetAsync<List<OnHandRow>>("/api/v1/inventory/on-hand"));
        await (await b.Client.GetAsync($"/api/v1/inventory/documents/{receipt.Id}")).ShouldBeAsync(HttpStatusCode.NotFound);
        await (await b.Client.GetAsync("/api/v1/inventory/lots/by-number/LOT-SECRET")).ShouldBeAsync(HttpStatusCode.NotFound);
        var foreignItem = await dataB.DraftAsync("Receipt", Today, [new { itemId = item.Id, quantity = 1m }]);
        await foreignItem.ShouldBeAsync(HttpStatusCode.BadRequest);
    }
}
