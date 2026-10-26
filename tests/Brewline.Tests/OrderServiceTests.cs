using Brewline.Tests.TestSupport;
using Brewline.Web.Domain;
using Brewline.Web.Services;

namespace Brewline.Tests;

public class OrderServiceTests : IDisposable
{
    private readonly TestShop _t = new();
    public void Dispose() => _t.Dispose();

    // ---- confirming payment ----

    [Fact]
    public async Task A_confirmed_payment_marks_the_order_paid_and_emails_the_customer()
    {
        var coffee = await _t.AddProduct(priceCents: 1000);
        var order = await _t.PendingOrder(coffee, quantity: 2);

        var outcome = await _t.Orders.ConfirmPaymentAsync("cs_test_1", order.TotalCents, "eur");

        Assert.Equal(PaymentOutcome.Paid, outcome);
        var reloaded = await _t.Reload(order);
        Assert.Equal(OrderStatus.Paid, reloaded.Status);
        Assert.Equal(_t.Clock.GetUtcNow().UtcDateTime, reloaded.PaidAt);

        var mail = Assert.Single(_t.Email.Sent);
        Assert.Equal("ana@example.com", mail.To);
        Assert.Contains(order.Reference, mail.Subject);
        Assert.Contains("2 x Test Coffee", mail.Body);
    }

    [Fact]
    public async Task The_same_payment_reported_twice_is_recorded_and_emailed_once()
    {
        var order = await _t.PendingOrder(await _t.AddProduct());

        var first = await _t.Orders.ConfirmPaymentAsync("cs_test_1", order.TotalCents, "eur");
        var second = await _t.Orders.ConfirmPaymentAsync("cs_test_1", order.TotalCents, "eur");

        Assert.Equal(PaymentOutcome.Paid, first);
        Assert.Equal(PaymentOutcome.AlreadyPaid, second);
        Assert.Single(_t.Email.Sent);
    }

    [Fact]
    public async Task A_paid_amount_that_does_not_match_the_order_is_refused()
    {
        var order = await _t.PendingOrder(await _t.AddProduct(priceCents: 1000));

        var outcome = await _t.Orders.ConfirmPaymentAsync("cs_test_1", amountCents: 1, "eur");

        Assert.Equal(PaymentOutcome.AmountMismatch, outcome);
        Assert.Equal(OrderStatus.Pending, (await _t.Reload(order)).Status);
        Assert.Empty(_t.Email.Sent);
    }

    [Fact]
    public async Task A_payment_in_the_wrong_currency_is_refused()
    {
        var order = await _t.PendingOrder(await _t.AddProduct());

        Assert.Equal(PaymentOutcome.AmountMismatch, await _t.Orders.ConfirmPaymentAsync("cs_test_1", order.TotalCents, "usd"));
        Assert.Equal(PaymentOutcome.Paid, await _t.Orders.ConfirmPaymentAsync("cs_test_1", order.TotalCents, "EUR")); // case does not matter
    }

    [Fact]
    public async Task A_payment_for_an_unknown_session_changes_nothing()
    {
        var order = await _t.PendingOrder(await _t.AddProduct());

        Assert.Equal(PaymentOutcome.UnknownOrder, await _t.Orders.ConfirmPaymentAsync("cs_somebody_else", order.TotalCents, "eur"));
        Assert.Equal(OrderStatus.Pending, (await _t.Reload(order)).Status);
    }

    // ---- cancelling and expiry ----

    [Fact]
    public async Task Cancelling_a_pending_order_puts_the_stock_back()
    {
        var coffee = await _t.AddProduct(stock: 10);
        var order = await _t.PendingOrder(coffee, quantity: 3);
        Assert.Equal(7, await _t.StockOf(coffee));

        Assert.True(await _t.Orders.CancelPendingAsync(order.Token));

        Assert.Equal(10, await _t.StockOf(coffee));
        Assert.Equal(OrderStatus.Cancelled, (await _t.Reload(order)).Status);
    }

    [Fact]
    public async Task Cancelling_twice_or_cancelling_a_paid_order_does_nothing()
    {
        var coffee = await _t.AddProduct(stock: 10);
        var order = await _t.PendingOrder(coffee);
        await _t.Orders.CancelPendingAsync(order.Token);
        var stockAfterFirst = await _t.StockOf(coffee);

        Assert.False(await _t.Orders.CancelPendingAsync(order.Token));
        Assert.Equal(stockAfterFirst, await _t.StockOf(coffee));

        var paidOrder = await _t.PendingOrder(coffee, sessionId: "cs_test_2");
        await _t.Orders.ConfirmPaymentAsync("cs_test_2", paidOrder.TotalCents, "eur");
        Assert.False(await _t.Orders.CancelPendingAsync(paidOrder.Token));
        Assert.Equal(OrderStatus.Paid, (await _t.Reload(paidOrder)).Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-such-token")]
    public async Task Cancelling_an_unknown_order_returns_false(string? token)
    {
        Assert.False(await _t.Orders.CancelPendingAsync(token));
    }

    [Fact]
    public async Task The_sweeper_cancels_only_unpaid_orders_older_than_the_limit_and_restocks()
    {
        var coffee = await _t.AddProduct(stock: 20);
        var old = await _t.PendingOrder(coffee, 2, "cs_old");
        var paidOld = await _t.PendingOrder(coffee, 2, "cs_paid");
        await _t.Orders.ConfirmPaymentAsync("cs_paid", paidOld.TotalCents, "eur");

        _t.Clock.Advance(TimeSpan.FromMinutes(90));
        var fresh = await _t.PendingOrder(coffee, 1, "cs_fresh");

        var cancelled = await _t.Orders.ExpireStaleAsync(TimeSpan.FromMinutes(60));

        Assert.Equal(1, cancelled);
        Assert.Equal(OrderStatus.Cancelled, (await _t.Reload(old)).Status);
        Assert.Equal(OrderStatus.Paid, (await _t.Reload(paidOld)).Status);
        Assert.Equal(OrderStatus.Pending, (await _t.Reload(fresh)).Status);
        Assert.Equal(20 - 2 - 1, await _t.StockOf(coffee)); // 2 (paid) + 1 (fresh) are still taken, the old 2 are back
    }

    // ---- paid after we had already cancelled ----

    [Fact]
    public async Task Payment_arriving_for_a_cancelled_order_revives_it_when_the_stock_is_still_there()
    {
        var coffee = await _t.AddProduct(stock: 10);
        var order = await _t.PendingOrder(coffee, 3);
        await _t.Orders.CancelPendingAsync(order.Token);
        Assert.Equal(10, await _t.StockOf(coffee));

        var outcome = await _t.Orders.ConfirmPaymentAsync("cs_test_1", order.TotalCents, "eur");

        Assert.Equal(PaymentOutcome.Paid, outcome);
        Assert.Equal(OrderStatus.Paid, (await _t.Reload(order)).Status);
        Assert.Equal(7, await _t.StockOf(coffee));
    }

    [Fact]
    public async Task Payment_arriving_for_a_cancelled_order_is_flagged_for_a_human_when_the_stock_is_gone()
    {
        var coffee = await _t.AddProduct(stock: 3);
        var order = await _t.PendingOrder(coffee, 3);
        await _t.Orders.CancelPendingAsync(order.Token);

        // Somebody else buys the released stock before the late payment arrives.
        var other = await _t.Checkout.PlaceOrderAsync(await _t.Basket((coffee, 3)), TestShop.Customer("Other"));

        var outcome = await _t.Orders.ConfirmPaymentAsync("cs_test_1", order.TotalCents, "eur");

        Assert.Equal(PaymentOutcome.NeedsReview, outcome);
        Assert.Equal(OrderStatus.NeedsReview, (await _t.Reload(order)).Status);
        Assert.Equal(0, await _t.StockOf(coffee));          // never goes negative
        Assert.Equal(OrderStatus.Pending, (await _t.Reload(other)).Status);
        Assert.Empty(_t.Email.Sent);                         // no "thank you" for an order we cannot fulfil

        // Repeating the event keeps it flagged instead of changing anything else.
        Assert.Equal(PaymentOutcome.NeedsReview, await _t.Orders.ConfirmPaymentAsync("cs_test_1", order.TotalCents, "eur"));
    }

    // ---- shipping ----

    [Fact]
    public async Task A_paid_order_can_be_shipped_and_the_customer_is_told()
    {
        var order = await _t.PendingOrder(await _t.AddProduct());
        await _t.Orders.ConfirmPaymentAsync("cs_test_1", order.TotalCents, "eur");

        var shipped = await _t.Orders.ShipAsync(order.Id);

        Assert.Equal(OrderStatus.Shipped, shipped!.Status);
        Assert.Equal(2, _t.Email.Sent.Count);
        Assert.Contains("shipped", _t.Email.Sent[1].Body);
    }

    [Fact]
    public async Task An_unpaid_order_cannot_be_shipped_and_an_unknown_one_returns_null()
    {
        var order = await _t.PendingOrder(await _t.AddProduct());

        await Assert.ThrowsAsync<DomainException>(() => _t.Orders.ShipAsync(order.Id));
        Assert.Null(await _t.Orders.ShipAsync(999_999));
    }

    // ---- lookups ----

    [Fact]
    public async Task Orders_are_found_by_token_and_listed_newest_first_with_a_status_filter()
    {
        var coffee = await _t.AddProduct(stock: 50);
        var first = await _t.PendingOrder(coffee, 1, "cs_1");
        _t.Clock.Advance(TimeSpan.FromMinutes(5));
        var second = await _t.PendingOrder(coffee, 1, "cs_2");
        await _t.Orders.ConfirmPaymentAsync("cs_2", second.TotalCents, "eur");

        Assert.Equal(first.Id, (await _t.Orders.GetByTokenAsync(first.Token))!.Id);
        Assert.Null(await _t.Orders.GetByTokenAsync("guess"));
        Assert.Null(await _t.Orders.GetByTokenAsync(null));

        Assert.Equal(new[] { second.Id, first.Id }, (await _t.Orders.ListAsync(null)).Select(o => o.Id));
        Assert.Equal(second.Id, Assert.Single(await _t.Orders.ListAsync(OrderStatus.Paid)).Id);
    }
}

public class CatalogServiceTests : IDisposable
{
    private readonly TestShop _t = new();
    public void Dispose() => _t.Dispose();

    private static ProductInput Input(string name = "New Coffee", int cents = 1000, int stock = 5, bool active = true, string accent = "#7c4a21") =>
        new(name, "Coffee", "A summary", null, cents, stock, active, accent, "250 g");

    [Fact]
    public async Task Only_active_products_are_listed_sorted_by_category_and_name()
    {
        await _t.Catalog.CreateAsync(Input("Zeta"));
        await _t.Catalog.CreateAsync(Input("Alpha"));
        await _t.Catalog.CreateAsync(Input("Hidden", active: false));

        var list = await _t.Catalog.ListAsync(null, null);

        Assert.Equal(new[] { "Alpha", "Zeta" }, list.Select(p => p.Name));
        Assert.Equal(3, (await _t.Catalog.AdminListAsync()).Count);
    }

    [Fact]
    public async Task Filtering_by_category_and_searching_by_text()
    {
        await _t.Catalog.CreateAsync(Input("Ethiopia Natural") with { Summary = "Blueberry and wine" });
        await _t.Catalog.CreateAsync(Input("Hand Grinder") with { Category = "Equipment" });

        Assert.Single(await _t.Catalog.ListAsync("Equipment", null));
        Assert.Single(await _t.Catalog.ListAsync(null, "ethiopia"));
        Assert.Single(await _t.Catalog.ListAsync(null, "BLUEBERRY"));
        Assert.Empty(await _t.Catalog.ListAsync("Equipment", "ethiopia"));
        Assert.Equal(new[] { "Coffee", "Equipment" }, await _t.Catalog.CategoriesAsync());
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("\\")]
    public async Task Wildcard_characters_in_a_search_are_searched_literally(string search)
    {
        await _t.Catalog.CreateAsync(Input("Plain Coffee"));

        Assert.Empty(await _t.Catalog.ListAsync(null, search));
    }

    [Fact]
    public async Task Creating_makes_a_unique_slug_and_updating_keeps_it()
    {
        var first = await _t.Catalog.CreateAsync(Input("Sarajevo Blend"));
        var second = await _t.Catalog.CreateAsync(Input("Sarajevo Blend"));

        Assert.Equal("sarajevo-blend", first.Slug);
        Assert.Equal("sarajevo-blend-2", second.Slug);

        await _t.Catalog.UpdateAsync(first.Id, Input("Completely Renamed", cents: 1500));
        var updated = await _t.Catalog.AdminGetAsync(first.Id);
        Assert.Equal("sarajevo-blend", updated!.Slug);
        Assert.Equal("Completely Renamed", updated.Name);
        Assert.Equal(1500, updated.PriceCents);
    }

    [Fact]
    public async Task Hidden_products_are_not_found_by_slug_but_their_picture_still_is()
    {
        var hidden = await _t.Catalog.CreateAsync(Input("Secret", active: false));

        Assert.Null(await _t.Catalog.GetBySlugAsync(hidden.Slug));
        Assert.NotNull(await _t.Catalog.FindAnyBySlugAsync(hidden.Slug));
    }

    [Fact]
    public async Task Featured_skips_sold_out_and_hidden_products()
    {
        await _t.Catalog.CreateAsync(Input("Good", stock: 5));
        await _t.Catalog.CreateAsync(Input("Sold out", stock: 0));
        await _t.Catalog.CreateAsync(Input("Hidden", active: false));

        Assert.Equal("Good", Assert.Single(await _t.Catalog.FeaturedAsync(4)).Name);
    }

    public static TheoryData<string, ProductInput> BadInputs => new()
    {
        { "blank name", Input(name: " ") },
        { "price too low", Input(cents: 10) },
        { "negative stock", Input(stock: -1) },
        { "bad colour", Input(accent: "red") },
    };

    [Theory]
    [MemberData(nameof(BadInputs))]
    public async Task Invalid_products_are_rejected(string _, ProductInput input)
    {
        await Assert.ThrowsAsync<DomainException>(() => _t.Catalog.CreateAsync(input));
        await Assert.ThrowsAsync<DomainException>(() => _t.Catalog.UpdateAsync(1, input));
    }

    [Fact]
    public async Task Updating_an_unknown_product_returns_null()
    {
        Assert.Null(await _t.Catalog.UpdateAsync(999_999, Input()));
    }
}
