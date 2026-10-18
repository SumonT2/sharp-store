using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Shop.Core.Entities;
using Shop.Core.Text;
using Shop.Infrastructure.Data;

namespace Shop.Infrastructure.Catalog;

/// <summary>
/// Find-or-create helpers for categories, brands and specification attributes, shared by the CSV import and the admin.
/// Keeps an in-memory cache for the lifetime of one unit of work, so bulk imports don't query per row.
/// </summary>
public partial class CatalogWriter(ShopDbContext db)
{
    private Dictionary<string, Category>? _categories;
    private Dictionary<string, Brand>? _brands;
    private Dictionary<string, SpecificationAttribute>? _attributes;

    /// <summary>Resolves a path like "Phones > Android" and creates any missing level.</summary>
    public async Task<Category?> GetOrCreateCategoryPathAsync(string? path, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        _categories ??= await db.Categories.ToDictionaryAsync(c => c.Slug, ct);

        Category? parent = null;
        var slugPrefix = "";
        foreach (var rawName in path.Split('>', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = _categories.Values.FirstOrDefault(c =>
                string.Equals(c.Name, rawName, StringComparison.OrdinalIgnoreCase) && IsChildOf(c, parent));

            if (match is null)
            {
                var slug = UniqueSlug(slugPrefix + TextUtil.Slugify(rawName), _categories.ContainsKey);
                match = new Category { Name = rawName, Slug = slug, Parent = parent };
                db.Categories.Add(match);
                _categories[slug] = match;
            }

            parent = match;
            slugPrefix = match.Slug + "-";
        }
        return parent;
    }

    // Categories created in this unit of work have Id 0 and only the navigation set, so check both.
    private static bool IsChildOf(Category c, Category? parent) =>
        parent is null
            ? c.ParentId is null && c.Parent is null
            : ReferenceEquals(c.Parent, parent) || (parent.Id != 0 && c.ParentId == parent.Id);

    public async Task<Brand?> GetOrCreateBrandAsync(string? name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        _brands ??= await db.Brands.ToDictionaryAsync(b => b.Slug, ct);
        var slug = TextUtil.Slugify(name);
        if (_brands.TryGetValue(slug, out var brand))
            return brand;

        brand = new Brand { Name = name.Trim(), Slug = slug };
        db.Brands.Add(brand);
        _brands[slug] = brand;
        return brand;
    }

    /// <summary>
    /// Parses a specification header such as "Display > Screen Size [inch]" into group, name and unit,
    /// and returns the matching attribute (created if missing).
    /// </summary>
    public async Task<SpecificationAttribute> GetOrCreateAttributeAsync(string header, CancellationToken ct = default)
    {
        _attributes ??= await db.SpecificationAttributes.ToDictionaryAsync(a => a.Slug, ct);

        var (group, name, unit) = ParseAttributeHeader(header);
        var slug = TextUtil.Slugify(name);
        if (_attributes.TryGetValue(slug, out var attr))
        {
            // Fill in details the first import did not have, but never overwrite admin edits.
            attr.GroupName ??= group;
            attr.Unit ??= unit;
            return attr;
        }

        attr = new SpecificationAttribute
        {
            Name = name,
            Slug = slug,
            GroupName = group,
            Unit = unit,
            IsFilterable = true,
            DisplayOrder = _attributes.Count,
        };
        db.SpecificationAttributes.Add(attr);
        _attributes[slug] = attr;
        return attr;
    }

    /// <summary>Sets a product's value for an attribute. An empty value removes it.</summary>
    public static void SetSpecification(Product product, SpecificationAttribute attribute, string? value)
    {
        var existing = product.Specifications.FirstOrDefault(s =>
            (attribute.Id != 0 && s.AttributeId == attribute.Id) || ReferenceEquals(s.Attribute, attribute));

        value = value?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            if (existing is not null)
                product.Specifications.Remove(existing);
            return;
        }

        if (value.Length > 300)
            value = value[..300];

        if (existing is null)
        {
            existing = new ProductSpecification { Attribute = attribute };
            product.Specifications.Add(existing);
        }
        existing.Value = value;
        existing.NumericValue = NumericOnly().IsMatch(value) ? TextUtil.ParseDecimal(value) : null;
    }

    public static (string? Group, string Name, string? Unit) ParseAttributeHeader(string header)
    {
        var text = header.Trim();
        string? unit = null;
        var unitMatch = UnitSuffix().Match(text);
        if (unitMatch.Success)
        {
            unit = unitMatch.Groups[1].Value.Trim();
            text = text[..unitMatch.Index].Trim();
        }

        string? group = null;
        var sep = text.LastIndexOf('>');
        if (sep >= 0)
        {
            group = text[..sep].Trim();
            text = text[(sep + 1)..].Trim();
        }

        return (string.IsNullOrEmpty(group) ? null : group, text, string.IsNullOrEmpty(unit) ? null : unit);
    }

    public async Task<string> UniqueProductSlugAsync(string name, int? excludeProductId, CancellationToken ct = default)
    {
        var baseSlug = TextUtil.Slugify(name);
        var taken = await db.Products
            .Where(p => (p.Slug == baseSlug || p.Slug.StartsWith(baseSlug + "-")) && p.Id != excludeProductId)
            .Select(p => p.Slug)
            .ToListAsync(ct);
        // Include slugs of products added but not yet saved in this unit of work.
        taken.AddRange(db.ChangeTracker.Entries<Product>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity.Slug));
        var set = taken.ToHashSet();
        return UniqueSlug(baseSlug, set.Contains);
    }

    private static string UniqueSlug(string baseSlug, Func<string, bool> exists)
    {
        if (!exists(baseSlug))
            return baseSlug;
        for (var i = 2; ; i++)
        {
            var candidate = $"{baseSlug}-{i}";
            if (!exists(candidate))
                return candidate;
        }
    }

    [GeneratedRegex(@"\[([^\]]*)\]\s*$")]
    private static partial Regex UnitSuffix();

    [GeneratedRegex(@"^-?\d+([.,]\d+)?$")]
    private static partial Regex NumericOnly();
}
