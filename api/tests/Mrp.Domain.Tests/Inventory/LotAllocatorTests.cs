using Mrp.Inventory.Domain;
using Mrp.SharedKernel.Domain;

namespace Mrp.Domain.Tests.Inventory;

/// <summary>
/// Golden cases for lot allocation. The legacy system ordered by the batch number string and silently
/// dropped shortages; the new rules order by receive time (FIFO) or expiry (FEFO) and fail on shortage.
/// </summary>
public sealed class LotAllocatorTests
{
    private static readonly DateOnly Today = new(2026, 10, 15);
    private static readonly Guid LocationA = Guid.NewGuid();
    private static readonly Guid LocationB = Guid.NewGuid();

    private static readonly LotStock Oldest = Stock("LOT-2609-0009", received: "2026-09-01", expiry: "2027-09-01", quantity: 100);
    private static readonly LotStock Middle = Stock("LOT-2609-0002", received: "2026-09-10", expiry: "2026-12-31", quantity: 50);
    private static readonly LotStock Newest = Stock("LOT-2610-0001", received: "2026-10-01", expiry: "2027-03-31", quantity: 200);

    [Fact]
    public void Fifo_takes_lots_in_receive_order_not_lot_number_order()
    {
        var result = LotAllocator.Allocate([Newest, Middle, Oldest], 120m, IssueStrategy.Fifo, Today);

        Assert.Equal(
            [new LotAllocation(Oldest.LotId, null, 100m), new LotAllocation(Middle.LotId, null, 20m)],
            result);
    }

    [Fact]
    public void Fefo_takes_the_lot_that_expires_first()
    {
        var result = LotAllocator.Allocate([Newest, Middle, Oldest], 120m, IssueStrategy.Fefo, Today);

        Assert.Equal(
            [new LotAllocation(Middle.LotId, null, 50m), new LotAllocation(Newest.LotId, null, 70m)],
            result);
    }

    [Fact]
    public void Fefo_puts_lots_without_expiry_last()
    {
        var noExpiry = Stock("LOT-2601-0001", received: "2026-01-01", expiry: null, quantity: 500);

        var result = LotAllocator.Allocate([noExpiry, Newest], 250m, IssueStrategy.Fefo, Today);

        Assert.Equal(
            [new LotAllocation(Newest.LotId, null, 200m), new LotAllocation(noExpiry.LotId, null, 50m)],
            result);
    }

    [Fact]
    public void Exact_quantity_consumes_the_lot_completely_and_stops()
    {
        var result = LotAllocator.Allocate([Oldest, Middle, Newest], 100m, IssueStrategy.Fifo, Today);

        Assert.Equal([new LotAllocation(Oldest.LotId, null, 100m)], result);
    }

    [Fact]
    public void Fractional_quantities_keep_six_decimals()
    {
        var result = LotAllocator.Allocate([Oldest with { Quantity = 0.333333m }, Middle], 0.5m, IssueStrategy.Fifo, Today);

        Assert.Equal(
            [new LotAllocation(Oldest.LotId, null, 0.333333m), new LotAllocation(Middle.LotId, null, 0.166667m)],
            result);
    }

    [Theory]
    [InlineData(LotQcStatus.Quarantine)]
    [InlineData(LotQcStatus.Rejected)]
    [InlineData(LotQcStatus.OnHold)]
    public void Lots_that_are_not_released_are_skipped(LotQcStatus status)
    {
        var blocked = Oldest with { QcStatus = status };

        var result = LotAllocator.Allocate([blocked, Middle, Newest], 60m, IssueStrategy.Fifo, Today);

        Assert.Equal(
            [new LotAllocation(Middle.LotId, null, 50m), new LotAllocation(Newest.LotId, null, 10m)],
            result);
    }

    [Fact]
    public void Expired_lots_are_skipped_but_lots_expiring_today_are_usable()
    {
        var expired = Oldest with { ExpiryDate = Today.AddDays(-1) };
        var expiresToday = Middle with { ExpiryDate = Today };

        var result = LotAllocator.Allocate([expired, expiresToday, Newest], 60m, IssueStrategy.Fifo, Today);

        Assert.Equal(
            [new LotAllocation(expiresToday.LotId, null, 50m), new LotAllocation(Newest.LotId, null, 10m)],
            result);
    }

    [Fact]
    public void Shortage_fails_the_whole_request_and_allocates_nothing()
    {
        var error = Assert.Throws<DomainException>(() =>
            LotAllocator.Allocate([Oldest, Middle, Newest], 350.000001m, IssueStrategy.Fifo, Today, itemCode: "RM-001"));

        Assert.Equal("inventory.insufficient_stock", error.Code);
        Assert.Contains("RM-001", error.Message);
        Assert.Contains("350", error.Message);
    }

    [Fact]
    public void Blocked_stock_does_not_count_as_available()
    {
        var quarantine = Newest with { QcStatus = LotQcStatus.Quarantine };

        Assert.Throws<DomainException>(() => LotAllocator.Allocate([Oldest, quarantine], 150m, IssueStrategy.Fifo, Today));
    }

    [Fact]
    public void Returns_and_adjustments_may_take_blocked_or_expired_stock()
    {
        var rejected = Oldest with { QcStatus = LotQcStatus.Rejected, ExpiryDate = Today.AddDays(-30) };

        var result = LotAllocator.Allocate([rejected], 40m, IssueStrategy.Fifo, Today, usableOnly: false);

        Assert.Equal([new LotAllocation(rejected.LotId, null, 40m)], result);
    }

    [Fact]
    public void Same_lot_in_two_locations_is_taken_in_location_order()
    {
        var atB = Oldest with { LocationId = LocationB, LocationCode = "B-01", Quantity = 30m };
        var atA = Oldest with { LocationId = LocationA, LocationCode = "A-01", Quantity = 30m };

        var result = LotAllocator.Allocate([atB, atA], 40m, IssueStrategy.Fifo, Today);

        Assert.Equal(
            [new LotAllocation(Oldest.LotId, LocationA, 30m), new LotAllocation(Oldest.LotId, LocationB, 10m)],
            result);
    }

    [Fact]
    public void Lots_received_at_the_same_time_are_ordered_by_lot_number()
    {
        var first = Stock("LOT-A", received: "2026-09-01", expiry: null, quantity: 10);
        var second = Stock("LOT-B", received: "2026-09-01", expiry: null, quantity: 10);

        var result = LotAllocator.Allocate([second, first], 15m, IssueStrategy.Fifo, Today);

        Assert.Equal([new LotAllocation(first.LotId, null, 10m), new LotAllocation(second.LotId, null, 5m)], result);
    }

    [Fact]
    public void Result_does_not_depend_on_input_order()
    {
        LotStock[] stock = [Oldest, Middle, Newest];

        var expected = LotAllocator.Allocate(stock, 330m, IssueStrategy.Fifo, Today);

        Assert.Equal(expected, LotAllocator.Allocate(stock.Reverse(), 330m, IssueStrategy.Fifo, Today));
        Assert.Equal(expected, LotAllocator.Allocate([Middle, Newest, Oldest], 330m, IssueStrategy.Fifo, Today));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Quantity_must_be_positive(decimal quantity)
    {
        var error = Assert.Throws<DomainException>(() => LotAllocator.Allocate([Oldest], quantity, IssueStrategy.Fifo, Today));

        Assert.Equal("inventory.invalid_quantity", error.Code);
    }

    [Fact]
    public void Empty_or_negative_balances_are_ignored()
    {
        var empty = Oldest with { Quantity = 0m };
        var negative = Middle with { Quantity = -5m };

        var result = LotAllocator.Allocate([empty, negative, Newest], 10m, IssueStrategy.Fifo, Today);

        Assert.Equal([new LotAllocation(Newest.LotId, null, 10m)], result);
    }

    private static LotStock Stock(string lotNo, string received, string? expiry, decimal quantity) =>
        new(
            Guid.NewGuid(),
            lotNo,
            null,
            null,
            new DateTimeOffset(DateTime.Parse(received, System.Globalization.CultureInfo.InvariantCulture), TimeSpan.Zero),
            expiry is null ? null : DateOnly.Parse(expiry, System.Globalization.CultureInfo.InvariantCulture),
            LotQcStatus.Released,
            quantity);
}

public sealed class LotTests
{
    [Fact]
    public void Default_expiry_uses_manufacturing_date_plus_shelf_life()
    {
        Assert.Equal(new DateOnly(2027, 9, 1), Lot.DefaultExpiry(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 15), 365));
        Assert.Equal(new DateOnly(2026, 11, 14), Lot.DefaultExpiry(null, new DateOnly(2026, 10, 15), 30));
        Assert.Null(Lot.DefaultExpiry(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 15), null));
    }

    [Theory]
    [InlineData(LotQcStatus.Quarantine, LotQcStatus.Released, true)]
    [InlineData(LotQcStatus.Quarantine, LotQcStatus.Rejected, true)]
    [InlineData(LotQcStatus.Released, LotQcStatus.OnHold, true)]
    [InlineData(LotQcStatus.OnHold, LotQcStatus.Released, true)]
    [InlineData(LotQcStatus.Rejected, LotQcStatus.Released, false)]
    [InlineData(LotQcStatus.Released, LotQcStatus.Quarantine, false)]
    [InlineData(LotQcStatus.Quarantine, LotQcStatus.OnHold, false)]
    public void Qc_status_follows_the_state_machine(LotQcStatus from, LotQcStatus to, bool allowed)
    {
        var lot = new Lot(Guid.NewGuid(), "lot-1", from, 10m, DateTimeOffset.UtcNow, null, null, null, null);

        if (allowed)
        {
            lot.SetQcStatus(to, "checked", null);
            Assert.Equal(to, lot.QcStatus);
        }
        else
        {
            Assert.Equal("inventory.lot.invalid_qc_transition", Assert.Throws<DomainException>(() => lot.SetQcStatus(to, null, null)).Code);
        }
    }

    [Fact]
    public void Lot_number_is_upper_cased_and_expiry_must_not_precede_manufacturing()
    {
        var lot = new Lot(Guid.NewGuid(), " lot-abc ", LotQcStatus.Released, 1m, DateTimeOffset.UtcNow, null, null, null, null);
        Assert.Equal("LOT-ABC", lot.LotNo);

        Assert.Throws<DomainException>(() =>
            new Lot(Guid.NewGuid(), "L1", LotQcStatus.Released, 1m, DateTimeOffset.UtcNow, null, new DateOnly(2026, 10, 1), new DateOnly(2026, 9, 1), null));
    }
}
