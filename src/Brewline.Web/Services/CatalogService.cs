using Brewline.Web.Data;
using Brewline.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace Brewline.Web.Services;

public sealed record ProductInput(
    string Name, string Category, string Summary, string? Details, int PriceCents, int Stock, bool IsActive, string Accent, string? Size);

public sealed class CatalogService(ShopDbContext db)
{
    public async Task<List<Product>> ListAsync(string? category, string? search, CancellationToken ct = default)
    {
        var query = db.Products.AsNoTracking().Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(p => p.Category == category);

        if (!string.IsNullOrWhiteSpace(search))
        {
            // % and _ typed by the visitor are searched literally, not used as wildcards.
            var like = $"%{search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            query = query.Where(p => EF.Functions.Like(p.Name, like, "\\") || EF.Functions.Like(p.Summary, like, "\\"));
        }

        return await query.OrderBy(p => p.Category).ThenBy(p => p.Name).ToListAsync(ct);
    }

    public Task<List<string>> CategoriesAsync(CancellationToken ct = default) =>
        db.Products.AsNoTracking().Where(p => p.IsActive).Select(p => p.Category).Distinct().OrderBy(c => c).ToListAsync(ct);

    public Task<Product?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Slug == slug && p.IsActive, ct);

    /// <summary>Includes switched-off products: pictures are not secret and the admin list shows them.</summary>
    public Task<Product?> FindAnyBySlugAsync(string slug, CancellationToken ct = default) =>
        db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Slug == slug, ct);

    public async Task<List<Product>> FeaturedAsync(int count, CancellationToken ct = default) =>
        (await db.Products.AsNoTracking().Where(p => p.IsActive && p.Stock > 0).ToListAsync(ct))
            .OrderByDescending(p => p.Category == "Coffee").ThenBy(p => p.PriceCents).Take(count).ToList();

    // ---- admin ----

    public async Task<List<Product>> AdminListAsync(CancellationToken ct = default) =>
        await db.Products.AsNoTracking().OrderBy(p => p.Category).ThenBy(p => p.Name).ToListAsync(ct);

    public Task<Product?> AdminGetAsync(int id, CancellationToken ct = default) =>
        db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<Product> CreateAsync(ProductInput input, CancellationToken ct = default)
    {
        Validate(input);
        var product = new Product { Slug = await UniqueSlugAsync(Tokens.Slugify(input.Name), null, ct) };
        Apply(product, input);
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        return product;
    }

    /// <summary>The slug (and therefore the product URL) never changes after creation, links stay valid.</summary>
    public async Task<Product?> UpdateAsync(int id, ProductInput input, CancellationToken ct = default)
    {
        Validate(input);
        var product = await AdminGetAsync(id, ct);
        if (product is null) return null;

        Apply(product, input);
        await db.SaveChangesAsync(ct);
        return product;
    }

    private async Task<string> UniqueSlugAsync(string baseSlug, int? ignoreId, CancellationToken ct)
    {
        var slug = baseSlug;
        for (var n = 2; await db.Products.AnyAsync(p => p.Slug == slug && p.Id != ignoreId, ct); n++)
            slug = $"{baseSlug}-{n}";
        return slug;
    }

    private static void Validate(ProductInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 120)
            throw new DomainException("Name is required (up to 120 characters).");
        if (string.IsNullOrWhiteSpace(input.Summary) || input.Summary.Length > 300)
            throw new DomainException("Summary is required (up to 300 characters).");
        if (input.PriceCents is < 50 or > 10_000_000)
            throw new DomainException("Price must be between 0.50 and 100,000.00.");
        if (input.Stock is < 0 or > 100_000)
            throw new DomainException("Stock must be between 0 and 100,000.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(input.Accent, "^#[0-9a-fA-F]{6}$"))
            throw new DomainException("Colour must look like #7c4a21.");
        if (string.IsNullOrWhiteSpace(input.Category) || input.Category.Length > 40)
            throw new DomainException("Category is required.");
    }

    private static void Apply(Product product, ProductInput input)
    {
        product.Name = input.Name.Trim();
        product.Category = input.Category.Trim();
        product.Summary = input.Summary.Trim();
        product.Details = string.IsNullOrWhiteSpace(input.Details) ? null : input.Details.Trim();
        product.PriceCents = input.PriceCents;
        product.Stock = input.Stock;
        product.IsActive = input.IsActive;
        product.Accent = input.Accent.ToLowerInvariant();
        product.Size = string.IsNullOrWhiteSpace(input.Size) ? null : input.Size.Trim();
    }
}
