namespace AiVisibility.Core.Models;

/// <summary>
/// The four areas a store is scored on. Each maps to one check and carries its own weight.
/// </summary>
public enum ScoreArea
{
    /// <summary>Whether AI crawlers are allowed to read the store at all.</summary>
    CrawlerAccess,

    /// <summary>Whether product pages carry complete schema.org markup.</summary>
    StructuredData,

    /// <summary>Whether product copy answers the questions an agent gets asked.</summary>
    ContentDepth,

    /// <summary>Whether the store publishes the files agents look for (llms.txt, sitemap).</summary>
    DiscoveryFiles
}

/// <summary>
/// How much of the audit actually completed.
/// </summary>
public enum ScanStatus
{
    /// <summary>Every area was verified.</summary>
    Complete,

    /// <summary>Some areas could not be read; the score covers only what was verified.</summary>
    Partial,

    /// <summary>Nothing could be read. There is no score to report.</summary>
    Unreachable
}

/// <summary>
/// The outcome of one check: a 0-100 sub-score plus whatever it found.
/// </summary>
/// <remarks>
/// A check that could not read what it needed is <em>inconclusive</em>, never a pass.
/// Reporting an unverifiable area as 100 would tell a merchant behind bot protection that
/// they are in perfect shape — the most damaging thing this tool could do.
/// </remarks>
public sealed record AreaResult(
    ScoreArea Area,
    int? Score,
    IReadOnlyList<Finding> Findings,
    string? InconclusiveReason = null)
{
    public bool IsInconclusive => InconclusiveReason is not null;

    public static AreaResult Clean(ScoreArea area) => new(area, 100, Array.Empty<Finding>());

    /// <summary>The check could not gather enough evidence to judge this area.</summary>
    public static AreaResult Inconclusive(ScoreArea area, string reason) =>
        new(area, null, Array.Empty<Finding>(), reason);
}

/// <summary>
/// A complete store audit: the headline score, the per-area breakdown, and every finding.
/// </summary>
public sealed record ScanResult(
    Uri StoreUrl,
    int? Score,
    ScanStatus Status,
    IReadOnlyList<AreaResult> Areas,
    int ProductsInspected,
    DateTimeOffset ScannedAt)
{
    /// <summary>All findings across all areas, worst first.</summary>
    public IReadOnlyList<Finding> Findings { get; } = Areas
        .SelectMany(a => a.Findings)
        .OrderByDescending(f => f.Severity)
        .ToList();

    /// <summary>Areas that could not be verified, and why.</summary>
    public IReadOnlyList<AreaResult> UnverifiedAreas { get; } = Areas.Where(a => a.IsInconclusive).ToList();

    /// <summary>
    /// A plain-language verdict for the top of the merchant's report. Never claims a store is
    /// healthy on the strength of a partial scan.
    /// </summary>
    public string Verdict => Status switch
    {
        ScanStatus.Unreachable =>
            "The store could not be read at all, so no assessment was possible. "
            + "This usually means bot protection is turning the scanner away — which is worth "
            + "checking, because it may be turning AI crawlers away too.",

        ScanStatus.Partial =>
            $"Partial scan: {Describe()} Based only on what could be verified, "
            + ScoreVerdict().ToLowerInvariant(),

        _ => ScoreVerdict()
    };

    private string Describe() =>
        UnverifiedAreas.Count == 1
            ? $"{Name(UnverifiedAreas[0].Area)} could not be checked ({UnverifiedAreas[0].InconclusiveReason})."
            : $"{UnverifiedAreas.Count} areas could not be checked.";

    private string ScoreVerdict() => Score switch
    {
        >= 85 => "Well prepared. AI shopping agents can read and recommend this store.",
        >= 65 => "Readable, but competitors with richer product data will be recommended first.",
        >= 40 => "Partially visible. Agents are missing key facts they need to recommend products.",
        _ => "Effectively invisible to AI shopping agents."
    };

    public static string Name(ScoreArea area) => area switch
    {
        ScoreArea.CrawlerAccess => "Crawler access",
        ScoreArea.StructuredData => "Structured data",
        ScoreArea.ContentDepth => "Content depth",
        ScoreArea.DiscoveryFiles => "Discovery files",
        _ => area.ToString()
    };
}
