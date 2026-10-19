namespace Shop.Infrastructure;

public class ShopOptions
{
    public const string Section = "Shop";

    public string Name { get; set; } = "TechBhai";
    public string Tagline { get; set; } = "Genuine gadgets, honest prices — delivered all over Bangladesh.";
    public string BaseCurrency { get; set; } = "BDT";
    /// <summary>Symbol shown before prices.</summary>
    public string CurrencySymbol { get; set; } = "৳";
    /// <summary>Decimal places shown in prices (BDT prices are usually whole taka).</summary>
    public int PriceDecimals { get; set; }
    /// <summary>Culture used for digit grouping. "en-IN" groups the South Asian way: 1,24,990.</summary>
    public string NumberCulture { get; set; } = "en-IN";
    /// <summary>IANA or Windows time zone id used for CSV dates and display.</summary>
    public string TimeZone { get; set; } = "Asia/Dhaka";
    /// <summary>Offers delivering within this many days are sold as "in stock"; slower ones as pre-orders.</summary>
    public int InStockMaxLeadDays { get; set; } = 3;
    /// <summary>
    /// Delivery areas the customer chooses from at checkout. Left empty here on purpose: configuration binding
    /// appends to list defaults, so the defaults live in <see cref="EffectiveDeliveryZones"/>.
    /// </summary>
    public List<DeliveryZone> DeliveryZones { get; set; } = new();
    public decimal? FreeShippingOver { get; set; } = 50_000m;
    public int MaxQuantityPerItem { get; set; } = 10;
    /// <summary>Regex a phone number must match after spaces and dashes are removed. Empty disables the check.</summary>
    public string? PhonePattern { get; set; } = @"^(?:\+?88)?01[3-9]\d{8}$";
    public string PhoneHint { get; set; } = "Bangladeshi mobile number, e.g. 01712-345678";

    /// <summary>Default accent colour (ocean, emerald, teal, violet, amber, raspberry or coral); visitors can pick another.</summary>
    public string Accent { get; set; } = "ocean";

    /// <summary>Customer phone line shown in the header and footer; hidden when empty.</summary>
    public string? Hotline { get; set; }
    /// <summary>When the hotline answers, e.g. "10am–8pm, every day".</summary>
    public string? SupportHours { get; set; }
    /// <summary>Shop or pickup address shown in the footer; hidden when empty.</summary>
    public string? Address { get; set; }
    /// <summary>The big banner at the top of the home page.</summary>
    public HomeBanner Hero { get; set; } = new()
    {
        Eyebrow = "Fresh stock every week",
        Title = "Type faster.\nAim sharper.",
        Text = "Mechanical keyboards, gaming mice and desk gear. Compare every switch, layout and sensor, then pay cash on delivery.",
        ImageUrl = "/img/demo/hero-light.webp",
        DarkImageUrl = "/img/demo/hero.webp",
        Url = "/products",
        LinkText = "Shop all products",
    };
    /// <summary>Optional smaller banner beside the hero (the other one advertises pre-order deals).</summary>
    public HomeBanner? Promo { get; set; }

    /// <summary>Rounds a selling price up to the precision customers see, so the shown price is what they pay.</summary>
    public decimal RoundPrice(decimal price)
    {
        var factor = (decimal)Math.Pow(10, Math.Clamp(PriceDecimals, 0, 4));
        return Math.Ceiling(price * factor) / factor;
    }

    public IReadOnlyList<DeliveryZone> EffectiveDeliveryZones => DeliveryZones.Count > 0
        ? DeliveryZones
        : [new DeliveryZone { Name = "Inside Dhaka", Fee = 70, Days = "1–2 days" }, new DeliveryZone { Name = "Outside Dhaka", Fee = 130, Days = "2–4 days" }];
}

public class HomeBanner
{
    /// <summary>Small label above the title, e.g. "Deal of the week".</summary>
    public string? Eyebrow { get; set; }
    /// <summary>Headline; a line break ("\n") starts a new line.</summary>
    public string Title { get; set; } = "";
    public string? Text { get; set; }
    /// <summary>Wide picture (about 2:1) with its subject on the right, so the text on the left stays readable.</summary>
    public string? ImageUrl { get; set; }
    /// <summary>Optional darker picture used with the dark theme; the normal one is used when empty.</summary>
    public string? DarkImageUrl { get; set; }
    public string? Url { get; set; }
    public string? LinkText { get; set; }
    /// <summary>When set, the banner shows this product's live "from" price and links to it.</summary>
    public string? ProductSku { get; set; }
}

public class DeliveryZone
{
    public string Name { get; set; } = "";
    public decimal Fee { get; set; }
    /// <summary>Shown to the customer, e.g. "1–2 days".</summary>
    public string? Days { get; set; }
}

public class JobOptions
{
    public const string Section = "Jobs";

    /// <summary>How often prices are recalculated, so offer windows open and close on time.</summary>
    public int RepriceIntervalMinutes { get; set; } = 10;
    public bool ScrapingEnabled { get; set; }
    public int ScrapeIntervalHours { get; set; } = 6;
}
