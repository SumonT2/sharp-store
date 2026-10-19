using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shop.Infrastructure;
using Shop.Infrastructure.Data;
using Shop.Infrastructure.Orders;
using Shop.Web.Infrastructure;
using Shop.Web.Models;

namespace Shop.Web.Controllers;

public class CartController(CartCookie cart, OrderService orders, ShopDbContext db, IOptions<ShopOptions> options) : Controller
{
    [HttpGet("/cart")]
    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(await orders.BuildCartAsync(cart.Read(), ct));

    [HttpPost("/cart/add")]
    public async Task<IActionResult> Add(int productId, int quantity = 1, bool buyNow = false, CancellationToken ct = default)
    {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId && p.IsPublished, ct);
        if (product is null || !product.IsPurchasable)
        {
            TempData["Flash"] = "Sorry, that product can't be ordered right now.";
            return Redirect(product is null ? "/" : $"/p/{product.Slug}");
        }

        var existing = cart.Read().FirstOrDefault(l => l.ProductId == productId)?.Quantity ?? 0;
        var max = OrderService.MaxQuantity(product, options.Value);
        var newQuantity = Math.Clamp(existing + Math.Max(1, quantity), 1, Math.Max(1, max));
        cart.Set(productId, newQuantity);
        if (buyNow)
            return Redirect("/checkout");

        TempData["Flash"] = newQuantity < existing + quantity
            ? $"Added — you can order at most {max} of this item."
            : $"Added “{product.Name}” to your cart.";
        return Redirect("/cart");
    }

    [HttpPost("/cart/update")]
    public IActionResult Update(int productId, int quantity)
    {
        cart.Set(productId, Math.Clamp(quantity, 0, options.Value.MaxQuantityPerItem));
        return Redirect("/cart");
    }

    [HttpPost("/cart/remove")]
    public IActionResult Remove(int productId)
    {
        cart.Set(productId, 0);
        return Redirect("/cart");
    }

    [HttpGet("/checkout")]
    public async Task<IActionResult> Checkout(CancellationToken ct)
    {
        var view = await orders.BuildCartAsync(cart.Read(), ct);
        if (view.IsEmpty)
            return Redirect("/cart");
        return View(new CheckoutViewModel { Cart = view });
    }

    [HttpPost("/checkout")]
    public async Task<IActionResult> Checkout([Bind(Prefix = "Input")] CheckoutInput input, CancellationToken ct)
    {
        var lines = cart.Read();
        var view = await orders.BuildCartAsync(lines, ct);
        if (view.IsEmpty)
            return Redirect("/cart");
        if (view.HasProblems)
            return View(new CheckoutViewModel { Cart = view, Input = input, Error = "Some items in your cart changed. Please review them first." });
        if (!string.IsNullOrWhiteSpace(input.Phone) && OrderService.NormalizePhone(input.Phone, options.Value) is null)
            ModelState.AddModelError("Input.Phone", $"Enter a valid phone number ({options.Value.PhoneHint}).");
        if (!string.IsNullOrWhiteSpace(input.DeliveryZone) && OrderService.FindZone(input.DeliveryZone, options.Value) is null)
            ModelState.AddModelError("Input.DeliveryZone", "Choose a delivery area.");
        if (!ModelState.IsValid)
            return View(new CheckoutViewModel { Cart = view, Input = input });

        try
        {
            var order = await orders.PlaceOrderAsync(input, lines, ct);
            cart.Clear();
            TempData["LastOrder"] = order.Number;
            return Redirect($"/order/{order.Number}");
        }
        catch (OrderException ex)
        {
            var refreshed = await orders.BuildCartAsync(lines, ct);
            return View(new CheckoutViewModel { Cart = refreshed, Input = input, Error = ex.Message });
        }
    }

    [HttpGet("/order/{number}")]
    public async Task<IActionResult> Confirmation(string number, CancellationToken ct)
    {
        // Only shown right after checkout, so order details are not exposed to anyone guessing numbers.
        if (TempData.Peek("LastOrder") as string != number)
            return Redirect("/");
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).FirstOrDefaultAsync(o => o.Number == number, ct);
        if (order is null)
            return NotFound();
        return View(order);
    }
}
