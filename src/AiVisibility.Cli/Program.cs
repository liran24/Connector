using AiVisibility.Cli;
using AiVisibility.Core;
using AiVisibility.Core.Catalog;
using AiVisibility.Core.Http;
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

var count = positional.Length > 1 && int.TryParse(positional[1], out var parsed)
    ? parsed
    : (mode == "track" ? 2 : 10);

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
    ConsoleReporter.Write(await StoreScanner.CreateDefault(fetcher).ScanAsync(storeUrl, count, cancellation.Token));
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

    var tracker = new VisibilityTracker(storeUrl, brands, new[] { new ClaudeAssistantProbe() });
    ConsoleReporter.Write(await tracker.RunAsync(prompts, token));
    return 0;
}

static bool TryParseStoreUrl(string input, out Uri storeUrl)
{
    var candidate = input.Contains("://", StringComparison.Ordinal) ? input : $"https://{input}";

    return Uri.TryCreate(candidate, UriKind.Absolute, out storeUrl!)
        && storeUrl.Scheme is "http" or "https";
}
