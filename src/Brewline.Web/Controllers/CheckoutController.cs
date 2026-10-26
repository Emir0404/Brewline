using Brewline.Web.Domain;
using Brewline.Web.Infrastructure;
using Brewline.Web.Payments;
using Brewline.Web.Services;
using Brewline.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace Brewline.Web.Controllers;

[Route("checkout")]
public class CheckoutController(CartService cart, CheckoutService checkout, PaymentService payments) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var view = await cart.GetAsync(CartCookie.Read(Request), ct);
        if (view.IsEmpty) return RedirectToAction("Index", "Cart");

        ViewBag.Cart = view;
        return View(new CheckoutForm());
    }

    [HttpPost("")]
    public async Task<IActionResult> Place(CheckoutForm form, CancellationToken ct)
    {
        var token = CartCookie.Read(Request);
        var view = await cart.GetAsync(token, ct);
        if (view.IsEmpty) return RedirectToAction("Index", "Cart");

        if (!Countries.All.Contains(form.Country))
            ModelState.AddModelError(nameof(form.Country), "Please choose a country from the list.");

        if (!ModelState.IsValid)
        {
            ViewBag.Cart = view;
            return View("Index", form);
        }

        Order order;
        try
        {
            order = await checkout.PlaceOrderAsync(token, new CheckoutInput(
                form.CustomerName, form.Email, form.AddressLine, form.City, form.PostalCode, form.Country, form.Note), ct);
        }
        catch (CheckoutException ex)
        {
            TempData["Error"] = ex.Message + " Please review your basket.";
            return RedirectToAction("Index", "Cart");
        }

        var redirect = await payments.TryStartAsync(order, $"{Request.Scheme}://{Request.Host}", ct);
        if (redirect is null)
        {
            TempData["Error"] = "We could not reach the payment service. Nothing was charged, please try again in a moment.";
            return RedirectToAction("Index", "Cart");
        }

        return Redirect(redirect);
    }
}
