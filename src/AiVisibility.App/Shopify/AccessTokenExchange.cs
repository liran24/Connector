using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AiVisibility.App.Configuration;
using Microsoft.Extensions.Options;

namespace AiVisibility.App.Shopify;

/// <summary>What Shopify grants at the end of a successful install.</summary>
/// <param name="AccessToken">The Admin API token for this shop.</param>
/// <param name="GrantedScopes">Scopes actually granted, which can be narrower than requested.</param>
public sealed record AccessGrant(string AccessToken, string GrantedScopes);

/// <summary>
/// Trades the one-time authorisation code from an install for a lasting access token.
/// </summary>
public interface IAccessTokenExchange
{
    /// <returns>The grant, or null when Shopify rejected the exchange.</returns>
    Task<AccessGrant?> ExchangeAsync(ShopDomain shop, string authorizationCode, CancellationToken cancellationToken = default);
}

/// <summary>
/// Calls Shopify's token endpoint.
/// </summary>
public sealed class AccessTokenExchange : IAccessTokenExchange
{
    private readonly HttpClient _http;
    private readonly ShopifyOptions _options;
    private readonly ILogger<AccessTokenExchange> _logger;

    public AccessTokenExchange(HttpClient http, IOptions<ShopifyOptions> options, ILogger<AccessTokenExchange> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AccessGrant?> ExchangeAsync(
        ShopDomain shop,
        string authorizationCode,
        CancellationToken cancellationToken = default)
    {
        // The URL is built from a verified ShopDomain, so this request cannot be redirected to
        // an attacker's host by a crafted 'shop' parameter.
        var endpoint = new Uri($"https://{shop.Value}/admin/oauth/access_token");

        var request = new
        {
            client_id = _options.ApiKey,
            client_secret = _options.ApiSecret,
            code = authorizationCode
        };

        using var response = await _http.PostAsJsonAsync(endpoint, request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            // Never log the response body: a failed exchange can still echo the code.
            _logger.LogWarning("Token exchange for {Shop} failed with {StatusCode}.", shop.Value, response.StatusCode);
            return null;
        }

        var payload = await response.Content
            .ReadFromJsonAsync<TokenResponse>(cancellationToken)
            .ConfigureAwait(false);

        if (payload is null || string.IsNullOrWhiteSpace(payload.AccessToken))
        {
            _logger.LogWarning("Token exchange for {Shop} returned no access token.", shop.Value);
            return null;
        }

        return new AccessGrant(payload.AccessToken, payload.Scope ?? string.Empty);
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("scope")] string? Scope);
}
