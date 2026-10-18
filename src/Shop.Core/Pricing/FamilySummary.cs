using Shop.Core.Entities;

namespace Shop.Core.Pricing;

/// <summary>
/// Summarises a product and its variants for listings: the price range of the options you can buy now,
/// the best availability among them, and how many there are.
/// </summary>
public sealed record FamilySummary(
    decimal? MinPrice,
    decimal? MaxPrice,
    Availability Availability,
    int? LeadTimeDays,
    DateTime? OfferEndsAt,
    int OptionCount)
{
    public static FamilySummary Compute(Product main, IEnumerable<Product> variants)
    {
        var members = variants.Where(v => v.IsPublished).Prepend(main).ToList();
        var buyable = members.Where(m => m.IsPurchasable && m.StockQuantity is null or > 0).ToList();

        // Prices shown are those of options you can buy; if none, the last known prices.
        var priced = (buyable.Count > 0 ? buyable : members).Where(m => m.Price is > 0).ToList();

        // The option a shopper would most likely pick: in stock beats pre-order, then the cheapest.
        var best = buyable
            .OrderBy(m => m.Availability == Availability.InStock ? 0 : 1)
            .ThenBy(m => m.Price)
            .FirstOrDefault();

        return new FamilySummary(
            priced.Count > 0 ? priced.Min(m => m.Price) : null,
            priced.Count > 0 ? priced.Max(m => m.Price) : null,
            best?.Availability ?? Availability.OutOfStock,
            best?.LeadTimeDays,
            best?.Availability == Availability.PreOrder ? best.OfferEndsAt : null,
            buyable.Count);
    }

    /// <summary>Copies the summary onto the main product; returns whether anything changed.</summary>
    public bool ApplyTo(Product main)
    {
        var changed = main.ListPrice != MinPrice || main.ListPriceMax != MaxPrice || main.ListAvailability != Availability
                      || main.ListLeadTimeDays != LeadTimeDays || main.ListOfferEndsAt != OfferEndsAt || main.ListOptionCount != OptionCount;
        main.ListPrice = MinPrice;
        main.ListPriceMax = MaxPrice;
        main.ListAvailability = Availability;
        main.ListLeadTimeDays = LeadTimeDays;
        main.ListOfferEndsAt = OfferEndsAt;
        main.ListOptionCount = OptionCount;
        return changed;
    }
}
