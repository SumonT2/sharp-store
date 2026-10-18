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
        Assert.Equal(100.8m, p.Price); // 600 * 0.14 * 1.2
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
}
