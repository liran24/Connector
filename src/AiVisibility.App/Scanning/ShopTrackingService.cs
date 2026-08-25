using System.Text.Json;
using AiVisibility.App.Configuration;
using AiVisibility.App.Data;
using AiVisibility.App.Shopify;
using AiVisibility.Core.Catalog;
using AiVisibility.Core.Http;
using AiVisibility.Core.Tracking;
using Anthropic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AiVisibility.App.Scanning;

/// <summary>Why a tracking run could not happen.</summary>
public enum TrackingFailure
{
    /// <summary>Tracking is switched off, or has no API key.</summary>
    NotConfigured,

    /// <summary>The shop is not installed.</summary>
    ShopNotFound,

    /// <summary>The storefront would not share a catalogue, so there are no questions to ask.</summary>
    NoCatalogue
}

/// <summary>The outcome of asking for a tracking run.</summary>
public sealed record TrackingOutcome(TrackingReport? Report, TrackingFailure? Failure)
{
    public static TrackingOutcome Success(TrackingReport report) => new(report, null);

    public static TrackingOutcome Failed(TrackingFailure failure) => new(null, failure);
}

/// <summary>
/// Runs visibility tracking for a shop and keeps the result.
/// </summary>
public interface IShopTrackingService
{
    /// <summary>Whether tracking can run at all, given the app's configuration.</summary>
    bool IsAvailable { get; }

    Task<TrackingOutcome> RunAndStoreAsync(ShopDomain shop, CancellationToken cancellationToken = default);

    /// <summary>The most recent stored run, or null when the shop has never been tracked.</summary>
    Task<StoredTrackingRun?> GetLatestAsync(ShopDomain shop, CancellationToken cancellationToken = default);
}

/// <summary>
/// Builds shopper questions from a shop's catalogue, puts them to Claude, and records who
/// the answers actually pointed at.
/// </summary>
public sealed class ShopTrackingService : IShopTrackingService
{
    private readonly AiVisibilityDbContext _database;
    private readonly IPageFetcher _fetcher;
    private readonly TrackingOptions _options;
    private readonly ILogger<ShopTrackingService> _logger;

    public bool IsAvailable => _options.IsUsable;

    public ShopTrackingService(
        AiVisibilityDbContext database,
        IPageFetcher fetcher,
        IOptions<TrackingOptions> options,
        ILogger<ShopTrackingService> logger)
    {
        _database = database;
        _fetcher = fetcher;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TrackingOutcome> RunAndStoreAsync(ShopDomain shop, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return TrackingOutcome.Failed(TrackingFailure.NotConfigured);
        }

        var installed = await _database.Shops
            .FirstOrDefaultAsync(s => s.Domain == shop.Value && s.UninstalledAt == null, cancellationToken)
            .ConfigureAwait(false);

        if (installed is null)
        {
            return TrackingOutcome.Failed(TrackingFailure.ShopNotFound);
        }

        var products = await new ShopifyCatalogSource(_fetcher)
            .GetProductsAsync(shop.StorefrontUrl, 50, cancellationToken)
            .ConfigureAwait(false);

        if (products.Count == 0)
        {
            _logger.LogWarning("Cannot track {Shop}: its catalogue could not be read.", shop.Value);
            return TrackingOutcome.Failed(TrackingFailure.NoCatalogue);
        }

        var report = await RunAsync(shop, products, cancellationToken).ConfigureAwait(false);

        _database.TrackingRuns.Add(new StoredTrackingRun
        {
            InstalledShopId = installed.Id,
            RanAt = DateTimeOffset.UtcNow,
            CitationRate = report.CitationRate,
            ResultJson = JsonSerializer.Serialize(report, ShopScanService.ReportJson)
        });

        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Tracked {Shop}: cited in {Rate:P0} of {Count} answers.",
            shop.Value, report.CitationRate, report.Questions.Count);

        return TrackingOutcome.Success(report);
    }

    /// <summary>
    /// Asks the questions.
    /// </summary>
    /// <remarks>
    /// The brand names come from the catalogue's own vendor field, which is the most reliable
    /// statement of what the shop trades under.
    /// </remarks>
    private async Task<TrackingReport> RunAsync(
        ShopDomain shop,
        IReadOnlyList<CatalogProduct> products,
        CancellationToken cancellationToken)
    {
        var prompts = PromptGenerator.ForCatalogue(products, _options.CategoriesPerRun);

        var brands = products
            .Select(p => p.Vendor)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var probe = new ClaudeAssistantProbe(
            new AnthropicClient { ApiKey = _options.ApiKey },
            _options.Model);

        var tracker = new VisibilityTracker(shop.StorefrontUrl, brands, new[] { probe });

        return TrackingReport.From(await tracker.RunAsync(prompts, cancellationToken).ConfigureAwait(false));
    }

    public Task<StoredTrackingRun?> GetLatestAsync(ShopDomain shop, CancellationToken cancellationToken = default) =>
        _database.TrackingRuns
            .Where(run => run.Shop!.Domain == shop.Value)
            .OrderByDescending(run => run.RanAt)
            .FirstOrDefaultAsync(cancellationToken);
}
