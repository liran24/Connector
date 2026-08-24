using AiVisibility.Core.Http;
using AiVisibility.Core.Models;
using AiVisibility.Core.Robots;

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
public sealed class DiscoveryFilesCheck
{
    private readonly IPageFetcher _fetcher;

    public DiscoveryFilesCheck(IPageFetcher fetcher) => _fetcher = fetcher;

    public async Task<AreaResult> RunAsync(
        Uri storeUrl,
        RobotsTxtParser robots,
        CancellationToken cancellationToken = default)
    {
        var findings = new List<Finding>();
        var penalty = 0;

        var sitemap = await _fetcher
            .GetAsync(new Uri(storeUrl, "/sitemap.xml"), cancellationToken)
            .ConfigureAwait(false);

        var llms = await _fetcher
            .GetAsync(new Uri(storeUrl, "/llms.txt"), cancellationToken)
            .ConfigureAwait(false);

        // Only a 404 proves a file is absent. A 403 or a timeout means the scanner was turned
        // away, and reporting that as "missing" would send merchants chasing a file they
        // already publish.
        if (IsWithheld(sitemap) && IsWithheld(llms))
        {
            return AreaResult.Inconclusive(
                ScoreArea.DiscoveryFiles,
                $"neither file could be fetched ({Describe(sitemap)})");
        }

        if (sitemap.IsNotFound)
        {
            penalty += 45;
            findings.Add(new Finding(
                Code: "discovery-no-sitemap",
                Severity: Severity.Important,
                Title: "No reachable sitemap.xml",
                Detail: "Crawlers use the sitemap to enumerate products. Without it they only find "
                    + "pages that happen to be linked, so deeper catalogue items go unindexed.",
                Fix: "Publish /sitemap.xml and reference it from robots.txt.",
                Url: new Uri(storeUrl, "/sitemap.xml").ToString()));
        }
        else if (sitemap.IsSuccess && robots.Sitemaps.Count == 0)
        {
            penalty += 10;
            findings.Add(new Finding(
                Code: "discovery-sitemap-not-declared",
                Severity: Severity.Minor,
                Title: "Sitemap exists but robots.txt does not point to it",
                Detail: "Crawlers that read robots.txt first have to guess the sitemap location.",
                Fix: "Add a 'Sitemap:' line to robots.txt.",
                Url: new Uri(storeUrl, "/robots.txt").ToString()));
        }

        if (llms.IsNotFound || (llms.IsSuccess && string.IsNullOrWhiteSpace(llms.Body)))
        {
            penalty += 20;
            findings.Add(new Finding(
                Code: "discovery-no-llms-txt",
                Severity: Severity.Minor,
                Title: "No llms.txt published",
                Detail: "llms.txt is an emerging convention for handing assistants a curated map of "
                    + "a site. Adoption by the major assistants is still partial and its effect on "
                    + "rankings is unproven — treat this as cheap insurance, not a fix.",
                Fix: "Publish a /llms.txt listing key collections and policy pages.",
                Url: new Uri(storeUrl, "/llms.txt").ToString()));
        }

        return new AreaResult(ScoreArea.DiscoveryFiles, Math.Max(0, 100 - penalty), findings);
    }

    /// <summary>Whether the scanner was refused rather than told the file does not exist.</summary>
    private static bool IsWithheld(FetchResult result) => !result.IsSuccess && !result.IsNotFound;

    private static string Describe(FetchResult result) =>
        result.Error ?? $"HTTP {result.StatusCode}";
}
