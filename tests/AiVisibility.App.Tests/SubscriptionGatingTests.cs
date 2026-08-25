using AiVisibility.App.Configuration;
using AiVisibility.App.Data;
using AiVisibility.App.Scanning;
using AiVisibility.App.Shopify;
using AiVisibility.Core.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AiVisibility.App.Tests;

/// <summary>
/// The rules that decide whether a shop gets work that costs money, and whether tracking can
/// run at all.
/// </summary>
public class SubscriptionGatingTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AiVisibilityDbContext> _options;

    public SubscriptionGatingTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<AiVisibilityDbContext>().UseSqlite(_connection).Options;

        using var database = new AiVisibilityDbContext(_options);
        database.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private AiVisibilityDbContext NewContext() => new(_options);

    private static ShopTrackingService TrackingWith(AiVisibilityDbContext database, TrackingOptions options) =>
        new(database, new UnreachableFetcher(), Options.Create(options), NullLogger<ShopTrackingService>.Instance);

    [Fact]
    public async Task DeletingAShopTakesItsTrackingRunsWithIt()
    {
        // shop/redact promises complete erasure, and tracking runs are shop data too.
        await using (var database = NewContext())
        {
            var shop = new InstalledShop { Domain = "example.myshopify.com", ProtectedAccessToken = "x" };
            shop.TrackingRuns.Add(new StoredTrackingRun { CitationRate = 0.5, ResultJson = "{}" });
            shop.Scans.Add(new StoredScan { Score = 70, ResultJson = "{}" });
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
            Assert.Empty(database.TrackingRuns);
        }
    }

    [Theory]
    [InlineData(false, "key", false)]
    [InlineData(true, "", false)]
    [InlineData(true, "   ", false)]
    [InlineData(true, "key", true)]
    public async Task TrackingIsOnlyAvailableWhenBothEnabledAndKeyed(bool enabled, string apiKey, bool expected)
    {
        // A button that always fails is worse for a merchant than no button, so both halves
        // of the configuration must be present before the feature is offered.
        await using var database = NewContext();

        var tracking = TrackingWith(database, new TrackingOptions { Enabled = enabled, ApiKey = apiKey });

        Assert.Equal(expected, tracking.IsAvailable);
    }

    [Fact]
    public async Task RunningTrackingWithoutConfiguration_FailsWithoutSpendingAnything()
    {
        await using var database = NewContext();
        var tracking = TrackingWith(database, new TrackingOptions { Enabled = false });

        var outcome = await tracking.RunAndStoreAsync(ShopDomain.Parse("example.myshopify.com"));

        Assert.Null(outcome.Report);
        Assert.Equal(TrackingFailure.NotConfigured, outcome.Failure);
    }

    [Fact]
    public async Task TrackingAnUninstalledShop_IsRefused()
    {
        await using var database = NewContext();
        var tracking = TrackingWith(database, new TrackingOptions { Enabled = true, ApiKey = "key" });

        var outcome = await tracking.RunAndStoreAsync(ShopDomain.Parse("never-installed.myshopify.com"));

        Assert.Equal(TrackingFailure.ShopNotFound, outcome.Failure);
    }

    [Fact]
    public async Task AShopWhoseCatalogueCannotBeRead_FailsBeforeCallingTheApi()
    {
        // Reaching the model with no questions to ask would spend credit for nothing.
        await using var database = NewContext();
        database.Shops.Add(new InstalledShop { Domain = "example.myshopify.com", ProtectedAccessToken = "x" });
        await database.SaveChangesAsync();

        var tracking = TrackingWith(database, new TrackingOptions { Enabled = true, ApiKey = "key" });

        var outcome = await tracking.RunAndStoreAsync(ShopDomain.Parse("example.myshopify.com"));

        Assert.Equal(TrackingFailure.NoCatalogue, outcome.Failure);
        Assert.Empty(database.TrackingRuns);
    }

    /// <summary>A fetcher that reaches nothing, standing in for a storefront that shares no catalogue.</summary>
    private sealed class UnreachableFetcher : IPageFetcher
    {
        public Task<FetchResult> GetAsync(Uri url, CancellationToken cancellationToken = default) =>
            Task.FromResult(FetchResult.Failed(url, "no network in tests"));
    }
}
