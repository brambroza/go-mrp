using Mrp.SharedKernel.Domain;

namespace Mrp.Purchasing.Domain;

/// <summary>State of a purchase order.</summary>
public enum PurchaseOrderStatus
{
    /// <summary>Editable.</summary>
    Draft,

    /// <summary>Waiting for approval.</summary>
    Submitted,

    /// <summary>Approved; goods may be received.</summary>
    Approved,

    /// <summary>Rejected; may be revised.</summary>
    Rejected,

    /// <summary>Some quantity received.</summary>
    PartiallyReceived,

    /// <summary>Every line received or closed.</summary>
    Received,

    /// <summary>Remaining quantity will not be received.</summary>
    Closed,

    /// <summary>Cancelled before receiving.</summary>
    Cancelled,
}

/// <summary>Header values of a purchase order.</summary>
public sealed record PurchaseOrderHeader(
    DateOnly DocumentDate, Guid SupplierId, string Currency, decimal ExchangeRate, decimal VatPercent, decimal DiscountAmount,
    int CreditDays, DateOnly? DeliveryDate, string? Remark);

/// <summary>Line values of a purchase order.</summary>
public sealed record PurchaseOrderLineData(
    Guid ItemId, Guid UnitId, decimal Quantity, decimal ConversionFactor, decimal StockQuantity, decimal UnitPrice,
    decimal DiscountPercent, DateOnly? DeliveryDate, Guid? PrLineId, string? Remark);

/// <summary>Purchase order.</summary>
public sealed class PurchaseOrder : Entity
{
    private static readonly StateMachine<PurchaseOrderStatus, PurchaseAction> Machine =
        new StateMachine<PurchaseOrderStatus, PurchaseAction>()
            .Permit(PurchaseOrderStatus.Draft, PurchaseAction.Submit, PurchaseOrderStatus.Submitted)
            .Permit(PurchaseOrderStatus.Submitted, PurchaseAction.Approve, PurchaseOrderStatus.Approved)
            .Permit(PurchaseOrderStatus.Submitted, PurchaseAction.Reject, PurchaseOrderStatus.Rejected)
            .Permit(PurchaseOrderStatus.Submitted, PurchaseAction.Revise, PurchaseOrderStatus.Draft)
            .Permit(PurchaseOrderStatus.Rejected, PurchaseAction.Revise, PurchaseOrderStatus.Draft)
            .Permit(PurchaseOrderStatus.Approved, PurchaseAction.Progress, PurchaseOrderStatus.PartiallyReceived)
            .Permit(PurchaseOrderStatus.Approved, PurchaseAction.Complete, PurchaseOrderStatus.Received)
            .Permit(PurchaseOrderStatus.Approved, PurchaseAction.Regress, PurchaseOrderStatus.Approved)
            .Permit(PurchaseOrderStatus.PartiallyReceived, PurchaseAction.Progress, PurchaseOrderStatus.PartiallyReceived)
            .Permit(PurchaseOrderStatus.PartiallyReceived, PurchaseAction.Complete, PurchaseOrderStatus.Received)
            .Permit(PurchaseOrderStatus.PartiallyReceived, PurchaseAction.Regress, PurchaseOrderStatus.Approved)
            .Permit(PurchaseOrderStatus.Received, PurchaseAction.Progress, PurchaseOrderStatus.PartiallyReceived)
            .Permit(PurchaseOrderStatus.Received, PurchaseAction.Regress, PurchaseOrderStatus.Approved)
            .Permit(PurchaseOrderStatus.Received, PurchaseAction.Complete, PurchaseOrderStatus.Received)
            .Permit(PurchaseOrderStatus.Approved, PurchaseAction.Close, PurchaseOrderStatus.Closed)
            .Permit(PurchaseOrderStatus.PartiallyReceived, PurchaseAction.Close, PurchaseOrderStatus.Closed)
            .Permit(PurchaseOrderStatus.Draft, PurchaseAction.Cancel, PurchaseOrderStatus.Cancelled)
            .Permit(PurchaseOrderStatus.Rejected, PurchaseAction.Cancel, PurchaseOrderStatus.Cancelled)
            .Permit(PurchaseOrderStatus.Approved, PurchaseAction.Cancel, PurchaseOrderStatus.Cancelled);

    private readonly List<PurchaseOrderLine> _lines = [];

    private PurchaseOrder()
    {
    }

    /// <summary>Creates a draft order.</summary>
    public PurchaseOrder(string documentNo)
    {
        DocumentNo = documentNo;
    }

    /// <summary>Document number.</summary>
    public string DocumentNo { get; private set; } = string.Empty;

    /// <summary>Document date.</summary>
    public DateOnly DocumentDate { get; private set; }

    /// <summary>Supplier.</summary>
    public Guid SupplierId { get; private set; }

    /// <summary>State.</summary>
    public PurchaseOrderStatus Status { get; private set; } = PurchaseOrderStatus.Draft;

    /// <summary>ISO 4217 currency.</summary>
    public string Currency { get; private set; } = "THB";

    /// <summary>Rate to the tenant currency at document date.</summary>
    public decimal ExchangeRate { get; private set; } = 1m;

    /// <summary>VAT percent.</summary>
    public decimal VatPercent { get; private set; }

    /// <summary>Header discount.</summary>
    public decimal DiscountAmount { get; private set; }

    /// <summary>Sum of line net amounts.</summary>
    public decimal Subtotal { get; private set; }

    /// <summary>VAT amount.</summary>
    public decimal VatAmount { get; private set; }

    /// <summary>Grand total.</summary>
    public decimal Total { get; private set; }

    /// <summary>Payment term in days.</summary>
    public int CreditDays { get; private set; }

    /// <summary>Expected delivery date.</summary>
    public DateOnly? DeliveryDate { get; private set; }

    /// <summary>Remark.</summary>
    public string? Remark { get; private set; }

    /// <summary>Reason of rejection, cancellation or closing.</summary>
    public string? StatusReason { get; private set; }

    /// <summary>Optimistic concurrency token.</summary>
    public uint Version { get; private set; }

    /// <summary>Lines.</summary>
    public IReadOnlyList<PurchaseOrderLine> Lines => _lines;

    /// <summary>Whether header and lines may be changed.</summary>
    public bool IsEditable => Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.Rejected;

    /// <summary>Whether goods may be received against the order.</summary>
    public bool IsReceivable => Status is PurchaseOrderStatus.Approved or PurchaseOrderStatus.PartiallyReceived;

    /// <summary>Whether the order still counts as ordered quantity for purchase requests and MRP.</summary>
    public bool CountsAsOrdered => Status is not (PurchaseOrderStatus.Cancelled or PurchaseOrderStatus.Rejected);

    /// <summary>Total in the tenant currency, used for approval routing.</summary>
    public decimal TotalInBaseCurrency => PurchaseCalculator.RoundMoney(Total * ExchangeRate);

    /// <summary>Replaces header and lines and recalculates the amounts.</summary>
    public void Update(PurchaseOrderHeader header, IReadOnlyList<PurchaseOrderLineData> lines)
    {
        if (!IsEditable)
        {
            throw DomainException.Conflict("purchasing.document.not_editable", $"An order that is {Status} cannot be edited.");
        }

        var currency = (header.Currency ?? string.Empty).Trim().ToUpperInvariant();
        if (currency.Length != 3)
        {
            throw new DomainException("purchasing.po.invalid_currency", "Currency must be a 3-letter ISO code.", 400);
        }

        if (header.ExchangeRate <= 0 || (currency == "THB" && header.ExchangeRate != 1m))
        {
            throw new DomainException("purchasing.po.invalid_exchange_rate", "Exchange rate must be positive, and 1 for THB.", 400);
        }

        if (header.VatPercent is < 0 or > 100 || header.CreditDays is < 0 or > 365)
        {
            throw new DomainException("purchasing.po.invalid_terms", "VAT percent or credit days are out of range.", 400);
        }

        _lines.Clear();
        var number = 0;
        foreach (var line in lines)
        {
            number++;
            if (line.Quantity <= 0 || line.StockQuantity <= 0 || line.ConversionFactor <= 0)
            {
                throw new DomainException("purchasing.line.invalid_quantity", $"Line {number}: quantity must be greater than zero.", 400);
            }

            if (line.UnitPrice < 0 || line.DiscountPercent is < 0 or > 100)
            {
                throw new DomainException("purchasing.line.invalid_price", $"Line {number}: price or discount is out of range.", 400);
            }

            _lines.Add(new PurchaseOrderLine(number, line));
        }

        var totals = PurchaseCalculator.Totals(_lines.Select(l => l.NetAmount), header.DiscountAmount, header.VatPercent);
        if (header.DiscountAmount < 0 || header.DiscountAmount > totals.Subtotal)
        {
            throw new DomainException("purchasing.po.invalid_discount", "Discount must be between 0 and the subtotal.", 400);
        }

        DocumentDate = header.DocumentDate;
        SupplierId = header.SupplierId;
        Currency = currency;
        ExchangeRate = header.ExchangeRate;
        VatPercent = header.VatPercent;
        DiscountAmount = header.DiscountAmount;
        CreditDays = header.CreditDays;
        DeliveryDate = header.DeliveryDate;
        Remark = Text.Clean(header.Remark, 500);
        Subtotal = totals.Subtotal;
        VatAmount = totals.VatAmount;
        Total = totals.Total;

        if (Status == PurchaseOrderStatus.Rejected)
        {
            Status = Machine.Fire(Status, PurchaseAction.Revise);
        }
    }

    /// <summary>Applies an action of the state machine.</summary>
    public void Apply(PurchaseAction action, string? reason = null)
    {
        if (action == PurchaseAction.Submit && _lines.Count == 0)
        {
            throw new DomainException("purchasing.document.no_lines", "The document has no lines.");
        }

        if (action is PurchaseAction.Cancel or PurchaseAction.Close && string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("purchasing.document.reason_required", "A reason is required.", 400);
        }

        if (action == PurchaseAction.Cancel && _lines.Any(l => l.ReceivedStockQuantity != 0))
        {
            throw DomainException.Conflict("purchasing.po.has_receipts", "An order with receipts cannot be cancelled; close it instead.");
        }

        Status = Machine.Fire(Status, action);
        if (action is PurchaseAction.Cancel or PurchaseAction.Close or PurchaseAction.Reject)
        {
            StatusReason = Text.Clean(reason, 500);
        }

        if (action == PurchaseAction.Close)
        {
            foreach (var line in _lines)
            {
                line.Close();
            }
        }
    }

    /// <summary>Adds received quantity (negative for returns and voids) to a line and updates the status.</summary>
    public void ApplyReceipt(Guid lineId, decimal stockQuantity)
    {
        var line = _lines.FirstOrDefault(l => l.Id == lineId)
            ?? throw DomainException.NotFound("Purchase order line", lineId);
        if (stockQuantity > 0 && (!IsReceivable || line.IsClosed))
        {
            throw DomainException.Conflict("purchasing.po.not_receivable", $"Purchase order {DocumentNo} is not open for receiving.");
        }

        line.AddReceived(stockQuantity);
        RefreshReceiveStatus();
    }

    /// <summary>Closes one line so its outstanding quantity is no longer expected.</summary>
    public void CloseLine(Guid lineId)
    {
        if (!IsReceivable)
        {
            throw DomainException.Conflict("purchasing.po.not_receivable", $"Purchase order {DocumentNo} is not open.");
        }

        var line = _lines.FirstOrDefault(l => l.Id == lineId)
            ?? throw DomainException.NotFound("Purchase order line", lineId);
        line.Close();
        RefreshReceiveStatus();
    }

    private void RefreshReceiveStatus()
    {
        if (Status is not (PurchaseOrderStatus.Approved or PurchaseOrderStatus.PartiallyReceived or PurchaseOrderStatus.Received))
        {
            return;
        }

        var any = _lines.Any(l => l.ReceivedStockQuantity > 0);
        var all = _lines.All(l => l.IsClosed || l.ReceivedStockQuantity >= l.StockQuantity) && (any || _lines.All(l => l.IsClosed));
        var action = all ? PurchaseAction.Complete : any ? PurchaseAction.Progress : PurchaseAction.Regress;
        Status = Machine.Fire(Status, action);
    }
}

/// <summary>Line of a purchase order.</summary>
public sealed class PurchaseOrderLine : ITenantOwned
{
    private PurchaseOrderLine()
    {
    }

    internal PurchaseOrderLine(int lineNo, PurchaseOrderLineData data)
    {
        LineNo = lineNo;
        ItemId = data.ItemId;
        UnitId = data.UnitId;
        Quantity = data.Quantity;
        ConversionFactor = data.ConversionFactor;
        StockQuantity = data.StockQuantity;
        UnitPrice = data.UnitPrice;
        DiscountPercent = data.DiscountPercent;
        NetAmount = PurchaseCalculator.LineNetAmount(data.Quantity, data.UnitPrice, data.DiscountPercent);
        DeliveryDate = data.DeliveryDate;
        PrLineId = data.PrLineId;
        Remark = Text.Clean(data.Remark, 300);
    }

    /// <summary>Primary key.</summary>
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Owning order.</summary>
    public Guid OrderId { get; private set; }

    /// <summary>Order within the document.</summary>
    public int LineNo { get; private set; }

    /// <summary>Item.</summary>
    public Guid ItemId { get; private set; }

    /// <summary>Unit of the quantity and price.</summary>
    public Guid UnitId { get; private set; }

    /// <summary>Ordered quantity.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Multiplier to the stock unit.</summary>
    public decimal ConversionFactor { get; private set; }

    /// <summary>Ordered quantity in stock unit.</summary>
    public decimal StockQuantity { get; private set; }

    /// <summary>Price per unit.</summary>
    public decimal UnitPrice { get; private set; }

    /// <summary>Line discount in percent of the line amount.</summary>
    public decimal DiscountPercent { get; private set; }

    /// <summary>Line amount after discount.</summary>
    public decimal NetAmount { get; private set; }

    /// <summary>Expected delivery date of the line.</summary>
    public DateOnly? DeliveryDate { get; private set; }

    /// <summary>Purchase request line the order line fulfils.</summary>
    public Guid? PrLineId { get; private set; }

    /// <summary>Remark.</summary>
    public string? Remark { get; private set; }

    /// <summary>Net received quantity in stock unit (receipts minus returns).</summary>
    public decimal ReceivedStockQuantity { get; private set; }

    /// <summary>Closed lines expect no further receipt.</summary>
    public bool IsClosed { get; private set; }

    /// <summary>Outstanding quantity in stock unit.</summary>
    public decimal OutstandingStockQuantity => IsClosed ? 0m : Math.Max(StockQuantity - ReceivedStockQuantity, 0m);

    internal void AddReceived(decimal stockQuantity)
    {
        var result = ReceivedStockQuantity + stockQuantity;
        if (result < 0)
        {
            throw DomainException.Conflict("purchasing.po.negative_received", "Returned quantity exceeds the received quantity.");
        }

        ReceivedStockQuantity = result;
    }

    internal void Close() => IsClosed = true;
}
