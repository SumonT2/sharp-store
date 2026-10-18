using Microsoft.Extensions.Options;
using Shop.Infrastructure;
using Shop.Infrastructure.Pricing;
using Shop.Infrastructure.Scraping;

namespace Shop.Web.Infrastructure;

/// <summary>Last-run information shown on the admin Jobs page.</summary>
public class JobStatus
{
    public DateTime? LastRepriceAt { get; set; }
    public string? LastRepriceResult { get; set; }
    public DateTime? LastScrapeAt { get; set; }
    public string? LastScrapeResult { get; set; }
    public bool ScrapeRunning { get; set; }
}

/// <summary>
/// Re-prices regularly so time-limited offers start and end on schedule, and runs the scraper when enabled.
/// </summary>
public class ScheduledJobsService(IServiceScopeFactory scopes, IOptions<JobOptions> options, JobStatus status, ILogger<ScheduledJobsService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        DateTime nextReprice = DateTime.UtcNow.AddSeconds(30);
        DateTime nextScrape = DateTime.UtcNow.AddMinutes(5);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var o = options.Value;
            var now = DateTime.UtcNow;

            if (now >= nextReprice)
            {
                nextReprice = now.AddMinutes(Math.Max(1, o.RepriceIntervalMinutes));
                await RunAsync("reprice", async sp =>
                {
                    var summary = await sp.GetRequiredService<PriceUpdateService>().RepriceAllAsync("Scheduled", stoppingToken);
                    status.LastRepriceAt = DateTime.UtcNow;
                    status.LastRepriceResult = $"{summary.Checked} checked, {summary.Changed} changed";
                });
            }

            if (o.ScrapingEnabled && now >= nextScrape && !status.ScrapeRunning)
            {
                nextScrape = now.AddHours(Math.Max(1, o.ScrapeIntervalHours));
                await RunScrapeAsync(scopes, status, logger, stoppingToken);
            }
        }
    }

    public static async Task RunScrapeAsync(IServiceScopeFactory scopes, JobStatus status, ILogger logger, CancellationToken ct)
    {
        if (status.ScrapeRunning)
            return;
        status.ScrapeRunning = true;
        try
        {
            using var scope = scopes.CreateScope();
            var summary = await scope.ServiceProvider.GetRequiredService<ScrapeService>().RunAsync(null, ct);
            status.LastScrapeAt = DateTime.UtcNow;
            status.LastScrapeResult = $"{summary.Updated} updated, {summary.Failed} failed, {summary.Skipped} skipped of {summary.Attempted}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Scrape job failed");
            status.LastScrapeAt = DateTime.UtcNow;
            status.LastScrapeResult = "Failed: " + ex.Message;
        }
        finally
        {
            status.ScrapeRunning = false;
        }
    }

    private async Task RunAsync(string name, Func<IServiceProvider, Task> work)
    {
        try
        {
            using var scope = scopes.CreateScope();
            await work(scope.ServiceProvider);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Scheduled job {Job} failed", name);
        }
    }
}
