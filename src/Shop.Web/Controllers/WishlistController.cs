using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shop.Infrastructure.Catalog;
using Shop.Infrastructure.Data;
using Shop.Web.Infrastructure;

namespace Shop.Web.Controllers;

public class WishlistController(WishlistCookie wishlist, CatalogQueryService catalog, ShopDbContext db) : Controller
{
    [HttpGet("/wishlist")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var cards = await catalog.CardsByIdsAsync(wishlist.Read(), ct);
        // Forget products that were deleted or unpublished since they were saved.
        var missing = wishlist.Read().Except(cards.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
            wishlist.Remove(missing);
        return View(cards);
    }

    /// <summary>
    /// Saves or un-saves a product. Script requests (X-Requested-With: fetch) get JSON; plain form posts go back to the page.
    /// Options (variants) are saved as their main product.
    /// </summary>
    [HttpPost("/wishlist/toggle")]
    public async Task<IActionResult> Toggle(int productId, string? returnUrl, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking()
            .Where(p => p.Id == productId && p.IsPublished)
            .Select(p => new { Id = p.VariantOfId ?? p.Id, p.Name })
            .FirstOrDefaultAsync(ct);
        if (product is null)
            return NotFound();

        var saved = wishlist.Toggle(product.Id);
        if (Request.Headers.XRequestedWith == "fetch")
            return Json(new { productId = product.Id, saved, count = wishlist.Count });

        TempData["Flash"] = saved ? $"Saved “{product.Name}” to your wishlist." : $"Removed “{product.Name}” from your wishlist.";
        return Redirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "/wishlist");
    }
}
