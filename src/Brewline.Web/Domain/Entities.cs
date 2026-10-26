namespace Brewline.Web.Domain;

public class DomainException(string message) : Exception(message);

public class Product
{
    public int Id { get; set; }

    /// <summary>URL-friendly unique name, e.g. "sarajevo-blend".</summary>
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "Coffee";
    public string Summary { get; set; } = "";
    public string? Details { get; set; }

    /// <summary>Money is always whole cents (integers): no rounding surprises, and it is what Stripe expects.</summary>
    public int PriceCents { get; set; }
    public int Stock { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Colour of the generated product illustration, e.g. "#7c4a21".</summary>
    public string Accent { get; set; } = "#7c4a21";
    public string? Size { get; set; }
}

public class Cart
{
    public int Id { get; set; }

    /// <summary>The random value stored in the visitor's cookie. Unguessable, so carts cannot be enumerated.</summary>
    public string Token { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
    public List<CartItem> Items { get; set; } = [];
}

public class CartItem
{
    public int Id { get; set; }
    public int CartId { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int Quantity { get; set; }
}

public enum OrderStatus
{
    /// <summary>Created, stock reserved, waiting for the payment.</summary>
    Pending = 0,
    Paid = 1,
    Shipped = 2,
    Cancelled = 3,

    /// <summary>Money arrived for an order we had already cancelled and could not re-stock: a human must refund or fulfil it.</summary>
    NeedsReview = 4,
}

public class Order
{
    public int Id { get; set; }

    /// <summary>The unguessable id used in customer-facing URLs (guests have no account to protect their orders).</summary>
    public string Token { get; set; } = "";

    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public DateTime CreatedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? ShippedAt { get; set; }

    public string CustomerName { get; set; } = "";
    public string Email { get; set; } = "";
    public string AddressLine { get; set; } = "";
    public string City { get; set; } = "";
    public string PostalCode { get; set; } = "";
    public string Country { get; set; } = "";

    public int SubtotalCents { get; set; }
    public int ShippingCents { get; set; }
    public int TotalCents => SubtotalCents + ShippingCents;
    public string Currency { get; set; } = "eur";

    /// <summary>Id of the checkout session at the payment provider (Stripe "cs_..." or our demo id).</summary>
    public string? PaymentSessionId { get; set; }

    public string? Note { get; set; }

    public List<OrderItem> Items { get; set; } = [];

    /// <summary>Human friendly number for emails and support: BL-100042.</summary>
    public string Reference => $"BL-{100000 + Id}";

    public bool IsOpen => Status is OrderStatus.Pending;

    /// <returns>true if the order changed, false if it was already paid (a repeated webhook must do nothing).</returns>
    public bool MarkPaid(DateTime now)
    {
        if (Status is OrderStatus.Paid or OrderStatus.Shipped) return false;
        if (Status != OrderStatus.Pending)
            throw new DomainException($"An order that is {Status} cannot be marked as paid.");

        Status = OrderStatus.Paid;
        PaidAt = now;
        return true;
    }

    public void Ship(DateTime now)
    {
        if (Status != OrderStatus.Paid)
            throw new DomainException("Only a paid order can be shipped.");

        Status = OrderStatus.Shipped;
        ShippedAt = now;
    }

    /// <summary>Used when payment arrives for an order we had already given up on (and the stock could be taken again).</summary>
    public void Reopen()
    {
        if (Status != OrderStatus.Cancelled)
            throw new DomainException("Only a cancelled order can be reopened.");

        Status = OrderStatus.Pending;
    }

    public void FlagForReview()
    {
        if (Status != OrderStatus.Cancelled)
            throw new DomainException("Only a cancelled order can be flagged for review.");

        Status = OrderStatus.NeedsReview;
    }

    public void Cancel()
    {
        if (Status != OrderStatus.Pending)
            throw new DomainException("Only an order that is still waiting for payment can be cancelled.");

        Status = OrderStatus.Cancelled;
    }
}

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int ProductId { get; set; }

    /// <summary>Name and price are copied at purchase time: a later price change must not rewrite history.</summary>
    public string ProductName { get; set; } = "";
    public int UnitPriceCents { get; set; }
    public int Quantity { get; set; }

    public int LineTotalCents => UnitPriceCents * Quantity;
}
