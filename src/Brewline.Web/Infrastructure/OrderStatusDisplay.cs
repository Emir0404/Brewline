using Brewline.Web.Domain;

namespace Brewline.Web.Infrastructure;

/// <summary>How an order status is shown to people: one wording and one colour everywhere in the shop and the admin.</summary>
public static class OrderStatusDisplay
{
    public static string Label(OrderStatus status) => status switch
    {
        OrderStatus.Pending => "Waiting for payment",
        OrderStatus.Paid => "Paid",
        OrderStatus.Shipped => "Shipped",
        OrderStatus.Cancelled => "Cancelled",
        OrderStatus.NeedsReview => "Needs review",
        _ => status.ToString(),
    };

    /// <summary>Bootstrap colour name for <c>text-bg-*</c>.</summary>
    public static string Badge(OrderStatus status) => status switch
    {
        OrderStatus.Pending => "warning",
        OrderStatus.Paid => "success",
        OrderStatus.Shipped => "primary",
        OrderStatus.NeedsReview => "danger",
        _ => "secondary",
    };
}
