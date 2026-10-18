using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shop.Core.Entities;
using Shop.Infrastructure;
using Shop.Infrastructure.Data;
using Shop.Web.Areas.Admin.Models;

namespace Shop.Web.Areas.Admin.Controllers;

public class DashboardController(ShopDbContext db, ShopClock clock) : AdminController
{
    [HttpGet("/admin")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var counts = await db.Products
            .GroupBy(p => new { p.IsPublished, p.Availability })
            .Select(g => new { g.Key.IsPublished, g.Key.Availability, Count = g.Count() })
            .ToListAsync(ct);
        int Count(Availability a) => counts.Where(c => c.IsPublished && c.Availability == a).Sum(c => c.Count);

        var since = now.AddDays(-30);
        var model = new DashboardViewModel
        {
            Products = counts.Sum(c => c.Count),
            Published = counts.Where(c => c.IsPublished).Sum(c => c.Count),
            Drafts = counts.Where(c => !c.IsPublished).Sum(c => c.Count),
            InStock = Count(Availability.InStock),
            PreOrder = Count(Availability.PreOrder),
            OutOfStock = Count(Availability.OutOfStock),
            PendingOrders = await db.Orders.CountAsync(o => o.Status == OrderStatus.Pending, ct),
            SalesLast30Days = await db.Orders.Where(o => o.CreatedAt >= since && o.Status != OrderStatus.Cancelled).SumAsync(o => (decimal?)o.Total, ct) ?? 0,
            RecentOrders = await db.Orders.AsNoTracking().OrderByDescending(o => o.CreatedAt).Take(8).ToListAsync(ct),
            RecentImports = await db.ImportLogs.AsNoTracking().Include(l => l.Supplier).OrderByDescending(l => l.StartedAt).Take(5).ToListAsync(ct),
            ExpiringOffers = await db.SupplierOffers.AsNoTracking()
                .Include(o => o.Product).Include(o => o.Supplier)
                .Where(o => o.IsActive && o.ValidTo != null && o.ValidTo > now && o.ValidTo < now.AddDays(3))
                .OrderBy(o => o.ValidTo).Take(10).ToListAsync(ct),
            ScrapeErrors = await db.SupplierOffers.AsNoTracking()
                .Include(o => o.Product).Include(o => o.Supplier)
                .Where(o => o.LastScrapeError != null)
                .OrderByDescending(o => o.LastScrapedAt).Take(10).ToListAsync(ct),
        };
        return View(model);
    }
}
