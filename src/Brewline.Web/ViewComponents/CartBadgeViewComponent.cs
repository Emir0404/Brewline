using Brewline.Web.Infrastructure;
using Brewline.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Brewline.Web.ViewComponents;

/// <summary>The little basket counter in the top bar.</summary>
public class CartBadgeViewComponent(CartService cart) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync() =>
        View((await cart.GetAsync(CartCookie.Read(Request))).ItemCount);
}
