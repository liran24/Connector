using System.Text.Json;
using AiVisibility.App.Data;
using AiVisibility.App.Scanning;
using AiVisibility.App.Shopify;
using Microsoft.EntityFrameworkCore;

namespace AiVisibility.App.Endpoints;

/// <summary>What the dashboard shows about the shop itself.</summary>
public sealed record ShopStatusResponse(
    string Domain,
    string SubscriptionStatus,
    bool IsActive,
    DateTimeOffset InstalledAt);

/// <summary>One point on the dashboard's score trend.</summary>
public sealed record ScanHistoryPoint(DateTimeOffset ScannedAt, int? Score);

/// <summary>
/// The JSON API behind the embedded dashboard.
/// </summary>
/// <remarks>
/// Every route here requires a valid Shopify session token, and the shop is taken from that
/// token rather than from anything the caller sends. A shop parameter in the request would be
/// an invitation to read another merchant's data.
/// </remarks>
public static class DashboardApiEndpoints
{
    public static IEndpointRouteBuilder MapDashboardApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").AddEndpointFilter<SessionTokenFilter>();

        api.MapGet("/shop", GetShop);
        api.MapGet("/scans/latest", GetLatestScan);
        api.MapGet("/scans/history", GetHistory);
        // Scanning makes a dozen outbound requests to a storefront, so it needs a paying
        // shop. Reading past reports does not, and stays open to a lapsed one.
        api.MapPost("/scans", RunScan).AddEndpointFilter<ActiveSubscriptionFilter>();

        return app;
    }

    private static async Task<IResult> GetShop(HttpContext context, AiVisibilityDbContext database)
    {
        var shop = context.GetShop();

        var installed = await database.Shops
            .FirstOrDefaultAsync(s => s.Domain == shop.Value, context.RequestAborted)
            .ConfigureAwait(false);

        if (installed is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new ShopStatusResponse(
            installed.Domain,
            installed.SubscriptionStatus.ToString().ToLowerInvariant(),
            installed.IsActive,
            installed.InstalledAt));
    }

    /// <summary>
    /// Returns the last stored report, or 204 when the shop has never been scanned — which is
    /// what the dashboard uses to decide whether to show its empty state.
    /// </summary>
    private static async Task<IResult> GetLatestScan(HttpContext context, IShopScanService scans)
    {
        var latest = await scans.GetLatestAsync(context.GetShop(), context.RequestAborted).ConfigureAwait(false);
        if (latest is null)
        {
            return Results.NoContent();
        }

        // Stored as JSON, so it is returned as-is rather than being re-serialised through the
        // current model — an old report keeps the shape it had when it was written.
        return Results.Content(latest.ResultJson, "application/json");
    }

    private static async Task<IResult> GetHistory(HttpContext context, IShopScanService scans)
    {
        var history = await scans.GetHistoryAsync(context.GetShop(), 30, context.RequestAborted).ConfigureAwait(false);

        return Results.Ok(history
            .Select(scan => new ScanHistoryPoint(scan.ScannedAt, scan.Score))
            .Reverse()
            .ToList());
    }

    private static async Task<IResult> RunScan(HttpContext context, IShopScanService scans)
    {
        var result = await scans.ScanAndStoreAsync(context.GetShop(), context.RequestAborted).ConfigureAwait(false);

        return result is null
            ? Results.NotFound()
            : Results.Json(ScanReport.From(result), ShopScanService.ReportJson);
    }
}

/// <summary>
/// Rejects any API request that does not carry a valid Shopify session token.
/// </summary>
/// <remarks>
/// Applied to the whole <c>/api</c> group so a new endpoint is protected by default. Adding a
/// route to that group is enough; forgetting an attribute cannot leave one open.
/// </remarks>
public sealed class SessionTokenFilter : IEndpointFilter
{
    /// <summary>Where the verified shop is stashed for the endpoint to read.</summary>
    internal const string ShopItemKey = "shop";

    private readonly ISessionTokenValidator _validator;

    public SessionTokenFilter(ISessionTokenValidator validator) => _validator = validator;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var header = context.HttpContext.Request.Headers.Authorization.ToString();

        const string scheme = "Bearer ";
        var token = header.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)
            ? header[scheme.Length..]
            : null;

        var shop = await _validator.ValidateAsync(token).ConfigureAwait(false);
        if (shop is null)
        {
            return Results.Unauthorized();
        }

        context.HttpContext.Items[ShopItemKey] = shop;
        return await next(context).ConfigureAwait(false);
    }
}

internal static class HttpContextShopExtensions
{
    /// <summary>
    /// The shop proven by the session token.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The endpoint was reached without <see cref="SessionTokenFilter"/> having run, which is a
    /// wiring bug rather than a bad request.
    /// </exception>
    public static ShopDomain GetShop(this HttpContext context) =>
        context.Items[SessionTokenFilter.ShopItemKey] as ShopDomain
        ?? throw new InvalidOperationException(
            "No verified shop on the request. Endpoints under /api must run behind SessionTokenFilter.");
}
