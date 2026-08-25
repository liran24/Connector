using AiVisibility.Core;
using AiVisibility.Core.Catalog;
using AiVisibility.Core.Http;
using AiVisibility.Core.Models;
using AiVisibility.Core.Tracking;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("""
        Usage:
          aivis scan  <store-url> [product-sample]   Audit how readable the store is to AI agents
          aivis track <store-url> [categories]       Check whether assistants actually recommend it

        scan   Reads robots.txt, product markup and copy. No credentials needed.
        track  Puts unbranded shopper questions to Claude with web search and reports
               whether the store gets cited. Requires ANTHROPIC_API_KEY and spends credit.
        """);
    return 0;
}

var mode = args[0] is "scan" or "track" ? args[0] : "scan";
var positional = args[0] is "scan" or "track" ? args.Skip(1).ToArray() : args;

if (positional.Length == 0 || !TryParseStoreUrl(positional[0], out var storeUrl))
{
    Console.Error.WriteLine($"Not a usable store URL: {(positional.Length > 0 ? positional[0] : "(none)")}");
    return 2;
}

var count = positional.Length > 1 && int.TryParse(positional[1], out var parsed) ? parsed : (mode == "track" ? 2 : 10);

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

var fetcher = HttpPageFetcher.CreateDefault();

try
{
    if (mode == "track")
    {
        return await TrackAsync(storeUrl, count, fetcher, cancellation.Token);
    }

    Console.WriteLine($"Scanning {storeUrl} ...\n");
    Report(await new StoreScanner(fetcher).ScanAsync(storeUrl, count, cancellation.Token));
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled.");
    return 130;
}

static async Task<int> TrackAsync(Uri storeUrl, int categories, IPageFetcher fetcher, CancellationToken token)
{
    if (Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") is null)
    {
        Console.Error.WriteLine("Tracking needs ANTHROPIC_API_KEY set — it calls the Claude API and spends credit.");
        return 2;
    }

    Console.WriteLine($"Reading the catalogue of {storeUrl} ...");
    var products = await new ShopifyCatalogSource(fetcher).GetProductsAsync(storeUrl, 50, token);

    if (products.Count == 0)
    {
        Console.Error.WriteLine("Could not read any products, so there is nothing to build shopper questions from.");
        return 1;
    }

    var prompts = PromptGenerator.ForCatalogue(products, categories);

    // The store's own vendor field is the most reliable source of the name it trades under.
    var brands = products
        .Select(p => p.Vendor)
        .OfType<string>()
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    Console.WriteLine($"Asking Claude {prompts.Count} shopper question(s). This costs API credit.\n");

    var report = await new VisibilityTracker(storeUrl, brands, new[] { new ClaudeAssistantProbe() })
        .RunAsync(prompts, token);

    ReportTracking(report);
    return 0;
}

static void ReportTracking(VisibilityReport report)
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

    if (report.Rivals.Count > 0)
    {
        Console.WriteLine("\nWho is winning these questions:");
        foreach (var rival in report.Rivals.Take(8))
        {
            Console.WriteLine($"  {rival.Domain,-40} cited {rival.TimesCited}x, ahead of you {rival.TimesAhead}x");
        }
    }

    Console.WriteLine("\nPer question:\n");
    foreach (var outcome in report.Outcomes)
    {
        var verdict = outcome.Error is not null
            ? $"failed: {outcome.Error}"
            : outcome.Mention.Evidence switch
            {
                MentionEvidence.NamedAndCited => $"named and cited (#{outcome.Mention.Rank})",
                MentionEvidence.Cited => $"cited (#{outcome.Mention.Rank})",
                MentionEvidence.NameOnly => "named, but not linked",
                _ => "not mentioned"
            };

        Console.WriteLine($"[{outcome.Engine}] {outcome.Prompt.Text}");
        Console.WriteLine($"    -> {verdict}\n");
    }

    Console.WriteLine(
        "Note: this queries the model with web search, which approximates but does not "
        + "reproduce what a shopper sees in a consumer chat product. Read it as a trend, not a transcript.");
}

static bool TryParseStoreUrl(string input, out Uri storeUrl)
{
    var candidate = input.Contains("://", StringComparison.Ordinal) ? input : $"https://{input}";

    return Uri.TryCreate(candidate, UriKind.Absolute, out storeUrl!)
        && storeUrl.Scheme is "http" or "https";
}

static void Report(ScanResult result)
{
    Console.WriteLine(result.Score is int score
        ? $"AI visibility score: {score}/100"
        : "AI visibility score: not available");

    Console.WriteLine(result.Verdict);
    Console.WriteLine($"Products inspected: {result.ProductsInspected}");
    Console.WriteLine();

    foreach (var area in result.Areas)
    {
        var value = area.Score is int areaScore ? $"{areaScore,3}/100" : "  not checked";
        Console.WriteLine($"  {ScanResult.Name(area.Area),-20} {value}");
    }

    if (result.UnverifiedAreas.Count > 0)
    {
        Console.WriteLine("\nCould not be verified:");
        foreach (var area in result.UnverifiedAreas)
        {
            Console.WriteLine($"  - {ScanResult.Name(area.Area)}: {area.InconclusiveReason}");
        }
    }

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
