namespace AiVisibility.Core.Http;

/// <summary>
/// One fetched resource. A failed fetch is a result, not an exception — a store that
/// times out still needs a report.
/// </summary>
/// <param name="Url">The URL that was requested.</param>
/// <param name="StatusCode">HTTP status, or null when the request never completed.</param>
/// <param name="Body">Response body, empty when the request failed.</param>
/// <param name="ContentType">Media type without parameters, e.g. "text/html".</param>
/// <param name="Error">Transport-level failure message, null on success.</param>
public sealed record FetchResult(
    Uri Url,
    int? StatusCode,
    string Body,
    string? ContentType,
    string? Error)
{
    public bool IsSuccess => Error is null && StatusCode is >= 200 and < 300;

    public bool IsNotFound => StatusCode == 404;

    public static FetchResult Failed(Uri url, string error) => new(url, null, string.Empty, null, error);
}

/// <summary>
/// Fetches pages the way an AI crawler would. Abstracted so the scanner can be tested
/// against fixtures instead of live stores.
/// </summary>
public interface IPageFetcher
{
    Task<FetchResult> GetAsync(Uri url, CancellationToken cancellationToken = default);
}
