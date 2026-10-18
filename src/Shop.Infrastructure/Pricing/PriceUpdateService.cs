using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shop.Core.Entities;
using Shop.Core.Pricing;
using Shop.Infrastructure.Data;

namespace Shop.Infrastructure.Pricing;

public sealed record RepriceSummary(int Checked, int Changed, IReadOnlyList<string> Warnings);

/// <summary>Recalculates product prices and availability from supplier offers and stores the result on each product.</summary>
public class PriceUpdateService(ShopDbContext db, ShopClock clock, IOptions<ShopOptions> options, ILogger<PriceUpdateService> logger)
{
    private const int BatchSize = 500;

    public async Task<PricingContext> BuildContextAsync(CancellationToken ct = default)
    {
        var o = options.Value;
        return new PricingContext
        {
            BaseCurrency = o.BaseCurrency.ToUpperInvariant(),
            RatesToBase = await db.CurrencyRates.AsNoTracking().ToDictionaryAsync(r => r.Code.ToUpper(), r => r.RateToBase, ct),
            Rules = await db.PricingRules.AsNoTracking().Where(r => r.IsActive).ToListAsync(ct),
            CategoryParents = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.ParentId, ct),
            InStockMaxLeadDays = o.InStockMaxLeadDays,
            NowUtc = clock.UtcNow,
        };
    }

    public Task<RepriceSummary> RepriceAllAsync(string reason, CancellationToken ct = default) =>
        RepriceAsync(null, reason, ct);

    public async Task<RepriceSummary> RepriceAsync(IReadOnlyCollection<int>? productIds, string reason, CancellationToken ct = default)
    {
        var ctx = await BuildContextAsync(ct);
        var warnings = new HashSet<string>();
        int checkedCount = 0, changed = 0;

        var ids = productIds?.Distinct().Order().ToList()
                  ?? await db.Products.AsNoTracking().OrderBy(p => p.Id).Select(p => p.Id).ToListAsync(ct);

        foreach (var chunk in ids.Chunk(BatchSize))
        {
            var products = await db.Products
                .Where(p => chunk.Contains(p.Id))
                .Include(p => p.Offers).ThenInclude(o => o.Supplier)
                .AsSplitQuery()
                .ToListAsync(ct);

            foreach (var product in products)
            {
                checkedCount++;
                var result = PricingEngine.Evaluate(product, product.Offers, ctx);
                foreach (var w in result.Warnings)
                    warnings.Add(w);

                if (Apply(product, result, reason))
                    changed++;
            }

            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }

        if (changed > 0)
            logger.LogInformation("Repriced {Checked} products, {Changed} changed ({Reason})", checkedCount, changed, reason);
        foreach (var w in warnings)
            logger.LogWarning("Pricing warning: {Warning}", w);

        return new RepriceSummary(checkedCount, changed, warnings.ToList());
    }

    private bool Apply(Product product, PricingResult result, string reason)
    {
        var price = result.Price is { } p ? Math.Round(p, 2) : (decimal?)null;
        var changed = product.Price != price
                      || product.Availability != result.Availability
                      || product.StockQuantity != result.StockQuantity
                      || product.LeadTimeDays != result.LeadTimeDays
                      || product.OfferEndsAt != result.OfferEndsAt
                      || product.ActiveOfferId != result.Best?.Offer.Id;

        if (product.Price != price)
        {
            db.PriceHistory.Add(new PriceHistory
            {
                ProductId = product.Id,
                OldPrice = product.Price,
                NewPrice = price,
                Reason = reason.Length > 200 ? reason[..200] : reason,
                ChangedAt = clock.UtcNow,
            });
        }

        product.Price = price;
        product.Availability = result.Availability;
        product.StockQuantity = result.StockQuantity;
        product.LeadTimeDays = result.LeadTimeDays;
        product.OfferEndsAt = result.OfferEndsAt;
        product.ActiveOfferId = result.Best?.Offer.Id;
        product.PricedAt = clock.UtcNow;
        return changed;
    }
}
