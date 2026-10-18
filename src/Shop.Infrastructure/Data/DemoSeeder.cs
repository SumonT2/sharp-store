using Microsoft.EntityFrameworkCore;
using Shop.Core.Entities;
using Shop.Infrastructure.Catalog;
using Shop.Infrastructure.Pricing;

namespace Shop.Infrastructure.Data;

/// <summary>
/// Fills an empty database with sample data so the store can be tried out. Brands and products are fictional.
/// </summary>
public class DemoSeeder(ShopDbContext db, CatalogWriter writer, PriceUpdateService pricing, ShopClock clock)
{
    private sealed record DemoProduct(
        string Sku, string Name, string Category, string Brand, string Summary,
        decimal ImporterCost, int? ImporterStock, decimal? ChinaCostCny, decimal? WarehouseCost, int? WarehouseStock,
        (string Header, string Value)[] Specs);

    private static readonly (string Header, bool OnCard)[] AttributeHeaders =
    [
        ("Display > Screen Size [inch]", true),
        ("Display > Resolution", false),
        ("Display > Refresh Rate [Hz]", false),
        ("Performance > Processor", false),
        ("Performance > RAM [GB]", true),
        ("Storage > Storage [GB]", true),
        ("Battery > Battery [mAh]", false),
        ("Battery > Charging Power [W]", true),
        ("Camera > Main Camera [MP]", false),
        ("General > Color", false),
        ("General > Weight [g]", false),
        ("Connectivity > Connector", false),
        ("Audio > Noise Cancelling", true),
        ("Audio > Playtime [h]", false),
    ];

    private static readonly DemoProduct[] Products =
    [
        new("PH-NX-A5", "Nexora A5 Smartphone", "Phones > Smartphones", "Nexora", "6.5\" 90 Hz display, 5000 mAh battery and a 50 MP camera.",
            165m, 40, 980m, null, null,
            [("Screen Size", "6.5"), ("Resolution", "1600 x 720"), ("Refresh Rate", "90"), ("Processor", "Octa-core 2.2 GHz"), ("RAM", "4"), ("Storage", "128"), ("Battery", "5000"), ("Charging Power", "18"), ("Main Camera", "50"), ("Color", "Midnight Blue"), ("Weight", "192")]),
        new("PH-NX-A7", "Nexora A7 Smartphone", "Phones > Smartphones", "Nexora", "AMOLED 120 Hz, 8 GB RAM and 45 W fast charging.",
            235m, 25, 1390m, null, null,
            [("Screen Size", "6.7"), ("Resolution", "2400 x 1080"), ("Refresh Rate", "120"), ("Processor", "Octa-core 2.6 GHz"), ("RAM", "8"), ("Storage", "256"), ("Battery", "5000"), ("Charging Power", "45"), ("Main Camera", "64"), ("Color", "Graphite"), ("Weight", "188")]),
        new("PH-NX-X1", "Nexora X1 Pro", "Phones > Smartphones", "Nexora", "Flagship with 12 GB RAM, 512 GB storage and 100 W charging.",
            520m, 6, 3100m, null, null,
            [("Screen Size", "6.8"), ("Resolution", "3200 x 1440"), ("Refresh Rate", "120"), ("Processor", "Octa-core 3.2 GHz"), ("RAM", "12"), ("Storage", "512"), ("Battery", "5100"), ("Charging Power", "100"), ("Main Camera", "200"), ("Color", "Titanium"), ("Weight", "215")]),
        new("PH-OR-M3", "Orbitel M3", "Phones > Smartphones", "Orbitel", "Compact 6.1\" phone with a clean, fast interface.",
            190m, 0, 1150m, null, null,
            [("Screen Size", "6.1"), ("Resolution", "2340 x 1080"), ("Refresh Rate", "90"), ("Processor", "Octa-core 2.4 GHz"), ("RAM", "6"), ("Storage", "128"), ("Battery", "4300"), ("Charging Power", "25"), ("Main Camera", "48"), ("Color", "Sand"), ("Weight", "165")]),
        new("PH-OR-M5", "Orbitel M5 Max", "Phones > Smartphones", "Orbitel", "Big screen, big battery: 6.9\" and 6000 mAh.",
            280m, 12, 1650m, null, null,
            [("Screen Size", "6.9"), ("Resolution", "2400 x 1080"), ("Refresh Rate", "120"), ("Processor", "Octa-core 2.8 GHz"), ("RAM", "8"), ("Storage", "256"), ("Battery", "6000"), ("Charging Power", "67"), ("Main Camera", "108"), ("Color", "Forest Green"), ("Weight", "210")]),
        new("LT-VL-14", "Valtrix Book 14", "Laptops", "Valtrix", "Light 14\" laptop for study and office work.",
            480m, 8, null, null, null,
            [("Screen Size", "14"), ("Resolution", "1920 x 1080"), ("Processor", "4-core 3.6 GHz"), ("RAM", "8"), ("Storage", "512"), ("Color", "Silver"), ("Weight", "1350")]),
        new("LT-VL-16", "Valtrix Book 16 Pro", "Laptops", "Valtrix", "16\" 2.5K screen, 16 GB RAM and 1 TB SSD.",
            890m, 4, 5600m, null, null,
            [("Screen Size", "16"), ("Resolution", "2560 x 1600"), ("Refresh Rate", "120"), ("Processor", "8-core 4.5 GHz"), ("RAM", "16"), ("Storage", "1024"), ("Color", "Space Grey"), ("Weight", "1900")]),
        new("LT-OR-13", "Orbitel Air 13", "Laptops", "Orbitel", "Fanless 13\" ultrabook with all-day battery.",
            640m, 0, 4100m, null, null,
            [("Screen Size", "13.3"), ("Resolution", "2560 x 1600"), ("Processor", "8-core 3.8 GHz"), ("RAM", "16"), ("Storage", "512"), ("Color", "Starlight"), ("Weight", "1240")]),
        new("AC-CH-65", "Voltaro 65 W GaN Charger", "Accessories > Chargers", "Voltaro", "Charges a laptop and phone together from one small plug.",
            18m, 150, 95m, 15m, 30,
            [("Charging Power", "65"), ("Connector", "USB-C x2, USB-A x1"), ("Color", "White"), ("Weight", "110")]),
        new("AC-CH-25", "Voltaro 25 W Fast Charger", "Accessories > Chargers", "Voltaro", "Everyday fast charger for phones.",
            6.5m, 300, 32m, null, null,
            [("Charging Power", "25"), ("Connector", "USB-C x1"), ("Color", "Black"), ("Weight", "55")]),
        new("AC-CH-100", "Voltaro 100 W Desk Charger", "Accessories > Chargers", "Voltaro", "Four ports, 100 W total, for your whole desk.",
            32m, 0, 170m, null, null,
            [("Charging Power", "100"), ("Connector", "USB-C x3, USB-A x1"), ("Color", "Grey"), ("Weight", "260")]),
        new("AC-EB-S1", "Sonique Buds S1", "Accessories > Earbuds", "Sonique", "Wireless earbuds with 30 hours of total playtime.",
            14m, 80, 70m, null, null,
            [("Noise Cancelling", "No"), ("Playtime", "30"), ("Color", "White"), ("Weight", "45")]),
        new("AC-EB-P2", "Sonique Buds Pro 2", "Accessories > Earbuds", "Sonique", "Active noise cancelling and wireless charging case.",
            38m, 20, 210m, null, null,
            [("Noise Cancelling", "Yes"), ("Playtime", "36"), ("Color", "Black"), ("Weight", "52")]),
        new("AC-PB-20", "Voltaro PowerCell 20000", "Accessories > Power Banks", "Voltaro", "20,000 mAh power bank with 22.5 W output.",
            16m, 60, 85m, null, null,
            [("Battery", "20000"), ("Charging Power", "22.5"), ("Connector", "USB-C x1, USB-A x2"), ("Color", "Black"), ("Weight", "420")]),
    ];

    public async Task<bool> SeedIfEmptyAsync(CancellationToken ct = default)
    {
        if (await db.Products.AnyAsync(ct) || await db.Suppliers.AnyAsync(ct))
            return false;

        var now = clock.UtcNow;

        db.CurrencyRates.AddRange(
            new CurrencyRate { Code = "CNY", RateToBase = 0.14m, UpdatedAt = now },
            new CurrencyRate { Code = "EUR", RateToBase = 1.08m, UpdatedAt = now });

        var warehouse = new Supplier { Name = "Own Warehouse", Code = "WH", Kind = SupplierKind.Warehouse, Currency = "USD", DefaultLeadTimeDays = 1 };
        var importer = new Supplier
        {
            Name = "City Electronics Importer", Code = "IMP", Kind = SupplierKind.Importer, Currency = "USD", DefaultLeadTimeDays = 2,
            ImportColumnMap = "supplier_sku=Item Code; cost=Dealer Price; stock=Qty",
        };
        var china = new Supplier
        {
            Name = "Shenzhen Partner", Code = "CN", Kind = SupplierKind.OverseasPartner, Currency = "CNY", DefaultLeadTimeDays = 15,
            ExtraCostPercent = 8, ExtraCostFixed = 2, Notes = "Sends a price list per period; orders inside the window ship in ~15 days.",
        };
        db.Suppliers.AddRange(warehouse, importer, china);

        db.PricingRules.AddRange(
            new PricingRule { Name = "Default markup", MarkupPercent = 18, Rounding = PriceRounding.Ends99, MinMargin = 3 },
            new PricingRule { Name = "Pre-order (China) markup", Supplier = china, MarkupPercent = 12, Rounding = PriceRounding.Ends99, MinMargin = 3 });

        var accessories = await writer.GetOrCreateCategoryPathAsync("Accessories", ct);
        db.PricingRules.Add(new PricingRule { Name = "Accessories markup", Category = accessories, MarkupPercent = 35, Rounding = PriceRounding.Ends99, MinMargin = 2 });

        var attributes = new Dictionary<string, SpecificationAttribute>(StringComparer.OrdinalIgnoreCase);
        foreach (var (header, onCard) in AttributeHeaders)
        {
            var attr = await writer.GetOrCreateAttributeAsync(header, ct);
            attr.ShowOnCard = onCard;
            attr.IsFilterable = !header.Contains("Resolution") && !header.Contains("Processor") && !header.Contains("Weight") && !header.Contains("Connector");
            attributes[attr.Name] = attr;
        }

        // China offer window: from yesterday until 10 days from now, so the demo shows a countdown.
        var windowStart = now.AddDays(-1);
        var windowEnd = now.AddDays(10);

        var index = 0;
        foreach (var d in Products)
        {
            var product = new Product
            {
                Sku = d.Sku,
                Name = d.Name,
                Slug = await writer.UniqueProductSlugAsync(d.Name, null, ct),
                ShortDescription = d.Summary,
                Description = $"{d.Summary}\n\nThis is sample data. Replace it with your own product descriptions, written by you or supplied with permission by the manufacturer or importer.",
                Category = await writer.GetOrCreateCategoryPathAsync(d.Category, ct),
                Brand = await writer.GetOrCreateBrandAsync(d.Brand, ct),
                IsPublished = true,
                CreatedAt = now.AddMinutes(-index++),
                UpdatedAt = now,
            };
            foreach (var (name, value) in d.Specs)
                CatalogWriter.SetSpecification(product, attributes[name], value);
            db.Products.Add(product);

            db.SupplierOffers.Add(new SupplierOffer
            {
                Supplier = importer, Product = product, SupplierSku = "IMP-" + d.Sku, Cost = d.ImporterCost, StockQuantity = d.ImporterStock, UpdatedAt = now,
            });
            if (d.ChinaCostCny is { } cny)
            {
                db.SupplierOffers.Add(new SupplierOffer
                {
                    Supplier = china, Product = product, SupplierSku = "CN-" + d.Sku, Cost = cny, StockQuantity = null,
                    ValidFrom = windowStart, ValidTo = windowEnd, UpdatedAt = now,
                });
            }
            if (d.WarehouseCost is { } whCost)
            {
                db.SupplierOffers.Add(new SupplierOffer
                {
                    Supplier = warehouse, Product = product, Cost = whCost, StockQuantity = d.WarehouseStock, UpdatedAt = now,
                });
            }
        }

        await db.SaveChangesAsync(ct);
        await pricing.RepriceAllAsync("Demo data", ct);
        return true;
    }
}
