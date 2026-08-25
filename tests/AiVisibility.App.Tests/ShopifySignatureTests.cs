using System.Security.Cryptography;
using System.Text;
using AiVisibility.App.Shopify;

namespace AiVisibility.App.Tests;

public class ShopifySignatureTests
{
    private const string Secret = "hush-this-is-the-app-secret";

    private static readonly ShopifySignature Signature = new(Secret);

    /// <summary>Signs a query the way Shopify does: sorted key=value pairs, lowercase hex.</summary>
    private static string SignQuery(params (string Key, string Value)[] parameters)
    {
        var message = string.Join("&", parameters
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"{p.Key}={p.Value}"));

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(message))).ToLowerInvariant();
    }

    /// <summary>Signs a webhook body the way Shopify does: base64 over the raw bytes.</summary>
    private static string SignBody(byte[] body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        return Convert.ToBase64String(hmac.ComputeHash(body));
    }

    private static List<KeyValuePair<string, string>> Query(params (string Key, string Value)[] parameters) =>
        parameters.Select(p => new KeyValuePair<string, string>(p.Key, p.Value)).ToList();

    [Fact]
    public void AGenuineOAuthRedirect_IsAccepted()
    {
        var hmac = SignQuery(("code", "abc123"), ("shop", "example.myshopify.com"), ("state", "nonce"));

        var valid = Signature.IsValidQuery(Query(
            ("code", "abc123"),
            ("shop", "example.myshopify.com"),
            ("state", "nonce"),
            ("hmac", hmac)));

        Assert.True(valid);
    }

    [Fact]
    public void ParameterOrderDoesNotMatter_BecauseShopifySortsBeforeSigning()
    {
        var hmac = SignQuery(("code", "abc123"), ("shop", "example.myshopify.com"));

        var valid = Signature.IsValidQuery(Query(
            ("hmac", hmac),
            ("shop", "example.myshopify.com"),
            ("code", "abc123")));

        Assert.True(valid);
    }

    [Fact]
    public void TamperingWithAnyParameter_InvalidatesTheSignature()
    {
        var hmac = SignQuery(("code", "abc123"), ("shop", "example.myshopify.com"));

        // An attacker swapping the shop is the whole reason this check exists.
        var valid = Signature.IsValidQuery(Query(
            ("code", "abc123"),
            ("shop", "attacker.myshopify.com"),
            ("hmac", hmac)));

        Assert.False(valid);
    }

    [Fact]
    public void AnExtraParameter_InvalidatesTheSignature()
    {
        var hmac = SignQuery(("code", "abc123"));

        var valid = Signature.IsValidQuery(Query(("code", "abc123"), ("injected", "1"), ("hmac", hmac)));

        Assert.False(valid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-signature")]
    public void AMissingOrJunkSignature_IsRejected(string hmac)
    {
        Assert.False(Signature.IsValidQuery(Query(("code", "abc123"), ("hmac", hmac))));
    }

    [Fact]
    public void AQueryWithNoHmacAtAll_IsRejected()
    {
        Assert.False(Signature.IsValidQuery(Query(("code", "abc123"))));
    }

    [Fact]
    public void SignatureVerificationIsCaseInsensitiveOnTheParameterName()
    {
        var hmac = SignQuery(("code", "abc123"));

        Assert.True(Signature.IsValidQuery(Query(("code", "abc123"), ("HMAC", hmac))));
    }

    [Fact]
    public void AGenuineWebhook_IsAccepted()
    {
        var body = Encoding.UTF8.GetBytes("""{"id":123,"domain":"example.myshopify.com"}""");

        Assert.True(Signature.IsValidWebhook(body, SignBody(body)));
    }

    [Fact]
    public void AWebhookBodyChangedByOneByte_IsRejected()
    {
        var body = Encoding.UTF8.GetBytes("""{"id":123}""");
        var header = SignBody(body);

        var tampered = Encoding.UTF8.GetBytes("""{"id":124}""");

        Assert.False(Signature.IsValidWebhook(tampered, header));
    }

    [Fact]
    public void AWebhookSignedWithAnotherSecret_IsRejected()
    {
        var body = Encoding.UTF8.GetBytes("""{"id":123}""");

        using var wrongKey = new HMACSHA256(Encoding.UTF8.GetBytes("someone-elses-secret"));
        var forged = Convert.ToBase64String(wrongKey.ComputeHash(body));

        Assert.False(Signature.IsValidWebhook(body, forged));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AWebhookWithNoSignatureHeader_IsRejected(string? header)
    {
        Assert.False(Signature.IsValidWebhook(Encoding.UTF8.GetBytes("{}"), header));
    }

    [Fact]
    public void AnEmptyWebhookBody_StillVerifiesAgainstItsOwnSignature()
    {
        var empty = Array.Empty<byte>();

        Assert.True(Signature.IsValidWebhook(empty, SignBody(empty)));
    }
}
