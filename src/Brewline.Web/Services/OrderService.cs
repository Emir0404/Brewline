using Brewline.Web.Data;
using Brewline.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace Brewline.Web.Services;

public enum PaymentOutcome
{
    /// <summary>The order is now paid.</summary>
    Paid,

    /// <summary>We had already recorded this payment (providers send the same event more than once).</summary>
    AlreadyPaid,

    /// <summary>No order belongs to that payment session.</summary>
    UnknownOrder,

    /// <summary>The paid amount or currency differs from what we asked for. Nothing is changed.</summary>
    AmountMismatch,

    /// <summary>Paid after we cancelled the order and the stock was gone: needs a human.</summary>
    NeedsReview,
}

public sealed class OrderService(
    ShopDbContext db, TimeProvider clock, IEmailSender email, ILogger<OrderService> logger)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public Task<Order?> GetByTokenAsync(string? token, CancellationToken ct = default) =>
        string.IsNullOrEmpty(token)
            ? Task.FromResult<Order?>(null)
            : db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Token == token, ct);

    public Task<Order?> GetAsync(int id, CancellationToken ct = default) =>
        db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<List<Order>> ListAsync(OrderStatus? status, CancellationToken ct = default)
    {
        var query = db.Orders.AsNoTracking().Include(o => o.Items).AsQueryable();
        if (status is { } s) query = query.Where(o => o.Status == s);

        // Sorted in memory: SQLite cannot order by DateTime reliably through every provider path.
        return (await query.ToListAsync(ct)).OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id).ToList();
    }

    public async Task AttachPaymentSessionAsync(int orderId, string sessionId, CancellationToken ct = default)
    {
        await db.Orders.Where(o => o.Id == orderId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.PaymentSessionId, sessionId), ct);
    }

    /// <summary>
    /// Records a successful payment. Safe to call many times for the same payment and
    /// refuses to mark an order paid when the money does not match the order.
    /// </summary>
    public async Task<PaymentOutcome> ConfirmPaymentAsync(
        string sessionId, long amountCents, string currency, CancellationToken ct = default)
    {
        var order = await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.PaymentSessionId == sessionId, ct);
        if (order is null)
        {
            logger.LogWarning("Payment for unknown session {SessionId}", sessionId);
            return PaymentOutcome.UnknownOrder;
        }

        if (amountCents != order.TotalCents || !string.Equals(currency, order.Currency, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogError("Payment mismatch for {Reference}: expected {Expected} {Currency}, got {Amount} {PaidCurrency}",
                order.Reference, order.TotalCents, order.Currency, amountCents, currency);
            return PaymentOutcome.AmountMismatch;
        }

        if (order.Status is OrderStatus.Paid or OrderStatus.Shipped or OrderStatus.NeedsReview)
            return order.Status == OrderStatus.NeedsReview ? PaymentOutcome.NeedsReview : PaymentOutcome.AlreadyPaid;

        if (order.Status == OrderStatus.Cancelled)
        {
            // The customer paid after we released the stock. Try to take it again; if it is gone, a human decides.
            if (!await TryReserveAsync(order, ct))
            {
                order.FlagForReview();
                await db.SaveChangesAsync(ct);
                logger.LogCritical("Order {Reference} was paid after cancellation and cannot be fulfilled: refund or restock", order.Reference);
                return PaymentOutcome.NeedsReview;
            }
            order.Reopen();
        }

        order.MarkPaid(Now);
        await db.SaveChangesAsync(ct);

        await email.SendAsync(order.Email, $"Order {order.Reference} confirmed", OrderEmails.Confirmation(order), ct);
        return PaymentOutcome.Paid;
    }

    /// <summary>Cancels an order that is still waiting for payment and puts its items back on the shelf.</summary>
    /// <returns>false when it was not pending (already paid, cancelled or unknown).</returns>
    public async Task<bool> CancelPendingAsync(string? token, CancellationToken ct = default)
    {
        var order = await GetByTokenAsync(token, ct);
        if (order is null || order.Status != OrderStatus.Pending) return false;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        order.Cancel();
        await ReleaseAsync(order, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    /// <summary>Cancels every order that has waited for payment longer than <paramref name="maxAge"/>.</summary>
    public async Task<int> ExpireStaleAsync(TimeSpan maxAge, CancellationToken ct = default)
    {
        var cutoff = Now - maxAge;
        var stale = (await db.Orders.Include(o => o.Items).Where(o => o.Status == OrderStatus.Pending).ToListAsync(ct))
            .Where(o => o.CreatedAt < cutoff)
            .ToList();

        foreach (var order in stale)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            order.Cancel();
            await ReleaseAsync(order, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        return stale.Count;
    }

    public async Task<Order?> ShipAsync(int id, CancellationToken ct = default)
    {
        var order = await GetAsync(id, ct);
        if (order is null) return null;

        order.Ship(Now);
        await db.SaveChangesAsync(ct);
        await email.SendAsync(order.Email, $"Order {order.Reference} is on its way", OrderEmails.Shipped(order), ct);
        return order;
    }

    private async Task ReleaseAsync(Order order, CancellationToken ct)
    {
        foreach (var item in order.Items)
        {
            var quantity = item.Quantity;
            await db.Products.Where(p => p.Id == item.ProductId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock + quantity), ct);
        }
    }

    private async Task<bool> TryReserveAsync(Order order, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        foreach (var item in order.Items)
        {
            var quantity = item.Quantity;
            var taken = await db.Products.Where(p => p.Id == item.ProductId && p.Stock >= quantity)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock - quantity), ct);
            if (taken != 1)
            {
                await transaction.RollbackAsync(ct);
                return false;
            }
        }
        await transaction.CommitAsync(ct);
        return true;
    }
}
