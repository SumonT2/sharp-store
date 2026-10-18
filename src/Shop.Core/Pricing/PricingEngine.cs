using Shop.Core.Entities;

namespace Shop.Core.Pricing;

public sealed class PricingContext
{
    public required string BaseCurrency { get; init; }
    public required IReadOnlyDictionary<string, decimal> RatesToBase { get; init; }
    public required IReadOnlyList<PricingRule> Rules { get; init; }
    /// <summary>Category id → parent id, so rules on a parent category apply to its children.</summary>
    public required IReadOnlyDictionary<int, int?> CategoryParents { get; init; }
    /// <summary>Offers delivering within this many days count as "in stock"; slower ones are pre-orders.</summary>
    public int InStockMaxLeadDays { get; init; } = 3;
    public required DateTime NowUtc { get; init; }

    public decimal? RateFor(string currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || string.Equals(currency, BaseCurrency, StringComparison.OrdinalIgnoreCase))
            return 1m;
        return RatesToBase.TryGetValue(currency.ToUpperInvariant(), out var rate) && rate > 0 ? rate : null;
    }
}

public sealed record OfferQuote(
    SupplierOffer Offer,
    decimal LandedCost,
    decimal SellPrice,
    Availability Availability,
    PricingRule? Rule);

public sealed record PricingResult(
    OfferQuote? Best,
    decimal? Price,
    Availability Availability,
    int? StockQuantity,
    int? LeadTimeDays,
    DateTime? OfferEndsAt,
    IReadOnlyList<string> Warnings);

public static class PricingEngine
{
    /// <summary>
    /// Picks the best currently-available offer for the product and derives the selling price and availability.
    /// In-stock offers win over pre-orders; among those, the lowest selling price wins.
    /// </summary>
    public static PricingResult Evaluate(Product product, IEnumerable<SupplierOffer> offers, PricingContext ctx)
    {
        var warnings = new List<string>();
        var quotes = new List<OfferQuote>();

        foreach (var offer in offers)
        {
            if (!offer.IsAvailableAt(ctx.NowUtc))
                continue;

            var supplier = offer.Supplier ?? throw new InvalidOperationException($"Offer {offer.Id} has no supplier loaded.");
            var rate = ctx.RateFor(offer.EffectiveCurrency);
            if (rate is null)
            {
                warnings.Add($"No exchange rate for {offer.EffectiveCurrency} (supplier {supplier.Code}).");
                continue;
            }

            var landed = LandedCost(offer.Cost, rate.Value, supplier);
            var rule = FindRule(product, supplier.Id, ctx);
            var price = ApplyRule(landed, rule);
            var availability = offer.EffectiveLeadTimeDays <= ctx.InStockMaxLeadDays ? Availability.InStock : Availability.PreOrder;
            quotes.Add(new OfferQuote(offer, landed, price, availability, rule));
        }

        var best = quotes
            .OrderBy(q => q.Availability == Availability.InStock ? 0 : 1)
            .ThenBy(q => q.SellPrice)
            .ThenBy(q => q.Offer.EffectiveLeadTimeDays)
            .FirstOrDefault();

        if (best is null)
        {
            // Keep the last known price so the product page can still show it as "out of stock".
            return new PricingResult(null, product.PriceOverride ?? product.Price, Availability.OutOfStock, 0, null, null, warnings);
        }

        var finalPrice = product.PriceOverride ?? best.SellPrice;
        return new PricingResult(
            best,
            finalPrice,
            best.Availability,
            best.Offer.StockQuantity,
            best.Offer.EffectiveLeadTimeDays,
            best.Offer.ValidTo,
            warnings);
    }

    public static decimal LandedCost(decimal cost, decimal rateToBase, Supplier supplier)
    {
        var converted = cost * rateToBase;
        return converted + converted * supplier.ExtraCostPercent / 100m + supplier.ExtraCostFixed;
    }

    public static decimal ApplyRule(decimal landedCost, PricingRule? rule)
    {
        if (rule is null)
            return Round(landedCost, PriceRounding.None);

        var price = landedCost * (1 + rule.MarkupPercent / 100m) + rule.FixedMarkup;
        if (rule.MinMargin is { } min && price - landedCost < min)
            price = landedCost + min;
        return Round(price, rule.Rounding);
    }

    /// <summary>Rounds upwards so rounding never eats into the margin.</summary>
    public static decimal Round(decimal price, PriceRounding rounding)
    {
        if (price <= 0)
            return 0;

        switch (rounding)
        {
            case PriceRounding.Whole:
                return Math.Ceiling(price);
            case PriceRounding.Ends99:
            {
                var r = Math.Ceiling(price) - 0.01m;
                return r >= price ? r : r + 1;
            }
            case PriceRounding.Ends9:
            {
                var n = Math.Ceiling(price);
                var lastDigit = n % 10;
                return n + (9 - lastDigit + 10) % 10;
            }
            case PriceRounding.UpTo10:
                return Math.Ceiling(price / 10) * 10;
            case PriceRounding.UpTo50:
                return Math.Ceiling(price / 50) * 50;
            case PriceRounding.UpTo100:
                return Math.Ceiling(price / 100) * 100;
            default:
                return Math.Round(price, 2, MidpointRounding.AwayFromZero);
        }
    }

    public static PricingRule? FindRule(Product product, int supplierId, PricingContext ctx)
    {
        var categoryChain = CategoryChain(product.CategoryId, ctx.CategoryParents);

        PricingRule? best = null;
        var bestScore = (int.MinValue, int.MinValue);
        foreach (var rule in ctx.Rules)
        {
            if (!rule.IsActive)
                continue;
            if (rule.SupplierId is { } s && s != supplierId)
                continue;
            if (rule.BrandId is { } b && b != product.BrandId)
                continue;

            var categoryDepthScore = 0;
            if (rule.CategoryId is { } c)
            {
                var index = categoryChain.IndexOf(c);
                if (index < 0)
                    continue;
                // The product's own category scores highest, ancestors less.
                categoryDepthScore = 100 - index;
            }

            var specificity = (rule.SupplierId is null ? 0 : 1000) + (rule.BrandId is null ? 0 : 500) + categoryDepthScore;
            var score = (rule.Priority, specificity);
            if (best is null || score.CompareTo(bestScore) > 0)
            {
                best = rule;
                bestScore = score;
            }
        }
        return best;
    }

    private static List<int> CategoryChain(int? categoryId, IReadOnlyDictionary<int, int?> parents)
    {
        var chain = new List<int>();
        var current = categoryId;
        while (current is { } id && !chain.Contains(id))
        {
            chain.Add(id);
            current = parents.TryGetValue(id, out var parent) ? parent : null;
        }
        return chain;
    }
}
