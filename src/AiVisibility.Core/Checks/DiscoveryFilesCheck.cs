using AiVisibility.Core.Http;
using AiVisibility.Core.Models;

namespace AiVisibility.Core.Checks;

/// <summary>
/// Checks for the files agents and crawlers use to find their way around a store.
/// </summary>
/// <remarks>
/// A sitemap is long-established and its absence genuinely costs discovery. <c>llms.txt</c>
/// is a young convention whose real-world effect is still unproven, so its absence is
/// reported as a minor, clearly-labelled opportunity rather than a defect — overstating it
/// is how the existing apps in this category lost merchant trust.
/// </remarks>
public sealed class DiscoveryFilesCheck : ICheck
{
    private readonly IPageFetcher _fetcher;

    public ScoreArea Area => ScoreArea.DiscoveryFiles;

    public DiscoveryFilesCheck(IPageFetcher fetcher) => _fetcher = fetcher;

    public async Task<AreaResult> RunAsync(StoreSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var sitemap = await Fetch(snapshot.StoreUrl, "/sitemap.xml", cancellationToken).ConfigureAwait(false);
        var llms = await Fetch(snapshot.StoreUrl, "/llms.txt", cancellationToken).ConfigureAwait(false);

        // Only a 404 proves a file is absent. A 403 or a timeout means the scanner was turned
        // away, and reporting that as "missing" would send merchants chasing a file they
        // already publish.
        if (IsWithheld(sitemap) && IsWithheld(llms))
        {
            return AreaResult.Inconclusive(Area, $"neither file could be fetched ({Describe(sitemap)})");
        }

        var findings = new List<Finding>();
        var penalty = 0;

        if (SitemapFinding(snapshot, sitemap) is (Finding sitemapFinding, int sitemapPenalty))
        {
            findings.Add(sitemapFinding);
            penalty += sitemapPenalty;
        }

        if (LlmsTxtFinding(snapshot.StoreUrl, llms) is Finding llmsFinding)
        {
            findings.Add(llmsFinding);
            penalty += 20;
        }

        return new AreaResult(Area, Math.Max(0, 100 - penalty), findings);
    }

    private Task<FetchResult> Fetch(Uri storeUrl, string path, CancellationToken cancellationToken) =>
        _fetcher.GetAsync(new Uri(storeUrl, path), cancellationToken);

    /// <summary>
    /// Reports a missing sitemap, or a present one the store never points crawlers at.
    /// </summary>
    private static (Finding, int)? SitemapFinding(StoreSnapshot snapshot, FetchResult sitemap)
    {
        if (sitemap.IsNotFound)
        {
            return (new Finding(
                Code: "discovery-no-sitemap",
                Severity: Severity.Important,
                Title: "No reachable sitemap.xml",
                Detail: "Crawlers use the sitemap to enumerate products. Without it they only find "
                    + "pages that happen to be linked, so deeper catalogue items go unindexed.",
                Fix: "Publish /sitemap.xml and reference it from robots.txt.",
                Url: new Uri(snapshot.StoreUrl, "/sitemap.xml").ToString()), 45);
        }

        if (sitemap.IsSuccess && snapshot.Robots.Sitemaps.Count == 0)
        {
            return (new Finding(
                Code: "discovery-sitemap-not-declared",
                Severity: Severity.Minor,
                Title: "Sitemap exists but robots.txt does not point to it",
                Detail: "Crawlers that read robots.txt first have to guess the sitemap location.",
                Fix: "Add a 'Sitemap:' line to robots.txt.",
                Url: new Uri(snapshot.StoreUrl, "/robots.txt").ToString()), 10);
        }

        return null;
    }

    private static Finding? LlmsTxtFinding(Uri storeUrl, FetchResult llms)
    {
        var absent = llms.IsNotFound || (llms.IsSuccess && string.IsNullOrWhiteSpace(llms.Body));
        if (!absent)
        {
            return null;
        }

        return new Finding(
            Code: "discovery-no-llms-txt",
            Severity: Severity.Minor,
            Title: "No llms.txt published",
            Detail: "llms.txt is an emerging convention for handing assistants a curated map of "
                + "a site. Adoption by the major assistants is still partial and its effect on "
                + "rankings is unproven — treat this as cheap insurance, not a fix.",
            Fix: "Publish a /llms.txt listing key collections and policy pages.",
            Url: new Uri(storeUrl, "/llms.txt").ToString());
    }

    /// <summary>Whether the scanner was refused rather than told the file does not exist.</summary>
    private static bool IsWithheld(FetchResult result) => !result.IsSuccess && !result.IsNotFound;

    private static string Describe(FetchResult result) => result.Error ?? $"HTTP {result.StatusCode}";
}
