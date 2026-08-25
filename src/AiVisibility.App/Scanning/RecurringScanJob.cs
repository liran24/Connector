using AiVisibility.App.Data;
using AiVisibility.App.Shopify;
using Microsoft.EntityFrameworkCore;

namespace AiVisibility.App.Scanning;

/// <summary>
/// Rescans every paying shop on a schedule, so a merchant's trend line keeps moving without
/// anyone opening the dashboard.
/// </summary>
/// <remarks>
/// The recurring value of the subscription depends on this: a merchant who has to remember to
/// press a button will not renew. Registered with Hangfire in <c>Program.cs</c>.
/// </remarks>
public sealed class RecurringScanJob
{
    /// <summary>Identifier Hangfire uses to keep a single schedule rather than stacking duplicates.</summary>
    public const string JobId = "rescan-active-shops";

    private readonly AiVisibilityDbContext _database;
    private readonly IShopScanService _scans;
    private readonly ILogger<RecurringScanJob> _logger;

    public RecurringScanJob(AiVisibilityDbContext database, IShopScanService scans, ILogger<RecurringScanJob> logger)
    {
        _database = database;
        _scans = scans;
        _logger = logger;
    }

    /// <summary>
    /// Scans every shop that is installed and paying.
    /// </summary>
    /// <remarks>
    /// Sequential on purpose. Each scan makes a dozen requests to a storefront, and running
    /// every shop at once would look like a burst of traffic to the merchants being scanned.
    /// One shop failing must not stop the rest, so failures are logged and the loop continues.
    /// </remarks>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var domains = await _database.Shops
            .Where(shop => shop.UninstalledAt == null
                && (shop.SubscriptionStatus == SubscriptionStatus.Active
                    || shop.SubscriptionStatus == SubscriptionStatus.Trialing))
            .Select(shop => shop.Domain)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation("Rescanning {Count} active shop(s).", domains.Count);

        foreach (var domain in domains)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!ShopDomain.TryParse(domain, out var shop))
            {
                _logger.LogError("Stored domain '{Domain}' is not a valid shop domain; skipping.", domain);
                continue;
            }

            try
            {
                await _scans.ScanAndStoreAsync(shop, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Rescan of {Shop} failed; continuing with the rest.", shop.Value);
            }
        }
    }
}
