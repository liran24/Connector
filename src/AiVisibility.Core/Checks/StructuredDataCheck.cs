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
public sealed class StructuredDataCheck : ICheck
{
    private readonly IPageFetcher _fetcher;

    public ScoreArea Area => ScoreArea.StructuredData;

    public StructuredDataCheck(IPageFetcher fetcher) => _fetcher = fetcher;

    public async Task<AreaResult> RunAsync(StoreSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (snapshot.Products.Count == 0)
        {
            return AreaResult.Inconclusive(Area, "no products could be read from the storefront");
        }

        var survey = await SurveyAsync(snapshot.Products, cancellationToken).ConfigureAwait(false);

        if (survey.Inspected == 0)
        {
            return AreaResult.Inconclusive(
                Area,
                $"none of the {snapshot.Products.Count} product page(s) could be fetched");
        }

        return new AreaResult(Area, survey.Score(), survey.ToFindings());
    }

    /// <summary>Reads each product page and records what its markup is missing.</summary>
    private async Task<MarkupSurvey> SurveyAsync(
        IReadOnlyList<CatalogProduct> products,
        CancellationToken cancellationToken)
    {
        var survey = new MarkupSurvey();

        foreach (var product in products)
        {
            var page = await _fetcher.GetAsync(product.Url, cancellationToken).ConfigureAwait(false);
            if (!page.IsSuccess)
            {
                continue;
            }

            survey.Record(product, ProductSchemaAnalyzer.FindMissingFields(page.Body));
        }

        return survey;
    }

    /// <summary>
    /// Accumulates markup gaps across the sampled pages, then turns them into a score and a
    /// de-duplicated set of findings.
    /// </summary>
    private sealed class MarkupSurvey
    {
        private readonly Dictionary<SchemaField, int> _missing = new();
        private readonly List<CatalogProduct> _withoutMarkup = new();

        public int Inspected { get; private set; }

        public void Record(CatalogProduct product, IReadOnlyList<SchemaField>? missingFields)
        {
            Inspected++;

            if (missingFields is null)
            {
                _withoutMarkup.Add(product);
                return;
            }

            foreach (var field in missingFields)
            {
                _missing[field] = _missing.GetValueOrDefault(field) + 1;
            }
        }

        /// <summary>
        /// Weights each gap by severity and by how much of the catalogue it affects, so a field
        /// missing everywhere costs far more than one missing on a single page.
        /// </summary>
        public int Score()
        {
            var penalty = (double)_withoutMarkup.Count / Inspected * 60;

            foreach (var (field, count) in _missing)
            {
                var weight = field.Severity switch
                {
                    Severity.Critical => 12.0,
                    Severity.Important => 6.0,
                    _ => 2.5
                };

                penalty += weight * count / Inspected;
            }

            return Math.Max(0, 100 - (int)Math.Round(penalty));
        }

        public IReadOnlyList<Finding> ToFindings()
        {
            var findings = new List<Finding>();

            if (_withoutMarkup.Count > 0)
            {
                findings.Add(new Finding(
                    Code: "schema-missing-product",
                    Severity: Severity.Critical,
                    Title: $"{Describe(_withoutMarkup.Count)} have no Product markup",
                    Detail: "These pages carry no schema.org Product data, so an agent has to guess "
                        + "the price, stock and specification from raw page text — and usually declines to.",
                    Fix: "Emit a JSON-LD Product block on every product page.",
                    Url: _withoutMarkup[0].Url.ToString()));
            }

            findings.AddRange(_missing
                .OrderByDescending(kv => kv.Key.Severity)
                .ThenByDescending(kv => kv.Value)
                .Select(kv => ToFinding(kv.Key, kv.Value)));

            return findings;
        }

        private Finding ToFinding(SchemaField field, int count) => new(
            Code: $"schema-missing-field:{field.Path}",
            Severity: field.Severity,
            Title: $"{field.Label} missing on {Describe(count)}",
            Detail: $"schema.org property '{field.Path}' is absent, so {field.WhyItMatters}.",
            Fix: $"Populate '{field.Path}' in the Product JSON-LD on every product page.");

        private string Describe(int count) =>
            count == Inspected ? $"all {Inspected} sampled products" : $"{count} of {Inspected} sampled products";
    }
}
