using AiVisibility.App.Shopify;

namespace AiVisibility.App.Tests;

public class ShopDomainTests
{
    [Theory]
    [InlineData("example.myshopify.com")]
    [InlineData("my-store-123.myshopify.com")]
    [InlineData("a.myshopify.com")]
    public void RealShopDomains_AreAccepted(string candidate)
    {
        Assert.True(ShopDomain.TryParse(candidate, out var shop));
        Assert.Equal(candidate, shop.Value);
    }

    [Theory]
    // The attack this type exists to stop: anything that would send a token elsewhere.
    [InlineData("evil.com")]
    [InlineData("example.myshopify.com.evil.com")]
    [InlineData("https://evil.com@example.myshopify.com")]
    [InlineData("example.myshopify.com/../../evil")]
    [InlineData("example.myshopify.com:8080")]
    [InlineData("example.myshopify.com/admin")]
    [InlineData("https://example.myshopify.com")]
    [InlineData("myshopify.com")]
    [InlineData(".myshopify.com")]
    [InlineData("-bad.myshopify.com")]
    [InlineData("exa mple.myshopify.com")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void AnythingElse_IsRejected(string? candidate)
    {
        Assert.False(ShopDomain.TryParse(candidate, out var shop));
        Assert.Null(shop);
    }

    [Theory]
    [InlineData("  Example.MyShopify.com  ")]
    [InlineData("example.myshopify.com\n")]
    [InlineData("\texample.myshopify.com\r\n")]
    public void SurroundingWhitespaceIsTrimmedAndCaseIsNormalised(string candidate)
    {
        Assert.True(ShopDomain.TryParse(candidate, out var shop));
        Assert.Equal("example.myshopify.com", shop.Value);
    }

    [Theory]
    [InlineData("example.myshopify.com\nHost: evil.com")]
    [InlineData("example\n.myshopify.com")]
    [InlineData("example.myshopify.com\r\nSet-Cookie: x=1")]
    public void ANewlineInsideTheValue_IsRejected(string candidate)
    {
        // A newline that survived into an outbound request header would be an injection
        // primitive, so the pattern is anchored with \z rather than $.
        Assert.False(ShopDomain.TryParse(candidate, out _));
    }

    [Fact]
    public void UrlsAreBuiltFromTheVerifiedDomain()
    {
        var shop = ShopDomain.Parse("example.myshopify.com");

        Assert.Equal("https://example.myshopify.com/", shop.StorefrontUrl.ToString());
        Assert.Equal("https://example.myshopify.com/admin/api/2025-07/", shop.AdminApiUrl("2025-07").ToString());
    }

    [Fact]
    public void ParseThrowsOnRubbish_SoAStoredValueCannotSilentlyBecomeATarget()
    {
        Assert.Throws<ArgumentException>(() => ShopDomain.Parse("evil.com"));
    }

    [Fact]
    public void EqualityIsByValue()
    {
        Assert.Equal(ShopDomain.Parse("example.myshopify.com"), ShopDomain.Parse("EXAMPLE.myshopify.com"));
    }
}
