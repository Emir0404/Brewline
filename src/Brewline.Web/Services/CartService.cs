using Brewline.Web.Data;
using Brewline.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace Brewline.Web.Services;

public sealed record CartLine(
    int ProductId, string Slug, string Name, string Accent, int UnitPriceCents, int Quantity, int Stock, bool Available)
{
    public int LineTotalCents => UnitPriceCents * Quantity;

    /// <summary>The customer wants more than we have, or the product was switched off after they added it.</summary>
    public bool HasProblem => !Available || Quantity > Stock;
}

public sealed record CartView(IReadOnlyList<CartLine> Lines)
{
    public static readonly CartView Empty = new([]);

    public int ItemCount => Lines.Sum(l => l.Quantity);
    public int SubtotalCents => Lines.Sum(l => l.LineTotalCents);
    public PriceBreakdown Price => Pricing.Calculate(SubtotalCents);
    public bool IsEmpty => Lines.Count == 0;
    public bool HasProblems => Lines.Any(l => l.HasProblem);
}

/// <summary>
/// The basket of an anonymous visitor, stored in the database and found through a random cookie value.
/// Prices always come from the products table, never from anything the browser sends.
/// </summary>
public sealed class CartService(ShopDbContext db, TimeProvider clock)
{
    public async Task<CartView> GetAsync(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(token)) return CartView.Empty;

        var items = await db.CartItems.AsNoTracking()
            .Where(i => db.Carts.Any(c => c.Id == i.CartId && c.Token == token))
            .Include(i => i.Product)
            .OrderBy(i => i.Id)
            .ToListAsync(ct);

        return new CartView(items.Select(i => new CartLine(
            i.ProductId, i.Product.Slug, i.Product.Name, i.Product.Accent, i.Product.PriceCents,
            i.Quantity, i.Product.Stock, i.Product.IsActive)).ToList());
    }

    /// <returns>The cart token to store in the cookie (new when the visitor had none).</returns>
    public async Task<string> AddAsync(string? token, int productId, int quantity, CancellationToken ct = default)
    {
        if (quantity < 1) throw new DomainException("Quantity must be at least 1.");

        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId && p.IsActive, ct)
            ?? throw new DomainException("This product is not available.");

        var cart = await FindAsync(token, ct) ?? await CreateAsync(ct);
        var item = cart.Items.FirstOrDefault(i => i.ProductId == productId);

        var wanted = (item?.Quantity ?? 0) + quantity;
        EnsureAvailable(product, wanted);

        if (item is null) cart.Items.Add(new CartItem { ProductId = productId, Quantity = wanted });
        else item.Quantity = wanted;

        cart.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return cart.Token;
    }

    /// <summary>Sets the exact quantity. Zero removes the line.</summary>
    public async Task SetQuantityAsync(string? token, int productId, int quantity, CancellationToken ct = default)
    {
        var cart = await FindAsync(token, ct);
        var item = cart?.Items.FirstOrDefault(i => i.ProductId == productId);
        if (cart is null || item is null) return;

        if (quantity <= 0)
        {
            db.CartItems.Remove(item);
        }
        else
        {
            var product = await db.Products.AsNoTracking().FirstAsync(p => p.Id == productId, ct);
            EnsureAvailable(product, quantity);
            item.Quantity = quantity;
        }

        cart.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
    }

    public Task RemoveAsync(string? token, int productId, CancellationToken ct = default) =>
        SetQuantityAsync(token, productId, 0, ct);

    private static void EnsureAvailable(Product product, int wanted)
    {
        if (wanted > Pricing.MaxQuantityPerLine)
            throw new DomainException($"You can order at most {Pricing.MaxQuantityPerLine} of one product.");
        if (wanted > product.Stock)
            throw new DomainException(product.Stock == 0
                ? $"{product.Name} is sold out."
                : $"Only {product.Stock} of {product.Name} left in stock.");
    }

    private Task<Cart?> FindAsync(string? token, CancellationToken ct) =>
        string.IsNullOrEmpty(token)
            ? Task.FromResult<Cart?>(null)
            : db.Carts.Include(c => c.Items).FirstOrDefaultAsync(c => c.Token == token, ct);

    private async Task<Cart> CreateAsync(CancellationToken ct)
    {
        var cart = new Cart { Token = Tokens.New(), UpdatedAt = clock.GetUtcNow().UtcDateTime };
        db.Carts.Add(cart);
        await db.SaveChangesAsync(ct);
        return cart;
    }
}
