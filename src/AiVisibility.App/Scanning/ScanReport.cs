using AiVisibility.Core.Models;

namespace AiVisibility.App.Scanning;

/// <summary>
/// A scan as the dashboard consumes it.
/// </summary>
/// <remarks>
/// A deliberate boundary rather than serialising <see cref="ScanResult"/> directly. The engine's
/// types are free to change shape; this contract is what the TypeScript client is written
/// against, and it is also what gets written into stored reports, so an old report keeps
/// meaning what it meant.
/// </remarks>
/// <param name="Score">0-100, or null when the store could not be read.</param>
/// <param name="Status">One of <c>complete</c>, <c>partial</c>, <c>unreachable</c>.</param>
/// <param name="Verdict">One plain sentence for the top of the dashboard.</param>
public sealed record ScanReport(
    int? Score,
    string Status,
    string Verdict,
    int ProductsInspected,
    DateTimeOffset ScannedAt,
    IReadOnlyList<AreaReport> Areas,
    IReadOnlyList<FindingReport> Findings)
{
    public static ScanReport From(ScanResult result) => new(
        result.Score,
        result.Status.ToString().ToLowerInvariant(),
        result.Verdict,
        result.ProductsInspected,
        result.ScannedAt,
        result.Areas.Select(AreaReport.From).ToList(),
        result.Findings.Select(FindingReport.From).ToList());
}

/// <param name="Name">Merchant-facing area name, e.g. "Crawler access".</param>
/// <param name="Score">0-100, or null when the area could not be verified.</param>
/// <param name="NotCheckedReason">Why it could not be verified; null when it was.</param>
public sealed record AreaReport(string Key, string Name, int? Score, string? NotCheckedReason)
{
    public static AreaReport From(AreaResult area) => new(
        area.Area.ToString(),
        ScanResult.Name(area.Area),
        area.Score,
        area.InconclusiveReason);
}

/// <param name="Severity">One of <c>critical</c>, <c>important</c>, <c>minor</c>.</param>
public sealed record FindingReport(
    string Code,
    string Severity,
    string Title,
    string Detail,
    string Fix,
    string? Url)
{
    public static FindingReport From(Finding finding) => new(
        finding.Code,
        finding.Severity.ToString().ToLowerInvariant(),
        finding.Title,
        finding.Detail,
        finding.Fix,
        finding.Url);
}
