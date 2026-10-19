using Microsoft.EntityFrameworkCore;
using Shop.Core.Entities;
using Shop.Core.Text;
using Shop.Infrastructure.Data;

namespace Shop.Infrastructure.Catalog;

public enum CatalogSort
{
    Featured,
    PriceLowHigh,
    PriceHighLow,
    Newest,
    NameAz,
}

public sealed class CatalogQuery
{
    public string? CategorySlug { get; set; }
    public string? Search { get; set; }
    public List<string> Brands { get; set; } = new();
    public List<Availability> Availability { get; set; } = new();
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    /// <summary>Attribute slug → selected values. Values within one attribute are OR-ed; attributes are AND-ed.</summary>
    public Dictionary<string, List<string>> Specs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public CatalogSort Sort { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 24;
}

public sealed record FacetValue(string Value, string Label, int Count, bool Selected);
public sealed record Facet(string Key, string Name, IReadOnlyList<FacetValue> Values);

public sealed record ProductCard(
    int Id,
    string Name,
    string Slug,
    string Sku,
    string? Brand,
    string? ImageUrl,
    decimal? Price,
    decimal? CompareAtPrice,
    Availability Availability,
    int? LeadTimeDays,
    DateTime? OfferEndsAt,
    IReadOnlyList<string> Highlights)
{
    public string? ShortDescription { get; init; }
    public bool IsFeatured { get; init; }
    public bool CanBuy { get; init; }
    /// <summary>Highest option price, when the product has variants at different prices.</summary>
    public decimal? PriceMax { get; init; }
    /// <summary>Options that can be bought now.</summary>
    public int OptionCount { get; init; }
    /// <summary>The product comes in options, so "add to cart" happens on the product page.</summary>
    public bool HasVariants { get; init; }
}

public sealed record CatalogResult(
    Category? Category,
    IReadOnlyList<Category> Breadcrumb,
    IReadOnlyList<Category> Subcategories,
    IReadOnlyList<ProductCard> Products,
    int Total,
    int Page,
    int PageSize,
    Facet BrandFacet,
    Facet AvailabilityFacet,
    IReadOnlyList<Facet> SpecFacets,
    decimal? PriceFloor,
    decimal? PriceCeiling)
{
    public int PageCount => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
}

/// <summary>
/// Storefront product listing with faceted filtering. Facet counts are "disjunctive": the counts for a facet
/// ignore that facet's own selection, so shoppers can see how many results each extra choice would add.
/// </summary>
public class CatalogQueryService(ShopDbContext db)
{
    private const string BrandKey = "brand";
    private const string AvailabilityKey = "availability";
    private const string PriceKey = "price";
    private const int MaxFacetValues = 30;

    public async Task<CatalogResult> SearchAsync(CatalogQuery query, CancellationToken ct = default)
    {
        query.Page = Math.Max(1, query.Page);
        query.PageSize = Math.Clamp(query.PageSize, 1, 96);

        var categories = await db.Categories.AsNoTracking().OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name).ToListAsync(ct);
        Category? category = null;
        List<int>? categoryIds = null;
        if (!string.IsNullOrEmpty(query.CategorySlug))
        {
            category = categories.FirstOrDefault(c => c.Slug == query.CategorySlug);
            categoryIds = category is null ? [] : Descendants(category.Id, categories);
        }

        var brandIds = query.Brands.Count == 0
            ? []
            : await db.Brands.Where(b => query.Brands.Contains(b.Slug)).Select(b => b.Id).ToListAsync(ct);

        var specSlugs = query.Specs.Where(kv => kv.Value.Count > 0).Select(kv => kv.Key).ToList();
        var attributes = await db.SpecificationAttributes.AsNoTracking().Where(a => a.IsFilterable).ToListAsync(ct);
        var selectedSpecs = attributes
            .Where(a => specSlugs.Contains(a.Slug, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(a => a.Id, a => query.Specs[a.Slug]);

        IQueryable<Product> Filtered(string? exclude = null)
        {
            // One row per product family: variants are folded into their main product.
            var q = db.Products.AsNoTracking().Where(p => p.IsPublished && p.VariantOfId == null);
            if (categoryIds is not null)
                q = q.Where(p => p.CategoryId != null && categoryIds.Contains(p.CategoryId.Value));

            foreach (var term in SearchTerms(query.Search))
            {
                q = q.Where(p => p.Name.ToLower().Contains(term)
                                 || p.Sku.ToLower().Contains(term)
                                 || (p.Brand != null && p.Brand.Name.ToLower().Contains(term))
                                 || p.Variants.Any(v => v.IsPublished && (v.Sku.ToLower().Contains(term)
                                                                          || (v.VariantLabel != null && v.VariantLabel.ToLower().Contains(term)))));
            }

            if (exclude != BrandKey && query.Brands.Count > 0)
                q = q.Where(p => p.BrandId != null && brandIds.Contains(p.BrandId.Value));
            if (exclude != AvailabilityKey && query.Availability.Count > 0)
                q = q.Where(p => query.Availability.Contains(p.ListAvailability));
            if (exclude != PriceKey)
            {
                // A family matches when any part of its price range falls inside the requested range.
                if (query.MinPrice is { } min)
                    q = q.Where(p => (p.ListPriceMax ?? p.ListPrice) >= min);
                if (query.MaxPrice is { } max)
                    q = q.Where(p => p.ListPrice <= max);
            }

            foreach (var (attributeId, values) in selectedSpecs)
            {
                if (exclude == SpecKey(attributeId))
                    continue;
                q = q.Where(p => p.Specifications.Any(s => s.AttributeId == attributeId && values.Contains(s.Value))
                                 || p.Variants.Any(v => v.IsPublished && v.Specifications.Any(s => s.AttributeId == attributeId && values.Contains(s.Value))));
            }
            return q;
        }

        var filtered = Filtered();
        var total = await filtered.CountAsync(ct);
        var page = Math.Min(query.Page, Math.Max(1, (int)Math.Ceiling(total / (double)query.PageSize)));

        var cards = await ToCardsAsync(Sort(filtered, query.Sort).Skip((page - 1) * query.PageSize).Take(query.PageSize), ct);

        var brandFacet = await BrandFacetAsync(Filtered(BrandKey), query, ct);
        var availabilityFacet = await AvailabilityFacetAsync(Filtered(AvailabilityKey), query, ct);
        var specFacets = await SpecFacetsAsync(filtered, Filtered, attributes, selectedSpecs, ct);

        var priceBounds = await Filtered(PriceKey)
            .Where(p => p.ListPrice != null)
            .GroupBy(_ => 1)
            .Select(g => new { Min = g.Min(p => p.ListPrice), Max = g.Max(p => p.ListPriceMax ?? p.ListPrice) })
            .FirstOrDefaultAsync(ct);

        var breadcrumb = new List<Category>();
        for (var c = category; c is not null; c = categories.FirstOrDefault(x => x.Id == c.ParentId))
            breadcrumb.Insert(0, c);
        var subcategories = categories.Where(c => c.ParentId == category?.Id).ToList();

        return new CatalogResult(
            category, breadcrumb, subcategories, cards, total, page, query.PageSize,
            brandFacet, availabilityFacet, specFacets, priceBounds?.Min, priceBounds?.Max);
    }

    /// <summary>A short list of published products, e.g. for the home page or "related products".</summary>
    public async Task<IReadOnlyList<ProductCard>> TopAsync(
        int take,
        CatalogSort sort = CatalogSort.Featured,
        Availability? availability = null,
        int? categoryId = null,
        int? excludeProductId = null,
        bool featuredOnly = false,
        CancellationToken ct = default)
    {
        var q = db.Products.AsNoTracking().Where(p => p.IsPublished && p.VariantOfId == null && p.ListPrice != null);
        if (featuredOnly)
            q = q.Where(p => p.IsFeatured);
        if (availability is { } a)
            q = q.Where(p => p.ListAvailability == a);
        else
            q = q.Where(p => p.ListAvailability != Availability.OutOfStock);
        if (categoryId is { } c)
            q = q.Where(p => p.CategoryId == c);
        if (excludeProductId is { } x)
            q = q.Where(p => p.Id != x);
        if (availability == Availability.PreOrder)
            q = q.OrderBy(p => p.ListOfferEndsAt == null).ThenBy(p => p.ListOfferEndsAt).ThenBy(p => p.Id);
        else
            q = Sort(q, sort);
        return await ToCardsAsync(q.Take(take), ct);
    }

    /// <summary>Cards for these main products in the given order (wishlist), including ones that are out of stock.</summary>
    public async Task<IReadOnlyList<ProductCard>> CardsByIdsAsync(IReadOnlyList<int> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0)
            return [];
        var cards = await ToCardsAsync(db.Products.AsNoTracking().Where(p => ids.Contains(p.Id) && p.IsPublished && p.VariantOfId == null), ct);
        return cards.OrderBy(c => ids.ToList().IndexOf(c.Id)).ToList();
    }

    /// <summary>The card for one published main product, or null.</summary>
    public async Task<ProductCard?> CardBySkuAsync(string sku, CancellationToken ct = default) =>
        (await ToCardsAsync(db.Products.AsNoTracking().Where(p => p.Sku == sku && p.IsPublished && p.VariantOfId == null && p.ListPrice != null), ct)).FirstOrDefault();

    /// <summary>
    /// Best sellers by units ordered since <paramref name="since"/> (cancelled orders excluded). Options count towards
    /// their main product. Products that can't be bought right now are skipped.
    /// </summary>
    public async Task<IReadOnlyList<ProductCard>> TopSellingAsync(int take, DateTime since, CancellationToken ct = default)
    {
        var ranked = await db.OrderItems.AsNoTracking()
            .Join(db.Orders, i => i.OrderId, o => o.Id, (i, o) => new { i.ProductId, i.Quantity, o.CreatedAt, o.Status })
            .Where(x => x.CreatedAt >= since && x.Status != OrderStatus.Cancelled)
            .Join(db.Products, x => x.ProductId, p => p.Id, (x, p) => new { MainId = p.VariantOfId ?? p.Id, x.Quantity })
            .GroupBy(x => x.MainId)
            .Select(g => new { Id = g.Key, Units = g.Sum(x => x.Quantity) })
            .OrderByDescending(x => x.Units).ThenBy(x => x.Id)
            .Take(take * 2)
            .ToListAsync(ct);
        if (ranked.Count == 0)
            return [];

        var ids = ranked.Select(r => r.Id).ToList();
        var cards = await ToCardsAsync(db.Products.AsNoTracking()
            .Where(p => ids.Contains(p.Id) && p.IsPublished && p.VariantOfId == null && p.ListPrice != null && p.ListAvailability != Availability.OutOfStock), ct);
        return cards.OrderBy(c => ids.IndexOf(c.Id)).Take(take).ToList();
    }

    /// <summary>A photo for each category, taken from its products (featured ones first), for category tiles.</summary>
    public async Task<IReadOnlyDictionary<int, string>> CategoryPhotosAsync(IReadOnlyCollection<int> categoryIds, CancellationToken ct = default)
    {
        var rows = await db.Products.AsNoTracking()
            .Where(p => p.IsPublished && p.VariantOfId == null && p.ImageUrl != null && p.CategoryId != null && categoryIds.Contains(p.CategoryId.Value))
            .OrderByDescending(p => p.IsFeatured).ThenBy(p => p.ListAvailability == Availability.OutOfStock).ThenBy(p => p.Id)
            .Select(p => new { CategoryId = p.CategoryId!.Value, p.ImageUrl })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.CategoryId).ToDictionary(g => g.Key, g => g.First().ImageUrl!);
    }

    private async Task<List<ProductCard>> ToCardsAsync(IQueryable<Product> q, CancellationToken ct)
    {
        var rows = await q
            .Select(p => new
            {
                p.Id, p.Name, p.Slug, p.Sku, p.ShortDescription, p.IsFeatured,
                Brand = p.Brand != null ? p.Brand.Name : null,
                p.ImageUrl, p.ListPrice, p.ListPriceMax, p.CompareAtPrice, p.ListAvailability, p.ListLeadTimeDays, p.ListOfferEndsAt, p.ListOptionCount,
                HasVariants = p.Variants.Any(v => v.IsPublished),
                Highlights = p.Specifications
                    .Where(s => s.Attribute!.ShowOnCard)
                    .Select(s => new { s.AttributeId, s.Attribute!.DisplayOrder, s.Value, s.Attribute.Unit })
                    .ToList(),
                VariantHighlights = p.Variants
                    .Where(v => v.IsPublished)
                    .SelectMany(v => v.Specifications.Where(s => s.Attribute!.ShowOnCard))
                    .Select(s => new { s.AttributeId, s.Attribute!.DisplayOrder, s.Value, s.Attribute.Unit })
                    .ToList(),
            })
            .ToListAsync(ct);

        return rows.Select(r => new ProductCard(
            r.Id, r.Name, r.Slug, r.Sku, r.Brand, r.ImageUrl, r.ListPrice, r.CompareAtPrice, r.ListAvailability, r.ListLeadTimeDays, r.ListOfferEndsAt,
            // Where options differ (e.g. switch type): "Red / Brown", or "Red +2" when there are more.
            r.Highlights.Concat(r.VariantHighlights)
                .GroupBy(h => new { h.AttributeId, h.DisplayOrder, h.Unit })
                .OrderBy(g => g.Key.DisplayOrder)
                .Select(g =>
                {
                    var values = g.Select(h => h.Value).Distinct().ToList();
                    var text = values.Count switch
                    {
                        1 => values[0],
                        2 => $"{values[0]} / {values[1]}",
                        _ => $"{values[0]} +{values.Count - 1}",
                    };
                    return string.IsNullOrEmpty(g.Key.Unit) ? text : $"{text} {g.Key.Unit}";
                })
                .ToList())
        {
            ShortDescription = r.ShortDescription,
            IsFeatured = r.IsFeatured,
            PriceMax = r.ListPriceMax,
            OptionCount = r.ListOptionCount,
            HasVariants = r.HasVariants,
            CanBuy = r.ListOptionCount > 0,
        }).ToList();
    }

    public static IEnumerable<string> SearchTerms(string? search) =>
        string.IsNullOrWhiteSpace(search)
            ? []
            : search.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(8);

    private static IQueryable<Product> Sort(IQueryable<Product> q, CatalogSort sort) => sort switch
    {
        CatalogSort.PriceLowHigh => q.OrderBy(p => p.ListPrice == null).ThenBy(p => p.ListPrice).ThenBy(p => p.Id),
        CatalogSort.PriceHighLow => q.OrderBy(p => p.ListPrice == null).ThenByDescending(p => p.ListPriceMax ?? p.ListPrice).ThenBy(p => p.Id),
        CatalogSort.Newest => q.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id),
        CatalogSort.NameAz => q.OrderBy(p => p.Name).ThenBy(p => p.Id),
        // Featured: things you can buy first (in stock, then pre-order), featured products first within each group.
        _ => q.OrderBy(p => p.ListAvailability == Availability.InStock ? 0 : p.ListAvailability == Availability.PreOrder ? 1 : 2)
              .ThenByDescending(p => p.IsFeatured)
              .ThenByDescending(p => p.CreatedAt)
              .ThenByDescending(p => p.Id),
    };

    private async Task<Facet> BrandFacetAsync(IQueryable<Product> q, CatalogQuery query, CancellationToken ct)
    {
        var counts = await q.Where(p => p.BrandId != null)
            .GroupBy(p => new { p.Brand!.Slug, p.Brand.Name })
            .Select(g => new { g.Key.Slug, g.Key.Name, Count = g.Count() })
            .ToListAsync(ct);

        var values = counts
            .OrderByDescending(c => c.Count).ThenBy(c => c.Name)
            .Take(MaxFacetValues)
            .Select(c => new FacetValue(c.Slug, c.Name, c.Count, query.Brands.Contains(c.Slug, StringComparer.OrdinalIgnoreCase)))
            .OrderBy(v => v.Label)
            .ToList();
        return new Facet(BrandKey, "Brand", values);
    }

    private static async Task<Facet> AvailabilityFacetAsync(IQueryable<Product> q, CatalogQuery query, CancellationToken ct)
    {
        var counts = await q.GroupBy(p => p.ListAvailability).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var values = counts
            .OrderBy(c => c.Key == Availability.InStock ? 0 : c.Key == Availability.PreOrder ? 1 : 2)
            .Select(c => new FacetValue(AvailabilitySlug(c.Key), AvailabilityLabel(c.Key), c.Count, query.Availability.Contains(c.Key)))
            .ToList();
        return new Facet(AvailabilityKey, "Availability", values);
    }

    private async Task<IReadOnlyList<Facet>> SpecFacetsAsync(
        IQueryable<Product> filtered,
        Func<string?, IQueryable<Product>> filteredExcluding,
        List<SpecificationAttribute> attributes,
        Dictionary<int, List<string>> selected,
        CancellationToken ct)
    {
        // One query for every attribute that has no selection...
        var counts = await CountSpecValues(filtered).ToListAsync(ct);
        var byAttribute = counts
            .Where(c => !selected.ContainsKey(c.AttributeId))
            .GroupBy(c => c.AttributeId)
            .ToDictionary(g => g.Key, g => g.Select(x => (x.Value, x.Count)).ToList());

        // ...and one per selected attribute, ignoring its own selection.
        foreach (var attributeId in selected.Keys)
        {
            var own = await CountSpecValues(filteredExcluding(SpecKey(attributeId)), attributeId).ToListAsync(ct);
            byAttribute[attributeId] = own.Select(x => (x.Value, x.Count)).ToList();
        }

        var facets = new List<Facet>();
        foreach (var attribute in attributes.OrderBy(a => a.DisplayOrder).ThenBy(a => a.Name))
        {
            if (!byAttribute.TryGetValue(attribute.Id, out var valueCounts) || valueCounts.Count == 0)
                continue;
            var selectedValues = selected.GetValueOrDefault(attribute.Id) ?? [];

            // A facet with a single value that every result shares doesn't help narrow anything down.
            if (valueCounts.Count == 1 && selectedValues.Count == 0)
                continue;

            var values = OrderValues(valueCounts)
                .Take(MaxFacetValues)
                .Select(v => new FacetValue(
                    v.Value,
                    string.IsNullOrEmpty(attribute.Unit) ? v.Value : $"{v.Value} {attribute.Unit}",
                    v.Count,
                    selectedValues.Contains(v.Value, StringComparer.OrdinalIgnoreCase)))
                .ToList();
            facets.Add(new Facet(attribute.Slug, attribute.Name, values));
        }
        return facets;
    }

    private IQueryable<SpecCount> CountSpecValues(IQueryable<Product> products, int? onlyAttributeId = null)
    {
        // Count product families, not rows: a keyboard offered with three switches counts once per switch value.
        var ids = products.Select(p => p.Id);
        var specs = db.ProductSpecifications.AsNoTracking().Where(s =>
            s.Attribute!.IsFilterable
            && (ids.Contains(s.ProductId)
                || (s.Product!.VariantOfId != null && s.Product.IsPublished && ids.Contains(s.Product.VariantOfId.Value))));
        if (onlyAttributeId is { } attributeId)
            specs = specs.Where(s => s.AttributeId == attributeId);
        return specs
            .Select(s => new { s.AttributeId, s.Value, Family = s.Product!.VariantOfId ?? s.ProductId })
            .GroupBy(s => new { s.AttributeId, s.Value })
            .Select(g => new SpecCount(g.Key.AttributeId, g.Key.Value, g.Select(x => x.Family).Distinct().Count()));
    }

    /// <summary>Numbers sort numerically (8, 12, 16), text alphabetically.</summary>
    private static IEnumerable<(string Value, int Count)> OrderValues(List<(string Value, int Count)> values)
    {
        var allNumeric = values.All(v => TextUtil.ParseDecimal(v.Value) is not null && char.IsDigit(v.Value.TrimStart()[0]));
        return allNumeric
            ? values.OrderBy(v => TextUtil.ParseDecimal(v.Value))
            : values.OrderBy(v => v.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static string SpecKey(int attributeId) => $"spec:{attributeId}";

    private static List<int> Descendants(int rootId, List<Category> all)
    {
        var result = new List<int> { rootId };
        for (var i = 0; i < result.Count; i++)
            result.AddRange(all.Where(c => c.ParentId == result[i] && !result.Contains(c.Id)).Select(c => c.Id));
        return result;
    }

    public static string AvailabilitySlug(Availability a) => a switch
    {
        Availability.InStock => "in-stock",
        Availability.PreOrder => "pre-order",
        _ => "out-of-stock",
    };

    public static Availability? ParseAvailability(string slug) => slug switch
    {
        "in-stock" => Availability.InStock,
        "pre-order" => Availability.PreOrder,
        "out-of-stock" => Availability.OutOfStock,
        _ => null,
    };

    public static string AvailabilityLabel(Availability a) => a switch
    {
        Availability.InStock => "In stock",
        Availability.PreOrder => "Pre-order",
        _ => "Out of stock",
    };

    private sealed record SpecCount(int AttributeId, string Value, int Count);
}
