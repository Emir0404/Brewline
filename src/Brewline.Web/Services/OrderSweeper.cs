namespace Brewline.Web.Services;

/// <summary>
/// Every few minutes gives up on orders that were never paid, so the reserved stock goes back on sale.
/// Works for every payment provider (it does not depend on Stripe's "expired" webhook arriving).
/// </summary>
public sealed class OrderSweeper(IServiceScopeFactory scopes, ILogger<OrderSweeper> logger) : BackgroundService
{
    public static readonly TimeSpan MaxPendingAge = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var cancelled = await scope.ServiceProvider.GetRequiredService<OrderService>()
                    .ExpireStaleAsync(MaxPendingAge, stoppingToken);
                if (cancelled > 0) logger.LogInformation("Cancelled {Count} unpaid order(s) and released their stock", cancelled);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A failed sweep must never stop the app: try again next round.
                logger.LogError(ex, "Sweeping unpaid orders failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
