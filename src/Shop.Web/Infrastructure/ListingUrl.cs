using Microsoft.AspNetCore.WebUtilities;
using Shop.Infrastructure.Catalog;

namespace Shop.Web.Infrastructure;

/// <summary>
/// Reads listing filters from the query string and builds links that toggle one filter at a time.
/// Format: ?brand=a&amp;brand=b&amp;avail=in-stock&amp;min=10&amp;max=99&amp;f.ram-gb=8&amp;sort=price-asc&amp;page=2
/// </summary>
public sealed class ListingUrl
{
    public const string SpecPrefix = "f.";
    private static readonly string[] Known = ["q", "brand", "avail", "min", "max", "sort", "page"];

    private readonly string _path;
    private readonly Dictionary<string, List<string>> _values;

    private ListingUrl(string path, Dictionary<string, List<string>> values)
    {
        _path = path;
        _values = values;
    }

    public static ListingUrl FromRequest(HttpRequest request)
    {
        var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, raw) in request.Query)
        {
            if (!Known.Contains(key, StringComparer.OrdinalIgnoreCase) && !key.StartsWith(SpecPrefix, StringComparison.OrdinalIgnoreCase))
                continue;
            var list = raw.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()).Distinct().Take(50).ToList();
            if (list.Count > 0)
                values[key.ToLowerInvariant()] = list;
        }
        return new ListingUrl(request.Path, values);
    }

    public CatalogQuery ToQuery(string? categorySlug)
    {
        var query = new CatalogQuery
        {
            CategorySlug = categorySlug,
            Search = First("q"),
            Brands = All("brand").ToList(),
            Availability = All("avail").Select(CatalogQueryService.ParseAvailability).OfType<Shop.Core.Entities.Availability>().ToList(),
            MinPrice = decimal.TryParse(First("min"), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var min) && min >= 0 ? min : null,
            MaxPrice = decimal.TryParse(First("max"), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var max) && max >= 0 ? max : null,
            Sort = SortFromSlug(First("sort")),
            Page = int.TryParse(First("page"), out var page) ? page : 1,
        };
        foreach (var (key, list) in _values.Where(kv => kv.Key.StartsWith(SpecPrefix)))
            query.Specs[key[SpecPrefix.Length..]] = list;
        return query;
    }

    public string? First(string key) => _values.TryGetValue(key, out var list) ? list[0] : null;
    public IEnumerable<string> All(string key) => _values.TryGetValue(key, out var list) ? list : [];
    public bool HasFilters => _values.Keys.Any(k => k is not ("sort" or "page" or "q"));

    public bool IsSelected(string key, string value) =>
        _values.TryGetValue(key, out var list) && list.Contains(value, StringComparer.OrdinalIgnoreCase);

    /// <summary>Link with the value switched on or off; goes back to page 1.</summary>
    public string Toggle(string key, string value)
    {
        var copy = Copy();
        copy.Remove("page");
        if (copy.TryGetValue(key, out var list))
        {
            if (list.RemoveAll(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase)) == 0)
                list.Add(value);
            if (list.Count == 0)
                copy.Remove(key);
        }
        else
        {
            copy[key] = [value];
        }
        return Build(copy);
    }

    public string With(string key, string? value)
    {
        var copy = Copy();
        if (key != "page")
            copy.Remove("page");
        if (string.IsNullOrEmpty(value))
            copy.Remove(key);
        else
            copy[key] = [value];
        return Build(copy);
    }

    public string Without(params string[] keys)
    {
        var copy = Copy();
        copy.Remove("page");
        foreach (var k in keys)
            copy.Remove(k);
        return Build(copy);
    }

    public string ClearFilters()
    {
        var copy = Copy().Where(kv => kv.Key is "sort" or "q").ToDictionary(kv => kv.Key, kv => kv.Value);
        return Build(copy);
    }

    /// <summary>Hidden inputs that keep the current filters when a form (e.g. sort or price) is submitted.</summary>
    public IEnumerable<KeyValuePair<string, string>> HiddenFields(params string[] except) =>
        _values.Where(kv => !except.Contains(kv.Key) && kv.Key != "page")
            .SelectMany(kv => kv.Value.Select(v => new KeyValuePair<string, string>(kv.Key, v)));

    public static string SpecKey(string attributeSlug) => SpecPrefix + attributeSlug;

    public static CatalogSort SortFromSlug(string? slug) => slug switch
    {
        "price-asc" => CatalogSort.PriceLowHigh,
        "price-desc" => CatalogSort.PriceHighLow,
        "newest" => CatalogSort.Newest,
        "name" => CatalogSort.NameAz,
        _ => CatalogSort.Featured,
    };

    public static readonly (string Slug, string Label)[] SortOptions =
    [
        ("featured", "Featured"),
        ("price-asc", "Price: low to high"),
        ("price-desc", "Price: high to low"),
        ("newest", "Newest"),
        ("name", "Name A–Z"),
    ];

    private Dictionary<string, List<string>> Copy() =>
        _values.ToDictionary(kv => kv.Key, kv => kv.Value.ToList(), StringComparer.OrdinalIgnoreCase);

    private string Build(Dictionary<string, List<string>> values)
    {
        var pairs = values.OrderBy(kv => Array.IndexOf(Known, kv.Key) is var i && i >= 0 ? i : 100).ThenBy(kv => kv.Key)
            .SelectMany(kv => kv.Value.Select(v => new KeyValuePair<string, string?>(kv.Key, v)));
        return QueryHelpers.AddQueryString(_path, pairs);
    }
}
