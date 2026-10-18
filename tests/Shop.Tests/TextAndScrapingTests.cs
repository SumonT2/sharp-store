using Shop.Core.Text;
using Shop.Infrastructure.Scraping;

namespace Shop.Tests;

public class TextAndScrapingTests
{
    [Theory]
    [InlineData("1,299.00", 1299.00)]
    [InlineData("৳ 1,299", 1299)]
    [InlineData("1.299,50 €", 1299.50)]
    [InlineData("$12", 12)]
    [InlineData("CNY 89.5", 89.5)]
    [InlineData("12,5", 12.5)]
    [InlineData("1,234,567", 1234567)]
    public void Parses_prices_in_common_formats(string text, double expected)
    {
        Assert.Equal((decimal)expected, TextUtil.ParseDecimal(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("call for price")]
    public void Returns_null_for_non_numbers(string text) => Assert.Null(TextUtil.ParseDecimal(text));

    [Theory]
    [InlineData("Phones & Tablets", "phones-tablets")]
    [InlineData("  USB-C  Charger 65W ", "usb-c-charger-65w")]
    [InlineData("Café Crème", "cafe-creme")]
    [InlineData("Galaxy S24+", "galaxy-s24plus")]
    public void Slugify(string input, string expected) => Assert.Equal(expected, TextUtil.Slugify(input));

    [Fact]
    public void Robots_longest_match_wins_and_specific_agent_group_is_used()
    {
        var robots = RobotsPolicy.Parse("""
            User-agent: *
            Disallow: /

            User-agent: ShopPriceMonitor
            Disallow: /checkout
            Allow: /checkout/help
            """, "ShopPriceMonitor/1.0");

        Assert.True(robots.IsAllowed("/product/1"));
        Assert.False(robots.IsAllowed("/checkout/pay"));
        Assert.True(robots.IsAllowed("/checkout/help"));
        Assert.False(RobotsPolicy.Parse("User-agent: *\nDisallow: /", "Other").IsAllowed("/x"));
        Assert.True(RobotsPolicy.Parse("User-agent: *\nDisallow:", "Other").IsAllowed("/x"));
    }

    [Fact]
    public void Extracts_price_with_css_selector_and_stock_pattern()
    {
        const string html = """<div class="price"><span class="amount">৳ 24,500</span></div><p class="stock">In Stock (5)</p>""";
        var result = PriceExtractor.Extract(html, ".price .amount", ".stock", "in stock");
        Assert.Null(result.Error);
        Assert.Equal(24500m, result.Price);
        Assert.True(result.InStock);

        var outOfStock = PriceExtractor.Extract(html.Replace("In Stock (5)", "Sold out"), ".price .amount", ".stock", "in stock");
        Assert.False(outOfStock.InStock);
    }

    [Fact]
    public void Extracts_price_from_json_ld_without_selector()
    {
        const string html = """
            <script type="application/ld+json">{"@context":"https://schema.org","@graph":[{"@type":"Product","name":"X",
            "offers":{"@type":"Offer","price":"199.90","availability":"https://schema.org/OutOfStock"}}]}</script>
            """;
        var result = PriceExtractor.Extract(html, null, null, null);
        Assert.Equal(199.90m, result.Price);
        Assert.False(result.InStock);
    }

    [Fact]
    public void Reports_missing_price_element()
    {
        var result = PriceExtractor.Extract("<p>nothing</p>", ".price", null, null);
        Assert.NotNull(result.Error);
        Assert.Null(result.Price);
    }
}
