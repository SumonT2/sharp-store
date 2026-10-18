using Shop.Infrastructure.Orders;

namespace Shop.Web.Infrastructure;

/// <summary>
/// Stores the cart in a cookie as "productId:qty|productId:qty". Only ids and quantities are kept;
/// prices are always read fresh from the database.
/// </summary>
public class CartCookie(IHttpContextAccessor accessor)
{
    private const string Name = "shop_cart";
    private const int MaxLines = 50;

    private HttpContext Http => accessor.HttpContext ?? throw new InvalidOperationException("No HTTP context.");

    public List<CartLine> Read()
    {
        var raw = Http.Request.Cookies[Name];
        var lines = new List<CartLine>();
        if (string.IsNullOrEmpty(raw))
            return lines;

        foreach (var part in raw.Split('|', StringSplitOptions.RemoveEmptyEntries).Take(MaxLines))
        {
            var pieces = part.Split(':');
            if (pieces.Length == 2 && int.TryParse(pieces[0], out var id) && int.TryParse(pieces[1], out var qty) && id > 0 && qty > 0)
                lines.Add(new CartLine(id, Math.Min(qty, 999)));
        }
        return lines.GroupBy(l => l.ProductId).Select(g => new CartLine(g.Key, g.Sum(l => l.Quantity))).ToList();
    }

    public void Write(IEnumerable<CartLine> lines)
    {
        var value = string.Join('|', lines.Where(l => l.Quantity > 0).Take(MaxLines).Select(l => $"{l.ProductId}:{l.Quantity}"));
        if (value.Length == 0)
        {
            Http.Response.Cookies.Delete(Name);
            return;
        }
        Http.Response.Cookies.Append(Name, value, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = Http.Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.AddDays(30),
        });
    }

    public void Set(int productId, int quantity)
    {
        var lines = Read().Where(l => l.ProductId != productId).ToList();
        if (quantity > 0)
            lines.Add(new CartLine(productId, quantity));
        Write(lines);
    }

    public void Clear() => Write([]);

    public int Count() => Read().Sum(l => l.Quantity);
}
