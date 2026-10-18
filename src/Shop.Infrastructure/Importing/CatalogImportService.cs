using Microsoft.EntityFrameworkCore;
using Shop.Core.Entities;
using Shop.Core.Text;
using Shop.Infrastructure.Catalog;
using Shop.Infrastructure.Data;
using Shop.Infrastructure.Pricing;

namespace Shop.Infrastructure.Importing;

/// <summary>
/// Creates or updates products from a CSV file, matched by SKU.
/// Empty cells leave the existing value unchanged; a single "-" clears it.
/// Columns named "spec:Group > Name [unit]" set specifications (group and unit are optional).
/// </summary>
public class CatalogImportService(ShopDbContext db, CatalogWriter writer, PriceUpdateService pricing, ShopClock clock)
{
    public const string SpecPrefix = "spec:";
    private const int ChunkSize = 500;

    public static readonly IReadOnlyDictionary<string, string[]> Fields = new Dictionary<string, string[]>
    {
        ["sku"] = ["product_sku", "item_sku"],
        ["name"] = ["title", "product_name"],
        ["slug"] = [],
        ["category"] = ["category_path", "categories"],
        ["brand"] = ["manufacturer"],
        ["short_description"] = ["summary"],
        ["description"] = ["long_description"],
        ["image_url"] = ["image", "picture"],
        ["published"] = ["is_published", "visible"],
        ["featured"] = ["is_featured"],
        ["price_override"] = ["fixed_price"],
        ["compare_at_price"] = ["old_price", "was_price", "regular_price"],
    };

    public async Task<ImportResult> ImportAsync(Stream csv, string fileName, CancellationToken ct = default)
    {
        var result = new ImportResult();
        CsvTable table;
        try
        {
            table = CsvTable.Read(csv, Fields);
        }
        catch (Exception ex) when (ex is InvalidDataException or CsvHelper.CsvHelperException)
        {
            result.Error(0, ex.Message);
            return await LogAsync(result, fileName, ct);
        }

        if (!table.HasField("sku"))
        {
            result.Error(0, "Missing required column \"sku\".");
            return await LogAsync(result, fileName, ct);
        }

        var specHeaders = table.Headers.Where(h => h.StartsWith(SpecPrefix, StringComparison.OrdinalIgnoreCase)).ToList();
        var attributes = new Dictionary<string, SpecificationAttribute>();
        foreach (var header in specHeaders)
            attributes[header] = await writer.GetOrCreateAttributeAsync(header[SpecPrefix.Length..], ct);

        result.TotalRows = table.Rows.Count;
        var touched = new HashSet<Product>();
        var seenSkus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var chunk in table.Rows.Chunk(ChunkSize))
        {
            var skus = chunk.Select(r => r.Get("sku")).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var existing = await db.Products
                .Include(p => p.Specifications)
                .Where(p => skus.Contains(p.Sku))
                .ToDictionaryAsync(p => p.Sku, StringComparer.OrdinalIgnoreCase, ct);

            foreach (var row in chunk)
            {
                var sku = row.Get("sku");
                if (sku is null)
                {
                    result.Error(row.LineNumber, "SKU is empty.");
                    continue;
                }
                if (sku.Length > 100)
                {
                    result.Error(row.LineNumber, "SKU is longer than 100 characters.");
                    continue;
                }
                if (!seenSkus.Add(sku))
                    result.Note(row.LineNumber, $"SKU {sku} appears more than once; the later row wins.");

                var isNew = !existing.TryGetValue(sku, out var product);
                if (product is null)
                {
                    var name = row.Get("name");
                    if (name is null)
                    {
                        result.Error(row.LineNumber, $"New product {sku} needs a name.");
                        continue;
                    }
                    product = new Product { Sku = sku, CreatedAt = clock.UtcNow, IsPublished = true };
                    db.Products.Add(product);
                    existing[sku] = product;
                }

                var error = await ApplyRowAsync(product, row, attributes, ct);
                if (error is not null)
                {
                    result.Error(row.LineNumber, error);
                    if (isNew)
                    {
                        db.Products.Remove(product);
                        existing.Remove(sku);
                    }
                    continue;
                }

                product.UpdatedAt = clock.UtcNow;
                touched.Add(product);
                if (isNew)
                    result.Created++;
                else
                    result.Updated++;
            }

            await db.SaveChangesAsync(ct);
        }

        // Price overrides may have changed, so refresh prices for the affected products.
        var summary = await pricing.RepriceAsync(touched.Select(p => p.Id).ToList(), $"Catalog import {fileName}", ct);
        result.Repriced = summary.Changed;
        return await LogAsync(result, fileName, ct);
    }

    private async Task<string?> ApplyRowAsync(Product product, CsvRow row, Dictionary<string, SpecificationAttribute> attributes, CancellationToken ct)
    {
        if (row.Get("name") is { } name)
        {
            if (name.Length > 300)
                return "Name is longer than 300 characters.";
            product.Name = name;
        }

        if (row.Get("slug") is { } slug)
            product.Slug = await writer.UniqueProductSlugAsync(slug, product.Id == 0 ? null : product.Id, ct);
        else if (string.IsNullOrEmpty(product.Slug))
            product.Slug = await writer.UniqueProductSlugAsync(product.Name, null, ct);

        if (row.Get("category") is { } category)
        {
            if (category == "-")
            {
                product.Category = null;
                product.CategoryId = null;
            }
            else
            {
                product.Category = await writer.GetOrCreateCategoryPathAsync(category, ct);
            }
        }

        if (row.Get("brand") is { } brand)
        {
            if (brand == "-")
            {
                product.Brand = null;
                product.BrandId = null;
            }
            else
            {
                product.Brand = await writer.GetOrCreateBrandAsync(brand, ct);
            }
        }

        product.ShortDescription = TextOrKeep(row.Get("short_description"), product.ShortDescription, 1000);
        product.Description = TextOrKeep(row.Get("description"), product.Description, 20_000);
        product.ImageUrl = TextOrKeep(row.Get("image_url"), product.ImageUrl, 1000);

        if (row.Get("published") is { } published)
        {
            if (TextUtil.ParseBool(published) is not { } flag)
                return $"\"published\" must be yes/no, true/false or 1/0 (got \"{published}\").";
            product.IsPublished = flag;
        }

        if (row.Get("featured") is { } featured)
        {
            if (TextUtil.ParseBool(featured) is not { } flag)
                return $"\"featured\" must be yes/no, true/false or 1/0 (got \"{featured}\").";
            product.IsFeatured = flag;
        }

        if (!TryMoney(row.Get("price_override"), product.PriceOverride, out var overridePrice, out var err))
            return $"price_override: {err}";
        product.PriceOverride = overridePrice;

        if (!TryMoney(row.Get("compare_at_price"), product.CompareAtPrice, out var compareAt, out err))
            return $"compare_at_price: {err}";
        product.CompareAtPrice = compareAt;

        foreach (var (header, attribute) in attributes)
        {
            var value = row.GetByHeader(header);
            if (value is null)
                continue;
            CatalogWriter.SetSpecification(product, attribute, value == "-" ? null : value);
        }

        return null;
    }

    private static string? TextOrKeep(string? value, string? current, int maxLength) =>
        value switch
        {
            null => current,
            "-" => null,
            _ => value.Length > maxLength ? value[..maxLength] : value,
        };

    private static bool TryMoney(string? value, decimal? current, out decimal? result, out string? error)
    {
        error = null;
        result = current;
        if (value is null)
            return true;
        if (value == "-")
        {
            result = null;
            return true;
        }
        if (TextUtil.ParseDecimal(value) is not { } amount || amount < 0)
        {
            error = $"\"{value}\" is not a valid amount.";
            return false;
        }
        result = amount;
        return true;
    }

    private async Task<ImportResult> LogAsync(ImportResult result, string fileName, CancellationToken ct)
    {
        var log = new ImportLog
        {
            Kind = ImportKind.Catalog,
            FileName = Truncate(fileName, 300),
            StartedAt = clock.UtcNow,
            TotalRows = result.TotalRows,
            Created = result.Created,
            Updated = result.Updated,
            Failed = result.Failed,
            Messages = string.Join('\n', result.Messages),
        };
        db.ImportLogs.Add(log);
        await db.SaveChangesAsync(ct);
        result.ImportLogId = log.Id;
        return result;
    }

    internal static string Truncate(string s, int max) => s.Length > max ? s[..max] : s;
}
