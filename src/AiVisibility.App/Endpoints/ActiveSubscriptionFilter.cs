using AiVisibility.App.Data;
using Microsoft.EntityFrameworkCore;

namespace AiVisibility.App.Endpoints;

/// <summary>
/// Blocks work that costs money from shops that are not paying.
/// </summary>
/// <remarks>
/// Applied to the routes that make outbound requests — scanning fetches pages from a
/// storefront, tracking spends API credit. Read-only routes stay open so a merchant who has
/// let their subscription lapse can still see the reports they already paid for, which is
/// both fairer and a better argument for resubscribing than a locked screen.
/// <para>
/// Returns 402 rather than 403: the dashboard uses that specific code to show the subscribe
/// prompt instead of an error.
/// </para>
/// </remarks>
public sealed class ActiveSubscriptionFilter : IEndpointFilter
{
    private readonly AiVisibilityDbContext _database;

    public ActiveSubscriptionFilter(AiVisibilityDbContext database) => _database = database;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var shop = context.HttpContext.GetShop();

        var installed = await _database.Shops
            .FirstOrDefaultAsync(s => s.Domain == shop.Value, context.HttpContext.RequestAborted)
            .ConfigureAwait(false);

        if (installed is null)
        {
            return Results.NotFound();
        }

        if (!installed.IsActive)
        {
            return Results.Json(
                new { error = "subscription_required", subscriptionStatus = installed.SubscriptionStatus.ToString().ToLowerInvariant() },
                statusCode: StatusCodes.Status402PaymentRequired);
        }

        return await next(context).ConfigureAwait(false);
    }
}
