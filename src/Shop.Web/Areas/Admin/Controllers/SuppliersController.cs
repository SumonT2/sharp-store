using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shop.Core.Entities;
using Shop.Infrastructure.Data;
using Shop.Infrastructure.Pricing;
using Shop.Web.Areas.Admin.Models;

namespace Shop.Web.Areas.Admin.Controllers;

public class SuppliersController(ShopDbContext db, PriceUpdateService pricing) : AdminController
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var suppliers = await db.Suppliers.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct);
        ViewBag.OfferCounts = await db.SupplierOffers.GroupBy(o => o.SupplierId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return View(suppliers);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int? id, CancellationToken ct)
    {
        if (id is null or 0)
            return View(new SupplierEditInput());
        var s = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null)
            return NotFound();
        return View(new SupplierEditInput
        {
            Id = s.Id, Name = s.Name, Code = s.Code, Kind = s.Kind, Currency = s.Currency, DefaultLeadTimeDays = s.DefaultLeadTimeDays,
            ExtraCostPercent = s.ExtraCostPercent, ExtraCostFixed = s.ExtraCostFixed, IsActive = s.IsActive, Notes = s.Notes,
            ImportColumnMap = s.ImportColumnMap, ScrapePriceSelector = s.ScrapePriceSelector, ScrapeStockSelector = s.ScrapeStockSelector,
            ScrapeInStockPattern = s.ScrapeInStockPattern, ScrapeDelaySeconds = s.ScrapeDelaySeconds,
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(SupplierEditInput input, CancellationToken ct)
    {
        input.Code = input.Code?.Trim().ToUpperInvariant() ?? "";
        if (await db.Suppliers.AnyAsync(s => s.Code == input.Code && s.Id != input.Id, ct))
            ModelState.AddModelError(nameof(input.Code), "Another supplier already uses this code.");
        if (!ModelState.IsValid)
            return View(input);

        var supplier = input.Id == 0 ? new Supplier() : await db.Suppliers.FindAsync([input.Id], ct);
        if (supplier is null)
            return NotFound();
        if (input.Id == 0)
            db.Suppliers.Add(supplier);

        supplier.Name = input.Name.Trim();
        supplier.Code = input.Code;
        supplier.Kind = input.Kind;
        supplier.Currency = input.Currency.Trim().ToUpperInvariant();
        supplier.DefaultLeadTimeDays = input.DefaultLeadTimeDays;
        supplier.ExtraCostPercent = input.ExtraCostPercent;
        supplier.ExtraCostFixed = input.ExtraCostFixed;
        supplier.IsActive = input.IsActive;
        supplier.Notes = input.Notes;
        supplier.ImportColumnMap = input.ImportColumnMap;
        supplier.ScrapePriceSelector = input.ScrapePriceSelector;
        supplier.ScrapeStockSelector = input.ScrapeStockSelector;
        supplier.ScrapeInStockPattern = input.ScrapeInStockPattern;
        supplier.ScrapeDelaySeconds = input.ScrapeDelaySeconds;
        await db.SaveChangesAsync(ct);

        // Costs, currency or lead time may have changed for every product this supplier offers.
        var productIds = await db.SupplierOffers.Where(o => o.SupplierId == supplier.Id).Select(o => o.ProductId).ToListAsync(ct);
        var summary = await pricing.RepriceAsync(productIds, $"Supplier {supplier.Code} edited", ct);
        Flash($"Saved {supplier.Name}. {summary.Changed} product price(s) changed.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var supplier = await db.Suppliers.FindAsync([id], ct);
        if (supplier is null)
            return NotFound();
        var productIds = await db.SupplierOffers.Where(o => o.SupplierId == id).Select(o => o.ProductId).ToListAsync(ct);
        db.Suppliers.Remove(supplier);
        await db.SaveChangesAsync(ct);
        await pricing.RepriceAsync(productIds, $"Supplier {supplier.Code} deleted", ct);
        Flash($"Deleted {supplier.Name} and its offers.");
        return RedirectToAction(nameof(Index));
    }
}
