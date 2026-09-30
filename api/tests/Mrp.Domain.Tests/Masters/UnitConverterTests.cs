using Mrp.Masters.Domain;
using Mrp.SharedKernel.Domain;

namespace Mrp.Domain.Tests.Masters;

/// <summary>Golden cases for the single conversion formula <c>qty_to = round(qty_from × factor, 6)</c>.</summary>
public sealed class UnitConverterTests
{
    private static readonly Guid Kg = Guid.NewGuid();
    private static readonly Guid Gram = Guid.NewGuid();
    private static readonly Guid Piece = Guid.NewGuid();
    private static readonly Guid Box = Guid.NewGuid();
    private static readonly Guid Dozen = Guid.NewGuid();
    private static readonly Guid Litre = Guid.NewGuid();
    private static readonly Guid Bottle = Guid.NewGuid();
    private static readonly Guid Cap = Guid.NewGuid();

    private static readonly UnitConverter Converter = new(
    [
        new ConversionRule(null, Kg, Gram, 1000m),
        new ConversionRule(null, Dozen, Piece, 12m),
        new ConversionRule(Bottle, Box, Piece, 48m),
        new ConversionRule(Cap, Box, Piece, 500m),
        new ConversionRule(Cap, Kg, Piece, 350m),
    ]);

    [Fact]
    public void Same_unit_is_unchanged()
    {
        Assert.Equal(12.345678m, Converter.Convert(Bottle, Piece, Piece, Piece, 12.345678m));
    }

    [Theory]
    [InlineData(2.5, 2500)]
    [InlineData(0.000001, 0.001)]
    public void Global_rule_forward(decimal kg, decimal grams)
    {
        Assert.Equal(grams, Converter.Convert(Bottle, Gram, Kg, Gram, kg));
    }

    [Theory]
    [InlineData(2500, 2.5)]
    [InlineData(1, 0.001)]
    [InlineData(0.5, 0.0005)]
    public void Global_rule_backward_uses_inverse_factor(decimal grams, decimal kg)
    {
        Assert.Equal(kg, Converter.Convert(Bottle, Gram, Gram, Kg, grams));
    }

    [Fact]
    public void Item_rule_differs_per_item()
    {
        Assert.Equal(96m, Converter.Convert(Bottle, Piece, Box, Piece, 2m));
        Assert.Equal(1000m, Converter.Convert(Cap, Piece, Box, Piece, 2m));
    }

    [Fact]
    public void Inverse_result_is_rounded_to_six_decimals_half_away_from_zero()
    {
        // 100 / 48 = 2.083333333... ; 1 / 48 = 0.0208333...
        Assert.Equal(2.083333m, Converter.Convert(Bottle, Piece, Piece, Box, 100m));
        Assert.Equal(0.020833m, Converter.Convert(Bottle, Piece, Piece, Box, 1m));
        // 5 / 12 = 0.41666666... rounds up at the sixth decimal
        Assert.Equal(0.416667m, Converter.Convert(Bottle, Piece, Piece, Dozen, 5m));
    }

    [Fact]
    public void Converts_through_the_stock_unit_when_no_direct_rule_exists()
    {
        // Cap: 1 kg = 350 pcs and 1 box = 500 pcs, so 1 box = 500 / 350 kg.
        Assert.Equal(1.428571m, Converter.Convert(Cap, Piece, Box, Kg, 1m));
        Assert.Equal(0.7m, Converter.Convert(Cap, Piece, Kg, Box, 1m));
    }

    [Fact]
    public void Missing_conversion_is_an_error_not_a_guess()
    {
        var error = Assert.Throws<DomainException>(() => Converter.Convert(Bottle, Piece, Litre, Piece, 1m));

        Assert.Equal("masters.uom.no_conversion", error.Code);
        Assert.Null(Converter.FindFactor(Bottle, Piece, Litre, Kg));
    }

    [Fact]
    public void To_stock_unit_converts_purchase_quantities()
    {
        Assert.Equal(240m, Converter.ToStockUnit(Bottle, Piece, Box, 5m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Factor_must_be_positive(decimal factor)
    {
        Assert.Throws<DomainException>(() => new UnitConversion(null, Kg, Gram, factor));
    }

    [Fact]
    public void Conversion_between_the_same_unit_is_rejected()
    {
        Assert.Throws<DomainException>(() => new UnitConversion(null, Kg, Kg, 1m));
    }
}

/// <summary>Golden cases for purchase price tiers (largest tier not above the quantity, valid on the date).</summary>
public sealed class SupplierPriceResolverTests
{
    private static readonly Guid Supplier = Guid.NewGuid();
    private static readonly Guid Item = Guid.NewGuid();
    private static readonly Guid Kg = Guid.NewGuid();
    private static readonly Guid Gram = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 10, 15);

    private static readonly SupplierPrice[] Tiers =
    [
        new(Supplier, Item, Kg, 0m, 120m, "THB", new DateOnly(2026, 1, 1), null),
        new(Supplier, Item, Kg, 100m, 110m, "THB", new DateOnly(2026, 1, 1), null),
        new(Supplier, Item, Kg, 500m, 95.5m, "THB", new DateOnly(2026, 1, 1), new DateOnly(2026, 9, 30)),
        new(Supplier, Item, Kg, 1000m, 90m, "THB", new DateOnly(2026, 11, 1), null),
    ];

    [Theory]
    [InlineData(1, 120)]
    [InlineData(99.999999, 120)]
    [InlineData(100, 110)]
    [InlineData(750, 110)]
    [InlineData(5000, 110)]
    public void Picks_largest_tier_not_above_quantity_among_valid_ones(decimal quantity, decimal expectedPrice)
    {
        var tier = SupplierPriceResolver.Resolve(Tiers, Today, _ => quantity);

        Assert.Equal(expectedPrice, tier!.UnitPrice);
    }

    [Fact]
    public void Expired_and_future_tiers_apply_only_inside_their_period()
    {
        Assert.Equal(95.5m, SupplierPriceResolver.Resolve(Tiers, new DateOnly(2026, 9, 30), _ => 750m)!.UnitPrice);
        Assert.Equal(90m, SupplierPriceResolver.Resolve(Tiers, new DateOnly(2026, 11, 1), _ => 1000m)!.UnitPrice);
    }

    [Fact]
    public void Quantity_is_compared_in_the_unit_of_the_tier()
    {
        // 150,000 g ordered = 150 kg, so the 100 kg tier applies.
        var tier = SupplierPriceResolver.Resolve(Tiers, Today, tierUnit => tierUnit == Kg ? 150_000m / 1000m : null);

        Assert.Equal(110m, tier!.UnitPrice);
    }

    [Fact]
    public void No_price_when_nothing_is_valid_or_convertible()
    {
        Assert.Null(SupplierPriceResolver.Resolve(Tiers, new DateOnly(2025, 12, 31), _ => 10m));
        Assert.Null(SupplierPriceResolver.Resolve(Tiers, Today, _ => null));
    }

    [Fact]
    public void Period_must_be_valid()
    {
        Assert.Throws<DomainException>(() => new SupplierPrice(Supplier, Item, Gram, 0m, 1m, "THB", new DateOnly(2026, 2, 1), new DateOnly(2026, 1, 1)));
    }
}
