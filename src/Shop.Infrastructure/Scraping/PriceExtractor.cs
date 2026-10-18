using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Shop.Core.Text;

namespace Shop.Infrastructure.Scraping;

public sealed record ExtractedPrice(decimal? Price, bool? InStock, string? Error);

/// <summary>
/// Reads only the price and stock status from a product page. Descriptions, images and other content are
/// deliberately ignored: they are usually copyrighted, while a price is a plain fact.
/// </summary>
public static class PriceExtractor
{
    private static readonly HtmlParser Parser = new();

    public static ExtractedPrice Extract(string html, string? priceSelector, string? stockSelector, string? inStockPattern)
    {
        var doc = Parser.ParseDocument(html);
        var structured = FromStructuredData(doc);

        decimal? price;
        if (!string.IsNullOrWhiteSpace(priceSelector))
        {
            IElement? element;
            try
            {
                element = doc.QuerySelector(priceSelector);
            }
            catch (Exception)
            {
                return new ExtractedPrice(null, null, $"Invalid CSS selector \"{priceSelector}\".");
            }
            if (element is null)
                return new ExtractedPrice(null, null, $"Price element \"{priceSelector}\" not found on the page.");
            price = TextUtil.ParseDecimal(element.GetAttribute("content") ?? element.GetAttribute("data-price") ?? element.TextContent);
            if (price is null)
                return new ExtractedPrice(null, null, $"Could not read a number from \"{Shorten(element.TextContent)}\".");
        }
        else
        {
            price = structured.Price;
            if (price is null)
                return new ExtractedPrice(null, null, "No price found. Set a price CSS selector on the supplier.");
        }

        var inStock = structured.InStock;
        if (!string.IsNullOrWhiteSpace(stockSelector))
        {
            IElement? stockElement;
            try
            {
                stockElement = doc.QuerySelector(stockSelector);
            }
            catch (Exception)
            {
                return new ExtractedPrice(price, null, $"Invalid CSS selector \"{stockSelector}\".");
            }

            if (stockElement is null)
            {
                inStock = false;
            }
            else if (!string.IsNullOrWhiteSpace(inStockPattern))
            {
                try
                {
                    inStock = Regex.IsMatch(stockElement.TextContent, inStockPattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
                }
                catch (ArgumentException)
                {
                    return new ExtractedPrice(price, null, $"Invalid stock pattern \"{inStockPattern}\".");
                }
            }
            else
            {
                inStock = true;
            }
        }

        return new ExtractedPrice(price, inStock, null);
    }

    /// <summary>Reads schema.org Product/Offer data (JSON-LD or microdata) that most shops publish for search engines.</summary>
    private static (decimal? Price, bool? InStock) FromStructuredData(IDocument doc)
    {
        foreach (var script in doc.QuerySelectorAll("script[type='application/ld+json']"))
        {
            try
            {
                using var json = JsonDocument.Parse(script.TextContent);
                var found = FindOffer(json.RootElement, depth: 0);
                if (found.Price is not null)
                    return found;
            }
            catch (JsonException)
            {
                // Broken JSON-LD is common; try the next block.
            }
        }

        var meta = doc.QuerySelector("[itemprop='price'], meta[property='product:price:amount'], meta[property='og:price:amount']");
        var metaPrice = TextUtil.ParseDecimal(meta?.GetAttribute("content") ?? meta?.TextContent);
        var availability = doc.QuerySelector("[itemprop='availability']");
        var availabilityText = availability?.GetAttribute("href") ?? availability?.GetAttribute("content");
        return (metaPrice, AvailabilityFromSchema(availabilityText));
    }

    private static (decimal? Price, bool? InStock) FindOffer(JsonElement e, int depth)
    {
        if (depth > 8)
            return (null, null);

        switch (e.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in e.EnumerateArray())
                {
                    var r = FindOffer(item, depth + 1);
                    if (r.Price is not null)
                        return r;
                }
                break;

            case JsonValueKind.Object:
                if (e.TryGetProperty("offers", out var offers))
                {
                    var r = FindOffer(offers, depth + 1);
                    if (r.Price is not null)
                        return r;
                }
                if (TryGetNumber(e, "price", out var price) || TryGetNumber(e, "lowPrice", out price))
                {
                    var availability = e.TryGetProperty("availability", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
                    return (price, AvailabilityFromSchema(availability));
                }
                if (e.TryGetProperty("@graph", out var graph))
                    return FindOffer(graph, depth + 1);
                break;
        }
        return (null, null);
    }

    private static bool TryGetNumber(JsonElement e, string name, out decimal? value)
    {
        value = null;
        if (!e.TryGetProperty(name, out var p))
            return false;
        value = p.ValueKind switch
        {
            JsonValueKind.Number => p.GetDecimal(),
            JsonValueKind.String => TextUtil.ParseDecimal(p.GetString()),
            _ => null,
        };
        return value is not null;
    }

    private static bool? AvailabilityFromSchema(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var v = value.ToLowerInvariant();
        if (v.Contains("outofstock") || v.Contains("soldout") || v.Contains("discontinued"))
            return false;
        if (v.Contains("instock") || v.Contains("limitedavailability") || v.Contains("preorder") || v.Contains("backorder"))
            return true;
        return null;
    }

    private static string Shorten(string s)
    {
        s = s.Trim();
        return s.Length > 60 ? s[..60] + "…" : s;
    }
}
