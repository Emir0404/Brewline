using System.Globalization;

namespace Brewline.Web.Domain;

public sealed record PriceBreakdown(int SubtotalCents, int ShippingCents)
{
    public int TotalCents => SubtotalCents + ShippingCents;
}

public static class Pricing
{
    public const int FreeShippingFromCents = 5000;
    public const int FlatShippingCents = 490;
    public const int MaxQuantityPerLine = 20;

    /// <summary>Flat shipping, free for orders from 50.00. An empty basket costs nothing.</summary>
    public static PriceBreakdown Calculate(int subtotalCents)
    {
        if (subtotalCents < 0) throw new ArgumentOutOfRangeException(nameof(subtotalCents));
        var shipping = subtotalCents is 0 || subtotalCents >= FreeShippingFromCents ? 0 : FlatShippingCents;
        return new PriceBreakdown(subtotalCents, shipping);
    }

    /// <summary>How much more the customer must add to get free shipping (0 when already free or empty).</summary>
    public static int AwayFromFreeShipping(int subtotalCents) =>
        subtotalCents is 0 || subtotalCents >= FreeShippingFromCents ? 0 : FreeShippingFromCents - subtotalCents;

    public static string Format(int cents, string currency = "eur")
    {
        var amount = (cents / 100m).ToString("N2", CultureInfo.GetCultureInfo("en-US"));
        return currency.ToLowerInvariant() switch
        {
            "eur" => $"€{amount}",
            "usd" => $"${amount}",
            var other => $"{amount} {other.ToUpperInvariant()}",
        };
    }
}
