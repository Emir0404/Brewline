using System.Diagnostics;
using Brewline.Web.Models;
using Brewline.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Brewline.Web.Controllers;

public class HomeController(CatalogService catalog) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(await catalog.FeaturedAsync(4, ct));

    // Error pages are shown for failed POSTs too (the pipeline re-executes with the original method),
    // and a 429 or 404 must not turn into a 400 just because that re-executed request has no token.
    [IgnoreAntiforgeryToken]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error(int? code)
    {
        if (code is not null)
            Response.StatusCode = code.Value;

        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier, Code = code });
    }
}
