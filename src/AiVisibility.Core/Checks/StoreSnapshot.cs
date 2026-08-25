using AiVisibility.Core.Catalog;
using AiVisibility.Core.Http;
using AiVisibility.Core.Robots;

namespace AiVisibility.Core.Checks;

/// <summary>
/// Everything gathered from a storefront in one pass, before any judgement is applied.
/// </summary>
/// <remarks>
/// Separating collection from scoring is what lets each check be a small, independent unit:
/// the robots.txt file is fetched once and read by two different checks, and no check has to
/// hand its internal state to the next one.
/// </remarks>
/// <param name="StoreUrl">Storefront root.</param>
/// <param name="Robots">Parsed rules, permissive when the file is absent or unreadable.</param>
/// <param name="RobotsFetch">The raw robots.txt response, so checks can tell 404 from 403.</param>
/// <param name="Products">Catalogue sample, empty when the storefront would not share one.</param>
public sealed record StoreSnapshot(
    Uri StoreUrl,
    RobotsTxtParser Robots,
    FetchResult RobotsFetch,
    IReadOnlyList<CatalogProduct> Products);

/// <summary>
/// Gathers a <see cref="StoreSnapshot"/>, or reports that the storefront cannot be read.
/// </summary>
public interface IStorefrontProbe
{
    /// <summary>
    /// Reads the storefront.
    /// </summary>
    /// <returns>The snapshot, or null when the store answered nothing at all.</returns>
    Task<StoreSnapshot?> CaptureAsync(Uri storeUrl, int productSample, CancellationToken cancellationToken = default);

    /// <summary>Why the last capture failed. Set only when <see cref="CaptureAsync"/> returned null.</summary>
    string? FailureReason { get; }
}

/// <summary>
/// Reads a storefront over HTTP.
/// </summary>
public sealed class StorefrontProbe : IStorefrontProbe
{
    private readonly IPageFetcher _fetcher;
    private readonly ShopifyCatalogSource _catalog;

    public string? FailureReason { get; private set; }

    public StorefrontProbe(IPageFetcher fetcher)
    {
        _fetcher = fetcher;
        _catalog = new ShopifyCatalogSource(fetcher);
    }

    public async Task<StoreSnapshot?> CaptureAsync(
        Uri storeUrl,
        int productSample,
        CancellationToken cancellationToken = default)
    {
        // If the storefront itself will not answer, every check below is guesswork. Fail once,
        // here, rather than letting four checks each invent a wrong sub-score.
        var root = await _fetcher.GetAsync(storeUrl, cancellationToken).ConfigureAwait(false);
        if (!root.IsSuccess)
        {
            FailureReason = root.Error ?? $"the storefront returned HTTP {root.StatusCode}";
            return null;
        }

        FailureReason = null;

        var robotsFetch = await _fetcher
            .GetAsync(new Uri(storeUrl, "/robots.txt"), cancellationToken)
            .ConfigureAwait(false);

        var robots = robotsFetch.IsSuccess
            ? RobotsTxtParser.Parse(robotsFetch.Body)
            : RobotsTxtParser.AllowAll();

        var products = await _catalog
            .GetProductsAsync(storeUrl, productSample, cancellationToken)
            .ConfigureAwait(false);

        return new StoreSnapshot(storeUrl, robots, robotsFetch, products);
    }
}
