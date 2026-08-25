using Microsoft.EntityFrameworkCore;

namespace AiVisibility.App.Data;

/// <summary>
/// The app's database.
/// </summary>
/// <remarks>
/// Runs on SQLite for local development and PostgreSQL in production; the provider is chosen
/// by connection string in <c>Program.cs</c> so neither the model nor any query knows which
/// one it is talking to.
/// </remarks>
public sealed class AiVisibilityDbContext : DbContext
{
    public AiVisibilityDbContext(DbContextOptions<AiVisibilityDbContext> options) : base(options)
    {
    }

    public DbSet<InstalledShop> Shops => Set<InstalledShop>();

    public DbSet<StoredScan> Scans => Set<StoredScan>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        var shop = builder.Entity<InstalledShop>();

        // One row per shop. A reinstall updates the existing row rather than adding a second,
        // which keeps the merchant's scan history across an uninstall.
        shop.HasIndex(s => s.Domain).IsUnique();

        // Deleting a shop must take its data with it — this is what shop/redact relies on.
        shop.HasMany(s => s.Scans)
            .WithOne(scan => scan.Shop!)
            .HasForeignKey(scan => scan.InstalledShopId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<StoredScan>()
            .HasIndex(scan => new { scan.InstalledShopId, scan.ScannedAt });
    }
}
