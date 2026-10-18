using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Shop.Core.Entities;
using Shop.Infrastructure.Catalog;
using Shop.Web.Infrastructure;
using Shop.Web.Models;

namespace Shop.Web.Controllers;

public class HomeController(CatalogQueryService catalog, CategoryMenu menu) : Controller
{
    [HttpGet("/")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var featured = await catalog.TopAsync(10, CatalogSort.Featured, featuredOnly: true, ct: ct);
        var featuredIds = featured.Select(p => p.Id).ToHashSet();
        var model = new HomeViewModel
        {
            Categories = await menu.GetAsync(),
            Featured = featured,
            // New arrivals that aren't already shown in the featured row.
            InStock = (await catalog.TopAsync(12 + featuredIds.Count, CatalogSort.Newest, ct: ct)).Where(p => !featuredIds.Contains(p.Id)).Take(12).ToList(),
            PreOrderDeals = await catalog.TopAsync(4, availability: Availability.PreOrder, ct: ct),
            Newest = await catalog.TopAsync(4, CatalogSort.Newest, ct: ct),
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
