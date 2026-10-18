using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shop.Infrastructure.Catalog;
using Shop.Infrastructure.Data;
using Shop.Infrastructure.Importing;
using Shop.Infrastructure.Orders;
using Shop.Infrastructure.Pricing;
using Shop.Infrastructure.Scraping;

namespace Shop.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddShopInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<ShopOptions>(config.GetSection(ShopOptions.Section));
        services.Configure<JobOptions>(config.GetSection(JobOptions.Section));
        services.Configure<ScrapeOptions>(config.GetSection(ScrapeOptions.Section));

        var connectionString = config.GetConnectionString("Shop")
                               ?? throw new InvalidOperationException("Connection string 'Shop' is missing.");
        services.AddDbContext<ShopDbContext>(o => o.UseNpgsql(connectionString));

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ShopClock>();
        services.AddScoped<CatalogWriter>();
        services.AddScoped<CatalogQueryService>();
        services.AddScoped<PriceUpdateService>();
        services.AddScoped<CatalogImportService>();
        services.AddScoped<OfferImportService>();
        services.AddScoped<OrderService>();
        services.AddScoped<ScrapeService>();
        services.AddScoped<DemoSeeder>();

        services.AddHttpClient(ScrapeService.HttpClientName, (sp, client) =>
            {
                var o = sp.GetRequiredService<IOptions<ScrapeOptions>>().Value;
                client.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(o.UserAgent);
                client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 5,
            });

        return services;
    }
}
