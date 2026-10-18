using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shop.Core.Entities;
using Shop.Core.Pricing;
using Shop.Infrastructure.Data;
using Shop.Infrastructure.Pricing;

namespace Shop.Infrastructure.Orders;

public sealed record CartLine(int ProductId, int Quantity);

public sealed record CartItemView(
    int ProductId,
    string Name,
    string Slug,
    string Sku,
    string? ImageUrl,
    decimal UnitPrice,
    int Quantity,
    int MaxQuantity,
    Availability Availability,
    int? LeadTimeDays,
    DateTime? OfferEndsAt,
    string? Problem)
{
    public decimal LineTotal => UnitPrice * Quantity;
}

public sealed record CartView(IReadOnlyList<CartItemView> Items, decimal Subtotal, decimal ShippingFee, decimal Total)
{
    public bool IsEmpty => Items.Count == 0;
    public bool HasProblems => Items.Any(i => i.Problem is not null);
    public int Count => Items.Sum(i => i.Quantity);
}

public class CheckoutInput
{
    [Required, StringLength(200)]
    public string CustomerName { get; set; } = "";

    [Required, Phone, StringLength(50)]
    public string Phone { get; set; } = "";

    [EmailAddress, StringLength(200)]
    public string? Email { get; set; }

    [Required, StringLength(500)]
    public string Address { get; set; } = "";

    [Required, StringLength(100)]
    public string City { get; set; } = "";

    [Required(ErrorMessage = "Choose a delivery area."), StringLength(100)]
    public string DeliveryZone { get; set; } = "";

    [StringLength(1000)]
    public string? Notes { get; set; }
}

public sealed class OrderException(string message) : Exception(message);

public class OrderService(ShopDbContext db, PriceUpdateService pricing, ShopClock clock, IOptions<ShopOptions> options)
{
    public async Task<CartView> BuildCartAsync(IReadOnlyList<CartLine> lines, CancellationToken ct = default)
    {
        var o = options.Value;
        var ids = lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        var items = new List<CartItemView>();
        foreach (var line in lines)
        {
            if (!products.TryGetValue(line.ProductId, out var p) || !p.IsPublished)
                continue;

            var max = MaxQuantity(p, o);
            string? problem = null;
            if (!p.IsPurchasable)
                problem = "No longer available.";
            else if (line.Quantity > max)
                problem = $"Only {max} available.";

            items.Add(new CartItemView(p.Id, p.Name, p.Slug, p.Sku, p.ImageUrl, p.Price ?? 0, line.Quantity, max,
                p.Availability, p.LeadTimeDays, p.OfferEndsAt, problem));
        }

        var subtotal = items.Where(i => i.Problem is null).Sum(i => i.LineTotal);
        // Before an area is chosen, show the cheapest delivery fee.
        var shipping = ShippingFor(subtotal, o.EffectiveDeliveryZones.Min(z => z.Fee), o);
        return new CartView(items, subtotal, shipping, subtotal + shipping);
    }

    public static int MaxQuantity(Product p, ShopOptions o) =>
        p.StockQuantity is { } stock ? Math.Clamp(stock, 0, o.MaxQuantityPerItem) : o.MaxQuantityPerItem;

    public static decimal ShippingFor(decimal subtotal, decimal zoneFee, ShopOptions o) =>
        subtotal <= 0 || (o.FreeShippingOver is { } free && subtotal >= free) ? 0 : zoneFee;

    public static DeliveryZone? FindZone(string? name, ShopOptions o) =>
        o.EffectiveDeliveryZones.FirstOrDefault(z => string.Equals(z.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Removes spaces, dashes and brackets, then checks the configured pattern.</summary>
    public static string? NormalizePhone(string phone, ShopOptions o)
    {
        var digits = new string(phone.Where(c => char.IsDigit(c) || c == '+').ToArray());
        if (string.IsNullOrEmpty(o.PhonePattern))
            return digits.Length >= 6 ? digits : null;
        return System.Text.RegularExpressions.Regex.IsMatch(digits, o.PhonePattern) ? digits : null;
    }

    /// <summary>
    /// Places the order at today's prices. The price and offer are re-checked here, so an offer window
    /// that closed while the customer was checking out is not honoured.
    /// </summary>
    public async Task<Order> PlaceOrderAsync(CheckoutInput input, IReadOnlyList<CartLine> lines, CancellationToken ct = default)
    {
        if (lines.Count == 0)
            throw new OrderException("Your cart is empty.");

        var o = options.Value;
        var zone = FindZone(input.DeliveryZone, o) ?? throw new OrderException("Choose a delivery area.");
        var phone = NormalizePhone(input.Phone, o) ?? throw new OrderException("Enter a valid phone number.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var ids = lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products
            .Include(p => p.Offers).ThenInclude(x => x.Supplier)
            .Where(p => ids.Contains(p.Id))
            .AsSplitQuery()
            .ToDictionaryAsync(p => p.Id, ct);

        var ctx = await pricing.BuildContextAsync(ct);
        var now = clock.UtcNow;
        var order = new Order
        {
            Number = await NewOrderNumberAsync(ct),
            Status = OrderStatus.Pending,
            CustomerName = input.CustomerName.Trim(),
            Phone = phone,
            Email = string.IsNullOrWhiteSpace(input.Email) ? null : input.Email.Trim(),
            Address = input.Address.Trim(),
            City = input.City.Trim(),
            DeliveryZone = zone.Name,
            Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim(),
            PaymentMethod = "CashOnDelivery",
            CreatedAt = now,
        };

        foreach (var line in lines.GroupBy(l => l.ProductId).Select(g => new CartLine(g.Key, g.Sum(x => x.Quantity))))
        {
            if (!products.TryGetValue(line.ProductId, out var product) || !product.IsPublished)
                throw new OrderException("A product in your cart is no longer available.");

            var result = PricingEngine.Evaluate(product, product.Offers, ctx);
            if (result.Best is null || result.Price is not > 0)
                throw new OrderException($"\"{product.Name}\" is no longer available.");

            var unitPrice = o.RoundPrice(result.Price.Value);
            if (product.Price is { } shown && shown != unitPrice)
                throw new OrderException($"The price of \"{product.Name}\" has just changed. Please review your cart.");

            var offer = result.Best.Offer;
            if (line.Quantity < 1 || line.Quantity > o.MaxQuantityPerItem)
                throw new OrderException($"Invalid quantity for \"{product.Name}\".");
            if (offer.StockQuantity is { } stock)
            {
                if (stock < line.Quantity)
                    throw new OrderException($"Only {stock} of \"{product.Name}\" left.");
                offer.StockQuantity = stock - line.Quantity;
            }

            var leadDays = offer.EffectiveLeadTimeDays;
            order.Items.Add(new OrderItem
            {
                ProductId = product.Id,
                Sku = product.Sku,
                Name = product.Name,
                UnitPrice = unitPrice,
                Quantity = line.Quantity,
                LineTotal = unitPrice * line.Quantity,
                Availability = result.Availability,
                SupplierOfferId = offer.Id,
                SupplierId = offer.SupplierId,
                UnitCost = Math.Round(result.Best.LandedCost, 2),
                EstimatedDeliveryDate = DateOnly.FromDateTime(clock.ToLocal(now)).AddDays(Math.Max(1, leadDays)),
            });
        }

        order.Subtotal = order.Items.Sum(i => i.LineTotal);
        order.ShippingFee = ShippingFor(order.Subtotal, zone.Fee, o);
        order.Total = order.Subtotal + order.ShippingFee;

        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        // Stock went down, which can change availability or switch to another supplier's offer.
        await pricing.RepriceAsync(ids, $"Order {order.Number}", ct);
        return order;
    }

    private async Task<string> NewOrderNumberAsync(CancellationToken ct)
    {
        var prefix = clock.LocalNow.ToString("yyMMdd");
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var number = $"{prefix}-{RandomNumberGenerator.GetInt32(1000, 10000)}";
            if (!await db.Orders.AnyAsync(x => x.Number == number, ct))
                return number;
        }
        return $"{prefix}-{RandomNumberGenerator.GetInt32(100000, 1000000)}";
    }
}
