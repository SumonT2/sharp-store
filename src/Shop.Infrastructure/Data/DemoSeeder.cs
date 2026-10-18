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

    private static readonly (string Header, bool OnCard, bool Filterable)[] AttributeHeaders =
    [
        ("Keyboard > Layout", true, true),
        ("Keyboard > Switch Type", true, true),
        ("Keyboard > Hot-swappable", false, true),
        ("Keyboard > Keycaps", false, true),
        ("Mouse > Sensor DPI [DPI]", true, true),
        ("Mouse > Polling Rate [Hz]", false, true),
        ("Mouse > Buttons", false, true),
        ("Mouse Pad > Size [mm]", true, true),
        ("Connectivity > Connection", true, true),
        ("Connectivity > Battery [mAh]", false, false),
        ("General > Backlight", false, true),
        ("General > Weight [g]", false, false),
        ("General > Color", false, true),
        ("General > Warranty", false, true),
    ];

    private static readonly HashSet<string> FeaturedSkus = ["KB-KV-K75W", "KB-KV-K68", "MS-CL-G3W", "MS-AR-M7", "KB-KV-K100", "MS-CL-G1"];

    private const string Keyboards = "Keyboards > Mechanical Keyboards";
    private const string GamingMice = "Mice > Gaming Mice";
    private const string WirelessMice = "Mice > Wireless Mice";

    // Costs: importer and warehouse in BDT, China partner in CNY.
    private static readonly DemoProduct[] Products =
    [
        new("KB-KV-K68", "Keyvo K68 Hot-swap 65% Keyboard", Keyboards, "Keyvo", "Compact 65% layout with hot-swappable switches and PBT keycaps.",
            4200m, 25, 210m, 4000m, 5,
            [("Layout", "65%"), ("Switch Type", "Red (Linear)"), ("Hot-swappable", "Yes"), ("Keycaps", "PBT"), ("Connection", "Wired"), ("Backlight", "RGB"), ("Weight", "780"), ("Color", "Black"), ("Warranty", "1 year")]),
        new("KB-KV-K75W", "Keyvo K75 Wireless 75% Keyboard", Keyboards, "Keyvo", "Tri-mode wireless 75% board with a gasket mount and a 4000 mAh battery.",
            6900m, 12, 330m, null, null,
            [("Layout", "75%"), ("Switch Type", "Brown (Tactile)"), ("Hot-swappable", "Yes"), ("Keycaps", "PBT"), ("Connection", "Wired + 2.4G + Bluetooth"), ("Battery", "4000"), ("Backlight", "RGB"), ("Weight", "950"), ("Color", "White"), ("Warranty", "1 year")]),
        new("KB-KV-K87", "Keyvo K87 TKL Keyboard", Keyboards, "Keyvo", "Tenkeyless board with clicky blue switches for typists.",
            3600m, 0, 175m, null, null,
            [("Layout", "TKL"), ("Switch Type", "Blue (Clicky)"), ("Hot-swappable", "No"), ("Keycaps", "ABS"), ("Connection", "Wired"), ("Backlight", "White"), ("Weight", "820"), ("Color", "Black"), ("Warranty", "6 months")]),
        new("KB-KV-K100", "Keyvo K100 Full-size Pro Keyboard", Keyboards, "Keyvo", "Full-size wireless board with a number pad, knob and 8000 mAh battery.",
            8900m, 6, 450m, null, null,
            [("Layout", "Full-size"), ("Switch Type", "Red (Linear)"), ("Hot-swappable", "Yes"), ("Keycaps", "PBT"), ("Connection", "Wired + 2.4G + Bluetooth"), ("Battery", "8000"), ("Backlight", "RGB"), ("Weight", "1250"), ("Color", "Grey"), ("Warranty", "1 year")]),
        new("KB-AR-61", "Arkon A61 60% Keyboard", Keyboards, "Arkon", "Small, light 60% keyboard — great first mechanical board.",
            2900m, 40, 135m, null, null,
            [("Layout", "60%"), ("Switch Type", "Red (Linear)"), ("Hot-swappable", "Yes"), ("Keycaps", "ABS"), ("Connection", "Wired"), ("Backlight", "RGB"), ("Weight", "560"), ("Color", "White"), ("Warranty", "6 months")]),
        new("KB-AR-84W", "Arkon A84 Wireless 75% Keyboard", Keyboards, "Arkon", "75% Bluetooth keyboard that switches between three devices.",
            5400m, 0, 260m, null, null,
            [("Layout", "75%"), ("Switch Type", "Brown (Tactile)"), ("Hot-swappable", "Yes"), ("Keycaps", "PBT"), ("Connection", "Wired + Bluetooth"), ("Battery", "3000"), ("Backlight", "RGB"), ("Weight", "870"), ("Color", "Black"), ("Warranty", "1 year")]),
        new("MS-CL-G1", "Clickra G1 Gaming Mouse", GamingMice, "Clickra", "Wired gaming mouse with a 12,000 DPI sensor and RGB lighting.",
            1600m, 50, 70m, null, null,
            [("Sensor DPI", "12000"), ("Polling Rate", "1000"), ("Buttons", "6"), ("Connection", "Wired"), ("Backlight", "RGB"), ("Weight", "68"), ("Color", "Black"), ("Warranty", "6 months")]),
        new("MS-CL-G3W", "Clickra G3 Wireless Gaming Mouse", GamingMice, "Clickra", "59 g wireless mouse with a 26,000 DPI sensor.",
            3900m, 15, 185m, null, null,
            [("Sensor DPI", "26000"), ("Polling Rate", "1000"), ("Buttons", "5"), ("Connection", "Wired + 2.4G"), ("Battery", "500"), ("Backlight", "None"), ("Weight", "59"), ("Color", "White"), ("Warranty", "1 year")]),
        new("MS-CL-G5", "Clickra G5 Ultralight Wireless Mouse", GamingMice, "Clickra", "49 g esports mouse with 8 kHz polling.",
            5200m, 0, 250m, null, null,
            [("Sensor DPI", "30000"), ("Polling Rate", "8000"), ("Buttons", "5"), ("Connection", "Wired + 2.4G"), ("Battery", "300"), ("Backlight", "None"), ("Weight", "49"), ("Color", "Black"), ("Warranty", "1 year")]),
        new("MS-AR-W2", "Arkon W2 Silent Wireless Mouse", WirelessMice, "Arkon", "Quiet-click wireless mouse for office and home.",
            750m, 120, 30m, null, null,
            [("Sensor DPI", "1600"), ("Polling Rate", "125"), ("Buttons", "3"), ("Connection", "2.4G"), ("Backlight", "None"), ("Weight", "85"), ("Color", "Grey"), ("Warranty", "6 months")]),
        new("MS-AR-M7", "Arkon M7 Multi-device Mouse", WirelessMice, "Arkon", "Rechargeable mouse that pairs with three devices.",
            2400m, 30, 110m, null, null,
            [("Sensor DPI", "4000"), ("Polling Rate", "125"), ("Buttons", "7"), ("Connection", "2.4G + Bluetooth"), ("Battery", "500"), ("Backlight", "None"), ("Weight", "98"), ("Color", "Black"), ("Warranty", "1 year")]),
        new("AC-PD-XL", "Clickra Speed Mouse Pad XL", "Accessories > Mouse Pads", "Clickra", "Desk-size cloth pad with stitched edges.",
            650m, 80, 25m, null, null,
            [("Size", "900 x 400"), ("Color", "Black"), ("Weight", "520")]),
        new("AC-SW-RED", "Keyvo Linear Red Switches (35 pcs)", "Accessories > Switches & Keycaps", "Keyvo", "Pre-lubed linear switches for hot-swap keyboards.",
            900m, 60, 38m, null, null,
            [("Switch Type", "Red (Linear)"), ("Warranty", "No warranty")]),
        new("AC-KC-PBT", "Keyvo PBT Keycap Set", "Accessories > Switches & Keycaps", "Keyvo", "Double-shot PBT keycaps that fit most layouts.",
            1800m, 0, 90m, null, null,
            [("Keycaps", "PBT"), ("Color", "Beige"), ("Warranty", "No warranty")]),
    ];

    private async Task AddOptionsAsync(
        string mainSku, string mainLabel, string varyingAttribute,
        (string Sku, string Label, decimal ImporterCost, int Stock, decimal? ChinaCostCny)[] options,
        Supplier importer, Supplier china, DateTime now, DateTime windowStart, DateTime windowEnd, CancellationToken ct)
    {
        var main = await db.Products.Include(p => p.Specifications).ThenInclude(s => s.Attribute).FirstAsync(p => p.Sku == mainSku, ct);
        main.VariantLabel = mainLabel;
        foreach (var (sku, label, cost, stock, cny) in options)
        {
            var variant = new Product
            {
                Sku = sku,
                Name = $"{main.Name} – {label}",
                Slug = await writer.UniqueProductSlugAsync($"{main.Name} {label}", null, ct),
                VariantOf = main,
                VariantLabel = label,
                CategoryId = main.CategoryId,
                BrandId = main.BrandId,
                ShortDescription = main.ShortDescription,
                Description = main.Description,
                IsPublished = true,
                CreatedAt = now,
                UpdatedAt = now,
            };
            foreach (var spec in main.Specifications)
                CatalogWriter.SetSpecification(variant, spec.Attribute!, spec.Attribute!.Name == varyingAttribute ? label : spec.Value);
            db.Products.Add(variant);
            db.SupplierOffers.Add(new SupplierOffer { Supplier = importer, Product = variant, SupplierSku = "IMP-" + sku, Cost = cost, StockQuantity = stock, UpdatedAt = now });
            if (cny is { } c)
            {
                db.SupplierOffers.Add(new SupplierOffer
                {
                    Supplier = china, Product = variant, SupplierSku = "CN-" + sku, Cost = c, StockQuantity = null,
                    ValidFrom = windowStart, ValidTo = windowEnd, UpdatedAt = now,
                });
            }
        }
    }

    public async Task<bool> SeedIfEmptyAsync(CancellationToken ct = default)
    {
        if (await db.Products.AnyAsync(ct) || await db.Suppliers.AnyAsync(ct))
            return false;

        var now = clock.UtcNow;

        db.CurrencyRates.AddRange(
            new CurrencyRate { Code = "CNY", RateToBase = 16.8m, UpdatedAt = now },
            new CurrencyRate { Code = "USD", RateToBase = 122m, UpdatedAt = now });

        var warehouse = new Supplier { Name = "Own Warehouse", Code = "WH", Kind = SupplierKind.Warehouse, Currency = "BDT", DefaultLeadTimeDays = 1 };
        var importer = new Supplier
        {
            Name = "Dhaka Peripherals Importer", Code = "IMP", Kind = SupplierKind.Importer, Currency = "BDT", DefaultLeadTimeDays = 2,
            ImportColumnMap = "supplier_sku=Item Code; cost=Dealer Price; stock=Qty",
        };
        var china = new Supplier
        {
            Name = "Shenzhen Partner", Code = "CN", Kind = SupplierKind.OverseasPartner, Currency = "CNY", DefaultLeadTimeDays = 15,
            ExtraCostPercent = 8, ExtraCostFixed = 250, Notes = "Sends a price list per period; orders inside the window ship in ~15 days.",
        };
        db.Suppliers.AddRange(warehouse, importer, china);

        db.PricingRules.AddRange(
            new PricingRule { Name = "Default markup", MarkupPercent = 15, Rounding = PriceRounding.UpTo10, MinMargin = 150 },
            new PricingRule { Name = "Pre-order (China) markup", Supplier = china, MarkupPercent = 10, Rounding = PriceRounding.UpTo10, MinMargin = 150 });

        var accessories = await writer.GetOrCreateCategoryPathAsync("Accessories", ct);
        db.PricingRules.Add(new PricingRule { Name = "Accessories markup", Category = accessories, MarkupPercent = 30, Rounding = PriceRounding.UpTo10, MinMargin = 100 });

        var attributes = new Dictionary<string, SpecificationAttribute>(StringComparer.OrdinalIgnoreCase);
        foreach (var (header, onCard, filterable) in AttributeHeaders)
        {
            var attr = await writer.GetOrCreateAttributeAsync(header, ct);
            attr.ShowOnCard = onCard;
            attr.IsFilterable = filterable;
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
                IsFeatured = FeaturedSkus.Contains(d.Sku),
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

        // Options of the same product: the K68 in three switch types, the G3 mouse in two colours.
        await AddOptionsAsync("KB-KV-K68", "Red (Linear)", "Switch Type",
            [("KB-KV-K68-BRN", "Brown (Tactile)", 4350m, 9, null), ("KB-KV-K68-BLU", "Blue (Clicky)", 4150m, 0, 205m)],
            importer, china, now, windowStart, windowEnd, ct);
        await AddOptionsAsync("MS-CL-G3W", "White", "Color",
            [("MS-CL-G3W-BLK", "Black", 3950m, 7, null)],
            importer, china, now, windowStart, windowEnd, ct);

        await db.SaveChangesAsync(ct);
        await pricing.RepriceAllAsync("Demo data", ct);
        return true;
    }
}
