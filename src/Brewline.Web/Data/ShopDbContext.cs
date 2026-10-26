using Brewline.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace Brewline.Web.Data;

public class ShopDbContext(DbContextOptions<ShopDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Product>(e =>
        {
            e.Property(x => x.Slug).HasMaxLength(80).IsRequired();
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Category).HasMaxLength(40).IsRequired();
            e.Property(x => x.Summary).HasMaxLength(300).IsRequired();
            e.Property(x => x.Details).HasMaxLength(4000);
            e.Property(x => x.Accent).HasMaxLength(7).IsRequired();
            e.Property(x => x.Size).HasMaxLength(40);
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => new { x.IsActive, x.Category });
            e.ToTable(t => t.HasCheckConstraint("CK_Product_NonNegative", "PriceCents >= 0 AND Stock >= 0"));
        });

        builder.Entity<Cart>(e =>
        {
            e.Property(x => x.Token).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.Token).IsUnique();
            e.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.CartId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<CartItem>(e =>
        {
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.CartId, x.ProductId }).IsUnique();
        });

        builder.Entity<Order>(e =>
        {
            e.Property(x => x.Token).HasMaxLength(64).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.CustomerName).HasMaxLength(120).IsRequired();
            e.Property(x => x.Email).HasMaxLength(200).IsRequired();
            e.Property(x => x.AddressLine).HasMaxLength(200).IsRequired();
            e.Property(x => x.City).HasMaxLength(100).IsRequired();
            e.Property(x => x.PostalCode).HasMaxLength(20).IsRequired();
            e.Property(x => x.Country).HasMaxLength(60).IsRequired();
            e.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            e.Property(x => x.PaymentSessionId).HasMaxLength(200);
            e.Property(x => x.Note).HasMaxLength(500);
            e.Ignore(x => x.TotalCents);
            e.Ignore(x => x.Reference);
            e.Ignore(x => x.IsOpen);
            e.HasIndex(x => x.Token).IsUnique();
            e.HasIndex(x => x.PaymentSessionId);
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<OrderItem>(e =>
        {
            e.Property(x => x.ProductName).HasMaxLength(120).IsRequired();
            e.Ignore(x => x.LineTotalCents);
        });
    }
}
