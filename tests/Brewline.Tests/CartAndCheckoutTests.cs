using Brewline.Tests.TestSupport;
using Brewline.Web.Domain;
using Brewline.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Brewline.Tests;

public class CartServiceTests : IDisposable
{
    private readonly TestShop _t = new();
    public void Dispose() => _t.Dispose();

    [Fact]
    public async Task Adding_creates_a_cart_and_returns_its_token()
    {
        var coffee = await _t.AddProduct(priceCents: 890);

        var token = await _t.Cart.AddAsync(null, coffee.Id, 2);
        var cart = await _t.Cart.GetAsync(token);

        Assert.True(token.Length >= 20);
        var line = Assert.Single(cart.Lines);
        Assert.Equal(2, line.Quantity);
        Assert.Equal(1780, cart.SubtotalCents);
        Assert.Equal(2, cart.ItemCount);
    }

    [Fact]
    public async Task Adding_the_same_product_again_adds_to_the_quantity()
    {
        var coffee = await _t.AddProduct();
        var token = await _t.Cart.AddAsync(null, coffee.Id, 1);

        await _t.Cart.AddAsync(token, coffee.Id, 3);

        Assert.Equal(4, Assert.Single((await _t.Cart.GetAsync(token)).Lines).Quantity);
    }

    [Fact]
    public async Task Prices_always_come_from_the_database_not_from_the_basket()
    {
        var coffee = await _t.AddProduct(priceCents: 1000);
        var token = await _t.Cart.AddAsync(null, coffee.Id, 1);

        coffee.PriceCents = 1500; // the admin changes the price later
        await _t.Db.SaveChangesAsync();

        Assert.Equal(1500, (await _t.Cart.GetAsync(token)).SubtotalCents);
    }

    [Fact]
    public async Task Baskets_of_different_visitors_are_separate()
    {
        var coffee = await _t.AddProduct();
        var mine = await _t.Cart.AddAsync(null, coffee.Id, 1);
        var theirs = await _t.Cart.AddAsync(null, coffee.Id, 5);

        Assert.NotEqual(mine, theirs);
        Assert.Equal(1, (await _t.Cart.GetAsync(mine)).ItemCount);
        Assert.Equal(5, (await _t.Cart.GetAsync(theirs)).ItemCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-real-token")]
    public async Task An_unknown_or_missing_token_is_an_empty_basket(string? token)
    {
        Assert.True((await _t.Cart.GetAsync(token)).IsEmpty);
    }

    [Fact]
    public async Task More_than_the_stock_is_refused_with_a_helpful_message()
    {
        var coffee = await _t.AddProduct("Colombia Huila", stock: 3);

        var ex = await Assert.ThrowsAsync<DomainException>(() => _t.Cart.AddAsync(null, coffee.Id, 4));
        Assert.Contains("Only 3", ex.Message);

        // The limit counts what is already in the basket, too.
        var token = await _t.Cart.AddAsync(null, coffee.Id, 2);
        await Assert.ThrowsAsync<DomainException>(() => _t.Cart.AddAsync(token, coffee.Id, 2));
    }

    [Fact]
    public async Task Sold_out_and_switched_off_products_cannot_be_added()
    {
        var soldOut = await _t.AddProduct("Sold Out", stock: 0);
        var hidden = await _t.AddProduct("Hidden", active: false);

        Assert.Contains("sold out", (await Assert.ThrowsAsync<DomainException>(() => _t.Cart.AddAsync(null, soldOut.Id, 1))).Message);
        await Assert.ThrowsAsync<DomainException>(() => _t.Cart.AddAsync(null, hidden.Id, 1));
        await Assert.ThrowsAsync<DomainException>(() => _t.Cart.AddAsync(null, 999_999, 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task Quantity_must_be_positive(int quantity)
    {
        var coffee = await _t.AddProduct();

        await Assert.ThrowsAsync<DomainException>(() => _t.Cart.AddAsync(null, coffee.Id, quantity));
    }

    [Fact]
    public async Task At_most_twenty_of_one_product()
    {
        var coffee = await _t.AddProduct(stock: 100);

        await Assert.ThrowsAsync<DomainException>(() => _t.Cart.AddAsync(null, coffee.Id, Pricing.MaxQuantityPerLine + 1));
        Assert.NotNull(await _t.Cart.AddAsync(null, coffee.Id, Pricing.MaxQuantityPerLine));
    }

    [Fact]
    public async Task Setting_the_quantity_to_zero_removes_the_line_and_a_new_quantity_replaces_the_old()
    {
        var a = await _t.AddProduct("A");
        var b = await _t.AddProduct("B");
        var token = await _t.Basket((a, 1), (b, 2));

        await _t.Cart.SetQuantityAsync(token, a.Id, 5);
        await _t.Cart.SetQuantityAsync(token, b.Id, 0);

        var line = Assert.Single((await _t.Cart.GetAsync(token)).Lines);
        Assert.Equal(a.Id, line.ProductId);
        Assert.Equal(5, line.Quantity);
    }

    [Fact]
    public async Task Setting_a_quantity_above_the_stock_is_refused_and_changes_nothing()
    {
        var coffee = await _t.AddProduct(stock: 4);
        var token = await _t.Basket((coffee, 2));

        await Assert.ThrowsAsync<DomainException>(() => _t.Cart.SetQuantityAsync(token, coffee.Id, 9));

        Assert.Equal(2, (await _t.Cart.GetAsync(token)).ItemCount);
    }

    [Fact]
    public async Task Changing_someone_elses_basket_without_the_token_does_nothing()
    {
        var coffee = await _t.AddProduct();
        var token = await _t.Basket((coffee, 2));

        await _t.Cart.RemoveAsync("guess-the-token", coffee.Id);
        await _t.Cart.RemoveAsync(null, coffee.Id);

        Assert.Equal(2, (await _t.Cart.GetAsync(token)).ItemCount);
    }

    [Fact]
    public async Task The_basket_flags_items_that_became_unavailable_after_they_were_added()
    {
        var coffee = await _t.AddProduct(stock: 5);
        var token = await _t.Basket((coffee, 4));

        coffee.Stock = 2;
        await _t.Db.SaveChangesAsync();

        var cart = await _t.Cart.GetAsync(token);
        Assert.True(cart.HasProblems);
        Assert.True(cart.Lines[0].HasProblem);

        coffee.IsActive = false;
        await _t.Db.SaveChangesAsync();
        Assert.False((await _t.Cart.GetAsync(token)).Lines[0].Available);
    }

    [Fact]
    public async Task Shipping_follows_the_basket_total()
    {
        var cheap = await _t.AddProduct("Cheap", priceCents: 1000);
        var token = await _t.Basket((cheap, 2));
        Assert.Equal(490, (await _t.Cart.GetAsync(token)).Price.ShippingCents);

        await _t.Cart.SetQuantityAsync(token, cheap.Id, 5);
        Assert.Equal(0, (await _t.Cart.GetAsync(token)).Price.ShippingCents);
    }
}

public class CheckoutServiceTests : IDisposable
{
    private readonly TestShop _t = new();
    public void Dispose() => _t.Dispose();

    [Fact]
    public async Task Placing_an_order_reserves_stock_snapshots_prices_and_empties_the_basket()
    {
        var coffee = await _t.AddProduct("Sarajevo Blend", priceCents: 890, stock: 10);
        var mug = await _t.AddProduct("Mug", priceCents: 1290, stock: 5);
        var token = await _t.Basket((coffee, 3), (mug, 1));

        var order = await _t.Checkout.PlaceOrderAsync(token, TestShop.Customer());

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(3 * 890 + 1290, order.SubtotalCents);
        Assert.Equal(490, order.ShippingCents);         // 39.60 is below the 50.00 free shipping limit
        Assert.Equal(7, await _t.StockOf(coffee));
        Assert.Equal(4, await _t.StockOf(mug));
        Assert.Equal(2, order.Items.Count);
        Assert.Contains(order.Items, i => i.ProductName == "Sarajevo Blend" && i.UnitPriceCents == 890 && i.Quantity == 3);
        Assert.True((await _t.Cart.GetAsync(token)).IsEmpty);
        Assert.True(order.Token.Length >= 20);
    }

    [Fact]
    public async Task Shipping_is_added_below_the_free_shipping_limit()
    {
        var coffee = await _t.AddProduct(priceCents: 1000);
        var order = await _t.Checkout.PlaceOrderAsync(await _t.Basket((coffee, 2)), TestShop.Customer());

        Assert.Equal(2000, order.SubtotalCents);
        Assert.Equal(490, order.ShippingCents);
        Assert.Equal(2490, order.TotalCents);
    }

    [Fact]
    public async Task Later_price_changes_do_not_rewrite_an_existing_order()
    {
        var coffee = await _t.AddProduct(priceCents: 1000);
        var order = await _t.Checkout.PlaceOrderAsync(await _t.Basket((coffee, 1)), TestShop.Customer());

        coffee.PriceCents = 9999;
        coffee.Name = "Renamed";
        await _t.Db.SaveChangesAsync();

        var reloaded = await _t.Reload(order);
        Assert.Equal(1000, reloaded.Items[0].UnitPriceCents);
        Assert.Equal("Test Coffee", reloaded.Items[0].ProductName);
    }

    [Fact]
    public async Task Customer_details_are_trimmed_and_saved()
    {
        var coffee = await _t.AddProduct();

        var order = await _t.Checkout.PlaceOrderAsync(await _t.Basket((coffee, 1)),
            new CheckoutInput("  Ana  ", " ana@example.com ", " Ulica 1 ", " Sarajevo ", " 71000 ", "Bosnia and Herzegovina", "   "));

        Assert.Equal("Ana", order.CustomerName);
        Assert.Equal("ana@example.com", order.Email);
        Assert.Equal("Sarajevo", order.City);
        Assert.Null(order.Note);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown-token")]
    public async Task An_empty_or_unknown_basket_cannot_be_checked_out(string? token)
    {
        var ex = await Assert.ThrowsAsync<CheckoutException>(() => _t.Checkout.PlaceOrderAsync(token, TestShop.Customer()));

        Assert.Contains("empty", ex.Message);
    }

    [Fact]
    public async Task When_stock_ran_out_meanwhile_the_whole_order_is_refused_and_nothing_stays_reserved()
    {
        var plenty = await _t.AddProduct("Plenty", stock: 10);
        var scarce = await _t.AddProduct("Scarce", stock: 5);
        var token = await _t.Basket((plenty, 2), (scarce, 4));

        // Someone else buys most of the scarce item first.
        scarce.Stock = 1;
        await _t.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<CheckoutException>(() => _t.Checkout.PlaceOrderAsync(token, TestShop.Customer()));

        Assert.Contains("Scarce", ex.Message);
        Assert.Equal(10, await _t.StockOf(plenty));   // the first line was rolled back
        Assert.Equal(1, await _t.StockOf(scarce));
        Assert.Equal(0, await _t.Db.Orders.CountAsync());
        Assert.False((await _t.Cart.GetAsync(token)).IsEmpty); // the customer keeps the basket to fix it
    }

    [Fact]
    public async Task Two_customers_cannot_both_buy_the_last_item()
    {
        var last = await _t.AddProduct("Last One", stock: 1);
        var first = await _t.Basket((last, 1));
        var second = await _t.Basket((last, 1));

        await _t.Checkout.PlaceOrderAsync(first, TestShop.Customer("First"));
        await Assert.ThrowsAsync<CheckoutException>(() => _t.Checkout.PlaceOrderAsync(second, TestShop.Customer("Second")));

        Assert.Equal(0, await _t.StockOf(last));
        Assert.Equal(1, await _t.Db.Orders.CountAsync());
    }

    [Fact]
    public async Task A_product_switched_off_after_adding_blocks_checkout()
    {
        var coffee = await _t.AddProduct(stock: 5);
        var token = await _t.Basket((coffee, 1));
        coffee.IsActive = false;
        await _t.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<CheckoutException>(() => _t.Checkout.PlaceOrderAsync(token, TestShop.Customer()));
        Assert.Equal(5, await _t.StockOf(coffee));
    }

    [Fact]
    public async Task The_database_itself_refuses_negative_stock()
    {
        var coffee = await _t.AddProduct(stock: 1);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            _t.Db.Products.Where(p => p.Id == coffee.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, -1)));

        Assert.Contains("CK_Product_NonNegative", ex.ToString());
    }
}
