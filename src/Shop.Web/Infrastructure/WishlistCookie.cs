namespace Shop.Web.Infrastructure;

/// <summary>
/// The visitor's wishlist, kept in a cookie as "id|id|…" (main product ids, newest first). No account is needed;
/// it lasts a year in this browser.
/// </summary>
public class WishlistCookie(IHttpContextAccessor accessor)
{
    private const string Name = "shop_wishlist";
    public const int MaxItems = 100;

    private HttpContext Http => accessor.HttpContext ?? throw new InvalidOperationException("No HTTP context.");
    private List<int>? _ids;

    public IReadOnlyList<int> Read()
    {
        if (_ids is not null)
            return _ids;
        var raw = Http.Request.Cookies[Name];
        _ids = string.IsNullOrEmpty(raw)
            ? []
            : raw.Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => int.TryParse(p, out var id) ? id : 0)
                .Where(id => id > 0)
                .Distinct()
                .Take(MaxItems)
                .ToList();
        return _ids;
    }

    public bool Contains(int productId) => Read().Contains(productId);

    public int Count => Read().Count;

    /// <summary>Adds the product, or removes it when it's already saved. Returns true when it is now on the wishlist.</summary>
    public bool Toggle(int productId)
    {
        var ids = Read().ToList();
        var added = !ids.Remove(productId);
        if (added)
            ids.Insert(0, productId);
        Write(ids);
        return added;
    }

    public void Remove(IEnumerable<int> productIds)
    {
        var drop = productIds.ToHashSet();
        Write(Read().Where(id => !drop.Contains(id)).ToList());
    }

    private void Write(List<int> ids)
    {
        _ids = ids.Take(MaxItems).ToList();
        if (_ids.Count == 0)
        {
            Http.Response.Cookies.Delete(Name);
            return;
        }
        Http.Response.Cookies.Append(Name, string.Join('|', _ids), new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = Http.Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.AddYears(1),
        });
    }
}
