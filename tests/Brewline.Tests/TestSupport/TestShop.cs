using Brewline.Web.Data;
using Brewline.Web.Domain;
using Brewline.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Brewline.Tests.TestSupport;

public sealed class FakeEmailSender : IEmailSender
{
    public List<(string To, string Subject, string Body)> Sent { get; } = [];

    public Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        Sent.Add((to, subject, body));
        return Task.CompletedTask;
    }
}

/// <summary>A real in-memory SQLite database with the real schema (including the stock >= 0 constraint) plus the services under test.</summary>
public sealed class TestShop : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public ShopDbContext Db { get; }
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero));
    public FakeEmailSender Email { get; } = new();
    public CartService Cart { get; }
    public CheckoutService Checkout { get; }
    public OrderService Orders { get; }
    public CatalogService Catalog { get; }

    public TestShop()
    {
        _connection.Open();
        Db = NewContext();
        Db.Database.EnsureCreated();

        Cart = new CartService(Db, Clock);
        Checkout = new CheckoutService(Db, Clock);
        Orders = new OrderService(Db, Clock, Email, NullLogger<OrderService>.Instance);
        Catalog = new CatalogService(Db);
    }

    /// <summary>A second context on the same database, like a second web request would have.</summary>
    public ShopDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ShopDbContext>().UseSqlite(_connection).Options);

    public async Task<Product> AddProduct(string name = "Test Coffee", int priceCents = 1000, int stock = 10, bool active = true)
    {
        var product = new Product
        {
            Slug = Tokens.Slugify(name) + "-" + Guid.NewGuid().ToString("N")[..4],
            Name = name, Summary = "Summary", PriceCents = priceCents, Stock = stock, IsActive = active,
        };
        Db.Products.Add(product);
        await Db.SaveChangesAsync();
        return product;
    }

    public static CheckoutInput Customer(string name = "Ana Anić") =>
        new(name, "ana@example.com", "Ulica 1", "Sarajevo", "71000", "Bosnia and Herzegovina", null);

    /// <summary>Puts the given quantity in a fresh basket and returns its token.</summary>
    public async Task<string> Basket(params (Product Product, int Quantity)[] items)
    {
        string? token = null;
        foreach (var (product, quantity) in items)
            token = await Cart.AddAsync(token, product.Id, quantity);
        return token!;
    }

    /// <summary>Creates a pending order with a payment session id attached, like a started payment.</summary>
    public async Task<Order> PendingOrder(Product product, int quantity = 1, string sessionId = "cs_test_1")
    {
        var order = await Checkout.PlaceOrderAsync(await Basket((product, quantity)), Customer());
        await Orders.AttachPaymentSessionAsync(order.Id, sessionId);
        return (await Orders.GetAsync(order.Id))!;
    }

    public async Task<int> StockOf(Product product)
    {
        using var fresh = NewContext();
        return (await fresh.Products.AsNoTracking().FirstAsync(p => p.Id == product.Id)).Stock;
    }

    public async Task<Order> Reload(Order order)
    {
        using var fresh = NewContext();
        return await fresh.Orders.AsNoTracking().Include(o => o.Items).FirstAsync(o => o.Id == order.Id);
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}
