namespace Mrp.Purchasing.Domain;

/// <summary>Amounts of a purchase order.</summary>
/// <param name="Subtotal">Sum of line net amounts.</param>
/// <param name="DiscountAmount">Header discount.</param>
/// <param name="NetAmount">Subtotal minus header discount.</param>
/// <param name="VatAmount">VAT on the net amount.</param>
/// <param name="Total">Net amount plus VAT.</param>
public sealed record PurchaseTotals(decimal Subtotal, decimal DiscountAmount, decimal NetAmount, decimal VatAmount, decimal Total);

/// <summary>
/// The single definition of purchase amounts. Line discount is a percentage of the line amount;
/// every amount is rounded to 2 decimals, half away from zero.
/// </summary>
public static class PurchaseCalculator
{
    /// <summary>Rounds a money amount to 2 decimals.</summary>
    public static decimal RoundMoney(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    /// <summary><c>round(quantity × unitPrice × (1 − discountPercent/100), 2)</c>.</summary>
    public static decimal LineNetAmount(decimal quantity, decimal unitPrice, decimal discountPercent) =>
        RoundMoney(quantity * unitPrice * (1m - (discountPercent / 100m)));

    /// <summary>Header amounts from line net amounts.</summary>
    public static PurchaseTotals Totals(IEnumerable<decimal> lineNetAmounts, decimal discountAmount, decimal vatPercent)
    {
        var subtotal = lineNetAmounts.Sum();
        var net = subtotal - discountAmount;
        var vat = RoundMoney(net * vatPercent / 100m);
        return new PurchaseTotals(subtotal, discountAmount, net, vat, net + vat);
    }

    /// <summary>
    /// Cost per stock unit in the tenant currency:
    /// <c>unitPrice × (1 − discount%/100) × exchangeRate ÷ conversionFactor</c>, rounded to 4 decimals.
    /// </summary>
    public static decimal StockUnitCost(decimal unitPrice, decimal discountPercent, decimal exchangeRate, decimal conversionFactor) =>
        Math.Round(unitPrice * (1m - (discountPercent / 100m)) * exchangeRate / conversionFactor, 4, MidpointRounding.AwayFromZero);
}
