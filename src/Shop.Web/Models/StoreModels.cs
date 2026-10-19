using Shop.Core.Entities;
using Shop.Infrastructure;
using Shop.Infrastructure.Catalog;
using Shop.Infrastructure.Orders;
using Shop.Web.Infrastructure;

namespace Shop.Web.Models;

public sealed record CategoryLink(int Id, string Name, string Slug, IReadOnlyList<CategoryLink> Children);

public sealed record CategoryTile(string Name, string Slug, string? ImageUrl);

public sealed class HomeViewModel
{
    public required HomeBanner Hero { get; init; }
    /// <summary>The product the hero advertises, when it names one that is on sale.</summary>
    public ProductCard? HeroProduct { get; init; }
    public HomeBanner? Promo { get; init; }
    public required IReadOnlyList<CategoryTile> Categories { get; init; }
    public required IReadOnlyList<ProductCard> Featured { get; init; }
    public required IReadOnlyList<ProductCard> PreOrderDeals { get; init; }
    /// <summary>When the soonest pre-order offer window closes.</summary>
    public DateTime? DealsEndAt { get; init; }
    /// <summary>Best sellers when there are orders to rank by; otherwise the newest products.</summary>
    public required IReadOnlyList<ProductCard> Popular { get; init; }
    public bool PopularIsRanked { get; init; }
}

public sealed class ListingViewModel
{
    public required string Title { get; init; }
    public required CatalogResult Result { get; init; }
    public required ListingUrl Url { get; init; }
    public string? Search { get; init; }
}

/// <summary>One buyable option on the product page: the product itself or one of its variants.</summary>
public sealed record ProductOption(Product Product, string Label, int MaxQuantity);

/// <summary>A specification row; values are per option id, so the page can switch them when another option is picked.</summary>
public sealed record SpecRow(string Name, bool ShowOnCard, IReadOnlyDictionary<int, string> Values)
{
    public bool Varies => Values.Values.Distinct().Count() > 1;
    public string ValueFor(int optionId) => Values.TryGetValue(optionId, out var v) ? v : "—";
}

public sealed record SpecGroup(string Name, IReadOnlyList<SpecRow> Rows);

public sealed class ProductViewModel
{
    /// <summary>The main product: name, brand, description and images shared by all options.</summary>
    public required Product Product { get; init; }
    public required IReadOnlyList<ProductOption> Options { get; init; }
    public required ProductOption Selected { get; init; }
    public required IReadOnlyList<Category> Breadcrumb { get; init; }
    public required IReadOnlyList<SpecGroup> SpecGroups { get; init; }
    public required IReadOnlyList<ProductCard> Related { get; init; }
    public bool HasChoice => Options.Count > 1;
}

public sealed class CheckoutViewModel
{
    public required CartView Cart { get; init; }
    public CheckoutInput Input { get; init; } = new();
    public string? Error { get; init; }
}

public sealed class ErrorViewModel
{
    public int StatusCode { get; init; }
    public string? RequestId { get; init; }
}
