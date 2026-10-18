using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shop.Core.Entities;
using Shop.Infrastructure.Data;
using Shop.Infrastructure.Pricing;

namespace Shop.Web.Areas.Admin.Controllers;

public class OrdersController(ShopDbContext db, PriceUpdateService pricing) : AdminController
{
    private const int PageSize = 50;

    public async Task<IActionResult> Index(OrderStatus? status, string? q, int page = 1, CancellationToken ct = default)
    {
        var query = db.Orders.AsNoTracking().AsQueryable();
        if (status is { } s)
            query = query.Where(o => o.Status == s);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(o => o.Number.Contains(term) || o.Phone.Contains(term) || o.CustomerName.ToLower().Contains(term));
        }

        var total = await query.CountAsync(ct);
        page = Math.Max(1, page);
        ViewBag.Status = status;
        ViewBag.Q = q;
        ViewBag.Page = page;
        ViewBag.PageCount = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        return View(await query.OrderByDescending(o => o.CreatedAt).Skip((page - 1) * PageSize).Take(PageSize).ToListAsync(ct));
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
            return NotFound();
        var supplierIds = order.Items.Select(i => i.SupplierId).OfType<int>().Distinct().ToList();
        ViewBag.Suppliers = await db.Suppliers.AsNoTracking().Where(s => supplierIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        return View(order);
    }

    [HttpPost]
    public async Task<IActionResult> SetStatus(int id, OrderStatus status, CancellationToken ct)
    {
        var order = await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
            return NotFound();

        var wasCancelled = order.Status == OrderStatus.Cancelled;
        order.Status = status;

        // Give tracked stock back to the supplier offer when an order is cancelled.
        if (status == OrderStatus.Cancelled && !wasCancelled)
        {
            var offerIds = order.Items.Select(i => i.SupplierOfferId).OfType<int>().ToList();
            var offers = await db.SupplierOffers.Where(o => offerIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, ct);
            foreach (var item in order.Items)
            {
                if (item.SupplierOfferId is { } oid && offers.TryGetValue(oid, out var offer) && offer.StockQuantity is { } stock)
                    offer.StockQuantity = stock + item.Quantity;
            }
        }

        await db.SaveChangesAsync(ct);
        if (status == OrderStatus.Cancelled && !wasCancelled)
            await pricing.RepriceAsync(order.Items.Select(i => i.ProductId).ToList(), $"Order {order.Number} cancelled", ct);

        Flash($"Order {order.Number} is now {status}.");
        return RedirectToAction(nameof(Details), new { id });
    }
}
