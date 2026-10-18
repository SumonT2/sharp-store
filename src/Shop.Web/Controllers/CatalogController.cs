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
        return View("Listing", new ListingViewModel { Title = result.Category.Name, Result = result, Url = url });
    }

    [HttpGet("/products")]
    public async Task<IActionResult> All(CancellationToken ct)
    {
        var url = ListingUrl.FromRequest(Request);
        var result = await catalog.SearchAsync(url.ToQuery(null), ct);
        return View("Listing", new ListingViewModel { Title = "All products", Result = result, Url = url });
    }

    [HttpGet("/search")]
    public async Task<IActionResult> Search(string? q, CancellationToken ct)
    {
        var url = ListingUrl.FromRequest(Request);
        var result = await catalog.SearchAsync(url.ToQuery(null), ct);
        var title = string.IsNullOrWhiteSpace(q) ? "Search" : $"Results for “{q.Trim()}”";
        return View("Listing", new ListingViewModel { Title = title, Result = result, Url = url, Search = q });
    }

    [HttpGet("/p/{slug}")]
    public async Task<IActionResult> Product(string slug, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking()
            .Include(p => p.Brand)
            .Include(p => p.Category)
            .Include(p => p.Specifications).ThenInclude(s => s.Attribute)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Slug == slug && p.IsPublished, ct);
        if (product is null)
            return NotFound();

        var categories = await db.Categories.AsNoTracking().ToListAsync(ct);
        var breadcrumb = new List<Shop.Core.Entities.Category>();
        for (var c = categories.FirstOrDefault(x => x.Id == product.CategoryId); c is not null && breadcrumb.Count < 6;
             c = categories.FirstOrDefault(x => x.Id == c.ParentId))
            breadcrumb.Insert(0, c);

        var groups = product.Specifications
            .OrderBy(s => s.Attribute!.DisplayOrder).ThenBy(s => s.Attribute!.Name)
            .GroupBy(s => s.Attribute!.GroupName ?? "General")
            .Select(g => new SpecGroup(g.Key, g.ToList()))
            .ToList();

        var related = product.CategoryId is { } categoryId
            ? await catalog.TopAsync(4, categoryId: categoryId, excludeProductId: product.Id, ct: ct)
            : [];

        return View("Product", new ProductViewModel
        {
            Product = product,
            Breadcrumb = breadcrumb,
            SpecGroups = groups,
            Related = related,
            MaxQuantity = OrderService.MaxQuantity(product, options.Value),
        });
    }
}
