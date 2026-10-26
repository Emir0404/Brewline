using Brewline.Web.Domain;
using Brewline.Web.Payments;
using Brewline.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Brewline.Web.Controllers;

/// <summary>
/// The customer's view of an order. Guests have no account, so the long random token in the address
/// is the only key: it cannot be guessed, and an unknown token is simply a 404.
/// </summary>
[Route("orders/{token}")]
public class OrdersController(OrderService orders, PaymentService payments) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Details(string token, bool confirming = false, CancellationToken ct = default)
    {
        var order = await orders.GetByTokenAsync(token, ct);
        if (order is null) return NotFound();

        ViewBag.Confirming = confirming;
        return View(order);
    }

    [HttpPost("retry")]
    public async Task<IActionResult> Retry(string token, CancellationToken ct)
    {
        var order = await orders.GetByTokenAsync(token, ct);
        if (order is null) return NotFound();
        if (order.Status != OrderStatus.Pending) return RedirectToAction(nameof(Details), new { token });

        var redirect = await payments.TryStartAsync(order, $"{Request.Scheme}://{Request.Host}", ct);
        if (redirect is null)
        {
            TempData["Error"] = "We could not reach the payment service. The order was cancelled and nothing was charged.";
            return RedirectToAction(nameof(Details), new { token });
        }
        return Redirect(redirect);
    }

    [HttpPost("cancel")]
    public async Task<IActionResult> Cancel(string token, CancellationToken ct)
    {
        if (await orders.GetByTokenAsync(token, ct) is null) return NotFound();

        if (await orders.CancelPendingAsync(token, ct))
            TempData["Success"] = "Order cancelled. Nothing was charged.";
        return RedirectToAction(nameof(Details), new { token });
    }
}

/// <summary>
/// Simulated payment page, only available while Payments:Provider is "Demo".
/// Lets anybody try the full shop flow without a Stripe account and without any money moving.
/// </summary>
[Route("demo-pay/{token}")]
public class DemoPayController(OrderService orders, IOptions<PaymentsOptions> options) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string token, CancellationToken ct)
    {
        if (!options.Value.IsDemo) return NotFound();
        var order = await orders.GetByTokenAsync(token, ct);
        return order is null ? NotFound() : View(order);
    }

    [HttpPost("pay")]
    public async Task<IActionResult> Pay(string token, CancellationToken ct)
    {
        if (!options.Value.IsDemo) return NotFound();
        var order = await orders.GetByTokenAsync(token, ct);
        if (order is null) return NotFound();

        // Same call the Stripe webhook makes: there is only one way an order becomes paid.
        await orders.ConfirmPaymentAsync($"demo_{order.Token}", order.TotalCents, order.Currency, ct);
        return Redirect($"/orders/{order.Token}?confirming=1");
    }

    [HttpPost("cancel")]
    public async Task<IActionResult> Cancel(string token, CancellationToken ct)
    {
        if (!options.Value.IsDemo) return NotFound();
        if (await orders.GetByTokenAsync(token, ct) is null) return NotFound();
        return Redirect($"/orders/{token}");
    }
}
