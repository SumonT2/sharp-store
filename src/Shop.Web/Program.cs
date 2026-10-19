using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting.WindowsServices;
using Shop.Infrastructure;
using Shop.Infrastructure.Data;
using Shop.Web.Infrastructure;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // A Windows service starts in System32, so the app's own folder must be the content root (views, wwwroot).
    ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : null,
});
// Settings kept outside the app folder (deploy.ps1 uses C:\Store\appsettings.Production.json), so a deployment never
// overwrites passwords or shop details.
if (Environment.GetEnvironmentVariable("SHOP_SETTINGS") is { Length: > 0 } settingsFile)
    builder.Configuration.AddJsonFile(settingsFile, optional: false, reloadOnChange: true);
// Runs as a Windows service when started by the service manager; does nothing otherwise.
builder.Services.AddWindowsService(o => o.ServiceName = "TechBhai");
const long maxUploadBytes = 20 * 1024 * 1024;

builder.Services.AddShopInfrastructure(builder.Configuration);
builder.Services.AddScoped<ShopFormatter>();
builder.Services.AddScoped<CartCookie>();
builder.Services.AddScoped<WishlistCookie>();
builder.Services.AddScoped<CategoryMenu>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<AdminAccountService>();
builder.Services.AddSingleton<JobStatus>();
builder.Services.AddHostedService<ScheduledJobsService>();
builder.Services.AddHttpContextAccessor();

builder.Services.AddControllersWithViews(o => o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/admin/login";
        o.AccessDeniedPath = "/admin/login";
        o.Cookie.Name = "shop_admin";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromHours(12);
        o.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();

// Slow down password guessing: 10 login attempts per minute per IP address.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

// Keep login cookies valid across restarts and deployments.
var keysPath = builder.Configuration["DataProtection:KeysPath"] ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys");
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysPath)).SetApplicationName("Shop");

builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = maxUploadBytes);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = maxUploadBytes);

// Behind nginx on a VPS: trust X-Forwarded-For/Proto from the local reverse proxy.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);

var app = builder.Build();

await InitializeDatabaseAsync(app);

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseStatusCodePagesWithReExecute("/error/{0}");

// Fixed culture so prices and dates parse the same way on every server.
app.UseRequestLocalization("en-US");
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapControllerRoute("admin", "admin/{controller=Dashboard}/{action=Index}/{id?}", defaults: new { area = "Admin" })
    .WithStaticAssets();
app.MapControllers();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}").WithStaticAssets();

app.Run();

static async Task InitializeDatabaseAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    var config = services.GetRequiredService<IConfiguration>();
    var logger = services.GetRequiredService<ILogger<Program>>();

    if (config.GetValue("Database:AutoMigrate", true))
        await services.GetRequiredService<ShopDbContext>().Database.MigrateAsync();

    await services.GetRequiredService<AdminAccountService>().EnsureInitialAdminAsync();

    if (config.GetValue("Seed:DemoData", false))
    {
        var seeder = services.GetRequiredService<DemoSeeder>();
        if (await seeder.SeedIfEmptyAsync())
            logger.LogInformation("Demo data added.");
        else if (await seeder.AddMissingPhotosAsync() is > 0 and var count)
            logger.LogInformation("Added demo photos to {Count} products.", count);
    }

    // Bring prices, availability and product-family summaries up to date after an upgrade or restart.
    await services.GetRequiredService<Shop.Infrastructure.Pricing.PriceUpdateService>().RepriceAllAsync("Startup");
}

public partial class Program;
