namespace Brewline.Web.Infrastructure;

/// <summary>The cookie that remembers which basket belongs to the visitor.</summary>
public static class CartCookie
{
    public const string Name = "brewline.cart";

    public static string? Read(HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out var value) && value.Length is > 10 and < 64 ? value : null;

    public static void Write(HttpResponse response, string token, bool secure) =>
        response.Cookies.Append(Name, token, new CookieOptions
        {
            HttpOnly = true,       // scripts cannot read it
            SameSite = SameSiteMode.Lax,
            Secure = secure,
            IsEssential = true,
            MaxAge = TimeSpan.FromDays(60),
        });
}
