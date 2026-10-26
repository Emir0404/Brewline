using Brewline.Web.Infrastructure;
using Brewline.Web.Services;
using Brewline.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace Brewline.Web.Controllers;

public class ShopController(CatalogService catalog) : Controller
{
    [HttpGet("shop")]
    public async Task<IActionResult> Index(string? category, string? q, CancellationToken ct) =>
        View(new ShopPage
        {
            Products = await catalog.ListAsync(category, q, ct),
            Categories = await catalog.CategoriesAsync(ct),
            Category = category,
            Search = q,
        });

    [HttpGet("shop/{slug}")]
    public async Task<IActionResult> Details(string slug, CancellationToken ct)
    {
        var product = await catalog.GetBySlugAsync(slug, ct);
        return product is null ? NotFound() : View(product);
    }

    /// <summary>The generated product picture (SVG).</summary>
    [HttpGet("images/products/{slug}.svg")]
    [ResponseCache(Duration = 3600)]
    public async Task<IActionResult> Image(string slug, CancellationToken ct)
    {
        var product = await catalog.FindAnyBySlugAsync(slug, ct);
        return product is null ? NotFound() : Content(ProductImage.Render(product), "image/svg+xml");
    }
}
