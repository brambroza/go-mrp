namespace Mrp.SharedKernel.Domain;

/// <summary>Document type keys used for numbering and approval routes.</summary>
public static class DocumentTypes
{
    /// <summary>Purchase request.</summary>
    public const string PurchaseRequest = "PR";

    /// <summary>Purchase order.</summary>
    public const string PurchaseOrder = "PO";

    /// <summary>Goods receipt.</summary>
    public const string GoodsReceipt = "GR";

    /// <summary>Goods issue.</summary>
    public const string GoodsIssue = "GI";

    /// <summary>Stock transfer.</summary>
    public const string StockTransfer = "TF";

    /// <summary>Stock adjustment.</summary>
    public const string StockAdjustment = "ADJ";

    /// <summary>Stock count.</summary>
    public const string StockCount = "CNT";

    /// <summary>Return to supplier.</summary>
    public const string SupplierReturn = "RTS";

    /// <summary>Lot (barcode) number.</summary>
    public const string Lot = "LOT";

    /// <summary>Sales order.</summary>
    public const string SalesOrder = "SO";

    /// <summary>Work order (production order).</summary>
    public const string WorkOrder = "WO";

    /// <summary>MRP run.</summary>
    public const string MrpRun = "MRP";

    /// <summary>All known keys.</summary>
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        PurchaseRequest, PurchaseOrder, GoodsReceipt, GoodsIssue, StockTransfer, StockAdjustment,
        StockCount, SupplierReturn, Lot, SalesOrder, WorkOrder, MrpRun,
    };
}
