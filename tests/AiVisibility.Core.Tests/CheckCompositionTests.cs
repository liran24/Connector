using AiVisibility.Core;
using AiVisibility.Core.Checks;
using AiVisibility.Core.Models;
using AiVisibility.Core.Robots;

namespace AiVisibility.Core.Tests;

/// <summary>A check the scanner has never heard of, to prove it does not need to.</summary>
internal sealed class StubCheck : ICheck
{
    private readonly AreaResult _result;

    public ScoreArea Area { get; }

    public StubCheck(ScoreArea area, int? score, string? inconclusiveReason = null)
    {
        Area = area;
        _result = inconclusiveReason is not null
            ? AreaResult.Inconclusive(area, inconclusiveReason)
            : new AreaResult(area, score, Array.Empty<Finding>());
    }

    public Task<AreaResult> RunAsync(StoreSnapshot snapshot, CancellationToken cancellationToken = default) =>
        Task.FromResult(_result);
}

/// <summary>Hands back a fixed snapshot, or reports the store unreachable.</summary>
internal sealed class StubProbe : IStorefrontProbe
{
    private readonly StoreSnapshot? _snapshot;

    public string? FailureReason { get; }

    public StubProbe(StoreSnapshot? snapshot, string? failureReason = null)
    {
        _snapshot = snapshot;
        FailureReason = failureReason;
    }

    public static StoreSnapshot EmptySnapshot(Uri storeUrl) => new(
        storeUrl,
        RobotsTxtParser.AllowAll(),
        new Http.FetchResult(storeUrl, 404, string.Empty, null, null),
        Array.Empty<Catalog.CatalogProduct>());

    public Task<StoreSnapshot?> CaptureAsync(Uri storeUrl, int productSample, CancellationToken cancellationToken = default) =>
        Task.FromResult(_snapshot);
}

public class CheckCompositionTests
{
    private static readonly Uri StoreUrl = new("https://shop.example");

    private static StoreScanner ScannerWith(params ICheck[] checks) =>
        new(new StubProbe(StubProbe.EmptySnapshot(StoreUrl)), checks);

    [Fact]
    public async Task AnUnknownCheckIsScoredWithoutTheScannerKnowingItsName()
    {
        // The whole point of ICheck: adding a check must not require editing StoreScanner.
        var result = await ScannerWith(new StubCheck(ScoreArea.CrawlerAccess, 50)).ScanAsync(StoreUrl);

        var area = Assert.Single(result.Areas);
        Assert.Equal(50, area.Score);
        Assert.Equal(50, result.Score);
    }

    [Fact]
    public async Task WeightsDecideHowMuchEachAreaContributes()
    {
        var scanner = ScannerWith(
            new StubCheck(ScoreArea.CrawlerAccess, 100),
            new StubCheck(ScoreArea.DiscoveryFiles, 0));

        var result = await scanner.ScanAsync(StoreUrl);

        // 0.35 and 0.10 renormalise to 78/22, not a flat average of 50.
        Assert.Equal(78, result.Score);
    }

    [Fact]
    public async Task InconclusiveAreasAreExcludedFromTheScoreEntirely()
    {
        var scanner = ScannerWith(
            new StubCheck(ScoreArea.CrawlerAccess, 80),
            new StubCheck(ScoreArea.StructuredData, null, "catalogue unavailable"));

        var result = await scanner.ScanAsync(StoreUrl);

        Assert.Equal(ScanStatus.Partial, result.Status);
        Assert.Equal(80, result.Score);
        Assert.Single(result.UnverifiedAreas);
    }

    [Fact]
    public async Task EveryCheckIsRun()
    {
        var scanner = ScannerWith(
            new StubCheck(ScoreArea.CrawlerAccess, 10),
            new StubCheck(ScoreArea.StructuredData, 20),
            new StubCheck(ScoreArea.ContentDepth, 30),
            new StubCheck(ScoreArea.DiscoveryFiles, 40));

        var result = await scanner.ScanAsync(StoreUrl);

        Assert.Equal(4, result.Areas.Count);
    }

    [Fact]
    public async Task AnUnreachableStoreMarksEveryRegisteredCheckUnverified()
    {
        var scanner = new StoreScanner(
            new StubProbe(null, "connection refused"),
            new ICheck[] { new StubCheck(ScoreArea.CrawlerAccess, 100), new StubCheck(ScoreArea.ContentDepth, 100) });

        var result = await scanner.ScanAsync(StoreUrl);

        Assert.Equal(ScanStatus.Unreachable, result.Status);
        Assert.Null(result.Score);
        Assert.Equal(2, result.Areas.Count);
        Assert.All(result.Areas, area => Assert.Equal("connection refused", area.InconclusiveReason));
    }

    [Fact]
    public async Task CustomWeightsAreHonoured()
    {
        var weights = new AreaWeights(new Dictionary<ScoreArea, double>
        {
            [ScoreArea.CrawlerAccess] = 0.9,
            [ScoreArea.DiscoveryFiles] = 0.1
        });

        var scanner = new StoreScanner(
            new StubProbe(StubProbe.EmptySnapshot(StoreUrl)),
            new ICheck[] { new StubCheck(ScoreArea.CrawlerAccess, 100), new StubCheck(ScoreArea.DiscoveryFiles, 0) },
            weights);

        Assert.Equal(90, (await scanner.ScanAsync(StoreUrl)).Score);
    }
}
