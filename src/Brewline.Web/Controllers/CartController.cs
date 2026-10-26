using Brewline.Web.Domain;
using Brewline.Web.Infrastructure;
using Brewline.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Brewline.Web.Controllers;

[Route("cart")]
public class CartController(CartService cart) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(await cart.GetAsync(CartCookie.Read(Request), ct));

    [HttpPost("add")]
    public async Task<IActionResult> Add(int productId, int quantity = 1, string? returnTo = null, CancellationToken ct = default)
    {
        try
        {
            var token = await cart.AddAsync(CartCookie.Read(Request), productId, quantity, ct);
            CartCookie.Write(Response, token, Request.IsHttps);
            TempData["Success"] = "Added to your basket.";
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
            return LocalRedirect(SafeLocal(returnTo) ?? "/shop");
        }

        return LocalRedirect(SafeLocal(returnTo) ?? "/cart");
    }

    [HttpPost("update")]
    public async Task<IActionResult> Update(int productId, int quantity, CancellationToken ct)
    {
        try
        {
            await cart.SetQuantityAsync(CartCookie.Read(Request), productId, quantity, ct);
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("remove")]
    public async Task<IActionResult> Remove(int productId, CancellationToken ct)
    {
        await cart.RemoveAsync(CartCookie.Read(Request), productId, ct);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Only same-site paths, so a crafted form cannot bounce the customer to another website.</summary>
    private string? SafeLocal(string? url) => !string.IsNullOrEmpty(url) && Url.IsLocalUrl(url) ? url : null;
}
