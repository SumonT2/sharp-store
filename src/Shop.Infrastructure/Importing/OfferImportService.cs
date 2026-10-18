using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Shop.Core.Entities;
using Shop.Core.Text;
using Shop.Infrastructure.Catalog;
using Shop.Infrastructure.Data;
using Shop.Infrastructure.Pricing;

namespace Shop.Infrastructure.Importing;

public sealed class OfferImportOptions
{
    public required int SupplierId { get; init; }
    /// <summary>Treat the file as the supplier's full stock list: offers missing from it get stock 0.</summary>
    public bool MarkMissingOutOfStock { get; init; }
    /// <summary>Create unpublished draft products for unknown SKUs when the row has a name.</summary>
    public bool CreateMissingProducts { get; init; }
}

/// <summary>
/// Updates one supplier's cost, stock, delivery time and offer window per product from a CSV file.
/// Rows match products by our SKU, or by the supplier's SKU of an existing offer.
/// Empty cells keep the current value; "-" clears it; stock "*" or "unlimited" means not tracked.
/// </summary>
public class OfferImportService(ShopDbContext db, CatalogWriter writer, PriceUpdateService pricing, ShopClock clock)
{
    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss",
    ];

    public static readonly IReadOnlyDictionary<string, string[]> Fields = new Dictionary<string, string[]>
    {
        ["sku"] = ["product_sku", "our_sku"],
        ["supplier_sku"] = ["vendor_sku", "item_code", "model"],
        ["name"] = ["title", "product_name", "description"],
        ["cost"] = ["price", "cost_price", "unit_price", "dealer_price"],
        ["currency"] = ["cur"],
        ["stock"] = ["qty", "quantity", "stock_qty", "available"],
        ["lead_time_days"] = ["lead_days", "eta_days", "delivery_days"],
        ["valid_from"] = ["from", "offer_start", "start"],
        ["valid_to"] = ["to", "valid_until", "offer_end", "end", "expires"],
        ["source_url"] = ["url", "link"],
        ["active"] = ["is_active", "enabled"],
    };

    public async Task<ImportResult> ImportAsync(Stream csv, string fileName, OfferImportOptions options, CancellationToken ct = default)
    {
        var result = new ImportResult();
        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == options.SupplierId, ct)
                       ?? throw new ArgumentException("Supplier not found.", nameof(options));

        CsvTable table;
        try
        {
            table = CsvTable.Read(csv, Fields, CsvTable.ParseColumnMap(supplier.ImportColumnMap));
        }
        catch (Exception ex) when (ex is InvalidDataException or CsvHelper.CsvHelperException)
        {
            result.Error(0, ex.Message);
            return await LogAsync(result, fileName, supplier.Id, ct);
        }

        if (!table.HasField("sku") && !table.HasField("supplier_sku"))
        {
            result.Error(0, "The file needs a \"sku\" or \"supplier_sku\" column (or a column mapping on the supplier).");
            return await LogAsync(result, fileName, supplier.Id, ct);
        }

        result.TotalRows = table.Rows.Count;

        // Offers are per supplier, so loading this supplier's offers once is cheap and avoids per-row queries.
        var offers = await db.SupplierOffers.Where(o => o.SupplierId == supplier.Id).ToListAsync(ct);
        var offerByProduct = offers.ToDictionary(o => o.ProductId);
        var offerBySupplierSku = offers.Where(o => o.SupplierSku != null)
            .GroupBy(o => o.SupplierSku!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var skus = table.Rows.Select(r => r.Get("sku")).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var products = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in skus.Chunk(1000))
        {
            foreach (var p in await db.Products.Where(p => chunk.Contains(p.Sku)).ToListAsync(ct))
                products[p.Sku] = p;
        }

        var touchedOffers = new HashSet<SupplierOffer>();
        // Offers for products created during this import (they have no id until saved).
        var pendingOffers = new Dictionary<Product, SupplierOffer>(ReferenceEqualityComparer.Instance);

        foreach (var row in table.Rows)
        {
            var sku = row.Get("sku");
            var supplierSku = row.Get("supplier_sku");

            SupplierOffer? offer = null;
            Product? product = null;
            var createdProduct = false;
            if (sku is not null && products.TryGetValue(sku, out product))
            {
                if (product.Id == 0)
                    pendingOffers.TryGetValue(product, out offer);
                else
                    offerByProduct.TryGetValue(product.Id, out offer);
            }
            else if (supplierSku is not null && offerBySupplierSku.TryGetValue(supplierSku, out offer))
                product = null; // matched through the existing offer

            if (offer is null && product is null)
            {
                var name = row.Get("name");
                if (!options.CreateMissingProducts || name is null)
                {
                    result.Error(row.LineNumber, $"No product found for SKU \"{sku ?? supplierSku}\".");
                    continue;
                }

                var newSku = sku ?? $"{supplier.Code}-{supplierSku}";
                if (newSku.Length > 100 || name.Length > 300)
                {
                    result.Error(row.LineNumber, "SKU or name is too long.");
                    continue;
                }
                product = new Product
                {
                    Sku = newSku,
                    Name = name,
                    Slug = await writer.UniqueProductSlugAsync(name, null, ct),
                    IsPublished = false,
                    CreatedAt = clock.UtcNow,
                    UpdatedAt = clock.UtcNow,
                };
                db.Products.Add(product);
                products[newSku] = product;
                createdProduct = true;
            }

            var isNewOffer = offer is null;
            if (offer is null)
            {
                offer = new SupplierOffer { SupplierId = supplier.Id, Product = product };
                if (product!.Id != 0)
                    offer.ProductId = product.Id;
            }

            var error = ApplyRow(offer, row, isNewOffer);
            if (error is not null)
            {
                result.Error(row.LineNumber, error);
                if (createdProduct)
                {
                    db.Products.Remove(product!);
                    products.Remove(product!.Sku);
                }
                continue;
            }
            if (createdProduct)
                result.Note(row.LineNumber, $"Created draft product {product!.Sku}; review and publish it in the admin.");

            if (supplierSku is not null)
                offer.SupplierSku = CatalogImportService.Truncate(supplierSku, 100);
            offer.UpdatedAt = clock.UtcNow;

            if (isNewOffer)
            {
                db.SupplierOffers.Add(offer);
                if (product!.Id != 0)
                    offerByProduct[product.Id] = offer;
                else
                    pendingOffers[product] = offer;
                if (offer.SupplierSku is not null)
                    offerBySupplierSku[offer.SupplierSku] = offer;
                result.Created++;
            }
            else if (touchedOffers.Contains(offer))
            {
                result.Note(row.LineNumber, "The same product appears more than once; the later row wins.");
            }
            else
            {
                result.Updated++;
            }
            touchedOffers.Add(offer);
        }

        if (options.MarkMissingOutOfStock)
        {
            var zeroed = 0;
            foreach (var offer in offers.Where(o => !touchedOffers.Contains(o) && o.StockQuantity != 0))
            {
                offer.StockQuantity = 0;
                offer.UpdatedAt = clock.UtcNow;
                touchedOffers.Add(offer);
                zeroed++;
            }
            if (zeroed > 0)
                result.Note(0, $"{zeroed} offer(s) not in the file were set to stock 0.");
        }

        await db.SaveChangesAsync(ct);

        var productIds = touchedOffers.Select(o => o.ProductId).Where(id => id != 0).ToList();
        var summary = await pricing.RepriceAsync(productIds, $"Offer import {supplier.Code} {fileName}", ct);
        result.Repriced = summary.Changed;
        foreach (var w in summary.Warnings)
            result.Note(0, w);

        return await LogAsync(result, fileName, supplier.Id, ct);
    }

    private string? ApplyRow(SupplierOffer offer, CsvRow row, bool isNew)
    {
        if (row.Get("cost") is { } costText)
        {
            if (TextUtil.ParseDecimal(costText) is not { } cost || cost < 0)
                return $"Invalid cost \"{costText}\".";
            offer.Cost = cost;
        }
        else if (isNew && row.Get("source_url") is null)
        {
            // Without a source URL nobody will fill the cost in later, so it's needed now.
            return "A cost (or a source_url to read the price from) is required for a new offer.";
        }

        if (row.Get("currency") is { } currency)
        {
            if (currency == "-")
                offer.Currency = null;
            else if (currency.Length != 3 || !currency.All(char.IsLetter))
                return $"Currency must be a 3-letter code like USD or CNY (got \"{currency}\").";
            else
                offer.Currency = currency.ToUpperInvariant();
        }

        if (row.Get("stock") is { } stockText)
        {
            var normalized = stockText.Trim().ToLowerInvariant();
            if (normalized is "*" or "unlimited" or "-")
                offer.StockQuantity = null;
            else if (normalized is "in stock" or "yes" or "available")
                offer.StockQuantity = offer.StockQuantity is > 0 ? offer.StockQuantity : null;
            else if (normalized is "out of stock" or "no" or "sold out")
                offer.StockQuantity = 0;
            else if (TextUtil.ParseInt(stockText) is { } stock && stock >= 0)
                offer.StockQuantity = stock;
            else
                return $"Invalid stock \"{stockText}\".";
        }

        if (row.Get("lead_time_days") is { } leadText)
        {
            if (leadText == "-")
                offer.LeadTimeDays = null;
            else if (TextUtil.ParseInt(leadText) is { } lead && lead is >= 0 and <= 365)
                offer.LeadTimeDays = lead;
            else
                return $"Invalid lead_time_days \"{leadText}\".";
        }

        if (row.Get("valid_from") is { } fromText)
        {
            if (!TryParseDate(fromText, endOfDay: false, out var from))
                return $"Invalid valid_from \"{fromText}\". Use yyyy-MM-dd or yyyy-MM-dd HH:mm.";
            offer.ValidFrom = from;
        }

        if (row.Get("valid_to") is { } toText)
        {
            if (!TryParseDate(toText, endOfDay: true, out var to))
                return $"Invalid valid_to \"{toText}\". Use yyyy-MM-dd or yyyy-MM-dd HH:mm.";
            offer.ValidTo = to;
        }

        if (offer.ValidFrom is { } f && offer.ValidTo is { } t && t <= f)
            return "valid_to must be after valid_from.";

        if (row.Get("source_url") is { } url)
        {
            if (url == "-")
                offer.SourceUrl = null;
            else if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                return $"Invalid source_url \"{url}\".";
            else
                offer.SourceUrl = CatalogImportService.Truncate(url, 1000);
        }

        if (row.Get("active") is { } activeText)
        {
            if (TextUtil.ParseBool(activeText) is not { } active)
                return $"\"active\" must be yes/no (got \"{activeText}\").";
            offer.IsActive = active;
        }

        return null;
    }

    /// <summary>
    /// Dates in the file are in the shop's time zone. A date without a time means the start of that day,
    /// or for an end date, the end of that day.
    /// </summary>
    private bool TryParseDate(string text, bool endOfDay, out DateTime? utc)
    {
        utc = null;
        if (text == "-")
            return true;
        if (!DateTime.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            return false;
        if (endOfDay && text.Length == 10)
            local = local.AddDays(1);
        utc = clock.ToUtc(local);
        return true;
    }

    private async Task<ImportResult> LogAsync(ImportResult result, string fileName, int supplierId, CancellationToken ct)
    {
        var log = new ImportLog
        {
            Kind = ImportKind.Offers,
            FileName = CatalogImportService.Truncate(fileName, 300),
            SupplierId = supplierId,
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
}
