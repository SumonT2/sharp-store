using System.ComponentModel.DataAnnotations;
using Shop.Core.Entities;

namespace Shop.Web.Areas.Admin.Models;

public sealed class LoginInput
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = "";

    public string? ReturnUrl { get; set; }
}

public sealed class ChangePasswordInput
{
    [Required, DataType(DataType.Password)]
    public string CurrentPassword { get; set; } = "";

    [Required, DataType(DataType.Password), MinLength(10, ErrorMessage = "Use at least 10 characters.")]
    public string NewPassword { get; set; } = "";

    [Compare(nameof(NewPassword), ErrorMessage = "The passwords don't match.")]
    public string ConfirmPassword { get; set; } = "";
}

public sealed class DashboardViewModel
{
    public int Products { get; init; }
    public int Published { get; init; }
    public int InStock { get; init; }
    public int PreOrder { get; init; }
    public int OutOfStock { get; init; }
    public int Drafts { get; init; }
    public int PendingOrders { get; init; }
    public decimal SalesLast30Days { get; init; }
    public required IReadOnlyList<Order> RecentOrders { get; init; }
    public required IReadOnlyList<ImportLog> RecentImports { get; init; }
    public required IReadOnlyList<SupplierOffer> ExpiringOffers { get; init; }
    public required IReadOnlyList<SupplierOffer> ScrapeErrors { get; init; }
}

public sealed class ProductEditInput
{
    public int Id { get; set; }

    [Required, StringLength(100)]
    public string Sku { get; set; } = "";

    [Required, StringLength(300)]
    public string Name { get; set; } = "";

    [StringLength(200)]
    public string? Slug { get; set; }

    [Display(Name = "Category"), StringLength(500)]
    public string? CategoryPath { get; set; }

    [StringLength(200)]
    public string? Brand { get; set; }

    [Display(Name = "Short description"), StringLength(1000)]
    public string? ShortDescription { get; set; }

    [StringLength(20000)]
    public string? Description { get; set; }

    [Display(Name = "Image URL"), Url, StringLength(1000)]
    public string? ImageUrl { get; set; }

    [Display(Name = "Published")]
    public bool IsPublished { get; set; } = true;

    [Display(Name = "Featured")]
    public bool IsFeatured { get; set; }

    [Display(Name = "Variant of (main product SKU)"), StringLength(100)]
    public string? VariantOfSku { get; set; }

    [Display(Name = "Option label"), StringLength(100)]
    public string? VariantLabel { get; set; }

    [Display(Name = "Fixed price (overrides pricing rules)"), Range(0, 100_000_000)]
    public decimal? PriceOverride { get; set; }

    [Display(Name = "Compare-at price (shown struck through)"), Range(0, 100_000_000)]
    public decimal? CompareAtPrice { get; set; }

    /// <summary>One per line: "Group > Name [unit]: value".</summary>
    [Display(Name = "Specifications")]
    public string? SpecsText { get; set; }
}

public sealed class ProductEditViewModel
{
    public required ProductEditInput Input { get; init; }
    public Product? Product { get; init; }
    public IReadOnlyList<SupplierOffer> Offers { get; init; } = [];
    public IReadOnlyList<PriceHistory> History { get; init; } = [];
    /// <summary>Main product and its variants, when this product belongs to a family.</summary>
    public IReadOnlyList<Product> Family { get; init; } = [];
    public Product? Main { get; init; }
    public IReadOnlyList<string> CategoryPaths { get; init; } = [];
    public IReadOnlyList<string> Brands { get; init; } = [];
}

public sealed class SupplierEditInput
{
    public int Id { get; set; }

    [Required, StringLength(200)]
    public string Name { get; set; } = "";

    [Required, StringLength(30), RegularExpression("^[A-Za-z0-9_-]+$", ErrorMessage = "Letters, digits, - and _ only.")]
    public string Code { get; set; } = "";

    public SupplierKind Kind { get; set; } = SupplierKind.Importer;

    [Required, StringLength(3, MinimumLength = 3)]
    public string Currency { get; set; } = "USD";

    [Display(Name = "Default delivery time (days)"), Range(0, 365)]
    public int DefaultLeadTimeDays { get; set; } = 2;

    [Display(Name = "Extra cost % (negative = discount)"), Range(-100, 1000)]
    public decimal ExtraCostPercent { get; set; }

    [Display(Name = "Extra cost per unit"), Range(0, 1_000_000)]
    public decimal ExtraCostFixed { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    [StringLength(2000)]
    public string? Notes { get; set; }

    [Display(Name = "CSV column mapping"), StringLength(2000)]
    public string? ImportColumnMap { get; set; }

    [Display(Name = "Price CSS selector"), StringLength(300)]
    public string? ScrapePriceSelector { get; set; }

    [Display(Name = "Stock CSS selector"), StringLength(300)]
    public string? ScrapeStockSelector { get; set; }

    [Display(Name = "In-stock text pattern"), StringLength(300)]
    public string? ScrapeInStockPattern { get; set; }

    [Display(Name = "Seconds between requests"), Range(1, 600)]
    public int ScrapeDelaySeconds { get; set; } = 5;
}

public sealed class OfferEditInput
{
    public int Id { get; set; }

    [Required, Display(Name = "Product SKU"), StringLength(100)]
    public string ProductSku { get; set; } = "";

    [Display(Name = "Supplier")]
    public int SupplierId { get; set; }

    [Display(Name = "Supplier SKU"), StringLength(100)]
    public string? SupplierSku { get; set; }

    [Range(0, 100_000_000)]
    public decimal Cost { get; set; }

    [Display(Name = "Currency (blank = supplier's)"), StringLength(3)]
    public string? Currency { get; set; }

    [Display(Name = "Stock (blank = not tracked)"), Range(0, 10_000_000)]
    public int? StockQuantity { get; set; }

    [Display(Name = "Delivery days (blank = supplier default)"), Range(0, 365)]
    public int? LeadTimeDays { get; set; }

    [Display(Name = "Valid from")]
    public DateTime? ValidFrom { get; set; }

    [Display(Name = "Valid until")]
    public DateTime? ValidTo { get; set; }

    [Display(Name = "Source URL (for scraping)"), Url, StringLength(1000)]
    public string? SourceUrl { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}

public sealed class RuleEditInput
{
    public int Id { get; set; }

    [Required, StringLength(200)]
    public string Name { get; set; } = "";

    [Range(-1000, 1000)]
    public int Priority { get; set; }

    [Display(Name = "Supplier")]
    public int? SupplierId { get; set; }

    [Display(Name = "Category")]
    public int? CategoryId { get; set; }

    [Display(Name = "Brand")]
    public int? BrandId { get; set; }

    [Display(Name = "Markup %"), Range(-100, 10_000)]
    public decimal MarkupPercent { get; set; }

    [Display(Name = "Fixed markup"), Range(-1_000_000, 1_000_000)]
    public decimal FixedMarkup { get; set; }

    [Display(Name = "Minimum profit per unit"), Range(0, 1_000_000)]
    public decimal? MinMargin { get; set; }

    public PriceRounding Rounding { get; set; } = PriceRounding.Ends99;

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}

public sealed record Option(string Value, string Label);
