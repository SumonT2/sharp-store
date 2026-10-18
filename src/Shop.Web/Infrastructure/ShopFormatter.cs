using System.Globalization;
using Microsoft.Extensions.Options;
using Shop.Core.Entities;
using Shop.Infrastructure;

namespace Shop.Web.Infrastructure;

/// <summary>Formatting helpers for views: money in the shop currency and dates in the shop time zone.</summary>
public class ShopFormatter(IOptions<ShopOptions> options, ShopClock clock)
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    private readonly CultureInfo _numberCulture = ResolveCulture(options.Value.NumberCulture);

    public ShopOptions Shop => options.Value;
    public ShopClock Clock => clock;

    public string Money(decimal? amount)
    {
        if (amount is not { } a)
            return "—";
        var decimals = Math.Clamp(options.Value.PriceDecimals, 0, 4);
        return $"{options.Value.CurrencySymbol}{options.Value.RoundPrice(a).ToString("N" + decimals, _numberCulture)}";
    }

    private static CultureInfo ResolveCulture(string? name)
    {
        try
        {
            return string.IsNullOrWhiteSpace(name) ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(name);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }

    public string DateTime(DateTime? utc) =>
        utc is { } d ? clock.ToLocal(d).ToString("d MMM yyyy, HH:mm", Culture) : "—";

    public string Date(DateTime? utc) =>
        utc is { } d ? clock.ToLocal(d).ToString("d MMM yyyy", Culture) : "—";

    public string Date(DateOnly? date) => date?.ToString("ddd, d MMM", Culture) ?? "—";

    public DateOnly DeliveryBy(int leadDays) => DateOnly.FromDateTime(clock.LocalNow).AddDays(Math.Max(1, leadDays));

    /// <summary>ISO timestamp for the countdown script.</summary>
    public static string Iso(DateTime utc) => System.DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("o", Culture);

    public string AvailabilityText(Availability availability, int? leadDays) => availability switch
    {
        Availability.InStock => "In stock",
        Availability.PreOrder => leadDays is { } d ? $"Pre-order · ships in ~{d} days" : "Pre-order",
        _ => "Out of stock",
    };

    public static string AvailabilityClass(Availability availability) => availability switch
    {
        Availability.InStock => "badge-ok",
        Availability.PreOrder => "badge-pre",
        _ => "badge-out",
    };

    public static int? DiscountPercent(decimal? price, decimal? compareAt) =>
        price is > 0 && compareAt is { } c && c > price ? (int)Math.Round((c - price.Value) / c * 100) : null;
}
