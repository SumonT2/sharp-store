using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Shop.Infrastructure.Data;
using Shop.Web.Models;

namespace Shop.Web.Infrastructure;

/// <summary>Category tree for the navigation, cached briefly because it is on every page.</summary>
public class CategoryMenu(ShopDbContext db, IMemoryCache cache)
{
    private const string CacheKey = "category-menu";

    public async Task<IReadOnlyList<CategoryLink>> GetAsync()
    {
        if (cache.TryGetValue(CacheKey, out IReadOnlyList<CategoryLink>? cached) && cached is not null)
            return cached;

        var all = await db.Categories.AsNoTracking().OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name).ToListAsync();
        IReadOnlyList<CategoryLink> Build(int? parentId, int depth) =>
            depth > 4 ? [] : all.Where(c => c.ParentId == parentId).Select(c => new CategoryLink(c.Id, c.Name, c.Slug, Build(c.Id, depth + 1))).ToList();

        var tree = Build(null, 0);
        cache.Set(CacheKey, tree, TimeSpan.FromMinutes(2));
        return tree;
    }

    public void Invalidate() => cache.Remove(CacheKey);
}
