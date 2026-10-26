using System.Security.Claims;
using Brewline.Web.Domain;
using Brewline.Web.Infrastructure;
using Brewline.Web.Services;
using Brewline.Web.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Brewline.Web.Controllers;

[Route("admin")]
public class AdminAccountController(AdminAuth auth) : Controller
{
    [HttpGet("login")]
    public IActionResult Login() => View(new LoginForm());

    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginForm form)
    {
        if (!ModelState.IsValid) return View(form);

        if (!auth.Validate(form.Username, form.Password))
        {
            // One message for "no such user" and "wrong password", and for "admin switched off".
            ModelState.AddModelError("", "Wrong username or password.");
            return View(form);
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, form.Username), new Claim(ClaimTypes.Role, "Admin")],
            CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(new ClaimsPrincipal(identity));
        return RedirectToAction("Index", "AdminOrders");
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }
}

[Authorize]
[Route("admin/orders")]
public class AdminOrdersController(OrderService orders) : Controller
{
    [HttpGet("~/admin")]
    [HttpGet("")]
    public async Task<IActionResult> Index(OrderStatus? status, CancellationToken ct)
    {
        ViewBag.Status = status;
        return View(await orders.ListAsync(status, ct));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var order = await orders.GetAsync(id, ct);
        return order is null ? NotFound() : View(order);
    }

    [HttpPost("{id:int}/ship")]
    public async Task<IActionResult> Ship(int id, CancellationToken ct)
    {
        try
        {
            if (await orders.ShipAsync(id, ct) is null) return NotFound();
            TempData["Success"] = "Marked as shipped. The customer was emailed.";
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Details), new { id });
    }
}

[Authorize]
[Route("admin/products")]
public class AdminProductsController(CatalogService catalog) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) => View(await catalog.AdminListAsync(ct));

    [HttpGet("new")]
    public IActionResult Create() => View("Form", new ProductForm());

    [HttpPost("new")]
    public async Task<IActionResult> Create(ProductForm form, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View("Form", form);

        try
        {
            await catalog.CreateAsync(ToInput(form), ct);
        }
        catch (DomainException ex)
        {
            ModelState.AddModelError("", ex.Message);
            return View("Form", form);
        }
        TempData["Success"] = "Product added.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var product = await catalog.AdminGetAsync(id, ct);
        if (product is null) return NotFound();

        ViewBag.ProductId = id;
        return View("Form", ProductForm.From(product));
    }

    [HttpPost("{id:int}")]
    public async Task<IActionResult> Edit(int id, ProductForm form, CancellationToken ct)
    {
        ViewBag.ProductId = id;
        if (!ModelState.IsValid) return View("Form", form);

        try
        {
            if (await catalog.UpdateAsync(id, ToInput(form), ct) is null) return NotFound();
        }
        catch (DomainException ex)
        {
            ModelState.AddModelError("", ex.Message);
            return View("Form", form);
        }
        TempData["Success"] = "Product saved.";
        return RedirectToAction(nameof(Index));
    }

    // Euros typed by a person become whole cents here, once, at the edge of the system.
    private static ProductInput ToInput(ProductForm f) => new(
        f.Name, f.Category, f.Summary, f.Details, (int)Math.Round(f.Price!.Value * 100m, MidpointRounding.AwayFromZero),
        f.Stock, f.IsActive, f.Accent, f.Size);
}
