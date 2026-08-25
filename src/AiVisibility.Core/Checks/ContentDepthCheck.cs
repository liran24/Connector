using System.Text.RegularExpressions;
using AiVisibility.Core.Catalog;
using AiVisibility.Core.Models;
using AngleSharp.Html.Parser;

namespace AiVisibility.Core.Checks;

/// <summary>
/// Judges whether product copy actually answers the questions shoppers ask agents.
/// </summary>
/// <remarks>
/// Marketing copy ("you'll love it!") reads well to a human who can already see the photo,
/// but gives an agent nothing to match a query against. What agents need is specifics:
/// measurements, materials, compatibility, and use cases.
/// </remarks>
public sealed partial class ContentDepthCheck : ICheck
{
    /// <summary>Below this, a description cannot carry enough detail to answer a query.</summary>
    private const int ThinWordCount = 40;

    /// <summary>Terms that signal the copy states facts rather than sentiment.</summary>
    private static readonly string[] SpecificityMarkers =
    {
        "material", "made from", "made of", "cotton", "wool", "leather", "steel", "wood",
        "dimensions", "measures", "weight", "capacity", "fits", "compatible", "includes",
        "care", "wash", "warranty", "battery", "voltage", "ingredients", "volume"
    };

    private static readonly HtmlParser Parser = new();

    [GeneratedRegex(@"\d+\s?(cm|mm|m|in|inch|""|kg|g|lb|oz|ml|l|litre|liter|w|v|mah|gb|tb)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex MeasurementPattern();

    public ScoreArea Area => ScoreArea.ContentDepth;

    public Task<AreaResult> RunAsync(StoreSnapshot snapshot, CancellationToken cancellationToken = default) =>
        Task.FromResult(Evaluate(snapshot.Products));

    private static AreaResult Evaluate(IReadOnlyList<CatalogProduct> products)
    {
        if (products.Count == 0)
        {
            return AreaResult.Inconclusive(ScoreArea.ContentDepth, "no products could be read from the storefront");
        }

        var thin = products.Where(p => IsThin(p.BodyHtml)).ToList();
        var vague = products.Except(thin).Where(p => !HasSpecifics(ToPlainText(p.BodyHtml))).ToList();

        var findings = new List<Finding>();

        if (thin.Count > 0)
        {
            findings.Add(ThinCopyFinding(thin, products.Count));
        }

        if (vague.Count > 0)
        {
            findings.Add(VagueCopyFinding(vague, products.Count));
        }

        return new AreaResult(ScoreArea.ContentDepth, Score(thin.Count, vague.Count, products.Count), findings);
    }

    private static Finding ThinCopyFinding(IReadOnlyList<CatalogProduct> thin, int total) => new(
        Code: "content-thin-description",
        Severity: Severity.Important,
        Title: $"{Describe(thin.Count, total)} have descriptions under {ThinWordCount} words",
        Detail: "There is not enough text for an agent to determine what the product is for, "
            + "who it suits, or how it compares — so it gets left out of recommendations.",
        Fix: "Expand each description to cover what it is, who it is for, what it is made of, "
            + "and how it is used.",
        Url: thin[0].Url.ToString());

    private static Finding VagueCopyFinding(IReadOnlyList<CatalogProduct> vague, int total) => new(
        Code: "content-no-specifics",
        Severity: Severity.Minor,
        Title: $"{Describe(vague.Count, total)} describe benefits but state no facts",
        Detail: "The copy carries no measurements, materials or compatibility details. "
            + "Agents match shopper constraints ('fits a 15\" laptop', '100% wool') against "
            + "concrete facts, and this copy offers none.",
        Fix: "Add the physical specifics — dimensions, materials, capacity, what's included.",
        Url: vague[0].Url.ToString());

    private static int Score(int thin, int vague, int total)
    {
        var penalty = ((double)thin / total * 55) + ((double)vague / total * 25);
        return Math.Max(0, 100 - (int)Math.Round(penalty));
    }

    private static bool IsThin(string bodyHtml) => CountWords(ToPlainText(bodyHtml)) < ThinWordCount;

    private static bool HasSpecifics(string text) =>
        MeasurementPattern().IsMatch(text)
        || SpecificityMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static string ToPlainText(string html) =>
        string.IsNullOrWhiteSpace(html) ? string.Empty : Parser.ParseDocument(html).Body?.TextContent ?? string.Empty;

    private static int CountWords(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static string Describe(int count, int total) =>
        count == total ? $"all {total} sampled products" : $"{count} of {total} sampled products";
}
