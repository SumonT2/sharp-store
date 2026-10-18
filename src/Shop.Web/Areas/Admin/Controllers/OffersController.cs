using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shop.Core.Entities;
using Shop.Infrastructure;
using Shop.Infrastructure.Data;
using Shop.Infrastructure.Pricing;
using Shop.Infrastructure.Scraping;
using Shop.Web.Areas.Admin.Models;

namespace Shop.Web.Areas.Admin.Controllers;

public class OffersController(ShopDbContext db, PriceUpdateService pricing, ScrapeService scraper, ShopClock clock) : AdminController
{
    private const int PageSize = 50;

    public async Task<IActionResult> Index(int? supplierId, string? q, string? show, int page = 1, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var query = db.SupplierOffers.AsNoTracking().Include(o => o.Product).Include(o => o.Supplier).AsQueryable();
        if (supplierId is { } sid)
            query = query.Where(o => o.SupplierId == sid);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(o => o.Product!.Sku.ToLower().Contains(term) || o.Product.Name.ToLower().Contains(term)
                                     || (o.SupplierSku != null && o.SupplierSku.ToLower().Contains(term)));
        }
        query = show switch
        {
            "expiring" => query.Where(o => o.ValidTo != null && o.ValidTo > now && o.ValidTo < now.AddDays(3)),
            "expired" => query.Where(o => o.ValidTo != null && o.ValidTo <= now),
            "errors" => query.Where(o => o.LastScrapeError != null),
            "inactive" => query.Where(o => !o.IsActive),
            _ => query,
        };

        var total = await query.CountAsync(ct);
        page = Math.Max(1, page);
        var offers = await query.OrderBy(o => o.Product!.Name).ThenBy(o => o.Supplier!.Name)
            .Skip((page - 1) * PageSize).Take(PageSize).ToListAsync(ct);

        ViewBag.Suppliers = await db.Suppliers.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct);
        ViewBag.SupplierId = supplierId;
        ViewBag.Q = q;
        ViewBag.Show = show;
        ViewBag.Page = page;
        ViewBag.PageCount = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        ViewBag.Total = total;
        return View(offers);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int? id, string? sku, int? supplierId, CancellationToken ct)
    {
        await LoadListsAsync(ct);
        if (id is null or 0)
            return View(new OfferEditInput { ProductSku = sku ?? "", SupplierId = supplierId ?? 0 });

        var o = await db.SupplierOffers.AsNoTracking().Include(x => x.Product).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (o is null)
            return NotFound();
        ViewBag.Offer = o;
        return View(new OfferEditInput
        {
            Id = o.Id, ProductSku = o.Product!.Sku, SupplierId = o.SupplierId, SupplierSku = o.SupplierSku, Cost = o.Cost, Currency = o.Currency,
            StockQuantity = o.StockQuantity, LeadTimeDays = o.LeadTimeDays,
            ValidFrom = o.ValidFrom is { } f ? clock.ToLocal(f) : null,
            ValidTo = o.ValidTo is { } t ? clock.ToLocal(t) : null,
            SourceUrl = o.SourceUrl, IsActive = o.IsActive,
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(OfferEditInput input, CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Sku == input.ProductSku.Trim(), ct);
        if (product is null)
            ModelState.AddModelError(nameof(input.ProductSku), "No product with this SKU.");
        if (!await db.Suppliers.AnyAsync(s => s.Id == input.SupplierId, ct))
            ModelState.AddModelError(nameof(input.SupplierId), "Choose a supplier.");
        if (input.ValidFrom is { } f && input.ValidTo is { } t && t <= f)
            ModelState.AddModelError(nameof(input.ValidTo), "Must be after 'valid from'.");
        if (!string.IsNullOrWhiteSpace(input.Currency) && (input.Currency.Trim().Length != 3 || !input.Currency.Trim().All(char.IsLetter)))
            ModelState.AddModelError(nameof(input.Currency), "Use a 3-letter code like USD or CNY.");
        if (product is not null && await db.SupplierOffers.AnyAsync(o => o.SupplierId == input.SupplierId && o.ProductId == product.Id && o.Id != input.Id, ct))
            ModelState.AddModelError(nameof(input.SupplierId), "This supplier already has an offer for this product. Edit that one instead.");

        if (!ModelState.IsValid)
        {
            await LoadListsAsync(ct);
            return View(input);
        }

        var offer = input.Id == 0 ? new SupplierOffer() : await db.SupplierOffers.FindAsync([input.Id], ct);
        if (offer is null)
            return NotFound();
        if (input.Id == 0)
            db.SupplierOffers.Add(offer);

        var previousProductId = offer.ProductId;
        offer.ProductId = product!.Id;
        offer.SupplierId = input.SupplierId;
        offer.SupplierSku = string.IsNullOrWhiteSpace(input.SupplierSku) ? null : input.SupplierSku.Trim();
        offer.Cost = input.Cost;
        offer.Currency = string.IsNullOrWhiteSpace(input.Currency) ? null : input.Currency.Trim().ToUpperInvariant();
        offer.StockQuantity = input.StockQuantity;
        offer.LeadTimeDays = input.LeadTimeDays;
        offer.ValidFrom = input.ValidFrom is { } vf ? clock.ToUtc(vf) : null;
        offer.ValidTo = input.ValidTo is { } vt ? clock.ToUtc(vt) : null;
        offer.SourceUrl = string.IsNullOrWhiteSpace(input.SourceUrl) ? null : input.SourceUrl.Trim();
        offer.IsActive = input.IsActive;
        offer.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        var ids = new List<int> { offer.ProductId };
        if (previousProductId != 0 && previousProductId != offer.ProductId)
            ids.Add(previousProductId);
        await pricing.RepriceAsync(ids, "Offer edited in admin", ct);
        Flash("Offer saved.");
        return RedirectToAction("Edit", "Products", new { id = offer.ProductId });
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var offer = await db.SupplierOffers.FindAsync([id], ct);
        if (offer is null)
            return NotFound();
        db.SupplierOffers.Remove(offer);
        await db.SaveChangesAsync(ct);
        await pricing.RepriceAsync([offer.ProductId], "Offer deleted", ct);
        Flash("Offer deleted.");
        return RedirectToAction("Edit", "Products", new { id = offer.ProductId });
    }

    [HttpPost]
    public async Task<IActionResult> TestScrape(int id, CancellationToken ct)
    {
        var result = await scraper.TestAsync(id, ct);
        Flash(result.Error is not null
                ? $"Test failed: {result.Error}"
                : $"Found price {result.Price} · stock: {(result.InStock is null ? "unknown" : result.InStock.Value ? "in stock" : "out of stock")}. Nothing was saved.",
            error: result.Error is not null);
        return RedirectToAction(nameof(Edit), new { id });
    }

    private async Task LoadListsAsync(CancellationToken ct)
    {
        ViewBag.Suppliers = await db.Suppliers.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct);
    }
}
