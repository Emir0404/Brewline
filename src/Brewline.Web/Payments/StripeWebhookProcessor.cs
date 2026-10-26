using Brewline.Web.Services;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace Brewline.Web.Payments;

public enum WebhookResult
{
    /// <summary>Signature missing or wrong: answer 400, never process.</summary>
    InvalidSignature,

    /// <summary>Handled (or deliberately ignored): answer 200 so Stripe stops retrying.</summary>
    Handled,
}

/// <summary>
/// Turns Stripe's webhook calls into order changes. This is the only place where an order becomes "paid":
/// the customer returning to our success page proves nothing, a signed message from Stripe does.
/// </summary>
public sealed class StripeWebhookProcessor(
    IOptions<PaymentsOptions> options, OrderService orders, ILogger<StripeWebhookProcessor> logger)
{
    public async Task<WebhookResult> ProcessAsync(string payload, string? signatureHeader, CancellationToken ct = default)
    {
        // Without a signature there is nothing to verify (and Stripe's parser does not expect a missing header).
        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            logger.LogWarning("Rejected Stripe webhook: no Stripe-Signature header");
            return WebhookResult.InvalidSignature;
        }

        Event stripeEvent;
        try
        {
            // Verifies the HMAC signature and that the message is recent (replay protection).
            stripeEvent = EventUtility.ConstructEvent(
                payload, signatureHeader, options.Value.StripeWebhookSecret, throwOnApiVersionMismatch: false);
        }
        catch (Exception ex) when (ex is StripeException or System.Text.Json.JsonException or Newtonsoft.Json.JsonException)
        {
            // Wrong signature, too old, or not even an event: always a 400, never a 500 and never processed.
            logger.LogWarning("Rejected Stripe webhook: {Message}", ex.Message);
            return WebhookResult.InvalidSignature;
        }

        if (stripeEvent.Data.Object is not Session session)
        {
            logger.LogDebug("Ignoring Stripe event {Type}", stripeEvent.Type);
            return WebhookResult.Handled;
        }

        switch (stripeEvent.Type)
        {
            case EventTypes.CheckoutSessionCompleted:
            case EventTypes.CheckoutSessionAsyncPaymentSucceeded:
                // "completed" also fires for payment methods that settle later: wait until the money is really there.
                if (session.PaymentStatus == "paid")
                    await orders.ConfirmPaymentAsync(session.Id, session.AmountTotal ?? -1, session.Currency ?? "", ct);
                else
                    logger.LogInformation("Session {Id} completed but payment status is {Status}", session.Id, session.PaymentStatus);
                break;

            case EventTypes.CheckoutSessionExpired:
                var token = session.Metadata is { } m && m.TryGetValue("order_token", out var t) ? t : null;
                await orders.CancelPendingAsync(token, ct);
                break;

            default:
                logger.LogDebug("Ignoring Stripe event {Type}", stripeEvent.Type);
                break;
        }

        return WebhookResult.Handled;
    }
}
