using Brewline.Web.Data;
using Brewline.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace Brewline.Web.Services;

public sealed record CheckoutInput(
    string CustomerName, string Email, string AddressLine, string City, string PostalCode, string Country, string? Note);

/// <summary>The basket cannot be turned into an order. Carries one message per problem.</summary>
public sealed class CheckoutException(IReadOnlyList<string> problems)
    : DomainException(string.Join(" ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

public sealed class CheckoutService(ShopDbContext db, TimeProvider clock)
{
    /// <summary>
    /// Turns the basket into a pending order and reserves the stock, all or nothing.
    /// Stock is taken with a single conditional UPDATE ("only if enough is left"), so two customers
    /// buying the last item at the same moment cannot both succeed.
    /// </summary>
    public async Task<Order> PlaceOrderAsync(string? cartToken, CheckoutInput input, CancellationToken ct = default)
    {
        var cart = string.IsNullOrEmpty(cartToken)
            ? null
            : await db.Carts.Include(c => c.Items).ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(c => c.Token == cartToken, ct);

        if (cart is null || cart.Items.Count == 0)
            throw new CheckoutException(["Your basket is empty."]);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var problems = new List<string>();
        foreach (var item in cart.Items)
        {
            var product = item.Product;
            var quantity = item.Quantity;

            var taken = product.IsActive && quantity is >= 1 and <= Pricing.MaxQuantityPerLine
                && await db.Products
                    .Where(p => p.Id == product.Id && p.IsActive && p.Stock >= quantity)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock - quantity), ct) == 1;

            if (!taken)
                problems.Add(product.IsActive ? $"{product.Name} is no longer available in that quantity." : $"{product.Name} is no longer available.");
        }

        if (problems.Count > 0)
        {
            await transaction.RollbackAsync(ct); // puts back whatever was reserved for the earlier lines
            throw new CheckoutException(problems);
        }

        var price = Pricing.Calculate(cart.Items.Sum(i => i.Product.PriceCents * i.Quantity));
        var order = new Order
        {
            Token = Tokens.New(),
            Status = OrderStatus.Pending,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
            CustomerName = input.CustomerName.Trim(),
            Email = input.Email.Trim(),
            AddressLine = input.AddressLine.Trim(),
            City = input.City.Trim(),
            PostalCode = input.PostalCode.Trim(),
            Country = input.Country.Trim(),
            Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim(),
            SubtotalCents = price.SubtotalCents,
            ShippingCents = price.ShippingCents,
            Items = cart.Items.Select(i => new OrderItem
            {
                ProductId = i.ProductId,
                ProductName = i.Product.Name,
                UnitPriceCents = i.Product.PriceCents,
                Quantity = i.Quantity,
            }).ToList(),
        };

        db.Orders.Add(order);
        db.Carts.Remove(cart);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return order;
    }
}
