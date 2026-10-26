using System.Threading.RateLimiting;
using Brewline.Web.Data;
using Brewline.Web.Infrastructure;
using Brewline.Web.Payments;
using Brewline.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.WebEncoders;
using System.Text.Encodings.Web;
using System.Text.Unicode;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ShopDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Shop") ?? "Data Source=brewline.db"));

// Without this Razor writes the euro sign and letters like č, ć, š as &#x20AC; / &#x10D; (it renders, but the HTML is unreadable).
builder.Services.Configure<WebEncoderOptions>(o => o.TextEncoderSettings = new TextEncoderSettings(
    UnicodeRanges.BasicLatin, UnicodeRanges.Latin1Supplement, UnicodeRanges.LatinExtendedA,
    UnicodeRanges.CurrencySymbols, UnicodeRanges.GeneralPunctuation));

builder.Services.AddControllersWithViews(options =>
    // Every POST needs a valid anti-forgery token, no need to remember the attribute on each action.
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));

// ---- configuration (checked when the app starts, so a wrong setup fails immediately, not on the first order) ----
builder.Services.AddOptions<PaymentsOptions>()
    .Bind(builder.Configuration.GetSection(PaymentsOptions.SectionName))
    .Validate(o => o.IsDemo || o.IsStripe, "Payments:Provider must be 'Demo' or 'Stripe'.")
    .Validate(o => !o.IsStripe || (!string.IsNullOrWhiteSpace(o.StripeSecretKey) && !string.IsNullOrWhiteSpace(o.StripeWebhookSecret)),
        "Payments:StripeSecretKey and Payments:StripeWebhookSecret are required when the provider is Stripe.")
    .ValidateOnStart();
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.SectionName));

// ---- shop services ----
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<CartService>();
builder.Services.AddScoped<CheckoutService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddSingleton<IEmailSender, LoggingEmailSender>();
builder.Services.AddSingleton<AdminAuth>();

builder.Services.AddScoped<IPaymentProvider>(sp =>
    sp.GetRequiredService<IOptions<PaymentsOptions>>().Value.IsStripe
        ? ActivatorUtilities.CreateInstance<StripePaymentProvider>(sp)
        : new DemoPaymentProvider());
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<StripeWebhookProcessor>();
builder.Services.AddHostedService<OrderSweeper>();

// ---- admin login (cookie) and its brute-force protection ----
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/admin/login";
        o.AccessDeniedPath = "/admin/login";
        o.Cookie.Name = "brewline.admin";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 8, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

// In a container the default key ring would vanish on every restart and log the admin out.
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysPath)).SetApplicationName("Brewline");

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
    await db.Database.MigrateAsync();

    if (app.Configuration.GetValue("SeedCatalog", true))
        await CatalogSeed.SeedAsync(db);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

if (app.Configuration.GetValue("HttpsRedirection", true))
    app.UseHttpsRedirection();

// One fixed culture so prices and number inputs behave the same on every server.
app.UseRequestLocalization(new RequestLocalizationOptions()
    .SetDefaultCulture("en-US").AddSupportedCultures("en-US").AddSupportedUICultures("en-US"));

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    // form-action must list Stripe: after "Pay" our server redirects to checkout.stripe.com and browsers
    // check redirects of form posts against this rule too.
    headers["Content-Security-Policy"] =
        "default-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; frame-ancestors 'none'; " +
        "base-uri 'self'; form-action 'self' https://checkout.stripe.com";
    await next();
});

app.UseStatusCodePagesWithReExecute("/Home/Error", "?code={0}");
app.UseRouting();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

// Lets the integration tests start the real app in memory.
public partial class Program;
