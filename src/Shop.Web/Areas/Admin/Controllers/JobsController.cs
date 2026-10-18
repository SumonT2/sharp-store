using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shop.Infrastructure;
using Shop.Infrastructure.Pricing;
using Shop.Web.Infrastructure;

namespace Shop.Web.Areas.Admin.Controllers;

public class JobsController(
    PriceUpdateService pricing,
    JobStatus status,
    IServiceScopeFactory scopes,
    IHostApplicationLifetime lifetime,
    IOptions<JobOptions> options,
    ILogger<JobsController> logger) : AdminController
{
    public IActionResult Index()
    {
        ViewBag.Options = options.Value;
        return View(status);
    }

    [HttpPost]
    public async Task<IActionResult> Reprice(CancellationToken ct)
    {
        var summary = await pricing.RepriceAllAsync("Manual reprice", ct);
        status.LastRepriceAt = DateTime.UtcNow;
        status.LastRepriceResult = $"{summary.Checked} checked, {summary.Changed} changed";
        Flash($"Repriced {summary.Checked} products; {summary.Changed} changed." +
              (summary.Warnings.Count > 0 ? " Warnings: " + string.Join(" ", summary.Warnings) : ""),
            error: summary.Warnings.Count > 0);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public IActionResult Scrape()
    {
        if (status.ScrapeRunning)
        {
            Flash("A scrape is already running.", error: true);
            return RedirectToAction(nameof(Index));
        }

        // Scraping is slow by design (polite delays), so run it in the background.
        _ = Task.Run(() => ScheduledJobsService.RunScrapeAsync(scopes, status, logger, lifetime.ApplicationStopping));
        Flash("Scrape started. Refresh this page to see the result.");
        return RedirectToAction(nameof(Index));
    }
}
