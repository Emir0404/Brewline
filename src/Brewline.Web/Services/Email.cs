using System.Text;
using Brewline.Web.Domain;

namespace Brewline.Web.Services;

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct = default);
}

/// <summary>
/// Writes emails to the log instead of sending them, so the demo needs no mail server.
/// To send real mail, implement <see cref="IEmailSender"/> (SMTP, SendGrid, ...) and register it instead.
/// </summary>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        logger.LogInformation("EMAIL to {To}: {Subject}\n{Body}", to, subject, body);
        return Task.CompletedTask;
    }
}

public static class OrderEmails
{
    public static string Confirmation(Order order)
    {
        var text = new StringBuilder();
        text.AppendLine($"Hi {order.CustomerName},");
        text.AppendLine();
        text.AppendLine($"Thank you for your order {order.Reference}. We have received your payment.");
        text.AppendLine();
        foreach (var item in order.Items)
            text.AppendLine($"  {item.Quantity} x {item.ProductName}  {Pricing.Format(item.LineTotalCents, order.Currency)}");
        text.AppendLine();
        text.AppendLine($"Shipping: {Pricing.Format(order.ShippingCents, order.Currency)}");
        text.AppendLine($"Total:    {Pricing.Format(order.TotalCents, order.Currency)}");
        text.AppendLine();
        text.AppendLine($"We will ship to: {order.AddressLine}, {order.PostalCode} {order.City}, {order.Country}.");
        text.AppendLine();
        text.AppendLine("Brewline Coffee Roasters");
        return text.ToString();
    }

    public static string Shipped(Order order) =>
        $"Hi {order.CustomerName},\n\nGood news: order {order.Reference} has been shipped to {order.City}.\n\nBrewline Coffee Roasters\n";
}
