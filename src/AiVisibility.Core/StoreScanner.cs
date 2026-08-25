using AiVisibility.Core.Checks;
using AiVisibility.Core.Http;
using AiVisibility.Core.Models;

namespace AiVisibility.Core;

/// <summary>
/// How much each area contributes to the headline score.
/// </summary>
/// <remarks>
/// Crawler access and structured data dominate because they are the two things that decide
/// whether an agent can use the store at all.
/// </remarks>
public sealed class AreaWeights
{
    private static readonly IReadOnlyDictionary<ScoreArea, double> Defaults = new Dictionary<ScoreArea, double>
    {
        [ScoreArea.CrawlerAccess] = 0.35,
        [ScoreArea.StructuredData] = 0.35,
        [ScoreArea.ContentDepth] = 0.20,
        [ScoreArea.DiscoveryFiles] = 0.10
    };

    private readonly IReadOnlyDictionary<ScoreArea, double> _weights;

    public AreaWeights(IReadOnlyDictionary<ScoreArea, double>? weights = null) => _weights = weights ?? Defaults;

    /// <summary>An area with no configured weight still counts, just modestly.</summary>
    public double For(ScoreArea area) => _weights.GetValueOrDefault(area, 0.1);

    /// <summary>
    /// Averages the verified areas, renormalising their weights so a partial scan is scored on
    /// its own terms rather than penalised for what could not be read.
    /// </summary>
    public int? Combine(IReadOnlyList<AreaResult> verified)
    {
        if (verified.Count == 0)
        {
            return null;
        }

        var totalWeight = verified.Sum(area => For(area.Area));
        var weighted = verified.Sum(area => area.Score!.Value * For(area.Area));

        return (int)Math.Round(weighted / totalWeight);
    }
}

/// <summary>
/// Reads a storefront once, runs every registered check against it, and combines the results.
/// </summary>
public sealed class StoreScanner
{
    private readonly IStorefrontProbe _probe;
    private readonly IReadOnlyList<ICheck> _checks;
    private readonly AreaWeights _weights;

    public StoreScanner(IStorefrontProbe probe, IEnumerable<ICheck> checks, AreaWeights? weights = null)
    {
        _probe = probe;
        _checks = checks.ToList();
        _weights = weights ?? new AreaWeights();
    }

    /// <summary>Builds a scanner with the standard checks, for callers not using a DI container.</summary>
    public static StoreScanner CreateDefault(IPageFetcher fetcher) => new(
        new StorefrontProbe(fetcher),
        new ICheck[]
        {
            new CrawlerAccessCheck(),
            new StructuredDataCheck(fetcher),
            new ContentDepthCheck(),
            new DiscoveryFilesCheck(fetcher)
        });

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
        var snapshot = await _probe.CaptureAsync(storeUrl, productSample, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            return Unreachable(storeUrl, _probe.FailureReason ?? "the storefront could not be read");
        }

        var areas = new List<AreaResult>();
        foreach (var check in _checks)
        {
            areas.Add(await check.RunAsync(snapshot, cancellationToken).ConfigureAwait(false));
        }

        var verified = areas.Where(a => !a.IsInconclusive).ToList();

        var status = verified.Count == areas.Count ? ScanStatus.Complete
            : verified.Count == 0 ? ScanStatus.Unreachable
            : ScanStatus.Partial;

        return new ScanResult(
            storeUrl,
            _weights.Combine(verified),
            status,
            areas,
            snapshot.Products.Count,
            DateTimeOffset.UtcNow);
    }

    private ScanResult Unreachable(Uri storeUrl, string reason)
    {
        var areas = _checks
            .Select(check => AreaResult.Inconclusive(check.Area, reason))
            .ToList();

        return new ScanResult(storeUrl, Score: null, ScanStatus.Unreachable, areas, 0, DateTimeOffset.UtcNow);
    }
}
