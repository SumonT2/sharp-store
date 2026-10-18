using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shop.Infrastructure.Data;
using Shop.Infrastructure.Importing;
using Shop.Web.Infrastructure;

namespace Shop.Web.Areas.Admin.Controllers;

public class ImportController(ShopDbContext db, CatalogImportService catalogImport, OfferImportService offerImport, CategoryMenu menu)
    : AdminController
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewBag.Suppliers = await db.Suppliers.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct);
        var logs = await db.ImportLogs.AsNoTracking().Include(l => l.Supplier).OrderByDescending(l => l.StartedAt).Take(20).ToListAsync(ct);
        return View(logs);
    }

    [HttpPost]
    public async Task<IActionResult> Catalog(IFormFile? file, CancellationToken ct)
    {
        if (!IsCsv(file))
        {
            Flash("Choose a .csv file to upload.", error: true);
            return RedirectToAction(nameof(Index));
        }

        await using var stream = file!.OpenReadStream();
        var result = await catalogImport.ImportAsync(stream, file.FileName, ct);
        menu.Invalidate();
        Flash($"Catalog import: {result.Created} created, {result.Updated} updated, {result.Failed} failed.", error: result.Failed > 0 && result.Created + result.Updated == 0);
        return RedirectToAction(nameof(Log), new { id = result.ImportLogId });
    }

    [HttpPost]
    public async Task<IActionResult> Offers(IFormFile? file, int supplierId, bool markMissingOutOfStock, bool createMissingProducts, CancellationToken ct)
    {
        if (!IsCsv(file))
        {
            Flash("Choose a .csv file to upload.", error: true);
            return RedirectToAction(nameof(Index));
        }
        if (!await db.Suppliers.AnyAsync(s => s.Id == supplierId, ct))
        {
            Flash("Choose a supplier.", error: true);
            return RedirectToAction(nameof(Index));
        }

        await using var stream = file!.OpenReadStream();
        var result = await offerImport.ImportAsync(stream, file.FileName, new OfferImportOptions
        {
            SupplierId = supplierId,
            MarkMissingOutOfStock = markMissingOutOfStock,
            CreateMissingProducts = createMissingProducts,
        }, ct);
        Flash($"Stock & price import: {result.Created} new offers, {result.Updated} updated, {result.Failed} failed, {result.Repriced} prices changed.",
            error: result.Failed > 0 && result.Created + result.Updated == 0);
        return RedirectToAction(nameof(Log), new { id = result.ImportLogId });
    }

    public async Task<IActionResult> Log(int id, CancellationToken ct)
    {
        var log = await db.ImportLogs.AsNoTracking().Include(l => l.Supplier).FirstOrDefaultAsync(l => l.Id == id, ct);
        return log is null ? NotFound() : View(log);
    }

    /// <summary>Downloadable CSV templates with an example row.</summary>
    public IActionResult Template(string kind)
    {
        var (name, content) = kind switch
        {
            "offers" => ("stock-price-template.csv",
                "sku,supplier_sku,cost,currency,stock,lead_time_days,valid_from,valid_to,source_url\n" +
                "PH-NX-A7,CN-A7-256,1390,CNY,*,15,2026-11-01,2026-11-15,\n" +
                "AC-CH-65,IMP-65W,18.00,USD,120,,,,\n"),
            _ => ("catalog-template.csv",
                "sku,name,category,brand,short_description,description,image_url,published,compare_at_price," +
                "spec:Display > Screen Size [inch],spec:Performance > RAM [GB],spec:Storage > Storage [GB],spec:General > Color\n" +
                "PH-EX-01,Example Phone 128 GB,Phones > Smartphones,ExampleBrand,\"A short, honest summary.\",\"Longer description written by you.\",,yes,,6.5,8,128,Black\n"),
        };
        // UTF-8 BOM so Excel opens non-English text correctly.
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(content)).ToArray();
        return File(bytes, "text/csv", name);
    }

    private static bool IsCsv(IFormFile? file) =>
        file is { Length: > 0 } && (file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) || file.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
}
