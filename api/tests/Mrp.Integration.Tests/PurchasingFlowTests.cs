using System.Net;
using System.Net.Http.Json;
using Mrp.Integration.Tests.Infrastructure;
using Mrp.Inventory.Application;
using Mrp.Platform.Application;
using Mrp.Platform.Domain;
using Mrp.Purchasing.Application;
using Mrp.Purchasing.Domain;
using Mrp.SharedKernel.Domain;
using Mrp.SharedKernel.Web;

namespace Mrp.Integration.Tests;

[Collection(ApiCollection.Name)]
public sealed class PurchasingFlowTests(ApiFixture fixture)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));

    [Fact]
    public async Task Request_to_order_to_two_step_approval_to_receipt()
    {
        var session = await fixture.CreateTenantAsync();
        var data = await Factory.CreateAsync(session.Client);
        var bottle = await data.PackagingAsync(piecesPerBox: 48m);
        var (buyer, _, _) = await session.CreateUserAsync("buyer",
            Permissions.PurchasingRead, Permissions.PurchaseRequestManage, Permissions.PurchaseOrderManage, Permissions.MastersRead);
        var (supervisor, _, supervisorRole) = await session.CreateUserAsync("supervisor", Permissions.PurchasingRead);
        var (manager, _, managerRole) = await session.CreateUserAsync("manager", Permissions.PurchasingRead);
        await (await session.Client.PutAsJsonAsync("/api/v1/approval-routes/PO", new
        {
            name = "อนุมัติ PO", isActive = true,
            steps = new object[]
            {
                new { stepNo = 1, name = "หัวหน้า", roleId = supervisorRole, requireTwoFactor = false },
                new { stepNo = 2, name = "ผู้จัดการ", roleId = managerRole, minAmount = 10000m, requireTwoFactor = false },
            },
        })).ShouldBeAsync(HttpStatusCode.OK);

        // Purchase request: 100 boxes, no approval route for PR so it is approved on submit.
        var pr = await (await buyer.PostAsJsonAsync("/api/v1/purchasing/requests", new
        {
            documentDate = Today, requiredDate = Today.AddDays(14),
            lines = new[] { new { itemId = bottle.Id, quantity = 100m } },
        })).ReadAsync<PurchaseRequestDto>();
        Assert.Equal(data.Box.Id, pr.Lines[0].UnitId);
        Assert.Equal(4800m, pr.Lines[0].StockQuantity);
        pr = await (await buyer.PostAsJsonAsync($"/api/v1/purchasing/requests/{pr.Id}/submit", new { })).ReadAsync<PurchaseRequestDto>();
        Assert.Equal(PurchaseRequestStatus.Approved, pr.Status);

        // Ordering more than requested is refused.
        var tooMuch = await buyer.PostAsJsonAsync("/api/v1/purchasing/orders", new
        {
            documentDate = Today, supplierId = data.Supplier.Id,
            lines = new[] { new { itemId = bottle.Id, quantity = 101m, unitPrice = 120m, prLineId = pr.Lines[0].Id } },
        });
        await tooMuch.ShouldBeAsync(HttpStatusCode.UnprocessableEntity);
        Assert.Equal("purchasing.po.exceeds_request", await tooMuch.ProblemCodeAsync());

        var poResponse = await buyer.PostAsJsonAsync("/api/v1/purchasing/orders", new
        {
            documentDate = Today, supplierId = data.Supplier.Id, deliveryDate = Today.AddDays(7),
            lines = new[] { new { itemId = bottle.Id, quantity = 100m, unitPrice = 120m, discountPercent = 5m, prLineId = pr.Lines[0].Id } },
        });
        await poResponse.ShouldBeAsync(HttpStatusCode.OK);
        var po = await poResponse.ReadAsync<PurchaseOrderDto>();
        Assert.Equal(11400m, po.Subtotal);
        Assert.Equal(798m, po.VatAmount);
        Assert.Equal(12198m, po.Total);
        Assert.Equal(PurchaseRequestStatus.Ordered, (await data.GetAsync<PurchaseRequestDto>($"/api/v1/purchasing/requests/{pr.Id}")).Status);

        po = await (await buyer.PostAsJsonAsync($"/api/v1/purchasing/orders/{po.Id}/submit", new { })).ReadAsync<PurchaseOrderDto>();
        Assert.Equal(PurchaseOrderStatus.Submitted, po.Status);

        // Receiving before approval is refused.
        var early = await data.DraftAsync("Receipt", Today, [new { itemId = bottle.Id, quantity = 10m, unitId = data.Box.Id, poLineId = po.Lines[0].Id }], supplierId: data.Supplier.Id);
        var earlyDraft = await early.ReadAsync<StockDocumentDto>();
        var earlyPost = await session.Client.PostAsJsonAsync($"/api/v1/inventory/documents/{earlyDraft.Id}/post", new { });
        await earlyPost.ShouldBeAsync(HttpStatusCode.Conflict);
        Assert.Equal("inventory.receipt.po_not_open", await earlyPost.ProblemCodeAsync());

        // The manager cannot act before the supervisor.
        Assert.Empty((await (await manager.GetAsync("/api/v1/approvals/inbox")).ReadAsync<PagedResult<ApprovalRequestDto>>()).Items);
        var request = Assert.Single((await (await supervisor.GetAsync("/api/v1/approvals/inbox")).ReadAsync<PagedResult<ApprovalRequestDto>>()).Items);
        Assert.Equal(12198m, request.Amount);
        await (await manager.PostAsJsonAsync($"/api/v1/approvals/{request.Id}/approve", new { })).ShouldBeAsync(HttpStatusCode.Forbidden);
        await (await supervisor.PostAsJsonAsync($"/api/v1/approvals/{request.Id}/approve", new { comment = "ราคาตามใบเสนอ" })).ShouldBeAsync(HttpStatusCode.OK);
        Assert.Equal(PurchaseOrderStatus.Submitted, (await data.GetAsync<PurchaseOrderDto>($"/api/v1/purchasing/orders/{po.Id}")).Status);
        var final = await (await manager.PostAsJsonAsync($"/api/v1/approvals/{request.Id}/approve", new { })).ReadAsync<ApprovalRequestDto>();
        Assert.Equal(ApprovalStatus.Approved, final.Status);
        Assert.Equal(PurchaseOrderStatus.Approved, (await data.GetAsync<PurchaseOrderDto>($"/api/v1/purchasing/orders/{po.Id}")).Status);

        // Partial receipt: 40 boxes; cost per piece comes from the order line (120 × 0.95 ÷ 48).
        var partialDraft = await (await data.DraftAsync("Receipt", Today,
            [new { itemId = bottle.Id, quantity = 40m, unitId = data.Box.Id, poLineId = po.Lines[0].Id }], supplierId: data.Supplier.Id)).ReadAsync<StockDocumentDto>();
        var partial = await data.PostAsync<StockDocumentDto>($"/api/v1/inventory/documents/{partialDraft.Id}/post", new { });
        var lot = await data.GetAsync<LotDto>($"/api/v1/inventory/lots/by-number/{partial.Lines[0].LotNo}");
        Assert.Equal(2.375m, lot.UnitCost);
        Assert.Equal(1920m, lot.OnHand);
        po = await data.GetAsync<PurchaseOrderDto>($"/api/v1/purchasing/orders/{po.Id}");
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, po.Status);
        Assert.Equal(1920m, po.Lines[0].ReceivedStockQuantity);
        Assert.Equal(2880m, po.Lines[0].OutstandingStockQuantity);

        // Over-receipt is blocked at 0% tolerance, allowed within 5%.
        var overDraft = await (await data.DraftAsync("Receipt", Today,
            [new { itemId = bottle.Id, quantity = 62m, unitId = data.Box.Id, poLineId = po.Lines[0].Id }], supplierId: data.Supplier.Id)).ReadAsync<StockDocumentDto>();
        var over = await session.Client.PostAsJsonAsync($"/api/v1/inventory/documents/{overDraft.Id}/post", new { });
        await over.ShouldBeAsync(HttpStatusCode.UnprocessableEntity);
        Assert.Equal("inventory.receipt.over_receive", await over.ProblemCodeAsync());
        await (await session.Client.PutAsJsonAsync("/api/v1/settings", new[] { new { key = SettingKeys.OverReceivePercent, value = 5m } })).ShouldBeAsync(HttpStatusCode.OK);
        await data.PostAsync<StockDocumentDto>($"/api/v1/inventory/documents/{overDraft.Id}/post", new { });
        po = await data.GetAsync<PurchaseOrderDto>($"/api/v1/purchasing/orders/{po.Id}");
        Assert.Equal(PurchaseOrderStatus.Received, po.Status);

        // Voiding the second receipt reopens the order.
        await data.PostAsync<StockDocumentDto>($"/api/v1/inventory/documents/{overDraft.Id}/void", new { reason = "นับผิด" });
        po = await data.GetAsync<PurchaseOrderDto>($"/api/v1/purchasing/orders/{po.Id}");
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, po.Status);
        Assert.Equal(1920m, po.Lines[0].ReceivedStockQuantity);

        // Return 5 boxes of the first lot to the supplier.
        var returnDraft = await (await data.DraftAsync("SupplierReturn", Today,
            [new { itemId = bottle.Id, quantity = 5m, unitId = data.Box.Id, lotId = lot.Id, poLineId = po.Lines[0].Id }], supplierId: data.Supplier.Id)).ReadAsync<StockDocumentDto>();
        await data.PostAsync<StockDocumentDto>($"/api/v1/inventory/documents/{returnDraft.Id}/post", new { });
        po = await data.GetAsync<PurchaseOrderDto>($"/api/v1/purchasing/orders/{po.Id}");
        Assert.Equal(1680m, po.Lines[0].ReceivedStockQuantity);
    }

    [Fact]
    public async Task Rejected_order_returns_to_the_buyer_with_the_reason_and_can_be_resubmitted()
    {
        var session = await fixture.CreateTenantAsync();
        var data = await Factory.CreateAsync(session.Client);
        var item = await data.RawMaterialAsync();
        var (approver, _, role) = await session.CreateUserAsync("approver", Permissions.PurchasingRead);
        await session.Client.PutAsJsonAsync("/api/v1/approval-routes/PO", new
        {
            name = "อนุมัติ PO", isActive = true, steps = new[] { new { stepNo = 1, name = "ผู้จัดการ", roleId = role, requireTwoFactor = false } },
        });
        var po = await data.PostAsync<PurchaseOrderDto>("/api/v1/purchasing/orders", new
        {
            documentDate = Today, supplierId = data.Supplier.Id, lines = new[] { new { itemId = item.Id, quantity = 10m, unitPrice = 99m } },
        });
        await data.PostAsync<PurchaseOrderDto>($"/api/v1/purchasing/orders/{po.Id}/submit", new { });

        // The owner submitted the order, so the owner cannot approve it even with every permission.
        var request = Assert.Single((await (await approver.GetAsync("/api/v1/approvals/inbox")).ReadAsync<PagedResult<ApprovalRequestDto>>()).Items);
        await (await session.Client.PostAsJsonAsync($"/api/v1/approvals/{request.Id}/approve", new { })).ShouldBeAsync(HttpStatusCode.Forbidden);

        await (await approver.PostAsJsonAsync($"/api/v1/approvals/{request.Id}/reject", new { })).ShouldBeAsync(HttpStatusCode.BadRequest);
        await (await approver.PostAsJsonAsync($"/api/v1/approvals/{request.Id}/reject", new { comment = "ขอใบเสนอราคาเพิ่ม" })).ShouldBeAsync(HttpStatusCode.OK);
        po = await data.GetAsync<PurchaseOrderDto>($"/api/v1/purchasing/orders/{po.Id}");
        Assert.Equal(PurchaseOrderStatus.Rejected, po.Status);
        Assert.Equal("ขอใบเสนอราคาเพิ่ม", po.StatusReason);

        var edited = await session.Client.PutAsJsonAsync($"/api/v1/purchasing/orders/{po.Id}", new
        {
            documentDate = Today, supplierId = data.Supplier.Id, lines = new[] { new { itemId = item.Id, quantity = 10m, unitPrice = 95m } },
        });
        await edited.ShouldBeAsync(HttpStatusCode.OK);
        po = await data.PostAsync<PurchaseOrderDto>($"/api/v1/purchasing/orders/{po.Id}/submit", new { });
        Assert.Equal(PurchaseOrderStatus.Submitted, po.Status);
        Assert.Single((await (await approver.GetAsync("/api/v1/approvals/inbox")).ReadAsync<PagedResult<ApprovalRequestDto>>()).Items);
    }

    [Fact]
    public async Task Default_price_comes_from_the_supplier_price_tier()
    {
        var session = await fixture.CreateTenantAsync();
        var data = await Factory.CreateAsync(session.Client);
        var item = await data.RawMaterialAsync();
        foreach (var (minQty, price) in new[] { (0m, 120m), (100m, 110m) })
        {
            await data.PostAsync<Masters.Application.SupplierPriceDto>("/api/v1/masters/supplier-prices", new
            {
                supplierId = data.Supplier.Id, itemId = item.Id, unitId = data.Kg.Id, minQty, unitPrice = price, validFrom = Today.AddDays(-30),
            });
        }

        var po = await data.PostAsync<PurchaseOrderDto>("/api/v1/purchasing/orders", new
        {
            documentDate = Today, supplierId = data.Supplier.Id,
            lines = new object[] { new { itemId = item.Id, quantity = 99m }, new { itemId = item.Id, quantity = 150_000m, unitId = data.Gram.Id } },
        });

        Assert.Equal(120m, po.Lines[0].UnitPrice);
        Assert.Equal(110m, po.Lines[1].UnitPrice);
    }

    [Fact]
    public async Task Orders_of_another_tenant_cannot_be_read_or_received()
    {
        var a = await fixture.CreateTenantAsync();
        var b = await fixture.CreateTenantAsync();
        var dataA = await Factory.CreateAsync(a.Client);
        var dataB = await Factory.CreateAsync(b.Client);
        var itemA = await dataA.RawMaterialAsync();
        var itemB = await dataB.RawMaterialAsync();
        var po = await dataA.PostAsync<PurchaseOrderDto>("/api/v1/purchasing/orders", new
        {
            documentDate = Today, supplierId = dataA.Supplier.Id, lines = new[] { new { itemId = itemA.Id, quantity = 10m, unitPrice = 1m } },
        });
        await dataA.PostAsync<PurchaseOrderDto>($"/api/v1/purchasing/orders/{po.Id}/submit", new { });

        await (await b.Client.GetAsync($"/api/v1/purchasing/orders/{po.Id}")).ShouldBeAsync(HttpStatusCode.NotFound);
        await (await b.Client.PostAsJsonAsync($"/api/v1/purchasing/orders/{po.Id}/cancel", new { reason = "hack" })).ShouldBeAsync(HttpStatusCode.NotFound);
        var receipt = await dataB.DraftAsync("Receipt", Today, [new { itemId = itemB.Id, quantity = 1m, poLineId = po.Lines[0].Id }]);
        await receipt.ShouldBeAsync(HttpStatusCode.BadRequest);
        Assert.Equal("inventory.receipt.po_line_not_found", await receipt.ProblemCodeAsync());
    }
}
