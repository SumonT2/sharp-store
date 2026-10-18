using Shop.Core.Entities;
using Shop.Core.Pricing;

namespace Shop.Tests;

public class PricingEngineTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    private static PricingContext Context(params PricingRule[] rules) => new()
    {
        BaseCurrency = "USD",
        RatesToBase = new Dictionary<string, decimal> { ["CNY"] = 0.14m },
        Rules = rules,
        CategoryParents = new Dictionary<int, int?> { [1] = null, [2] = 1 },
        InStockMaxLeadDays = 3,
        NowUtc = Now,
    };

    private static readonly Supplier Local = new() { Id = 1, Code = "IMP", Currency = "USD", DefaultLeadTimeDays = 2 };
    private static readonly Supplier China = new() { Id = 2, Code = "CN", Currency = "CNY", DefaultLeadTimeDays = 15, ExtraCostPercent = 10, ExtraCostFixed = 1 };

    private static SupplierOffer Offer(Supplier s, decimal cost, int? stock = null, DateTime? from = null, DateTime? to = null) =>
        new() { Id = s.Id * 100, Supplier = s, SupplierId = s.Id, Cost = cost, StockQuantity = stock, ValidFrom = from, ValidTo = to };

    [Fact]
    public void In_stock_offer_wins_over_cheaper_pre_order()
    {
        var product = new Product { CategoryId = 2 };
        var result = PricingEngine.Evaluate(product, [Offer(Local, 100, stock: 5), Offer(China, 500)], Context());

        Assert.Equal(Availability.InStock, result.Availability);
        Assert.Equal(100m, result.Price);
        Assert.Equal(5, result.StockQuantity);
    }

    [Fact]
    public void Falls_back_to_pre_order_when_local_stock_is_zero()
    {
        var product = new Product { CategoryId = 2 };
        var end = Now.AddDays(5);
        var result = PricingEngine.Evaluate(product, [Offer(Local, 100, stock: 0), Offer(China, 500, to: end)], Context());

        Assert.Equal(Availability.PreOrder, result.Availability);
        Assert.Equal(15, result.LeadTimeDays);
        Assert.Equal(end, result.OfferEndsAt);
        // 500 CNY * 0.14 = 70, +10% = 77, +1 fixed = 78
        Assert.Equal(78m, result.Price);
    }

    [Fact]
    public void Offer_outside_its_window_is_ignored()
    {
        var product = new Product();
        var expired = Offer(China, 500, from: Now.AddDays(-10), to: Now.AddSeconds(-1));
        var future = Offer(China, 500, from: Now.AddDays(1));

        Assert.Equal(Availability.OutOfStock, PricingEngine.Evaluate(product, [expired], Context()).Availability);
        Assert.Equal(Availability.OutOfStock, PricingEngine.Evaluate(product, [future], Context()).Availability);
    }

    [Fact]
    public void Out_of_stock_keeps_last_known_price()
    {
        var product = new Product { Price = 123m };
        var result = PricingEngine.Evaluate(product, [Offer(Local, 100, stock: 0)], Context());
        Assert.Equal(Availability.OutOfStock, result.Availability);
        Assert.Equal(123m, result.Price);
    }

    [Fact]
    public void Missing_exchange_rate_skips_offer_with_warning()
    {
        var euro = new Supplier { Id = 3, Code = "EU", Currency = "EUR", DefaultLeadTimeDays = 2 };
        var result = PricingEngine.Evaluate(new Product(), [Offer(euro, 10)], Context());
        Assert.Equal(Availability.OutOfStock, result.Availability);
        Assert.Contains(result.Warnings, w => w.Contains("EUR"));
    }

    [Fact]
    public void Price_override_wins()
    {
        var product = new Product { PriceOverride = 49.5m };
        var result = PricingEngine.Evaluate(product, [Offer(Local, 100, stock: 1)], Context());
        Assert.Equal(49.5m, result.Price);
        Assert.Equal(Availability.InStock, result.Availability);
    }

    [Fact]
    public void Most_specific_rule_wins_and_parent_category_rules_apply_to_children()
    {
        var global = new PricingRule { Id = 1, MarkupPercent = 10 };
        var parentCategory = new PricingRule { Id = 2, CategoryId = 1, MarkupPercent = 30 };
        var supplierRule = new PricingRule { Id = 3, SupplierId = 2, MarkupPercent = 5 };
        var ctx = Context(global, parentCategory, supplierRule);
        var product = new Product { CategoryId = 2 };

        Assert.Same(parentCategory, PricingEngine.FindRule(product, supplierId: 1, ctx));
        Assert.Same(supplierRule, PricingEngine.FindRule(product, supplierId: 2, ctx));
        Assert.Same(global, PricingEngine.FindRule(new Product { CategoryId = 99 }, supplierId: 1, ctx));
    }

    [Fact]
    public void Priority_beats_specificity()
    {
        var urgent = new PricingRule { Id = 1, MarkupPercent = 0, Priority = 10 };
        var specific = new PricingRule { Id = 2, SupplierId = 1, MarkupPercent = 50 };
        Assert.Same(urgent, PricingEngine.FindRule(new Product(), 1, Context(urgent, specific)));
    }

    [Fact]
    public void Minimum_margin_is_enforced()
    {
        var rule = new PricingRule { MarkupPercent = 1, MinMargin = 5 };
        Assert.Equal(15m, PricingEngine.ApplyRule(10m, rule));
    }

    [Theory]
    [InlineData(PriceRounding.Ends99, 123.40, 123.99)]
    [InlineData(PriceRounding.Ends99, 123.995, 124.99)]
    [InlineData(PriceRounding.Ends99, 123.99, 123.99)]
    [InlineData(PriceRounding.Ends9, 120.5, 129)]
    [InlineData(PriceRounding.Ends9, 129, 129)]
    [InlineData(PriceRounding.Whole, 10.01, 11)]
    [InlineData(PriceRounding.UpTo10, 101, 110)]
    [InlineData(PriceRounding.UpTo50, 101, 150)]
    [InlineData(PriceRounding.UpTo100, 100, 100)]
    [InlineData(PriceRounding.None, 10.005, 10.01)]
    public void Rounding_always_goes_up(PriceRounding rounding, double input, double expected)
    {
        Assert.Equal((decimal)expected, PricingEngine.Round((decimal)input, rounding));
    }
}
