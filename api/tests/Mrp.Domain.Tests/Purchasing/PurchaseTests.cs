using Mrp.Purchasing.Domain;
using Mrp.SharedKernel.Domain;

namespace Mrp.Domain.Tests.Purchasing;

/// <summary>Golden cases for the single definition of purchase amounts.</summary>
public sealed class PurchaseCalculatorTests
{
    [Theory]
    [InlineData(10, 125.50, 0, 1255.00)]
    [InlineData(3, 33.3333, 0, 100.00)]
    [InlineData(7, 14.2857, 5, 95.00)]
    [InlineData(1, 0.005, 0, 0.01)]
    [InlineData(2.5, 199.99, 12.5, 437.48)]
    [InlineData(1000, 0.0449, 0, 44.90)]
    [InlineData(10, 100, 100, 0)]
    public void Line_net_amount_is_rounded_half_away_from_zero(decimal quantity, decimal price, decimal discountPercent, decimal expected)
    {
        Assert.Equal(expected, PurchaseCalculator.LineNetAmount(quantity, price, discountPercent));
    }

    [Fact]
    public void Totals_apply_header_discount_before_vat()
    {
        var totals = PurchaseCalculator.Totals([1255.00m, 100.00m, 95.00m], discountAmount: 50m, vatPercent: 7m);

        Assert.Equal(new PurchaseTotals(1450.00m, 50m, 1400.00m, 98.00m, 1498.00m), totals);
    }

    [Theory]
    [InlineData(999.99, 7, 70.00, 1069.99)]
    [InlineData(0.07, 7, 0.00, 0.07)]
    [InlineData(0.08, 7, 0.01, 0.09)]
    [InlineData(12345.67, 0, 0, 12345.67)]
    public void Vat_is_rounded_to_two_decimals(decimal net, decimal vatPercent, decimal expectedVat, decimal expectedTotal)
    {
        var totals = PurchaseCalculator.Totals([net], 0m, vatPercent);

        Assert.Equal(expectedVat, totals.VatAmount);
        Assert.Equal(expectedTotal, totals.Total);
    }

    [Theory]
    [InlineData(120, 0, 1, 48, 2.5)]
    [InlineData(100, 10, 1, 1, 90)]
    [InlineData(10, 0, 35.25, 1000, 0.3525)]
    [InlineData(1, 0, 1, 3, 0.3333)]
    public void Stock_unit_cost_divides_by_the_conversion_factor(decimal price, decimal discount, decimal rate, decimal factor, decimal expected)
    {
        Assert.Equal(expected, PurchaseCalculator.StockUnitCost(price, discount, rate, factor));
    }
}

public sealed class PurchaseOrderTests
{
    private static readonly Guid Item = Guid.NewGuid();
    private static readonly Guid Unit = Guid.NewGuid();

    [Fact]
    public void Update_calculates_amounts()
    {
        var order = NewOrder(Line(10m, 125.50m), Line(3m, 33.3333m), Line(7m, 14.2857m, discount: 5m));

        Assert.Equal(1450.00m, order.Subtotal);
        Assert.Equal(98.00m, order.VatAmount);
        Assert.Equal(1498.00m, order.Total);
    }

    [Fact]
    public void Foreign_currency_total_is_converted_for_approval_routing()
    {
        var order = new PurchaseOrder("PO-1");
        order.Update(new PurchaseOrderHeader(new DateOnly(2026, 10, 1), Guid.NewGuid(), "usd", 35.5m, 0m, 0m, 30, null, null), [Line(10m, 100m)]);

        Assert.Equal("USD", order.Currency);
        Assert.Equal(35_500m, order.TotalInBaseCurrency);
    }

    [Theory]
    [InlineData("THB", 35)]
    [InlineData("USD", 0)]
    [InlineData("US", 1)]
    public void Invalid_currency_or_rate_is_rejected(string currency, decimal rate)
    {
        var order = new PurchaseOrder("PO-1");

        Assert.Throws<DomainException>(() =>
            order.Update(new PurchaseOrderHeader(new DateOnly(2026, 10, 1), Guid.NewGuid(), currency, rate, 7m, 0m, 30, null, null), [Line(1m, 1m)]));
    }

    [Fact]
    public void Header_discount_cannot_exceed_subtotal()
    {
        var order = new PurchaseOrder("PO-1");

        var error = Assert.Throws<DomainException>(() =>
            order.Update(new PurchaseOrderHeader(new DateOnly(2026, 10, 1), Guid.NewGuid(), "THB", 1m, 7m, 100.01m, 30, null, null), [Line(1m, 100m)]));

        Assert.Equal("purchasing.po.invalid_discount", error.Code);
    }

    [Fact]
    public void Receiving_moves_the_order_through_partial_to_received_and_back()
    {
        var order = Approved(NewOrder(Line(100m, 10m), Line(50m, 10m)));
        var first = order.Lines[0].Id;
        var second = order.Lines[1].Id;

        order.ApplyReceipt(first, 40m);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, order.Status);
        Assert.Equal(60m, order.Lines[0].OutstandingStockQuantity);

        order.ApplyReceipt(first, 60m);
        order.ApplyReceipt(second, 50m);
        Assert.Equal(PurchaseOrderStatus.Received, order.Status);

        order.ApplyReceipt(second, -50m);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, order.Status);

        order.ApplyReceipt(first, -100m);
        Assert.Equal(PurchaseOrderStatus.Approved, order.Status);
    }

    [Fact]
    public void Closing_a_line_completes_the_order_when_the_rest_is_received()
    {
        var order = Approved(NewOrder(Line(100m, 10m), Line(50m, 10m)));
        order.ApplyReceipt(order.Lines[0].Id, 100m);

        order.CloseLine(order.Lines[1].Id);

        Assert.Equal(PurchaseOrderStatus.Received, order.Status);
        Assert.Equal(0m, order.Lines[1].OutstandingStockQuantity);
    }

    [Fact]
    public void Draft_order_cannot_receive_and_returns_cannot_exceed_receipts()
    {
        var draft = NewOrder(Line(10m, 10m));
        Assert.Equal("purchasing.po.not_receivable", Assert.Throws<DomainException>(() => draft.ApplyReceipt(draft.Lines[0].Id, 1m)).Code);

        var order = Approved(NewOrder(Line(10m, 10m)));
        order.ApplyReceipt(order.Lines[0].Id, 4m);
        Assert.Equal("purchasing.po.negative_received", Assert.Throws<DomainException>(() => order.ApplyReceipt(order.Lines[0].Id, -5m)).Code);
    }

    [Fact]
    public void Order_with_receipts_cannot_be_cancelled_but_can_be_closed()
    {
        var order = Approved(NewOrder(Line(10m, 10m)));
        order.ApplyReceipt(order.Lines[0].Id, 4m);

        Assert.ThrowsAny<DomainException>(() => order.Apply(PurchaseAction.Cancel, "ยกเลิก"));

        order.Apply(PurchaseAction.Close, "supplier ส่งไม่ครบ");
        Assert.Equal(PurchaseOrderStatus.Closed, order.Status);
        Assert.All(order.Lines, l => Assert.True(l.IsClosed));
    }

    [Fact]
    public void Approved_order_cannot_be_edited_and_rejected_order_returns_to_draft_on_edit()
    {
        var approved = Approved(NewOrder(Line(10m, 10m)));
        Assert.Equal("purchasing.document.not_editable", Assert.Throws<DomainException>(() => Edit(approved)).Code);

        var rejected = NewOrder(Line(10m, 10m));
        rejected.Apply(PurchaseAction.Submit);
        rejected.Apply(PurchaseAction.Reject, "ราคาสูง");
        Edit(rejected);
        Assert.Equal(PurchaseOrderStatus.Draft, rejected.Status);
    }

    [Fact]
    public void Cancel_and_close_need_a_reason()
    {
        var order = NewOrder(Line(10m, 10m));

        Assert.Equal("purchasing.document.reason_required", Assert.Throws<DomainException>(() => order.Apply(PurchaseAction.Cancel, " ")).Code);
    }

    private static void Edit(PurchaseOrder order) =>
        order.Update(new PurchaseOrderHeader(new DateOnly(2026, 10, 1), Guid.NewGuid(), "THB", 1m, 7m, 0m, 30, null, null), [Line(5m, 10m)]);

    private static PurchaseOrder Approved(PurchaseOrder order)
    {
        order.Apply(PurchaseAction.Submit);
        order.Apply(PurchaseAction.Approve);
        return order;
    }

    private static PurchaseOrder NewOrder(params PurchaseOrderLineData[] lines)
    {
        var order = new PurchaseOrder("PO-2610-0001");
        order.Update(new PurchaseOrderHeader(new DateOnly(2026, 10, 1), Guid.NewGuid(), "THB", 1m, 7m, 50m > lines.Sum(l => l.Quantity * l.UnitPrice) ? 0m : 50m, 30, null, null), lines);
        return order;
    }

    private static PurchaseOrderLineData Line(decimal quantity, decimal price, decimal discount = 0m) =>
        new(Item, Unit, quantity, 1m, quantity, price, discount, null, null, null);
}

public sealed class PurchaseRequestTests
{
    [Fact]
    public void Ordered_quantities_drive_the_status()
    {
        var request = new PurchaseRequest("PR-1", Guid.NewGuid(), PurchaseRequestSource.Manual, null);
        request.Update(new DateOnly(2026, 10, 1), null, null,
        [
            new PurchaseRequestLineData(Guid.NewGuid(), Guid.NewGuid(), 10m, 1m, 10m, null, null, null),
            new PurchaseRequestLineData(Guid.NewGuid(), Guid.NewGuid(), 5m, 1m, 5m, null, null, null),
        ]);
        request.Apply(PurchaseAction.Submit);
        request.Apply(PurchaseAction.Approve);
        var first = request.Lines[0].Id;
        var second = request.Lines[1].Id;

        request.ApplyOrderedQuantities(new Dictionary<Guid, decimal> { [first] = 4m });
        Assert.Equal(PurchaseRequestStatus.PartiallyOrdered, request.Status);

        request.ApplyOrderedQuantities(new Dictionary<Guid, decimal> { [first] = 10m, [second] = 5m });
        Assert.Equal(PurchaseRequestStatus.Ordered, request.Status);

        request.ApplyOrderedQuantities(new Dictionary<Guid, decimal>());
        Assert.Equal(PurchaseRequestStatus.Approved, request.Status);
    }

    [Fact]
    public void Required_date_must_not_precede_document_date()
    {
        var request = new PurchaseRequest("PR-1", Guid.NewGuid(), PurchaseRequestSource.Manual, null);

        Assert.Throws<DomainException>(() => request.Update(new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 9), null, []));
    }

    [Fact]
    public void Empty_request_cannot_be_submitted()
    {
        var request = new PurchaseRequest("PR-1", Guid.NewGuid(), PurchaseRequestSource.Manual, null);
        request.Update(new DateOnly(2026, 10, 10), null, null, []);

        Assert.Equal("purchasing.document.no_lines", Assert.Throws<DomainException>(() => request.Apply(PurchaseAction.Submit)).Code);
    }
}
