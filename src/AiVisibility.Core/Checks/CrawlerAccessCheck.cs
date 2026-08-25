using AiVisibility.Core.Http;
using AiVisibility.Core.Models;
using AiVisibility.Core.Robots;

namespace AiVisibility.Core.Checks;

/// <summary>
/// Establishes whether AI crawlers are allowed to read the store at all.
/// </summary>
/// <remarks>
/// This matters most: every other signal is worthless if the crawler that would read it is
/// turned away at robots.txt. Blocks here are also the cheapest thing a merchant can fix,
/// which makes this the finding that sells the audit.
/// </remarks>
public sealed class CrawlerAccessCheck : ICheck
{
    private const string ProductPath = "/products/example-product";

    public ScoreArea Area => ScoreArea.CrawlerAccess;

    public Task<AreaResult> RunAsync(StoreSnapshot snapshot, CancellationToken cancellationToken = default) =>
        Task.FromResult(Evaluate(snapshot));

    private static AreaResult Evaluate(StoreSnapshot snapshot)
    {
        if (Unreadable(snapshot.RobotsFetch) is string reason)
        {
            return AreaResult.Inconclusive(ScoreArea.CrawlerAccess, reason);
        }

        var blocked = AiCrawlers.All
            .Select(crawler => Inspect(crawler, snapshot))
            .OfType<BlockedCrawler>()
            .ToList();

        var findings = blocked.Select(b => b.ToFinding(snapshot.StoreUrl)).ToList();
        var blockedTier1 = blocked.Count(b => b.Crawler.Tier == CrawlerTier.Tier1);

        return new AreaResult(ScoreArea.CrawlerAccess, Score(blockedTier1, blocked.Count), findings);
    }

    /// <summary>
    /// Distinguishes "no rules exist" from "the file was withheld".
    /// </summary>
    /// <returns>Why the file could not be read, or null when it was read (or genuinely absent).</returns>
    private static string? Unreadable(FetchResult robots)
    {
        // A 404 means no rules exist, so the store is fully open — a real pass.
        if (robots.IsNotFound || robots.IsSuccess)
        {
            return null;
        }

        // Anything else (403, 5xx, timeout) means the file exists but was withheld. Guessing
        // "open" would hand a false all-clear to the stores most likely to be blocking crawlers.
        return robots.Error is not null
            ? $"robots.txt could not be fetched: {robots.Error}"
            : $"robots.txt returned HTTP {robots.StatusCode}";
    }

    private static BlockedCrawler? Inspect(AiCrawler crawler, StoreSnapshot snapshot)
    {
        var homeAllowed = snapshot.Robots.IsAllowed(crawler.UserAgent, "/");
        var productsAllowed = snapshot.Robots.IsAllowed(crawler.UserAgent, ProductPath);

        if (homeAllowed && productsAllowed)
        {
            return null;
        }

        var scope = !homeAllowed && !productsAllowed ? "the entire store" : "product pages";
        return new BlockedCrawler(crawler, scope);
    }

    /// <summary>
    /// A single blocked Tier1 crawler is close to fatal, so the curve drops steeply rather
    /// than proportionally — a store blocking ChatGPT should not read as "mostly fine".
    /// </summary>
    private static int Score(int blockedTier1, int blockedTotal)
    {
        if (blockedTotal == 0)
        {
            return 100;
        }

        var score = 100 - (blockedTier1 * 30) - ((blockedTotal - blockedTier1) * 8);
        return Math.Max(0, score);
    }

    /// <summary>One crawler that cannot reach the store, and how much of it is off limits.</summary>
    private sealed record BlockedCrawler(AiCrawler Crawler, string Scope)
    {
        public Finding ToFinding(Uri storeUrl) => new(
            Code: $"crawler-blocked:{Crawler.UserAgent}",
            Severity: Crawler.Tier == CrawlerTier.Tier1 ? Severity.Critical : Severity.Important,
            Title: $"{Crawler.Product} is blocked from {Scope}",
            Detail: $"robots.txt disallows {Crawler.UserAgent}, which {Crawler.Purpose}. "
                + $"Until this is lifted, {Crawler.Product} cannot see these pages at all.",
            Fix: $"Remove the Disallow rule covering {Crawler.UserAgent} in robots.txt, "
                + "or add an explicit Allow for it.",
            Url: new Uri(storeUrl, "/robots.txt").ToString());
    }
}
