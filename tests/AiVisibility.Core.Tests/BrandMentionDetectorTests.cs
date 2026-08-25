using AiVisibility.Core.Tracking;

namespace AiVisibility.Core.Tests;

public class BrandMentionDetectorTests
{
    private static readonly Uri StoreUrl = new("https://kettleco.example");

    private static BrandMentionDetector Detector(params string[] brands) =>
        new(StoreUrl, brands.Length == 0 ? new[] { "KettleCo" } : brands);

    private static AssistantAnswer Answer(string text, params string[] citedUrls) =>
        new("Claude", text, citedUrls.Select(u => new Citation(new Uri(u), null)).ToList());

    [Fact]
    public void StoreNeitherNamedNorCited_IsNotAMention()
    {
        var mention = Detector().Analyse(Answer("Try Amazon.", "https://amazon.com/kettles"));

        Assert.False(mention.Mentioned);
        Assert.Equal(MentionEvidence.None, mention.Evidence);
        Assert.Null(mention.Rank);
    }

    [Fact]
    public void CitationOfTheStore_IsTheStrongestEvidence()
    {
        var mention = Detector().Analyse(Answer(
            "A few good options.",
            "https://amazon.com/kettles",
            "https://kettleco.example/products/gooseneck"));

        Assert.Equal(MentionEvidence.Cited, mention.Evidence);
        Assert.Equal(2, mention.Rank);
        Assert.Equal(new[] { "amazon.com" }, mention.DomainsAhead);
    }

    [Fact]
    public void NameWithoutCitation_IsRecordedButNotRanked()
    {
        // An assistant can name a brand while sending the shopper somewhere else entirely,
        // so this must stay distinguishable from a citation.
        var mention = Detector().Analyse(Answer("KettleCo make good ones.", "https://amazon.com/kettles"));

        Assert.Equal(MentionEvidence.NameOnly, mention.Evidence);
        Assert.True(mention.Mentioned);
        Assert.Null(mention.Rank);
    }

    [Fact]
    public void NamedAndCited_IsReportedAsBoth()
    {
        var mention = Detector().Analyse(Answer("KettleCo is worth a look.", "https://kettleco.example/"));

        Assert.Equal(MentionEvidence.NamedAndCited, mention.Evidence);
        Assert.Equal(1, mention.Rank);
    }

    [Fact]
    public void BrandNameInsideAnotherWord_IsNotAMatch()
    {
        // The failure that would quietly inflate every merchant's numbers.
        var mention = new BrandMentionDetector(StoreUrl, new[] { "Ada" })
            .Analyse(Answer("Use a suitable adaptor and adapt the settings."));

        Assert.Equal(MentionEvidence.None, mention.Evidence);
    }

    [Fact]
    public void BrandNameAdjacentToPunctuation_StillMatches()
    {
        var mention = Detector().Analyse(Answer("Options include KettleCo, Amazon, and others."));

        Assert.Equal(MentionEvidence.NameOnly, mention.Evidence);
    }

    [Fact]
    public void BrandNameMatching_IsCaseInsensitive()
    {
        var mention = Detector().Analyse(Answer("kettleco sells them."));

        Assert.Equal(MentionEvidence.NameOnly, mention.Evidence);
    }

    [Fact]
    public void MultiWordBrand_ToleratesExtraWhitespace()
    {
        var mention = new BrandMentionDetector(StoreUrl, new[] { "Death Wish Coffee" })
            .Analyse(Answer("Death  Wish\nCoffee is very strong."));

        Assert.Equal(MentionEvidence.NameOnly, mention.Evidence);
    }

    [Fact]
    public void VeryShortBrandNames_AreIgnoredEntirely()
    {
        // A two-letter name matches too much ordinary prose to be evidence of anything.
        var mention = new BrandMentionDetector(StoreUrl, new[] { "Co" })
            .Analyse(Answer("Co is mentioned here."));

        Assert.Equal(MentionEvidence.None, mention.Evidence);
    }

    [Fact]
    public void WwwPrefix_DoesNotBreakStoreRecognition()
    {
        var mention = Detector().Analyse(Answer("Here.", "https://www.kettleco.example/products/x"));

        Assert.Equal(1, mention.Rank);
    }

    [Fact]
    public void SubdomainOfTheStore_CountsAsTheStore()
    {
        var mention = Detector().Analyse(Answer("Here.", "https://shop.kettleco.example/products/x"));

        Assert.Equal(1, mention.Rank);
    }

    [Fact]
    public void RepeatedCitationsOfOneSite_CountOnceForRanking()
    {
        var mention = Detector().Analyse(Answer(
            "Here.",
            "https://amazon.com/a",
            "https://amazon.com/b",
            "https://kettleco.example/"));

        Assert.Equal(2, mention.Rank);
        Assert.Equal(new[] { "amazon.com", "kettleco.example" }, mention.CitedDomains);
    }

    [Fact]
    public void BlankBrandNames_AreDiscarded()
    {
        var mention = new BrandMentionDetector(StoreUrl, new[] { "", "   ", "KettleCo" })
            .Analyse(Answer("KettleCo sells them."));

        Assert.Equal(MentionEvidence.NameOnly, mention.Evidence);
    }

    [Fact]
    public void AnswerWithNoCitations_RanksNothing()
    {
        var mention = Detector().Analyse(Answer("I am not sure where to buy those."));

        Assert.Empty(mention.CitedDomains);
        Assert.Null(mention.Rank);
    }
}
