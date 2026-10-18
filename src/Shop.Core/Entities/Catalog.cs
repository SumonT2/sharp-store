namespace Shop.Core.Entities;

public enum Availability
{
    OutOfStock = 0,
    InStock = 1,
    PreOrder = 2,
}

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? Description { get; set; }
    public int? ParentId { get; set; }
    public Category? Parent { get; set; }
    public List<Category> Children { get; set; } = new();
    public int DisplayOrder { get; set; }
}

public class Brand
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
}

public class Product
{
    public int Id { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? ShortDescription { get; set; }
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsPublished { get; set; }
    /// <summary>Shown in the "Featured" section of the home page and first in listings.</summary>
    public bool IsFeatured { get; set; }

    public int? CategoryId { get; set; }
    public Category? Category { get; set; }
    public int? BrandId { get; set; }
    public Brand? Brand { get; set; }

    /// <summary>When set, the pricing engine uses this instead of the calculated price.</summary>
    public decimal? PriceOverride { get; set; }
    /// <summary>Optional "was" price shown struck through.</summary>
    public decimal? CompareAtPrice { get; set; }

    // The fields below are maintained by the pricing engine from the supplier offers.
    // They are stored on the product so listing, filtering and sorting stay fast.
    public decimal? Price { get; set; }
    public Availability Availability { get; set; }
    /// <summary>Units available from the active offer; null when the supplier does not track stock.</summary>
    public int? StockQuantity { get; set; }
    public int? LeadTimeDays { get; set; }
    public DateTime? OfferEndsAt { get; set; }
    public int? ActiveOfferId { get; set; }
    public DateTime? PricedAt { get; set; }

    // Variants: options of the same product (switch type, colour…). Each variant is a full product with its own
    // SKU, offers, stock and price; the main product carries the family summary used by listings.
    /// <summary>The main product this is an option of; null for main and standalone products.</summary>
    public int? VariantOfId { get; set; }
    public Product? VariantOf { get; set; }
    public List<Product> Variants { get; set; } = new();
    /// <summary>Short option name shown in the selector, e.g. "Brown (Tactile)".</summary>
    public string? VariantLabel { get; set; }

    // Family summary on main products (a product without variants summarises itself). Maintained by the pricing engine.
    public decimal? ListPrice { get; set; }
    public decimal? ListPriceMax { get; set; }
    public Availability ListAvailability { get; set; }
    public int? ListLeadTimeDays { get; set; }
    public DateTime? ListOfferEndsAt { get; set; }
    /// <summary>How many options can be bought right now.</summary>
    public int ListOptionCount { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<ProductSpecification> Specifications { get; set; } = new();
    public List<SupplierOffer> Offers { get; set; } = new();

    public bool IsPurchasable => IsPublished && Price is > 0 && Availability != Availability.OutOfStock;
}

public class SpecificationAttribute
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? Unit { get; set; }
    public string? GroupName { get; set; }
    public bool IsFilterable { get; set; } = true;
    public bool ShowOnCard { get; set; }
    public int DisplayOrder { get; set; }
}

public class ProductSpecification
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public int AttributeId { get; set; }
    public SpecificationAttribute? Attribute { get; set; }
    public string Value { get; set; } = "";
    public decimal? NumericValue { get; set; }

    public string DisplayValue => Attribute?.Unit is { Length: > 0 } unit ? $"{Value} {unit}" : Value;
}

public class PriceHistory
{
    public long Id { get; set; }
    public int ProductId { get; set; }
    public decimal? OldPrice { get; set; }
    public decimal? NewPrice { get; set; }
    public string? Reason { get; set; }
    public DateTime ChangedAt { get; set; }
}
