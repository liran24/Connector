using AiVisibility.Core.Catalog;
using AiVisibility.Core.Checks;
using AiVisibility.Core.Http;
using AiVisibility.Core.Models;

namespace AiVisibility.Core;

/// <summary>
/// Runs every check against a storefront and combines them into one score.
/// </summary>
public sealed class StoreScanner
{
    /// <summary>
    /// How much each area contributes. Crawler access and structured data dominate because
    /// they are the two things that decide whether an agent can use the store at all.
    /// </summary>
    private static readonly IReadOnlyDictionary<ScoreArea, double> Weights = new Dictionary<ScoreArea, double>
    {
        [ScoreArea.CrawlerAccess] = 0.35,
        [ScoreArea.StructuredData] = 0.35,
        [ScoreArea.ContentDepth] = 0.20,
        [ScoreArea.DiscoveryFiles] = 0.10
    };

    private readonly IPageFetcher _fetcher;
    private readonly ShopifyCatalogSource _catalog;

    public StoreScanner(IPageFetcher fetcher)
    {
        _fetcher = fetcher;
        _catalog = new ShopifyCatalogSource(fetcher);
    }

    /// <summary>
    /// Audits a store.
    /// </summary>
    /// <param name="storeUrl">Storefront root, e.g. https://example.myshopify.com.</param>
    /// <param name="productSample">
    /// How many products to inspect. A sample is enough — themes emit the same markup for
    /// every product, so the twentieth page rarely says anything the first five did not.
    /// </param>
    public async Task<ScanResult> ScanAsync(
        Uri storeUrl,
        int productSample = 10,
        CancellationToken cancellationToken = default)
    {
        // If the storefront itself will not answer, every check below is guesswork. Say so
        // once, up front, instead of emitting four separately-wrong sub-scores.
        var root = await _fetcher.GetAsync(storeUrl, cancellationToken).ConfigureAwait(false);
        if (!root.IsSuccess)
        {
            return Unreachable(storeUrl, root.Error ?? $"the storefront returned HTTP {root.StatusCode}");
        }

        var (crawlerAccess, robots) = await new CrawlerAccessCheck(_fetcher)
            .RunAsync(storeUrl, cancellationToken)
            .ConfigureAwait(false);

        var products = await _catalog
            .GetProductsAsync(storeUrl, productSample, cancellationToken)
            .ConfigureAwait(false);

        var structuredData = await new StructuredDataCheck(_fetcher)
            .RunAsync(products, cancellationToken)
            .ConfigureAwait(false);

        var contentDepth = new ContentDepthCheck().Run(products);

        var discoveryFiles = await new DiscoveryFilesCheck(_fetcher)
            .RunAsync(storeUrl, robots, cancellationToken)
            .ConfigureAwait(false);

        var areas = new[] { crawlerAccess, structuredData, contentDepth, discoveryFiles };
        var verified = areas.Where(a => !a.IsInconclusive).ToList();

        var status = verified.Count == areas.Length ? ScanStatus.Complete
            : verified.Count == 0 ? ScanStatus.Unreachable
            : ScanStatus.Partial;

        return new ScanResult(
            storeUrl,
            CombineScores(verified),
            status,
            areas,
            products.Count,
            DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Averages the areas that were actually verified, renormalising their weights so a
    /// partial scan is scored on its own terms rather than penalised for what it could not read.
    /// </summary>
    private static int? CombineScores(IReadOnlyList<AreaResult> verified)
    {
        if (verified.Count == 0)
        {
            return null;
        }

        var totalWeight = verified.Sum(area => Weights[area.Area]);
        var weighted = verified.Sum(area => area.Score!.Value * Weights[area.Area]);

        return (int)Math.Round(weighted / totalWeight);
    }

    private static ScanResult Unreachable(Uri storeUrl, string reason)
    {
        var areas = Weights.Keys
            .Select(area => AreaResult.Inconclusive(area, reason))
            .ToList();

        return new ScanResult(storeUrl, Score: null, ScanStatus.Unreachable, areas, 0, DateTimeOffset.UtcNow);
    }
}
