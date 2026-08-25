using AiVisibility.App.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AiVisibility.App.Tests;

public class DataModelTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AiVisibilityDbContext> _options;

    public DataModelTests()
    {
        // A real SQLite engine, kept in memory: the cascade and unique index below are
        // enforced by the database rather than mocked away.
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<AiVisibilityDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var database = new AiVisibilityDbContext(_options);
        database.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private AiVisibilityDbContext NewContext() => new(_options);

    private static InstalledShop NewShop(string domain = "example.myshopify.com") => new()
    {
        Domain = domain,
        ProtectedAccessToken = "protected-token"
    };

    [Fact]
    public async Task DeletingAShopTakesItsScansWithIt()
    {
        // shop/redact promises complete erasure, and the cascade is what delivers it.
        await using (var database = NewContext())
        {
            var shop = NewShop();
            shop.Scans.Add(new StoredScan { Score = 72, ResultJson = "{}" });
            database.Shops.Add(shop);
            await database.SaveChangesAsync();
        }

        await using (var database = NewContext())
        {
            database.Shops.Remove(await database.Shops.SingleAsync());
            await database.SaveChangesAsync();
        }

        await using (var database = NewContext())
        {
            Assert.Empty(database.Shops);
            Assert.Empty(database.Scans);
        }
    }

    [Fact]
    public async Task AShopDomainCannotBeStoredTwice()
    {
        await using var database = NewContext();

        database.Shops.Add(NewShop());
        await database.SaveChangesAsync();

        database.Shops.Add(NewShop());

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => database.SaveChangesAsync());
    }

    [Fact]
    public async Task AFreshInstallIsNotYetPayingAndNotYetActive()
    {
        await using var database = NewContext();

        database.Shops.Add(NewShop());
        await database.SaveChangesAsync();

        var shop = await database.Shops.SingleAsync();

        Assert.Equal(SubscriptionStatus.None, shop.SubscriptionStatus);
        Assert.False(shop.IsActive);
    }

    [Theory]
    [InlineData(SubscriptionStatus.Trialing, true)]
    [InlineData(SubscriptionStatus.Active, true)]
    [InlineData(SubscriptionStatus.PendingApproval, false)]
    [InlineData(SubscriptionStatus.Inactive, false)]
    [InlineData(SubscriptionStatus.None, false)]
    public void OnlyTrialingAndPayingShopsGetPaidWork(SubscriptionStatus status, bool expected)
    {
        var shop = NewShop();
        shop.SubscriptionStatus = status;

        Assert.Equal(expected, shop.IsActive);
    }

    [Fact]
    public void AnUninstalledShopIsNeverActive_EvenWhileItsSubscriptionLooksAlive()
    {
        var shop = NewShop();
        shop.SubscriptionStatus = SubscriptionStatus.Active;
        shop.UninstalledAt = DateTimeOffset.UtcNow;

        Assert.False(shop.IsActive);
    }
}
