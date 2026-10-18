using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shop.Infrastructure.Catalog;
using Shop.Infrastructure.Data;
using Shop.Infrastructure.Orders;
using Shop.Infrastructure;
using Microsoft.Extensions.Options;
using Shop.Web.Infrastructure;
using Shop.Web.Models;

namespace Shop.Web.Controllers;

public class CatalogController(CatalogQueryService catalog, ShopDbContext db, IOptions<ShopOptions> options) : Controller
{
    [HttpGet("/c/{slug}")]
    public async Task<IActionResult> Category(string slug, CancellationToken ct)
    {
        var url = ListingUrl.FromRequest(Request);
        var result = await catalog.SearchAsync(url.ToQuery(slug), ct);
        if (result.Category is null)
            return NotFound();
        return Listing(new ListingViewModel { Title = result.Category.Name, Result = result, Url = url });
    }

    [HttpGet("/products")]
    public async Task<IActionResult> All(CancellationToken ct)
    {
        var url = ListingUrl.FromRequest(Request);
        var result = await catalog.SearchAsync(url.ToQuery(null), ct);
        return Listing(new ListingViewModel { Title = "All products", Result = result, Url = url });
    }

    [HttpGet("/search")]
    public async Task<IActionResult> Search(string? q, CancellationToken ct)
    {
        var url = ListingUrl.FromRequest(Request);
        var result = await catalog.SearchAsync(url.ToQuery(null), ct);
        var title = string.IsNullOrWhiteSpace(q) ? "Search" : $"Results for “{q.Trim()}”";
        return Listing(new ListingViewModel { Title = title, Result = result, Url = url, Search = q });
    }

    /// <summary>
    /// Full page normally; just the listing body when the page's script asks for it, so filters can update
    /// the products in place without reloading the page.
    /// </summary>
    private IActionResult Listing(ListingViewModel model)
    {
        Response.Headers.Vary = "X-Partial";
        return Request.Headers["X-Partial"] == "listing"
            ? PartialView("_ListingBody", model)
            : View("Listing", model);
    }

    [HttpGet("/p/{slug}")]
    public async Task<IActionResult> Product(string slug, string? v, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Slug == slug && p.IsPublished, ct);
        if (product is null)
            return NotFound();

        // Variants live on their main product's page, with that option selected.
        if (product.VariantOfId is { } mainId)
        {
            var mainSlug = await db.Products.Where(p => p.Id == mainId && p.IsPublished).Select(p => p.Slug).FirstOrDefaultAsync(ct);
            if (mainSlug is not null)
                return RedirectPermanent($"/p/{mainSlug}?v={Uri.EscapeDataString(product.Sku)}");
        }

        var main = await db.Products.AsNoTracking()
            .Include(p => p.Brand)
            .Include(p => p.Category)
            .Include(p => p.Specifications).ThenInclude(s => s.Attribute)
            .Include(p => p.Variants.Where(x => x.IsPublished)).ThenInclude(x => x.Specifications).ThenInclude(s => s.Attribute)
            .AsSplitQuery()
            .FirstAsync(p => p.Id == product.Id, ct);

        var o = options.Value;
        var all = main.Variants.OrderBy(x => x.Id).Prepend(main).ToList();
        var productOptions = all
            .Select(x => new ProductOption(x, x.VariantLabel ?? (x == main ? "Standard" : x.Name), OrderService.MaxQuantity(x, o)))
            .ToList();

        // Requested option, else the one most people would want: in stock, then cheapest.
        var selected = productOptions.FirstOrDefault(x => string.Equals(x.Product.Sku, v, StringComparison.OrdinalIgnoreCase))
                       ?? productOptions
                           .Where(x => x.Product.IsPurchasable)
                           .OrderBy(x => x.Product.Availability == Shop.Core.Entities.Availability.InStock ? 0 : 1)
                           .ThenBy(x => x.Product.Price)
                           .FirstOrDefault()
                       ?? productOptions[0];

        var categories = await db.Categories.AsNoTracking().ToListAsync(ct);
        var breadcrumb = new List<Shop.Core.Entities.Category>();
        for (var c = categories.FirstOrDefault(x => x.Id == main.CategoryId); c is not null && breadcrumb.Count < 6;
             c = categories.FirstOrDefault(x => x.Id == c.ParentId))
            breadcrumb.Insert(0, c);

        var related = main.CategoryId is { } categoryId
            ? await catalog.TopAsync(4, categoryId: categoryId, excludeProductId: main.Id, ct: ct)
            : [];

        return View("Product", new ProductViewModel
        {
            Product = main,
            Options = productOptions,
            Selected = selected,
            Breadcrumb = breadcrumb,
            SpecGroups = BuildSpecs(all),
            Related = related,
        });
    }

    /// <summary>Specification table rows across all options, grouped and in display order.</summary>
    private static List<SpecGroup> BuildSpecs(List<Shop.Core.Entities.Product> optionsInFamily) =>
        optionsInFamily
            .SelectMany(x => x.Specifications.Select(s => (Option: x, Spec: s)))
            .GroupBy(t => t.Spec.AttributeId)
            .Select(g => (Attribute: g.First().Spec.Attribute!, Values: g.ToDictionary(t => t.Option.Id, t => t.Spec.DisplayValue)))
            .OrderBy(t => t.Attribute.DisplayOrder).ThenBy(t => t.Attribute.Name)
            .GroupBy(t => t.Attribute.GroupName ?? "General")
            .Select(g => new SpecGroup(g.Key, g.Select(t => new SpecRow(t.Attribute.Name, t.Attribute.ShowOnCard, t.Values)).ToList()))
            .ToList();
}
