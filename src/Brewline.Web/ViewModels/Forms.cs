using System.ComponentModel.DataAnnotations;
using Brewline.Web.Domain;

namespace Brewline.Web.ViewModels;

public static class Countries
{
    public static readonly IReadOnlyList<string> All =
        ["Bosnia and Herzegovina", "Croatia", "Serbia", "Montenegro", "Slovenia", "Austria", "Germany"];
}

public class CheckoutForm
{
    [Required, Display(Name = "Full name"), StringLength(120)]
    public string CustomerName { get; set; } = "";

    [Required, EmailAddress, StringLength(200)]
    public string Email { get; set; } = "";

    [Required, Display(Name = "Address"), StringLength(200)]
    public string AddressLine { get; set; } = "";

    [Required, StringLength(100)]
    public string City { get; set; } = "";

    [Required, Display(Name = "Postal code"), StringLength(20)]
    public string PostalCode { get; set; } = "";

    [Required, StringLength(60)]
    public string Country { get; set; } = Countries.All[0];

    [Display(Name = "Note for us (optional)"), StringLength(500)]
    public string? Note { get; set; }
}

public class ProductForm
{
    [Required, StringLength(120)]
    public string Name { get; set; } = "";

    [Required, StringLength(40)]
    public string Category { get; set; } = "Coffee";

    [Required, StringLength(300)]
    public string Summary { get; set; } = "";

    [StringLength(4000)]
    public string? Details { get; set; }

    [Required, Display(Name = "Price (EUR)"), Range(0.5, 100000)]
    public decimal? Price { get; set; }

    [Range(0, 100000)]
    public int Stock { get; set; }

    [Display(Name = "Visible in the shop")]
    public bool IsActive { get; set; } = true;

    [Required, Display(Name = "Picture colour"), RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Use a colour like #7c4a21.")]
    public string Accent { get; set; } = "#7c4a21";

    [StringLength(40)]
    public string? Size { get; set; }

    public static ProductForm From(Product p) => new()
    {
        Name = p.Name, Category = p.Category, Summary = p.Summary, Details = p.Details,
        Price = p.PriceCents / 100m, Stock = p.Stock, IsActive = p.IsActive, Accent = p.Accent, Size = p.Size,
    };
}

public class LoginForm
{
    [Required]
    public string Username { get; set; } = "";

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = "";
}

public class ShopPage
{
    public required IReadOnlyList<Product> Products { get; init; }
    public required IReadOnlyList<string> Categories { get; init; }
    public string? Category { get; init; }
    public string? Search { get; init; }
}
