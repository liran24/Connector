using System.Text.Json;
using AiVisibility.App.Data;
using AiVisibility.App.Shopify;
using AiVisibility.Core;
using AiVisibility.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace AiVisibility.App.Scanning;

/// <summary>
/// Scans a shop's storefront and keeps the result.
/// </summary>
/// <remarks>
/// The bridge between the scanning engine, which knows nothing about tenants, and the
/// database, which knows nothing about scoring.
/// </remarks>
public interface IShopScanService
{
    /// <summary>Scans a shop now and stores the result.</summary>
    /// <returns>The scan, or null when the shop is not installed.</returns>
    Task<ScanResult?> ScanAndStoreAsync(ShopDomain shop, CancellationToken cancellationToken = default);

    /// <summary>The most recent stored scan, or null when the shop has never been scanned.</summary>
    Task<StoredScan?> GetLatestAsync(ShopDomain shop, CancellationToken cancellationToken = default);

    /// <summary>Scores over time, newest first, for the dashboard's trend line.</summary>
    Task<IReadOnlyList<StoredScan>> GetHistoryAsync(ShopDomain shop, int limit = 30, CancellationToken cancellationToken = default);
}

/// <summary>
/// Runs scans against the storefront and records them against the tenant.
/// </summary>
public sealed class ShopScanService : IShopScanService
{
    /// <summary>
    /// Stored reports are read back by the dashboard, so property names stay camelCase to
    /// match what the TypeScript client expects.
    /// </summary>
    public static readonly JsonSerializerOptions ReportJson = new(JsonSerializerDefaults.Web);

    private readonly StoreScanner _scanner;
    private readonly AiVisibilityDbContext _database;
    private readonly ILogger<ShopScanService> _logger;

    public ShopScanService(StoreScanner scanner, AiVisibilityDbContext database, ILogger<ShopScanService> logger)
    {
        _scanner = scanner;
        _database = database;
        _logger = logger;
    }

    public async Task<ScanResult?> ScanAndStoreAsync(ShopDomain shop, CancellationToken cancellationToken = default)
    {
        var installed = await FindShopAsync(shop, cancellationToken).ConfigureAwait(false);
        if (installed is null)
        {
            _logger.LogWarning("Asked to scan {Shop}, which is not installed.", shop.Value);
            return null;
        }

        var result = await _scanner
            .ScanAsync(shop.StorefrontUrl, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        _database.Scans.Add(new StoredScan
        {
            InstalledShopId = installed.Id,
            Score = result.Score,
            ScannedAt = result.ScannedAt,

            // The whole report is stored, not just the score, so a report a merchant already
            // saw does not change meaning when the scoring model does.
            ResultJson = JsonSerializer.Serialize(ScanReport.From(result), ReportJson)
        });

        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Scanned {Shop}: {Score}.", shop.Value, result.Score?.ToString() ?? "unscored");
        return result;
    }

    public async Task<StoredScan?> GetLatestAsync(ShopDomain shop, CancellationToken cancellationToken = default) =>
        await ScansFor(shop)
            .OrderByDescending(scan => scan.ScannedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<StoredScan>> GetHistoryAsync(
        ShopDomain shop,
        int limit = 30,
        CancellationToken cancellationToken = default) =>
        await ScansFor(shop)
            .OrderByDescending(scan => scan.ScannedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    private IQueryable<StoredScan> ScansFor(ShopDomain shop) =>
        _database.Scans.Where(scan => scan.Shop!.Domain == shop.Value);

    private Task<InstalledShop?> FindShopAsync(ShopDomain shop, CancellationToken cancellationToken) =>
        _database.Shops.FirstOrDefaultAsync(s => s.Domain == shop.Value && s.UninstalledAt == null, cancellationToken);
}
