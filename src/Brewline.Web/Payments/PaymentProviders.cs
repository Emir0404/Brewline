using System.ComponentModel.DataAnnotations;
using Brewline.Web.Domain;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace Brewline.Web.Payments;

public sealed class PaymentsOptions
{
    public const string SectionName = "Payments";

    /// <summary>"Demo" (simulated, no money) or "Stripe".</summary>
    [Required] public string Provider { get; set; } = "Demo";

    public string? StripeSecretKey { get; set; }
    public string? StripeWebhookSecret { get; set; }

    public bool IsDemo => string.Equals(Provider, "Demo", StringComparison.OrdinalIgnoreCase);
    public bool IsStripe => string.Equals(Provider, "Stripe", StringComparison.OrdinalIgnoreCase);
}

public sealed record PaymentUrls(string Success, string Cancel);

/// <param name="SessionId">Our handle for the payment at the provider. Stored on the order.</param>
/// <param name="RedirectUrl">Where to send the customer to pay.</param>
public sealed record PaymentSession(string SessionId, string RedirectUrl);

public interface IPaymentProvider
{
    Task<PaymentSession> CreateSessionAsync(Order order, PaymentUrls urls, CancellationToken ct);
}

/// <summary>Pretends to take payment so the whole shop can be tried without a Stripe account.</summary>
public sealed class DemoPaymentProvider : IPaymentProvider
{
    public Task<PaymentSession> CreateSessionAsync(Order order, PaymentUrls urls, CancellationToken ct) =>
        Task.FromResult(new PaymentSession($"demo_{order.Token}", $"/demo-pay/{order.Token}"));
}

/// <summary>
/// Real card payments through Stripe Checkout: Stripe hosts the payment page, so card numbers never touch
/// our server. We only learn the result from Stripe's signed webhook.
/// </summary>
public sealed class StripePaymentProvider(IOptions<PaymentsOptions> options, TimeProvider clock) : IPaymentProvider
{
    public async Task<PaymentSession> CreateSessionAsync(Order order, PaymentUrls urls, CancellationToken ct)
    {
        var secret = options.Value.StripeSecretKey;
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Payments:StripeSecretKey is not configured.");

        var service = new SessionService(new StripeClient(secret));
        var session = await service.CreateAsync(BuildOptions(order, urls, clock.GetUtcNow().UtcDateTime), cancellationToken: ct);
        return new PaymentSession(session.Id, session.Url);
    }

    /// <summary>
    /// What we tell Stripe. Amounts come from our own order (integer cents), never from the browser.
    /// Public so it can be unit tested without calling Stripe.
    /// </summary>
    public static SessionCreateOptions BuildOptions(Order order, PaymentUrls urls, DateTime nowUtc)
    {
        var lines = order.Items.Select(item => new SessionLineItemOptions
        {
            Quantity = item.Quantity,
            PriceData = new SessionLineItemPriceDataOptions
            {
                Currency = order.Currency,
                UnitAmount = item.UnitPriceCents,
                ProductData = new SessionLineItemPriceDataProductDataOptions { Name = item.ProductName },
            },
        }).ToList();

        if (order.ShippingCents > 0)
            lines.Add(new SessionLineItemOptions
            {
                Quantity = 1,
                PriceData = new SessionLineItemPriceDataOptions
                {
                    Currency = order.Currency,
                    UnitAmount = order.ShippingCents,
                    ProductData = new SessionLineItemPriceDataProductDataOptions { Name = "Shipping" },
                },
            });

        return new SessionCreateOptions
        {
            Mode = "payment",
            LineItems = lines,
            CustomerEmail = order.Email,
            ClientReferenceId = order.Reference,
            SuccessUrl = urls.Success,
            CancelUrl = urls.Cancel,
            // Stripe wants an expiry between 30 minutes and 24 hours from now. Our sweeper gives up on unpaid orders well after that.
            ExpiresAt = nowUtc.AddMinutes(31),
            Metadata = new Dictionary<string, string> { ["order_token"] = order.Token },
        };
    }
}
