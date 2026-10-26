using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Brewline.Tests.TestSupport;

/// <summary>The real shop, in memory, on its own temporary SQLite file.</summary>
public sealed class ShopApp : WebApplicationFactory<Program>
{
    public const string AdminUser = "boss";
    public const string AdminPassword = "correct horse battery staple";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"brewline-test-{Guid.NewGuid():N}.db");
    private readonly Dictionary<string, string?> _extra;

    /// <summary>The default shop (used as a shared xUnit fixture).</summary>
    public ShopApp() : this([]) { }

    private ShopApp(Dictionary<string, string?> extra) => _extra = extra;

    /// <summary>A shop with some settings changed, e.g. a different payment provider.</summary>
    public static ShopApp With(Dictionary<string, string?> extra) => new(extra);

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Shop"] = $"DataSource={_dbPath}",
                ["Payments:Provider"] = "Demo",
                ["Admin:Username"] = AdminUser,
                ["Admin:Password"] = AdminPassword,
                ["HttpsRedirection"] = "false",
            };
            foreach (var (key, value) in _extra) settings[key] = value;
            config.AddInMemoryCollection(settings);
        });

    /// <summary>A "browser": its own cookie jar, does not follow redirects so tests can inspect them.</summary>
    public Browser NewBrowser() => new(CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }));

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            try { File.Delete(file); } catch (IOException) { /* best effort */ }
    }
}

public sealed partial class Browser(HttpClient client)
{
    public HttpClient Client { get; } = client;

    public Task<HttpResponseMessage> Get(string url) => Client.GetAsync(url);

    public async Task<string> Html(string url)
    {
        var response = await Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>Loads a page containing the form to get its anti-forgery token, then posts like a browser would.</summary>
    public async Task<HttpResponseMessage> Post(string pageWithForm, string postUrl, params (string Key, string Value)[] fields)
    {
        var token = await Token(pageWithForm);
        var all = fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value))
            .Append(new("__RequestVerificationToken", token));
        return await Client.PostAsync(postUrl, new FormUrlEncodedContent(all));
    }

    public async Task<string> Token(string pageWithForm)
    {
        var html = await Html(pageWithForm);
        var match = TokenRegex().Match(html);
        Assert.True(match.Success, $"No anti-forgery token on {pageWithForm}");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    public async Task AdminLogin()
    {
        var response = await Post("/admin/login", "/admin/login",
            ("Username", ShopApp.AdminUser), ("Password", ShopApp.AdminPassword));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    /// <summary>Finds a product id by its slug on the public shop page (the "add to basket" form carries it).</summary>
    public async Task<int> ProductId(string slug)
    {
        var html = await Html($"/shop/{slug}");
        var match = Regex.Match(html, @"name=""productId"" value=""(\d+)""");
        Assert.True(match.Success, $"Could not find product id for {slug}");
        return int.Parse(match.Groups[1].Value);
    }

    public async Task AddToBasket(string slug, int quantity = 1)
    {
        var id = await ProductId(slug);
        var response = await Post($"/shop/{slug}", "/cart/add", ("productId", id.ToString()), ("quantity", quantity.ToString()));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    public static (string, string)[] DeliveryFields(string name = "Ana Anić") =>
    [
        ("CustomerName", name), ("Email", "ana@example.com"), ("AddressLine", "Ulica 1"),
        ("City", "Sarajevo"), ("PostalCode", "71000"), ("Country", "Bosnia and Herzegovina"),
    ];

    /// <summary>Checks out the current basket and returns the order token from the payment page address.</summary>
    public async Task<string> Checkout(string name = "Ana Anić")
    {
        var response = await Post("/checkout", "/checkout", DeliveryFields(name));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        var match = Regex.Match(location, @"/demo-pay/([A-Za-z0-9_-]+)");
        Assert.True(match.Success, $"Expected the demo payment page, got {location}");
        return match.Groups[1].Value;
    }

    public async Task PayDemo(string token)
    {
        var response = await Post($"/demo-pay/{token}", $"/demo-pay/{token}/pay");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex TokenRegex();
}
