using Brewline.Tests.TestSupport;
using Brewline.Web.Domain;
using Brewline.Web.Payments;
using Brewline.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Brewline.Tests;

public class StripeSessionOptionsTests
{
    private static readonly PaymentUrls Urls = new("https://shop.test/orders/abc?confirming=1", "https://shop.test/orders/abc");
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    private static Order Order(int shippingCents = 490) => new()
    {
        Id = 7,
        Token = "abc",
        Email = "ana@example.com",
        Currency = "eur",
        SubtotalCents = 2780,
        ShippingCents = shippingCents,
        Items =
        [
            new OrderItem { ProductName = "Sarajevo Blend", UnitPriceCents = 890, Quantity = 2 },
            new OrderItem { ProductName = "Mug", UnitPriceCents = 1000, Quantity = 1 },
        ],
    };

    [Fact]
    public void Line_items_use_our_prices_in_cents_and_include_shipping()
    {
        var options = StripePaymentProvider.BuildOptions(Order(), Urls, Now);

        Assert.Equal("payment", options.Mode);
        Assert.Equal(3, options.LineItems.Count);

        var blend = options.LineItems[0];
        Assert.Equal(2, blend.Quantity);
        Assert.Equal(890, blend.PriceData.UnitAmount);
        Assert.Equal("eur", blend.PriceData.Currency);
        Assert.Equal("Sarajevo Blend", blend.PriceData.ProductData.Name);

        var shipping = options.LineItems[2];
        Assert.Equal("Shipping", shipping.PriceData.ProductData.Name);
        Assert.Equal(490, shipping.PriceData.UnitAmount);
        Assert.Equal(1, shipping.Quantity);
    }

    [Fact]
    public void The_sum_of_the_line_items_equals_the_order_total()
    {
        var order = Order();
        var options = StripePaymentProvider.BuildOptions(order, Urls, Now);

        var stripeTotal = options.LineItems.Sum(l => (l.PriceData.UnitAmount ?? 0) * (l.Quantity ?? 0));

        Assert.Equal(order.TotalCents, stripeTotal);
    }

    [Fact]
    public void Free_shipping_means_no_shipping_line()
    {
        var options = StripePaymentProvider.BuildOptions(Order(shippingCents: 0), Urls, Now);

        Assert.Equal(2, options.LineItems.Count);
        Assert.DoesNotContain(options.LineItems, l => l.PriceData.ProductData.Name == "Shipping");
    }

    [Fact]
    public void The_session_carries_the_order_identity_and_return_addresses()
    {
        var options = StripePaymentProvider.BuildOptions(Order(), Urls, Now);

        Assert.Equal("BL-100007", options.ClientReferenceId);
        Assert.Equal("ana@example.com", options.CustomerEmail);
        Assert.Equal("abc", options.Metadata["order_token"]);
        Assert.Equal(Urls.Success, options.SuccessUrl);
        Assert.Equal(Urls.Cancel, options.CancelUrl);
    }

    [Fact]
    public void The_session_expires_within_the_window_stripe_allows()
    {
        var options = StripePaymentProvider.BuildOptions(Order(), Urls, Now);

        var minutes = (options.ExpiresAt!.Value - Now).TotalMinutes;
        Assert.InRange(minutes, 30, 24 * 60); // Stripe: between 30 minutes and 24 hours from now
    }

    [Fact]
    public async Task Without_a_secret_key_the_provider_fails_clearly()
    {
        var provider = new StripePaymentProvider(Options.Create(new PaymentsOptions { Provider = "Stripe" }), TimeProvider.System);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CreateSessionAsync(Order(), Urls, default));

        Assert.Contains("StripeSecretKey", ex.Message);
    }
}

public class StripeWebhookTests : IDisposable
{
    private readonly TestShop _t = new();
    private readonly StripeWebhookProcessor _processor;

    public StripeWebhookTests() =>
        _processor = new StripeWebhookProcessor(
            Options.Create(new PaymentsOptions { Provider = "Stripe", StripeWebhookSecret = StripeSigner.Secret }),
            _t.Orders, NullLogger<StripeWebhookProcessor>.Instance);

    public void Dispose() => _t.Dispose();

    private async Task<Order> PendingOrder(string session = "cs_test_1") =>
        await _t.PendingOrder(await _t.AddProduct(priceCents: 1000, stock: 10), quantity: 2, sessionId: session);

    private Task<WebhookResult> Deliver(string payload, string? header) => _processor.ProcessAsync(payload, header);

    [Fact]
    public async Task A_correctly_signed_paid_session_marks_the_order_paid()
    {
        var order = await PendingOrder();
        var payload = StripeSigner.Event("checkout.session.completed", "cs_test_1", amountTotal: order.TotalCents);

        var result = await Deliver(payload, StripeSigner.Header(payload));

        Assert.Equal(WebhookResult.Handled, result);
        Assert.Equal(OrderStatus.Paid, (await _t.Reload(order)).Status);
        Assert.Single(_t.Email.Sent);
    }

    [Fact]
    public async Task Stripe_delivering_the_same_event_twice_changes_nothing_the_second_time()
    {
        var order = await PendingOrder();
        var payload = StripeSigner.Event("checkout.session.completed", "cs_test_1", amountTotal: order.TotalCents);

        await Deliver(payload, StripeSigner.Header(payload));
        var again = await Deliver(payload, StripeSigner.Header(payload));

        Assert.Equal(WebhookResult.Handled, again);
        Assert.Single(_t.Email.Sent);
        Assert.Equal(OrderStatus.Paid, (await _t.Reload(order)).Status);
    }

    [Fact]
    public async Task A_wrong_signature_is_rejected_and_nothing_is_paid()
    {
        var order = await PendingOrder();
        var payload = StripeSigner.Event("checkout.session.completed", "cs_test_1", amountTotal: order.TotalCents);

        var result = await Deliver(payload, StripeSigner.Header(payload, secret: "whsec_the_attackers_guess"));

        Assert.Equal(WebhookResult.InvalidSignature, result);
        Assert.Equal(OrderStatus.Pending, (await _t.Reload(order)).Status);
    }

    [Fact]
    public async Task A_payload_changed_after_signing_is_rejected()
    {
        var order = await PendingOrder();
        var genuine = StripeSigner.Event("checkout.session.completed", "cs_test_1", amountTotal: 1);
        var header = StripeSigner.Header(genuine);
        var tampered = StripeSigner.Event("checkout.session.completed", "cs_test_1", amountTotal: order.TotalCents);

        Assert.Equal(WebhookResult.InvalidSignature, await Deliver(tampered, header));
        Assert.Equal(OrderStatus.Pending, (await _t.Reload(order)).Status);
    }

    [Fact]
    public async Task A_missing_or_garbage_signature_header_is_rejected()
    {
        var payload = StripeSigner.Event("checkout.session.completed", "cs_test_1");

        Assert.Equal(WebhookResult.InvalidSignature, await Deliver(payload, null));
        Assert.Equal(WebhookResult.InvalidSignature, await Deliver(payload, ""));
        Assert.Equal(WebhookResult.InvalidSignature, await Deliver(payload, "t=123,v1=abc"));
        Assert.Equal(WebhookResult.InvalidSignature, await Deliver("not json at all", "t=123,v1=abc"));
    }

    [Fact]
    public async Task A_correctly_signed_body_that_is_not_an_event_is_a_rejection_not_a_crash()
    {
        const string payload = "<html>definitely not an event</html>";

        Assert.Equal(WebhookResult.InvalidSignature, await Deliver(payload, StripeSigner.Header(payload)));
    }

    [Fact]
    public async Task An_old_but_genuinely_signed_message_is_rejected_as_a_replay()
    {
        var order = await PendingOrder();
        var payload = StripeSigner.Event("checkout.session.completed", "cs_test_1", amountTotal: order.TotalCents);

        var result = await Deliver(payload, StripeSigner.Header(payload, at: DateTimeOffset.UtcNow.AddMinutes(-30)));

        Assert.Equal(WebhookResult.InvalidSignature, result);
        Assert.Equal(OrderStatus.Pending, (await _t.Reload(order)).Status);
    }

    [Fact]
    public async Task A_session_that_is_not_paid_yet_does_not_mark_the_order_paid()
    {
        var order = await PendingOrder();
        var payload = StripeSigner.Event("checkout.session.completed", "cs_test_1", amountTotal: order.TotalCents, paymentStatus: "unpaid");

        Assert.Equal(WebhookResult.Handled, await Deliver(payload, StripeSigner.Header(payload)));
        Assert.Equal(OrderStatus.Pending, (await _t.Reload(order)).Status);

        // ...until the delayed payment method confirms.
        var later = StripeSigner.Event("checkout.session.async_payment_succeeded", "cs_test_1", amountTotal: order.TotalCents);
        await Deliver(later, StripeSigner.Header(later));
        Assert.Equal(OrderStatus.Paid, (await _t.Reload(order)).Status);
    }

    [Fact]
    public async Task A_paid_amount_that_differs_from_the_order_is_not_accepted()
    {
        var order = await PendingOrder();
        var payload = StripeSigner.Event("checkout.session.completed", "cs_test_1", amountTotal: order.TotalCents - 1);

        await Deliver(payload, StripeSigner.Header(payload));

        Assert.Equal(OrderStatus.Pending, (await _t.Reload(order)).Status);
    }

    [Fact]
    public async Task An_expired_session_cancels_the_order_and_releases_the_stock()
    {
        var coffee = await _t.AddProduct(stock: 10);
        var order = await _t.PendingOrder(coffee, 4, "cs_expired");
        Assert.Equal(6, await _t.StockOf(coffee));
        var payload = StripeSigner.Event("checkout.session.expired", "cs_expired", orderToken: order.Token);

        Assert.Equal(WebhookResult.Handled, await Deliver(payload, StripeSigner.Header(payload)));

        Assert.Equal(OrderStatus.Cancelled, (await _t.Reload(order)).Status);
        Assert.Equal(10, await _t.StockOf(coffee));
    }

    [Fact]
    public async Task An_expiry_message_cannot_cancel_an_order_that_was_already_paid()
    {
        var order = await PendingOrder("cs_paid");
        await _t.Orders.ConfirmPaymentAsync("cs_paid", order.TotalCents, "eur");
        var payload = StripeSigner.Event("checkout.session.expired", "cs_paid", orderToken: order.Token);

        await Deliver(payload, StripeSigner.Header(payload));

        Assert.Equal(OrderStatus.Paid, (await _t.Reload(order)).Status);
    }

    [Fact]
    public async Task Events_we_do_not_care_about_are_acknowledged_and_ignored()
    {
        var payload = StripeSigner.Event("customer.created", "cs_whatever");

        Assert.Equal(WebhookResult.Handled, await Deliver(payload, StripeSigner.Header(payload)));
    }

    [Fact]
    public async Task A_payment_for_a_session_we_never_started_is_ignored_safely()
    {
        var payload = StripeSigner.Event("checkout.session.completed", "cs_unknown");

        Assert.Equal(WebhookResult.Handled, await Deliver(payload, StripeSigner.Header(payload)));
        Assert.Empty(_t.Email.Sent);
    }
}
