using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shop.Core.Entities;
using Shop.Infrastructure;
using Shop.Infrastructure.Data;
using Shop.Infrastructure.Pricing;
using Shop.Web.Areas.Admin.Models;

namespace Shop.Web.Areas.Admin.Controllers;

public class PricingController(ShopDbContext db, PriceUpdateService pricing, ShopClock clock, IOptions<ShopOptions> options) : AdminController
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewBag.Rates = await db.CurrencyRates.AsNoTracking().OrderBy(r => r.Code).ToListAsync(ct);
        ViewBag.BaseCurrency = options.Value.BaseCurrency;
        var rules = await db.PricingRules.AsNoTracking()
            .Include(r => r.Supplier).Include(r => r.Category).Include(r => r.Brand)
            .OrderByDescending(r => r.Priority).ThenBy(r => r.Name)
            .ToListAsync(ct);
        return View(rules);
    }

    [HttpGet]
    public async Task<IActionResult> EditRule(int? id, CancellationToken ct)
    {
        await LoadListsAsync(ct);
        if (id is null or 0)
            return View(new RuleEditInput { MarkupPercent = 20 });
        var r = await db.PricingRules.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null)
            return NotFound();
        return View(new RuleEditInput
        {
            Id = r.Id, Name = r.Name, Priority = r.Priority, SupplierId = r.SupplierId, CategoryId = r.CategoryId, BrandId = r.BrandId,
            MarkupPercent = r.MarkupPercent, FixedMarkup = r.FixedMarkup, MinMargin = r.MinMargin, Rounding = r.Rounding, IsActive = r.IsActive,
        });
    }

    [HttpPost]
    public async Task<IActionResult> EditRule(RuleEditInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await LoadListsAsync(ct);
            return View(input);
        }

        var rule = input.Id == 0 ? new PricingRule() : await db.PricingRules.FindAsync([input.Id], ct);
        if (rule is null)
            return NotFound();
        if (input.Id == 0)
            db.PricingRules.Add(rule);

        rule.Name = input.Name.Trim();
        rule.Priority = input.Priority;
        rule.SupplierId = input.SupplierId;
        rule.CategoryId = input.CategoryId;
        rule.BrandId = input.BrandId;
        rule.MarkupPercent = input.MarkupPercent;
        rule.FixedMarkup = input.FixedMarkup;
        rule.MinMargin = input.MinMargin;
        rule.Rounding = input.Rounding;
        rule.IsActive = input.IsActive;
        await db.SaveChangesAsync(ct);

        var summary = await pricing.RepriceAllAsync($"Pricing rule \"{rule.Name}\" saved", ct);
        Flash($"Rule saved. {summary.Changed} product price(s) changed.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> DeleteRule(int id, CancellationToken ct)
    {
        var rule = await db.PricingRules.FindAsync([id], ct);
        if (rule is null)
            return NotFound();
        db.PricingRules.Remove(rule);
        await db.SaveChangesAsync(ct);
        var summary = await pricing.RepriceAllAsync($"Pricing rule \"{rule.Name}\" deleted", ct);
        Flash($"Rule deleted. {summary.Changed} product price(s) changed.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> SaveRate(string code, decimal rateToBase, CancellationToken ct)
    {
        code = (code ?? "").Trim().ToUpperInvariant();
        if (code.Length != 3 || !code.All(char.IsLetter) || rateToBase <= 0)
        {
            Flash("Enter a 3-letter currency code and a rate above 0.", error: true);
            return RedirectToAction(nameof(Index));
        }
        if (code == options.Value.BaseCurrency.ToUpperInvariant())
        {
            Flash($"{code} is the base currency; its rate is always 1.", error: true);
            return RedirectToAction(nameof(Index));
        }

        var rate = await db.CurrencyRates.FindAsync([code], ct);
        if (rate is null)
            db.CurrencyRates.Add(rate = new CurrencyRate { Code = code });
        rate.RateToBase = rateToBase;
        rate.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        var summary = await pricing.RepriceAllAsync($"Exchange rate {code} = {rateToBase}", ct);
        Flash($"Saved 1 {code} = {rateToBase} {options.Value.BaseCurrency}. {summary.Changed} product price(s) changed.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> DeleteRate(string code, CancellationToken ct)
    {
        var rate = await db.CurrencyRates.FindAsync([code], ct);
        if (rate is not null)
        {
            db.CurrencyRates.Remove(rate);
            await db.SaveChangesAsync(ct);
            await pricing.RepriceAllAsync($"Exchange rate {code} removed", ct);
        }
        return RedirectToAction(nameof(Index));
    }

    private async Task LoadListsAsync(CancellationToken ct)
    {
        ViewBag.Suppliers = await db.Suppliers.AsNoTracking().OrderBy(s => s.Name).Select(s => new Option(s.Id.ToString(), s.Name)).ToListAsync(ct);
        ViewBag.Brands = await db.Brands.AsNoTracking().OrderBy(b => b.Name).Select(b => new Option(b.Id.ToString(), b.Name)).ToListAsync(ct);
        var categories = await db.Categories.AsNoTracking().ToListAsync(ct);
        var byId = categories.ToDictionary(c => c.Id);
        string PathOf(Category c) => c.ParentId is { } p && byId.TryGetValue(p, out var parent) ? $"{PathOf(parent)} > {c.Name}" : c.Name;
        ViewBag.Categories = categories.Select(c => new Option(c.Id.ToString(), PathOf(c))).OrderBy(o => o.Label).ToList();
    }
}
