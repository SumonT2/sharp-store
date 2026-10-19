using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shop.Core.Entities;
using Shop.Infrastructure;
using Shop.Infrastructure.Catalog;
using Shop.Infrastructure.Data;
using Shop.Infrastructure.Importing;
using Shop.Infrastructure.Pricing;

namespace Shop.Tests;

/// <summary>Runs the CSV imports and pricing against an in-memory SQLite database.</summary>
public sealed class ImportTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly ShopDbContext _db;
    private readonly ShopClock _clock;

    private sealed class FixedTime(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc);
    }

    public ImportTests()
    {
        _connection.Open();
        _db = new ShopDbContext(new DbContextOptionsBuilder<ShopDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _clock = new ShopClock(new FixedTime(Now), Options.Create(new ShopOptions { TimeZone = "UTC" }));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private PriceUpdateService Pricing() =>
        new(_db, _clock, Options.Create(new ShopOptions { BaseCurrency = "USD", InStockMaxLeadDays = 3 }), NullLogger<PriceUpdateService>.Instance);

    private Task<ImportResult> ImportCatalog(string csv) =>
        new CatalogImportService(_db, new CatalogWriter(_db), Pricing(), _clock).ImportAsync(Stream(csv), "catalog.csv");

    private Task<ImportResult> ImportOffers(string csv, Supplier supplier, bool markMissing = false, bool createMissing = false) =>
        new OfferImportService(_db, new CatalogWriter(_db), Pricing(), _clock).ImportAsync(Stream(csv), "offers.csv",
            new OfferImportOptions { SupplierId = supplier.Id, MarkMissingOutOfStock = markMissing, CreateMissingProducts = createMissing });

    private static MemoryStream Stream(string s) => new(Encoding.UTF8.GetBytes(s));

    private async Task<Supplier> AddSupplier(string code, string currency, int leadDays, string? columnMap = null)
    {
        var s = new Supplier { Name = code, Code = code, Currency = currency, DefaultLeadTimeDays = leadDays, ImportColumnMap = columnMap };
        _db.Suppliers.Add(s);
        await _db.SaveChangesAsync();
        return s;
    }

    private Task<Product> Product(string sku) =>
        _db.Products.AsNoTracking().Include(p => p.Specifications).ThenInclude(s => s.Attribute).Include(p => p.Category).SingleAsync(p => p.Sku == sku);

    [Fact]
    public async Task Catalog_import_creates_products_categories_and_specs_then_updates_only_given_cells()
    {
        var result = await ImportCatalog("""
            sku,name,category,brand,spec:Display > Screen Size [inch],spec:RAM [GB]
            P1,Phone One,Phones > Smartphones,Acme,6.5,8
            P2,Phone Two,Phones > Smartphones,Acme,6.1,
            """);
        Assert.Equal(2, result.Created);
        Assert.Equal(0, result.Failed);
        Assert.Equal(2, await _db.Categories.CountAsync());

        var p1 = await Product("P1");
        Assert.Equal("Smartphones", p1.Category!.Name);
        var size = p1.Specifications.Single(s => s.Attribute!.Name == "Screen Size");
        Assert.Equal("6.5", size.Value);
        Assert.Equal("inch", size.Attribute!.Unit);
        Assert.Equal("Display", size.Attribute.GroupName);
        Assert.Single((await Product("P2")).Specifications);

        // Second import: empty cells keep values, "-" clears.
        result = await ImportCatalog("""
            sku,name,spec:RAM [GB],spec:Display > Screen Size [inch]
            P1,,12,-
            """);
        Assert.Equal(1, result.Updated);
        p1 = await Product("P1");
        Assert.Equal("Phone One", p1.Name);
        Assert.Equal("12", p1.Specifications.Single().Value);
    }

    [Fact]
    public async Task Catalog_import_reports_bad_rows_without_stopping()
    {
        var result = await ImportCatalog("""
            sku,name,published
            ,No sku,yes
            P3,,yes
            P4,Fine,maybe
            P5,Good,no
            """);
        Assert.Equal(1, result.Created);
        Assert.Equal(3, result.Failed);
        Assert.False((await Product("P5")).IsPublished);
    }

    [Fact]
    public async Task Offer_imports_drive_price_and_availability_including_time_limited_pre_orders()
    {
        await ImportCatalog("sku,name\nP1,Phone One\n");
        var importer = await AddSupplier("IMP", "USD", 2, "supplier_sku=Item Code; cost=Dealer Price; stock=Qty");
        var china = await AddSupplier("CN", "CNY", 15);
        _db.CurrencyRates.Add(new CurrencyRate { Code = "CNY", RateToBase = 0.14m, UpdatedAt = Now });
        _db.PricingRules.Add(new PricingRule { Name = "Default", MarkupPercent = 20 });
        await _db.SaveChangesAsync();

        // Local importer uses its own column names (mapped on the supplier).
        var r = await ImportOffers("sku,Item Code,Dealer Price,Qty\nP1,X-1,100,4\n", importer);
        Assert.Equal(1, r.Created);
        var p = await Product("P1");
        Assert.Equal(Availability.InStock, p.Availability);
        Assert.Equal(120m, p.Price);
        Assert.Equal(4, p.StockQuantity);

        // China partner: cheaper, unlimited, but only until 2026-10-25 (end of day).
        r = await ImportOffers("sku,cost,stock,valid_from,valid_to\nP1,600,*,2026-10-01,2026-10-25\n", china);
        Assert.Equal(0, r.Failed);
        p = await Product("P1");
        Assert.Equal(Availability.InStock, p.Availability); // in stock still wins

        // Importer sells out: the full stock list no longer contains P1.
        r = await ImportOffers("sku,Item Code,Dealer Price,Qty\n", importer, markMissing: true);
        p = await Product("P1");
        Assert.Equal(Availability.PreOrder, p.Availability);
        Assert.Equal(15, p.LeadTimeDays);
        Assert.Equal(101m, p.Price); // 600 * 0.14 * 1.2 = 100.8, rounded up to whole taka
        Assert.Equal(new DateTime(2026, 10, 26, 0, 0, 0), p.OfferEndsAt);
        Assert.Null(p.StockQuantity);
    }

    [Fact]
    public async Task Offer_import_matches_by_supplier_sku_and_can_create_drafts()
    {
        var importer = await AddSupplier("IMP", "USD", 2);
        var r = await ImportOffers("""
            supplier_sku,name,cost,stock
            ABC-1,New Gadget,10,3
            ABC-2,,10,3
            """, importer, createMissing: true);
        Assert.Equal(1, r.Created);
        Assert.Equal(1, r.Failed);
        var draft = await Product("IMP-ABC-1");
        Assert.False(draft.IsPublished);

        r = await ImportOffers("supplier_sku,stock\nABC-1,7\n", importer);
        Assert.Equal(1, r.Updated);
        Assert.Equal(7, (await _db.SupplierOffers.SingleAsync()).StockQuantity);
    }

    [Fact]
    public async Task Sample_csv_files_import_without_errors()
    {
        var samples = Path.Combine(RepoRoot(), "samples", "csv");
        var catalog = await new CatalogImportService(_db, new CatalogWriter(_db), Pricing(), _clock)
            .ImportAsync(File.OpenRead(Path.Combine(samples, "1-catalog.csv")), "1-catalog.csv");
        Assert.Equal(0, catalog.Failed);
        Assert.Equal(4, catalog.Created);
        Assert.Contains("keycap puller", (await Product("KB-KV-K68")).Description);

        // The China file also lists products the catalog sample doesn't have; those rows should be the only failures.
        await ImportCatalog("sku,name\nKB-KV-K100,Keyvo K100\nMS-CL-G5,Clickra G5\n");

        var importer = await AddSupplier("IMP", "BDT", 2, "supplier_sku=Item Code\ncost=Dealer Price\nstock=Qty");
        var china = await AddSupplier("CN", "CNY", 15);
        _db.CurrencyRates.Add(new CurrencyRate { Code = "CNY", RateToBase = 16.8m, UpdatedAt = Now });
        await _db.SaveChangesAsync();

        foreach (var (file, supplier) in new[] { ("2-importer-stock.csv", importer), ("3-importer-own-format.csv", importer), ("4-china-price-list.csv", china) })
        {
            var r = await new OfferImportService(_db, new CatalogWriter(_db), Pricing(), _clock).ImportAsync(
                File.OpenRead(Path.Combine(samples, file)), file, new OfferImportOptions { SupplierId = supplier.Id });
            Assert.True(r.Failed == 0, $"{file}: {string.Join("; ", r.Messages)}");
        }

        var k98 = await _db.SupplierOffers.SingleAsync(o => o.SupplierId == importer.Id && o.SupplierSku == "DPI-K98-BRN");
        Assert.Equal(4, k98.StockQuantity); // updated by the importer's own-format file
        var g5 = await _db.SupplierOffers.SingleAsync(o => o.SupplierId == china.Id && o.SupplierSku == "SZ-G5");
        Assert.Equal(new DateTime(2026, 11, 1, 10, 0, 0), g5.ValidFrom); // this test clock uses UTC as shop time
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Shop.slnx")))
                return dir.FullName;
        }
        throw new InvalidOperationException("Repository root not found.");
    }

    [Fact]
    public async Task Links_only_file_creates_drafts_waiting_for_scraped_prices()
    {
        var web = await AddSupplier("GB", "BDT", 2);
        var r = await ImportOffers("""
            supplier_sku,name,source_url
            UG-CH-65,UGREEN 65W charger,https://supplier.example/p/ug-ch-65
            """, web, createMissing: true);
        Assert.Equal(0, r.Failed);
        var offer = await _db.SupplierOffers.Include(o => o.Product).SingleAsync();
        Assert.Equal(0m, offer.Cost);
        Assert.False(offer.Product!.IsPublished);
        Assert.Equal(Availability.OutOfStock, offer.Product.Availability); // until the scraper fills in a price
    }

    [Fact]
    public async Task Catalog_import_links_variants_and_listing_shows_the_family_price_range()
    {
        // The variant row comes before its main product on purpose.
        var r = await ImportCatalog("""
            sku,name,variant_of,variant_label
            K68-BRN,K68 Brown,K68,Brown
            K68,K68 Keyboard,,Red
            K68-BAD,K68 Bad,NOPE,Blue
            """);
        Assert.Equal(1, r.Failed);
        var brown = await Product("K68-BRN");
        var main = await Product("K68");
        Assert.Equal(main.Id, brown.VariantOfId);
        Assert.Equal("Red", main.VariantLabel);
        Assert.Null((await Product("K68-BAD")).VariantOfId);

        var importer = await AddSupplier("IMP", "USD", 2); // the test shop's base currency
        await ImportOffers("sku,cost,stock\nK68,4600,5\nK68-BRN,4950,2\n", importer);

        main = await _db.Products.AsNoTracking().SingleAsync(p => p.Sku == "K68");
        Assert.Equal(4600m, main.ListPrice);
        Assert.Equal(4950m, main.ListPriceMax);
        Assert.Equal(2, main.ListOptionCount);
        Assert.Equal(Availability.InStock, main.ListAvailability);

        // A variant can't have variants of its own.
        r = await ImportCatalog("sku,variant_of\nK68,K68-BRN\n");
        Assert.Equal(1, r.Failed);
    }

    [Fact]
    public async Task Offer_import_rejects_invalid_values()
    {
        await ImportCatalog("sku,name\nP1,One\nP2,Two\nP3,Three\nP4,Four\n");
        var s = await AddSupplier("IMP", "USD", 2);
        var r = await ImportOffers("""
            sku,cost,currency,valid_from,valid_to
            P1,abc,,,
            P2,10,DOLLARS,,
            P3,10,,2026-10-10,2026-10-01
            P4,10,,18/10/2026,
            NOPE,10,,,
            """, s);
        Assert.Equal(5, r.Failed);
        Assert.Equal(0, await _db.SupplierOffers.CountAsync());
    }
    [Fact]
    public async Task Catalog_import_reads_gallery_and_option_pictures()
    {
        var result = await ImportCatalog("""
            sku,name,image_url,gallery,option_image
            P1,Board,/img/a.webp,/img/b.webp | /img/a.webp |https://cdn.example.com/c.jpg,/img/red.webp
            """);
        Assert.Equal(0, result.Failed);
        var p = await Product("P1");
        Assert.Equal("/img/red.webp", p.OptionImageUrl);
        // Main photo first, then the gallery without repeats.
        Assert.Equal(["/img/a.webp", "/img/b.webp", "https://cdn.example.com/c.jpg"], p.Photos());

        // "-" clears the gallery; an empty cell keeps it.
        await ImportCatalog("sku,gallery,option_image\nP1,-,\n");
        p = await Product("P1");
        Assert.Null(p.GalleryUrls);
        Assert.Equal("/img/red.webp", p.OptionImageUrl);
        Assert.Equal(["/img/a.webp"], p.Photos());
    }

    [Fact]
    public async Task Top_selling_ranks_main_products_by_recent_units_and_skips_cancelled_orders()
    {
        await ImportCatalog("""
            sku,name,variant_of,variant_label
            A,Board A,,Red
            A-BRN,Board A Brown,A,Brown
            B,Board B,,
            C,Board C,,
            """);
        var importer = await AddSupplier("IMP", "USD", 2);
        _db.PricingRules.Add(new PricingRule { Name = "Default", MarkupPercent = 20 });
        await _db.SaveChangesAsync();
        await ImportOffers("sku,cost,stock\nA,100,5\nA-BRN,100,5\nB,100,5\nC,100,5\n", importer);

        var ids = await _db.Products.ToDictionaryAsync(p => p.Sku, p => p.Id);
        void Order(OrderStatus status, DateTime at, params (string Sku, int Qty)[] lines) => _db.Orders.Add(new Order
        {
            Number = Guid.NewGuid().ToString("N")[..8], Status = status, CreatedAt = at,
            Items = lines.Select(l => new OrderItem { ProductId = ids[l.Sku], Sku = l.Sku, Name = l.Sku, Quantity = l.Qty }).ToList(),
        });
        Order(OrderStatus.Delivered, Now.AddDays(-2), ("B", 3));
        Order(OrderStatus.Pending, Now.AddDays(-1), ("A", 1), ("A-BRN", 3)); // options count for the main product: 4
        Order(OrderStatus.Cancelled, Now.AddDays(-1), ("C", 10));
        Order(OrderStatus.Delivered, Now.AddDays(-60), ("C", 10));         // too old
        await _db.SaveChangesAsync();

        var top = await new CatalogQueryService(_db).TopSellingAsync(5, Now.AddDays(-30));
        Assert.Equal(["Board A", "Board B"], top.Select(c => c.Name));
    }
}
