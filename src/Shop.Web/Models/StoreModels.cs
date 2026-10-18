using Shop.Core.Entities;
using Shop.Infrastructure.Catalog;
using Shop.Infrastructure.Orders;
using Shop.Web.Infrastructure;

namespace Shop.Web.Models;

public sealed record CategoryLink(string Name, string Slug, IReadOnlyList<CategoryLink> Children);

public sealed class HomeViewModel
{
    public required IReadOnlyList<CategoryLink> Categories { get; init; }
    public required IReadOnlyList<ProductCard> Featured { get; init; }
    public required IReadOnlyList<ProductCard> PreOrderDeals { get; init; }
    public required IReadOnlyList<ProductCard> Newest { get; init; }
}

public sealed class ListingViewModel
{
    public required string Title { get; init; }
    public required CatalogResult Result { get; init; }
    public required ListingUrl Url { get; init; }
    public string? Search { get; init; }
}

public sealed record SpecGroup(string Name, IReadOnlyList<ProductSpecification> Items);

public sealed class ProductViewModel
{
    public required Product Product { get; init; }
    public required IReadOnlyList<Category> Breadcrumb { get; init; }
    public required IReadOnlyList<SpecGroup> SpecGroups { get; init; }
    public required IReadOnlyList<ProductCard> Related { get; init; }
    public required int MaxQuantity { get; init; }
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
