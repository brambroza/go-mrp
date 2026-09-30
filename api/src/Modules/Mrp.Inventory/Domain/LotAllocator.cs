using Mrp.SharedKernel.Domain;

namespace Mrp.Inventory.Domain;

/// <summary>Order in which lots are consumed.</summary>
public enum IssueStrategy
{
    /// <summary>First in, first out by receive time.</summary>
    Fifo,

    /// <summary>First expired, first out; lots without expiry last.</summary>
    Fefo,
}

/// <summary>Stock of one lot at one location, as seen by the allocator.</summary>
/// <param name="LotId">Lot id.</param>
/// <param name="LotNo">Lot number (tie breaker).</param>
/// <param name="LocationId">Location, if any.</param>
/// <param name="LocationCode">Location code (tie breaker).</param>
/// <param name="ReceivedAt">Time the lot entered stock.</param>
/// <param name="ExpiryDate">Expiry date.</param>
/// <param name="QcStatus">Quality status.</param>
/// <param name="Quantity">On-hand quantity in stock unit.</param>
public sealed record LotStock(
    Guid LotId,
    string LotNo,
    Guid? LocationId,
    string? LocationCode,
    DateTimeOffset ReceivedAt,
    DateOnly? ExpiryDate,
    LotQcStatus QcStatus,
    decimal Quantity);

/// <summary>Quantity taken from one lot at one location.</summary>
public sealed record LotAllocation(Guid LotId, Guid? LocationId, decimal Quantity);

/// <summary>
/// Chooses lots for an outgoing quantity. Deterministic: the same stock always gives the same result.
/// A shortage fails the whole request; nothing is allocated partially.
/// </summary>
public static class LotAllocator
{
    /// <summary>Lots in the order they would be consumed, after removing those that may not be issued.</summary>
    /// <param name="stock">Candidate stock of one item in one warehouse.</param>
    /// <param name="strategy">FIFO or FEFO.</param>
    /// <param name="postingDate">Date of the document; lots expired before it are skipped.</param>
    /// <param name="usableOnly">When true only released, unexpired lots are considered.</param>
    public static IReadOnlyList<LotStock> Order(IEnumerable<LotStock> stock, IssueStrategy strategy, DateOnly postingDate, bool usableOnly = true)
    {
        var candidates = stock.Where(s => s.Quantity > 0);
        if (usableOnly)
        {
            candidates = candidates.Where(s => s.QcStatus == LotQcStatus.Released && (s.ExpiryDate is null || s.ExpiryDate >= postingDate));
        }

        var ordered = strategy == IssueStrategy.Fefo
            ? candidates.OrderBy(s => s.ExpiryDate is null).ThenBy(s => s.ExpiryDate).ThenBy(s => s.ReceivedAt)
            : candidates.OrderBy(s => s.ReceivedAt);
        return ordered
            .ThenBy(s => s.LotNo, StringComparer.Ordinal)
            .ThenBy(s => s.LocationCode ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(s => s.LotId)
            .ToList();
    }

    /// <summary>Allocates <paramref name="required"/> from the stock or throws <c>inventory.insufficient_stock</c>.</summary>
    public static IReadOnlyList<LotAllocation> Allocate(
        IEnumerable<LotStock> stock,
        decimal required,
        IssueStrategy strategy,
        DateOnly postingDate,
        bool usableOnly = true,
        string? itemCode = null)
    {
        if (required <= 0)
        {
            throw new DomainException("inventory.invalid_quantity", "Quantity must be greater than zero.", 400);
        }

        var ordered = Order(stock, strategy, postingDate, usableOnly);
        var available = ordered.Sum(s => s.Quantity);
        if (available < required)
        {
            throw new DomainException(
                "inventory.insufficient_stock",
                $"Not enough stock for item {itemCode ?? string.Empty}: required {required:0.######}, available {available:0.######}.");
        }

        var allocations = new List<LotAllocation>();
        var remaining = required;
        foreach (var lot in ordered)
        {
            if (remaining <= 0)
            {
                break;
            }

            var take = Math.Min(lot.Quantity, remaining);
            allocations.Add(new LotAllocation(lot.LotId, lot.LocationId, take));
            remaining -= take;
        }

        return allocations;
    }
}
