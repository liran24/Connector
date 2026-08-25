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
/// Exercises the queries the dashboard actually runs.
/// </summary>
/// <remarks>
/// These exist because of a bug that shipped past the earlier tests: SQLite refuses to
/// translate <c>ORDER BY</c> over a <see cref="DateTimeOffset"/>, so every "latest" and
/// "history" query threw at runtime. The old tests used SQLite but only ever inserted and
/// deleted rows — they never ran the ordering. Reading through the service is what closes
/// that gap.
/// </remarks>
public class ScanHistoryQueryTests : IDisposable
{
    private static readonly ShopDomain Shop = ShopDomain.Parse("example.myshopify.com");

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AiVisibilityDbContext> _options;

    public ScanHistoryQueryTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<AiVisibilityDbContext>().UseSqlite(_connection).Options;

        using var database = new AiVisibilityDbContext(_options);
        database.Database.EnsureCreated();

        var shop = new InstalledShop { Domain = Shop.Value, ProtectedAccessToken = "x" };

        // Inserted out of order on purpose: a query that happens to return insertion order
        // would otherwise look correct.
        shop.Scans.Add(NewScan(70, DateTimeOffset.UtcNow.AddDays(-2)));
        shop.Scans.Add(NewScan(90, DateTimeOffset.UtcNow));
        shop.Scans.Add(NewScan(80, DateTimeOffset.UtcNow.AddDays(-1)));

        shop.TrackingRuns.Add(NewRun(0.2, DateTimeOffset.UtcNow.AddDays(-1)));
        shop.TrackingRuns.Add(NewRun(0.6, DateTimeOffset.UtcNow));

        database.Shops.Add(shop);
        database.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    private static StoredScan NewScan(int score, DateTimeOffset at) =>
        new() { Score = score, ScannedAt = at, ResultJson = $$"""{"score":{{score}}}""" };

    private static StoredTrackingRun NewRun(double rate, DateTimeOffset at) =>
        new() { CitationRate = rate, RanAt = at, ResultJson = $$"""{"citationRate":{{rate}}}""" };

    private AiVisibilityDbContext NewContext() => new(_options);

    private ShopScanService Scans(AiVisibilityDbContext database) =>
        new(null!, database, NullLogger<ShopScanService>.Instance);

    private ShopTrackingService Tracking(AiVisibilityDbContext database) =>
        new(database,
            new UnreachableFetcher(),
            Options.Create(new TrackingOptions { Enabled = true, ApiKey = "key" }),
            NullLogger<ShopTrackingService>.Instance);

    [Fact]
    public async Task TheLatestScanIsTheMostRecentOne_NotTheLastInserted()
    {
        await using var database = NewContext();

        var latest = await Scans(database).GetLatestAsync(Shop);

        Assert.NotNull(latest);
        Assert.Equal(90, latest.Score);
    }

    [Fact]
    public async Task HistoryComesBackNewestFirst()
    {
        await using var database = NewContext();

        var history = await Scans(database).GetHistoryAsync(Shop);

        Assert.Equal(new int?[] { 90, 80, 70 }, history.Select(scan => scan.Score));
    }

    [Fact]
    public async Task HistoryRespectsItsLimit()
    {
        await using var database = NewContext();

        var history = await Scans(database).GetHistoryAsync(Shop, limit: 2);

        Assert.Equal(new int?[] { 90, 80 }, history.Select(scan => scan.Score));
    }

    [Fact]
    public async Task TheLatestTrackingRunIsTheMostRecentOne()
    {
        await using var database = NewContext();

        var latest = await Tracking(database).GetLatestAsync(Shop);

        Assert.NotNull(latest);
        Assert.Equal(0.6, latest.CitationRate);
    }

    [Fact]
    public async Task AShopWithNoScansHasNoLatest()
    {
        await using var database = NewContext();

        Assert.Null(await Scans(database).GetLatestAsync(ShopDomain.Parse("other.myshopify.com")));
        Assert.Empty(await Scans(database).GetHistoryAsync(ShopDomain.Parse("other.myshopify.com")));
    }

    [Fact]
    public async Task OneShopCannotSeeAnothersScans()
    {
        await using var database = NewContext();

        database.Shops.Add(new InstalledShop { Domain = "rival.myshopify.com", ProtectedAccessToken = "x" });
        await database.SaveChangesAsync();

        Assert.Empty(await Scans(database).GetHistoryAsync(ShopDomain.Parse("rival.myshopify.com")));
        Assert.Equal(3, (await Scans(database).GetHistoryAsync(Shop)).Count);
    }

    [Fact]
    public async Task TimestampsSurviveTheRoundTrip()
    {
        // The SQLite converter must not quietly change the instant it stores.
        var when = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

        await using (var database = NewContext())
        {
            var shop = await database.Shops.FirstAsync(s => s.Domain == Shop.Value);
            database.Scans.Add(new StoredScan { InstalledShopId = shop.Id, Score = 55, ScannedAt = when, ResultJson = "{}" });
            await database.SaveChangesAsync();
        }

        await using (var database = NewContext())
        {
            var stored = await database.Scans.FirstAsync(scan => scan.Score == 55);
            Assert.Equal(when, stored.ScannedAt);
        }
    }

    private sealed class UnreachableFetcher : IPageFetcher
    {
        public Task<FetchResult> GetAsync(Uri url, CancellationToken cancellationToken = default) =>
            Task.FromResult(FetchResult.Failed(url, "no network in tests"));
    }
}
