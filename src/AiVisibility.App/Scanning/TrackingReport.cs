using AiVisibility.Core.Tracking;

namespace AiVisibility.App.Scanning;

/// <summary>
/// A tracking run as the dashboard consumes it.
/// </summary>
/// <remarks>
/// The same boundary as <see cref="ScanReport"/>, for the same reason: the engine's types are
/// free to change, this contract is what the TypeScript client is written against, and it is
/// what gets written into stored runs so an old one keeps meaning what it meant.
/// </remarks>
/// <param name="CitationRate">
/// Share of answers that cited the shop's own site, 0 to 1. The headline number, and
/// deliberately not the same as <paramref name="MentionRate"/>.
/// </param>
/// <param name="MentionRate">
/// Share of answers where the shop appeared at all, including being named without a link.
/// Always at least the citation rate.
/// </param>
public sealed record TrackingReport(
    DateTimeOffset RanAt,
    double CitationRate,
    double MentionRate,
    double? AverageRank,
    int AnsweredQuestions,
    int FailedQuestions,
    string Summary,
    IReadOnlyList<RivalReport> Rivals,
    IReadOnlyList<QuestionReport> Questions)
{
    public static TrackingReport From(VisibilityReport report) => new(
        report.RunAt,
        report.CitationRate,
        report.AnyMentionRate,
        report.AverageRank,
        report.Answered.Count,
        report.FailedProbes,
        report.Summary,
        report.Rivals.Take(8).Select(RivalReport.From).ToList(),
        report.Outcomes.Select(QuestionReport.From).ToList());
}

/// <summary>A site the assistants sent shoppers to instead.</summary>
public sealed record RivalReport(string Domain, int TimesCited, int TimesAhead)
{
    public static RivalReport From(Rival rival) => new(rival.Domain, rival.TimesCited, rival.TimesAhead);
}

/// <summary>
/// One question and what happened to the shop in the answer.
/// </summary>
/// <param name="Outcome">
/// One of <c>cited</c>, <c>named-only</c>, <c>absent</c>, <c>failed</c>. A name-drop stays
/// distinct from a citation all the way to the merchant's screen: an assistant can praise a
/// brand while linking the shopper elsewhere, and merging the two would inflate the number
/// they are paying to trust.
/// </param>
public sealed record QuestionReport(
    string Engine,
    string Question,
    string Category,
    string Outcome,
    int? Rank,
    string? Error)
{
    public static QuestionReport From(ProbeOutcome outcome) => new(
        outcome.Engine,
        outcome.Prompt.Text,
        outcome.Prompt.Category,
        Describe(outcome),
        outcome.Mention.Rank,
        outcome.Error);

    private static string Describe(ProbeOutcome outcome) => outcome.Error is not null
        ? "failed"
        : outcome.Mention.Evidence switch
        {
            MentionEvidence.NamedAndCited or MentionEvidence.Cited => "cited",
            MentionEvidence.NameOnly => "named-only",
            _ => "absent"
        };
}
