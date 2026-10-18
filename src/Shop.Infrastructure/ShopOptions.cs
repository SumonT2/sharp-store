namespace Shop.Infrastructure;

public class ShopOptions
{
    public const string Section = "Shop";

    public string Name { get; set; } = "My Store";
    public string BaseCurrency { get; set; } = "USD";
    /// <summary>Symbol or code shown before prices, e.g. "$" or "৳".</summary>
    public string CurrencySymbol { get; set; } = "$";
    /// <summary>IANA or Windows time zone id used for CSV dates and display.</summary>
    public string TimeZone { get; set; } = "UTC";
    /// <summary>Offers delivering within this many days are sold as "in stock"; slower ones as pre-orders.</summary>
    public int InStockMaxLeadDays { get; set; } = 3;
    public decimal ShippingFee { get; set; } = 5m;
    public decimal? FreeShippingOver { get; set; } = 100m;
    public int MaxQuantityPerItem { get; set; } = 10;
}

public class JobOptions
{
    public const string Section = "Jobs";

    /// <summary>How often prices are recalculated, so offer windows open and close on time.</summary>
    public int RepriceIntervalMinutes { get; set; } = 10;
    public bool ScrapingEnabled { get; set; }
    public int ScrapeIntervalHours { get; set; } = 6;
}
