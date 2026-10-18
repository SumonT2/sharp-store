using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shop.Core.Entities;
using Shop.Infrastructure.Data;
using Shop.Infrastructure.Pricing;

namespace Shop.Infrastructure.Scraping;

public class ScrapeOptions
{
    public const string Section = "Scraping";

    /// <summary>Identify yourself honestly; include a way for site owners to contact you.</summary>
    public string UserAgent { get; set; } = "ShopPriceMonitor/1.0 (+mailto:owner@example.com)";
    public int TimeoutSeconds { get; set; } = 20;
    public int MinDelaySeconds { get; set; } = 3;
    public bool RespectRobotsTxt { get; set; } = true;
}

public sealed record ScrapeSummary(int Attempted, int Updated, int Failed, int Skipped);

/// <summary>
/// Refreshes cost and stock for offers from web-source suppliers by reading the offer's source URL.
/// Requests to the same site are spaced out and robots.txt is respected.
/// Only scrape sites whose terms allow it, or where you have the owner's permission.
/// </summary>
public class ScrapeService(
    ShopDbContext db,
    IHttpClientFactory httpFactory,
    PriceUpdateService pricing,
    ShopClock clock,
    IOptions<ScrapeOptions> options,
    ILogger<ScrapeService> logger)
{
    public const string HttpClientName = "scraper";

    private readonly Dictionary<string, RobotsPolicy> _robots = new(StringComparer.OrdinalIgnoreCase);

    public async Task<ScrapeSummary> RunAsync(int? supplierId = null, CancellationToken ct = default)
    {
        var o = options.Value;
        var offers = await db.SupplierOffers
            .Include(x => x.Supplier)
            .Where(x => x.IsActive && x.SourceUrl != null && x.Supplier!.IsActive && x.Supplier.Kind == SupplierKind.WebSource)
            .Where(x => supplierId == null || x.SupplierId == supplierId)
            .OrderBy(x => x.LastScrapedAt)
            .ToListAsync(ct);

        var client = httpFactory.CreateClient(HttpClientName);
        int updated = 0, failed = 0, skipped = 0;
        var lastRequestByHost = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        var touchedProducts = new HashSet<int>();

        foreach (var offer in offers)
        {
            ct.ThrowIfCancellationRequested();
            if (!Uri.TryCreate(offer.SourceUrl, UriKind.Absolute, out var uri))
            {
                Fail(offer, "Invalid source URL.");
                failed++;
                continue;
            }

            var delay = TimeSpan.FromSeconds(Math.Max(o.MinDelaySeconds, offer.Supplier!.ScrapeDelaySeconds));
            if (lastRequestByHost.TryGetValue(uri.Host, out var last))
            {
                var wait = last + delay - DateTime.UtcNow;
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, ct);
            }

            if (o.RespectRobotsTxt && !(await RobotsForAsync(client, uri, ct)).IsAllowed(uri.PathAndQuery))
            {
                Fail(offer, "Blocked by the site's robots.txt.");
                skipped++;
                continue;
            }

            try
            {
                lastRequestByHost[uri.Host] = DateTime.UtcNow;
                using var response = await client.GetAsync(uri, ct);
                if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden)
                {
                    Fail(offer, $"The site answered {(int)response.StatusCode}; slow down or ask the owner for access.");
                    failed++;
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                {
                    Fail(offer, $"HTTP {(int)response.StatusCode}.");
                    failed++;
                    continue;
                }

                var html = await response.Content.ReadAsStringAsync(ct);
                var extracted = PriceExtractor.Extract(html, offer.Supplier.ScrapePriceSelector, offer.Supplier.ScrapeStockSelector, offer.Supplier.ScrapeInStockPattern);
                if (extracted.Error is not null || extracted.Price is null)
                {
                    Fail(offer, extracted.Error ?? "No price found.");
                    failed++;
                    continue;
                }

                offer.Cost = extracted.Price.Value;
                if (extracted.InStock is { } inStock)
                    offer.StockQuantity = inStock ? null : 0;
                offer.LastScrapedAt = clock.UtcNow;
                offer.LastScrapeError = null;
                offer.UpdatedAt = clock.UtcNow;
                touchedProducts.Add(offer.ProductId);
                updated++;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                Fail(offer, ex is TaskCanceledException ? "Timed out." : ex.Message);
                failed++;
            }

            // Save as we go so a long run doesn't lose its work if interrupted.
            await db.SaveChangesAsync(ct);
        }

        await db.SaveChangesAsync(ct);
        if (touchedProducts.Count > 0)
            await pricing.RepriceAsync(touchedProducts, "Scraped prices", ct);

        logger.LogInformation("Scrape finished: {Updated} updated, {Failed} failed, {Skipped} skipped of {Total}", updated, failed, skipped, offers.Count);
        return new ScrapeSummary(offers.Count, updated, failed, skipped);
    }

    /// <summary>Fetches one offer's page and shows what would be read, without saving anything.</summary>
    public async Task<ExtractedPrice> TestAsync(int offerId, CancellationToken ct = default)
    {
        var offer = await db.SupplierOffers.AsNoTracking().Include(x => x.Supplier).FirstOrDefaultAsync(x => x.Id == offerId, ct);
        if (offer?.Supplier is null || !Uri.TryCreate(offer.SourceUrl, UriKind.Absolute, out var uri))
            return new ExtractedPrice(null, null, "The offer has no valid source URL.");

        var client = httpFactory.CreateClient(HttpClientName);
        if (options.Value.RespectRobotsTxt && !(await RobotsForAsync(client, uri, ct)).IsAllowed(uri.PathAndQuery))
            return new ExtractedPrice(null, null, "Blocked by the site's robots.txt.");
        try
        {
            using var response = await client.GetAsync(uri, ct);
            if (!response.IsSuccessStatusCode)
                return new ExtractedPrice(null, null, $"HTTP {(int)response.StatusCode}.");
            var html = await response.Content.ReadAsStringAsync(ct);
            return PriceExtractor.Extract(html, offer.Supplier.ScrapePriceSelector, offer.Supplier.ScrapeStockSelector, offer.Supplier.ScrapeInStockPattern);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return new ExtractedPrice(null, null, ex is TaskCanceledException ? "Timed out." : ex.Message);
        }
    }

    private void Fail(SupplierOffer offer, string message)
    {
        offer.LastScrapedAt = clock.UtcNow;
        offer.LastScrapeError = message.Length > 500 ? message[..500] : message;
        logger.LogWarning("Scrape failed for offer {OfferId} ({Url}): {Message}", offer.Id, offer.SourceUrl, message);
    }

    private async Task<RobotsPolicy> RobotsForAsync(HttpClient client, Uri uri, CancellationToken ct)
    {
        var key = uri.GetLeftPart(UriPartial.Authority);
        if (_robots.TryGetValue(key, out var cached))
            return cached;

        RobotsPolicy policy;
        try
        {
            using var response = await client.GetAsync(new Uri(new Uri(key), "/robots.txt"), ct);
            policy = response.IsSuccessStatusCode
                ? RobotsPolicy.Parse(await response.Content.ReadAsStringAsync(ct), options.Value.UserAgent)
                : RobotsPolicy.AllowAll;
        }
        catch (HttpRequestException)
        {
            policy = RobotsPolicy.AllowAll;
        }
        _robots[key] = policy;
        return policy;
    }
}
