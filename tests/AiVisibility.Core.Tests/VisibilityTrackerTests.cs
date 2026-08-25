using AiVisibility.Core.Catalog;
using AiVisibility.Core.Tracking;

namespace AiVisibility.Core.Tests;

/// <summary>
/// Returns canned answers in order, so tracking can be tested without spending API credit.
/// </summary>
internal sealed class FakeProbe : IAssistantProbe
{
    private readonly Queue<AssistantAnswer> _answers;

    public string Engine { get; }

    public FakeProbe(string engine, params AssistantAnswer[] answers)
    {
        Engine = engine;
        _answers = new Queue<AssistantAnswer>(answers);
    }

    public Task<AssistantAnswer> AskAsync(ShopperPrompt prompt, CancellationToken cancellationToken = default) =>
        Task.FromResult(_answers.Count > 0
            ? _answers.Dequeue()
            : new AssistantAnswer(Engine, string.Empty, Array.Empty<Citation>()));
}

public class VisibilityTrackerTests
{
    private static readonly Uri StoreUrl = new("https://kettleco.example");

    private static readonly IReadOnlyList<ShopperPrompt> OnePrompt =
        new[] { new ShopperPrompt("best kettles?", "Kettles") };

    private static AssistantAnswer Answer(string text, params string[] urls) =>
        new("Claude", text, urls.Select(u => new Citation(new Uri(u), null)).ToList());

    private static VisibilityTracker Tracker(params IAssistantProbe[] probes) =>
        new(StoreUrl, new[] { "KettleCo" }, probes);

    [Fact]
    public async Task CitedInEveryAnswer_ReportsFullCitationRate()
    {
        var probe = new FakeProbe("Claude", Answer("Try KettleCo.", "https://kettleco.example/"));

        var report = await Tracker(probe).RunAsync(OnePrompt);

        Assert.Equal(1.0, report.CitationRate);
        Assert.Equal(1.0, report.AverageRank);
        Assert.Contains("Cited in 100%", report.Summary);
    }

    [Fact]
    public async Task NeverCited_ReportsZeroAndNamesTheRival()
    {
        var probe = new FakeProbe("Claude", Answer("Try Amazon.", "https://amazon.com/kettles"));

        var report = await Tracker(probe).RunAsync(OnePrompt);

        Assert.Equal(0, report.CitationRate);
        Assert.Null(report.AverageRank);

        // A store losing every question still needs to be told who is winning them.
        var rival = Assert.Single(report.Rivals);
        Assert.Equal("amazon.com", rival.Domain);
        Assert.Contains("amazon.com", report.Summary);
    }

    [Fact]
    public async Task NameOnlyMentions_CountSeparatelyFromCitations()
    {
        // The distinction the merchant is paying to trust: being talked about is not the
        // same as being linked to.
        var probe = new FakeProbe("Claude", Answer("KettleCo make good ones.", "https://amazon.com/k"));

        var report = await Tracker(probe).RunAsync(OnePrompt);

        Assert.Equal(0, report.CitationRate);
        Assert.Equal(1.0, report.AnyMentionRate);
    }

    [Fact]
    public async Task FailedProbes_AreExcludedFromRatesRatherThanCountedAsMisses()
    {
        var prompts = new[]
        {
            new ShopperPrompt("best kettles?", "Kettles"),
            new ShopperPrompt("where to buy kettles?", "Kettles")
        };

        var probe = new FakeProbe(
            "Claude",
            Answer("Try KettleCo.", "https://kettleco.example/"),
            AssistantAnswer.Failed("Claude", "rate limited"));

        var report = await Tracker(probe).RunAsync(prompts);

        Assert.Equal(1, report.FailedProbes);
        Assert.Single(report.Answered);
        Assert.Equal(1.0, report.CitationRate);
    }

    [Fact]
    public async Task AllProbesFailing_ProducesAnHonestSummary()
    {
        var probe = new FakeProbe("Claude", AssistantAnswer.Failed("Claude", "no api key"));

        var report = await Tracker(probe).RunAsync(OnePrompt);

        Assert.Equal(0, report.CitationRate);
        Assert.Contains("No assistant answered", report.Summary);
    }

    [Fact]
    public async Task RivalsAreRankedByHowOftenTheyOutrankTheStore()
    {
        var prompts = Enumerable.Range(0, 2)
            .Select(i => new ShopperPrompt($"q{i}", "Kettles"))
            .ToList();

        var probe = new FakeProbe(
            "Claude",
            Answer("x", "https://amazon.com/a", "https://kettleco.example/"),
            Answer("x", "https://amazon.com/b", "https://kettleco.example/"));

        var report = await Tracker(probe).RunAsync(prompts);

        var rival = Assert.Single(report.Rivals);
        Assert.Equal("amazon.com", rival.Domain);
        Assert.Equal(2, rival.TimesAhead);
    }

    [Fact]
    public async Task TheStoreItselfIsNeverListedAsItsOwnRival()
    {
        var probe = new FakeProbe("Claude", Answer("x", "https://kettleco.example/", "https://amazon.com/a"));

        var report = await Tracker(probe).RunAsync(OnePrompt);

        Assert.DoesNotContain(report.Rivals, r => r.Domain.Contains("kettleco"));
    }

    [Fact]
    public async Task EveryProbeIsAskedEveryPrompt()
    {
        var prompts = new[]
        {
            new ShopperPrompt("q1", "Kettles"),
            new ShopperPrompt("q2", "Kettles")
        };

        var report = await Tracker(new FakeProbe("Claude"), new FakeProbe("Perplexity")).RunAsync(prompts);

        Assert.Equal(4, report.Outcomes.Count);
        Assert.Equal(2, report.Outcomes.Select(o => o.Engine).Distinct().Count());
    }
}

public class PromptGeneratorTests
{
    private static CatalogProduct Product(string title, string? type) =>
        new(title, title.ToLowerInvariant(), new Uri("https://kettleco.example/products/x"), string.Empty, "KettleCo", type);

    [Fact]
    public void PromptsAreBuiltFromTheStoresOwnProductTypes()
    {
        var prompts = PromptGenerator.ForCatalogue(new[] { Product("Gooseneck", "Kettles") });

        Assert.Equal(3, prompts.Count);
        Assert.All(prompts, p => Assert.Equal("Kettles", p.Category));
        Assert.All(prompts, p => Assert.Contains("kettles", p.Text));
    }

    [Fact]
    public void PromptsNeverNameTheBrand()
    {
        // A branded query proves nothing — the assistant will always find a store by name.
        var prompts = PromptGenerator.ForCatalogue(new[] { Product("Gooseneck", "Kettles") });

        Assert.All(prompts, p => Assert.DoesNotContain("KettleCo", p.Text, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MostCommonCategoriesWin_AndTheLimitIsRespected()
    {
        var products = new[]
        {
            Product("A", "Kettles"),
            Product("B", "Kettles"),
            Product("C", "Mugs")
        };

        var prompts = PromptGenerator.ForCatalogue(products, maxCategories: 1);

        Assert.Equal(3, prompts.Count);
        Assert.All(prompts, p => Assert.Equal("Kettles", p.Category));
    }

    [Fact]
    public void StoreWithNoProductTypes_FallsBackToTitles()
    {
        var prompts = PromptGenerator.ForCatalogue(new[] { Product("Gooseneck kettle", null) }, maxCategories: 1);

        Assert.NotEmpty(prompts);
        Assert.All(prompts, p => Assert.Contains("gooseneck kettle", p.Text));
    }

    [Fact]
    public void EmptyCatalogue_ProducesNoPrompts()
    {
        Assert.Empty(PromptGenerator.ForCatalogue(Array.Empty<CatalogProduct>()));
    }
}
