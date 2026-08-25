using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AiVisibility.App.Configuration;
using Microsoft.Extensions.Options;

namespace AiVisibility.App.Shopify;

/// <summary>A charge waiting for the merchant to approve it.</summary>
/// <param name="ChargeId">Shopify's id for the charge, stored so approval can be confirmed later.</param>
/// <param name="ApprovalUrl">Where to send the merchant to accept it.</param>
public sealed record PendingSubscription(long ChargeId, Uri ApprovalUrl);

/// <summary>
/// Creates and confirms recurring charges through Shopify's Billing API.
/// </summary>
/// <remarks>
/// Shopify requires all charges to go through this API. Billing a merchant any other way is
/// grounds for removal from the App Store, so there is no alternative path in this codebase.
/// </remarks>
public interface ISubscriptionService
{
    /// <returns>The pending charge, or null when Shopify refused to create one.</returns>
    Task<PendingSubscription?> CreateAsync(ShopDomain shop, string accessToken, CancellationToken cancellationToken = default);

    /// <summary>Reads a charge's current status, to confirm the merchant actually approved it.</summary>
    Task<string?> GetStatusAsync(ShopDomain shop, string accessToken, long chargeId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Talks to the REST Billing API.
/// </summary>
public sealed class SubscriptionService : ISubscriptionService
{
    private const string AccessTokenHeader = "X-Shopify-Access-Token";

    private readonly HttpClient _http;
    private readonly ShopifyOptions _shopify;
    private readonly BillingOptions _billing;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        HttpClient http,
        IOptions<ShopifyOptions> shopify,
        IOptions<BillingOptions> billing,
        ILogger<SubscriptionService> logger)
    {
        _http = http;
        _shopify = shopify.Value;
        _billing = billing.Value;
        _logger = logger;
    }

    public async Task<PendingSubscription?> CreateAsync(
        ShopDomain shop,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        var charge = new
        {
            recurring_application_charge = new
            {
                name = _billing.PlanName,
                price = _billing.MonthlyPrice,
                return_url = new Uri(new Uri(_shopify.AppUrl), "/billing/confirm").ToString(),
                trial_days = _billing.TrialDays,
                test = _billing.UseTestCharges ? true : (bool?)null
            }
        };

        using var request = Authorised(HttpMethod.Post, shop, "recurring_application_charges.json", accessToken);
        request.Content = JsonContent.Create(charge);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Could not create a charge for {Shop}: {StatusCode}.", shop.Value, response.StatusCode);
            return null;
        }

        return ReadPendingSubscription(
            await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken).ConfigureAwait(false),
            shop);
    }

    public async Task<string?> GetStatusAsync(
        ShopDomain shop,
        string accessToken,
        long chargeId,
        CancellationToken cancellationToken = default)
    {
        using var request = Authorised(
            HttpMethod.Get, shop, $"recurring_application_charges/{chargeId}.json", accessToken);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content
            .ReadFromJsonAsync<JsonElement>(cancellationToken)
            .ConfigureAwait(false);

        return payload.TryGetProperty("recurring_application_charge", out var node)
            && node.TryGetProperty("status", out var status)
                ? status.GetString()
                : null;
    }

    private PendingSubscription? ReadPendingSubscription(JsonElement payload, ShopDomain shop)
    {
        if (!payload.TryGetProperty("recurring_application_charge", out var charge)
            || !charge.TryGetProperty("id", out var id)
            || !charge.TryGetProperty("confirmation_url", out var url)
            || !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var approvalUrl))
        {
            _logger.LogWarning("Charge response for {Shop} was missing an id or confirmation URL.", shop.Value);
            return null;
        }

        return new PendingSubscription(id.GetInt64(), approvalUrl);
    }

    /// <summary>Builds an Admin API request carrying the shop's access token.</summary>
    private HttpRequestMessage Authorised(HttpMethod method, ShopDomain shop, string path, string accessToken)
    {
        var request = new HttpRequestMessage(method, new Uri(shop.AdminApiUrl(_shopify.ApiVersion), path));
        request.Headers.Add(AccessTokenHeader, accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }
}
