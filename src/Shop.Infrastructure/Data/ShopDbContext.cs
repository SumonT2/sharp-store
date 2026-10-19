using Microsoft.EntityFrameworkCore;
using Shop.Core.Entities;

namespace Shop.Infrastructure.Data;

public class ShopDbContext(DbContextOptions<ShopDbContext> options) : DbContext(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<SpecificationAttribute> SpecificationAttributes => Set<SpecificationAttribute>();
    public DbSet<ProductSpecification> ProductSpecifications => Set<ProductSpecification>();
    public DbSet<PriceHistory> PriceHistory => Set<PriceHistory>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<SupplierOffer> SupplierOffers => Set<SupplierOffer>();
    public DbSet<PricingRule> PricingRules => Set<PricingRule>();
    public DbSet<CurrencyRate> CurrencyRates => Set<CurrencyRate>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<ImportLog> ImportLogs => Set<ImportLog>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<decimal>().HavePrecision(18, 4);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Category>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Slug).HasMaxLength(200);
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasOne(x => x.Parent).WithMany(x => x.Children).HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Brand>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Slug).HasMaxLength(200);
            e.HasIndex(x => x.Slug).IsUnique();
        });

        b.Entity<Product>(e =>
        {
            e.Property(x => x.Sku).HasMaxLength(100);
            e.Property(x => x.Name).HasMaxLength(300);
            e.Property(x => x.Slug).HasMaxLength(200);
            e.Property(x => x.ImageUrl).HasMaxLength(1000);
            e.Property(x => x.GalleryUrls).HasMaxLength(10_000);
            e.Property(x => x.OptionImageUrl).HasMaxLength(1000);
            e.Property(x => x.ShortDescription).HasMaxLength(1000);
            e.HasIndex(x => x.Sku).IsUnique();
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => new { x.IsPublished, x.CategoryId });
            e.HasIndex(x => x.Price);
            e.HasIndex(x => x.ListPrice);
            e.HasIndex(x => x.BrandId);
            e.HasIndex(x => x.VariantOfId);
            e.Property(x => x.VariantLabel).HasMaxLength(100);
            e.HasOne(x => x.VariantOf).WithMany(x => x.Variants).HasForeignKey(x => x.VariantOfId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Brand).WithMany().HasForeignKey(x => x.BrandId).OnDelete(DeleteBehavior.SetNull);
            e.Ignore(x => x.IsPurchasable);
        });

        b.Entity<SpecificationAttribute>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(150);
            e.Property(x => x.Slug).HasMaxLength(150);
            e.Property(x => x.Unit).HasMaxLength(30);
            e.Property(x => x.GroupName).HasMaxLength(100);
            e.HasIndex(x => x.Slug).IsUnique();
        });

        b.Entity<ProductSpecification>(e =>
        {
            e.Property(x => x.Value).HasMaxLength(300);
            e.HasIndex(x => new { x.ProductId, x.AttributeId }).IsUnique();
            e.HasIndex(x => new { x.AttributeId, x.Value });
            e.HasOne(x => x.Product).WithMany(x => x.Specifications).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Attribute).WithMany().HasForeignKey(x => x.AttributeId).OnDelete(DeleteBehavior.Cascade);
            e.Ignore(x => x.DisplayValue);
        });

        b.Entity<PriceHistory>(e =>
        {
            e.HasIndex(x => new { x.ProductId, x.ChangedAt });
            e.Property(x => x.Reason).HasMaxLength(200);
        });

        b.Entity<Supplier>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Code).HasMaxLength(30);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.HasIndex(x => x.Code).IsUnique();
        });

        b.Entity<SupplierOffer>(e =>
        {
            e.Property(x => x.SupplierSku).HasMaxLength(100);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.SourceUrl).HasMaxLength(1000);
            e.Property(x => x.LastScrapeError).HasMaxLength(500);
            e.HasIndex(x => new { x.SupplierId, x.ProductId }).IsUnique();
            e.HasIndex(x => new { x.SupplierId, x.SupplierSku });
            e.HasOne(x => x.Supplier).WithMany(x => x.Offers).HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Product).WithMany(x => x.Offers).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.Ignore(x => x.EffectiveLeadTimeDays);
            e.Ignore(x => x.EffectiveCurrency);
        });

        b.Entity<PricingRule>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Brand).WithMany().HasForeignKey(x => x.BrandId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CurrencyRate>(e =>
        {
            e.HasKey(x => x.Code);
            e.Property(x => x.Code).HasMaxLength(3);
            e.Property(x => x.RateToBase).HasPrecision(18, 8);
        });

        b.Entity<Order>(e =>
        {
            e.Property(x => x.Number).HasMaxLength(30);
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => x.CreatedAt);
            e.Property(x => x.CustomerName).HasMaxLength(200);
            e.Property(x => x.Phone).HasMaxLength(50);
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Address).HasMaxLength(500);
            e.Property(x => x.City).HasMaxLength(100);
            e.Property(x => x.DeliveryZone).HasMaxLength(100);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.Property(x => x.PaymentMethod).HasMaxLength(50);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<OrderItem>(e =>
        {
            e.Property(x => x.Sku).HasMaxLength(100);
            e.Property(x => x.Name).HasMaxLength(300);
        });

        b.Entity<AdminUser>(e =>
        {
            e.Property(x => x.Email).HasMaxLength(200);
            e.HasIndex(x => x.Email).IsUnique();
        });

        b.Entity<ImportLog>(e =>
        {
            e.Property(x => x.FileName).HasMaxLength(300);
            e.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.SetNull);
        });
    }
}
