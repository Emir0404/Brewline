using Brewline.Web.Payments;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Brewline.Web.Controllers;

[ApiController]
public class StripeWebhookController(StripeWebhookProcessor processor, IOptions<PaymentsOptions> options) : ControllerBase
{
    /// <summary>Stripe calls this after every payment event. No anti-forgery token (Stripe is not a browser): the signature is the proof.</summary>
    [HttpPost("webhooks/stripe")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        if (!options.Value.IsStripe) return NotFound();

        // The signature is calculated over the exact bytes Stripe sent, so read the body untouched.
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync(ct);

        var result = await processor.ProcessAsync(payload, Request.Headers["Stripe-Signature"], ct);
        return result == WebhookResult.InvalidSignature ? BadRequest() : Ok();
    }
}
