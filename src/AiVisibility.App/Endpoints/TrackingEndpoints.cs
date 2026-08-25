using AiVisibility.App.Scanning;

namespace AiVisibility.App.Endpoints;

/// <summary>Whether the dashboard should offer tracking at all.</summary>
public sealed record TrackingAvailability(bool Available);

/// <summary>
/// Visibility tracking routes.
/// </summary>
/// <remarks>
/// Running a track spends API credit, so <see cref="ActiveSubscriptionFilter"/> guards it.
/// Reading the last run does not, so it stays open to a lapsed shop — they already paid for
/// that report.
/// </remarks>
public static class TrackingEndpoints
{
    public static IEndpointRouteBuilder MapTrackingEndpoints(this IEndpointRouteBuilder app)
    {
        var tracking = app.MapGroup("/api/tracking").AddEndpointFilter<SessionTokenFilter>();

        tracking.MapGet("/availability", (IShopTrackingService service) =>
            Results.Ok(new TrackingAvailability(service.IsAvailable)));

        tracking.MapGet("/latest", GetLatest);

        tracking.MapPost("/", RunTracking).AddEndpointFilter<ActiveSubscriptionFilter>();

        return app;
    }

    private static async Task<IResult> GetLatest(HttpContext context, IShopTrackingService tracking)
    {
        var latest = await tracking.GetLatestAsync(context.GetShop(), context.RequestAborted).ConfigureAwait(false);

        // 204 is what the dashboard uses to decide whether to show its empty state.
        return latest is null
            ? Results.NoContent()
            : Results.Content(latest.ResultJson, "application/json");
    }

    private static async Task<IResult> RunTracking(HttpContext context, IShopTrackingService tracking)
    {
        var outcome = await tracking
            .RunAndStoreAsync(context.GetShop(), context.RequestAborted)
            .ConfigureAwait(false);

        if (outcome.Report is not null)
        {
            return Results.Json(outcome.Report, ShopScanService.ReportJson);
        }

        // Each failure is a different thing for the merchant to do about it, so each gets its
        // own status and message rather than one generic error.
        return outcome.Failure switch
        {
            TrackingFailure.NotConfigured => Results.Problem(
                "Visibility tracking is not switched on for this installation.",
                statusCode: StatusCodes.Status503ServiceUnavailable),

            TrackingFailure.NoCatalogue => Results.Problem(
                "Your storefront did not share a product list, so there are no shopper questions to ask. "
                + "This usually means the storefront is password protected or blocking automated requests.",
                statusCode: StatusCodes.Status422UnprocessableEntity),

            _ => Results.NotFound()
        };
    }
}
