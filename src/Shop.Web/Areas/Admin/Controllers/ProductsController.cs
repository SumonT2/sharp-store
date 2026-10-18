using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shop.Core.Entities;
using Shop.Infrastructure;
using Shop.Infrastructure.Catalog;
using Shop.Infrastructure.Data;
using Shop.Infrastructure.Pricing;
using Shop.Web.Areas.Admin.Models;
using Shop.Web.Infrastructure;

namespace Shop.Web.Areas.Admin.Controllers;

public class ProductsController(ShopDbContext db, CatalogWriter writer, PriceUpdateService pricing, CategoryMenu menu, ShopClock clock)
    : AdminController
{
    private const int PageSize = 50;

    public async Task<IActionResult> Index(string? q, string? status, int page = 1, CancellationToken ct = default)
    {
        var query = db.Products.AsNoTracking().Include(p => p.Brand).Include(p => p.Category).AsQueryable();
        foreach (var term in CatalogQueryService.SearchTerms(q))
            query = query.Where(p => p.Name.ToLower().Contains(term) || p.Sku.ToLower().Contains(term));

        query = status switch
        {
            "draft" => query.Where(p => !p.IsPublished),
            "in-stock" => query.Where(p => p.IsPublished && p.Availability == Availability.InStock),
            "pre-order" => query.Where(p => p.IsPublished && p.Availability == Availability.PreOrder),
            "out-of-stock" => query.Where(p => p.IsPublished && p.Availability == Availability.OutOfStock),
            _ => query,
        };

        var total = await query.CountAsync(ct);
        page = Math.Max(1, page);
        var items = await query.OrderByDescending(p => p.UpdatedAt).ThenBy(p => p.Id)
            .Skip((page - 1) * PageSize).Take(PageSize).ToListAsync(ct);

        ViewBag.Total = total;
        ViewBag.Page = page;
        ViewBag.PageCount = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        ViewBag.Q = q;
        ViewBag.Status = status;
        return View(items);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int? id, CancellationToken ct)
    {
        if (id is null or 0)
            return View(await BuildViewModelAsync(new ProductEditInput(), null, ct));

        var product = await LoadAsync(id.Value, ct);
        if (product is null)
            return NotFound();

        var input = new ProductEditInput
        {
            Id = product.Id,
            Sku = product.Sku,
            Name = product.Name,
            Slug = product.Slug,
            CategoryPath = CategoryPath(product.Category),
            Brand = product.Brand?.Name,
            ShortDescription = product.ShortDescription,
            Description = product.Description,
            ImageUrl = product.ImageUrl,
            IsPublished = product.IsPublished,
            PriceOverride = product.PriceOverride,
            CompareAtPrice = product.CompareAtPrice,
            SpecsText = string.Join('\n', product.Specifications
                .OrderBy(s => s.Attribute!.DisplayOrder)
                .Select(s => $"{FormatHeader(s.Attribute!)}: {s.Value}")),
        };
        return View(await BuildViewModelAsync(input, product, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(ProductEditInput input, CancellationToken ct)
    {
        input.Sku = input.Sku?.Trim() ?? "";
        var product = input.Id == 0 ? null : await LoadAsync(input.Id, ct, tracking: true);
        if (input.Id != 0 && product is null)
            return NotFound();

        if (await db.Products.AnyAsync(p => p.Sku == input.Sku && p.Id != input.Id, ct))
            ModelState.AddModelError(nameof(input.Sku), "Another product already uses this SKU.");

        var specLines = ParseSpecLines(input.SpecsText, out var specError);
        if (specError is not null)
            ModelState.AddModelError(nameof(input.SpecsText), specError);

        if (!ModelState.IsValid)
            return View(await BuildViewModelAsync(input, product, ct));

        if (product is null)
        {
            product = new Product { CreatedAt = clock.UtcNow };
            db.Products.Add(product);
        }

        product.Sku = input.Sku;
        product.Name = input.Name.Trim();
        var slugSource = string.IsNullOrWhiteSpace(input.Slug) ? product.Name : input.Slug;
        if (string.IsNullOrWhiteSpace(product.Slug) || !string.IsNullOrWhiteSpace(input.Slug) && input.Slug != product.Slug)
            product.Slug = await writer.UniqueProductSlugAsync(slugSource, product.Id == 0 ? null : product.Id, ct);
        // Set navigations only: a newly created category or brand has no id until saved.
        product.Category = await writer.GetOrCreateCategoryPathAsync(input.CategoryPath, ct);
        product.Brand = await writer.GetOrCreateBrandAsync(input.Brand, ct);
        if (product.Category is null)
            product.CategoryId = null;
        if (product.Brand is null)
            product.BrandId = null;
        product.ShortDescription = Clean(input.ShortDescription);
        product.Description = Clean(input.Description);
        product.ImageUrl = Clean(input.ImageUrl);
        product.IsPublished = input.IsPublished;
        product.PriceOverride = input.PriceOverride;
        product.CompareAtPrice = input.CompareAtPrice;
        product.UpdatedAt = clock.UtcNow;

        // Specifications: the textarea is the full list, so attributes not listed are removed.
        var keep = new HashSet<SpecificationAttribute>();
        foreach (var (header, value) in specLines)
        {
            var attribute = await writer.GetOrCreateAttributeAsync(header, ct);
            CatalogWriter.SetSpecification(product, attribute, value);
            keep.Add(attribute);
        }
        product.Specifications.RemoveAll(s => s.Attribute is not null && !keep.Contains(s.Attribute));

        await db.SaveChangesAsync(ct);
        await pricing.RepriceAsync([product.Id], "Edited in admin", ct);
        menu.Invalidate();
        Flash($"Saved {product.Name}.");
        return RedirectToAction(nameof(Edit), new { id = product.Id });
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var product = await db.Products.FindAsync([id], ct);
        if (product is null)
            return NotFound();
        db.Products.Remove(product);
        await db.SaveChangesAsync(ct);
        Flash($"Deleted {product.Name}.");
        return RedirectToAction(nameof(Index));
    }

    private Task<Product?> LoadAsync(int id, CancellationToken ct, bool tracking = false)
    {
        var q = db.Products
            .Include(p => p.Brand)
            .Include(p => p.Category)
            .Include(p => p.Specifications).ThenInclude(s => s.Attribute)
            .AsSplitQuery();
        return (tracking ? q : q.AsNoTracking()).FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    private async Task<ProductEditViewModel> BuildViewModelAsync(ProductEditInput input, Product? product, CancellationToken ct)
    {
        var categories = await db.Categories.AsNoTracking().ToListAsync(ct);
        var byId = categories.ToDictionary(c => c.Id);
        string PathOf(Category c) => c.ParentId is { } pid && byId.TryGetValue(pid, out var parent) ? $"{PathOf(parent)} > {c.Name}" : c.Name;

        return new ProductEditViewModel
        {
            Input = input,
            Product = product,
            Offers = product is null ? [] : await db.SupplierOffers.AsNoTracking().Include(o => o.Supplier)
                .Where(o => o.ProductId == product.Id).OrderBy(o => o.Supplier!.Name).ToListAsync(ct),
            History = product is null ? [] : await db.PriceHistory.AsNoTracking()
                .Where(h => h.ProductId == product.Id).OrderByDescending(h => h.ChangedAt).Take(10).ToListAsync(ct),
            CategoryPaths = categories.Select(PathOf).Order().ToList(),
            Brands = await db.Brands.AsNoTracking().OrderBy(b => b.Name).Select(b => b.Name).ToListAsync(ct),
        };
    }

    private string? CategoryPath(Category? category)
    {
        if (category is null)
            return null;
        var names = new List<string>();
        var all = db.Categories.AsNoTracking().ToDictionary(c => c.Id);
        for (var c = category; c is not null && names.Count < 6; c = c.ParentId is { } p ? all.GetValueOrDefault(p) : null)
            names.Insert(0, c.Name);
        return string.Join(" > ", names);
    }

    private static string FormatHeader(SpecificationAttribute a) =>
        (a.GroupName is { Length: > 0 } g ? $"{g} > " : "") + a.Name + (a.Unit is { Length: > 0 } u ? $" [{u}]" : "");

    private static List<(string Header, string Value)> ParseSpecLines(string? text, out string? error)
    {
        error = null;
        var result = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(text))
            return result;

        var lineNo = 0;
        foreach (var raw in text.Split('\n'))
        {
            lineNo++;
            var line = raw.Trim();
            if (line.Length == 0)
                continue;
            var colon = line.IndexOf(':');
            if (colon <= 0 || colon == line.Length - 1)
            {
                error = $"Line {lineNo}: use the format \"Name: value\" (got \"{line}\").";
                return result;
            }
            result.Add((line[..colon].Trim(), line[(colon + 1)..].Trim()));
        }
        return result;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
