using AiVisibility.App.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace AiVisibility.App.Shopify;

/// <summary>
/// Verifies the session token an embedded app sends with every API call.
/// </summary>
/// <remarks>
/// The dashboard runs inside an iframe in the merchant's admin, where cookies are unreliable.
/// Instead, App Bridge mints a short-lived JWT signed with the app secret, and the browser
/// sends it as <c>Authorization: Bearer</c>. Verifying it is what proves a request came from a
/// real, logged-in merchant rather than anyone who guessed an API route.
/// <para>
/// Validation uses a maintained JWT library rather than hand-rolled parsing: signature
/// stripping and algorithm confusion are exactly the mistakes a bespoke implementation makes.
/// </para>
/// </remarks>
public interface ISessionTokenValidator
{
    /// <summary>
    /// Checks a bearer token and extracts the shop it belongs to.
    /// </summary>
    /// <param name="token">The raw JWT, without the "Bearer " prefix.</param>
    /// <returns>The shop, or null when the token is missing, expired, forged or malformed.</returns>
    Task<ShopDomain?> ValidateAsync(string? token);
}

/// <summary>
/// Validates Shopify session tokens.
/// </summary>
public sealed class SessionTokenValidator : ISessionTokenValidator
{
    /// <summary>
    /// Tolerance for clock drift between Shopify's servers and this one. Shopify's tokens live
    /// about a minute, so this stays small — a generous window would widen the replay warning.
    /// </summary>
    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(10);

    private readonly ShopifyOptions _options;
    private readonly ILogger<SessionTokenValidator> _logger;
    private readonly JsonWebTokenHandler _handler = new();

    public SessionTokenValidator(IOptions<ShopifyOptions> options, ILogger<SessionTokenValidator> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ShopDomain?> ValidateAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var parameters = new TokenValidationParameters
        {
            // Signed with the app secret, so only Shopify and this app can mint one.
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.ApiSecret)),
            ValidateIssuerSigningKey = true,

            // The audience is this app: a token minted for a different app must not be accepted.
            ValidAudience = _options.ApiKey,
            ValidateAudience = true,

            // The issuer is the merchant's own shop, which is not known ahead of time, so it is
            // checked below against 'dest' rather than against a fixed list.
            ValidateIssuer = false,

            ValidateLifetime = true,
            ClockSkew = ClockSkew
        };

        var result = await _handler.ValidateTokenAsync(token, parameters).ConfigureAwait(false);

        if (!result.IsValid)
        {
            _logger.LogWarning("Rejected a session token: {Reason}.", result.Exception?.Message ?? "invalid");
            return null;
        }

        return ReadShop(result);
    }

    /// <summary>
    /// Reads the shop from the <c>dest</c> claim, which names the store the merchant is in.
    /// </summary>
    /// <remarks>
    /// <c>dest</c> is a URL like <c>https://example.myshopify.com</c>, so its host is parsed
    /// through <see cref="ShopDomain"/> like any other untrusted value.
    /// </remarks>
    private ShopDomain? ReadShop(TokenValidationResult result)
    {
        if (!result.Claims.TryGetValue("dest", out var destination)
            || destination is not string url
            || !Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            _logger.LogWarning("Session token had no usable 'dest' claim.");
            return null;
        }

        if (!ShopDomain.TryParse(parsed.Host, out var shop))
        {
            _logger.LogWarning("Session token 'dest' was not a Shopify domain.");
            return null;
        }

        return shop;
    }
}
