using Brewline.Web.Domain;
using Brewline.Web.Services;

namespace Brewline.Tests;

public class PricingTests
{
    [Theory]
    [InlineData(0, 0)]       // empty basket costs nothing
    [InlineData(1, 490)]
    [InlineData(4999, 490)]  // one cent below the free shipping limit
    [InlineData(5000, 0)]    // exactly at the limit is free
    [InlineData(12000, 0)]
    public void Shipping_is_flat_below_the_limit_and_free_from_it(int subtotal, int expectedShipping)
    {
        var price = Pricing.Calculate(subtotal);

        Assert.Equal(expectedShipping, price.ShippingCents);
        Assert.Equal(subtotal + expectedShipping, price.TotalCents);
    }

    [Fact]
    public void A_negative_subtotal_is_a_bug()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Pricing.Calculate(-1));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3000, 2000)]
    [InlineData(4999, 1)]
    [InlineData(5000, 0)]
    [InlineData(9000, 0)]
    public void Tells_how_much_is_missing_for_free_shipping(int subtotal, int missing)
    {
        Assert.Equal(missing, Pricing.AwayFromFreeShipping(subtotal));
    }

    [Theory]
    [InlineData(0, "eur", "€0.00")]
    [InlineData(5, "eur", "€0.05")]
    [InlineData(890, "eur", "€8.90")]
    [InlineData(123456, "EUR", "€1,234.56")]
    [InlineData(1000, "usd", "$10.00")]
    [InlineData(1000, "bam", "10.00 BAM")]
    public void Formats_whole_cents_as_money(int cents, string currency, string expected)
    {
        Assert.Equal(expected, Pricing.Format(cents, currency));
    }
}

public class OrderStateTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    private static Order NewOrder() => new()
    {
        Id = 42,
        SubtotalCents = 2000,
        ShippingCents = 490,
        Items = [new OrderItem { ProductName = "x", UnitPriceCents = 1000, Quantity = 2 }],
    };

    [Fact]
    public void Reference_and_total_are_derived()
    {
        var order = NewOrder();

        Assert.Equal("BL-100042", order.Reference);
        Assert.Equal(2490, order.TotalCents);
        Assert.Equal(2000, order.Items[0].LineTotalCents);
    }

    [Fact]
    public void A_new_order_waits_for_payment()
    {
        Assert.Equal(OrderStatus.Pending, NewOrder().Status);
        Assert.True(NewOrder().IsOpen);
    }

    [Fact]
    public void Paying_changes_the_order_once_and_a_repeat_changes_nothing()
    {
        var order = NewOrder();

        Assert.True(order.MarkPaid(Now));
        Assert.False(order.MarkPaid(Now.AddMinutes(5)));

        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(Now, order.PaidAt);
    }

    [Fact]
    public void A_shipped_order_stays_shipped_when_the_payment_event_repeats()
    {
        var order = NewOrder();
        order.MarkPaid(Now);
        order.Ship(Now);

        Assert.False(order.MarkPaid(Now));
        Assert.Equal(OrderStatus.Shipped, order.Status);
    }

    [Fact]
    public void A_cancelled_order_cannot_simply_be_marked_paid()
    {
        var order = NewOrder();
        order.Cancel();

        Assert.Throws<DomainException>(() => order.MarkPaid(Now));
    }

    [Fact]
    public void Only_a_paid_order_can_be_shipped()
    {
        var pending = NewOrder();
        Assert.Throws<DomainException>(() => pending.Ship(Now));

        var paid = NewOrder();
        paid.MarkPaid(Now);
        paid.Ship(Now.AddDays(1));
        Assert.Equal(OrderStatus.Shipped, paid.Status);
        Assert.Equal(Now.AddDays(1), paid.ShippedAt);
        Assert.Throws<DomainException>(() => paid.Ship(Now));
    }

    [Fact]
    public void Only_a_pending_order_can_be_cancelled()
    {
        var paid = NewOrder();
        paid.MarkPaid(Now);

        Assert.Throws<DomainException>(() => paid.Cancel());
        Assert.Equal(OrderStatus.Paid, paid.Status);
    }

    [Fact]
    public void A_cancelled_order_can_be_reopened_or_flagged_but_nothing_else_can()
    {
        var cancelled = NewOrder();
        cancelled.Cancel();
        cancelled.Reopen();
        Assert.Equal(OrderStatus.Pending, cancelled.Status);

        var other = NewOrder();
        other.Cancel();
        other.FlagForReview();
        Assert.Equal(OrderStatus.NeedsReview, other.Status);

        Assert.Throws<DomainException>(() => NewOrder().Reopen());
        Assert.Throws<DomainException>(() => NewOrder().FlagForReview());
    }
}

public class TokensTests
{
    [Fact]
    public void Tokens_are_long_unique_and_url_safe()
    {
        var tokens = Enumerable.Range(0, 500).Select(_ => Tokens.New()).ToList();

        Assert.Equal(500, tokens.Distinct().Count());
        Assert.All(tokens, t =>
        {
            Assert.True(t.Length >= 20);
            Assert.Matches("^[A-Za-z0-9_-]+$", t);
        });
    }

    [Theory]
    [InlineData("Sarajevo Blend", "sarajevo-blend")]
    [InlineData("  Ethiopia   Yirgacheffe (250 g) ", "ethiopia-yirgacheffe-250-g")]
    [InlineData("Šećerana Čapljina", "secerana-capljina")]
    [InlineData("A & B / C", "a-b-c")]
    [InlineData("!!!", "product")]
    [InlineData("", "product")]
    public void Slugify_makes_url_friendly_names(string input, string expected)
    {
        Assert.Equal(expected, Tokens.Slugify(input));
    }

    [Fact]
    public void Slugify_limits_the_length()
    {
        var slug = Tokens.Slugify(new string('a', 300));

        Assert.True(slug.Length <= 70);
    }
}
