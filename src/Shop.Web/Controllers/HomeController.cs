using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shop.Core.Entities;
using Shop.Infrastructure;
using Shop.Infrastructure.Catalog;
using Shop.Web.Infrastructure;
using Shop.Web.Models;

namespace Shop.Web.Controllers;

public class HomeController(CatalogQueryService catalog, CategoryMenu menu, ShopClock clock, IOptions<ShopOptions> options) : Controller
{
    private const int RowSize = 5;

    [HttpGet("/")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var shop = options.Value;
        var featured = await catalog.TopAsync(RowSize, CatalogSort.Featured, featuredOnly: true, ct: ct);
        var deals = await catalog.TopAsync(4, availability: Availability.PreOrder, ct: ct);

        // Best sellers of the last 30 days; a young shop without orders shows its newest products instead.
        var popular = await catalog.TopSellingAsync(RowSize, clock.UtcNow.AddDays(-30), ct);
        var ranked = popular.Count >= 3;
        if (!ranked)
        {
            var shown = featured.Select(p => p.Id).ToHashSet();
            popular = (await catalog.TopAsync(RowSize + shown.Count, CatalogSort.Newest, ct: ct)).Where(p => !shown.Contains(p.Id)).Take(RowSize).ToList();
        }

        // Category tiles: the most specific categories (sub-categories, or a top category without any).
        var leaves = (await menu.GetAsync()).SelectMany(c => c.Children.Count > 0 ? c.Children : [c]).ToList();
        var photos = await catalog.CategoryPhotosAsync(leaves.Select(c => c.Id).ToList(), ct);

        var model = new HomeViewModel
        {
            Hero = shop.Hero,
            HeroProduct = string.IsNullOrWhiteSpace(shop.Hero.ProductSku) ? null : await catalog.CardBySkuAsync(shop.Hero.ProductSku, ct),
            Promo = string.IsNullOrWhiteSpace(shop.Promo?.Title) ? null : shop.Promo,
            Categories = leaves.Select(c => new CategoryTile(c.Name, c.Slug, photos.TryGetValue(c.Id, out var url) ? url : null)).ToList(),
            Featured = featured,
            PreOrderDeals = deals,
            DealsEndAt = deals.Where(d => d.OfferEndsAt is not null).Min(d => d.OfferEndsAt),
            Popular = popular,
            PopularIsRanked = ranked,
        };
        return View(model);
    }

    [HttpGet("/error/{code:int?}")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error(int? code)
    {
        var status = code ?? 500;
        Response.StatusCode = status;
        return View(new ErrorViewModel { StatusCode = status, RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
