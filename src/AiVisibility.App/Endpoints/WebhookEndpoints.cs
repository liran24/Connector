using AiVisibility.App.Data;
using AiVisibility.App.Shopify;
using Microsoft.EntityFrameworkCore;

namespace AiVisibility.App.Endpoints;

/// <summary>
/// Receives webhooks from Shopify.
/// </summary>
/// <remarks>
/// Two rules govern every handler here.
/// <para>
/// Verify before parsing. The HMAC covers the raw bytes, so the body is buffered and checked
/// before anything is deserialised. An unverified webhook is an anonymous internet request.
/// </para>
/// <para>
/// Answer quickly. Shopify times out after five seconds and retries, so a handler records
/// what happened and returns; real work belongs on a background job.
/// </para>
/// </remarks>
public static class WebhookEndpoints
{
    /// <summary>Header carrying the base64 HMAC of the request body.</summary>
    private const string SignatureHeader = "X-Shopify-Hmac-Sha256";

    /// <summary>Header naming the shop the webhook is about.</summary>
    private const string ShopHeader = "X-Shopify-Shop-Domain";

    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        var webhooks = app.MapGroup("/shopify/webhooks");

        webhooks.MapPost("/app-uninstalled", HandleAppUninstalled);

        // The three Shopify requires of every app. An app that does not answer all three
        // fails App Review, whether or not it stores customer data.
        webhooks.MapPost("/customers/data-request", HandleCustomerDataRequest);
        webhooks.MapPost("/customers/redact", HandleCustomerRedact);
        webhooks.MapPost("/shop/redact", HandleShopRedact);

        return app;
    }

    /// <summary>
    /// The merchant removed the app. Shopify has already revoked the token, so the row is
    /// marked uninstalled rather than deleted — <c>shop/redact</c> arrives later to erase it.
    /// </summary>
    private static async Task<IResult> HandleAppUninstalled(HttpContext context, WebhookRequestReader reader)
    {
        var request = await reader.ReadAsync(context).ConfigureAwait(false);
        if (!request.IsVerified)
        {
            return Results.Unauthorized();
        }

        var shop = await reader.Database.Shops
            .FirstOrDefaultAsync(s => s.Domain == request.Shop!.Value, context.RequestAborted)
            .ConfigureAwait(false);

        if (shop is not null)
        {
            shop.UninstalledAt = DateTimeOffset.UtcNow;
            shop.SubscriptionStatus = SubscriptionStatus.Inactive;
            await reader.Database.SaveChangesAsync(context.RequestAborted).ConfigureAwait(false);
            reader.Logger.LogInformation("{Shop} uninstalled the app.", shop.Domain);
        }

        return Results.Ok();
    }

    /// <summary>
    /// A shopper asked what personal data the app holds on them.
    /// </summary>
    /// <remarks>
    /// This app stores no shopper data — only storefront-level scan results — so there is
    /// nothing to return. The endpoint still has to exist and acknowledge, and this comment is
    /// the answer to give App Review when they ask.
    /// </remarks>
    private static async Task<IResult> HandleCustomerDataRequest(HttpContext context, WebhookRequestReader reader)
    {
        var request = await reader.ReadAsync(context).ConfigureAwait(false);
        if (!request.IsVerified)
        {
            return Results.Unauthorized();
        }

        reader.Logger.LogInformation(
            "Customer data request for {Shop}: no shopper data is stored by this app.",
            request.Shop!.Value);

        return Results.Ok();
    }

    /// <summary>
    /// A shopper asked to be erased. Nothing to erase, for the reason above.
    /// </summary>
    private static async Task<IResult> HandleCustomerRedact(HttpContext context, WebhookRequestReader reader)
    {
        var request = await reader.ReadAsync(context).ConfigureAwait(false);
        if (!request.IsVerified)
        {
            return Results.Unauthorized();
        }

        reader.Logger.LogInformation(
            "Customer redact for {Shop}: no shopper data is stored by this app.",
            request.Shop!.Value);

        return Results.Ok();
    }

    /// <summary>
    /// Arrives 48 hours after an uninstall: erase everything held about this shop.
    /// </summary>
    /// <remarks>
    /// This is a real deletion, not a soft one. Stored scans go with the shop row through the
    /// cascade configured in the DbContext.
    /// </remarks>
    private static async Task<IResult> HandleShopRedact(HttpContext context, WebhookRequestReader reader)
    {
        var request = await reader.ReadAsync(context).ConfigureAwait(false);
        if (!request.IsVerified)
        {
            return Results.Unauthorized();
        }

        var shop = await reader.Database.Shops
            .FirstOrDefaultAsync(s => s.Domain == request.Shop!.Value, context.RequestAborted)
            .ConfigureAwait(false);

        if (shop is not null)
        {
            reader.Database.Shops.Remove(shop);
            await reader.Database.SaveChangesAsync(context.RequestAborted).ConfigureAwait(false);
            reader.Logger.LogInformation("Erased all data for {Shop}.", shop.Domain);
        }

        return Results.Ok();
    }
}
