using AiVisibility.Core.Models;
using AiVisibility.Core.Tracking;

namespace AiVisibility.Cli;

/// <summary>
/// Renders scan and tracking results for a terminal.
/// </summary>
/// <remarks>
/// Kept apart from argument parsing so the wording of a merchant-facing report can be changed
/// without touching how the command is invoked.
/// </remarks>
internal static class ConsoleReporter
{
    public static void Write(ScanResult result)
    {
        Console.WriteLine(result.Score is int score
            ? $"AI visibility score: {score}/100"
            : "AI visibility score: not available");

        Console.WriteLine(result.Verdict);
        Console.WriteLine($"Products inspected: {result.ProductsInspected}");
        Console.WriteLine();

        WriteAreaScores(result);
        WriteUnverified(result);
        WriteFindings(result);
    }

    private static void WriteAreaScores(ScanResult result)
    {
        foreach (var area in result.Areas)
        {
            var value = area.Score is int score ? $"{score,3}/100" : "  not checked";
            Console.WriteLine($"  {ScanResult.Name(area.Area),-20} {value}");
        }
    }

    private static void WriteUnverified(ScanResult result)
    {
        if (result.UnverifiedAreas.Count == 0)
        {
            return;
        }

        Console.WriteLine("\nCould not be verified:");
        foreach (var area in result.UnverifiedAreas)
        {
            Console.WriteLine($"  - {ScanResult.Name(area.Area)}: {area.InconclusiveReason}");
        }
    }

    private static void WriteFindings(ScanResult result)
    {
        if (result.Findings.Count == 0)
        {
            Console.WriteLine(result.Status == ScanStatus.Complete
                ? "\nNothing to fix — this store reads cleanly to AI agents."
                : "\nNo problems found in the areas that could be checked.");
            return;
        }

        Console.WriteLine($"\n{result.Findings.Count} finding(s), worst first:\n");

        foreach (var finding in result.Findings)
        {
            Console.WriteLine($"[{finding.Severity.ToString().ToUpperInvariant()}] {finding.Title}");
            Console.WriteLine($"    {finding.Detail}");
            Console.WriteLine($"    Fix: {finding.Fix}");

            if (finding.Url is not null)
            {
                Console.WriteLine($"    Seen at: {finding.Url}");
            }

            Console.WriteLine();
        }
    }

    public static void Write(VisibilityReport report)
    {
        Console.WriteLine(report.Summary);
        Console.WriteLine();
        Console.WriteLine($"  Cited in            {report.CitationRate:P0} of answers");
        Console.WriteLine($"  Mentioned at all in {report.AnyMentionRate:P0} of answers");

        if (report.AverageRank is double rank)
        {
            Console.WriteLine($"  Average position    {rank:0.#} among cited sources");
        }

        if (report.FailedProbes > 0)
        {
            Console.WriteLine($"  Failed probes       {report.FailedProbes}");
        }

        WriteRivals(report);
        WriteOutcomes(report);

        Console.WriteLine(
            "Note: this queries the model with web search, which approximates but does not "
            + "reproduce what a shopper sees in a consumer chat product. Read it as a trend, not a transcript.");
    }

    private static void WriteRivals(VisibilityReport report)
    {
        if (report.Rivals.Count == 0)
        {
            return;
        }

        Console.WriteLine("\nWho is winning these questions:");
        foreach (var rival in report.Rivals.Take(8))
        {
            Console.WriteLine($"  {rival.Domain,-40} cited {rival.TimesCited}x, ahead of you {rival.TimesAhead}x");
        }
    }

    private static void WriteOutcomes(VisibilityReport report)
    {
        Console.WriteLine("\nPer question:\n");

        foreach (var outcome in report.Outcomes)
        {
            Console.WriteLine($"[{outcome.Engine}] {outcome.Prompt.Text}");
            Console.WriteLine($"    -> {Describe(outcome)}\n");
        }
    }

    private static string Describe(ProbeOutcome outcome)
    {
        if (outcome.Error is not null)
        {
            return $"failed: {outcome.Error}";
        }

        return outcome.Mention.Evidence switch
        {
            MentionEvidence.NamedAndCited => $"named and cited (#{outcome.Mention.Rank})",
            MentionEvidence.Cited => $"cited (#{outcome.Mention.Rank})",
            MentionEvidence.NameOnly => "named, but not linked",
            _ => "not mentioned"
        };
    }
}
