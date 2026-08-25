namespace AiVisibility.Core.Robots;

/// <summary>
/// How much shopping traffic an agent actually drives. Blocking a Tier1 crawler is the
/// single most expensive mistake a store can make; Tier2 is worth fixing but survivable.
/// </summary>
public enum CrawlerTier
{
    Tier1 = 1,
    Tier2 = 2
}

/// <summary>
/// An AI crawler as it identifies itself in robots.txt, plus the product it feeds.
/// </summary>
/// <param name="UserAgent">Exact robots.txt token, matched case-insensitively.</param>
/// <param name="Product">Merchant-facing name of the assistant it serves.</param>
/// <param name="Purpose">What blocking it actually costs the merchant.</param>
public sealed record AiCrawler(
    string UserAgent,
    string Product,
    CrawlerTier Tier,
    string Purpose);

/// <summary>
/// The crawlers that decide whether a storefront can appear in AI shopping answers.
/// </summary>
/// <remarks>
/// Vendors add and rename agents regularly, so treat this as a maintained list rather than
/// a fixed one — a stale entry silently under-reports a real block.
/// </remarks>
public static class AiCrawlers
{
    public static IReadOnlyList<AiCrawler> All { get; } = new[]
    {
        new AiCrawler("GPTBot", "ChatGPT", CrawlerTier.Tier1,
            "indexes pages so ChatGPT can describe and recommend the store's products"),
        new AiCrawler("OAI-SearchBot", "ChatGPT Search", CrawlerTier.Tier1,
            "builds the search index behind ChatGPT's shopping answers"),
        new AiCrawler("ChatGPT-User", "ChatGPT (live browsing)", CrawlerTier.Tier1,
            "fetches the page when a shopper asks ChatGPT about it directly"),
        new AiCrawler("PerplexityBot", "Perplexity", CrawlerTier.Tier1,
            "indexes products for Perplexity's shopping and comparison answers"),
        new AiCrawler("Google-Extended", "Google AI Mode / Gemini", CrawlerTier.Tier1,
            "controls whether products appear in Google's AI shopping surfaces"),
        new AiCrawler("ClaudeBot", "Claude", CrawlerTier.Tier1,
            "indexes pages so Claude can reference the store's products"),

        new AiCrawler("Perplexity-User", "Perplexity (live browsing)", CrawlerTier.Tier2,
            "fetches the page when a shopper follows up on it in Perplexity"),
        new AiCrawler("Claude-User", "Claude (live browsing)", CrawlerTier.Tier2,
            "fetches the page when a shopper asks Claude about it directly"),
        new AiCrawler("Applebot-Extended", "Apple Intelligence", CrawlerTier.Tier2,
            "feeds Apple's on-device shopping suggestions"),
        new AiCrawler("meta-externalagent", "Meta AI", CrawlerTier.Tier2,
            "indexes products for Meta AI assistant answers"),
        new AiCrawler("Amazonbot", "Alexa / Rufus", CrawlerTier.Tier2,
            "feeds Amazon's assistant surfaces"),
        new AiCrawler("DuckAssistBot", "DuckDuckGo AI", CrawlerTier.Tier2,
            "feeds DuckDuckGo's assistant answers"),
        new AiCrawler("CCBot", "Common Crawl", CrawlerTier.Tier2,
            "supplies the open corpus many assistants train and ground on")
    };

    public static IEnumerable<AiCrawler> Tier1 => All.Where(c => c.Tier == CrawlerTier.Tier1);
}
