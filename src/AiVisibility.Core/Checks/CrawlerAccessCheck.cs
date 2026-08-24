using AiVisibility.Core.Http;
using AiVisibility.Core.Models;
using AiVisibility.Core.Robots;

namespace AiVisibility.Core.Checks;

/// <summary>
/// Establishes whether AI crawlers are allowed to read the store at all.
/// </summary>
/// <remarks>
/// This runs first and matters most: every other signal is worthless if the crawler that
/// would read it is turned away at robots.txt. Blocks here are also the cheapest thing a
/// merchant can fix, which makes this the finding that sells the audit.
/// </remarks>
public sealed class CrawlerAccessCheck
{
    private const string ProductPath = "/products/example-product";

    private readonly IPageFetcher _fetcher;

    public CrawlerAccessCheck(IPageFetcher fetcher) => _fetcher = fetcher;

    public async Task<(AreaResult Result, RobotsTxtParser Robots)> RunAsync(
        Uri storeUrl,
        CancellationToken cancellationToken = default)
    {
        var response = await _fetcher
            .GetAsync(new Uri(storeUrl, "/robots.txt"), cancellationToken)
            .ConfigureAwait(false);

        // A genuine 404 means no rules exist, so the store is fully open — that is a real pass.
        if (response.IsNotFound)
        {
            return (AreaResult.Clean(ScoreArea.CrawlerAccess), RobotsTxtParser.AllowAll());
        }

        // Anything else (403, 5xx, timeout) means the file exists but was withheld. Guessing
        // "open" there would hand a false all-clear to exactly the stores most likely to be
        // blocking crawlers.
        if (!response.IsSuccess)
        {
            var reason = response.Error is not null
                ? $"robots.txt could not be fetched: {response.Error}"
                : $"robots.txt returned HTTP {response.StatusCode}";

            return (AreaResult.Inconclusive(ScoreArea.CrawlerAccess, reason), RobotsTxtParser.AllowAll());
        }

        var robots = RobotsTxtParser.Parse(response.Body);
        var findings = new List<Finding>();
        var blockedTier1 = 0;

        foreach (var crawler in AiCrawlers.All)
        {
            var homeAllowed = robots.IsAllowed(crawler.UserAgent, "/");
            var productsAllowed = robots.IsAllowed(crawler.UserAgent, ProductPath);

            if (homeAllowed && productsAllowed)
            {
                continue;
            }

            var scope = !homeAllowed && !productsAllowed ? "the entire store" : "product pages";
            var severity = crawler.Tier == CrawlerTier.Tier1 ? Severity.Critical : Severity.Important;

            if (crawler.Tier == CrawlerTier.Tier1)
            {
                blockedTier1++;
            }

            findings.Add(new Finding(
                Code: $"crawler-blocked:{crawler.UserAgent}",
                Severity: severity,
                Title: $"{crawler.Product} is blocked from {scope}",
                Detail: $"robots.txt disallows {crawler.UserAgent}, which {crawler.Purpose}. "
                    + $"Until this is lifted, {crawler.Product} cannot see these pages at all.",
                Fix: $"Remove the Disallow rule covering {crawler.UserAgent} in robots.txt, "
                    + "or add an explicit Allow for it.",
                Url: new Uri(storeUrl, "/robots.txt").ToString()));
        }

        return (new AreaResult(ScoreArea.CrawlerAccess, ScoreFor(blockedTier1, findings.Count), findings), robots);
    }

    /// <summary>
    /// A single blocked Tier1 crawler is close to fatal, so the curve drops steeply rather
    /// than proportionally — a store blocking ChatGPT should not read as "mostly fine".
    /// </summary>
    private static int ScoreFor(int blockedTier1, int blockedTotal)
    {
        if (blockedTotal == 0)
        {
            return 100;
        }

        var score = 100 - (blockedTier1 * 30) - ((blockedTotal - blockedTier1) * 8);
        return Math.Max(0, score);
    }
}
