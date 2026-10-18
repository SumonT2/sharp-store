using Shop.Infrastructure;
using Shop.Infrastructure.Orders;

namespace Shop.Tests;

public class CheckoutRulesTests
{
    private static readonly ShopOptions Bd = new();

    [Theory]
    [InlineData("01712345678", "01712345678")]
    [InlineData("01712-345678", "01712345678")]
    [InlineData("+880 1712 345678", "+8801712345678")]
    [InlineData("8801912345678", "8801912345678")]
    public void Accepts_bangladeshi_mobile_numbers(string input, string expected) =>
        Assert.Equal(expected, OrderService.NormalizePhone(input, Bd));

    [Theory]
    [InlineData("0171234567")]   // too short
    [InlineData("01212345678")]  // no such operator prefix
    [InlineData("02-9123456")]   // landline
    [InlineData("hello")]
    public void Rejects_invalid_numbers(string input) => Assert.Null(OrderService.NormalizePhone(input, Bd));

    [Fact]
    public void Delivery_fee_depends_on_area_and_is_free_above_threshold()
    {
        var outside = OrderService.FindZone("outside dhaka", Bd)!;
        Assert.Equal(130m, OrderService.ShippingFor(10_000m, outside.Fee, Bd));
        Assert.Equal(0m, OrderService.ShippingFor(50_000m, outside.Fee, Bd));
        Assert.Null(OrderService.FindZone("Mars", Bd));
    }

    [Theory]
    [InlineData(100.8, 101)]
    [InlineData(24990, 24990)]
    [InlineData(24990.01, 24991)]
    public void Prices_round_up_to_whole_taka(double input, double expected) =>
        Assert.Equal((decimal)expected, Bd.RoundPrice((decimal)input));
}
