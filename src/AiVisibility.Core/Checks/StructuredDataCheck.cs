using AiVisibility.Core.Catalog;
using AiVisibility.Core.Http;
using AiVisibility.Core.Models;
using AiVisibility.Core.StructuredData;

namespace AiVisibility.Core.Checks;

/// <summary>
/// Grades the schema.org markup on a sample of product pages.
/// </summary>
/// <remarks>
/// Findings are aggregated per missing field rather than per page: a merchant needs to hear
/// "no product declares availability" once, not forty times.
/// </remarks>
public sealed class StructuredDataCheck
{
    private readonly IPageFetcher _fetcher;

    public StructuredDataCheck(IPageFetcher fetcher) => _fetcher = fetcher;

    public async Task<AreaResult> RunAsync(
        IReadOnlyList<CatalogProduct> products,
        CancellationToken cancellationToken = default)
    {
        if (products.Count == 0)
        {
            return AreaResult.Inconclusive(
                ScoreArea.StructuredData,
                "no products could be read from the storefront");
        }

        var missingCounts = new Dictionary<SchemaField, int>();
        var pagesWithoutMarkup = new List<CatalogProduct>();
        var inspected = 0;

        foreach (var product in products)
        {
            var page = await _fetcher.GetAsync(product.Url, cancellationToken).ConfigureAwait(false);
            if (!page.IsSuccess)
            {
                continue;
            }

            inspected++;
            var missing = ProductSchemaAnalyzer.FindMissingFields(page.Body);

            if (missing is null)
            {
                pagesWithoutMarkup.Add(product);
                continue;
            }

            foreach (var field in missing)
            {
                missingCounts[field] = missingCounts.GetValueOrDefault(field) + 1;
            }
        }

        if (inspected == 0)
        {
            return AreaResult.Inconclusive(
                ScoreArea.StructuredData,
                $"none of the {products.Count} product page(s) could be fetched");
        }

        var findings = BuildFindings(missingCounts, pagesWithoutMarkup, inspected);
        return new AreaResult(ScoreArea.StructuredData, ScoreFor(missingCounts, pagesWithoutMarkup.Count, inspected), findings);
    }

    private static List<Finding> BuildFindings(
        Dictionary<SchemaField, int> missingCounts,
        List<CatalogProduct> pagesWithoutMarkup,
        int inspected)
    {
        var findings = new List<Finding>();

        if (pagesWithoutMarkup.Count > 0)
        {
            findings.Add(new Finding(
                Code: "schema-missing-product",
                Severity: Severity.Critical,
                Title: $"{Describe(pagesWithoutMarkup.Count, inspected)} have no Product markup",
                Detail: "These pages carry no schema.org Product data, so an agent has to guess "
                    + "the price, stock and specification from raw page text — and usually declines to.",
                Fix: "Emit a JSON-LD Product block on every product page.",
                Url: pagesWithoutMarkup[0].Url.ToString()));
        }

        foreach (var (field, count) in missingCounts.OrderByDescending(kv => kv.Key.Severity).ThenByDescending(kv => kv.Value))
        {
            findings.Add(new Finding(
                Code: $"schema-missing-field:{field.Path}",
                Severity: field.Severity,
                Title: $"{field.Label} missing on {Describe(count, inspected)}",
                Detail: $"schema.org property '{field.Path}' is absent, so {field.WhyItMatters}.",
                Fix: $"Populate '{field.Path}' in the Product JSON-LD on every product page."));
        }

        return findings;
    }

    private static string Describe(int count, int total) =>
        count == total ? $"all {total} sampled products" : $"{count} of {total} sampled products";

    /// <summary>
    /// Weights each miss by severity and by how much of the catalogue it affects, so a field
    /// missing everywhere costs far more than one missing on a single page.
    /// </summary>
    private static int ScoreFor(Dictionary<SchemaField, int> missingCounts, int pagesWithoutMarkup, int inspected)
    {
        var penalty = (double)pagesWithoutMarkup / inspected * 60;

        foreach (var (field, count) in missingCounts)
        {
            var weight = field.Severity switch
            {
                Severity.Critical => 12.0,
                Severity.Important => 6.0,
                _ => 2.5
            };

            penalty += weight * count / inspected;
        }

        return Math.Max(0, 100 - (int)Math.Round(penalty));
    }
}
