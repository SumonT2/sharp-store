using Microsoft.Extensions.Options;

namespace Shop.Infrastructure;

/// <summary>Converts between UTC (stored) and the shop's local time zone (CSV input and display).</summary>
public class ShopClock(TimeProvider time, IOptions<ShopOptions> options)
{
    private readonly TimeZoneInfo _zone = ResolveZone(options.Value.TimeZone);

    public DateTime UtcNow => time.GetUtcNow().UtcDateTime;
    public DateTime LocalNow => ToLocal(UtcNow);
    public TimeZoneInfo Zone => _zone;

    public DateTime ToLocal(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _zone);

    public DateTime ToUtc(DateTime local) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), _zone);

    private static TimeZoneInfo ResolveZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
