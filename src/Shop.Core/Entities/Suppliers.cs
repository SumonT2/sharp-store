namespace Shop.Core.Entities;

public enum SupplierKind
{
    /// <summary>Your own stock.</summary>
    Warehouse = 0,
    /// <summary>Local importer that sends stock/price lists.</summary>
    Importer = 1,
    /// <summary>Overseas partner offering time-limited prices with longer delivery.</summary>
    OverseasPartner = 2,
    /// <summary>A website whose prices are scraped.</summary>
    WebSource = 3,
}

public class Supplier
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Short unique code, e.g. "CN-LI".</summary>
    public string Code { get; set; } = "";
    public SupplierKind Kind { get; set; }
    public string Currency { get; set; } = "USD";
    public int DefaultLeadTimeDays { get; set; } = 2;
    /// <summary>Shipping, duty, etc. as a percentage of the converted cost.</summary>
    public decimal ExtraCostPercent { get; set; }
    /// <summary>Fixed extra cost per unit, in the shop's base currency.</summary>
    public decimal ExtraCostFixed { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }

    /// <summary>
    /// Maps this supplier's CSV headers to standard fields, one per line or separated by ';'.
    /// Example: "cost=Unit Price; stock=Qty Available; supplier_sku=Item Code".
    /// </summary>
    public string? ImportColumnMap { get; set; }

    // Scraping configuration (WebSource suppliers).
    public string? ScrapePriceSelector { get; set; }
    public string? ScrapeStockSelector { get; set; }
    /// <summary>Regex matched against the stock element text; a match means "in stock".</summary>
    public string? ScrapeInStockPattern { get; set; }
    public int ScrapeDelaySeconds { get; set; } = 5;

    public List<SupplierOffer> Offers { get; set; } = new();
}

/// <summary>What a supplier can provide for one of our products, and at what cost.</summary>
public class SupplierOffer
{
    public int Id { get; set; }
    public int SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public string? SupplierSku { get; set; }
    public decimal Cost { get; set; }
    /// <summary>Currency of <see cref="Cost"/>; falls back to the supplier currency when empty.</summary>
    public string? Currency { get; set; }
    /// <summary>Null means the supplier does not limit quantity (e.g. order-on-demand).</summary>
    public int? StockQuantity { get; set; }
    /// <summary>Null means the supplier default applies.</summary>
    public int? LeadTimeDays { get; set; }
    /// <summary>Offer window (UTC). Null on either side means open-ended.</summary>
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public bool IsActive { get; set; } = true;

    public string? SourceUrl { get; set; }
    public DateTime? LastScrapedAt { get; set; }
    public string? LastScrapeError { get; set; }
    public DateTime UpdatedAt { get; set; }

    public int EffectiveLeadTimeDays => LeadTimeDays ?? Supplier?.DefaultLeadTimeDays ?? 0;
    public string EffectiveCurrency => string.IsNullOrWhiteSpace(Currency) ? Supplier?.Currency ?? "" : Currency;

    public bool IsAvailableAt(DateTime nowUtc) =>
        IsActive
        && (Supplier?.IsActive ?? true)
        && Cost > 0
        && (ValidFrom is null || ValidFrom <= nowUtc)
        && (ValidTo is null || ValidTo > nowUtc)
        && (StockQuantity is null || StockQuantity > 0);
}

public enum PriceRounding
{
    None = 0,
    /// <summary>Round up to a whole number.</summary>
    Whole = 1,
    /// <summary>Round up to x.99.</summary>
    Ends99 = 2,
    /// <summary>Round up to a whole number ending in 9.</summary>
    Ends9 = 3,
    UpTo10 = 4,
    UpTo50 = 5,
    UpTo100 = 6,
}

public class PricingRule
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Higher priority wins when several rules match.</summary>
    public int Priority { get; set; }
    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }
    public int? BrandId { get; set; }
    public Brand? Brand { get; set; }
    public decimal MarkupPercent { get; set; }
    public decimal FixedMarkup { get; set; }
    /// <summary>Minimum profit per unit in base currency.</summary>
    public decimal? MinMargin { get; set; }
    public PriceRounding Rounding { get; set; }
    public bool IsActive { get; set; } = true;
}

public class CurrencyRate
{
    /// <summary>ISO code, e.g. "CNY".</summary>
    public string Code { get; set; } = "";
    /// <summary>How much one unit of this currency is worth in the base currency.</summary>
    public decimal RateToBase { get; set; }
    public DateTime UpdatedAt { get; set; }
}
