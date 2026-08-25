using AiVisibility.Core.Robots;

namespace AiVisibility.Core.Tests;

public class RobotsTxtParserTests
{
    [Fact]
    public void MissingFile_AllowsEverything()
    {
        var robots = RobotsTxtParser.AllowAll();

        Assert.True(robots.IsAllowed("GPTBot", "/products/anything"));
    }

    [Fact]
    public void WildcardGroup_AppliesToUnnamedCrawlers()
    {
        var robots = RobotsTxtParser.Parse("""
            User-agent: *
            Disallow: /admin
            """);

        Assert.False(robots.IsAllowed("GPTBot", "/admin/orders"));
        Assert.True(robots.IsAllowed("GPTBot", "/products/kettle"));
    }

    [Fact]
    public void NamedGroup_OverridesWildcard()
    {
        var robots = RobotsTxtParser.Parse("""
            User-agent: *
            Disallow:

            User-agent: GPTBot
            Disallow: /
            """);

        Assert.False(robots.IsAllowed("GPTBot", "/products/kettle"));
        Assert.True(robots.IsAllowed("PerplexityBot", "/products/kettle"));
    }

    [Fact]
    public void UserAgentMatching_IsCaseInsensitive()
    {
        var robots = RobotsTxtParser.Parse("""
            User-agent: gptbot
            Disallow: /
            """);

        Assert.False(robots.IsAllowed("GPTBot", "/"));
    }

    [Fact]
    public void ConsecutiveUserAgents_ShareOneRuleBlock()
    {
        var robots = RobotsTxtParser.Parse("""
            User-agent: GPTBot
            User-agent: ClaudeBot
            Disallow: /products
            """);

        Assert.False(robots.IsAllowed("GPTBot", "/products/kettle"));
        Assert.False(robots.IsAllowed("ClaudeBot", "/products/kettle"));
    }

    [Fact]
    public void EmptyDisallow_DoesNotBlock()
    {
        var robots = RobotsTxtParser.Parse("""
            User-agent: GPTBot
            Disallow:
            """);

        Assert.True(robots.IsAllowed("GPTBot", "/products/kettle"));
    }

    [Fact]
    public void LongerRuleWins_SoAllowCarvesOutOfDisallow()
    {
        var robots = RobotsTxtParser.Parse("""
            User-agent: GPTBot
            Disallow: /products
            Allow: /products/featured
            """);

        Assert.False(robots.IsAllowed("GPTBot", "/products/kettle"));
        Assert.True(robots.IsAllowed("GPTBot", "/products/featured/kettle"));
    }

    [Fact]
    public void EqualLengthRules_ResolveToAllow()
    {
        var robots = RobotsTxtParser.Parse("""
            User-agent: GPTBot
            Disallow: /shop
            Allow: /shop
            """);

        Assert.True(robots.IsAllowed("GPTBot", "/shop/kettle"));
    }

    [Fact]
    public void WildcardInPath_Matches()
    {
        var robots = RobotsTxtParser.Parse("""
            User-agent: GPTBot
            Disallow: /*/checkout
            """);

        Assert.False(robots.IsAllowed("GPTBot", "/en/checkout"));
        Assert.True(robots.IsAllowed("GPTBot", "/en/products"));
    }

    [Fact]
    public void EndAnchor_MatchesOnlyExactPath()
    {
        var robots = RobotsTxtParser.Parse("""
            User-agent: GPTBot
            Disallow: /cart$
            """);

        Assert.False(robots.IsAllowed("GPTBot", "/cart"));
        Assert.True(robots.IsAllowed("GPTBot", "/cart/items"));
    }

    [Fact]
    public void CommentsAndBlankLines_AreIgnored()
    {
        var robots = RobotsTxtParser.Parse("""
            # store rules

            User-agent: GPTBot   # the ChatGPT crawler
            Disallow: /admin

            """);

        Assert.False(robots.IsAllowed("GPTBot", "/admin"));
        Assert.True(robots.IsAllowed("GPTBot", "/products"));
    }

    [Fact]
    public void Sitemaps_AreCollected()
    {
        var robots = RobotsTxtParser.Parse("""
            User-agent: *
            Disallow:
            Sitemap: https://shop.example/sitemap.xml
            """);

        Assert.Equal("https://shop.example/sitemap.xml", Assert.Single(robots.Sitemaps));
    }

    [Fact]
    public void HasExplicitRuleFor_DistinguishesNamedFromWildcard()
    {
        var robots = RobotsTxtParser.Parse("""
            User-agent: *
            Disallow: /admin

            User-agent: GPTBot
            Disallow: /
            """);

        Assert.True(robots.HasExplicitRuleFor("GPTBot"));
        Assert.False(robots.HasExplicitRuleFor("PerplexityBot"));
    }
}
