using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

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

    public DbSet<StoredTrackingRun> TrackingRuns => Set<StoredTrackingRun>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ConfigureTimestamps(builder);

        var shop = builder.Entity<InstalledShop>();

        // One row per shop. A reinstall updates the existing row rather than adding a second,
        // which keeps the merchant's scan history across an uninstall.
        shop.HasIndex(s => s.Domain).IsUnique();

        // Deleting a shop must take its data with it — this is what shop/redact relies on.
        shop.HasMany(s => s.Scans)
            .WithOne(scan => scan.Shop!)
            .HasForeignKey(scan => scan.InstalledShopId)
            .OnDelete(DeleteBehavior.Cascade);

        // Tracking runs go the same way as scans when a shop is erased.
        shop.HasMany(s => s.TrackingRuns)
            .WithOne(run => run.Shop!)
            .HasForeignKey(run => run.InstalledShopId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<StoredScan>()
            .HasIndex(scan => new { scan.InstalledShopId, scan.ScannedAt });

        builder.Entity<StoredTrackingRun>()
            .HasIndex(run => new { run.InstalledShopId, run.RanAt });
    }

    /// <summary>
    /// Makes <see cref="DateTimeOffset"/> columns sortable on SQLite.
    /// </summary>
    /// <remarks>
    /// SQLite has no native date type and refuses to translate <c>ORDER BY</c> over a
    /// <see cref="DateTimeOffset"/>, which throws at runtime rather than at build time —
    /// "latest scan" queries failed the moment they ran. Storing them as a sortable binary
    /// value fixes it while keeping the model's types honest.
    /// <para>
    /// Applied to SQLite only. PostgreSQL maps <see cref="DateTimeOffset"/> to
    /// <c>timestamptz</c> natively, and converting there would lose that.
    /// </para>
    /// </remarks>
    private void ConfigureTimestamps(ModelBuilder builder)
    {
        if (!Database.IsSqlite())
        {
            return;
        }

        var converter = new DateTimeOffsetToBinaryConverter();

        foreach (var property in builder.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetProperties())
            .Where(property => property.ClrType == typeof(DateTimeOffset)
                || property.ClrType == typeof(DateTimeOffset?)))
        {
            property.SetValueConverter(converter);
        }
    }
}
