using AiVisibility.Core;
using AiVisibility.Core.Http;
using AiVisibility.Core.Models;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("""
        Usage: aivis <store-url> [product-sample]

          store-url       Storefront root, e.g. https://example.myshopify.com
          product-sample  How many products to inspect (default 10)
        """);
    return 0;
}

if (!TryParseStoreUrl(args[0], out var storeUrl))
{
    Console.Error.WriteLine($"Not a usable store URL: {args[0]}");
    return 2;
}

var sample = args.Length > 1 && int.TryParse(args[1], out var parsed) ? parsed : 10;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

Console.WriteLine($"Scanning {storeUrl} ...\n");

var scanner = new StoreScanner(HttpPageFetcher.CreateDefault());

ScanResult result;
try
{
    result = await scanner.ScanAsync(storeUrl, sample, cancellation.Token);
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled.");
    return 130;
}

Report(result);
return 0;

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
