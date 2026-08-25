using AiVisibility.App.Configuration;
using AiVisibility.App.Data;
using AiVisibility.App.Security;
using AiVisibility.App.Shopify;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AiVisibility.App.Endpoints;

/// <summary>Where to send the merchant to approve a subscription.</summary>
public sealed record SubscribeResponse(string ApprovalUrl);

/// <summary>The plan on offer, so the dashboard can state the price it is asking for.</summary>
public sealed record PlanResponse(string Name, decimal MonthlyPrice, string Currency, int TrialDays, bool IsTest);

/// <summary>
/// Subscription routes.
/// </summary>
/// <remarks>
/// Two different kinds of request live here, authenticated two different ways:
/// <list type="bullet">
/// <item><c>/api/billing/*</c> is called by the embedded dashboard and carries a session token.</item>
/// <item><c>/billing/confirm</c> is a top-level browser redirect from Shopify after the
/// merchant approves the charge. There is no session token on it, so it is verified by the
/// query HMAC instead.</item>
/// </list>
/// Getting that distinction wrong is how a confirm route ends up either unusable or open.
/// </remarks>
public static class BillingEndpoints
{
    public static IEndpointRouteBuilder MapBillingEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/billing").AddEndpointFilter<SessionTokenFilter>();
        api.MapGet("/plan", GetPlan);
        api.MapPost("/subscribe", Subscribe);

        // Not under /api: Shopify redirects the merchant's browser here, not the dashboard.
        app.MapGet("/billing/confirm", ConfirmSubscription);

        return app;
    }

    private static IResult GetPlan(IOptions<BillingOptions> billing)
    {
        var plan = billing.Value;
        return Results.Ok(new PlanResponse(
            plan.PlanName, plan.MonthlyPrice, plan.Currency, plan.TrialDays, plan.UseTestCharges));
    }

    /// <summary>
    /// Creates a charge and hands back the URL where the merchant approves it.
    /// </summary>
    /// <remarks>
    /// The charge id is stored now so <see cref="ConfirmSubscription"/> can check that the
    /// charge it is told about is the one this app actually created.
    /// </remarks>
    private static async Task<IResult> Subscribe(
        HttpContext context,
        AiVisibilityDbContext database,
        ISubscriptionService subscriptions,
        IAccessTokenProtector protector)
    {
        var shop = context.GetShop();

        var installed = await database.Shops
            .FirstOrDefaultAsync(s => s.Domain == shop.Value && s.UninstalledAt == null, context.RequestAborted)
            .ConfigureAwait(false);

        if (installed is null)
        {
            return Results.NotFound();
        }

        var token = protector.Unprotect(installed.ProtectedAccessToken);
        if (token is null)
        {
            // The key ring lost the keys that encrypted this token. The shop must reinstall.
            return Results.Problem(
                "This shop's credentials could not be read. Please reinstall the app.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var pending = await subscriptions.CreateAsync(shop, token, context.RequestAborted).ConfigureAwait(false);
        if (pending is null)
        {
            return Results.Problem("Shopify would not create the charge.", statusCode: StatusCodes.Status502BadGateway);
        }

        installed.SubscriptionChargeId = pending.ChargeId;
        installed.SubscriptionStatus = SubscriptionStatus.PendingApproval;
        await database.SaveChangesAsync(context.RequestAborted).ConfigureAwait(false);

        return Results.Ok(new SubscribeResponse(pending.ApprovalUrl.ToString()));
    }

    /// <summary>
    /// Shopify sends the merchant here after they accept or decline the charge.
    /// </summary>
    /// <remarks>
    /// The status is read back from Shopify rather than taken from the query string. A
    /// redirect a merchant can edit is not evidence that they paid.
    /// </remarks>
    private static async Task<IResult> ConfirmSubscription(
        HttpContext context,
        AiVisibilityDbContext database,
        ISubscriptionService subscriptions,
        IAccessTokenProtector protector,
        ShopifySignature signature,
        IOptions<ShopifyOptions> shopify,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger(typeof(BillingEndpoints));

        if (!ShopDomain.TryParse(context.Request.Query["shop"], out var shop))
        {
            return Results.BadRequest("Missing or invalid 'shop' parameter.");
        }

        var query = context.Request.Query
            .Select(q => new KeyValuePair<string, string>(q.Key, q.Value.ToString()))
            .ToList();

        if (!signature.IsValidQuery(query))
        {
            logger.LogWarning("Rejected billing confirmation for {Shop}: bad HMAC.", shop.Value);
            return Results.Unauthorized();
        }

        var installed = await database.Shops
            .FirstOrDefaultAsync(s => s.Domain == shop.Value, context.RequestAborted)
            .ConfigureAwait(false);

        if (installed?.SubscriptionChargeId is null)
        {
            return Results.BadRequest("This shop has no pending charge.");
        }

        // The charge named in the redirect must be the one this app created for this shop.
        if (!long.TryParse(context.Request.Query["charge_id"], out var chargeId)
            || chargeId != installed.SubscriptionChargeId)
        {
            logger.LogWarning("Billing confirmation for {Shop} named an unexpected charge.", shop.Value);
            return Results.BadRequest("Unexpected charge.");
        }

        await ApplyChargeStatusAsync(context, database, subscriptions, protector, shop, installed, chargeId, logger)
            .ConfigureAwait(false);

        // Back into the merchant's admin, where the embedded dashboard loads.
        return Results.Redirect($"https://{shop.Value}/admin/apps/{shopify.Value.ApiKey}");
    }

    /// <summary>
    /// Asks Shopify what the charge is really worth and records it.
    /// </summary>
    private static async Task ApplyChargeStatusAsync(
        HttpContext context,
        AiVisibilityDbContext database,
        ISubscriptionService subscriptions,
        IAccessTokenProtector protector,
        ShopDomain shop,
        InstalledShop installed,
        long chargeId,
        ILogger logger)
    {
        var token = protector.Unprotect(installed.ProtectedAccessToken);
        if (token is null)
        {
            logger.LogError("Could not decrypt the token for {Shop} while confirming billing.", shop.Value);
            return;
        }

        var status = await subscriptions
            .GetStatusAsync(shop, token, chargeId, context.RequestAborted)
            .ConfigureAwait(false);

        installed.SubscriptionStatus = status switch
        {
            "active" => SubscriptionStatus.Active,
            // Shopify reports a trialing subscription as active with trial days remaining, so
            // an explicit trial status is honoured when it appears but never assumed.
            "pending" => SubscriptionStatus.PendingApproval,
            "declined" or "cancelled" or "expired" or "frozen" => SubscriptionStatus.Inactive,
            _ => SubscriptionStatus.Inactive
        };

        await database.SaveChangesAsync(context.RequestAborted).ConfigureAwait(false);
        logger.LogInformation("{Shop} subscription is now {Status}.", shop.Value, installed.SubscriptionStatus);
    }
}
