using Shop.Core.Entities;
using Shop.Core.Pricing;

namespace Shop.Tests;

public class FamilySummaryTests
{
    private static Product Option(decimal? price, Availability availability, int? stock = null, int? lead = 2, bool published = true) =>
        new() { IsPublished = published, Price = price, Availability = availability, StockQuantity = stock, LeadTimeDays = lead };

    [Fact]
    public void Price_range_and_best_availability_come_from_buyable_options()
    {
        var main = Option(4600, Availability.InStock, stock: 5);
        var summary = FamilySummary.Compute(main,
        [
            Option(4950, Availability.InStock, stock: 2),
            Option(4300, Availability.PreOrder, lead: 15),
            Option(3000, Availability.OutOfStock, stock: 0),           // not buyable: excluded from the range
            Option(2000, Availability.InStock, stock: 9, published: false), // hidden: excluded
        ]);

        Assert.Equal(4300m, summary.MinPrice);
        Assert.Equal(4950m, summary.MaxPrice);
        Assert.Equal(Availability.InStock, summary.Availability);
        Assert.Equal(3, summary.OptionCount);
    }

    [Fact]
    public void Pre_order_only_family_reports_pre_order_with_its_deadline()
    {
        var ends = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var main = Option(5000, Availability.OutOfStock, stock: 0);
        var pre = Option(4200, Availability.PreOrder, lead: 15);
        pre.OfferEndsAt = ends;

        var summary = FamilySummary.Compute(main, [pre]);
        Assert.Equal(Availability.PreOrder, summary.Availability);
        Assert.Equal(15, summary.LeadTimeDays);
        Assert.Equal(ends, summary.OfferEndsAt);
        Assert.Equal(1, summary.OptionCount);
        Assert.Equal(4200m, summary.MinPrice);
    }

    [Fact]
    public void Nothing_buyable_keeps_last_prices_and_is_out_of_stock()
    {
        var summary = FamilySummary.Compute(Option(1000, Availability.OutOfStock, stock: 0), [Option(1200, Availability.OutOfStock, stock: 0)]);
        Assert.Equal(Availability.OutOfStock, summary.Availability);
        Assert.Equal(0, summary.OptionCount);
        Assert.Equal(1000m, summary.MinPrice);
        Assert.Equal(1200m, summary.MaxPrice);
    }

    [Fact]
    public void Apply_reports_changes_only_when_something_changed()
    {
        var main = Option(100, Availability.InStock, stock: 1);
        var summary = FamilySummary.Compute(main, []);
        Assert.True(summary.ApplyTo(main));
        Assert.False(summary.ApplyTo(main));
        Assert.Equal(100m, main.ListPrice);
    }
}
