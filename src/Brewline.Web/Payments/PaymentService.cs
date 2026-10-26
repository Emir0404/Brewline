using Brewline.Web.Domain;
using Brewline.Web.Services;

namespace Brewline.Web.Payments;

/// <summary>Starts the payment for an order with whichever provider is configured.</summary>
public sealed class PaymentService(IPaymentProvider provider, OrderService orders, ILogger<PaymentService> logger)
{
    /// <returns>The address to send the customer to.</returns>
    public async Task<string> StartAsync(Order order, PaymentUrls urls, CancellationToken ct = default)
    {
        if (order.Status != OrderStatus.Pending)
            throw new DomainException("This order is not waiting for payment.");

        var session = await provider.CreateSessionAsync(order, urls, ct);
        await orders.AttachPaymentSessionAsync(order.Id, session.SessionId, ct);
        return session.RedirectUrl;
    }

    /// <summary>
    /// Like <see cref="StartAsync"/> but never throws for provider problems: if the payment service is
    /// unreachable the order is cancelled (stock goes back on sale) and null is returned.
    /// </summary>
    public async Task<string?> TryStartAsync(Order order, string siteBaseUrl, CancellationToken ct = default)
    {
        try
        {
            var urls = new PaymentUrls(
                Success: $"{siteBaseUrl}/orders/{order.Token}?confirming=1",
                Cancel: $"{siteBaseUrl}/orders/{order.Token}");
            return await StartAsync(order, urls, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not start payment for order {Reference}", order.Reference);
            await orders.CancelPendingAsync(order.Token, ct);
            return null;
        }
    }
}
