using AiVisibility.App.Configuration;
using AiVisibility.App.Data;
using AiVisibility.App.Security;
using AiVisibility.App.Shopify;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AiVisibility.App.Endpoints;

/// <summary>
/// The install flow: <c>/shopify/install</c> sends the merchant to Shopify, and
/// <c>/shopify/callback</c> receives them back with an authorisation code.
/// </summary>
public static class InstallEndpoints
{
    public static IEndpointRouteBuilder MapInstallEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/shopify/install", StartInstall);
        app.MapGet("/shopify/callback", CompleteInstall);
        return app;
    }

    /// <summary>
    /// Redirects the merchant to Shopify's permission screen.
    /// </summary>
    /// <remarks>
    /// Shopify calls this with <c>?shop=</c> when a merchant clicks Install. The domain is
    /// parsed rather than trusted, because everything downstream is addressed to that host.
    /// </remarks>
    private static IResult StartInstall(
        HttpContext context,
        IOptions<ShopifyOptions> options,
        IInstallStateStore stateStore)
    {
        if (!ShopDomain.TryParse(context.Request.Query["shop"], out var shop))
        {
            return Results.BadRequest("Missing or invalid 'shop' parameter. Expected a *.myshopify.com domain.");
        }

        var settings = options.Value;
        var nonce = stateStore.Issue(shop);

        var authorizeUrl = QueryHelpers.Build($"https://{shop.Value}/admin/oauth/authorize", new()
        {
            ["client_id"] = settings.ApiKey,
            ["scope"] = settings.ScopeParameter,
            ["redirect_uri"] = settings.CallbackUrl.ToString(),
            ["state"] = nonce
        });

        return Results.Redirect(authorizeUrl);
    }

    /// <summary>
    /// Completes the install: verifies the request, exchanges the code, and stores the shop.
    /// </summary>
    private static async Task<IResult> CompleteInstall(
        HttpContext context,
        IOptions<ShopifyOptions> options,
        ShopifySignature signature,
        IInstallStateStore stateStore,
        IAccessTokenExchange exchange,
        IAccessTokenProtector protector,
        AiVisibilityDbContext database,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(typeof(InstallEndpoints));
        var query = context.Request.Query
            .Select(q => new KeyValuePair<string, string>(q.Key, q.Value.ToString()))
            .ToList();

        if (!ShopDomain.TryParse(context.Request.Query["shop"], out var shop))
        {
            return Results.BadRequest("Missing or invalid 'shop' parameter.");
        }

        // Order matters: prove the request came from Shopify before acting on any of its values.
        if (!signature.IsValidQuery(query))
        {
            logger.LogWarning("Rejected install callback for {Shop}: bad HMAC.", shop.Value);
            return Results.Unauthorized();
        }

        if (!stateStore.TryRedeem(shop, context.Request.Query["state"]))
        {
            logger.LogWarning("Rejected install callback for {Shop}: unknown or reused state.", shop.Value);
            return Results.Unauthorized();
        }

        var code = context.Request.Query["code"].ToString();
        if (string.IsNullOrWhiteSpace(code))
        {
            return Results.BadRequest("Missing 'code' parameter.");
        }

        var grant = await exchange.ExchangeAsync(shop, code, cancellationToken).ConfigureAwait(false);
        if (grant is null)
        {
            return Results.Problem("Shopify declined the token exchange.", statusCode: StatusCodes.Status502BadGateway);
        }

        await SaveShopAsync(database, protector, shop, grant, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Installed {Shop}.", shop.Value);

        // Land the merchant inside their own admin, where the embedded app loads.
        return Results.Redirect($"https://{shop.Value}/admin/apps/{options.Value.ApiKey}");
    }

    /// <summary>
    /// Records the install, updating the existing row when a shop reinstalls so its scan
    /// history survives.
    /// </summary>
    private static async Task SaveShopAsync(
        AiVisibilityDbContext database,
        IAccessTokenProtector protector,
        ShopDomain shop,
        AccessGrant grant,
        CancellationToken cancellationToken)
    {
        var existing = await database.Shops
            .FirstOrDefaultAsync(s => s.Domain == shop.Value, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            database.Shops.Add(new InstalledShop
            {
                Domain = shop.Value,
                ProtectedAccessToken = protector.Protect(grant.AccessToken),
                GrantedScopes = grant.GrantedScopes
            });
        }
        else
        {
            existing.ProtectedAccessToken = protector.Protect(grant.AccessToken);
            existing.GrantedScopes = grant.GrantedScopes;
            existing.UninstalledAt = null;
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Builds URLs with escaped query strings.</summary>
internal static class QueryHelpers
{
    public static string Build(string baseUrl, Dictionary<string, string> parameters) =>
        baseUrl + "?" + string.Join("&", parameters
            .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
}
