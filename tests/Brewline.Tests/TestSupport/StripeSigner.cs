using System.Security.Cryptography;
using System.Text;

namespace Brewline.Tests.TestSupport;

/// <summary>Builds webhook calls exactly the way Stripe does, so the real signature check is tested, not mocked.</summary>
public static class StripeSigner
{
    public const string Secret = "whsec_test_secret_for_unit_tests";

    public static string Header(string payload, string secret = Secret, DateTimeOffset? at = null)
    {
        var timestamp = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        var signature = Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{payload}"))).ToLowerInvariant();
        return $"t={timestamp},v1={signature}";
    }

    public static string Event(string type, string sessionId, long amountTotal = 2490, string currency = "eur",
        string paymentStatus = "paid", string? orderToken = null, string eventId = "evt_test_1")
    {
        var metadata = orderToken is null ? "{}" : $$"""{"order_token":"{{orderToken}}"}""";
        return $$"""
        {
          "id": "{{eventId}}",
          "object": "event",
          "api_version": "2024-06-20",
          "created": 1700000000,
          "livemode": false,
          "pending_webhooks": 1,
          "request": { "id": null, "idempotency_key": null },
          "type": "{{type}}",
          "data": {
            "object": {
              "id": "{{sessionId}}",
              "object": "checkout.session",
              "amount_total": {{amountTotal}},
              "currency": "{{currency}}",
              "payment_status": "{{paymentStatus}}",
              "metadata": {{metadata}}
            }
          }
        }
        """;
    }
}
