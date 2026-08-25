using AiVisibility.Core;
using AiVisibility.Core.Models;

namespace AiVisibility.Core.Tests;

public class StoreScannerTests
{
    private static readonly Uri StoreUrl = new("https://shop.example");

    private const string RichProductPage = """
        <html><head><script type="application/ld+json">
        {
          "@type": "Product",
          "name": "Pour-over kettle",
          "description": "A gooseneck kettle for filter coffee.",
          "image": "https://shop.example/kettle.jpg",
          "brand": {"@type": "Brand", "name": "Example"},
          "sku": "KET-001",
          "color": "matte black",
          "aggregateRating": {"@type": "AggregateRating", "ratingValue": "4.6"},
          "offers": {
            "@type": "Offer", "price": "49.00", "priceCurrency": "USD",
            "availability": "https://schema.org/InStock",
            "shippingDetails": {"@type": "OfferShippingDetails"},
            "hasMerchantReturnPolicy": {"@type": "MerchantReturnPolicy"}
          }
        }
        </script></head><body></body></html>
        """;

    private const string DetailedDescription =
        "<p>A 1.0 litre gooseneck kettle made from brushed stainless steel, "
        + "measuring 28 cm from base to spout. The narrow spout controls pour rate for "
        + "filter coffee. Compatible with induction, gas and electric hobs. Dishwasher safe, "
        + "and it includes a removable lid gasket you can replace without tools.</p>";

    private static string CatalogJson(string bodyHtml) => $$"""
        {"products": [{"title": "Pour-over kettle", "handle": "kettle", "body_html": {{System.Text.Json.JsonSerializer.Serialize(bodyHtml)}}}]}
        """;

    /// <summary>A store doing everything right, used as the baseline for the failure cases.</summary>
    private static FakePageFetcher HealthyStore() => new FakePageFetcher()
        .Add("/", "<html><body>Shop</body></html>")
        .Add("/robots.txt","User-agent: *\nDisallow: /admin\nSitemap: https://shop.example/sitemap.xml", "text/plain")
        .Add("/sitemap.xml", "<urlset></urlset>", "application/xml")
        .Add("/llms.txt", "# Shop\n- /collections/all", "text/plain")
        .Add("/products.json?limit=10", CatalogJson(DetailedDescription), "application/json")
        .Add("/products/kettle", RichProductPage);

    [Fact]
    public async Task HealthyStore_ScoresNearPerfect()
    {
        var result = await StoreScanner.CreateDefault(HealthyStore()).ScanAsync(StoreUrl);

        Assert.Equal(100, result.Score);
        Assert.Empty(result.Findings);
        Assert.Equal(1, result.ProductsInspected);
    }

    [Fact]
    public async Task BlockingChatGpt_ProducesACriticalFindingAndDropsTheScore()
    {
        var fetcher = HealthyStore()
            .Add("/robots.txt", "User-agent: GPTBot\nDisallow: /\n", "text/plain");

        var result = await StoreScanner.CreateDefault(fetcher).ScanAsync(StoreUrl);

        var finding = Assert.Single(result.Findings, f => f.Code == "crawler-blocked:GPTBot");
        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Contains("ChatGPT", finding.Title);
        Assert.True(result.Score < 90, $"expected a blocked Tier1 crawler to cost real points, got {result.Score}");
    }

    [Fact]
    public async Task StoreWithNoRobotsFile_IsTreatedAsFullyOpen()
    {
        var fetcher = new FakePageFetcher()
            .Add("/", "<html><body>Shop</body></html>")
            .Add("/sitemap.xml", "<urlset></urlset>", "application/xml")
            .Add("/llms.txt", "# Shop", "text/plain")
            .Add("/products.json?limit=10", CatalogJson(DetailedDescription), "application/json")
            .Add("/products/kettle", RichProductPage);

        var result = await StoreScanner.CreateDefault(fetcher).ScanAsync(StoreUrl);

        Assert.DoesNotContain(result.Findings, f => f.Code.StartsWith("crawler-blocked"));
        var crawlerArea = result.Areas.Single(a => a.Area == ScoreArea.CrawlerAccess);
        Assert.Equal(100, crawlerArea.Score);
    }

    [Fact]
    public async Task ProductPageWithoutMarkup_IsReportedAsCritical()
    {
        var fetcher = HealthyStore()
            .Add("/products/kettle", "<html><body>A lovely kettle</body></html>");

        var result = await StoreScanner.CreateDefault(fetcher).ScanAsync(StoreUrl);

        var finding = Assert.Single(result.Findings, f => f.Code == "schema-missing-product");
        Assert.Equal(Severity.Critical, finding.Severity);
    }

    [Fact]
    public async Task ThinDescription_IsReported()
    {
        var fetcher = HealthyStore()
            .Add("/products.json?limit=10", CatalogJson("<p>You'll love it!</p>"), "application/json");

        var result = await StoreScanner.CreateDefault(fetcher).ScanAsync(StoreUrl);

        Assert.Contains(result.Findings, f => f.Code == "content-thin-description");
    }

    [Fact]
    public async Task LongButFactFreeCopy_IsReportedAsVagueRatherThanThin()
    {
        var fluff = "<p>" + string.Join(" ", Enumerable.Repeat("You will absolutely adore this beautiful piece", 12)) + "</p>";
        var fetcher = HealthyStore().Add("/products.json?limit=10", CatalogJson(fluff), "application/json");

        var result = await StoreScanner.CreateDefault(fetcher).ScanAsync(StoreUrl);

        Assert.Contains(result.Findings, f => f.Code == "content-no-specifics");
        Assert.DoesNotContain(result.Findings, f => f.Code == "content-thin-description");
    }

    [Fact]
    public async Task MissingSitemapAndLlmsTxt_AreBothReported()
    {
        var fetcher = new FakePageFetcher()
            .Add("/", "<html><body>Shop</body></html>")
            .Add("/robots.txt", "User-agent: *\nDisallow:", "text/plain")
            .Add("/products.json?limit=10", CatalogJson(DetailedDescription), "application/json")
            .Add("/products/kettle", RichProductPage);

        var result = await StoreScanner.CreateDefault(fetcher).ScanAsync(StoreUrl);

        Assert.Contains(result.Findings, f => f.Code == "discovery-no-sitemap");
        Assert.Contains(result.Findings, f => f.Code == "discovery-no-llms-txt");
    }

    [Fact]
    public async Task LlmsTxtGap_IsOnlyMinor_SoItCannotDominateTheReport()
    {
        var fetcher = HealthyStore().Add("/llms.txt", string.Empty, "text/plain", status: 404);

        var result = await StoreScanner.CreateDefault(fetcher).ScanAsync(StoreUrl);

        var finding = Assert.Single(result.Findings);
        Assert.Equal("discovery-no-llms-txt", finding.Code);
        Assert.Equal(Severity.Minor, finding.Severity);
        Assert.True(result.Score >= 95, $"a missing llms.txt should barely move the score, got {result.Score}");
    }

    [Fact]
    public async Task StoreWithClosedCatalogEndpoint_ReportsProductAreasAsUnverified()
    {
        var fetcher = new FakePageFetcher()
            .Add("/", "<html><body>Shop</body></html>")
            .Add("/robots.txt", "User-agent: GPTBot\nDisallow: /", "text/plain")
            .Add("/sitemap.xml", "<urlset></urlset>", "application/xml")
            .Add("/llms.txt", "# Shop", "text/plain");

        var result = await StoreScanner.CreateDefault(fetcher).ScanAsync(StoreUrl);

        Assert.Equal(0, result.ProductsInspected);
        Assert.Equal(ScanStatus.Partial, result.Status);
        Assert.Contains(result.Findings, f => f.Code == "crawler-blocked:GPTBot");

        // The catalogue was never read, so these two must not be scored at all.
        var unverified = result.UnverifiedAreas.Select(a => a.Area).ToList();
        Assert.Contains(ScoreArea.StructuredData, unverified);
        Assert.Contains(ScoreArea.ContentDepth, unverified);
    }

    [Fact]
    public async Task UnreachableStore_IsNeverScored()
    {
        // Every request fails, as it would behind bot protection. Reporting a healthy score
        // here was a real bug: it told the stores most likely to be blocking crawlers that
        // they were in perfect shape.
        var result = await StoreScanner.CreateDefault(new FakePageFetcher()).ScanAsync(StoreUrl);

        Assert.Equal(ScanStatus.Unreachable, result.Status);
        Assert.Null(result.Score);
        Assert.All(result.Areas, area => Assert.True(area.IsInconclusive));
        Assert.DoesNotContain("Well prepared", result.Verdict);
    }

    [Fact]
    public async Task RobotsTxtRefusedWithForbidden_IsNotTreatedAsPermission()
    {
        // A 403 means the file exists and was withheld — the opposite of a 404, and never
        // grounds for declaring crawler access healthy.
        var fetcher = HealthyStore()
            .Add("/robots.txt", string.Empty, "text/plain", status: 403);

        var result = await StoreScanner.CreateDefault(fetcher).ScanAsync(StoreUrl);

        var crawlerArea = result.Areas.Single(a => a.Area == ScoreArea.CrawlerAccess);
        Assert.True(crawlerArea.IsInconclusive);
        Assert.Equal(ScanStatus.Partial, result.Status);
    }

    [Fact]
    public async Task SitemapRefusedWithForbidden_IsNotReportedAsMissing()
    {
        var fetcher = HealthyStore()
            .Add("/sitemap.xml", string.Empty, "application/xml", status: 403)
            .Add("/llms.txt", string.Empty, "text/plain", status: 403);

        var result = await StoreScanner.CreateDefault(fetcher).ScanAsync(StoreUrl);

        Assert.DoesNotContain(result.Findings, f => f.Code == "discovery-no-sitemap");
        Assert.True(result.Areas.Single(a => a.Area == ScoreArea.DiscoveryFiles).IsInconclusive);
    }

    [Fact]
    public async Task PartialScan_ScoresOnlyTheVerifiedAreas()
    {
        // Crawler access and discovery files are clean; the catalogue is unreadable. The
        // score must reflect the two verified areas rather than being dragged toward zero.
        var fetcher = new FakePageFetcher()
            .Add("/", "<html><body>Shop</body></html>")
            .Add("/robots.txt", "User-agent: *\nDisallow: /admin\nSitemap: https://shop.example/sitemap.xml", "text/plain")
            .Add("/sitemap.xml", "<urlset></urlset>", "application/xml")
            .Add("/llms.txt", "# Shop", "text/plain");

        var result = await StoreScanner.CreateDefault(fetcher).ScanAsync(StoreUrl);

        Assert.Equal(ScanStatus.Partial, result.Status);
        Assert.Equal(100, result.Score);
        Assert.StartsWith("Partial scan", result.Verdict);
    }

    [Fact]
    public async Task FindingsAreOrderedWorstFirst()
    {
        var fetcher = HealthyStore()
            .Add("/robots.txt", "User-agent: GPTBot\nDisallow: /", "text/plain")
            .Add("/products/kettle", "<html><body>A kettle</body></html>")
            .Add("/llms.txt", string.Empty, "text/plain", status: 404);

        var result = await StoreScanner.CreateDefault(fetcher).ScanAsync(StoreUrl);

        var severities = result.Findings.Select(f => f.Severity).ToList();
        Assert.Equal(severities.OrderByDescending(s => s), severities);
    }

    [Fact]
    public async Task EveryScoreStaysWithinRange()
    {
        var brokenStore = new FakePageFetcher()
            .Add("/", "<html><body>Shop</body></html>")
            .Add("/robots.txt", "User-agent: *\nDisallow: /", "text/plain");

        var result = await StoreScanner.CreateDefault(brokenStore).ScanAsync(StoreUrl);

        Assert.InRange(result.Score ?? 0, 0, 100);
        Assert.All(
            result.Areas.Where(a => !a.IsInconclusive),
            area => Assert.InRange(area.Score!.Value, 0, 100));
    }
}
