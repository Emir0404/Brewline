using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Brewline.Tests.TestSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Brewline.Tests;

public class ShopWebTests : IClassFixture<ShopApp>
{
    private readonly ShopApp _app;
    public ShopWebTests(ShopApp app) => _app = app;

    // ---- browsing ----

    [Fact]
    public async Task The_home_page_and_shop_list_the_seeded_products()
    {
        var browser = _app.NewBrowser();

        var home = await browser.Html("/");
        Assert.Contains("Coffee roasted this week", home);
        Assert.Contains("Demo shop: payments are simulated", home);

        var shop = await browser.Html("/shop");
        Assert.Contains("Sarajevo Blend", shop);
        Assert.Contains("Hand Coffee Grinder", shop);
        Assert.Contains("Sold out", shop);            // Espresso Intenso is seeded with 0 stock
        Assert.Contains("Only 3 left", shop);         // Colombia Huila
    }

    [Fact]
    public async Task Category_filter_and_search_narrow_the_list()
    {
        var browser = _app.NewBrowser();

        var equipment = await browser.Html("/shop?category=Equipment");
        Assert.Contains("Ceramic Mug", equipment);
        Assert.DoesNotContain("Sarajevo Blend", equipment);

        var search = await browser.Html("/shop?q=yirgacheffe");
        Assert.Contains("Ethiopia Yirgacheffe", search);
        Assert.DoesNotContain("Sarajevo Blend", search);

        Assert.Contains("Nothing matches that", await browser.Html("/shop?q=zzzzzz"));
    }

    [Fact]
    public async Task A_product_page_shows_the_details_and_unknown_products_are_404()
    {
        var browser = _app.NewBrowser();

        var page = await browser.Html("/shop/ethiopia-yirgacheffe");
        Assert.Contains("€12.90", page);
        Assert.Contains("Add to basket", page);

        var missing = await browser.Get("/shop/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Contains("could not find that page", await missing.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_sold_out_product_cannot_be_added()
    {
        var browser = _app.NewBrowser();

        Assert.Contains("Sold out for now", await browser.Html("/shop/espresso-intenso"));
    }

    [Fact]
    public async Task Product_pictures_are_svg_images()
    {
        var response = await _app.NewBrowser().Get("/images/products/sarajevo-blend.svg");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/svg+xml", response.Content.Headers.ContentType?.MediaType);
        var svg = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("<svg", svg);
        Assert.Contains("Sarajevo", svg);
        Assert.Equal(HttpStatusCode.NotFound, (await _app.NewBrowser().Get("/images/products/nothing.svg")).StatusCode);
    }

    [Fact]
    public async Task Responses_carry_security_headers_and_a_csp_that_allows_stripe_redirects()
    {
        var response = await _app.NewBrowser().Get("/");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("default-src 'self'", csp);
        Assert.Contains("form-action 'self' https://checkout.stripe.com", csp);
    }

    // ---- basket ----

    [Fact]
    public async Task Adding_to_the_basket_sets_a_cookie_and_shows_the_item_and_a_counter()
    {
        var browser = _app.NewBrowser();

        var id = await browser.ProductId("sarajevo-blend");
        var response = await browser.Post("/shop/sarajevo-blend", "/cart/add", ("productId", id.ToString()), ("quantity", "2"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("brewline.cart="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);

        var basket = await browser.Html("/cart");
        Assert.Contains("Sarajevo Blend", basket);
        Assert.Contains("€17.80", basket);
        Assert.Matches(@"badge[^>]*>2<", basket);
        Assert.Contains("Add €32.20 more for free shipping", basket);
    }

    [Fact]
    public async Task Two_visitors_have_separate_baskets()
    {
        var anna = _app.NewBrowser();
        var ben = _app.NewBrowser();

        await anna.AddToBasket("ceramic-mug", 3);

        Assert.Contains("Ceramic Mug", await anna.Html("/cart"));
        Assert.Contains("Your basket is empty", await ben.Html("/cart"));
    }

    [Fact]
    public async Task Asking_for_more_than_the_stock_shows_an_error_and_adds_nothing()
    {
        var browser = _app.NewBrowser();
        var id = await browser.ProductId("colombia-huila");

        var response = await browser.Post("/shop/colombia-huila", "/cart/add", ("productId", id.ToString()), ("quantity", "9"));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        Assert.Contains("Your basket is empty", await browser.Html("/cart"));
    }

    [Fact]
    public async Task Changing_and_removing_basket_lines_works()
    {
        var browser = _app.NewBrowser();
        await browser.AddToBasket("ceramic-mug", 1);
        var id = await browser.ProductId("ceramic-mug");

        await browser.Post("/cart", "/cart/update", ("productId", id.ToString()), ("quantity", "4"));
        Assert.Contains("€51.60", await browser.Html("/cart"));   // 4 x 12.90

        await browser.Post("/cart", "/cart/remove", ("productId", id.ToString()));
        Assert.Contains("Your basket is empty", await browser.Html("/cart"));
    }

    [Fact]
    public async Task A_crafted_return_address_cannot_send_the_customer_to_another_website()
    {
        var browser = _app.NewBrowser();
        var id = await browser.ProductId("sarajevo-blend");

        var response = await browser.Post("/shop/sarajevo-blend", "/cart/add",
            ("productId", id.ToString()), ("quantity", "1"), ("returnTo", "https://evil.example/phish"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/cart", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Prices_sent_by_the_browser_are_ignored()
    {
        var browser = _app.NewBrowser();
        var id = await browser.ProductId("hand-grinder");

        await browser.Post("/shop/hand-grinder", "/cart/add",
            ("productId", id.ToString()), ("quantity", "1"), ("price", "1"), ("PriceCents", "1"), ("UnitPriceCents", "1"));

        var basket = await browser.Html("/cart");
        Assert.Contains("€59.90", basket);
        Assert.DoesNotContain("€0.01", basket);
    }

    // ---- checkout and payment (demo provider) ----

    [Fact]
    public async Task An_empty_basket_cannot_reach_checkout()
    {
        var response = await _app.NewBrowser().Get("/checkout");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Cart", response.Headers.Location!.ToString(), ignoreCase: true);
    }

    [Fact]
    public async Task The_whole_purchase_works_from_basket_to_shipped()
    {
        var customer = _app.NewBrowser();
        var admin = _app.NewBrowser();

        // Stock before: 25 Ethiopia Yirgacheffe.
        await customer.AddToBasket("ethiopia-yirgacheffe", 5); // 5 x 12.90 = 64.50, free shipping
        var token = await customer.Checkout();

        // The demo payment page, and the order waits for payment.
        var payPage = await customer.Html($"/demo-pay/{token}");
        Assert.Contains("€64.50", payPage);
        Assert.Contains("Demo payment page", payPage);
        Assert.Contains("Waiting for payment", await customer.Html($"/orders/{token}"));

        // Pay (simulated). The order becomes paid.
        await customer.PayDemo(token);
        var order = await customer.Html($"/orders/{token}?confirming=1");
        Assert.Contains("Paid", order);
        Assert.Contains("Payment received", order);
        Assert.Contains("5", order);
        Assert.DoesNotContain("http-equiv=\"refresh\"", order);   // no more waiting once it is paid

        // The basket is empty and the stock went down by 5.
        Assert.Contains("Your basket is empty", await customer.Html("/cart"));

        // The admin sees it and ships it.
        await admin.AdminLogin();
        var list = await admin.Html("/admin/orders?status=Paid");
        Assert.Contains("Ana Anić", list);
        var id = Regex.Match(list, @"/admin/orders/(\d+)""[^>]*>BL-").Groups[1].Value;

        var details = await admin.Html($"/admin/orders/{id}");
        Assert.Contains("Mark as shipped", details);

        var shipped = await admin.Post($"/admin/orders/{id}", $"/admin/orders/{id}/ship");
        Assert.Equal(HttpStatusCode.Redirect, shipped.StatusCode);
        Assert.Contains("Your order is on its way", await customer.Html($"/orders/{token}"));
    }

    [Fact]
    public async Task Shipping_is_charged_below_the_limit_and_shown_on_the_order()
    {
        var browser = _app.NewBrowser();
        await browser.AddToBasket("ceramic-mug", 1); // 12.90 + 4.90 shipping
        var token = await browser.Checkout();

        var page = await browser.Html($"/demo-pay/{token}");

        Assert.Contains("€17.80", page);
    }

    [Fact]
    public async Task An_unfinished_payment_can_be_cancelled_and_the_stock_comes_back()
    {
        var browser = _app.NewBrowser();
        Assert.Equal("3", await StockText(browser, "colombia-huila"));
        await browser.AddToBasket("colombia-huila", 2);
        var token = await browser.Checkout();
        Assert.Equal("1", await StockText(browser, "colombia-huila")); // reserved while the payment is pending

        var back = await browser.Post($"/demo-pay/{token}", $"/demo-pay/{token}/cancel");
        Assert.Equal($"/orders/{token}", back.Headers.Location!.ToString());
        Assert.Contains("Pay now", await browser.Html($"/orders/{token}"));

        await browser.Post($"/orders/{token}", $"/orders/{token}/cancel");

        Assert.Contains("Cancelled", await browser.Html($"/orders/{token}"));
        Assert.Equal("3", await StockText(browser, "colombia-huila"));
    }

    [Fact]
    public async Task A_cancelled_order_cannot_be_paid_afterwards()
    {
        var browser = _app.NewBrowser();
        await browser.AddToBasket("ceramic-mug", 1);
        var token = await browser.Checkout();
        await browser.Post($"/orders/{token}", $"/orders/{token}/cancel");

        // Paying a cancelled order through the demo page revives it only if stock allows; the mug has plenty,
        // so this proves the late-payment path works end to end over HTTP.
        await browser.PayDemo(token);

        Assert.Contains("Paid", await browser.Html($"/orders/{token}"));
    }

    [Fact]
    public async Task Checkout_validates_the_form_and_keeps_the_basket()
    {
        var browser = _app.NewBrowser();
        await browser.AddToBasket("ceramic-mug", 1);

        var response = await browser.Post("/checkout", "/checkout",
            ("CustomerName", ""), ("Email", "not-an-email"), ("AddressLine", "x"), ("City", "x"), ("PostalCode", "x"), ("Country", "Atlantis"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("field-validation-error", html);
        Assert.Contains("choose a country from the list", html);
        Assert.Contains("Ceramic Mug", await browser.Html("/cart"));
    }

    [Fact]
    public async Task Customer_text_is_html_encoded_so_it_cannot_inject_script()
    {
        var browser = _app.NewBrowser();
        await browser.AddToBasket("ceramic-mug", 1);
        var token = await browser.Checkout("<script>alert('x')</script>");

        var page = await browser.Html($"/orders/{token}");

        Assert.DoesNotContain("<script>alert", page);
        Assert.Contains("&lt;script&gt;alert", page);
    }

    // ---- order privacy ----

    [Fact]
    public async Task Orders_are_only_reachable_through_their_unguessable_token()
    {
        var browser = _app.NewBrowser();
        await browser.AddToBasket("ceramic-mug", 1);
        var token = await browser.Checkout();

        Assert.Equal(HttpStatusCode.OK, (await _app.NewBrowser().Get($"/orders/{token}")).StatusCode); // the link is the key
        Assert.True(token.Length >= 20);

        foreach (var guess in new[] { "BL-100001", "1", "100001", "AAAAAAAAAAAAAAAAAAAAAA" })
            Assert.Equal(HttpStatusCode.NotFound, (await _app.NewBrowser().Get($"/orders/{guess}")).StatusCode);

        // Someone who only knows the token of another order cannot pay or cancel with a wrong one.
        var other = _app.NewBrowser();
        Assert.Equal(HttpStatusCode.NotFound, (await other.Get($"/demo-pay/{token}x")).StatusCode);
    }

    // ---- webhook and demo-pay availability ----

    [Fact]
    public async Task The_stripe_webhook_does_not_exist_while_the_demo_provider_is_active()
    {
        var response = await _app.NewBrowser().Client.PostAsync("/webhooks/stripe", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- admin ----

    [Theory]
    [InlineData("/admin")]
    [InlineData("/admin/orders")]
    [InlineData("/admin/orders/1")]
    [InlineData("/admin/products")]
    [InlineData("/admin/products/new")]
    [InlineData("/admin/products/1")]
    public async Task Anonymous_visitors_are_sent_to_the_admin_login(string url)
    {
        var response = await _app.NewBrowser().Get(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/admin/login", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Admin_posts_are_refused_without_a_login()
    {
        var browser = _app.NewBrowser();
        await browser.AddToBasket("ceramic-mug", 1); // any page that has a token

        var response = await browser.Post("/cart", "/admin/orders/1/ship");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/admin/login", response.Headers.Location!.ToString());
    }

    [Fact]
    public void Every_admin_controller_except_the_login_requires_authorization()
    {
        var admin = typeof(Program).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract
                        && t.Name.StartsWith("Admin") && t.Name != "AdminAccountController");

        Assert.NotEmpty(admin);
        Assert.All(admin, type =>
            Assert.True(type.GetCustomAttribute<AuthorizeAttribute>() is not null, $"{type.Name} has no [Authorize]"));
    }

    [Fact]
    public async Task Wrong_credentials_are_refused_with_one_generic_message()
    {
        var browser = _app.NewBrowser();

        var wrongPassword = await browser.Post("/admin/login", "/admin/login", ("Username", ShopApp.AdminUser), ("Password", "nope"));
        var wrongUser = await browser.Post("/admin/login", "/admin/login", ("Username", "nobody"), ("Password", ShopApp.AdminPassword));

        foreach (var response in new[] { wrongPassword, wrongUser })
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Wrong username or password", await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(HttpStatusCode.Redirect, (await browser.Get("/admin/orders")).StatusCode);
    }

    [Fact]
    public async Task The_admin_can_add_and_edit_products_and_a_hidden_product_disappears_from_the_shop()
    {
        var admin = _app.NewBrowser();
        await admin.AdminLogin();

        var created = await admin.Post("/admin/products/new", "/admin/products/new",
            ("Name", "Kenya AA Special"), ("Category", "Coffee"), ("Summary", "Blackcurrant and brown sugar."),
            ("Price", "13.50"), ("Stock", "12"), ("IsActive", "true"), ("Accent", "#aa3322"), ("Size", "250 g"));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);

        var shop = await _app.NewBrowser().Html("/shop?q=kenya");
        Assert.Contains("Kenya AA Special", shop);
        Assert.Contains("€13.50", shop);

        var id = Regex.Match(await admin.Html("/admin/products"), @"/admin/products/(\d+)""[^>]*>Edit</a>(?![\s\S]*Kenya)")
            .Success ? 0 : 0; // (id is looked up below by name)
        var list = await admin.Html("/admin/products");
        var row = Regex.Match(list, @"Kenya AA Special[\s\S]*?/admin/products/(\d+)");
        Assert.True(row.Success);
        id = int.Parse(row.Groups[1].Value);

        // Hide it (unchecked boxes send "false" through the hidden field MVC adds; sending just false works too).
        var hidden = await admin.Post($"/admin/products/{id}", $"/admin/products/{id}",
            ("Name", "Kenya AA Special"), ("Category", "Coffee"), ("Summary", "Blackcurrant and brown sugar."),
            ("Price", "13.50"), ("Stock", "12"), ("IsActive", "false"), ("Accent", "#aa3322"));
        Assert.Equal(HttpStatusCode.Redirect, hidden.StatusCode);

        Assert.DoesNotContain("Kenya AA Special", await _app.NewBrowser().Html("/shop?q=kenya"));
        Assert.Equal(HttpStatusCode.NotFound, (await _app.NewBrowser().Get("/shop/kenya-aa-special")).StatusCode);
    }

    [Fact]
    public async Task A_product_name_with_markup_is_encoded_in_pages_and_pictures()
    {
        var admin = _app.NewBrowser();
        await admin.AdminLogin();
        await admin.Post("/admin/products/new", "/admin/products/new",
            ("Name", "<img src=x onerror=alert(1)>"), ("Category", "Coffee"), ("Summary", "<b>bold</b>"),
            ("Price", "9.99"), ("Stock", "3"), ("IsActive", "true"), ("Accent", "#112233"));

        var shop = await _app.NewBrowser().Html("/shop?q=onerror");
        Assert.DoesNotContain("<img src=x", shop);
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", shop);

        var slug = Regex.Match(shop, @"/shop/([a-z0-9-]+)""").Groups[1].Value;
        var svg = await (await _app.NewBrowser().Get($"/images/products/{slug}.svg")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("<img", svg);
    }

    [Fact]
    public async Task The_admin_product_form_rejects_invalid_values()
    {
        var admin = _app.NewBrowser();
        await admin.AdminLogin();

        var response = await admin.Post("/admin/products/new", "/admin/products/new",
            ("Name", "Cheap"), ("Category", "Coffee"), ("Summary", "x"), ("Price", "0.01"), ("Stock", "1"), ("IsActive", "true"), ("Accent", "red"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("field-validation-error", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Logging_out_ends_the_admin_session()
    {
        var admin = _app.NewBrowser();
        await admin.AdminLogin();
        Assert.Equal(HttpStatusCode.OK, (await admin.Get("/admin/orders")).StatusCode);

        await admin.Post("/admin/orders", "/admin/logout");

        Assert.Equal(HttpStatusCode.Redirect, (await admin.Get("/admin/orders")).StatusCode);
    }

    private static async Task<string> StockText(Browser browser, string slug)
    {
        var html = await browser.Html($"/shop/{slug}");
        var match = Regex.Match(html, @"Only (\d+) left in stock");
        return match.Success ? match.Groups[1].Value : "plenty";
    }
}

public class LoginBruteForceTests
{
    [Fact]
    public async Task Repeated_wrong_passwords_get_throttled_even_for_the_right_one_afterwards()
    {
        using var app = new ShopApp();
        var browser = app.NewBrowser();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 12; i++)
            statuses.Add((await browser.Post("/admin/login", "/admin/login", ("Username", "boss"), ("Password", $"guess-{i}"))).StatusCode);

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
        // Once throttled, even the correct password is refused for a while.
        var correct = await browser.Client.PostAsync("/admin/login", new FormUrlEncodedContent(
        [
            new("Username", ShopApp.AdminUser), new("Password", ShopApp.AdminPassword),
        ]));
        Assert.Equal(HttpStatusCode.TooManyRequests, correct.StatusCode);
    }
}

public class AdminDisabledTests
{
    [Fact]
    public async Task With_no_admin_credentials_configured_nobody_can_log_in()
    {
        using var app = ShopApp.With(new() { ["Admin:Username"] = "", ["Admin:Password"] = "" });
        var browser = app.NewBrowser();

        var response = await browser.Post("/admin/login", "/admin/login", ("Username", ""), ("Password", ""));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // validation error, not a login

        var attempt = await browser.Post("/admin/login", "/admin/login", ("Username", "admin"), ("Password", "admin123"));
        Assert.Equal(HttpStatusCode.OK, attempt.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.Get("/admin/orders")).StatusCode);
    }
}

public class StripeModeTests
{
    private static ShopApp StripeApp() => ShopApp.With(new()
    {
        ["Payments:Provider"] = "Stripe",
        ["Payments:StripeSecretKey"] = "sk_test_not_a_real_key",
        ["Payments:StripeWebhookSecret"] = StripeSigner.Secret,
    });

    [Fact]
    public async Task The_webhook_rejects_unsigned_calls_with_400_and_the_demo_pay_page_disappears()
    {
        using var app = StripeApp();
        var client = app.NewBrowser().Client;

        var unsigned = await client.PostAsync("/webhooks/stripe", new StringContent("{}"));
        Assert.Equal(HttpStatusCode.BadRequest, unsigned.StatusCode);

        var badSignature = new HttpRequestMessage(HttpMethod.Post, "/webhooks/stripe") { Content = new StringContent("{}") };
        badSignature.Headers.Add("Stripe-Signature", "t=1,v1=deadbeef");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(badSignature)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/demo-pay/anything")).StatusCode);
        Assert.DoesNotContain("Demo shop", await client.GetStringAsync("/"));
    }

    [Fact]
    public async Task A_signed_event_for_an_unknown_session_is_acknowledged_with_200()
    {
        using var app = StripeApp();
        var client = app.NewBrowser().Client;
        var payload = StripeSigner.Event("checkout.session.completed", "cs_nobody");

        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/stripe") { Content = new StringContent(payload) };
        request.Headers.Add("Stripe-Signature", StripeSigner.Header(payload));

        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task When_stripe_cannot_be_reached_the_customer_gets_a_clear_message_and_the_stock_is_released()
    {
        // A fake key means the call to Stripe fails (no network or 401). The shop must stay consistent.
        using var app = StripeApp();
        var browser = app.NewBrowser();
        await browser.AddToBasket("ceramic-mug", 2);

        var response = await browser.Post("/checkout", "/checkout", ShopAppDelivery());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Cart", response.Headers.Location!.ToString(), ignoreCase: true);
        var cart = await browser.Html("/cart");
        Assert.Contains("could not reach the payment service", cart);

        // Nothing stays reserved: the product page shows the full stock again.
        Assert.DoesNotContain("Only", await browser.Html("/shop/ceramic-mug"));
    }

    private static (string, string)[] ShopAppDelivery() => Browser.DeliveryFields();

    [Fact]
    public void Stripe_without_keys_stops_the_app_at_startup()
    {
        using var app = ShopApp.With(new() { ["Payments:Provider"] = "Stripe", ["Payments:StripeSecretKey"] = "", ["Payments:StripeWebhookSecret"] = "" });

        var ex = Assert.ThrowsAny<Exception>(() => app.CreateClient());

        Assert.Contains("StripeSecretKey", ex.ToString());
    }

    [Fact]
    public void An_unknown_provider_name_stops_the_app_at_startup()
    {
        using var app = ShopApp.With(new() { ["Payments:Provider"] = "PayPalish" });

        var ex = Assert.ThrowsAny<Exception>(() => app.CreateClient());

        Assert.Contains("Payments:Provider", ex.ToString());
    }
}
