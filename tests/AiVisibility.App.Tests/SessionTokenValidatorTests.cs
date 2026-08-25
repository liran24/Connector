using System.Security.Claims;
using System.Text;
using AiVisibility.App.Configuration;
using AiVisibility.App.Shopify;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AiVisibility.App.Tests;

public class SessionTokenValidatorTests
{
    private const string ApiKey = "this-apps-client-id";
    private const string ApiSecret = "this-apps-client-secret-which-is-long-enough";

    private static SessionTokenValidator Validator() => new(
        Options.Create(new ShopifyOptions
        {
            ApiKey = ApiKey,
            ApiSecret = ApiSecret,
            AppUrl = "https://app.example.com",
            Scopes = new[] { "read_products" }
        }),
        NullLogger<SessionTokenValidator>.Instance);

    /// <summary>Mints a token the way App Bridge does, with knobs for each failure case.</summary>
    private static string MintToken(
        string secret = ApiSecret,
        string audience = ApiKey,
        string destination = "https://example.myshopify.com",
        DateTime? expires = null,
        bool includeDestination = true)
    {
        var claims = new List<Claim> { new("sub", "user-1") };
        if (includeDestination)
        {
            claims.Add(new Claim("dest", destination));
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Audience = audience,
            Issuer = $"{destination}/admin",
            Expires = expires ?? DateTime.UtcNow.AddMinutes(1),
            NotBefore = DateTime.UtcNow.AddSeconds(-5),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                SecurityAlgorithms.HmacSha256)
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    [Fact]
    public async Task AGenuineTokenYieldsTheShopItNames()
    {
        var shop = await Validator().ValidateAsync(MintToken());

        Assert.NotNull(shop);
        Assert.Equal("example.myshopify.com", shop.Value);
    }

    [Fact]
    public async Task ATokenSignedWithAnotherSecret_IsRejected()
    {
        // Forging one of these would be free access to a merchant's data.
        Assert.Null(await Validator().ValidateAsync(MintToken(secret: "someone-elses-secret-long-enough")));
    }

    [Fact]
    public async Task ATokenMintedForAnotherApp_IsRejected()
    {
        Assert.Null(await Validator().ValidateAsync(MintToken(audience: "a-different-apps-client-id")));
    }

    [Fact]
    public async Task AnExpiredToken_IsRejected()
    {
        Assert.Null(await Validator().ValidateAsync(MintToken(expires: DateTime.UtcNow.AddMinutes(-5))));
    }

    [Fact]
    public async Task ATokenWithNoDestinationClaim_IsRejected()
    {
        Assert.Null(await Validator().ValidateAsync(MintToken(includeDestination: false)));
    }

    [Fact]
    public async Task ADestinationThatIsNotAShopifyDomain_IsRejected()
    {
        // A validly signed token still cannot name an arbitrary host as its shop.
        Assert.Null(await Validator().ValidateAsync(MintToken(destination: "https://evil.com")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-jwt")]
    [InlineData("aaa.bbb.ccc")]
    public async Task MissingOrMalformedTokens_AreRejected(string? token)
    {
        Assert.Null(await Validator().ValidateAsync(token));
    }

    [Fact]
    public async Task AnUnsignedTokenIsRejected_SoAlgorithmStrippingDoesNotWork()
    {
        // "alg": "none" is the classic JWT attack; the library refuses it.
        var header = Base64Url("""{"alg":"none","typ":"JWT"}""");
        var payload = Base64Url($$"""{"aud":"{{ApiKey}}","dest":"https://example.myshopify.com","exp":{{DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds()}}}""");

        Assert.Null(await Validator().ValidateAsync($"{header}.{payload}."));
    }

    private static string Base64Url(string json) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
