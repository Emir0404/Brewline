using Brewline.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace Brewline.Web.Data;

/// <summary>A small demo catalogue (made-up products) so the shop is not empty on first run.</summary>
public static class CatalogSeed
{
    public static async Task SeedAsync(ShopDbContext db)
    {
        if (await db.Products.AnyAsync()) return;

        db.Products.AddRange(
            Coffee("sarajevo-blend", "Sarajevo Blend", 890, 40, "#7c4a21", "250 g",
                "A balanced everyday roast with notes of cocoa and toasted hazelnut.",
                "Our house blend: Brazilian and Colombian beans roasted medium. Smooth, low acidity, forgiving in any brewer. Roasted weekly in small batches."),
            Coffee("ethiopia-yirgacheffe", "Ethiopia Yirgacheffe", 1290, 25, "#b5532b", "250 g",
                "Bright and floral, with lemon, jasmine and a tea-like finish.",
                "A washed single origin from the Yirgacheffe region. Light roast that shines as a pour-over or filter coffee."),
            Coffee("colombia-huila", "Colombia Huila", 1190, 3, "#c28a2f", "250 g",
                "Sweet and round: red apple, caramel and milk chocolate.",
                "Grown at 1,700 m in Huila. A crowd pleaser that works as espresso and as filter."),
            Coffee("brazil-santos-decaf", "Brazil Santos Decaf", 1090, 18, "#5b6b3a", "250 g",
                "Decaffeinated with spring water. Nutty, mellow, no jitters.",
                "Swiss-water process decaf, so you keep the flavour and lose the caffeine."),
            Coffee("espresso-intenso", "Espresso Intenso", 1050, 0, "#3b2418", "250 g",
                "Dark, syrupy and bold. Made for milk drinks and moka pots.",
                "A dark roast blend with Robusta for a thick crema. Currently sold out, back next week."),
            Gear("pour-over-dripper", "Ceramic Pour-over Dripper", 2490, 15, "#2f4858", "Size 02",
                "A classic cone dripper for clean, bright filter coffee.",
                "Hand-glazed ceramic. Fits most mugs and carafes. Takes size 02 paper filters."),
            Gear("hand-grinder", "Hand Coffee Grinder", 5990, 8, "#44556b", "Steel burrs",
                "Stainless steel burrs and 18 click settings, from espresso to French press.",
                "Grind fresh for every cup. Quiet, portable and built to last."),
            Gear("ceramic-mug", "Brewline Ceramic Mug", 1290, 30, "#a3a3a3", "300 ml",
                "A sturdy 300 ml mug that keeps its heat.",
                "Dishwasher and microwave safe."));

        await db.SaveChangesAsync();
    }

    private static Product Coffee(string slug, string name, int price, int stock, string accent, string size, string summary, string details) =>
        new() { Slug = slug, Name = name, Category = "Coffee", PriceCents = price, Stock = stock, Accent = accent, Size = size, Summary = summary, Details = details };

    private static Product Gear(string slug, string name, int price, int stock, string accent, string size, string summary, string details) =>
        new() { Slug = slug, Name = name, Category = "Equipment", PriceCents = price, Stock = stock, Accent = accent, Size = size, Summary = summary, Details = details };
}
